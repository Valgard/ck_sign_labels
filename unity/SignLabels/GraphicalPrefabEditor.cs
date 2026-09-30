using System.Collections.Generic;
using Pug.Sprite;
using UnityEngine;
using UnityEngine.Events;

namespace SignLabels
{
    /// <summary>
    /// Turns a target's graphical prefab into a labelled sign, once per prefab. The prefab is a
    /// loaded asset: it accepts added and destroyed components but no new children (a parented
    /// Instantiate is refused as "persistent"), so only components change here — the vanilla root
    /// is swapped for <see cref="LabeledSign"/>, and an <see cref="InteractableObject"/> copied from
    /// the text sign's goes onto the root. Everything that needs its own hierarchy is built per
    /// instance in <see cref="LabeledSign"/>'s Awake.
    ///
    /// Keyed by the prefab, not by <see cref="ObjectID"/>: two pairs of targets share one prefab,
    /// and the converter runs once per world, so the second and later calls return the cached result.
    /// </summary>
    public static class GraphicalPrefabEditor
    {
        // Prefab instance id -> failure text; null means the edit succeeded.
        private static readonly Dictionary<int, string> Results = new();

        /// <summary>SignText's "WorldText" child, cloned under every <see cref="LabeledSign"/> instance.</summary>
        public static GameObject WorldTextSource { get; private set; }

        /// <summary>
        /// Edits <paramref name="graphical"/> once; later calls return the cached result for that
        /// prefab. Every precondition is checked before the prefab is touched, so a failure caught
        /// there leaves it exactly as the game built it. <paramref name="failure"/> is null on success.
        /// </summary>
        public static bool EnsureEdited(GameObject graphical, SignLabelTarget target, out string failure)
        {
            if (graphical == null)
            {
                failure = "the object has no graphical prefab";
                return false;
            }

            var key = graphical.GetInstanceID();
            if (!Results.TryGetValue(key, out failure))
            {
                failure = Edit(graphical, target);
                Results[key] = failure;
            }
            return failure == null;
        }

        private static string Edit(GameObject graphical, SignLabelTarget target)
        {
            var textSign = PugDatabase.GetObjectInfo(ObjectID.SignText)?.prefabInfo?.GetGraphical();
            if (textSign == null)
                return "the text sign's graphical prefab is not available";

            var sourceInteractable = textSign.transform.Find("Interactable")?.GetComponent<InteractableObject>();
            if (sourceInteractable == null)
                return "the text sign has no Interactable child with an InteractableObject";

            var worldText = textSign.transform.Find("WorldText");
            if (worldText == null || worldText.GetComponent<ObjectNameTag>() == null)
                return "the text sign has no WorldText child with an ObjectNameTag";

            var oldRoot = graphical.GetComponent<EntityMonoBehaviour>();
            if (oldRoot == null)
                return "the root has no EntityMonoBehaviour";
            if (!target.IsExpectedRoot(oldRoot))
                return $"the root is {oldRoot}, expected {target.RootName}";

            if (graphical.GetComponentInChildren<InteractableObject>(true) != null)
                return "the prefab already has an InteractableObject";

            var sprite = graphical.GetComponentInChildren<SpriteObject>(true);
            if (sprite == null)
                return "the prefab has no SpriteObject to outline";

            WorldTextSource = worldText.gameObject;

            try
            {
                SwapRoot(graphical, oldRoot);
                AddInteractable(graphical, sourceInteractable, sprite);
            }
            catch (System.Exception e)
            {
                return $"the edit threw {e}";
            }

            if (graphical.GetComponents<EntityMonoBehaviour>().Length != 1 || !(graphical.GetComponent<EntityMonoBehaviour>() is LabeledSign))
                return "the edit did not leave exactly one LabeledSign and one InteractableObject on the root";
            if (graphical.GetComponents<InteractableObject>().Length != 1)
                return "the edit did not leave exactly one LabeledSign and one InteractableObject on the root";
            return null;
        }

        private static void SwapRoot(GameObject graphical, EntityMonoBehaviour oldRoot)
        {
            // LabeledSign derives from EntityMonoBehaviour through WorldLabel, so the vanilla root's
            // serialized fields (sprites, shadow, paint options, ...) carry over by name.
            var json = JsonUtility.ToJson(oldRoot);
            var labeledSign = graphical.AddComponent<LabeledSign>();
            JsonUtility.FromJsonOverwrite(json, labeledSign);
            Object.DestroyImmediate(oldRoot, true);
            // `interactable` stays null on the asset: pointed at the root, OnSpawn would rotate the
            // whole sign by its direction. Each instance sets it to its own child in Awake.
            labeledSign.interactable = null;
        }

        private static void AddInteractable(GameObject graphical, InteractableObject source, SpriteObject sprite)
        {
            var interactable = graphical.AddComponent<InteractableObject>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), interactable);

            // The copied references still point into the text sign's prefab: its events' persistent
            // calls target the text sign's component, and its outline and icon are its objects.
            // Listeners are added per instance in LabeledSign.Awake.
            interactable.onUseActions = new List<UnityEvent> { new UnityEvent() };
            interactable.onTriggerExitActions = new List<UnityEvent> { new UnityEvent() };
            interactable.optionalOutlineController = null;
            interactable.optionalIcon = null;
            for (var i = 0; i < interactable.subInteractingData.Count; i++)
            {
                // A struct in a list: copy, modify, write back.
                var data = interactable.subInteractingData[i];
                data.optionalSpriteObjectOutlines = new List<SpriteObject> { sprite };
                data.optionalOutlineControllers = new List<OutlineController>();
                interactable.subInteractingData[i] = data;
            }
        }
    }
}
