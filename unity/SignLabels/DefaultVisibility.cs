using System;
using System.Collections.Generic;
using System.Globalization;
using ModSettingsMenu.Settings;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace SignLabels
{
    /// <summary>
    /// The World Label visibility states a sign can be in — off (never shown), hover (shown while
    /// targeted, the vanilla default) and always. The int values are the game's own "amount" states
    /// (<see cref="WorldLabel.GetState"/> / the RPC index <c>SetWorldLabelVisibility</c> takes), not
    /// an identifier this mod invents.
    /// </summary>
    public enum Visibility
    {
        Off = 0,
        Hover = 1,
        Always = 2,
    }

    /// <summary>
    /// Applies the player's configured default visibility to a sign they just placed, through the
    /// game's own <c>SetWorldLabelVisibility</c> RPC — see "Default visibility at placement" in the
    /// design spec for why this reads replicated state instead of patching the placement path.
    ///
    /// <see cref="Tick"/> (called every frame from <see cref="SignLabelsMod.Update"/>) reads the
    /// local player's <c>PlacementCD</c> and feeds a placement observation to a
    /// <see cref="PlacementMatcher"/>; <see cref="LabeledSign.AnySpawned"/> feeds the matching
    /// consume, so a sign is picked out as the local player's own fresh placement, once.
    ///
    /// <para>Matching a sign is not enough to send the RPC: a freshly placed sign is a
    /// client-predicted spawn — <c>GhostInstance.ghostId == 0</c> and
    /// <c>PredictedGhostSpawnRequest</c> present — and an RPC naming that entity cannot be resolved
    /// by the server. NetCode later promotes the SAME entity to the real ghost rather than spawning a
    /// second one, so a matched sign is queued in <see cref="_pendingSends"/> and the RPC is sent from
    /// <see cref="ProcessPendingSends"/> once the entity's ghost id is confirmed.</para>
    /// </summary>
    public static class DefaultVisibility
    {
        private static readonly PlacementMatcher _matcher = new();

        private static SettingHandle<Visibility> _handle;
        private static bool _subscribed;

        // The most recent time Tick observed, in Time.time seconds. AnySpawned fires from the game's
        // own spawn path, not from Tick, so this is the closest "now" available when a sign spawns —
        // close enough given the several-second matching window.
        private static float _lastNow;

        // A sign matched to the local player's own placement, waiting for the server to confirm its
        // ghost before the RPC can name it. Entity/World/Value are captured at match time, not read
        // live off Sign or the handle later: a pooled-and-reused component (mine + re-place) cannot
        // make this entry silently start tracking someone else's sign, and a mid-wait option change
        // cannot retroactively change what this placement gets — see ProcessPendingSends.
        private readonly struct PendingSend
        {
            public readonly LabeledSign Sign;
            public readonly Entity Entity;
            public readonly World World;
            public readonly Visibility Value;
            public readonly float PlacementAt;
            public readonly float ExpiresAt;
            public readonly int TileX;
            public readonly int TileZ;

            public PendingSend(LabeledSign sign, Entity entity, World world, Visibility value, float placementAt, float expiresAt, int tileX, int tileZ)
            {
                Sign = sign;
                Entity = entity;
                World = world;
                Value = value;
                PlacementAt = placementAt;
                ExpiresAt = expiresAt;
                TileX = tileX;
                TileZ = tileZ;
            }
        }

        // How long to wait for the server to confirm a matched sign's ghost before giving up on it.
        private const float PendingSendTimeoutSeconds = 5f;

        private static readonly List<PendingSend> _pendingSends = new();

        // A sign whose RPC has been sent, waiting for the new state to come back from the server.
        // The game's sign window reads the state once, when it opens, so a window opened in the gap
        // between the send and the server's answer shows Hover until it is reopened — see
        // ProcessAwaitingEcho.
        private readonly struct AwaitingEcho
        {
            public readonly LabeledSign Sign;
            public readonly Entity Entity;
            public readonly World World;
            public readonly Visibility Value;
            public readonly float ExpiresAt;

            public AwaitingEcho(LabeledSign sign, Entity entity, World world, Visibility value, float expiresAt)
            {
                Sign = sign;
                Entity = entity;
                World = world;
                Value = value;
                ExpiresAt = expiresAt;
            }
        }

        // How long to wait for the sent state to arrive back before forgetting the sign. The send
        // has already happened; this only bounds how long an open window is watched.
        private const float EchoTimeoutSeconds = 2f;

        private static readonly List<AwaitingEcho> _awaitingEcho = new();

        /// <summary>Called once from <see cref="SignLabelsMod.Init"/>, after the Choice is built.</summary>
        public static void Bind(SettingHandle<Visibility> handle)
        {
            if (handle == null)
            {
                Debug.LogError("[SignLabels] DefaultVisibility.Bind received a null handle; defaultVisibility stays at Hover and ignores the menu.");
                return;
            }
            if (_handle != null)
                Debug.LogWarning("[SignLabels] DefaultVisibility.Bind called more than once — the later handle wins.");

            _handle = handle;

            if (!_subscribed)
            {
                _subscribed = true;
                LabeledSign.AnySpawned += OnSignSpawned;
            }
        }

        /// <summary>Called every frame from <see cref="SignLabelsMod.Update"/>. Safe with no player or
        /// world — the main menu and the load screens both run through here.</summary>
        public static void Tick(float now)
        {
            _lastNow = now;

            ProcessPendingSends(now);
            ProcessAwaitingEcho(now);

            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null)
            {
                // The local player is gone (main menu, between worlds). Nothing pending can still be
                // waiting on a world or entity that belongs to this session, so drop it rather than
                // let a stale World/Entity pair sit across a session boundary.
                ClearPending();
                return;
            }

            if (!EntityUtility.TryGetComponentData<PlacementCD>(player.entity, player.world, out var placement))
                return;

            // startTick 0 means "no placement yet" to PlacementMatcher.Observe as well, so an invalid
            // tick collapses to the same sentinel rather than needing its own branch.
            uint startTick = placement.timeSincePlaced.startTick.IsValid ? placement.timeSincePlaced.startTick.SerializedData : 0;
            int tileX = placement.positionLastPlacedAt.x;
            int tileZ = placement.positionLastPlacedAt.z;

            if (_matcher.Observe(startTick, tileX, tileZ, now))
                Debug.Log($"[SignLabels] placement at ({tileX},{tileZ})");
        }

        private static void ClearPending()
        {
            if (_pendingSends.Count > 0)
                _pendingSends.Clear();
            if (_awaitingEcho.Count > 0)
                _awaitingEcho.Clear();
        }

        /// <summary>
        /// Refreshes the game's sign window once the sent state has arrived back from the server.
        /// A window opened after the send but before the answer read Hover in
        /// <c>SignTextUI.ShowUI</c> and never reads again; when the sign's state now equals the sent
        /// value and that window is still open on this sign with its toggle untouched (still Hover),
        /// the toggle is set to the sent value. Each entry is dropped once the state has arrived,
        /// when its sign, entity or world is gone, or silently after
        /// <see cref="EchoTimeoutSeconds"/>.
        /// </summary>
        private static void ProcessAwaitingEcho(float now)
        {
            for (int i = _awaitingEcho.Count - 1; i >= 0; i--)
            {
                var awaiting = _awaitingEcho[i];
                try
                {
                    // Same ordering as ProcessPendingSends: GetState() goes through EntityManager, so
                    // every guard has to hold before it runs.
                    bool signAlive = awaiting.Sign != null;
                    bool sameEntity = signAlive && awaiting.Sign.entity == awaiting.Entity;
                    bool worldUsable = awaiting.World != null && awaiting.World.IsCreated;
                    bool entityExists = signAlive && sameEntity && worldUsable && awaiting.World.EntityManager.Exists(awaiting.Entity);
                    if (!entityExists)
                    {
                        _awaitingEcho.RemoveAt(i);
                        continue;
                    }

                    if (awaiting.Sign.GetState() == (int)awaiting.Value)
                    {
                        var player = Manager.main != null ? Manager.main.player : null;
                        SignTextUI window = player != null ? OpenWindowFor(player, awaiting.Sign) : null;
                        // Not SignTextUI.SetVisibilityState(): that would send the RPC again.
                        if (window != null && window.signStateToggle.stateIndex == (int)Visibility.Hover)
                            window.signStateToggle.SetState((int)awaiting.Value);
                        _awaitingEcho.RemoveAt(i);
                        continue;
                    }

                    if (now >= awaiting.ExpiresAt)
                        _awaitingEcho.RemoveAt(i);
                }
                catch (Exception ex)
                {
                    // A throw here would otherwise repeat every frame until the sign or its entity
                    // goes away on its own — drop the entry now so the per-frame path fails once.
                    Debug.LogError($"[SignLabels] echo watch threw ({ex}); dropped a pending window refresh");
                    _awaitingEcho.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Sends the deferred RPC for each pending sign once its ghost id is confirmed real (not 0).
        /// <see cref="PredictedGhostSpawnRequest"/> is also checked, but per the handbook's
        /// multiplayer chapter NetCode removes that component one simulation step after spawn
        /// regardless of confirmation — it is no marker to wait on — so by the time the ghost id is
        /// real the component is already gone; the check is redundant defence, not the actual gate.
        /// Drops an entry silently when its sign is gone, its entity is no longer the one that was
        /// matched (despawned, or its pooled component reused for something else), its world is no
        /// longer created, or its state is no longer the spawn default (someone already chose one) —
        /// and drops it with a log line when the confirmation window
        /// (<see cref="PendingSendTimeoutSeconds"/>) runs out first. Text on the sign does not
        /// matter: typing a label is not a visibility choice.
        ///
        /// <para>The game's sign window reads the state once, when it opens
        /// (<c>SignTextUI.ShowUI</c>), so a window opened on this sign before the send still shows
        /// Hover. When the send is due and that window is open on this sign, its toggle decides: a
        /// toggle the player already moved off Hover means they chose, and nothing is sent;
        /// otherwise the default is sent and the toggle is set to match it. A window opened after the
        /// send but before the server's answer is refreshed by <see cref="ProcessAwaitingEcho"/>.</para>
        /// </summary>
        private static void ProcessPendingSends(float now)
        {
            for (int i = _pendingSends.Count - 1; i >= 0; i--)
            {
                var pending = _pendingSends[i];
                try
                {
                    // Every condition here has to hold before an EntityManager call is safe: a
                    // destroyed sign, a reused pooled component, or a world that is no longer created
                    // (the session ended while this entry was waiting) must all stop the lookup before
                    // it runs, not only after — touching EntityManager on a disposed World is
                    // undefined behaviour with safety checks off, and throws every frame with them on.
                    bool signAlive = pending.Sign != null;
                    bool sameEntity = signAlive && pending.Sign.entity == pending.Entity;
                    bool worldUsable = pending.World != null && pending.World.IsCreated;
                    bool entityExists = signAlive && sameEntity && worldUsable && pending.World.EntityManager.Exists(pending.Entity);
                    if (!entityExists || pending.Sign.GetState() != (int)Visibility.Hover)
                    {
                        _pendingSends.RemoveAt(i);
                        continue;
                    }

                    bool hasGhost = pending.World.EntityManager.HasComponent<GhostInstance>(pending.Entity);
                    int ghostId = hasGhost ? pending.World.EntityManager.GetComponentData<GhostInstance>(pending.Entity).ghostId : 0;
                    // Kept as defence, not as the real gate: NetCode removes this component one
                    // step after spawn regardless of confirmation, so it is normally already gone
                    // by the time ghostId is real.
                    bool hasPredictedSpawnRequest = pending.World.EntityManager.HasComponent<PredictedGhostSpawnRequest>(pending.Entity);
                    bool confirmed = hasGhost && ghostId != 0 && !hasPredictedSpawnRequest;

                    if (confirmed)
                    {
                        var player = Manager.main != null ? Manager.main.player : null;
                        if (player != null)
                        {
                            SignTextUI window = OpenWindowFor(player, pending.Sign);
                            if (window != null && window.signStateToggle.stateIndex != (int)Visibility.Hover)
                            {
                                Debug.Log($"[SignLabels] default skipped at ({pending.TileX},{pending.TileZ}): set in the sign window");
                                _pendingSends.RemoveAt(i);
                                continue;
                            }

                            player.playerCommandSystem.SetWorldLabelVisibility(pending.Entity, (int)pending.Value);
                            // Not SignTextUI.SetVisibilityState(): that sends the RPC a second time.
                            if (window != null)
                                window.signStateToggle.SetState((int)pending.Value);
                            // A window opened after this point still reads the old state; the echo
                            // watch refreshes it once the new one arrives.
                            _awaitingEcho.Add(new AwaitingEcho(pending.Sign, pending.Entity, pending.World, pending.Value, now + EchoTimeoutSeconds));

                            float elapsedSeconds = now - pending.PlacementAt;
                            string elapsedText = elapsedSeconds.ToString("F2", CultureInfo.InvariantCulture);
                            Debug.Log($"[SignLabels] default {pending.Value} applied at ({pending.TileX},{pending.TileZ}) after {elapsedText} s");
                            _pendingSends.RemoveAt(i);
                            continue;
                        }
                        // else: no player right now (should not normally happen while the sign itself
                        // still exists) — fall through to the timeout check below instead of retrying
                        // forever.
                    }

                    if (now >= pending.ExpiresAt)
                    {
                        string timeoutText = PendingSendTimeoutSeconds.ToString("F0", CultureInfo.InvariantCulture);
                        Debug.Log($"[SignLabels] default not applied at ({pending.TileX},{pending.TileZ}): not confirmed by the server within {timeoutText} s");
                        _pendingSends.RemoveAt(i);
                    }
                    // else: still waiting on the server — leave it in the list for the next Tick.
                }
                catch (Exception ex)
                {
                    // A throw here would otherwise repeat every frame until the sign or its entity
                    // goes away on its own — drop the entry now so the per-frame path fails once.
                    Debug.LogError($"[SignLabels] default send at ({pending.TileX},{pending.TileZ}) threw ({ex}); dropped");
                    _pendingSends.RemoveAt(i);
                }
            }
        }

        /// <summary>The game's sign window if it is showing and open on <paramref name="sign"/>;
        /// null otherwise.</summary>
        private static SignTextUI OpenWindowFor(PlayerController player, LabeledSign sign)
        {
            var window = Manager.ui != null ? Manager.ui.signUI : null;
            if (window == null || window.signStateToggle == null || !window.isShowing)
                return null;
            return player.activeWorldLabel == sign ? window : null;
        }

        private static void OnSignSpawned(LabeledSign sign)
        {
            // Cheapest checks first: a handle that already means Hover needs no RPC, since Hover is
            // the state every sign spawns in.
            if (_handle == null || _handle.Value == Visibility.Hover)
                return;
            if (sign.GetState() != (int)Visibility.Hover)
                return;
            if (!_matcher.TryConsume(sign.TileX, sign.TileZ, _lastNow, out float elapsedSeconds))
                return;

            // Read the option now, at match time — not later at send time, when it may have changed.
            Visibility value = _handle.Value;

            // elapsedSeconds is (match time - placement time); reconstructing the placement time here
            // lets the eventual "applied … after N s" line measure from the placement to the SEND,
            // not merely to this match, since the two can now be seconds apart.
            float placementAt = _lastNow - elapsedSeconds;
            _pendingSends.Add(new PendingSend(sign, sign.entity, sign.world, value, placementAt, _lastNow + PendingSendTimeoutSeconds, sign.TileX, sign.TileZ));
        }
    }
}
