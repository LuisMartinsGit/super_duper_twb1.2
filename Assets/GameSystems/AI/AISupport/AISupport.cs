// AISupport.cs
// What the AI knows about its healers (Game_AI.md § 6m). Pure queries; the
// decisions are SimpleAISystem.Support.cs.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.AI
{
    public static class AISupport
    {
        public static AISupportConfig Cfg => AISupportConfig.I;

        static readonly ComponentType[] QT_Healer =
        {
            ComponentType.ReadOnly<LitharchTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Healer;

        /// <summary>This faction's living Litharchs, into <paramref name="into"/>.</summary>
        public static void Healers(EntityManager em, Faction faction, List<Entity> into)
        {
            into.Clear();
            var q = QC_Healer.Get(em, QT_Healer);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction && hps[i].Value > 0) into.Add(ents[i]);
        }

        /// <summary>How many healers an army of <paramref name="combat"/> wants.</summary>
        public static int Wanted(int combat)
        {
            var c = Cfg;
            if (!c.enabled || c.combatUnitsPerHealer <= 0) return 0;
            return System.Math.Min(c.maxHealers, combat / c.combatUnitsPerHealer);
        }
    }
}
