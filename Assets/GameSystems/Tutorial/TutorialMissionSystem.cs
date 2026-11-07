// TutorialMissionSystem.cs
// The Basic tutorial mission: stages, lost units, the camp, the scripted curse
// waves, win and loss. docs/Design/Tutorial_Missions.md
//
// A mission has no lockstep (LockstepBootstrap refuses TutorialActive), so it
// may write the world directly. It checks at 4 Hz on simulation time, so a
// paused game pauses the wave clock too.
//
// Nothing here runs outside a mission. The curse's own behaviour is off for the
// mission (CurseTerritorySystem stops after SyncHoldings), which is why these
// waves are the only curse units on the map.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Tutorial
{
    public partial class TutorialMissionSystem : SystemBase
    {
        private const float CheckInterval = 0.25f;

        private static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<LineOfSight>(),
        };
        private static CachedEntityQuery QC_Units;

        private static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Buildings;

        // Set at boot, before the first update.
        private static Entity _litharch, _node;

        private int _epoch = -1;
        private float _timer, _waveTimer, _retargetTimer;
        private bool _shelterEverFinished;
        private bool[] _workerSiteFound, _spearmanSiteFound;
        private readonly HashSet<Entity> _knownScouts = new HashSet<Entity>();
        private readonly HashSet<Entity> _knownSpearmen = new HashSet<Entity>();
        private readonly List<Entity> _waveUnits = new List<Entity>();

        private static TutorialMissionSystemConfig Cfg => TutorialMissionSystemConfig.I;
        private static Faction Player => GameSettings.LocalPlayerFaction;

        /// <summary>The Litharch, for the panel and the automated playthrough.</summary>
        public static Entity Litharch => _litharch;

        // ── Boot (called by the match bootstrap, in place of the normal spawns) ──

        public static float3 Ground(UnityEngine.Vector2 p)
            => new float3(p.x, TerrainUtility.GetHeight(p.x, p.y), p.y);

        /// <summary>The player's whole starting force: one Litharch.</summary>
        public static void SpawnStart(EntityManager em)
        {
            TutorialMissionState.Reset();
            _litharch = UnitFactory.Create(em, "Litharch", Ground(Cfg.litharchStart), Player);
        }

        /// <summary>The camp's resource nodes, by hand: the territory type would
        /// deal them all over the woods, out of a beginner's sight.</summary>
        public static void SpawnResourceNodes(EntityManager em)
        {
            foreach (var p in Cfg.supplyNodes) TerritoryResources.PlaceAuthored(em, Ground(p), 0);
            foreach (var p in Cfg.ironNodes) TerritoryResources.PlaceAuthored(em, Ground(p), 1);
            foreach (var p in Cfg.veilstoneNodes) TerritoryResources.PlaceAuthored(em, Ground(p), 2);
        }

        /// <summary>The curse's one node, in the Blight.</summary>
        public static void SpawnNode(EntityManager em)
        {
            _node = SmallNode.Create(em, Ground(Cfg.nodePosition));
        }

        // ── The tick ──────────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            if (!GameSettings.TutorialBasicActive) return;
            if (!MatchLifecycle.MapPopulated) return;

            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                ResetRun();
            }
            if (TutorialMissionState.Over) return;

            _timer += SystemAPI.Time.DeltaTime;
            if (_timer < CheckInterval) return;
            float step = _timer;
            _timer = 0f;

            var em = EntityManager;
            CheckLostUnits(em);
            ObserveLitharch(em);
            CountBuildings(em);
            CountTrained(em);
            AdvanceStage(em);
            if (TutorialMissionState.CurseOpen) TickCurse(em, step);
            CheckEnd(em);
        }

        private void ResetRun()
        {
            _timer = _retargetTimer = _waveTimer = 0f;
            _shelterEverFinished = false;
            _workerSiteFound = new bool[Cfg.workerSites.Length];
            _spearmanSiteFound = new bool[Cfg.spearmanSites.Length];
            _knownScouts.Clear();
            _knownSpearmen.Clear();
            _waveUnits.Clear();
            TutorialMissionState.Offer(null, null, null);
        }

        // ── Lost units ────────────────────────────────────────────────────

        /// <summary>A lost group joins when a player unit comes within sight of
        /// it. They do not exist until then, and the fog hides the moment.</summary>
        private void CheckLostUnits(EntityManager em)
        {
            var q = QC_Units.Get(em, QT_Units);
            using var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xforms = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sights = q.ToComponentDataArray<LineOfSight>(Allocator.Temp);

            bool Seen(UnityEngine.Vector2 site)
            {
                var s = new float2(site.x, site.y);
                for (int i = 0; i < factions.Length; i++)
                {
                    if (factions[i].Value != Player) continue;
                    if (math.distance(xforms[i].Position.xz, s) <= sights[i].Radius) return true;
                }
                return false;
            }

            for (int i = 0; i < _workerSiteFound.Length; i++)
            {
                if (_workerSiteFound[i] || !Seen(Cfg.workerSites[i])) continue;
                _workerSiteFound[i] = true;
                SpawnGroup(em, "Worker", Cfg.workerSites[i], Cfg.workersPerSite);
                TutorialMissionState.Workers += Cfg.workersPerSite;
                SimSignals.Notify(Loc.T("You found lost workers. They join you."));
            }

            // The Spearmen wait for their stage: found early, they would open
            // the curse before the player has a Barracks.
            if (TutorialMissionState.Stage != TutorialStage.Muster) return;
            for (int i = 0; i < _spearmanSiteFound.Length; i++)
            {
                if (_spearmanSiteFound[i] || !Seen(Cfg.spearmanSites[i])) continue;
                _spearmanSiteFound[i] = true;
                foreach (var e in SpawnGroup(em, "Spearman", Cfg.spearmanSites[i], Cfg.spearmenPerSite))
                    _knownSpearmen.Add(e);
                TutorialMissionState.SpearmenFound += Cfg.spearmenPerSite;
                SimSignals.Notify(Loc.T("You found lost Spearmen. They join you."));
            }
        }

        private List<Entity> SpawnGroup(EntityManager em, string unitId, UnityEngine.Vector2 site, int count)
        {
            var made = new List<Entity>(count);
            for (int i = 0; i < count; i++)
                made.Add(UnitFactory.Create(em, unitId, Around(site, i, count), Player));
            return made;
        }

        /// <summary>Spot <paramref name="i"/> of <paramref name="count"/> on a small ring.</summary>
        private static float3 Around(UnityEngine.Vector2 site, int i, int count)
        {
            float a = i * math.PI * 2f / math.max(1, count);
            float r = Cfg.groupSpacing * math.max(1f, count / 4f);
            return Ground(new UnityEngine.Vector2(site.x + math.cos(a) * r, site.y + math.sin(a) * r));
        }

        private void ObserveLitharch(EntityManager em)
        {
            if (!em.Exists(_litharch)) return;
            if (!TutorialMissionState.LitharchMoved)
            {
                var p = em.GetComponentData<LocalTransform>(_litharch).Position;
                var s = Cfg.litharchStart;
                if (math.distance(p.xz, new float2(s.x, s.y)) > Cfg.groupSpacing)
                    TutorialMissionState.LitharchMoved = true;
            }
            if (!TutorialMissionState.ChainedOrders
                && em.HasBuffer<QueuedCommand>(_litharch)
                && em.GetBuffer<QueuedCommand>(_litharch, true).Length > 0)
                TutorialMissionState.ChainedOrders = true;
        }

        // ── Counting ──────────────────────────────────────────────────────

        private void CountBuildings(EntityManager em)
        {
            int shelters = 0, huts = 0, gatherers = 0, veil = 0, mines = 0, barracks = 0;
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (factions[i].Value != Player) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (em.HasComponent<FortressTag>(e)) shelters++;
                else if (em.HasComponent<HutTag>(e)) huts++;
                else if (em.HasComponent<GathererHutTag>(e)) gatherers++;
                else if (em.HasComponent<VeilstoneMineTag>(e)) veil++;
                else if (em.HasComponent<MineTag>(e)) mines++;
                else if (em.HasComponent<BarracksTag>(e)) barracks++;
            }

            if (shelters > 0) _shelterEverFinished = true;
            TutorialMissionState.Shelters = shelters;
            TutorialMissionState.Huts = math.max(TutorialMissionState.Huts, huts);
            TutorialMissionState.GatherersHuts = math.max(TutorialMissionState.GatherersHuts, gatherers);
            TutorialMissionState.VeilstoneMines = math.max(TutorialMissionState.VeilstoneMines, veil);
            TutorialMissionState.IronMines = math.max(TutorialMissionState.IronMines, mines);
            TutorialMissionState.Barracks = math.max(TutorialMissionState.Barracks, barracks);
        }

        /// <summary>Anything new that was not spawned as a lost unit was trained.
        /// Counts from the Muster stage, when the buttons appear.</summary>
        private void CountTrained(EntityManager em)
        {
            if (TutorialMissionState.Stage != TutorialStage.Muster) return;
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (factions[i].Value != Player) continue;
                var e = ents[i];
                if (em.HasComponent<SpearmanTag>(e))
                {
                    if (_knownSpearmen.Add(e)) TutorialMissionState.SpearmenTrained++;
                }
                else if (em.HasComponent<UnitTypeId>(e)
                         && em.GetComponentData<UnitTypeId>(e).Value == "Scout")
                {
                    if (_knownScouts.Add(e)) TutorialMissionState.ScoutsTrained++;
                }
            }
        }

        private int LivingSpearmen(EntityManager em)
        {
            int n = 0;
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (factions[i].Value == Player && em.HasComponent<SpearmanTag>(ents[i])) n++;
            return n;
        }

        // ── Stages ────────────────────────────────────────────────────────

        private void AdvanceStage(EntityManager em)
        {
            var cfg = Cfg;
            switch (TutorialMissionState.Stage)
            {
                case TutorialStage.Rescue:
                    if (TutorialMissionState.Workers >= cfg.workerSites.Length * cfg.workersPerSite)
                    {
                        TutorialMissionState.Stage = TutorialStage.Camp;
                        SimSignals.Ping(Ground(cfg.campSpot), SimPingKind.Discovery, 12f, big: true);
                        SimSignals.Announce(Loc.T("Everyone is found. Look for a place to camp."));
                    }
                    break;

                case TutorialStage.Camp:
                    if (!TutorialMissionState.CampFound && AnyUnitNearCamp(em))
                        FindCamp(em);
                    // Nothing holds this ground until the Shelter stands: no
                    // soldiers, no finished building, so the meter would drain.
                    if (TutorialMissionState.CampFound) KeepCampClaimed();
                    if (_shelterEverFinished)
                    {
                        TutorialMissionState.Stage = TutorialStage.Settle;
                        TutorialMissionState.Offer(cfg.settleBuildings, null, null);
                        SimSignals.Announce(Loc.T("The camp has a Shelter. Now build it up."));
                    }
                    break;

                case TutorialStage.Settle:
                    if (TutorialMissionState.Huts >= cfg.hutsNeeded
                        && TutorialMissionState.GatherersHuts >= cfg.gatherersHutsNeeded
                        && TutorialMissionState.VeilstoneMines >= cfg.veilstoneMinesNeeded
                        && TutorialMissionState.IronMines >= cfg.ironMinesNeeded
                        && TutorialMissionState.Barracks >= cfg.barracksNeeded)
                    {
                        TutorialMissionState.Stage = TutorialStage.Muster;
                        TutorialMissionState.Offer(cfg.settleBuildings, cfg.musterUnits, cfg.musterResearch);
                        SimSignals.Announce(Loc.T("Soldiers are lost out there too. Find them, and train more."));
                    }
                    break;

                case TutorialStage.Muster:
                    if (!TutorialMissionState.CurseOpen && LivingSpearmen(em) >= cfg.harassAtSpearmen)
                        OpenCurse(em);
                    break;
            }
        }

        private bool AnyUnitNearCamp(EntityManager em)
        {
            var q = QC_Units.Get(em, QT_Units);
            using var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xforms = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var c = new float2(Cfg.campSpot.x, Cfg.campSpot.y);
            for (int i = 0; i < factions.Length; i++)
                if (factions[i].Value == Player && math.distance(xforms[i].Position.xz, c) <= Cfg.campRadius)
                    return true;
            return false;
        }

        private void FindCamp(EntityManager em)
        {
            var cfg = Cfg;
            TutorialMissionState.CampFound = true;
            KeepCampClaimed();
            FactionEconomy.Add(em, Player,
                Cost.Of(supplies: cfg.grantSupplies, iron: cfg.grantIron, veilstone: cfg.grantVeilstone),
                IncomeSource.Grant);
            TutorialMissionState.Offer(cfg.campBuildings, null, null);
            SimSignals.Announce(Loc.T("This clearing will do. The camp's stores are yours."));
        }

        private static void KeepCampClaimed()
        {
            int t = RegionMap.NearestRegion(Cfg.campSpot.x, Cfg.campSpot.y);
            if (t == RegionMap.None) return;
            if (TerritoryOwnership.HolderOf(t) != (int)Player || TerritoryOwnership.ValueOf(t) < 50f)
                TerritoryOwnership.ForceClaim(t, (int)Player);
        }

        // ── The curse ─────────────────────────────────────────────────────

        private void OpenCurse(EntityManager em)
        {
            var cfg = Cfg;
            TutorialMissionState.CurseOpen = true;
            var node = Ground(cfg.nodePosition);
            TheWaningBorder.Systems.Sect.SectActivePowerHelper.SpawnReveal(
                em, Player, node, cfg.nodeRevealRadius, float.MaxValue / 4f);
            SimSignals.Ping(node, SimPingKind.Curse, 15f, big: true);
            SimSignals.Announce(Loc.T("Crystallings! The curse has a node to the east. It must fall."));
            SendWave(em, cfg.harassCrystallings, 0);
            _waveTimer = cfg.waveIntervalSeconds;
        }

        private void TickCurse(EntityManager em, float step)
        {
            var cfg = Cfg;
            _waveTimer -= step;
            TutorialMissionState.SecondsToNextWave = math.max(0f, _waveTimer);
            if (_waveTimer <= 0f)
            {
                _waveTimer = cfg.waveIntervalSeconds;
                SendWave(em, cfg.waveCrystallings, cfg.waveVeilstingers);
                SimSignals.Announce(Loc.T("The curse sends a wave."));
            }

            _retargetTimer -= step;
            if (_retargetTimer > 0f) return;
            _retargetTimer = cfg.waveRetargetSeconds;

            // A wave that has reached its destination attacks the nearest building.
            // Commands are enable-bit components every unit carries: Active, not Has.
            for (int i = _waveUnits.Count - 1; i >= 0; i--)
            {
                var u = _waveUnits[i];
                if (!em.Exists(u)) { _waveUnits.RemoveAt(i); continue; }
                if (TransientState.Active<AttackMoveCommand>(em, u) || TransientState.Active<AttackCommand>(em, u)) continue;
                if (em.HasComponent<Target>(u) && em.GetComponentData<Target>(u).Value != Entity.Null) continue;
                if (TryNearestBuilding(em, em.GetComponentData<LocalTransform>(u).Position, out _, out var target))
                    AttackCommandHelper.Execute(em, u, target);
            }
        }

        private void SendWave(EntityManager em, int crystallings, int veilstingers)
        {
            TutorialMissionState.WavesSent++;
            var node = Cfg.nodePosition;
            if (!TryNearestBuilding(em, Ground(node), out var dest, out _)) dest = Ground(Cfg.campSpot);
            int count = crystallings + veilstingers;
            for (int i = 0; i < count; i++)
            {
                var at = Around(node, i, count);
                var u = i < crystallings
                    ? Crystalling.Create(em, at, Faction.Border)
                    : Veilstinger.Create(em, at, Faction.Border);
                AttackMoveCommandHelper.Execute(em, u, dest);
                _waveUnits.Add(u);
            }
        }

        private bool TryNearestBuilding(EntityManager em, float3 from, out float3 nearest, out Entity entity)
        {
            nearest = default;
            entity = Entity.Null;
            bool found = false;
            float best = float.MaxValue;
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xforms = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < factions.Length; i++)
            {
                if (factions[i].Value != Player) continue;
                float d = math.distancesq(xforms[i].Position.xz, from.xz);
                if (d < best) { best = d; nearest = xforms[i].Position; entity = ents[i]; found = true; }
            }
            return found;
        }

        // ── Win and loss ──────────────────────────────────────────────────

        private void CheckEnd(EntityManager em)
        {
            bool nodeDead = !em.Exists(_node)
                || em.HasComponent<BuildingCollapseState>(_node)
                || (em.HasComponent<Health>(_node) && em.GetComponentData<Health>(_node).Value <= 0);
            if (nodeDead)
            {
                End(true, Loc.T("Mission complete"), Loc.T("The node is gone and the camp stands."));
                return;
            }
            if (_shelterEverFinished && TutorialMissionState.Shelters == 0)
                End(false, Loc.T("Mission failed"), Loc.T("The curse destroyed your Shelter."));
        }

        private static void End(bool won, string title, string subtitle)
        {
            TutorialMissionState.Over = true;
            TutorialMissionState.Won = won;
            SimSignals.MatchEnded(title, subtitle, won);
        }
    }
}
