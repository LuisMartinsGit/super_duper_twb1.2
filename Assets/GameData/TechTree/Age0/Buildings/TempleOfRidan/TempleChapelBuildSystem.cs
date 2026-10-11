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

            // Process completed slots — task-063 phase 2a wiring:
            //   1. Spawn the corresponding chapel building entity at the slot
            //      position (BuildingFactory dispatches Chapel_Sect_<id> through
            //      its uniform CreateChapel path).
            //   2. Stash the new chapel entity into the slot record so the UI
            //      can show "this slot is occupied by chapel X".
            //   3. Call SectAdoption.OnChapelCompleted to deduct adoption RP,
            //      mark the sect adopted (PerSectState.AdoptedAtAge), and fire
            //      the public SectAdopted event.
            //
            // Failure modes — if the OnChapelCompleted call returns non-Ok (e.g.
            // not enough RP, sect already adopted, slots full), the chapel
            // entity is destroyed and the slot is reset. This shouldn't happen
            // in practice because the UI gates the chapel-build button via
            // SectAdoption.CanAdopt, but the guard avoids leaving an orphan
            // chapel pinned to a slot we couldn't credit.
            for (int i = 0; i < deferredSpawns.Length; i++)
            {
                var spawn = deferredSpawns[i];
                if (!em.Exists(spawn.Temple)) continue;
                if (!em.HasBuffer<TempleChapelSlot>(spawn.Temple)) continue;
                if (!em.HasComponent<FactionTag>(spawn.Temple)) continue;

                var faction = em.GetComponentData<FactionTag>(spawn.Temple).Value;
                string sectId = spawn.SectId.ToString();
                if (string.IsNullOrEmpty(sectId)) continue;
                if (!SectConfig.IsKnownSect(sectId)) continue;

                // Compute the chapel's world position from the slot offset.
                float3 templePos = em.HasComponent<LocalTransform>(spawn.Temple)
                    ? em.GetComponentData<LocalTransform>(spawn.Temple).Position
                    : float3.zero;
                float3 chapelPos = templePos + ComputeSlotOffset(spawn.SlotIndex);

                // Spawn the chapel entity through BuildingFactory. BFME2
                // docking: the chapel's BACK sits flush against its temple
                // face, so the door (+Z) faces OUTWARD, away from the temple.
                string chapelId = SectConfig.ChapelIdFor(sectId);
                Entity chapelEntity = BuildingFactory.Create(em, chapelId, chapelPos, faction);
                if (em.HasComponent<LocalTransform>(chapelEntity))
                {
                    float3 outward = chapelPos - templePos;
                    outward.y = 0f;
                    var lt = em.GetComponentData<LocalTransform>(chapelEntity);
                    lt.Rotation = quaternion.LookRotationSafe(outward, math.up());
                    em.SetComponentData(chapelEntity, lt);
                }

                // Credit the sect (deducts adoption RP, fires SectAdopted event).
                var result = SectAdoption.OnChapelCompleted(em, faction, chapelId);
                if (result != SectAdoptionResult.Ok)
                {
                    // Roll back — destroy the chapel entity so we don't leave an
                    // orphan that doesn't correspond to an adopted sect.
                    if (em.Exists(chapelEntity))
                        em.DestroyEntity(chapelEntity);
                    UnityEngine.Debug.LogWarning(
                        $"[TempleChapelBuildSystem] Chapel completion for sect '{sectId}' failed: {result}");
                    continue;
                }

                // Stash the chapel entity into the slot record so the UI can
                // resolve slot → chapel without a separate query.
                var slots = em.GetBuffer<TempleChapelSlot>(spawn.Temple);
                if (spawn.SlotIndex >= 0 && spawn.SlotIndex < slots.Length)
                {
                    var slot = slots[spawn.SlotIndex];
                    slot.Chapel = chapelEntity;
                    slots[spawn.SlotIndex] = slot;
                }
            }

            deferredSpawns.Dispose();

            ReseatAdoptedChapels(ref state, em);
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
