// SiegeProbe.cs
// Editor-only diagnostics, driven by the agent bridge via `menu` and readable
// from the Console. Three tools:
//
//   Launch WallSiege Scenario / Launch Quick Skirmish — start a match from
//   inside play mode the way the menus do. Opening a scenario or map scene
//   directly in the editor spawns nothing: the match settings are statics the
//   menu sets before the scene loads, and the domain reload on Play resets
//   them, so they have to be set from within the running player.
//
//   Siege Probe — every siege unit's firing state every two seconds: what it
//   is aiming at, how far, whether it is standing, how its aim and reload
//   timers read, and how many shots are in the air. "The catapults are not
//   attacking" has a dozen possible causes; this is cheaper than guessing.
//
//   Match Probe — the match as a whole every ten seconds: units per faction,
//   what every busy production queue is working on, the curse waves (how many
//   members, how many still in formation, how many fighting) and the
//   Crystalling packs.

using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Core.Config;
using TheWaningBorder.UI.Menus;

namespace TheWaningBorder.EditorTools
{
    public static class SiegeProbe
    {
        // ── Launchers ──────────────────────────────────────────────────────

        [MenuItem("Waning Border/Debug/Launch WallSiege Scenario")]
        public static void LaunchWallSiege()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SiegeProbe] enter play mode (any scene) first");
                return;
            }
            string scene = ScenarioCatalog.Prepare(ScenarioType.WallSiege);
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            Debug.Log("[SiegeProbe] loading " + scene);
        }

        /// <summary>
        /// Human (Blue) vs one Normal AI on Hollow Table, curse on, fog off —
        /// SkirmishPanel.StartGame with the roster it would build by default.
        /// </summary>
        [MenuItem("Waning Border/Debug/Launch Quick Skirmish (HollowTable vs 1 AI)")]
        public static void LaunchQuickSkirmish()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SiegeProbe] enter play mode (any scene) first");
                return;
            }

            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = 2;
            LobbyConfig.SetupSinglePlayer(2);
            GameSettings.SelectedMapScene = "HollowTable";
            GameSettings.SpawnSeed = 20260907;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.IsObserver = false;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            LobbyConfig.ApplyColorSelections();

            Debug.Log("[SiegeProbe] launching quick skirmish on HollowTable, seed " + GameSettings.SpawnSeed);
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        // ── Siege probe ────────────────────────────────────────────────────

        private const double SiegeSeconds = 30.0;
        private const double SiegePeriod = 2.0;
        private static double _siegeUntil, _siegeNext;
        private static bool _siegeArmed;

        [MenuItem("Waning Border/Debug/Siege Probe (30 s)")]
        public static void Start()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SiegeProbe] enter play mode first");
                return;
            }
            _siegeUntil = EditorApplication.timeSinceStartup + SiegeSeconds;
            _siegeNext = 0.0;
            if (!_siegeArmed)
            {
                EditorApplication.update += SiegeTick;
                _siegeArmed = true;
            }
            Debug.Log("[SiegeProbe] armed for 30 s");
        }

        private static void SiegeTick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > _siegeUntil)
            {
                EditorApplication.update -= SiegeTick;
                _siegeArmed = false;
                Debug.Log("[SiegeProbe] done");
                return;
            }
            if (EditorApplication.timeSinceStartup < _siegeNext) return;
            _siegeNext = EditorApplication.timeSinceStartup + SiegePeriod;

            if (!TryWorld(out var em)) return;

            // One-shot editor queries, disposed: not a repeating sim path.
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<SiegeTag>(),
                ComponentType.ReadOnly<ArcherState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            q.Dispose();

            var sb = new StringBuilder();
            sb.Append("[SiegeProbe] units=").Append(Count<UnitTag>(em))
              .Append(" buildings=").Append(Count<BuildingTag>(em))
              .Append(" siege=").Append(ents.Length)
              .Append(" projectiles=").Append(Count<Projectile>(em))
              .Append(" stones=").Append(Count<CatapultShotTag>(em));

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var a = em.GetComponentData<ArcherState>(e);
                var p = em.GetComponentData<LocalTransform>(e).Position;

                sb.Append('\n').Append("  ").Append(Name(em, e)).Append('/').Append(FactionOf(em, e))
                  .Append(" at (").Append(p.x.ToString("0")).Append(',').Append(p.z.ToString("0")).Append(')');

                var tgt = em.HasComponent<Target>(e) ? em.GetComponentData<Target>(e).Value : Entity.Null;
                if (tgt != Entity.Null && em.Exists(tgt))
                {
                    float dist = em.HasComponent<LocalTransform>(tgt)
                        ? math.distance(p, em.GetComponentData<LocalTransform>(tgt).Position) : -1f;
                    int hp = em.HasComponent<Health>(tgt) ? em.GetComponentData<Health>(tgt).Value : -1;
                    sb.Append(" target=").Append(Name(em, tgt)).Append(" d=").Append(dist.ToString("0.0"))
                      .Append(" hp=").Append(hp);
                }
                else sb.Append(" target=none");

                bool moving = em.HasComponent<DesiredDestination>(e)
                    && em.GetComponentData<DesiredDestination>(e).Has != 0;
                sb.Append(" moving=").Append(moving ? 1 : 0)
                  .Append(" aim=").Append(a.AimTimer.ToString("0.00")).Append('/').Append(a.AimTimeRequired.ToString("0.00"))
                  .Append(" cd=").Append(a.CooldownTimer.ToString("0.00"))
                  .Append(" firing=").Append(a.IsFiring)
                  .Append(" retreat=").Append(a.IsRetreating)
                  .Append(" range=").Append(a.MinRange.ToString("0")).Append('-').Append(a.MaxRange.ToString("0"))
                  .Append(" traj=").Append(a.Trajectory);
            }

            Debug.Log(sb.ToString());
        }

        // ── Match probe ────────────────────────────────────────────────────

        private const double MatchSeconds = 240.0;
        private const double MatchPeriod = 10.0;
        private static double _matchUntil, _matchNext;
        private static bool _matchArmed;

        [MenuItem("Waning Border/Debug/Match Probe (240 s)")]
        public static void StartMatchProbe()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[MatchProbe] enter play mode first");
                return;
            }
            _matchUntil = EditorApplication.timeSinceStartup + MatchSeconds;
            _matchNext = 0.0;
            if (!_matchArmed)
            {
                EditorApplication.update += MatchTick;
                _matchArmed = true;
            }
            Debug.Log("[MatchProbe] armed for 240 s");
        }

        private static void MatchTick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > _matchUntil)
            {
                EditorApplication.update -= MatchTick;
                _matchArmed = false;
                Debug.Log("[MatchProbe] done");
                return;
            }
            if (EditorApplication.timeSinceStartup < _matchNext) return;
            _matchNext = EditorApplication.timeSinceStartup + MatchPeriod;

            if (!TryWorld(out var em)) return;

            var sb = new StringBuilder();
            sb.Append("[MatchProbe] t=").Append(Time.timeSinceLevelLoad.ToString("0")).Append("s units:");

            // Units per faction.
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>());
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                q.Dispose();
                var per = new Dictionary<Faction, int>();
                for (int i = 0; i < facs.Length; i++)
                {
                    per.TryGetValue(facs[i].Value, out int n);
                    per[facs[i].Value] = n + 1;
                }
                foreach (var kv in per) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            }

            // Production: every busy queue, what it is on.
            {
                var q = em.CreateEntityQuery(
                    ComponentType.ReadOnly<ProductionState>(),
                    ComponentType.ReadOnly<ProductionQueueItem>(),
                    ComponentType.ReadOnly<FactionTag>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                int busy = 0, train = 0, research = 0, upgrade = 0, queued = 0;
                var lines = new List<string>();
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    var ps = em.GetComponentData<ProductionState>(e);
                    var buf = em.GetBuffer<ProductionQueueItem>(e);
                    queued += buf.Length;
                    if (ps.Busy == 0 || buf.Length == 0) continue;
                    busy++;
                    var head = buf[0];
                    switch (head.Kind)
                    {
                        case ProductionKind.Train: train++; break;
                        case ProductionKind.Research: research++; break;
                        default: upgrade++; break;
                    }
                    if (lines.Count < 8)
                        lines.Add("  " + FactionOf(em, e) + " " + Name(em, e) + ": " + head.Kind + " "
                                  + (head.Kind == ProductionKind.BuildingUpgrade ? "L" + head.Level : head.Id.ToString())
                                  + " " + (ps.Total - ps.Remaining).ToString("0.0") + "/" + ps.Total.ToString("0.0")
                                  + "s, " + buf.Length + " queued");
                }
                sb.Append(" | production busy=").Append(busy)
                  .Append(" (train ").Append(train).Append(", research ").Append(research)
                  .Append(", upgrade ").Append(upgrade).Append(") queued=").Append(queued);
                foreach (var l in lines) sb.Append('\n').Append(l);
            }

            // Curse waves.
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<CurseWaveMember>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                int inFormation = 0, fighting = 0, marching = 0;
                var waves = new HashSet<int>();
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    waves.Add(em.GetComponentData<CurseWaveMember>(e).WaveId);
                    if (em.HasComponent<FormationMemberState>(e)) inFormation++;
                    if (em.HasComponent<Target>(e) && em.GetComponentData<Target>(e).Value != Entity.Null) fighting++;
                    if (em.HasComponent<DesiredDestination>(e) && em.GetComponentData<DesiredDestination>(e).Has != 0) marching++;
                }
                sb.Append("\n  curse: waves=").Append(waves.Count).Append(" members=").Append(ents.Length)
                  .Append(" inFormation=").Append(inFormation).Append(" marching=").Append(marching)
                  .Append(" fighting=").Append(fighting)
                  .Append(" groups=").Append(Count<FormationGroup>(em));
            }

            // Crystalling packs.
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<CrystallingPack>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                int maxPack = 0, buffed = 0;
                float maxBonus = 0f;
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    maxPack = math.max(maxPack, em.GetComponentData<CrystallingPack>(e).Size);
                    if (em.HasComponent<BorderBuff>(e))
                    {
                        float b = em.GetComponentData<BorderBuff>(e).AttBonus;
                        if (b > 0f) buffed++;
                        maxBonus = math.max(maxBonus, b);
                    }
                }
                sb.Append("\n  packs: crystallings=").Append(ents.Length).Append(" maxPack=").Append(maxPack)
                  .Append(" buffed=").Append(buffed).Append(" maxBonus=+").Append((maxBonus * 100f).ToString("0")).Append('%');
            }

            Debug.Log(sb.ToString());
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static bool TryWorld(out EntityManager em)
        {
            em = default;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            em = world.EntityManager;
            return true;
        }

        private static int Count<T>(EntityManager em) where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
            int n = q.CalculateEntityCount();
            q.Dispose();
            return n;
        }

        private static string Name(EntityManager em, Entity e)
        {
            if (em.HasComponent<UnitTypeId>(e)) return em.GetComponentData<UnitTypeId>(e).Value.ToString();
            if (em.HasComponent<DisplayName>(e)) return em.GetComponentData<DisplayName>(e).Value.ToString();
            return em.HasComponent<BuildingTag>(e) ? "building" : "?";
        }

        private static string FactionOf(EntityManager em, Entity e)
            => em.HasComponent<FactionTag>(e) ? em.GetComponentData<FactionTag>(e).Value.ToString() : "?";
    }
}
