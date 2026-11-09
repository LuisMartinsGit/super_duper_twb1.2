// TutorialAdvancedSystem.cs
// The Alanthor tutorial mission: a fresh town, the Vault, the advanced economy,
// the army, claims, a rival, religion, the cursed Sanctum, the monument and the
// three ways to win. docs/Design/Tutorial_Missions.md section 4
//
// It leans on the real game wherever it can. The bases come from the normal
// spawn, the rival is a real Beginner AI, and the win is the real victory check:
// EliminationSystem takes over when the victory options open.
//
// Until then the rival shares the player's team, which is what keeps it out of
// reach: allies neither fight nor contest ground (Teams.md). With one curse node
// on the map the game raises exactly one monument, in the node's territory, and
// so that monument is the one that knows where the Shardroot lies.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Tutorial
{
    public partial class TutorialAdvancedSystem : SystemBase
    {
        private const float CheckInterval = 0.25f;
        private const byte MissionTeam = 1;

        private static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Buildings;

        private static readonly ComponentType[] QT_Monuments =
        {
            ComponentType.ReadOnly<MonumentState>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Monuments;

        private static Entity _litharch, _node;

        private int _epoch = -1;
        private float _timer, _waveTimer, _retargetTimer, _rivalNoteTimer;
        private readonly System.Collections.Generic.List<Entity> _waveUnits =
            new System.Collections.Generic.List<Entity>();

        private static TutorialAdvancedSystemConfig Cfg => TutorialAdvancedSystemConfig.I;
        private static Faction Player => GameSettings.LocalPlayerFaction;
        private static Faction Rival => Faction.Red;

        /// <summary>The first King, for the automated playthrough.</summary>
        public static Entity Hero { get; private set; }
        public static Entity Litharch => _litharch;

        private static float3 Ground(Vector2 p) => new float3(p.x, TerrainUtility.GetHeight(p.x, p.y), p.y);

        // ── Boot ──────────────────────────────────────────────────────────

        /// <summary>The curse's node (the monument follows it), the player's town
        /// beyond the normal base, and the rival's two extra territories.
        /// Called by the match bootstrap once the resource nodes are laid.</summary>
        public static void SetUp(EntityManager em, float3 home)
        {
            var cfg = Cfg;
            TutorialMissionState.Reset();
            TutorialMissionState.WinCountdownSeconds = cfg.winCountdownSeconds;
            TutorialMissionState.DenyBuildings(cfg.deniedUntilReligion);
            TutorialMissionState.DenyUnits(cfg.deniedUnits);
            Alliances.SetTeam(Player, MissionTeam);
            Alliances.SetTeam(Rival, MissionTeam);

            _node = SmallNode.Create(em, Ground(cfg.nodePosition));
            Monument.SpawnAll(em);

            _litharch = UnitFactory.Create(em, "Litharch", home + new float3(cfg.litharchOffset.x, 0f, cfg.litharchOffset.y), Player);
            Hero = Entity.Null;

            foreach (var o in cfg.townHutOffsets)
                BuildingFactory.Create(em, "Hut", Ground(new Vector2(home.x + o.x, home.z + o.y)), Player);
            BuildingFactory.Create(em, "Barracks",
                Ground(new Vector2(home.x + cfg.townBarracksOffset.x, home.z + cfg.townBarracksOffset.y)), Player);
            for (int i = 0; i < cfg.townGatherersHuts; i++) BuildOnNearestNode(em, "GatherersHut", home);
            for (int i = 0; i < cfg.townMines; i++) BuildOnNearestNode(em, "Mine", home);

            foreach (var p in cfg.rivalLands)
            {
                BuildingFactory.Create(em, "Hut", Ground(p), Rival);
                HoldRivalLand(p);
            }

            FactionEconomy.Add(em, Player,
                Cost.Of(supplies: cfg.grantSupplies, iron: cfg.grantIron, veilstone: cfg.grantVeilstone),
                IncomeSource.Grant);
        }

        private static void BuildOnNearestNode(EntityManager em, string id, float3 home)
        {
            if (TerritoryOwnership.TrySnapToNode(em, id, home, out var at))
                BuildingFactory.Create(em, id, at, Player);
            else
                Debug.LogError($"[TutorialAdvanced] no free node for the town's {id}.");
        }

        private static void HoldRivalLand(Vector2 p)
        {
            int t = RegionMap.NearestRegion(p.x, p.y);
            if (t == RegionMap.None) return;
            if (TerritoryOwnership.HolderOf(t) != (int)Rival || TerritoryOwnership.ValueOf(t) < 50f)
                TerritoryOwnership.ForceClaim(t, (int)Rival);
        }

        // ── The tick ──────────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            if (!GameSettings.TutorialAdvancedActive) return;
            if (!MatchLifecycle.MapPopulated) return;

            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                _timer = _waveTimer = _retargetTimer = _rivalNoteTimer = 0f;
                _waveUnits.Clear();
            }
            if (TutorialMissionState.Over) return;

            _timer += SystemAPI.Time.DeltaTime;
            if (_timer < CheckInterval) return;
            float step = _timer;
            _timer = 0f;

            var em = EntityManager;
            ReadProgress(em);
            Advance(em, step);
            if (!TutorialMissionState.ConquestOpen)
                foreach (var p in Cfg.rivalLands) HoldRivalLand(p);
            if (TutorialMissionState.AdvancedStage >= AdvancedStage.Economy && !TutorialMissionState.NodeDead)
                TickWaves(em, step);
            CheckLoss();
        }

        private void ReadProgress(EntityManager em)
        {
            var cfg = Cfg;
            int fortresses = 0, stopped = 0, ranges = 0, towers = 0, temples = 0;
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (factions[i].Value != Player || em.HasComponent<UnderConstruction>(e)) continue;
                if (em.HasComponent<FortressTag>(e)) { fortresses++; continue; }
                // 2b: an extractor told to stop (Veilstone_Economy.md §3.5).
                if (em.HasComponent<ExtractorStopped>(e)) { stopped++; continue; }
                string id = UpgradeBuildingCommandHelper.ResolveBuildingId(em, e);
                if (id == cfg.archeryRangeId) ranges++;
                else if (id == cfg.towerId) towers++;
                else if (id == cfg.templeId) temples++;
            }

            TutorialMissionState.Shelters = fortresses;
            TutorialMissionState.Stopped = math.max(TutorialMissionState.Stopped, stopped);
            TutorialMissionState.Ranges = math.max(TutorialMissionState.Ranges, ranges);
            TutorialMissionState.Towers = math.max(TutorialMissionState.Towers, towers);
            TutorialMissionState.Temples = math.max(TutorialMissionState.Temples, temples);

            var research = FactionResearchState.Instance;
            if (research != null && !TutorialMissionState.SurveyDone)
                foreach (var tech in cfg.surveyTechs)
                    if (research.HasResearched(Player, tech)) TutorialMissionState.SurveyDone = true;

            TutorialMissionState.Claimed = math.max(TutorialMissionState.Claimed, TerritoryOwnership.CountOf(Player) - 1);
            TutorialMissionState.ReligionPoints = FactionReligionPointsHelper.GetBalance(em, Player);

            if (!TutorialMissionState.SectAdopted)
                foreach (var sect in SectConfig.AllSectIds)
                    if (SectQuery.IsAdoptedAtLeast(em, Player, sect, default))
                        TutorialMissionState.SectAdopted = true;

            TutorialMissionState.NodeDead = !em.Exists(_node)
                || em.HasComponent<BuildingCollapseState>(_node)
                || (em.HasComponent<Health>(_node) && em.GetComponentData<Health>(_node).Value <= 0);

            var mq = QC_Monuments.Get(em, QT_Monuments);
            using var states = mq.ToComponentDataArray<MonumentState>(Allocator.Temp);
            if (states.Length > 0)
            {
                TutorialMissionState.MonumentHeld = states[0].Holder == Player;
                TutorialMissionState.MonumentExplored = Monument.HasExplored(states[0], Player);
            }
        }

        private void Advance(EntityManager em, float step)
        {
            var cfg = Cfg;
            switch (TutorialMissionState.AdvancedStage)
            {
                case AdvancedStage.Vault:
                    if (CultureConfig.GetCompletedCulture(em, Player) == Cultures.Alanthor)
                    {
                        CrownTheKing(em);
                        _waveTimer = cfg.firstWaveSeconds;
                        TutorialMissionState.AdvancedStage = AdvancedStage.Economy;
                        SimSignals.Announce(Loc.T("You are Alanthor now. Your buildings have new forms."));
                    }
                    break;

                case AdvancedStage.Economy:
                    if (TutorialMissionState.SurveyDone && TutorialMissionState.Stopped >= 1)
                        TutorialMissionState.AdvancedStage = AdvancedStage.Military;
                    break;

                case AdvancedStage.Military:
                    if (TutorialMissionState.Ranges >= 1)
                        TutorialMissionState.AdvancedStage = AdvancedStage.Defense;
                    break;

                case AdvancedStage.Defense:
                    if (TutorialMissionState.Towers >= 1 && TutorialMissionState.Claimed >= 1)
                    {
                        TutorialMissionState.AdvancedStage = AdvancedStage.Rival;
                        _rivalNoteTimer = cfg.rivalNoteSeconds;
                        SimSignals.Ping(Ground(cfg.rivalSeat), SimPingKind.Discovery, 12f, big: true);
                        SimSignals.Announce(Loc.T("A rival kingdom holds the east."));
                    }
                    break;

                case AdvancedStage.Rival:
                    _rivalNoteTimer -= step;
                    if (_rivalNoteTimer <= 0f)
                        TutorialMissionState.AdvancedStage = AdvancedStage.Points;
                    break;

                case AdvancedStage.Points:
                    if (TutorialMissionState.ReligionPoints >= cfg.religionPointsNeeded)
                    {
                        TutorialMissionState.AdvancedStage = AdvancedStage.Religion;
                        TutorialMissionState.DenyBuildings(null);
                    }
                    break;

                case AdvancedStage.Religion:
                    if (TutorialMissionState.Temples >= 1 && TutorialMissionState.SectAdopted)
                    {
                        TutorialMissionState.AdvancedStage = AdvancedStage.Sanctum;
                        var node = Ground(cfg.nodePosition);
                        TheWaningBorder.Systems.Sect.SectActivePowerHelper.SpawnReveal(
                            em, Player, node, cfg.nodeRevealRadius, float.MaxValue / 4f);
                        SimSignals.Ping(node, SimPingKind.Curse, 15f, big: true);
                    }
                    break;

                case AdvancedStage.Sanctum:
                    if (TutorialMissionState.NodeDead)
                    {
                        TutorialMissionState.AdvancedStage = AdvancedStage.Ruins;
                        SimSignals.Announce(Loc.T("The node is gone. A monument stands in the Sanctum."));
                    }
                    break;

                case AdvancedStage.Ruins:
                    if (TutorialMissionState.MonumentExplored)
                    {
                        TutorialMissionState.AdvancedStage = AdvancedStage.Victory;
                        // The rival is an enemy from here on, and the real victory
                        // check (EliminationSystem) takes over.
                        Alliances.SetTeam(Player, Alliances.NoTeam);
                        Alliances.SetTeam(Rival, Alliances.NoTeam);
                        TutorialMissionState.ConquestOpen = true;
                        SimSignals.Announce(Loc.T("The rival kingdom is now your enemy. Choose how to win."));
                    }
                    break;
            }
        }

        /// <summary>The Litharch becomes the first King Lexor: the unit is replaced
        /// where it stands. The old one goes through DeathSystem, like any unit.</summary>
        private static void CrownTheKing(EntityManager em)
        {
            float3 at = default;
            bool alive = em.Exists(_litharch);
            if (alive) at = em.GetComponentData<LocalTransform>(_litharch).Position;
            else if (TutorialMissionState.Shelters > 0)
            {
                var q = QC_Buildings.Get(em, QT_Buildings);
                using var xf = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                if (xf.Length > 0) at = xf[0].Position;
            }
            Hero = UnitFactory.Create(em, Cfg.kingUnitId, at, Player);
            if (alive)
            {
                var hp = em.GetComponentData<Health>(_litharch);
                hp.Value = 0;
                em.SetComponentData(_litharch, hp);
            }
            SimSignals.Ping(at, SimPingKind.Discovery, 8f);
        }

        private void CheckLoss()
        {
            // Past this point the real victory check decides.
            if (TutorialMissionState.ConquestOpen || TutorialMissionState.Shelters > 0) return;
            TutorialMissionState.Over = true;
            SimSignals.MatchEnded(Loc.T("Mission failed"), Loc.T("Your capital has fallen."), false);
        }

        // ── Waves ─────────────────────────────────────────────────────────

        private void TickWaves(EntityManager em, float step)
        {
            var cfg = Cfg;
            _waveTimer -= step;
            TutorialMissionState.SecondsToNextWave = math.max(0f, _waveTimer);
            if (_waveTimer <= 0f)
            {
                _waveTimer = cfg.waveIntervalSeconds;
                SendWave(em);
                SimSignals.Announce(Loc.T("The curse sends a wave."));
            }

            _retargetTimer -= step;
            if (_retargetTimer > 0f) return;
            _retargetTimer = cfg.waveRetargetSeconds;
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

        private void SendWave(EntityManager em)
        {
            var cfg = Cfg;
            TutorialMissionState.WavesSent++;
            var node = cfg.nodePosition;
            if (!TryNearestBuilding(em, Ground(node), out var dest, out _)) return;
            int count = cfg.waveCrystallings + cfg.waveVeilstingers;
            for (int i = 0; i < count; i++)
            {
                float a = i * math.PI * 2f / count;
                var at = Ground(new Vector2(node.x + math.cos(a) * cfg.waveSpawnRadius, node.y + math.sin(a) * cfg.waveSpawnRadius));
                var u = i < cfg.waveCrystallings
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
            float best = float.MaxValue;
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xforms = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < factions.Length; i++)
            {
                if (factions[i].Value != Player) continue;
                float d = math.distancesq(xforms[i].Position.xz, from.xz);
                if (d < best) { best = d; nearest = xforms[i].Position; entity = ents[i]; }
            }
            return entity != Entity.Null;
        }
    }
}
