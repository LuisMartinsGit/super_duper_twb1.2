// PetriarchyArmy.cs
// The Petriarchy's Army (docs/Design/Monuments_And_Petriarchy.md §6): religion
// points summon sect units around the Temple, drawn evenly from every sect's unit
// the edition carries. Spacing comes from the index alone, with no RNG draw, so
// every lockstep peer forms the same army in the same order.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Sect
{
    public static class PetriarchyArmy
    {
        private const int UnitsPerRing = 24;

        private static readonly ComponentType[] QT_Temples =
        {
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Temples;

        /// <summary>The sect units this build carries, in sect order.</summary>
        public static List<string> Roster()
        {
            var roster = new List<string>();
            foreach (var sect in SectConfig.AllSectIds)
            {
                string id = SectConfig.UnitIdFor(sect);
                if (string.IsNullOrEmpty(id) || !UnitFactory.HasRecipe(id)) continue;
                if (!TechCatalog.TryGetUnit(id, out _)) continue;
                if (!SectConfig.IsInEdition(sect)) continue;
                roster.Add(id);
            }
            return roster;
        }

        private static bool TryTemple(EntityManager em, Faction faction, out float3 position)
        {
            position = default;
            var q = QC_Temples.Get(em, QT_Temples);
            using var ents = q.ToEntityArray(Allocator.Temp);
            int best = int.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (e.Index >= best) continue;
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) continue;
                best = e.Index;
                position = em.GetComponentData<LocalTransform>(e).Position;
            }
            return best != int.MaxValue;
        }

        public static int Summon(EntityManager em, Faction faction, int count)
        {
            if (count <= 0 || !TryTemple(em, faction, out var origin)) return 0;
            var roster = Roster();
            if (roster.Count == 0) return 0;

            var cfg = FactionReligionPointsHelper.Cfg;
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                int ring = i / UnitsPerRing;
                float angle = math.PI * 2f * (i % UnitsPerRing) / UnitsPerRing;
                float r = cfg.armyRingRadius + ring * cfg.armyRingStep;
                float x = origin.x + math.cos(angle) * r;
                float z = origin.z + math.sin(angle) * r;
                var pos = new float3(x, TerrainUtility.GetHeight(x, z), z);

                var unit = UnitFactory.Create(em, roster[i % roster.Count], pos, faction);
                if (unit == Entity.Null) continue;
                TheWaningBorder.Systems.Research.TechEffectSystem.ApplyCompletedTechEffects(em, unit, faction);
                made++;
            }
            if (made > 0)
            {
                SimSignals.Ping(origin, SimPingKind.Combat, cfg.armyRingRadius);
                UnityEngine.Debug.Log($"[Petriarchy] {faction} summoned an army of {made} sect units");
            }
            return made;
        }
    }
}
