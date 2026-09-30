using System;
using System.Collections.Generic;
using Interaction;
using Pug.Conversion;
using UnityEngine;

namespace SignLabels
{
    /// <summary>
    /// Gives every target sign's entity what the text sign's authoring gives it: a
    /// <see cref="DescriptionBuffer"/> for the text, <see cref="AlwaysDropOneCD"/> so a sign set to
    /// Always (amount 2) still drops one item, and — only after a verified prefab edit — the
    /// interaction triggers. The game's InteractablePostConverter reads the graphical prefab's first
    /// InteractableObject without checking one exists, so trigger buffers on an unedited prefab
    /// throw at bake time and ECS initialisation fails.
    /// </summary>
    public class SignLabelConverter : Converter
    {
        // Runs once per world (twice in a hosting client); each message is logged once.
        private static readonly HashSet<string> Logged = new();

        public override void Convert(GameObject authoring)
        {
            if (!authoring.TryGetComponent<IEntityMonoBehaviourData>(out var data))
                return;
            var info = data.ObjectInfo;
            if (info == null || !SignLabelTargets.TryGet(info.objectID, out var target))
                return;

            // Unconditional, and before anything that can fail: every side gets the same ghost
            // layout, and no combination of success and failure can duplicate items.
            EnsureHasComponent<AlwaysDropOneCD>();
            EnsureHasBuffer<DescriptionBuffer>();

            // Everything below can throw on a future game update (a resolved-null prefab, a
            // renamed/removed field JsonUtility no longer finds, ...). Nothing here has thrown on
            // any build tested, but the promise this converter keeps is "the game must load", so a
            // throw is caught rather than left to fail ECS initialisation: log once, add no trigger
            // components, and this sign stays vanilla like any other verified edit failure.
            try
            {
                var graphical = info.prefabInfo?.GetGraphical();
                if (!GraphicalPrefabEditor.EnsureEdited(graphical, target, out var failure))
                {
                    LogOnce(false, $"[SignLabels] failed to edit {target.RootName} prefab ({failure}); ids {IdsOn(graphical, target)} stay vanilla");
                    return;
                }

                EnsureHasBuffer<TriggerUseInteractionBuffer>();
                EnsureHasComponent<LocalUseInteractionTriggerCD>(false);
                AddComponentData(new LocalUseInteractionTriggerSubIndexCD { subIndex = 0 });
                EnsureHasBuffer<TriggerExitInteractionBuffer>();
                EnsureHasComponent<LocalExitInteractionTriggerCD>(false);

                LogOnce(true, $"[SignLabels] edited {target.RootName} prefab for {IdsOn(graphical, target)}");
            }
            catch (Exception ex)
            {
                LogOnce(false, $"[SignLabels] {target.RootName} conversion threw ({ex}); {Describe(target.Id)} stays vanilla");
            }
        }

        private static void LogOnce(bool success, string message)
        {
            if (!Logged.Add(message))
                return;
            if (success)
                Debug.Log(message);
            else
                Debug.LogError(message);
        }

        /// <summary>
        /// Every target id whose graphical prefab is <paramref name="graphical"/> — the ids the edit
        /// (or its failure) applies to, including those not converted yet.
        /// </summary>
        private static string IdsOn(GameObject graphical, SignLabelTarget target)
        {
            var ids = new List<string>();
            if (graphical != null)
            {
                foreach (var t in SignLabelTargets.All)
                {
                    if (PugDatabase.GetObjectInfo(t.Id)?.prefabInfo?.GetGraphical() == graphical)
                        ids.Add(Describe(t.Id));
                }
            }
            if (ids.Count == 0)
                ids.Add(Describe(target.Id));
            return string.Join(", ", ids);
        }

        private static string Describe(ObjectID id) => $"{id} ({(int)id})";
    }
}
