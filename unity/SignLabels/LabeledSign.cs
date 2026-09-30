using System.Collections.Generic;
using Unity.Mathematics;
using Unity.NetCode; // DIAG — only needed for LogSpawnDiagnostics below.
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
            MoveInteractableToChild();
            base.Awake();
            AttachWorldText();

            if (interactable != null && interactable.onUseActions.Count > 0 && interactable.onTriggerExitActions.Count > 0)
            {
                interactable.onUseActions[0].AddListener(Interact);
                interactable.onTriggerExitActions[0].AddListener(OnPlayerLeft);
            }
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
            LogSpawnDiagnostics(); // DIAG
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

        // DIAG — temporary, added for Task 4 fix round 1 (default-visibility RPC not taking
        // effect). Logs every spawn, matched or not, so a session's log can be read against
        // DefaultVisibility's `placement at` / `default … applied` lines. Remove once the
        // investigation is closed.
        private void LogSpawnDiagnostics()
        {
            bool hasGhost = world.EntityManager.HasComponent<GhostInstance>(entity);
            string ghostInfo = "no";
            if (hasGhost)
            {
                var ghost = world.EntityManager.GetComponentData<GhostInstance>(entity);
                string spawnTick = ghost.spawnTick.IsValid ? ghost.spawnTick.SerializedData.ToString() : "invalid";
                ghostInfo = $"yes ghostId={ghost.ghostId} spawnTick={spawnTick}";
            }
            bool hasSpawnRequest = world.EntityManager.HasComponent<PredictedGhostSpawnRequest>(entity);
            bool hasPredictedGhost = world.EntityManager.HasComponent<PredictedGhost>(entity);

            Debug.Log(
                $"[SignLabels] DIAG spawn {name} at ({TileX},{TileZ}) entity={entity.Index}:{entity.Version} world={world.Name} "
                    + $"state={GetState()} ghost={ghostInfo} predictedSpawnRequest={hasSpawnRequest} predictedGhost={hasPredictedGhost}"
            );
        }

        protected override void OnDespawn()
        {
            // DIAG — see LogSpawnDiagnostics above; same fix round.
            Debug.Log($"[SignLabels] DIAG despawn {name} at ({TileX},{TileZ}) entity={entity.Index}:{entity.Version}");
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
