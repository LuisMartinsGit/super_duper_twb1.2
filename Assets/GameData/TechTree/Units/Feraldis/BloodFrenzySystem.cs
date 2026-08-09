// File: Assets/GameData/TechTree/Units/Feraldis/BloodFrenzySystem.cs
// Feraldis culture signature: units fighting on bloodsoaked ground frenzy.
// Canon: docs/Design/Age_1_Feraldis.md — "Blood, Frenzy & War Totems".
//
// BloodMap is a managed 128x128 grid on the main thread, so this is a
// SystemBase on a slow pulse rather than a per-frame Burst job. The buff
// LINGERS longer than the pulse, so a unit standing in a pool never gaps:
//   * every frame  -> tick BloodFrenzy.Remaining down, drop it at zero
//   * every pulse  -> re-stamp Remaining for anyone over blood
//
// Combat reads the component, never the map: MeleeCombatSystem and
// RangedCombatSystem call CombatDamageHelper.GetFrenzyDamageMult /
// GetFrenzyCooldownMult, so the hot path stays a component lookup.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Influence;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(MeleeCombatSystem))]
    public partial class BloodFrenzySystem : SystemBase
    {
        private float _scanAcc;

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;

            // --- Expiry: tick every frame so the buff fades smoothly. ---
            var expired = new NativeList<Entity>(Allocator.Temp);
            foreach (var (frenzy, entity) in SystemAPI
                .Query<RefRW<BloodFrenzy>>()
                .WithEntityAccess())
            {
                frenzy.ValueRW.Remaining -= dt;
                if (frenzy.ValueRO.Remaining <= 0f) expired.Add(entity);
            }
            for (int i = 0; i < expired.Length; i++)
                EntityManager.RemoveComponent<BloodFrenzy>(expired[i]);
            expired.Dispose();

            // --- Acquisition: slow pulse over the blood map. ---
            _scanAcc += dt;
            if (_scanAcc < FrenzyScanInterval) return;
            _scanAcc = 0f;

            if (!BloodMap.Ready) return;
            if (!BloodMap.HasPresence(FrenzyBloodThreshold)) return;

            var gained = new NativeList<Entity>(Allocator.Temp);
            foreach (var (transform, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<FeraldisUnitTag, UnitTag>()
                .WithNone<DeathAnimationState>()
                .WithEntityAccess())
            {
                // The tag alone is not enough. The Fiendstone Keep is an
                // Age 0 building available to EVERY culture, and its
                // miner-conversion produces real Berserkers — so an Alanthor
                // or Runai player could otherwise field units carrying the
                // Feraldis culture signature. Frenzy is Feraldis-only.
                if (CultureConfig.GetCompletedCulture(EntityManager, faction.ValueRO.Value)
                    != Cultures.Feraldis) continue;

                var p = transform.ValueRO.Position;
                if (BloodMap.SampleWorld(p.x, p.z) < FrenzyBloodThreshold) continue;

                if (SystemAPI.HasComponent<BloodFrenzy>(entity))
                {
                    SystemAPI.SetComponent(entity, new BloodFrenzy
                    {
                        Remaining = FrenzyLingerSeconds
                    });
                }
                else
                {
                    gained.Add(entity);
                }
            }

            for (int i = 0; i < gained.Length; i++)
            {
                EntityManager.AddComponentData(gained[i], new BloodFrenzy
                {
                    Remaining = FrenzyLingerSeconds
                });
            }
            gained.Dispose();
        }
    }
}
