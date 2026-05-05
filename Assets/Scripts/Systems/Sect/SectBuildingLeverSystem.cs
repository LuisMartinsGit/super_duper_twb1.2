// SectBuildingLeverSystem.cs
// Generic chapel-aura system for the Building lever (task-063 phase 5).
//
// Each chapel scans for nearby allied units once per frame and refreshes
// a SpellBuff (or HP regen) on each, with the aura parameters drawn from
// SectLeverEffects.AuraOf(sectId). The aura magnitudes scale with the
// faction's Building lever level via SectLeverEffects.LevelScalar.
//
// SpellBuff is refreshed every frame with TimeRemaining = 0.5s, so units
// stepping out of the aura lose the buff within half a second
// (SpellBuffSystem ticks the field down). The HP regen is applied
// directly inline, capped at MaxHP.
//
// task-063 phase 5.
//
// Location: Assets/Scripts/Systems/Sect/SectBuildingLeverSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectBuildingLeverSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChapelTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Snapshot all chapels and their auras once per tick.
            var chapelPositions = new NativeList<float3>(Allocator.Temp);
            var chapelFactions  = new NativeList<Faction>(Allocator.Temp);
            var chapelAuras     = new NativeList<SectAuraSpec>(Allocator.Temp);

            foreach (var (chapel, transform, faction, entity) in SystemAPI
                .Query<RefRO<ChapelTag>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                string sectId = chapel.ValueRO.SectId.ToString();
                byte level = SectQuery.LevelOf(em, faction.ValueRO.Value,
                    sectId, SectLeverKind.Building);
                if (level == 0) continue;

                var aura = SectLeverEffects.AuraOf(sectId);
                if (aura.Radius <= 0f) continue;

                float scalar = SectLeverEffects.LevelScalar(level);
                aura.DamageMultiplier = aura.DamageMultiplier > 1f
                    ? 1f + (aura.DamageMultiplier - 1f) * scalar : aura.DamageMultiplier;
                aura.SpeedMultiplier = aura.SpeedMultiplier > 1f
                    ? 1f + (aura.SpeedMultiplier - 1f) * scalar : aura.SpeedMultiplier;
                aura.ArmorBonus = (int)(aura.ArmorBonus * scalar);
                aura.DamageReflect = aura.DamageReflect * scalar;
                aura.HpRegenPerSecond = (int)(aura.HpRegenPerSecond * scalar);

                chapelPositions.Add(transform.ValueRO.Position);
                chapelFactions.Add(faction.ValueRO.Value);
                chapelAuras.Add(aura);
            }

            if (chapelPositions.Length == 0)
            {
                chapelPositions.Dispose();
                chapelFactions.Dispose();
                chapelAuras.Dispose();
                return;
            }

            // For each ally unit in range of any of its faction's chapels,
            // refresh SpellBuff with the merged max-of-fields semantics.
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (faction, transform, health, entity) in SystemAPI
                .Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRW<Health>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                Faction unitFaction = faction.ValueRO.Value;
                float3 unitPos = transform.ValueRO.Position;

                // Combine all in-range chapel auras into a single best-of buff.
                SectAuraSpec best = default;
                bool any = false;
                for (int i = 0; i < chapelPositions.Length; i++)
                {
                    if (chapelFactions[i] != unitFaction) continue;
                    var ca = chapelAuras[i];
                    if (math.distance(unitPos, chapelPositions[i]) > ca.Radius) continue;
                    any = true;
                    if (ca.DamageMultiplier > best.DamageMultiplier) best.DamageMultiplier = ca.DamageMultiplier;
                    if (ca.ArmorBonus > best.ArmorBonus)             best.ArmorBonus       = ca.ArmorBonus;
                    if (ca.SpeedMultiplier > best.SpeedMultiplier)   best.SpeedMultiplier  = ca.SpeedMultiplier;
                    if (ca.DamageReflect > best.DamageReflect)       best.DamageReflect    = ca.DamageReflect;
                    if (ca.HpRegenPerSecond > best.HpRegenPerSecond) best.HpRegenPerSecond = ca.HpRegenPerSecond;
                }
                if (!any) continue;

                // Refresh SpellBuff (MergeSpellBuff takes max of every field).
                if (best.DamageMultiplier > 0f
                    || best.ArmorBonus > 0
                    || best.SpeedMultiplier > 0f
                    || best.DamageReflect > 0f)
                {
                    var incoming = new SpellBuff
                    {
                        DamageMultiplier = best.DamageMultiplier,
                        ArmorBonus       = best.ArmorBonus,
                        SpeedMultiplier  = best.SpeedMultiplier,
                        DamageReflect    = best.DamageReflect,
                        TimeRemaining    = 0.5f, // refreshed every frame
                    };
                    TheWaningBorder.Systems.Combat.CombatDamageHelper
                        .MergeSpellBuff(em, ecb, entity, incoming);
                }

                // Inline HP regen — bounded by MaxHP, applied per dt.
                if (best.HpRegenPerSecond > 0
                    && health.ValueRO.Value > 0
                    && health.ValueRO.Value < health.ValueRO.Max)
                {
                    int delta = (int)math.ceil(best.HpRegenPerSecond * dt);
                    if (delta < 1) delta = 1;
                    int newHp = math.min(health.ValueRO.Max, health.ValueRO.Value + delta);
                    health.ValueRW.Value = newHp;
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
            chapelPositions.Dispose();
            chapelFactions.Dispose();
            chapelAuras.Dispose();
        }
    }
}
