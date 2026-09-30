using System.Collections.Generic;

namespace SignLabels
{
    /// <summary>
    /// Decides whether a sign that just spawned is the local player's own fresh placement, by
    /// matching the local player's replicated <c>PlacementCD</c> start tick against the sign
    /// entity's spawn tile within a short time window. Pure logic — no Unity or game types — so it
    /// can be exercised offline by tests/placement-harness as well as compiled into the mod.
    ///
    /// The caller observes the placement via <see cref="Observe"/> (start tick + tile), read once
    /// per frame off <c>PlacementCD</c> rather than from any RPC, then, once the corresponding sign
    /// entity appears, calls <see cref="TryConsume"/> with that tile to find out whether it was the
    /// just-placed one. A placement is consumable exactly once and expires after
    /// <see cref="WindowSeconds"/>.
    /// </summary>
    public sealed class PlacementMatcher
    {
        // 3 s — about 20x the measured placement-to-spawn gap (0.12-0.18 s singleplayer,
        // 0.31-0.35 s over a same-machine dedicated server), so the window never matters in
        // practice; see docs/manual-tests.md.
        public const float WindowSeconds = 3f;

        private readonly struct Pending
        {
            public readonly int TileX;
            public readonly int TileZ;
            public readonly float RecordedAt;

            public Pending(int tileX, int tileZ, float recordedAt)
            {
                TileX = tileX;
                TileZ = tileZ;
                RecordedAt = recordedAt;
            }
        }

        private readonly float _windowSeconds;
        private readonly List<Pending> _pending = new();

        // 0 = "no placement yet" — startTick 0 is never a real placement (see Observe).
        private uint _lastStartTick;

        public PlacementMatcher(float windowSeconds = WindowSeconds)
        {
            _windowSeconds = windowSeconds;
        }

        /// <summary>
        /// Records a placement's start tick and target tile. Returns true when this is a NEW
        /// placement — i.e. startTick differs from the last one observed and is not 0 ("no
        /// placement yet"). The caller reads <c>PlacementCD</c> every frame, so most calls repeat
        /// the previous one's start tick; those do not record a second pending entry.
        /// </summary>
        public bool Observe(uint startTick, int tileX, int tileZ, float now)
        {
            if (startTick == 0 || startTick == _lastStartTick)
                return false;

            _lastStartTick = startTick;
            DropExpired(now);
            _pending.Add(new Pending(tileX, tileZ, now));
            return true;
        }

        /// <summary>
        /// Consumes the pending placement recorded for (tileX, tileZ), if any is still within the
        /// window. Returns true exactly once per recorded placement on that tile — a second call
        /// for the same placement, or a call after the window has elapsed, returns false.
        /// </summary>
        public bool TryConsume(int tileX, int tileZ, float now)
        {
            return TryConsume(tileX, tileZ, now, out _);
        }

        /// <summary>
        /// Same as <see cref="TryConsume(int, int, float)"/>, plus how many seconds elapsed between
        /// <see cref="Observe"/> recording the placement and this call consuming it — 0 when nothing
        /// was consumed. The mod uses it to reconstruct the placement time that its
        /// "default … sent … after N s" log line measures from.
        /// </summary>
        public bool TryConsume(int tileX, int tileZ, float now, out float elapsedSeconds)
        {
            DropExpired(now);

            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (p.TileX == tileX && p.TileZ == tileZ)
                {
                    elapsedSeconds = now - p.RecordedAt;
                    _pending.RemoveAt(i);
                    return true;
                }
            }

            elapsedSeconds = 0f;
            return false;
        }

        private void DropExpired(float now)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (now - _pending[i].RecordedAt > _windowSeconds)
                    _pending.RemoveAt(i);
            }
        }
    }
}
