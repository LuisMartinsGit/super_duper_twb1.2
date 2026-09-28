// SectFortitudeHpSystem.cs
// Implements Fortitude's Lv I "Veiled Stone" passive (walls/towers +12% HP).
// Mirrors SectWitnessVisionSystem's stamp-and-apply pattern: scans walls
// (WallTag) and towers (WatchTowerTag, TotemTowerTag, WallTowerTag) of
// Fortitude-adopted factions that don't yet carry FortitudeHpApplied, and
// multiplies both Health.Max and Health.Value by the Lv I factor (×1.12).
//
// Tower range +0.5 (the second half of the Lv I lever) is deferred — tower
// fire range is read in BuildingCombatSystem and will need a per-faction
// scalar. Phase 4 / Phase 5.
//
// task-063 phase 2d.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectFortitudeHpSystem : ISystem
    {
        // Per-level HP multiplier. Phase 4 lever upgrades read AppliedLevel
        // and apply the diff (factorAt(new) / factorAt(old)) — see below.
        public static float MultiplierFor(byte level) => level switch
        {
            2 => 1.25f,
            3 => 1.40f,
            _ => 1.12f,
        };

        // Per-faction Passive level as of the LAST update, indexed by the
        // Faction byte (2026-09-25 perf pass). A level only has to be looked
        // up once per update for each faction — it was looked up once per
        // BUILDING, and each lookup walked every Temple in the world.
        private NativeArray<byte> _lastLevel;
        /// <summary>Factions that have owned a building this system saw;
        /// their level is polled every update so a rise is never missed.</summary>
        private NativeArray<byte> _seen;
        private byte _primed;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingTag>();
            _lastLevel = new NativeArray<byte>(256, Allocator.Persistent);
            _seen = new NativeArray<byte>(256, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_lastLevel.IsCreated) _lastLevel.Dispose();
            if (_seen.IsCreated) _seen.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // This update's level per faction, resolved lazily (at most one
            // SectQuery per faction). 255 = not resolved yet.
            var level = new NativeArray<byte>(256, Allocator.Temp);
            for (int i = 0; i < 256; i++) level[i] = 255;

            bool anyChanged = _primed == 0;
            for (int f = 0; f < 256; f++)
            {
                if (_seen[f] == 0) continue;
                if (Resolve(em, level, (Faction)f) != _lastLevel[f]) anyChanged = true;
            }

            // Pass 1: brand-new walls/towers that haven't received any HP bonus yet.
            // Normally only CHANGED chunks are walked (a new building, an
            // owner change, construction finishing: each marks its chunk).
            // An entity skipped at level 0 is never stamped, so whenever any
            // faction's level moves the whole set is walked again — which is
            // exactly when one of them can newly qualify. The old loop walked
            // every unstamped building (every non-wall one, forever) each tick.
            var pass1 = SystemAPI.QueryBuilder()
                .WithAll<Health, FactionTag, BuildingTag>()
                .WithNone<FortitudeHpApplied>()
                .Build();
            if (!anyChanged) pass1.SetChangedVersionFilter(ComponentType.ReadOnly<FactionTag>());
            var p1Entities = pass1.ToEntityArray(Allocator.Temp);
            var p1Factions = pass1.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var p1Health = pass1.ToComponentDataArray<Health>(Allocator.Temp);
            pass1.ResetFilter();

            var pendingNewStamps = new NativeList<Entity>(8, Allocator.Temp);
            var pendingNewLevels = new NativeList<byte>(8, Allocator.Temp);
            var pendingNewHp = new NativeList<Health>(8, Allocator.Temp);

            for (int i = 0; i < p1Entities.Length; i++)
            {
                var entity = p1Entities[i];
                // Only apply to walls + towers — the Lv I lever specifies them.
                bool isWallOrTower =
                    em.HasComponent<WallTag>(entity)
                 || em.HasComponent<WallHubTag>(entity)
                 || em.HasComponent<WallInstanceTag>(entity)
                 || em.HasComponent<WallTowerTag>(entity)
                 || em.HasComponent<WatchTowerTag>(entity)
                 || em.HasComponent<TotemTowerTag>(entity);
                if (!isWallOrTower) continue;

                byte lv = Resolve(em, level, p1Factions[i].Value);
                if (lv == 0) continue;

                float mult = MultiplierFor(lv);
                var hp = p1Health[i];
                hp.Max   = (int)(hp.Max   * mult);
                hp.Value = (int)(hp.Value * mult);

                pendingNewStamps.Add(entity);
                pendingNewLevels.Add(lv);
                pendingNewHp.Add(hp);
            }
            p1Entities.Dispose();
            p1Factions.Dispose();
            p1Health.Dispose();

            // Writes after the walk, and only for what changed: RefRW in the
            // loop bumped the Health change version of every building chunk
            // every tick, waking every Health change filter for nothing.
            for (int i = 0; i < pendingNewStamps.Length; i++)
            {
                if (!em.Exists(pendingNewStamps[i])) continue;
                em.SetComponentData(pendingNewStamps[i], pendingNewHp[i]);
                em.AddComponentData(pendingNewStamps[i],
                    new FortitudeHpApplied { AppliedLevel = pendingNewLevels[i] });
            }
            pendingNewStamps.Dispose();
            pendingNewLevels.Dispose();
            pendingNewHp.Dispose();

            // Pass 2 can only do something when a level ROSE (or on the very
            // first update, which has no baseline).
            bool anyIncreased = _primed == 0;
            for (int f = 0; f < 256 && !anyIncreased; f++)
            {
                byte now = level[f];
                if (now != 255 && now > _lastLevel[f]) anyIncreased = true;
            }

            // Pass 2: already-stamped buildings whose faction's lever level
            // has since increased. Apply the diff factor to bring them up
            // to date and bump AppliedLevel. (task-063 phase 4)
            if (anyIncreased)
            {
                var updEntities = new NativeList<Entity>(8, Allocator.Temp);
                var updHp = new NativeList<Health>(8, Allocator.Temp);
                var updLevel = new NativeList<byte>(8, Allocator.Temp);

                foreach (var (health, faction, applied, entity) in SystemAPI
                    .Query<RefRO<Health>, RefRO<FactionTag>, RefRO<FortitudeHpApplied>>()
                    .WithAll<BuildingTag>()
                    .WithEntityAccess())
                {
                    byte currentLevel = Resolve(em, level, faction.ValueRO.Value);
                    byte appliedLevel = applied.ValueRO.AppliedLevel;
                    if (currentLevel <= appliedLevel) continue;

                    float diffMult = MultiplierFor(currentLevel) / MultiplierFor(appliedLevel);
                    var hp = health.ValueRO;
                    hp.Max   = (int)(hp.Max   * diffMult);
                    hp.Value = (int)(hp.Value * diffMult);
                    updEntities.Add(entity);
                    updHp.Add(hp);
                    updLevel.Add(currentLevel);
                }

                for (int i = 0; i < updEntities.Length; i++)
                {
                    em.SetComponentData(updEntities[i], updHp[i]);
                    em.SetComponentData(updEntities[i],
                        new FortitudeHpApplied { AppliedLevel = updLevel[i] });
                }
                updEntities.Dispose();
                updHp.Dispose();
                updLevel.Dispose();
            }

            // Remember every level resolved this update, and who owns buildings.
            for (int f = 0; f < 256; f++)
            {
                if (level[f] == 255) continue;
                _lastLevel[f] = level[f];
                _seen[f] = 1;
            }
            _primed = 1;
            level.Dispose();
        }

        private static byte Resolve(EntityManager em, NativeArray<byte> cache, Faction faction)
        {
            int f = (int)faction;
            byte v = cache[f];
            if (v != 255) return v;
            v = SectQuery.LevelOf(em, faction, SectConfig.Fortitude, SectLeverKind.Passive);
            cache[f] = v;
            return v;
        }
    }
}
