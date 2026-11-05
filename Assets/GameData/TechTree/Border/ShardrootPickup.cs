// Free-floating Shardroot pickup spawned at the end of a ritual. Carry / deposit /
// intercept mechanics are a follow-up slice — for now the pickup just sits
// at the spawn position and despawns after ShardrootPickupTimeout (spec §4.5).

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>The Shardroot before anyone has picked it up
    /// (Monuments_And_Petriarchy.md §3): invisible except to the factions in
    /// RevealedTo (a bit per Faction). A dropped artifact is a new pickup without
    /// this, so picking it up once makes it public for good.</summary>
    public struct ShardrootHidden : IComponentData
    {
        public uint RevealedTo;
    }

    /// <summary>
    /// Shardroot pickup entity. Spec §5.1: "Drop a physical Shardroot pickup on
    /// completion that must be carried back to a deposit building. Shardroot
    /// pickup can be intercepted in transit by any faction."
    ///
    /// FactionTag is left unset (Faction.Border as a neutral default) so
    /// any unit can attempt to claim it. The pickup is owned by no one
    /// until carried.
    /// </summary>
    public static class ShardrootPickup
    {
        private static readonly ComponentType[] QT_Hidden = { ComponentType.ReadOnly<ShardrootHidden>() };
        private static TheWaningBorder.Core.CachedEntityQuery QC_Hidden;

        /// <summary>Can this faction not know this pickup exists?</summary>
        public static bool HiddenFrom(EntityManager em, Entity pickup, Faction faction)
            => em.HasComponent<ShardrootHidden>(pickup)
               && (em.GetComponentData<ShardrootHidden>(pickup).RevealedTo & (1u << (int)faction)) == 0;

        /// <summary>Lying hidden, and revealed to this faction.</summary>
        public static bool RevealedTo(EntityManager em, Entity pickup, Faction faction)
            => em.HasComponent<ShardrootHidden>(pickup)
               && (em.GetComponentData<ShardrootHidden>(pickup).RevealedTo & (1u << (int)faction)) != 0;

        /// <summary>Hidden from whoever this screen shows: the local player, or the
        /// faction a spectator watches. A spectator with none selected sees it.</summary>
        public static bool HiddenFromView(EntityManager em, Entity pickup)
        {
            var view = GameSettings.ViewFaction;
            return view.HasValue && HiddenFrom(em, pickup, view.Value);
        }

        /// <summary>Does the artifact lie hidden from this faction right now?</summary>
        public static bool AnyHiddenFrom(EntityManager em, Faction faction)
        {
            using var ents = QC_Hidden.Get(em, QT_Hidden).ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (HiddenFrom(em, ents[i], faction)) return true;
            return false;
        }

        public static bool AnyHiddenFromView(EntityManager em)
        {
            var view = GameSettings.ViewFaction;
            return view.HasValue && AnyHiddenFrom(em, view.Value);
        }

        /// <summary>Tell one faction where the hidden artifact lies. False when it no
        /// longer lies hidden.</summary>
        public static bool TryReveal(EntityManager em, Faction faction, out float3 at)
        {
            at = default;
            using var ents = QC_Hidden.Get(em, QT_Hidden).ToEntityArray(Unity.Collections.Allocator.Temp);
            if (ents.Length == 0) return false;
            var h = em.GetComponentData<ShardrootHidden>(ents[0]);
            h.RevealedTo |= 1u << (int)faction;
            em.SetComponentData(ents[0], h);
            at = em.GetComponentData<LocalTransform>(ents[0]).Position;
            return true;
        }

        public static Entity Create(EntityManager em, float3 position, RitualKind source, int amount = -1)
        {
            int yield = amount > 0
                ? amount
                : source switch
                {
                    RitualKind.Purification => PurificationShardrootYield,
                    _ => PurificationShardrootYield,  // Conversion / Violent Extraction yields TBD by follow-up
                };

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(ShardrootPickupTag),
                typeof(ShardrootPickupState),
                typeof(Radius)
            );

            em.SetComponentData(entity, new PresentationId { Id = ShardrootPresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = Faction.Border }); // neutral; reassigned on pickup
            em.SetComponentData(entity, new ShardrootPickupState
            {
                Amount = yield,
                TimeRemaining = ShardrootPickupTimeout,
                Source = source,
                Attuner = Entity.Null,
                AttunementProgress = 0f,
            });
            em.SetComponentData(entity, new Radius { Value = 0.6f });

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, RitualKind source, int amount = -1)
        {
            int yield = amount > 0
                ? amount
                : source switch
                {
                    RitualKind.Purification => PurificationShardrootYield,
                    _ => PurificationShardrootYield,
                };

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = ShardrootPresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = Faction.Border });
            ecb.AddComponent<ShardrootPickupTag>(entity);
            ecb.AddComponent(entity, new ShardrootPickupState
            {
                Amount = yield,
                TimeRemaining = ShardrootPickupTimeout,
                Source = source,
                Attuner = Entity.Null,
                AttunementProgress = 0f,
            });
            ecb.AddComponent(entity, new Radius { Value = 0.6f });

            return entity;
        }
    }
}
