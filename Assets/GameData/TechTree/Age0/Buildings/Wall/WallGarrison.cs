// WallGarrison.cs
// Putting men inside a reinforced curtain module and taking them out again
// (docs/Design/Age_1_Alanthor.md § Garrison slots). Level 3 only: two slots
// per module, foot units only.
//
// A garrisoned unit is ABSORBED — disabled and its view dropped — rather
// than parked on the crown. A unit standing on a 2.6 m curtain is at the
// mercy of steering, avoidance and every system that writes a unit's Y; an
// absorbed one cannot drift, cannot be pushed off and cannot fight the nav
// layers. What the player sees is the module's own visual: a manned shield
// position per occupant. It still costs population (PopulationSyncSystem
// counts disabled units for exactly this reason) and its damage is added to
// the module's own fire.
//
// THE WATCH TOWER uses the same machinery (2026-10-03, unification item 24):
// it carries `garrisonSlots` WallGarrisonSlots from its SO, so the same
// right-click, Empty button, lockstep command and orphan sweep serve it. Its
// rule differs in one place — the tower already shoots, so its men do not
// REPLACE its fire; each adds `garrisonArrowsPerOccupant` arrows (targets)
// to the tower's own volley (Age_1_Alanthor.md § Watch Tower: "Garrison
// slots / arrow-fire 4 / yes"). See RefreshTowerFire.
//
// 2026-10-20 (docs/Design/Age_1_Alanthor.md § Watch Tower):
//   * a garrison order WALKS the unit there (BeginApproach + GarrisonApproach,
//     driven by WallGarrisonApproachSystem); it goes in only within the
//     footprint plus WallGarrisonConfig.entryRange — no more entering from
//     across the map;
//   * a garrisoned unit leaves the selection and every overlay (the view is
//     dropped on entry; UnitIndicatorSystem / SelectionSystem skip disabled
//     units);
//   * when the building is destroyed by ANY path its men are EJECTED alive
//     around where it stood (Eject, from WallGarrisonSystem's orphan sweep)
//     — they used to die with it.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Entities
{
    public static class WallGarrison
    {
        static WallGarrisonConfig Cfg => WallGarrisonConfig.I;

        /// <summary>Reach of a manned wall, metres.</summary>
        public const float FireRange = 18f;
        /// <summary>Seconds between volleys from a manned wall.</summary>
        public const float FireCooldown = 2.0f;
        /// <summary>Line of sight a manned module sees at.</summary>
        public const float MannedLineOfSight = 14f;

        /// <summary>True when the module has a slot nobody is in.</summary>
        public static bool HasFreeSlot(EntityManager em, Entity module)
        {
            if (module == Entity.Null || !em.Exists(module)) return false;
            if (!em.HasBuffer<WallGarrisonSlot>(module)) return false;
            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Occupant == Entity.Null || !em.Exists(slots[i].Occupant)) return true;
            return false;
        }

        /// <summary>How many of the module's slots are filled.</summary>
        public static int OccupantCount(EntityManager em, Entity module)
        {
            if (module == Entity.Null || !em.Exists(module)) return 0;
            if (!em.HasBuffer<WallGarrisonSlot>(module)) return 0;
            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Occupant != Entity.Null && em.Exists(slots[i].Occupant)) n++;
            return n;
        }

        /// <summary>Total slots the module has (0 below level 3).</summary>
        public static int SlotCount(EntityManager em, Entity module)
            => module != Entity.Null && em.Exists(module) && em.HasBuffer<WallGarrisonSlot>(module)
               ? em.GetBuffer<WallGarrisonSlot>(module).Length : 0;

        /// <summary>
        /// Foot units only — infantry and archers. The same roster the
        /// wall-top order has always used: no cavalry, no siege, no workers,
        /// no scouts.
        /// </summary>
        public static bool IsFootUnit(EntityManager em, Entity unit)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return false;
            if (!em.HasComponent<UnitTag>(unit) || em.HasComponent<BuildingTag>(unit)) return false;
            if (em.HasComponent<CanBuild>(unit) || em.HasComponent<WorkerTag>(unit)) return false;
            if (em.HasComponent<CavalryTag>(unit)) return false;
            if (em.HasComponent<EmplacedEngineTag>(unit)) return false;
            // Heroes stay outside (Age_1_Alanthor.md § Watch Tower).
            if (em.HasComponent<HeroLevel>(unit)) return false;
            var cls = em.GetComponentData<UnitTag>(unit).Class;
            return cls != UnitClass.Siege && cls != UnitClass.Scout;
        }

        /// <summary>Every gate this order has to pass before a unit walks in.</summary>
        public static bool CanGarrison(EntityManager em, Entity unit, Entity module)
        {
            if (!IsFootUnit(em, unit)) return false;
            if (em.HasComponent<WallGarrisonedIn>(unit)) return false;
            if (!HasFreeSlot(em, module)) return false;
            if (em.HasComponent<UnderConstruction>(module)) return false;
            if (!em.HasComponent<FactionTag>(unit) || !em.HasComponent<FactionTag>(module)) return false;
            return em.GetComponentData<FactionTag>(unit).Value
                == em.GetComponentData<FactionTag>(module).Value;
        }

        // ═══════════════════════════════════════════════════════════════
        // WALKING THERE (2026-10-20)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Half the building's footprint, metres (BuildingSize is
        /// in metres; Radius for anything without one).</summary>
        public static float HalfExtent(EntityManager em, Entity building)
        {
            if (em.HasComponent<BuildingSize>(building))
            {
                var s = em.GetComponentData<BuildingSize>(building);
                return math.max(s.Width, s.Height) * 0.5f;
            }
            return em.HasComponent<Radius>(building) ? em.GetComponentData<Radius>(building).Value : 2f;
        }

        /// <summary>XZ distance from <paramref name="p"/> to the building's
        /// footprint square (0 inside it).</summary>
        public static float DistanceToFootprint(EntityManager em, Entity building, float3 p)
        {
            if (!em.HasComponent<LocalTransform>(building)) return float.MaxValue;
            var c = em.GetComponentData<LocalTransform>(building).Position;
            float h = HalfExtent(em, building);
            float dx = math.max(0f, math.abs(p.x - c.x) - h);
            float dz = math.max(0f, math.abs(p.z - c.z) - h);
            return math.sqrt(dx * dx + dz * dz);
        }

        /// <summary>True when <paramref name="unit"/> is close enough to go
        /// straight in (WallGarrisonConfig.entryRange past the footprint).</summary>
        public static bool InEntryRange(EntityManager em, Entity unit, Entity building)
            => em.HasComponent<LocalTransform>(unit)
               && DistanceToFootprint(em, building, em.GetComponentData<LocalTransform>(unit).Position)
                  <= Cfg.entryRange;

        /// <summary>
        /// The garrison ORDER's executor (every peer runs it off the wire):
        /// a unit within entry range goes in now; one further away walks to
        /// the footprint edge on its own side and goes in on arrival
        /// (<see cref="GarrisonApproach"/>, WallGarrisonApproachSystem).
        /// Structural: call outside query iteration.
        /// </summary>
        public static void BeginApproach(EntityManager em, Entity unit, Entity building)
        {
            if (!CanGarrison(em, unit, building)) return;
            if (InEntryRange(em, unit, building)) { Enter(em, unit, building); return; }

            float3 from = em.GetComponentData<LocalTransform>(unit).Position;
            float3 c = em.GetComponentData<LocalTransform>(building).Position;
            float h = HalfExtent(em, building) + Cfg.approachSlack;
            float3 d = from - c; d.y = 0f;
            float m = math.max(math.abs(d.x), math.abs(d.z));
            if (m < 0.01f) d = new float3(1f, 0f, 0f); else d /= m;   // onto the unit square
            float3 dest = new float3(c.x + d.x * h, c.y, c.z + d.z * h);

            // A plain move: it clears whatever the unit was doing (and any
            // earlier GarrisonApproach), snaps the point onto walkable ground
            // and shields the walk from auto-targeting. A unit on a wall-walk
            // comes down first (MoveCommandHelper's layered branch).
            TheWaningBorder.Core.Commands.Types.MoveCommandHelper.Execute(em, unit, dest);

            TransientState.Set(em, unit, new GarrisonApproach { Building = building });
        }

        /// <summary>Drop a pending garrison walk (any other order does).
        /// Non-structural: the component is only disabled.</summary>
        public static void CancelApproach(EntityManager em, Entity unit)
        {
            if (em.Exists(unit)) TransientState.Clear<GarrisonApproach>(em, unit);
        }

        /// <summary>True while <paramref name="unit"/> is walking to a garrison.</summary>
        public static bool IsApproaching(EntityManager em, Entity unit)
            => TransientState.Active<GarrisonApproach>(em, unit);

        /// <summary>
        /// Put <paramref name="unit"/> in the building's first free slot.
        /// Structural (the unit is disabled): call outside query iteration.
        /// </summary>
        public static void Enter(EntityManager em, Entity unit, Entity module)
        {
            if (!CanGarrison(em, unit, module)) return;

            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            int free = -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Occupant == Entity.Null || !em.Exists(slots[i].Occupant)) { free = i; break; }
            if (free < 0) return;
            slots[free] = new WallGarrisonSlot { Occupant = unit };

            // Inside, the unit is doing nothing: drop every order it carried
            // (and the walk that brought it here), so it does not resume a
            // stale march the moment it is put out again.
            TheWaningBorder.Core.Commands.CommandHelper.ClearAllCommands(em, unit);
            CancelApproach(em, unit);
            if (em.HasComponent<Target>(unit)) em.SetComponentData(unit, new Target { Value = Entity.Null });

            float3 at = em.HasComponent<LocalTransform>(module)
                ? em.GetComponentData<LocalTransform>(module).Position : float3.zero;
            em.AddComponentData(unit, new WallGarrisonedIn
            {
                Module = module,
                At = at,
                Half = HalfExtent(em, module),
            });
            // Drop the view first: once the entity is disabled the spawn
            // query no longer sees it, so nothing would ever come back for it.
            var spawn = PresentationSpawnSystem.Instance;
            if (spawn != null) spawn.ForceRespawn(unit);
            em.AddComponent<Disabled>(unit);

            RefreshFire(em, module);
            Reclad(em, module);
        }

        /// <summary>The module draws one manned position per occupant, so a
        /// garrison change means its view is rebuilt. A Watch Tower draws no
        /// occupants, so its view is left alone.</summary>
        static void Reclad(EntityManager em, Entity module)
        {
            if (em.Exists(module) && em.HasComponent<WatchTowerTag>(module)) return;
            var spawn = PresentationSpawnSystem.Instance;
            if (spawn != null && em.Exists(module)) spawn.ForceRespawn(module);
        }

        /// <summary>
        /// Take everyone out of <paramref name="module"/> and stand them on
        /// the ground around it. Structural: call outside query iteration.
        /// </summary>
        public static void EmptyModule(EntityManager em, Entity module)
        {
            if (module == Entity.Null || !em.Exists(module)) return;
            if (!em.HasBuffer<WallGarrisonSlot>(module)) return;

            var occupants = new List<Entity>();
            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Occupant != Entity.Null && em.Exists(slots[i].Occupant))
                    occupants.Add(slots[i].Occupant);
                slots[i] = new WallGarrisonSlot { Occupant = Entity.Null };
            }
            if (occupants.Count > 0)
            {
                float3 at = em.HasComponent<LocalTransform>(module)
                    ? em.GetComponentData<LocalTransform>(module).Position : float3.zero;
                Eject(em, occupants, at, HalfExtent(em, module));
            }

            RefreshFire(em, module);
            Reclad(em, module);
        }

        /// <summary>Sixteen fixed bearings for the ejection rings — a table,
        /// not trigonometry, so every peer lands every man on the same spot.</summary>
        static readonly float2[] EjectDirs =
        {
            new float2( 1.000000f,  0.000000f), new float2( 0.923880f,  0.382683f),
            new float2( 0.707107f,  0.707107f), new float2( 0.382683f,  0.923880f),
            new float2( 0.000000f,  1.000000f), new float2(-0.382683f,  0.923880f),
            new float2(-0.707107f,  0.707107f), new float2(-0.923880f,  0.382683f),
            new float2(-1.000000f,  0.000000f), new float2(-0.923880f, -0.382683f),
            new float2(-0.707107f, -0.707107f), new float2(-0.382683f, -0.923880f),
            new float2( 0.000000f, -1.000000f), new float2( 0.382683f, -0.923880f),
            new float2( 0.707107f, -0.707107f), new float2( 0.923880f, -0.382683f),
        };

        /// <summary>
        /// Stand <paramref name="units"/> back on the ground around a building
        /// that stood at <paramref name="at"/> (half-footprint
        /// <paramref name="half"/>), each on free walkable ground: rings of
        /// sixteen bearings from <c>ejectGap</c> past the footprint outwards,
        /// skipping blocked cells and spots another ejected man already took.
        /// Alive, enabled, idle, on the ground layer. Used by the Empty button
        /// and by the orphan sweep (the building was destroyed, by whatever
        /// path). Structural: call outside query iteration.
        /// </summary>
        public static void Eject(EntityManager em, List<Entity> units, float3 at, float half)
        {
            var cfg = Cfg;
            var taken = new List<float3>(units.Count);
            float minSq = cfg.ejectSpacing * cfg.ejectSpacing * 0.8f;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!em.Exists(u)) continue;

                float3 spot = default;
                bool found = false;
                for (int ring = 0; ring <= cfg.ejectRings && !found; ring++)
                {
                    float r = half + cfg.ejectGap + ring * cfg.ejectSpacing;
                    for (int k = 0; k < EjectDirs.Length && !found; k++)
                    {
                        // Each man starts his search a few bearings further
                        // round, so a crowd fans out instead of queueing on
                        // one side.
                        var dir = EjectDirs[(k + i * 5) % EjectDirs.Length];
                        float3 p = new float3(at.x + dir.x * r, at.y, at.z + dir.y * r);
                        if (!NavGridQuery.IsWorldStandable(p)) continue;
                        bool clash = false;
                        for (int t = 0; t < taken.Count && !clash; t++)
                            clash = math.distancesq(taken[t].xz, p.xz) < minSq;
                        if (clash) continue;
                        spot = p;
                        found = true;
                    }
                }
                if (!found)
                    NavGridQuery.SnapToWalkable(new float3(at.x + half + cfg.ejectGap, at.y, at.z), out spot, out _);
                spot.y = TerrainUtility.GetHeight(spot.x, spot.z);
                taken.Add(spot);

                if (em.HasComponent<Disabled>(u)) em.RemoveComponent<Disabled>(u);
                if (em.HasComponent<WallGarrisonedIn>(u)) em.RemoveComponent<WallGarrisonedIn>(u);
                CancelApproach(em, u);
                if (em.HasComponent<LayeredMoveOrder>(u)) em.RemoveComponent<LayeredMoveOrder>(u);
                if (em.HasComponent<LayerTraversalState>(u)) em.RemoveComponent<LayerTraversalState>(u);
                if (em.HasBuffer<DeckWaypoint>(u)) em.GetBuffer<DeckWaypoint>(u).Clear();
                if (em.HasComponent<NavLayerIndex>(u))
                    em.SetComponentData(u, new NavLayerIndex { Layer = NavLayerIndex.LayerGround });
                if (em.HasComponent<DesiredDestination>(u))
                    em.SetComponentData(u, new DesiredDestination { Position = spot, Has = 0 });
                if (em.HasComponent<LocalTransform>(u))
                {
                    var t = em.GetComponentData<LocalTransform>(u);
                    em.SetComponentData(u, LocalTransform.FromPositionRotationScale(spot, t.Rotation, t.Scale));
                }
                // Out here the guard point is where he stands, or the stance
                // leash would walk him back to wherever he was when he went in.
                if (em.HasComponent<GuardPoint>(u))
                    em.SetComponentData(u, new GuardPoint { Position = spot, Has = 1 });
            }
        }

        /// <summary>
        /// Rebuild the module's fire from the men actually inside it, and
        /// drop dead occupants out of their slots. A module with nobody in it
        /// loses the attack entirely — the wall does not shoot on its own.
        /// </summary>
        public static void RefreshFire(EntityManager em, Entity module)
        {
            if (module == Entity.Null || !em.Exists(module)) return;
            if (!em.HasBuffer<WallGarrisonSlot>(module)) return;

            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            int manned = 0, damage = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var occ = slots[i].Occupant;
                if (occ == Entity.Null) continue;
                if (!em.Exists(occ)) { slots[i] = new WallGarrisonSlot { Occupant = Entity.Null }; continue; }
                manned++;
                damage += em.HasComponent<Damage>(occ) ? em.GetComponentData<Damage>(occ).Value : 8;
            }

            if (em.HasComponent<WatchTowerTag>(module))
            {
                RefreshTowerFire(em, module, manned);
                return;
            }

            if (manned == 0)
            {
                if (em.HasComponent<BuildingRangedAttack>(module))
                    em.RemoveComponent<BuildingRangedAttack>(module);
                return;
            }

            float timer = em.HasComponent<BuildingRangedAttack>(module)
                ? em.GetComponentData<BuildingRangedAttack>(module).Timer : 0f;
            em.AddComponentData(module, new BuildingRangedAttack
            {
                Range = FireRange,
                Damage = damage,
                Cooldown = FireCooldown,
                Timer = timer,
                MaxTargets = manned,
            });
            if (!em.HasComponent<DamageTypeData>(module))
                em.AddComponentData(module, new DamageTypeData { Value = DamageType.Ranged });
            if (em.HasComponent<LineOfSight>(module))
            {
                var los = em.GetComponentData<LineOfSight>(module);
                if (los.Radius < MannedLineOfSight)
                    em.SetComponentData(module, new LineOfSight { Radius = MannedLineOfSight });
            }
        }

        /// <summary>The Watch Tower's building id (Tower.asset).</summary>
        private const string WatchTowerId = "Alanthor_Tower";

        /// <summary>
        /// A Watch Tower's volley with <paramref name="manned"/> men inside:
        /// its OWN attack (range, damage, cooldown untouched) firing at
        /// (level targets + manned x garrisonArrowsPerOccupant) targets. The
        /// level's targets are its BuildingLevelDefSO's authored attack when
        /// it has one, else the tower SO's attack — the same source
        /// BuildingUpgradeSystem applies, so a level-up that resets MaxTargets
        /// is corrected on the next WallGarrisonSystem sweep. Absolute, so
        /// re-applying is idempotent.
        /// </summary>
        private static void RefreshTowerFire(EntityManager em, Entity tower, int manned)
        {
            if (!em.HasComponent<BuildingRangedAttack>(tower)) return;
            if (!TechCatalog.TryGetBuildingSO(WatchTowerId, out var so)) return;

            int targets = so.attack != null ? so.attack.maxTargets : 1;
            int level = em.HasComponent<BuildingUpgradeState>(tower)
                ? em.GetComponentData<BuildingUpgradeState>(tower).Level : 0;
            if (level > 0 && em.HasComponent<FactionTag>(tower))
            {
                byte culture = CultureConfig.GetCompletedCulture(em, em.GetComponentData<FactionTag>(tower).Value);
                if (TechCatalog.TryGetBuildingLevel(culture, WatchTowerId, level, out var lvl)
                    && lvl.attack != null && lvl.attack.enabled)
                    targets = lvl.attack.maxTargets;
            }

            var atk = em.GetComponentData<BuildingRangedAttack>(tower);
            int wanted = targets + manned * so.garrisonArrowsPerOccupant;
            if (atk.MaxTargets == wanted) return;
            atk.MaxTargets = wanted;
            em.SetComponentData(tower, atk);
        }
    }
}
