// TempleChapelBuildSystem.cs
// Ticks chapel build timers on temple slot buffers and spawns chapels on completion

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Buildings // (task-062 Q-47 — was singular)
{
    /// <summary>
    /// Seats the Temple's chapels from the faction's adoption state.
    ///
    /// Adoption is instant (docs/Design/Religion.md §3, 2026-10-15): the
    /// SectAdopt command stamps SectAdoptionState on the bank, and this system
    /// mirrors every adopted sect onto a free Temple slot with its decorative
    /// chapel, on the next tick, in fixed sect order — so every lockstep peer
    /// seats the same sect in the same slot. The old chapel build timer
    /// (slot State 1) is gone.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TempleChapelBuildSystem : ISystem
    {
        private struct DeferredChapelSpawn
        {
            public Entity Temple;
            public int SlotIndex;
            public FixedString64Bytes SectId;
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TempleTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ReseatAdoptedChapels(ref state, state.EntityManager);
        }

        /// <summary>
        /// A LOST TEMPLE KEEPS ITS FAITH. Adoption, chapel levels and unlocked
        /// actives live on the faction bank (SectAdoptionState) and survive the
        /// Temple; only the chapel buildings die with it. When a faction stands
        /// a finished Temple again, every adopted sect that no slot holds is
        /// re-seated in the first empty slot, complete, with its chapel
        /// rebuilt - no RP, no resources, no build time, and the bank record
        /// untouched, so the levels and actives bought before come back as
        /// they were. Sect order is fixed (SectConfig.AllSectIds), so every
        /// lockstep peer seats the same sect in the same slot on the same tick.
        /// </summary>
        private void ReseatAdoptedChapels(ref SystemState state, EntityManager em)
        {
            var reseats = new NativeList<DeferredChapelSpawn>(0, Allocator.Temp);
            uint seenFactions = 0;

            foreach (var (fac, entity) in SystemAPI
                         .Query<RefRO<FactionTag>>()
                         .WithAll<TempleOfRidanTag, TempleChapelSlot>()
                         .WithNone<UnderConstruction>()
                         .WithEntityAccess())
            {
                var faction = fac.ValueRO.Value;
                int f = (int)faction;
                if (f < 0 || f > 31 || (seenFactions & (1u << f)) != 0) continue;
                seenFactions |= 1u << f;

                if (!FactionEconomy.TryGetBank(em, faction, out var bank)
                    || !em.HasComponent<SectAdoptionState>(bank)) continue;
                var adoption = em.GetComponentData<SectAdoptionState>(bank);
                if (adoption.AdoptedCount() == 0) continue;

                var slots = em.GetBuffer<TempleChapelSlot>(entity);
                for (int si = 0; si < SectConfig.AllSectIds.Length; si++)
                {
                    if (!adoption.Get(si).IsAdopted) continue;
                    var sectId = new FixedString64Bytes(SectConfig.AllSectIds[si]);

                    bool seated = false;
                    int empty = -1;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        if (slots[i].State != 0 && slots[i].SectId == sectId) { seated = true; break; }
                        if (empty < 0 && slots[i].State == 0) empty = i;
                    }
                    if (seated || empty < 0) continue;

                    var slot = slots[empty];
                    slot.SectId = sectId;
                    slot.State = 2;
                    slot.BuildTime = 1f;
                    slot.BuildProgress = 1f;
                    slot.Chapel = Entity.Null;
                    slot.ShardrootAllocated = 0;
                    slots[empty] = slot;
                    reseats.Add(new DeferredChapelSpawn { Temple = entity, SlotIndex = empty, SectId = sectId });
                }
            }

            for (int i = 0; i < reseats.Length; i++)
            {
                var r = reseats[i];
                if (!em.Exists(r.Temple)) continue;
                var faction = em.GetComponentData<FactionTag>(r.Temple).Value;
                float3 templePos = em.HasComponent<LocalTransform>(r.Temple)
                    ? em.GetComponentData<LocalTransform>(r.Temple).Position
                    : float3.zero;
                float3 chapelPos = templePos + ComputeSlotOffset(r.SlotIndex);
                Entity chapel = BuildingFactory.Create(em,
                    SectConfig.ChapelIdFor(r.SectId.ToString()), chapelPos, faction);
                if (em.HasComponent<LocalTransform>(chapel))
                {
                    float3 outward = chapelPos - templePos;
                    outward.y = 0f;
                    var lt = em.GetComponentData<LocalTransform>(chapel);
                    lt.Rotation = quaternion.LookRotationSafe(outward, math.up());
                    em.SetComponentData(chapel, lt);
                }
                var slots = em.GetBuffer<TempleChapelSlot>(r.Temple);
                if (r.SlotIndex >= 0 && r.SlotIndex < slots.Length)
                {
                    var slot = slots[r.SlotIndex];
                    slot.Chapel = chapel;
                    slots[r.SlotIndex] = slot;
                }
            }
            reseats.Dispose();
        }

        /// <summary>
        /// Convert a 0..5 slot index into a world-space offset around the temple.
        /// Delegates to TempleChapelRing so slots, site decals, and spawned
        /// chapels share the same heptagon-face-aligned ring (slot i = face
        /// i+1 of the 7-sided temple; face 0 is the door).
        /// </summary>
        private static float3 ComputeSlotOffset(int slotIndex)
            => TempleChapelRing.WorldOffset(slotIndex);
    }
}
