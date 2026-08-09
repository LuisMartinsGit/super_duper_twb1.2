// File: Assets/Scripts/Entities/Units/Tinker.cs
// The Tinker — Sect of Renewal's unit lever (task-063 spec, Lv I:
// "field repair worker (slow), cannot fight"). Implemented as a Worker
// variant: keeps CanBuild so the existing construction/repair systems
// drive it, drops MinerTag so it never mines, and never receives a
// Damage component so combat systems ignore it. Trained at the Temple
// of Ridan once Renewal is adopted.

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class Tinker
    {
        private const int HP = 80;
        public const int PresentationID = 405; // 388-403 taken; see PresentationSpawnSystem table

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var e = Worker.Create(em, position, faction);
            em.SetComponentData(e, new PresentationId { Id = PresentationID });
            em.SetComponentData(e, new Health { Value = HP, Max = HP });
            if (em.HasComponent<MinerTag>(e)) em.RemoveComponent<MinerTag>(e);
            return e;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var e = Worker.Create(ecb, position, faction);
            ecb.SetComponent(e, new PresentationId { Id = PresentationID });
            ecb.SetComponent(e, new Health { Value = HP, Max = HP });
            ecb.RemoveComponent<MinerTag>(e);
            return e;
        }
    }
}
