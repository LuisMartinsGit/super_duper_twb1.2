// NodeRubbleVisualSystem.cs
// Presentation for the veilstone main-node destruction cycle (2026-07 rework).
// Drives each main node's view GameObject from its ECS state:
//
//   Active                         → crystal shown, no rubble
//   Destroyed + !NodeRebuilding    → crystal hidden, rubble pile shown (dormant)
//   Destroyed + NodeRebuilding     → crystal shown, rubble shrinking away as the
//                                    node reconstructs (StateTimer / NodeRebuildTime)
//   Cleansed                       → crystal shown but dimmed (inert purified husk)
//
// Only the crystal renderers are toggled + a procedural rubble child is
// scaled — the node's root transform stays owned by
// PresentationSpawnSystem.SyncTransforms, so this never fights it.
//
// MonoBehaviour, mounted on RuntimeManagers by GameBootstrap. Polls a few
// nodes at ScanInterval — cheap.
//
// Location: Assets/GameData/TechTree/Buildings/Border/LargeNode/NodeRubbleVisualSystem.cs

using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Presentation
{
    public class NodeRubbleVisualSystem : MonoBehaviour
    {
        public static NodeRubbleVisualSystem Instance { get; private set; }

        private const float ScanInterval = 0.25f;
        private float _scanTimer;

        private Unity.Entities.World _world;
        private EntityManager _em;
        private EntityQuery _nodeQuery;
        private bool _ready;

        // Per-node rubble pile GameObject (child of the node view).
        private readonly Dictionary<Entity, GameObject> _rubble = new();
        private Material _rubbleMat;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
            {
                _em = _world.EntityManager;
                _nodeQuery = _em.CreateEntityQuery(
                    ComponentType.ReadOnly<BorderMainNodeTag>(),
                    ComponentType.ReadOnly<BorderNodeState>());
                _ready = true;
            }
        }

        void Update()
        {
            if (!_ready || _world == null || !_world.IsCreated) return;
            if (EntityViewManager.Instance == null) return;

            _scanTimer += Time.deltaTime;
            if (_scanTimer < ScanInterval) return;
            _scanTimer = 0f;

            using var nodes = _nodeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < nodes.Length; i++)
                UpdateNode(nodes[i]);

            PruneDeadRubble();
        }

        private void UpdateNode(Entity node)
        {
            var view = EntityViewManager.Instance.GetView(node);
            if (view == null) return;

            var state = _em.GetComponentData<BorderNodeState>(node);
            bool dormant     = _em.HasComponent<NodeDormant>(node);
            bool rebuilding  = _em.HasComponent<NodeRebuilding>(node);

            bool rubblePhase  = dormant && !rebuilding;                 // lying in rubble
            bool rebuildPhase = dormant && rebuilding;                  // reconstructing
            bool cleansed     = state.State == NodeState.Cleansed;

            // Crystal renderers: hidden only during the rubble phase.
            SetCrystalVisible(view, !rubblePhase);

            // Purified husk reads as inert — dim it (cheap: half-scale the view's
            // emissive punch is out of reach, so we just dim renderers via
            // material color when cleansed; skipped if it has no color prop).
            // Keeping it visible (not deleted) so victory tracking still counts it.

            if (rubblePhase || rebuildPhase)
            {
                var rubble = GetOrCreateRubble(node, view);
                if (rubble != null)
                {
                    rubble.SetActive(true);
                    // Shrink the rubble away as the rebuild completes.
                    float scale = 1f;
                    if (rebuildPhase)
                    {
                        float frac = NodeRebuildTime > 0f
                            ? Mathf.Clamp01(state.StateTimer / NodeRebuildTime) : 1f;
                        scale = Mathf.Lerp(1f, 0.05f, frac);
                    }
                    rubble.transform.localScale = Vector3.one * scale;
                }
            }
            else
            {
                // Active or Cleansed — rubble gone.
                if (_rubble.TryGetValue(node, out var r) && r != null)
                    r.SetActive(false);
            }
        }

        private static void SetCrystalVisible(GameObject view, bool visible)
        {
            var renderers = view.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                // Leave the rubble pile's own renderers alone.
                if (renderers[i].transform.IsChildOf(view.transform)
                    && renderers[i].gameObject.name.StartsWith("NodeRubble"))
                    continue;
                if (renderers[i].enabled != visible)
                    renderers[i].enabled = visible;
            }
        }

        private GameObject GetOrCreateRubble(Entity node, GameObject view)
        {
            if (_rubble.TryGetValue(node, out var existing) && existing != null)
            {
                // Re-parent if the view was swapped out from under us.
                if (existing.transform.parent != view.transform)
                    existing.transform.SetParent(view.transform, false);
                return existing;
            }

            var rubble = BuildRubble(node);
            rubble.transform.SetParent(view.transform, false);
            rubble.transform.localPosition = Vector3.zero;
            _rubble[node] = rubble;
            return rubble;
        }

        // A small mound of dark angular shards. Offsets are derived from the
        // entity index so a given node's rubble is stable across the session.
        private GameObject BuildRubble(Entity node)
        {
            if (_rubbleMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _rubbleMat = new Material(shader);
                var dark = new Color(0.16f, 0.13f, 0.20f); // spent-veilstone charcoal-violet
                if (_rubbleMat.HasProperty("_BaseColor")) _rubbleMat.SetColor("_BaseColor", dark);
                if (_rubbleMat.HasProperty("_Color")) _rubbleMat.SetColor("_Color", dark);
            }

            var root = new GameObject("NodeRubble");
            const int shards = 7;
            for (int i = 0; i < shards; i++)
            {
                // Deterministic scatter from index + shard number.
                float a = ((node.Index * 131 + i * 977) % 360) * Mathf.Deg2Rad;
                float r = 0.4f + ((node.Index * 37 + i * 53) % 100) / 100f * 1.4f;
                float sx = 0.5f + ((node.Index + i * 7) % 60) / 100f;
                float sy = 0.25f + ((node.Index * 3 + i * 11) % 45) / 100f;

                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "NodeRubbleShard";
                var col = shard.GetComponent<Collider>();
                if (col != null) Destroy(col);            // rubble is non-interactive
                shard.transform.SetParent(root.transform, false);
                shard.transform.localPosition = new Vector3(
                    Mathf.Cos(a) * r, sy * 0.5f, Mathf.Sin(a) * r);
                shard.transform.localRotation = Quaternion.Euler(
                    (node.Index * 17 + i * 29) % 40 - 20,
                    (node.Index * 23 + i * 41) % 360,
                    (node.Index * 13 + i * 31) % 40 - 20);
                shard.transform.localScale = new Vector3(sx, sy, sx);
                var mr = shard.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = _rubbleMat;
            }
            return root;
        }

        private void PruneDeadRubble()
        {
            if (_rubble.Count == 0) return;
            List<Entity> dead = null;
            foreach (var kvp in _rubble)
            {
                if (!_em.Exists(kvp.Key) || kvp.Value == null)
                    (dead ??= new List<Entity>()).Add(kvp.Key);
            }
            if (dead != null)
                for (int i = 0; i < dead.Count; i++)
                {
                    if (_rubble.TryGetValue(dead[i], out var go) && go != null) Destroy(go);
                    _rubble.Remove(dead[i]);
                }
        }
    }
}
