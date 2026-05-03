// TempleChapelBuildSystem.cs
// Ticks chapel build timers on temple slot buffers and spawns chapels on completion
// Location: Assets/Scripts/Systems/Work/TempleChapelBuildSystem.cs

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Buildings // (task-062 Q-47 — was singular)
{
    /// <summary>
    /// Processes chapel build timers on Temple of Ridan entities.
    ///
    /// Each temple has a TempleChapelSlot buffer with 7 elements.
    /// When a slot has State=1 (building), this system increments BuildProgress by deltaTime.
    /// When BuildProgress >= BuildTime, the chapel entity is spawned at the slot position
    /// and State changes to 2 (complete).
    ///
    /// Chapel creation is deferred: we collect slot completions during iteration,
    /// then create chapel entities after the query loop to avoid structural changes.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TempleChapelBuildSystem : ISystem
    {
        /// <summary>Holds data for a deferred chapel spawn after slot build completes.</summary>
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
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Collect deferred spawns — chapel creation involves structural changes
            var deferredSpawns = new NativeList<DeferredChapelSpawn>(4, Allocator.Temp);

            // Process all temple entities with chapel slot buffers
            foreach (var (templeTag, entity) in SystemAPI
                         .Query<RefRO<TempleTag>>()
                         .WithAll<TempleChapelSlot>()
                         .WithNone<UnderConstruction>()
                         .WithEntityAccess())
            {
                var slots = em.GetBuffer<TempleChapelSlot>(entity);

                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];

                    // Only tick slots that are currently building
                    if (slot.State != 1) continue;

                    slot.BuildProgress += dt;

                    if (slot.BuildProgress >= slot.BuildTime)
                    {
                        // Build complete — defer chapel creation
                        deferredSpawns.Add(new DeferredChapelSpawn
                        {
                            Temple = entity,
                            SlotIndex = i,
                            SectId = slot.SectId
                        });

                        // Update slot state to complete (Chapel entity assigned after spawn)
                        slot.State = 2;
                        slot.BuildProgress = slot.BuildTime;
                    }

                    // Write back the modified slot
                    slots[i] = slot;
                }
            }

            // Process completed slots — chapels are NOT standalone entities anymore,
            // they are part of the temple. Just log and grant RP.
            for (int i = 0; i < deferredSpawns.Length; i++)
            {
                var spawn = deferredSpawns[i];

                if (!em.Exists(spawn.Temple)) continue;
                if (!em.HasBuffer<TempleChapelSlot>(spawn.Temple)) continue;

                var faction = em.GetComponentData<FactionTag>(spawn.Temple).Value;

                // Grant +1 RP bonus for chapel construction
                GrantShrineRPBonus(em, faction);

                // Recalculate sect passives since a new chapel is complete
                SectEffectSystem.Instance?.RecalculateAllPassives(faction);

            }

            deferredSpawns.Dispose();
        }

        /// <summary>
        /// Grant +1 Religion Point when a chapel completes construction.
        /// Mirrors the logic in BuildingConstructionSystem.GrantShrineRPBonus.
        /// </summary>
        private void GrantShrineRPBonus(EntityManager em, Faction faction)
        {
            if (FactionEconomy.TryGetBank(em, faction, out var bank))
            {
                if (em.HasComponent<ReligionPoints>(bank))
                {
                    var rp = em.GetComponentData<ReligionPoints>(bank);
                    rp.Value += TempleLevelConfig.ShrineBonus;
                    em.SetComponentData(bank, rp);
                }
            }
        }
    }
}
