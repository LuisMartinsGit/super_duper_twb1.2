// TempleChapelBuildSystem.cs
// Ticks chapel build timers on temple slot buffers and spawns chapels on completion
// Location: Assets/Scripts/Systems/Work/TempleChapelBuildSystem.cs

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Building
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

            // Spawn chapel entities after iteration (structural changes are safe now)
            for (int i = 0; i < deferredSpawns.Length; i++)
            {
                var spawn = deferredSpawns[i];

                // Safety: temple entity may have been destroyed between iteration and deferred spawn
                if (!em.Exists(spawn.Temple)) continue;
                if (!em.HasBuffer<TempleChapelSlot>(spawn.Temple)) continue;

                var slots = em.GetBuffer<TempleChapelSlot>(spawn.Temple);
                if (spawn.SlotIndex >= slots.Length) continue;

                string sectId = spawn.SectId.ToString();
                var faction = em.GetComponentData<FactionTag>(spawn.Temple).Value;

                // Create chapel entity at the slot position
                Entity chapel = BuildingFactory.CreateChapelAtSlot(
                    em, sectId, spawn.Temple, spawn.SlotIndex, faction);

                // Update the slot buffer to reference the created chapel entity
                // Re-fetch buffer in case CreateChapelAtSlot caused a structural change
                slots = em.GetBuffer<TempleChapelSlot>(spawn.Temple);
                var slot = slots[spawn.SlotIndex];
                slot.Chapel = chapel;
                slots[spawn.SlotIndex] = slot;

                // Grant +1 RP bonus for shrine construction
                GrantShrineRPBonus(em, faction);

                UnityEngine.Debug.Log(
                    $"[TempleChapelBuild] Chapel '{sectId}' completed at slot {spawn.SlotIndex} for {faction}");
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
                    UnityEngine.Debug.Log(
                        $"[TempleChapelBuild] {faction} granted +{TempleLevelConfig.ShrineBonus} RP for chapel construction");
                }
            }
        }
    }
}
