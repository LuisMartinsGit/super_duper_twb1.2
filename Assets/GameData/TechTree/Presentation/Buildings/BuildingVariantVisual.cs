// BuildingVariantVisual.cs
// Controller for the authored multi-variant building prefab layout:
//
//   <BuildingRoot>
//     Lv0                          neutral construction/base model, parts
//                                  numbered 1..N for the ordered rise
//     Runai | Alanthor | Feraldis  one branch per culture, each holding
//       Lv1 / Lv2 / Lv3            the culture visual per building level
//
// Setup() hides every culture branch so only Lv0 shows (and so
// BuildingRiseData snapshots ONLY the numbered Lv0 pieces). ShowVariant()
// switches to a culture/level branch in place — used the moment
// construction completes when the faction already has a culture, and by
// BuildingPrefabSwapSystem for later level-ups.
//
// Prefabs without an "Lv0" child are untouched (TrySetup returns null),
// so every legacy visual keeps its existing pipeline.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public class BuildingVariantVisual : MonoBehaviour
    {
        private Transform _lv0;
        // Culture byte (Cultures.Runai/Alanthor/Feraldis) -> branch root.
        private readonly Dictionary<byte, Transform> _cultureRoots = new Dictionary<byte, Transform>();
        // Per culture: level number -> level node.
        private readonly Dictionary<byte, Dictionary<int, Transform>> _levels =
            new Dictionary<byte, Dictionary<int, Transform>>();
        // Level node -> generated "BaseParts" wrapper holding its non-Upgrades
        // meshes. Lets a lower level keep its EARNED tech visuals showing
        // (upgrades-only state) after the building has moved past it.
        private readonly Dictionary<Transform, Transform> _baseParts =
            new Dictionary<Transform, Transform>();
        private Transform _shown;

        /// <summary>
        /// Detect the multi-variant layout on a freshly spawned visual.
        /// Returns null for legacy prefabs (no "Lv0" child). On success the
        /// component is attached, every culture branch is deactivated, and
        /// Lv0 is active — call BEFORE BuildingRiseData.Init so the rise
        /// snapshot sees only the Lv0 pieces.
        /// </summary>
        public static BuildingVariantVisual TrySetup(GameObject root)
        {
            if (root == null) return null;

            var existing = root.GetComponent<BuildingVariantVisual>();
            if (existing != null) return existing;

            // The variant container is either the visual root itself or a
            // single authored wrapper below it (prefab roots often wrap the
            // model in one parent node).
            Transform container = FindContainerWithLv0(root.transform, maxDepth: 2);
            if (container == null) return null;

            var v = root.AddComponent<BuildingVariantVisual>();
            v.Scan(container);
            v.Show(v._lv0);
            return v;
        }

        /// <summary>
        /// Does this prefab (or spawned visual) carry the multi-variant
        /// layout? Answers the question WITHOUT instantiating, so the spawn
        /// path can decide between an SO-authored variant prefab and the
        /// legacy per-level Resources ladder before it commits to one.
        /// </summary>
        public static bool HasVariantLayout(GameObject root)
        {
            if (root == null) return false;
            return FindContainerWithLv0(root.transform, maxDepth: 2) != null;
        }

        private static Transform FindContainerWithLv0(Transform t, int maxDepth)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                if (IsLv0Name(t.GetChild(i).name)) return t;
            }
            if (maxDepth <= 1) return null;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindContainerWithLv0(t.GetChild(i), maxDepth - 1);
                if (found != null) return found;
            }
            return null;
        }

        private static bool IsLv0Name(string n) =>
            string.Equals(n, "Lv0", System.StringComparison.OrdinalIgnoreCase);

        private void Scan(Transform container)
        {
            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (IsLv0Name(child.name)) { _lv0 = child; continue; }

                byte culture = CultureForName(child.name);
                if (culture == Cultures.None) continue;

                _cultureRoots[culture] = child;
                var byLevel = new Dictionary<int, Transform>();
                for (int c = 0; c < child.childCount; c++)
                {
                    var lvNode = child.GetChild(c);
                    if (TryParseLevelName(lvNode.name, out int lvl))
                    {
                        byLevel[lvl] = lvNode;
                        HideUpgradeGroupContents(lvNode);
                        WrapBaseParts(lvNode);
                    }
                }
                _levels[culture] = byLevel;
            }
        }

        // Gather every non-Upgrades child of a level node under a generated
        // "BaseParts" wrapper, so the base model can hide independently of
        // the tech visuals: upgrading to level N keeps level N-1's EARNED
        // upgrade elements visible (they only leave when a higher tier of
        // the same tech dissolves them out).
        private void WrapBaseParts(Transform levelNode)
        {
            var wrapper = new GameObject("BaseParts").transform;
            wrapper.SetParent(levelNode, false);

            var toMove = new List<Transform>();
            for (int i = 0; i < levelNode.childCount; i++)
            {
                var child = levelNode.GetChild(i);
                if (child == wrapper) continue;
                if (child.name.StartsWith("Upgrades", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                toMove.Add(child);
            }
            for (int i = 0; i < toMove.Count; i++)
                toMove[i].SetParent(wrapper, true);

            _baseParts[levelNode] = wrapper;
        }

        // Tech-visual elements ("UpgradesLvN" groups inside each level
        // branch) start HIDDEN regardless of authored state — they appear
        // only when their technology is researched (ShowTechVisual). The
        // group itself stays active so individual nodes can toggle.
        private static void HideUpgradeGroupContents(Transform levelNode)
        {
            for (int i = 0; i < levelNode.childCount; i++)
            {
                var child = levelNode.GetChild(i);
                if (!child.name.StartsWith("Upgrades", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!child.gameObject.activeSelf) child.gameObject.SetActive(true);
                for (int c = 0; c < child.childCount; c++)
                {
                    var node = child.GetChild(c);
                    if (node.gameObject.activeSelf) node.gameObject.SetActive(false);
                }
            }
        }

        private static byte CultureForName(string n)
        {
            if (n.Equals("Runai", System.StringComparison.OrdinalIgnoreCase)
                || n.Equals("Runaii", System.StringComparison.OrdinalIgnoreCase))
                return Cultures.Runai;
            if (n.Equals("Alanthor", System.StringComparison.OrdinalIgnoreCase))
                return Cultures.Alanthor;
            if (n.Equals("Feraldis", System.StringComparison.OrdinalIgnoreCase))
                return Cultures.Feraldis;
            return Cultures.None;
        }

        private static bool TryParseLevelName(string n, out int level)
        {
            level = 0;
            if (n == null || n.Length < 3) return false;
            if (!(n[0] == 'L' || n[0] == 'l') || !(n[1] == 'v' || n[1] == 'V')) return false;
            return int.TryParse(n.Substring(2), out level) && level > 0;
        }

        /// <summary>
        /// The branch currently shown (Lv0 or a culture level node). The
        /// footprint refit measures THIS subtree only, so the dissolve
        /// transition's briefly-active old branch never pollutes the bounds.
        /// </summary>
        public Transform ShownBranch => _shown;

        /// <summary>
        /// Switch the visible branch. Culture None or level below 1 (or a
        /// culture branch this prefab doesn't have) shows Lv0. A missing
        /// exact level falls back to the highest authored level below it.
        /// Returns true when the visible branch actually changed (callers
        /// gate the level-up flourish on this).
        /// </summary>
        public bool ShowVariant(byte culture, int level)
        {
            Transform target = ResolveTarget(culture, level);
            if (target == _shown) return false;
            Show(target);
            return true;
        }

        /// <summary>
        /// Switch with the building-upgrade DISSOLVE: the old branch is eaten
        /// away from the base up while the new one reveals along the same
        /// wave (same effect the Hall's level-up prefab swap uses). The
        /// faction marker recolor is applied to the freshly activated branch
        /// BEFORE the wave binds materials, so the reveal shows team colors.
        /// Returns false when nothing changed (no wave, no effect).
        /// </summary>
        public bool ShowVariantWithTransition(byte culture, int level, Color accent)
        {
            Transform target = ResolveTarget(culture, level);
            if (target == _shown) return false;

            Transform old = _shown;
            if (old == null)
            {
                Show(target);
                return true;
            }

            // Activate the new branch while the old one STAYS visible — the
            // wave needs both; it deactivates the old side on completion
            // (destroyOldOnComplete: false restores its materials too).
            ActivateForTransition(target, old);
            _shown = target;
            BuildingFactionColorMarker.Apply(gameObject, accent);
            RefitSelectionCollider();

            // Old side of the wave: for a level node, only its BASE meshes
            // dissolve out — the node itself stays active, so its earned
            // tech visuals persist onto the new level (they leave only when
            // a higher tier of the same tech replaces them). Lv0 has no
            // persistent upgrades and dissolves out whole.
            GameObject oldDissolveGo = old.gameObject;
            if (old != _lv0 && _baseParts.TryGetValue(old, out var oldBase) && oldBase != null)
                oldDissolveGo = oldBase.gameObject;

            BuildingDissolveTransition.Begin(
                oldDissolveGo, target.gameObject,
                duration: 1.5f, edgeColor: accent, destroyOldOnComplete: false);
            return true;
        }

        private Transform ResolveTarget(byte culture, int level)
        {
            Transform target = _lv0;

            if (culture != Cultures.None && level >= 1
                && _cultureRoots.ContainsKey(culture)
                && _levels.TryGetValue(culture, out var byLevel) && byLevel.Count > 0)
            {
                Transform best = null;
                int bestLevel = 0;
                foreach (var kvp in byLevel)
                {
                    if (kvp.Key <= level && kvp.Key > bestLevel)
                    {
                        bestLevel = kvp.Key;
                        best = kvp.Value;
                    }
                }
                if (best != null) target = best;
            }
            return target;
        }

        /// <summary>Pre-transition activation: target level fully on, its
        /// culture root on, intermediate lower levels upgrades-only, higher
        /// levels hidden. The OLD node is left untouched — it stays fully
        /// visible for the wave, and the transition's cleanup hides only its
        /// BaseParts, leaving it in the upgrades-only state.</summary>
        private void ActivateForTransition(Transform target, Transform old)
        {
            if (target == _lv0)
            {
                if (!_lv0.gameObject.activeSelf) _lv0.gameObject.SetActive(true);
                return;
            }

            foreach (var kvp in _cultureRoots)
            {
                var cultureRoot = kvp.Value;
                if (!target.IsChildOf(cultureRoot)) continue;

                if (!cultureRoot.gameObject.activeSelf)
                    cultureRoot.gameObject.SetActive(true);

                if (_levels.TryGetValue(kvp.Key, out var byLevel))
                {
                    int targetLevel = 0;
                    foreach (var lv in byLevel)
                        if (lv.Value == target) { targetLevel = lv.Key; break; }

                    foreach (var lv in byLevel)
                    {
                        if (lv.Value == old) continue; // wave owns its base
                        if (lv.Value == target) SetLevelNodeState(lv.Value, visible: true, baseVisible: true);
                        else if (lv.Key < targetLevel) SetLevelNodeState(lv.Value, visible: true, baseVisible: false);
                        else SetLevelNodeState(lv.Value, visible: false, baseVisible: false);
                    }
                }
                return;
            }
        }

        private void Show(Transform target)
        {
            _shown = target;
            bool lv0Visible = target == _lv0;

            if (_lv0 != null && _lv0.gameObject.activeSelf != lv0Visible)
                _lv0.gameObject.SetActive(lv0Visible);

            foreach (var kvp in _cultureRoots)
            {
                var cultureRoot = kvp.Value;
                bool containsTarget = !lv0Visible && target != null && target.IsChildOf(cultureRoot);
                if (cultureRoot.gameObject.activeSelf != containsTarget)
                    cultureRoot.gameObject.SetActive(containsTarget);

                if (!containsTarget) continue;

                // Inside the active culture branch: the target level shows in
                // full; LOWER levels stay active with their base meshes
                // hidden so already-earned tech visuals persist; higher
                // levels stay fully hidden.
                if (_levels.TryGetValue(kvp.Key, out var byLevel))
                {
                    int targetLevel = 0;
                    foreach (var lv in byLevel)
                        if (lv.Value == target) { targetLevel = lv.Key; break; }

                    foreach (var lv in byLevel)
                    {
                        if (lv.Key == targetLevel) SetLevelNodeState(lv.Value, visible: true, baseVisible: true);
                        else if (lv.Key < targetLevel) SetLevelNodeState(lv.Value, visible: true, baseVisible: false);
                        else SetLevelNodeState(lv.Value, visible: false, baseVisible: false);
                    }
                }
            }

            RefitSelectionCollider();
        }

        private void SetLevelNodeState(Transform node, bool visible, bool baseVisible)
        {
            if (!visible)
            {
                if (node.gameObject.activeSelf) node.gameObject.SetActive(false);
                return;
            }
            if (!node.gameObject.activeSelf) node.gameObject.SetActive(true);
            if (_baseParts.TryGetValue(node, out var basePart) && basePart != null
                && basePart.gameObject.activeSelf != baseVisible)
                basePart.gameObject.SetActive(baseVisible);
        }

        // ─────────────────────────────────────────────────────────────────
        // TECH VISUALS — "UpgradesLvN" nodes matched to technology ids.
        // ─────────────────────────────────────────────────────────────────

        private struct TechVisualDef
        {
            public string TechId;
            public string[] Nodes;         // node-name candidates in the prefab
            public string[] ReplacesNodes; // nodes this tech visually supersedes
        }

        // Node names follow the authored prefab (roman-numeral tiers); the
        // Replaces chains hide the superseded tier's mesh — including the
        // BASE "wall_low" mesh that Veilstone Walls physically replaces.
        private static readonly TechVisualDef[] TechVisuals =
        {
            new TechVisualDef { TechId = "IronReinforcements",
                Nodes = new[] { "Iron_reinforcements", "Iron_Reinforcements" } },
            new TechVisualDef { TechId = "IronSurveying1",
                Nodes = new[] { "Iron_Surveying_I" } },
            new TechVisualDef { TechId = "IronSurveying2",
                Nodes = new[] { "Iron_Surveying_II" },
                ReplacesNodes = new[] { "Iron_Surveying_I" } },
            new TechVisualDef { TechId = "IronSurveying3",
                Nodes = new[] { "Iron_Surveying_III" },
                ReplacesNodes = new[] { "Iron_Surveying_II", "Iron_Surveying_I" } },
            new TechVisualDef { TechId = "VeilstoneSurvey1",
                Nodes = new[] { "Veilstone_Surveying_I" } },
            new TechVisualDef { TechId = "VeilstoneSurvey2",
                Nodes = new[] { "Veilstone_Surveying_II" },
                ReplacesNodes = new[] { "Veilstone_Surveying_I" } },
            new TechVisualDef { TechId = "VeilsteelSurvey",
                Nodes = new[] { "Veilsteel_Surveying" } },
            new TechVisualDef { TechId = "VeilstoneWalls",
                Nodes = new[] { "Veilstone_Walls" },
                ReplacesNodes = new[] { "wall_low" } },
            // No authored look yet — resolves to nothing until the artist
            // adds a node with this name.
            new TechVisualDef { TechId = "VeilsteelPylons",
                Nodes = new[] { "Veilsteel_Pylons" } },
        };

        /// <summary>
        /// Reveal the visual element for a researched technology inside the
        /// CURRENTLY SHOWN branch. Superseded nodes (lower survey tiers, the
        /// base wall mesh for Veilstone Walls) dissolve out along the same
        /// wave when withTransition is set. No-ops (false) when the branch
        /// has no node for this tech or it is already visible.
        /// </summary>
        public bool ShowTechVisual(string techId, Color accent, bool withTransition)
        {
            if (_shown == null || string.IsNullOrEmpty(techId)) return false;

            int defIdx = -1;
            for (int i = 0; i < TechVisuals.Length; i++)
            {
                if (string.Equals(TechVisuals[i].TechId, techId,
                        System.StringComparison.OrdinalIgnoreCase)) { defIdx = i; break; }
            }
            if (defIdx < 0) return false;
            var def = TechVisuals[defIdx];

            Transform node = FindTechNode(def.Nodes);
            if (node == null || node.gameObject.activeSelf) return false;

            // Highest VISIBLE superseded node dissolves out with the reveal
            // — it may live in a lower level's persisting upgrade group.
            Transform replaced = null;
            if (def.ReplacesNodes != null)
            {
                for (int i = 0; i < def.ReplacesNodes.Length && replaced == null; i++)
                {
                    var candidate = FindTechNode(new[] { def.ReplacesNodes[i] });
                    if (candidate != null && candidate.gameObject.activeSelf)
                        replaced = candidate;
                }
            }

            node.gameObject.SetActive(true);
            BuildingFactionColorMarker.Apply(gameObject, accent);

            // The wave needs both sides actually rendering; a node revealed
            // inside a hidden branch (research finishing ahead of the level)
            // just arms itself for that branch's later reveal.
            bool waveVisible = node.gameObject.activeInHierarchy
                && (replaced == null || replaced.gameObject.activeInHierarchy);

            if (withTransition && waveVisible)
            {
                BuildingDissolveTransition.Begin(
                    replaced != null ? replaced.gameObject : null, node.gameObject,
                    duration: 1.5f, edgeColor: accent, destroyOldOnComplete: false);
            }
            else if (replaced != null)
            {
                replaced.gameObject.SetActive(false);
            }
            return true;
        }

        /// <summary>
        /// Bring the shown branch's tech visuals in line with the faction's
        /// researched technologies. Instant after a branch switch (the new
        /// branch reveals with its earned upgrades already in place); with
        /// transition from the periodic scan, so a research completing
        /// mid-game dissolves in.
        /// </summary>
        public void SyncTechVisuals(Faction faction, Color accent, bool withTransition)
        {
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research == null) return;

            for (int i = 0; i < TechVisuals.Length; i++)
            {
                if (research.HasResearched(faction, TechVisuals[i].TechId))
                    ShowTechVisual(TechVisuals[i].TechId, accent, withTransition);
            }
        }

        // Search the WHOLE culture branch (all level nodes), not just the
        // shown level: replaced tiers persist in lower levels' upgrade
        // groups, and a lower-tier tech researched after a level-up still
        // has its only node down there. Matches under the shown level win.
        private Transform FindTechNode(string[] names)
        {
            if (_shown == null || names == null) return null;

            Transform searchRoot = _shown;
            if (_shown != _lv0 && _shown.parent != null)
                searchRoot = _shown.parent; // the culture root

            var all = searchRoot.GetComponentsInChildren<Transform>(true);
            for (int n = 0; n < names.Length; n++)
            {
                Transform fallback = null;
                for (int i = 0; i < all.Length; i++)
                {
                    if (!string.Equals(all[i].name, names[n],
                            System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (all[i].IsChildOf(_shown)) return all[i];
                    if (fallback == null) fallback = all[i];
                }
                if (fallback != null) return fallback;
            }
            return null;
        }

        // The selection collider was fitted to the Lv0 bounds at spawn; the
        // culture models can differ in silhouette, so refit after a switch.
        //
        // Delegates to the shared fitter rather than open-coding the bounds
        // maths: this copy had no minimum-size floor, so a culture model with a
        // slim silhouette could shrink the click box below what the building
        // actually occupies — the same class of defect as the Hall's tiny
        // collider. The entity-aware overload floors it by BuildingSize/Radius.
        private void RefitSelectionCollider()
        {
            if (GetComponent<BoxCollider>() == null) return;

            var link = GetComponent<TheWaningBorder.Core.EntityReference>();
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;

            if (link != null && world != null && world.IsCreated)
                PresentationSpawnSystem.FitSelectionCollider(gameObject, link.Entity, world.EntityManager);
            else
                PresentationSpawnSystem.FitSelectionCollider(gameObject);
        }
    }
}
