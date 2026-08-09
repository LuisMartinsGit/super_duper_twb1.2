// GlowWeaponDropSystem.cs
// Implements spec §4.5: only Glow-tier equipment drops on death, with a
// pickup window + attunement claim. Earlier tiers do not drop.
//
// Two phases, both in this system:
//   - Drop: a unit with effective Glow tier dies → spawn a GlowWeapon at
//           its death position, then strip the tier from the unit so the
//           drop only fires once. Border units never drop.
//   - Attune + claim: each tick, find qualifying units (Veilsteel-or-Glow
//           tier) standing within radius of a dropped weapon. First valid
//           unit becomes the Attuner; their AttunementProgress accumulates
//           uninterrupted. On reaching GlowWeaponAttunementTime, the unit
//           gets UnitTierOverride.Glow (EquipmentTierSystem stamps stats
//           on the next tick) and the weapon entity is destroyed.
//
// Runs UpdateBefore(DeathSystem) so we can read the dying unit's tier
// before the entity is destroyed.
//
// Location: Assets/Scripts/Systems/Combat/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class GlowWeaponDropSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;

            // ── Phase 1: drops ─────────────────────────────────────────
            // Collect during iteration, apply structural changes after.
            var dropPositions = new NativeList<float3>(2, Allocator.Temp);
            var dropClasses = new NativeList<UnitClass>(2, Allocator.Temp);
            var dropEntities = new NativeList<Entity>(2, Allocator.Temp);

            foreach (var (applied, transform, unitTag, faction, health, entity) in SystemAPI
                .Query<RefRO<UnitEquipmentApplied>, RefRO<LocalTransform>, RefRO<UnitTag>,
                       RefRO<FactionTag>, RefRO<Health>>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;
                if (applied.ValueRO.Value != EquipmentTier.Glow) continue;
                if (faction.ValueRO.Value == Faction.Border) continue;

                dropPositions.Add(transform.ValueRO.Position);
                dropClasses.Add(unitTag.ValueRO.Class);
                dropEntities.Add(entity);
            }

            for (int i = 0; i < dropPositions.Length; i++)
            {
                GlowWeapon.Create(em, dropPositions[i], dropClasses[i]);
                // Strip the tier so the same entity doesn't drop again if it
                // lingers another frame at 0 HP before DeathSystem runs.
                em.SetComponentData(dropEntities[i],
                    new UnitEquipmentApplied { Value = EquipmentTier.Base });
                if (em.HasComponent<UnitTierOverride>(dropEntities[i]))
                    em.RemoveComponent<UnitTierOverride>(dropEntities[i]);
                TWBLog.Log($"[GlowWeapon] {dropClasses[i]} dropped a Glow weapon");
            }

            dropPositions.Dispose();
            dropClasses.Dispose();
            dropEntities.Dispose();

            // ── Phase 2: attunement tick + claim ───────────────────────
            // Snapshot units once so we don't re-query for every weapon.
            var unitQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<UnitEquipmentApplied>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<FactionTag>());
            using var unitEnts = unitQuery.ToEntityArray(Allocator.Temp);
            using var unitTags = unitQuery.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var unitApplied = unitQuery.ToComponentDataArray<UnitEquipmentApplied>(Allocator.Temp);
            using var unitTransforms = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var unitHealths = unitQuery.ToComponentDataArray<Health>(Allocator.Temp);
            using var unitFactions = unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var claimedWeapons = new NativeList<Entity>(2, Allocator.Temp);
            var claimers = new NativeList<Entity>(2, Allocator.Temp);
            var expiredWeapons = new NativeList<Entity>(2, Allocator.Temp);

            foreach (var (stateRW, weaponTransform, weaponEntity) in SystemAPI
                .Query<RefRW<GlowWeaponState>, RefRO<LocalTransform>>()
                .WithAll<GlowWeaponTag>()
                .WithEntityAccess())
            {
                ref var state = ref stateRW.ValueRW;
                state.TimeRemaining -= dt;
                if (state.TimeRemaining <= 0f)
                {
                    expiredWeapons.Add(weaponEntity);
                    continue;
                }

                var weaponPos = weaponTransform.ValueRO.Position;

                // Validate current attuner — still alive, still in range, still qualifying?
                bool attunerValid = false;
                if (state.Attuner != Entity.Null && em.Exists(state.Attuner))
                {
                    if (em.HasComponent<Health>(state.Attuner)
                        && em.GetComponentData<Health>(state.Attuner).Value > 0
                        && em.HasComponent<LocalTransform>(state.Attuner))
                    {
                        var aPos = em.GetComponentData<LocalTransform>(state.Attuner).Position;
                        float dxz = math.distance(
                            new float2(aPos.x, aPos.z),
                            new float2(weaponPos.x, weaponPos.z));
                        if (dxz <= GlowWeaponClaimRadius
                            && em.HasComponent<UnitEquipmentApplied>(state.Attuner)
                            && IsQualifyingTier(em.GetComponentData<UnitEquipmentApplied>(state.Attuner).Value))
                        {
                            attunerValid = true;
                        }
                    }
                }
                if (!attunerValid)
                {
                    state.Attuner = Entity.Null;
                    state.AttunementProgress = 0f;
                }

                // Find a new attuner if none. First qualifier in the snapshot wins.
                if (state.Attuner == Entity.Null)
                {
                    for (int i = 0; i < unitEnts.Length; i++)
                    {
                        if (unitHealths[i].Value <= 0) continue;
                        if (unitFactions[i].Value == Faction.Border) continue;
                        if (!IsQualifyingTier(unitApplied[i].Value)) continue;

                        var uPos = unitTransforms[i].Position;
                        float dxz = math.distance(
                            new float2(uPos.x, uPos.z),
                            new float2(weaponPos.x, weaponPos.z));
                        if (dxz > GlowWeaponClaimRadius) continue;

                        state.Attuner = unitEnts[i];
                        state.AttunementProgress = 0f;
                        TWBLog.Log($"[GlowWeapon] {unitFactions[i].Value} unit begins attuning");
                        break;
                    }
                }

                // Tick attunement.
                if (state.Attuner != Entity.Null)
                {
                    state.AttunementProgress += dt;
                    if (state.AttunementProgress >= GlowWeaponAttunementTime)
                    {
                        claimedWeapons.Add(weaponEntity);
                        claimers.Add(state.Attuner);
                    }
                }
            }

            // Apply expirations + claims after the loop.
            for (int i = 0; i < expiredWeapons.Length; i++)
            {
                TWBLog.Log($"[GlowWeapon] pickup despawned (timeout)");
                em.DestroyEntity(expiredWeapons[i]);
            }

            for (int i = 0; i < claimedWeapons.Length; i++)
            {
                Entity weapon = claimedWeapons[i];
                Entity claimer = claimers[i];
                if (em.Exists(claimer))
                {
                    var ovr = new UnitTierOverride { Value = EquipmentTier.Glow };
                    if (em.HasComponent<UnitTierOverride>(claimer))
                        em.SetComponentData(claimer, ovr);
                    else
                        em.AddComponentData(claimer, ovr);
                    TWBLog.Log($"[GlowWeapon] claimed — unit upgraded to Glow tier");
                }
                if (em.Exists(weapon))
                    em.DestroyEntity(weapon);
            }

            claimedWeapons.Dispose();
            claimers.Dispose();
            expiredWeapons.Dispose();
        }

        /// <summary>
        /// Spec §4.5: claiming a Glow weapon requires being one tier below
        /// (Veilsteel-equipped). Already-Glow can also claim (they just take
        /// the value to deny it).
        /// </summary>
        private static bool IsQualifyingTier(EquipmentTier tier) =>
            tier == EquipmentTier.Veilsteel || tier == EquipmentTier.Glow;
    }
}
