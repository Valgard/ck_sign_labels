using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;

namespace SignLabels
{
    /// <summary>
    /// Root component of every edited sign prefab, replacing the vanilla one. A
    /// <see cref="WorldLabel"/>, so the game's sign window accepts it; <see cref="Interact"/> and
    /// <see cref="OnPlayerLeft"/> mirror <c>SignText</c>'s.
    /// </summary>
    public class LabeledSign : WorldLabel
    {
        private static bool _loggedMissingInteractable;
        private static bool _loggedMissingWorldText;
        private static bool _loggedHandlerException;
        private static bool _loggedAwakeException;

        /// <summary>Raised at the end of every spawn, after <see cref="TileX"/> and <see cref="TileZ"/> are set.</summary>
        public static event System.Action<LabeledSign> AnySpawned;

        public int TileX { get; private set; }

        /// <summary>The world z tile — the axis <c>PlacementCD</c> calls z.</summary>
        public int TileZ { get; private set; }

        protected override void Awake()
        {
            // Before base.Awake(), which caches `interactable`. The prefab carries the
            // InteractableObject on its root because baking needs one there and the asset accepts
            // no children. At runtime it has to sit on a child, as on the text sign: spawning copies
            // `interactable` into InteractableObjectReferenceCD (left null, interaction never
            // fires), and OnSpawn rotates `interactable.transform` by the sign's direction (on the
            // root, that turns the whole sign edge-on).
            //
            // Each step of our own is guarded separately so that base.Awake() runs no matter what
            // they do: skipping it would leave the game's own initialisation of this pooled instance
            // undone, which is worse than a sign without interaction or label. A failed move clears
            // `interactable` before base.Awake() caches it, so a half-finished move can never leave it
            // pointing at the root (rule 2 in CLAUDE.md) — null costs only the interaction. Throws
            // are logged once per session, as the null branches below are: Awake runs for every
            // pooled instance.
            try
            {
                MoveInteractableToChild();
            }
            catch (System.Exception e)
            {
                interactable = null;
                LogAwakeFailureOnce("moving its InteractableObject to a child", "it cannot be interacted with", e);
            }

            base.Awake();

            try
            {
                AttachWorldText();
            }
            catch (System.Exception e)
            {
                LogAwakeFailureOnce("attaching its text object", "its label cannot show", e);
            }

            try
            {
                if (interactable != null && interactable.onUseActions.Count > 0 && interactable.onTriggerExitActions.Count > 0)
                {
                    interactable.onUseActions[0].AddListener(Interact);
                    interactable.onTriggerExitActions[0].AddListener(OnPlayerLeft);
                }
            }
            catch (System.Exception e)
            {
                LogAwakeFailureOnce("wiring its interaction", "it cannot be interacted with", e);
            }
        }

        private void LogAwakeFailureOnce(string step, string consequence, System.Exception e)
        {
            if (_loggedAwakeException)
                return;
            _loggedAwakeException = true;
            Debug.LogError($"[SignLabels] LabeledSign {name} threw while {step} ({e}); {consequence} (logged once per session)");
        }

        private void MoveInteractableToChild()
        {
            var rootInteractable = GetComponent<InteractableObject>();
            if (rootInteractable == null)
            {
                if (!_loggedMissingInteractable)
                {
                    _loggedMissingInteractable = true;
                    Debug.LogError($"[SignLabels] LabeledSign {name} has no InteractableObject to move; it cannot be interacted with");
                }
                return;
            }

            var child = new GameObject("Interactable");
            child.transform.SetParent(transform, false);
            var childInteractable = child.AddComponent<InteractableObject>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(rootInteractable), childInteractable);
            childInteractable.onUseActions = new List<UnityEvent> { new UnityEvent() };
            childInteractable.onTriggerExitActions = new List<UnityEvent> { new UnityEvent() };
            DestroyImmediate(rootInteractable);
            interactable = childInteractable;
        }

        private void AttachWorldText()
        {
            var source = GraphicalPrefabEditor.WorldTextSource;
            if (source == null)
            {
                LogMissingWorldText();
                return;
            }

            var worldText = Instantiate(source, transform, false);
            worldText.name = "WorldText";
            var nameTag = worldText.GetComponent<ObjectNameTag>();
            if (nameTag == null)
            {
                LogMissingWorldText();
                return;
            }
            worldLabel = nameTag.text;
        }

        private void LogMissingWorldText()
        {
            if (_loggedMissingWorldText)
                return;
            _loggedMissingWorldText = true;
            Debug.LogError($"[SignLabels] LabeledSign {name} has no text object source; its label cannot show");
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            TileX = (int)math.round(WorldPosition.x);
            TileZ = (int)math.round(WorldPosition.z);
            try
            {
                AnySpawned?.Invoke(this);
            }
            catch (System.Exception e)
            {
                // Once per session: a broken subscriber would otherwise log on every spawn.
                if (!_loggedHandlerException)
                {
                    _loggedHandlerException = true;
                    Debug.LogError($"[SignLabels] AnySpawned handler threw {e}");
                }
            }
        }

        protected override void OnDespawn()
        {
            OnPlayerLeft();
            base.OnDespawn();
        }

        protected override void OnDeath()
        {
            base.OnDeath();
            OnPlayerLeft();
            UpdateWorldText("");
        }

        public void Interact()
        {
            var player = Manager.main.player;
            if (player == null)
                return;
            var canLabel = worldLabel != null && world.EntityManager.HasBuffer<DescriptionBuffer>(entity);
            player.SetActiveWorldLabel(canLabel ? this : null);
            Manager.ui.OnSignWindowOpen();
        }

        public void OnPlayerLeft()
        {
            var player = Manager.main.player;
            if (player == null || player.activeWorldLabel != this)
                return;
            Manager.ui.TryHideAllInventoryAndCraftingUI();
            player.SetActiveWorldLabel(null);
        }
    }
}
