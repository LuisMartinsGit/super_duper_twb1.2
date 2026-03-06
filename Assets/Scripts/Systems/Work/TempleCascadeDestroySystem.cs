// TempleCascadeDestroySystem.cs
// Cascades temple destruction to its chapel slots + cleans up slots when chapels die
// Location: Assets/Scripts/Systems/Work/TempleCascadeDestroySystem.cs

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Building
{
    /// <summary>
    /// Handles two cascading destruction scenarios for temple-chapel relationships:
    ///
    /// 1. Temple dies → all linked chapels die (cascade destruction)
    ///    - Queries temples with Health <= 0
    ///    - For each slot with a valid Chapel entity, sets its Health to 0
    ///    - DeathSystem handles the actual entity cleanup on the next frame
    ///
    /// 2. Chapel dies → parent temple's slot is cleared (slot cleanup)
    ///    - Queries chapel entities with TempleOwner and Health <= 0
    ///    - Clears the corresponding slot in the parent temple's buffer
    ///    - Slot returns to State=0 (empty) so a new chapel can be built there
    ///
    /// Runs BEFORE DeathSystem so we can read the dying entities' components
    /// before they are destroyed.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct TempleCascadeDestroySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // Run when there are temples or chapels with TempleOwner
            state.RequireForUpdate<TempleTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // ========== Part 1: Temple dies → cascade kill all chapels ==========
            foreach (var (health, entity) in SystemAPI
                         .Query<RefRO<Health>>()
                         .WithAll<TempleTag, TempleChapelSlot>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                // Temple is dying — kill all chapel entities in its slots
                var slots = em.GetBuffer<TempleChapelSlot>(entity);
                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if (slot.Chapel == Entity.Null) continue;
                    if (slot.State == 0) continue;

                    // Check if chapel entity still exists and is alive
                    if (em.Exists(slot.Chapel) && em.HasComponent<Health>(slot.Chapel))
                    {
                        var chapelHealth = em.GetComponentData<Health>(slot.Chapel);
                        if (chapelHealth.Value > 0)
                        {
                            // Set HP to 0 — DeathSystem will clean it up
                            chapelHealth.Value = 0;
                            em.SetComponentData(slot.Chapel, chapelHealth);
                        }
                    }

                    // Clear the slot (the temple is dying anyway, but keep data consistent)
                    slot.State = 0;
                    slot.Chapel = Entity.Null;
                    slot.SectId = default;
                    slot.BuildProgress = 0f;
                    slot.BuildTime = 0f;
                    slots[i] = slot;
                }

                UnityEngine.Debug.Log($"[TempleCascade] Temple {entity.Index} dying — cascade destroyed all chapels");
            }

            // ========== Part 2: Chapel dies → clear parent temple slot ==========
            foreach (var (health, owner, entity) in SystemAPI
                         .Query<RefRO<Health>, RefRO<TempleOwner>>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                // Chapel is dying — clear its slot in the parent temple
                var temple = owner.ValueRO.Temple;
                int slotIdx = owner.ValueRO.SlotIndex;

                if (temple == Entity.Null || !em.Exists(temple)) continue;
                if (!em.HasBuffer<TempleChapelSlot>(temple)) continue;

                var slots = em.GetBuffer<TempleChapelSlot>(temple);
                if (slotIdx < 0 || slotIdx >= slots.Length) continue;

                var slot = slots[slotIdx];
                // Only clear if this slot still references this chapel
                if (slot.Chapel == entity)
                {
                    slot.State = 0;
                    slot.Chapel = Entity.Null;
                    slot.SectId = default;
                    slot.BuildProgress = 0f;
                    slot.BuildTime = 0f;
                    slots[slotIdx] = slot;

                    UnityEngine.Debug.Log(
                        $"[TempleCascade] Chapel {entity.Index} dying — cleared slot {slotIdx} in temple {temple.Index}");
                }
            }
        }
    }
}
