using System;
using System.Collections.Generic;

namespace SignLabels
{
    /// <summary>
    /// One object the mod gives a label: its id, and the vanilla root component expected on its
    /// graphical prefab. The root check is what keeps a game update that renames or replaces the
    /// class from being papered over: the edit refuses and the sign stays vanilla.
    /// </summary>
    public readonly struct SignLabelTarget
    {
        public readonly ObjectID Id;

        /// <summary>The expected root class's name — for log lines only, never for type checks.</summary>
        public readonly string RootName;

        public readonly Func<EntityMonoBehaviour, bool> IsExpectedRoot;

        public SignLabelTarget(ObjectID id, string rootName, Func<EntityMonoBehaviour, bool> isExpectedRoot)
        {
            Id = id;
            RootName = rootName;
            IsExpectedRoot = isExpectedRoot;
        }
    }

    /// <summary>
    /// The target list, as data: every sign without a vanilla label. Seven ids on five graphical
    /// prefabs — the two warning signs share one, and so do the two wooden signs. The predicates
    /// are <c>is</c> patterns because the load-time sandbox rejects reflection-based type checks.
    /// </summary>
    public static class SignLabelTargets
    {
        private static readonly SignLabelTarget[] Targets =
        {
            new(ObjectID.SignArrow, "SignArrow", e => e is SignArrow),
            new(ObjectID.SignSkull, "SignPostSkull", e => e is SignPostSkull),
            new(ObjectID.SignBrute, "SignPostBrute", e => e is SignPostBrute),
            new(ObjectID.SignYellowWarning, "YellowWarningSign", e => e is YellowWarningSign),
            new(ObjectID.SignRedWarning, "YellowWarningSign", e => e is YellowWarningSign),
            new(ObjectID.WoodenSign1, "WoodenSign", e => e is WoodenSign),
            new(ObjectID.WoodenSign2, "WoodenSign", e => e is WoodenSign),
        };

        // A read-only wrapper, not the array itself: an array handed out as IReadOnlyList can be
        // cast back to IList and written to.
        private static readonly IReadOnlyList<SignLabelTarget> ReadOnlyTargets = Array.AsReadOnly(Targets);

        public static IReadOnlyList<SignLabelTarget> All => ReadOnlyTargets;

        public static bool TryGet(ObjectID id, out SignLabelTarget target)
        {
            foreach (var t in Targets)
            {
                if (t.Id == id)
                {
                    target = t;
                    return true;
                }
            }
            target = default;
            return false;
        }
    }
}
