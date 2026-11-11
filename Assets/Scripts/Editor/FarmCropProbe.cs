// FarmCropProbe.cs
// Editor-only look test for the Farm crop field (FarmCropField), driven over
// the agent bridge. After a quick skirmish has loaded (SiegeProbe launchers):
//
//   Farm Crops: Spawn Test Farms — two finished Farms beside the local
//   player's capital, the second close enough that the rings overlap, and the
//   camera brought down over them.
//   Farm Crops: Growth 0.3 / Ripe / Live — overrides the growth every field
//   shows, so a look test needs no six-minute wait.
//
// Presentation test only: the spawned Farms are ordinary entities, made with
// Farm.Create like any other.

using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Rendering;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.EditorTools
{
    public static class FarmCropProbe
    {
        [MenuItem("Waning Border/Debug/Farm Crops: Spawn Test Farms")]
        public static void SpawnTestFarms()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[FarmCropProbe] play mode first"); return; }
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null) { Debug.LogWarning("[FarmCropProbe] no world"); return; }
            var em = world.EntityManager;

            var me = GameSettings.LocalPlayerFaction;
            float3 capital = float3.zero;
            bool found = false;
            using (var q = em.CreateEntityQuery(typeof(HallTag), typeof(FactionTag), typeof(LocalTransform)))
            using (var ents = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in ents)
                    if (em.GetComponentData<FactionTag>(e).Value == me)
                    { capital = em.GetComponentData<LocalTransform>(e).Position; found = true; break; }
            }
            if (!found) { Debug.LogWarning("[FarmCropProbe] no capital for " + me); return; }

            // The freest spot about 30 m out, so the field has room to show.
            float2 best = capital.xz + new float2(30f, 0f);
            int bestFree = -1;
            for (int k = 0; k < 16; k++)
            {
                float a = k * math.PI * 2f / 16f;
                float2 p = capital.xz + new float2(math.cos(a), math.sin(a)) * 30f;
                p = math.round(p / 2f) * 2f + 1f;   // a 3-cell footprint centres on a cell centre
                int free = FarmRing.FreePermilleAt(em, p, Entity.Null, FarmRing.Ghost.None);
                if (free > bestFree) { bestFree = free; best = p; }
            }

            float2 dir = math.normalizesafe(best - capital.xz);
            float2 second = best + new float2(-dir.y, dir.x) * 18f;
            var a1 = Farm.Create(em, new float3(best.x, TerrainUtility.GetHeight(best.x, best.y), best.y), me);
            var a2 = Farm.Create(em, new float3(second.x, TerrainUtility.GetHeight(second.x, second.y), second.y), me);
            Debug.Log($"[FarmCropProbe] Farms {a1} at {best} (free {bestFree}‰) and {a2} at {second}; capital {capital}");

            FarmCropField.DebugGrowthOverride = 1f;
            _pin = new Vector3((best.x + second.x) * 0.5f, capital.y, (best.y + second.y) * 0.5f);
            Focus(_pin.Value, 38f);
            // Hold the camera on the Farms: an unattended Game view edge-scrolls
            // wherever the cursor happens to rest.
            EditorApplication.update -= HoldCamera;
            EditorApplication.update += HoldCamera;
        }

        private static Vector3? _pin;

        private static void HoldCamera()
        {
            if (!EditorApplication.isPlaying || _pin == null) { EditorApplication.update -= HoldCamera; _pin = null; return; }
            TheWaningBorder.CameraRig.CameraController.FocusOn(_pin.Value, instant: true);
        }

        [MenuItem("Waning Border/Debug/Farm Crops: Level 1")]
        public static void Level1() { FarmCropField.DebugLevelOverride = 1; }

        [MenuItem("Waning Border/Debug/Farm Crops: Level 2")]
        public static void Level2() { FarmCropField.DebugLevelOverride = 2; }

        [MenuItem("Waning Border/Debug/Farm Crops: Level 3")]
        public static void Level3() { FarmCropField.DebugLevelOverride = 3; }

        [MenuItem("Waning Border/Debug/Farm Crops: Live Level")]
        public static void LevelLive() { FarmCropField.DebugLevelOverride = null; }

        // ── The level-3 wall in the SIMULATION (FarmStoneWallSystem) ──

        [MenuItem("Waning Border/Debug/Farm Wall: Sim Level 3 (local player)")]
        public static void SimLevel3()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[FarmCropProbe] play mode first"); return; }
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            TheWaningBorder.Systems.Buildings.FactionBuildingLevels.ApplyToAll(em, GameSettings.LocalPlayerFaction, Farm.BuildingId, 3);
            FarmCropField.DebugLevelOverride = null;
            Debug.Log("[FarmCropProbe] Farm level 3 for " + GameSettings.LocalPlayerFaction);
        }

        [MenuItem("Waning Border/Debug/Farm Wall: Report")]
        public static void WallReport()
        {
            if (!EditorApplication.isPlaying) return;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            using (var q = em.CreateEntityQuery(typeof(FarmTag), typeof(FarmWallState)))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var f in ents)
                {
                    var st = em.GetComponentData<FarmWallState>(f);
                    var slots = em.GetBuffer<FarmWallSlot>(f);
                    int up = 0;
                    for (int i = 0; i < slots.Length; i++) if (slots[i].Piece != Entity.Null) up++;
                    Debug.Log($"[FarmWall] farm {f} laid={st.Laid} sig={st.Signature:X8} quiet={st.QuietTicks} slots={slots.Length} standing={up}");
                }
            using (var q = em.CreateEntityQuery(typeof(FarmWallPiece), typeof(Health)))
                Debug.Log($"[FarmWall] piece entities: {q.CalculateEntityCount()}");
        }

        [MenuItem("Waning Border/Debug/Farm Wall: Break 3 Pieces")]
        public static void BreakPieces()
        {
            if (!EditorApplication.isPlaying) return;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            using var q = em.CreateEntityQuery(typeof(FarmWallPiece), typeof(Health));
            using var ents = q.ToEntityArray(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length && n < 3; i += 4, n++)
            {
                var h = em.GetComponentData<Health>(ents[i]);
                h.Value = 0;
                em.SetComponentData(ents[i], h);
            }
            Debug.Log($"[FarmWall] broke {n} pieces");
        }

        private static readonly System.Collections.Generic.List<Entity> _hostiles = new System.Collections.Generic.List<Entity>();
        private static Entity _victim, _testFarm;
        private static float2 _goal;

        /// <summary>Four Red Spearmen outside the first walled Farm: two ordered
        /// onto a wall piece, two ordered to the far side of the field (through
        /// the gaps). Read the outcome with Farm Wall: Hostile Report.</summary>
        [MenuItem("Waning Border/Debug/Farm Wall: Hostile Test")]
        public static void HostileTest()
        {
            if (!EditorApplication.isPlaying) return;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            _hostiles.Clear(); _victim = Entity.Null; _testFarm = Entity.Null;
            using (var q = em.CreateEntityQuery(typeof(FarmTag), typeof(FarmWallState)))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                if (ents.Length > 0) _testFarm = ents[0];
            if (_testFarm == Entity.Null) { Debug.LogWarning("[FarmWall] no walled Farm"); return; }
            var c = em.GetComponentData<LocalTransform>(_testFarm).Position.xz;
            float R = FarmRing.OuterRadius;
            var start = c + new float2(-(R + 6f), 0f);
            _goal = c + new float2(R - 3f, 0f);
            float best = float.MaxValue;
            using (var q = em.CreateEntityQuery(typeof(FarmWallPiece), typeof(Health)))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    var p = em.GetComponentData<FarmWallPiece>(e);
                    if (p.Farm != _testFarm) continue;
                    float d = math.distancesq((p.A + p.B) * 0.5f, start);
                    if (d < best) { best = d; _victim = e; }
                }
            for (int i = 0; i < 4; i++)
            {
                var p = start + new float2(0f, (i - 1.5f) * 1.5f);
                var u = TheWaningBorder.Entities.UnitFactory.Create(em, "Spearman",
                    new float3(p.x, TerrainUtility.GetHeight(p.x, p.y), p.y), Faction.Red);
                _hostiles.Add(u);
            }
            Debug.Log($"[FarmWall] hostile test: farm {_testFarm} victim {_victim} start {start} goal {_goal}");
            EditorApplication.delayCall += IssueHostileOrders;
        }

        private static void IssueHostileOrders()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            for (int i = 0; i < _hostiles.Count; i++)
            {
                if (!em.Exists(_hostiles[i])) continue;
                if (i < 2 && _victim != Entity.Null)
                    TheWaningBorder.Core.Commands.CommandRouter.IssueAttack(em, _hostiles[i], _victim,
                        TheWaningBorder.Core.Commands.CommandSource.AI);
                else
                    TheWaningBorder.Core.Commands.CommandRouter.IssueMove(em, _hostiles[i],
                        new float3(_goal.x, TerrainUtility.GetHeight(_goal.x, _goal.y), _goal.y),
                        TheWaningBorder.Core.Commands.CommandSource.AI);
            }
            Debug.Log("[FarmWall] hostile orders issued");
        }

        [MenuItem("Waning Border/Debug/Farm Wall: Hostile Report")]
        public static void HostileReport()
        {
            if (!EditorApplication.isPlaying) return;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            string v = _victim != Entity.Null && em.Exists(_victim)
                ? em.GetComponentData<Health>(_victim).Value + "/" + em.GetComponentData<Health>(_victim).Max
                : "gone";
            Debug.Log($"[FarmWall] victim {_victim} hp {v}");
            var c = _testFarm != Entity.Null && em.Exists(_testFarm) ? em.GetComponentData<LocalTransform>(_testFarm).Position.xz : float2.zero;
            for (int i = 0; i < _hostiles.Count; i++)
            {
                if (!em.Exists(_hostiles[i])) { Debug.Log($"[FarmWall] hostile {i} gone"); continue; }
                var p = em.GetComponentData<LocalTransform>(_hostiles[i]).Position.xz;
                Debug.Log($"[FarmWall] hostile {i} at {p} dCentre {math.distance(p, c):F1} dGoal {math.distance(p, _goal):F1}");
            }
        }

        [MenuItem("Waning Border/Debug/Farm Crops: Growth 0")]
        public static void GrowthBare() { FarmCropField.DebugGrowthOverride = 0f; }

        [MenuItem("Waning Border/Debug/Farm Crops: Growth 0.3")]
        public static void GrowthYoung() { FarmCropField.DebugGrowthOverride = 0.3f; }

        [MenuItem("Waning Border/Debug/Farm Crops: Growth 0.65")]
        public static void GrowthMid() { FarmCropField.DebugGrowthOverride = 0.65f; }

        [MenuItem("Waning Border/Debug/Farm Crops: Ripe")]
        public static void GrowthRipe() { FarmCropField.DebugGrowthOverride = 1f; }

        [MenuItem("Waning Border/Debug/Farm Crops: Live Growth")]
        public static void GrowthLive() { FarmCropField.DebugGrowthOverride = null; }

        [MenuItem("Waning Border/Debug/Farm Crops: Camera Close")]
        public static void CameraClose() { SetZoom(26f); }

        [MenuItem("Waning Border/Debug/Farm Crops: Camera Far")]
        public static void CameraFar() { SetZoom(60f); }

        private static void Focus(Vector3 at, float zoom)
        {
            TheWaningBorder.CameraRig.CameraController.FocusOn(at, instant: true);
            SetZoom(zoom);
        }

        private static void SetZoom(float zoom)
        {
            var cam = Object.FindFirstObjectByType<TheWaningBorder.CameraRig.CameraController>();
            if (cam == null) return;
            var f = typeof(TheWaningBorder.CameraRig.CameraController)
                .GetField("_targetZoom", BindingFlags.Instance | BindingFlags.NonPublic);
            f?.SetValue(cam, zoom);
        }
    }
}
