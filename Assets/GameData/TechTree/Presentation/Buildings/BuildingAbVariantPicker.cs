// BuildingAbVariantPicker.cs
// Per-entity silhouette variety for authored buildings that ship more than
// one model per level.
//
// The house/hut art comes in an A and a B shape for every level. Inside the
// multi-variant prefab those live side by side under each level node:
//
//   Alanthor
//     Lv1
//       Lv1_A   <- one shape
//       Lv1_B   <- the other
//     Lv2
//       Lv2_A
//       Lv2_B
//
// This picker leaves exactly one of each pair active. Two properties matter:
//
//  * DETERMINISTIC, not random. The game runs a lockstep multiplayer
//    simulation; UnityEngine.Random would hand different peers different
//    hierarchies and, because BuildingVariantVisual reparents level nodes at
//    setup, desync the visual tree. The seed is the entity index — the same
//    source BuildingPrefabSwapSystem already uses for legacy house variants
//    (`1 + Mathf.Abs(e.Index) % 2`), so a given building always shows the
//    same shape for every player and for the whole session.
//
//  * STABLE ACROSS LEVELS. The pick is keyed off the seed ONLY, never the
//    group name, so Lv1_A / Lv2_A / Lv3_A resolve together. Keying per group
//    would let a hut upgrade from the A shape into the B shape, which reads
//    as the building being replaced rather than improved.
//
// Prefabs with no lettered groups are untouched, so every existing visual
// keeps its current behaviour.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class BuildingAbVariantPicker
    {
        // "Lv1_A" -> suffix 'A'. Groups are matched by their shared prefix so
        // siblings that merely happen to end in a letter never pair up.
        private const string Separator = "_";

        /// <summary>
        /// Resolve every lettered variant group under <paramref name="root"/>,
        /// leaving one child of each group active. Safe to call on any prefab;
        /// returns the number of groups resolved (0 = nothing to do).
        /// </summary>
        public static int Apply(GameObject root, int seed)
        {
            if (root == null) return 0;

            // includeInactive: the culture branches are deactivated by
            // BuildingVariantVisual.TrySetup before this runs, so an
            // active-only walk would find nothing at all.
            var all = root.GetComponentsInChildren<Transform>(true);

            // parent -> prefix -> (letter, transform) candidates
            var groups = new Dictionary<Transform, Dictionary<string, List<(char Letter, Transform T)>>>();

            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                var parent = t.parent;
                if (parent == null) continue;

                if (!TrySplit(t.name, out string prefix, out char letter)) continue;

                if (!groups.TryGetValue(parent, out var byPrefix))
                {
                    byPrefix = new Dictionary<string, List<(char, Transform)>>();
                    groups[parent] = byPrefix;
                }
                if (!byPrefix.TryGetValue(prefix, out var list))
                {
                    list = new List<(char, Transform)>();
                    byPrefix[prefix] = list;
                }
                list.Add((letter, t));
            }

            int resolved = 0;
            foreach (var byPrefix in groups.Values)
            {
                foreach (var list in byPrefix.Values)
                {
                    if (list.Count < 2) continue; // a lone "_A" is just a name

                    // Sort by letter so the candidate order is independent of
                    // Unity's child ordering — otherwise a re-export that
                    // shuffled siblings would silently change which shape a
                    // given entity shows.
                    list.Sort((x, y) => x.Letter.CompareTo(y.Letter));

                    int pick = Mathf.Abs(seed) % list.Count;
                    for (int i = 0; i < list.Count; i++)
                    {
                        bool on = (i == pick);
                        if (list[i].T.gameObject.activeSelf != on)
                            list[i].T.gameObject.SetActive(on);
                    }
                    resolved++;
                }
            }
            return resolved;
        }

        /// <summary>
        /// "Lv1_A" -> ("Lv1", 'A'). False for anything not ending in a single
        /// separator + uppercase letter.
        /// </summary>
        private static bool TrySplit(string name, out string prefix, out char letter)
        {
            prefix = null;
            letter = '\0';
            if (string.IsNullOrEmpty(name) || name.Length < 3) return false;

            int sep = name.Length - 2;
            if (name[sep] != Separator[0]) return false;

            char c = name[name.Length - 1];
            if (c < 'A' || c > 'Z') return false;

            prefix = name.Substring(0, sep);
            letter = c;
            return true;
        }
    }
}
