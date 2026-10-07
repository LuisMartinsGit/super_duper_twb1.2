// AIShardroot.cs
// What the AI knows about the Shardroot (Game_AI.md § 6l): where it is,
// who carries it, where this faction's living king and nearest Hall are.
// Pure queries; the decisions are SimpleAISystem.Shardroot.cs.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;

namespace TheWaningBorder.AI
{
    public static class AIShardroot
    {
        public static AIShardrootConfig Cfg => AIShardrootConfig.I;

        public enum Where : byte
        {
            /// <summary>Not in play (undiscovered, despawned) or enshrined.</summary>
            None = 0,
            /// <summary>A pickup on the ground.</summary>
            Ground = 1,
            /// <summary>Carried by one of this faction's units.</summary>
            Mine = 2,
            /// <summary>Carried by anyone hostile — another faction or the curse.</summary>
            Hostile = 3,
            /// <summary>Carried by an ally: leave it to them.</summary>
            Friendly = 4,
        }

        static readonly ComponentType[] QT_Pickup =
        {
            ComponentType.ReadOnly<ShardrootPickupTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Pickup;

        static readonly ComponentType[] QT_Bearer =
        {
            ComponentType.ReadOnly<ShardrootBearer>(),
            ComponentType.ReadOnly<ShardrootTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Bearer;

        static readonly ComponentType[] QT_Hero =
        {
            ComponentType.ReadOnly<UniqueUnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Hero;

        static readonly ComponentType[] QT_Hall =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Hall;

        /// <summary>Where the artifact is, for <paramref name="faction"/>.
        /// <paramref name="entity"/> is the pickup or the carrier.</summary>
        public static Where Locate(EntityManager em, Faction faction, out Entity entity, out float3 pos)
        {
            entity = Entity.Null; pos = default;
            var bq = QC_Bearer.Get(em, QT_Bearer);
            using (var ents = bq.ToEntityArray(Allocator.Temp))
            using (var facs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                if (ents.Length > 0)
                {
                    // There is only ever one Shardroot.
                    entity = ents[0]; pos = xfs[0].Position;
                    var f = facs[0].Value;
                    if (f == faction) return Where.Mine;
                    return Alliances.AreHostile(faction, f) ? Where.Hostile : Where.Friendly;
                }
            var pq = QC_Pickup.Get(em, QT_Pickup);
            using (var ents = pq.ToEntityArray(Allocator.Temp))
            using (var xfs = pq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                if (ents.Length > 0) { entity = ents[0]; pos = xfs[0].Position; return Where.Ground; }
            return Where.None;
        }

        /// <summary>This faction's living King Lexor, or Entity.Null.</summary>
        public static Entity LivingKing(EntityManager em, Faction faction)
        {
            var q = QC_Hero.Get(em, QT_Hero);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UniqueUnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction && hps[i].Value > 0 && tags[i].Kind == UniqueUnitKind.KingLexor)
                    return ents[i];
            return Entity.Null;
        }

        /// <summary>The nearest finished Hall (the capital) of this faction —
        /// where a carrier hands the artifact to the king.</summary>
        public static bool TryNearestHall(EntityManager em, Faction faction, float3 from, out float3 hallPos)
        {
            hallPos = default;
            var q = QC_Hall.Get(em, QT_Hall);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float best = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                float d = math.distancesq(xfs[i].Position.xz, from.xz);
                if (d < best) { best = d; hallPos = xfs[i].Position; }
            }
            return best < float.MaxValue;
        }
    }
}
