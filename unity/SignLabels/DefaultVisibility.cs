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
    /// <para>Matching a sign is not enough to send the RPC, though: a freshly placed sign is a
    /// client-PREDICTED spawn — <c>GhostInstance.ghostId == 0</c> and
    /// <c>PredictedGhostSpawnRequest</c> present — and an RPC naming that entity cannot be resolved
    /// by the server. NetCode later promotes the SAME entity to the real ghost (no second spawn), so
    /// a matched sign is queued in <see cref="_pendingSends"/> and the RPC is sent from
    /// <see cref="ProcessPendingSends"/> once the entity's ghost id is confirmed. Found and diagnosed
    /// in Task 4's fix rounds 1-2 (2026-09-30): fix round 1 added the DIAG logging that showed the
    /// same entity being promoted rather than replaced; this is fix round 2.</para>
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
        // ghost before the RPC can name it. Entity/World are captured at match time, not read live off
        // Sign, so a pooled-and-reused component (mine + re-place) cannot make this entry silently
        // start tracking someone else's sign — see ProcessPendingSends.
        private readonly struct PendingSend
        {
            public readonly LabeledSign Sign;
            public readonly Entity Entity;
            public readonly World World;
            public readonly float PlacementAt;
            public readonly float ExpiresAt;
            public readonly int TileX;
            public readonly int TileZ;

            public PendingSend(LabeledSign sign, Entity entity, World world, float placementAt, float expiresAt, int tileX, int tileZ)
            {
                Sign = sign;
                Entity = entity;
                World = world;
                PlacementAt = placementAt;
                ExpiresAt = expiresAt;
                TileX = tileX;
                TileZ = tileZ;
            }
        }

        // How long to wait for the server to confirm a matched sign's ghost before giving up on it.
        private const float PendingSendTimeoutSeconds = 5f;

        private static readonly List<PendingSend> _pendingSends = new();

        // DIAG — temporary, added for Task 4 fix round 1 (default-visibility RPC not taking
        // effect). One entry per applied default; RunPendingChecks logs each once it is due, then
        // drops it. Remove this struct, the list, RunPendingChecks and its call site together once
        // the investigation is closed.
        private readonly struct PendingCheck
        {
            public readonly LabeledSign Sign;
            public readonly Entity EntityAtApply;
            public readonly World WorldAtApply;
            public readonly int TileX;
            public readonly int TileZ;
            public readonly float DueAt;

            public PendingCheck(LabeledSign sign, Entity entityAtApply, World worldAtApply, int tileX, int tileZ, float dueAt)
            {
                Sign = sign;
                EntityAtApply = entityAtApply;
                WorldAtApply = worldAtApply;
                TileX = tileX;
                TileZ = tileZ;
                DueAt = dueAt;
            }
        }

        // DIAG
        private const float DiagRecheckDelaySeconds = 1f;

        // DIAG
        private static readonly List<PendingCheck> _pendingChecks = new();

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
            RunPendingChecks(now); // DIAG

            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null)
                return;

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

        /// <summary>
        /// Sends the deferred RPC for each pending sign once its ghost is confirmed: real ghost id
        /// (not 0) and no more <see cref="PredictedGhostSpawnRequest"/>. Drops an entry silently when
        /// its sign or entity is no longer the one that was matched (despawned, or its pooled
        /// component reused for something else), or when its state/text changed under it (someone
        /// else already acted on it) — and drops it with a log line when the confirmation window
        /// (<see cref="PendingSendTimeoutSeconds"/>) runs out first.
        /// </summary>
        private static void ProcessPendingSends(float now)
        {
            for (int i = _pendingSends.Count - 1; i >= 0; i--)
            {
                var pending = _pendingSends[i];

                bool signAlive = pending.Sign != null;
                bool sameEntity = signAlive && pending.Sign.entity == pending.Entity;
                bool entityExists = pending.World != null && pending.World.EntityManager.Exists(pending.Entity);
                if (!signAlive || !sameEntity || !entityExists)
                {
                    _pendingSends.RemoveAt(i);
                    continue;
                }

                if (pending.Sign.GetState() != 1 || !string.IsNullOrEmpty(pending.Sign.GetName()))
                {
                    _pendingSends.RemoveAt(i);
                    continue;
                }

                bool hasGhost = pending.World.EntityManager.HasComponent<GhostInstance>(pending.Entity);
                int ghostId = hasGhost ? pending.World.EntityManager.GetComponentData<GhostInstance>(pending.Entity).ghostId : 0;
                bool hasPredictedSpawnRequest = pending.World.EntityManager.HasComponent<PredictedGhostSpawnRequest>(pending.Entity);
                bool confirmed = hasGhost && ghostId != 0 && !hasPredictedSpawnRequest;

                if (confirmed)
                {
                    var player = Manager.main != null ? Manager.main.player : null;
                    if (player == null)
                        continue; // retry next Tick rather than dropping — the timeout still applies.

                    player.playerCommandSystem.SetWorldLabelVisibility(pending.Entity, (int)_handle.Value);
                    float elapsedSeconds = now - pending.PlacementAt;
                    string elapsedText = elapsedSeconds.ToString("F2", CultureInfo.InvariantCulture);
                    Debug.Log($"[SignLabels] default {_handle.Value} applied at ({pending.TileX},{pending.TileZ}) after {elapsedText} s");

                    // DIAG — entity identity + ghost id at the moment of the RPC, plus a recheck ~1s
                    // later (see RunPendingChecks) to see whether the state actually stuck.
                    Debug.Log(
                        $"[SignLabels] DIAG applied entity={pending.Entity.Index}:{pending.Entity.Version} ghostId={ghostId} at ({pending.TileX},{pending.TileZ})"
                    );
                    _pendingChecks.Add(
                        new PendingCheck(pending.Sign, pending.Entity, pending.World, pending.TileX, pending.TileZ, now + DiagRecheckDelaySeconds)
                    );

                    _pendingSends.RemoveAt(i);
                    continue;
                }

                if (now >= pending.ExpiresAt)
                {
                    string timeoutText = PendingSendTimeoutSeconds.ToString("F0", CultureInfo.InvariantCulture);
                    Debug.Log($"[SignLabels] default not applied at ({pending.TileX},{pending.TileZ}): not confirmed by the server within {timeoutText} s");
                    _pendingSends.RemoveAt(i);
                }
                // else: still waiting on the server — leave it in the list for the next Tick.
            }
        }

        // DIAG — timestamp-based, not per-frame logging: this walks the (normally empty) pending
        // list every Tick, but only logs an entry once its DueAt has passed, then removes it.
        private static void RunPendingChecks(float now)
        {
            for (int i = _pendingChecks.Count - 1; i >= 0; i--)
            {
                var check = _pendingChecks[i];
                if (now < check.DueAt)
                    continue;
                _pendingChecks.RemoveAt(i);

                bool signAlive = check.Sign != null;
                bool sameEntity = signAlive && check.Sign.entity == check.EntityAtApply;
                bool entityExists = check.WorldAtApply != null && check.WorldAtApply.EntityManager.Exists(check.EntityAtApply);
                int stateNow = signAlive ? check.Sign.GetState() : -1;
                int ghostIdNow = -1;
                if (entityExists && check.WorldAtApply.EntityManager.HasComponent<GhostInstance>(check.EntityAtApply))
                    ghostIdNow = check.WorldAtApply.EntityManager.GetComponentData<GhostInstance>(check.EntityAtApply).ghostId;

                Debug.Log(
                    $"[SignLabels] DIAG recheck at ({check.TileX},{check.TileZ}) entity={check.EntityAtApply.Index}:{check.EntityAtApply.Version} "
                        + $"signAlive={signAlive} sameEntity={sameEntity} entityExists={entityExists} stateNow={stateNow} ghostId={ghostIdNow}"
                );
            }
        }

        private static void OnSignSpawned(LabeledSign sign)
        {
            // Cheapest checks first: a handle that already means Hover needs no RPC, since Hover is
            // the state every sign spawns in.
            if (_handle == null || _handle.Value == Visibility.Hover)
                return;
            if (sign.GetState() != 1)
                return;
            if (!string.IsNullOrEmpty(sign.GetName()))
                return;
            if (!_matcher.TryConsume(sign.TileX, sign.TileZ, _lastNow, out float elapsedSeconds))
                return;

            // elapsedSeconds is (match time - placement time); reconstructing the placement time here
            // lets the eventual "applied … after N s" line measure from the placement to the SEND,
            // not merely to this match, since the two can now be seconds apart.
            float placementAt = _lastNow - elapsedSeconds;
            _pendingSends.Add(new PendingSend(sign, sign.entity, sign.world, placementAt, _lastNow + PendingSendTimeoutSeconds, sign.TileX, sign.TileZ));
        }
    }
}
