using ModSettingsMenu.Settings;
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
    /// consume, so the RPC fires only for the local player's own fresh placement, once.
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

            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null)
                return;

            player.playerCommandSystem.SetWorldLabelVisibility(sign.entity, (int)_handle.Value);
            Debug.Log($"[SignLabels] default {_handle.Value} applied at ({sign.TileX},{sign.TileZ}) after {elapsedSeconds:F2} s");
        }
    }
}
