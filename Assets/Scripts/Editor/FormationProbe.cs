// FormationProbe.cs
// Measures a marching formation without watching it (2026-11-13).
//   Waning Border > Debug > Formation Probe: Launch Octagon   (in Play mode)
//   Waning Border > Debug > Formation Probe: Dump
// The dump appends to Temp/formation-probe.txt: for every live formation
// group, each member's distance from its slot; and for every unit of the
// local squad, the distance to its nearest neighbour (the layout pitch is
// FormationMoveCommandHelper.Spacing).

using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.EditorTools
{
    public static class FormationProbe
    {
        [MenuItem("Waning Border/Debug/Formation Probe: Launch Octagon")]
        public static void Launch()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[FormationProbe] enter play mode first"); return; }
            string scene = ScenarioCatalog.Prepare(ScenarioType.FormationOctagon);
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            Debug.Log("[FormationProbe] loading " + scene);
        }

        static bool SpearmenOnly;
        static float OrderAngle;

        /// <summary>Only the Spearmen (the starting five), 14 m, each call 50 degrees further round.</summary>
        [MenuItem("Waning Border/Debug/Formation Probe: Order Spearmen 14 m")]
        public static void OrderSpearmen() { SpearmenOnly = true; Order(); SpearmenOnly = false; }

        /// <summary>Orders every local unit that cannot build to march 14 m as a Box.</summary>
        [MenuItem("Waning Border/Debug/Formation Probe: Order Local Squad 14 m")]
        public static void Order()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var squad = new System.Collections.Generic.List<Entity>();
            float3 sum = default;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>()))
            using (var units = q.ToEntityArray(Allocator.Temp))
                foreach (var u in units)
                {
                    if (em.GetComponentData<FactionTag>(u).Value != GameSettings.LocalPlayerFaction) continue;
                    if (em.HasComponent<CanBuild>(u)) continue;
                    if (SpearmenOnly && (!em.HasComponent<PresentationId>(u) || em.GetComponentData<PresentationId>(u).Id != 368)) continue;
                    squad.Add(u); sum += em.GetComponentData<LocalTransform>(u).Position;
                }
            if (squad.Count == 0) return;
            float3 dest = sum / squad.Count + new float3(math.cos(OrderAngle), 0f, math.sin(OrderAngle)) * 14f;
            OrderAngle += math.radians(50f);
            FormationMoveCommandHelper.Execute(em, squad, dest, FormationShape.Box, attackMove: false);
            Debug.Log($"[FormationProbe] ordered {squad.Count} units to {dest}");
            Directory.CreateDirectory("Temp");
            File.AppendAllText("Temp/formation-probe.txt", string.Format(CultureInfo.InvariantCulture,
                "=== ordered {0} units, travel heading {1:F0} deg" + System.Environment.NewLine,
                squad.Count, math.degrees(OrderAngle) - 50f));
        }

        [MenuItem("Waning Border/Debug/Formation Probe: Dump")]
        public static void Dump()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "--- t={0:F1}s pitch {1}", Time.timeSinceLevelLoad, FormationMoveCommandHelper.Spacing));

            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<FormationGroup>(), ComponentType.ReadOnly<FormationMember>()))
            using (var groups = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var g in groups)
                {
                    var grp = em.GetComponentData<FormationGroup>(g);
                    var members = em.GetBuffer<FormationMember>(g, true);
                    float max = 0f, sum = 0f; int n = 0, catching = 0;
                    var pts = new NativeList<float2>(Allocator.Temp);
                    for (int i = 0; i < members.Length; i++)
                    {
                        var m = members[i];
                        if (!em.Exists(m.Unit) || !em.HasComponent<LocalTransform>(m.Unit)) continue;
                        float3 p = em.GetComponentData<LocalTransform>(m.Unit).Position;
                        pts.Add(p.xz);
                        float d = math.distance(p.xz, m.SlotWorld.xz);
                        max = math.max(max, d); sum += d; n++;
                        if (m.CatchingUp != 0) catching++;
                    }
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "group {0}: state {1} speed {2:F2} members {3} catching-up {4}; off slot mean {5:F3} max {6:F3}; {7}",
                        g.Index, grp.State, grp.GroupSpeed, n, catching, n > 0 ? sum / n : 0f, max, Neighbours(pts)));
                    pts.Dispose();
                }
                if (groups.Length == 0) sb.AppendLine("no formation group alive");
            }

            // Every local Spearman: where it stands and where it was told to go.
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<PresentationId>()))
            using (var units = q.ToEntityArray(Allocator.Temp))
            {
                var here = new NativeList<float2>(Allocator.Temp);
                var goals = new NativeList<float2>(Allocator.Temp);
                foreach (var u in units)
                {
                    if (em.GetComponentData<FactionTag>(u).Value != GameSettings.LocalPlayerFaction) continue;
                    if (em.GetComponentData<PresentationId>(u).Id != 368) continue;
                    float3 p = em.GetComponentData<LocalTransform>(u).Position;
                    bool has = em.HasComponent<DesiredDestination>(u) && em.GetComponentData<DesiredDestination>(u).Has != 0;
                    float3 d = em.HasComponent<DesiredDestination>(u) ? em.GetComponentData<DesiredDestination>(u).Position : p;
                    bool member = em.HasComponent<FormationMemberState>(u) && em.IsComponentEnabled<FormationMemberState>(u);
                    here.Add(p.xz); if (has) goals.Add(d.xz);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  spearman {0}: pos {1:F2} {2:F2}  dest {3:F2} {4:F2} ({5})  to go {6:F2}  member {7}  heading {8:F0} deg",
                        u.Index, p.x, p.z, d.x, d.z, has ? "active" : "none", math.distance(p.xz, d.xz), member, Heading(em, u)));
                }
                sb.AppendLine("  spearmen standing: " + Neighbours(here) + (goals.Length > 1 ? ";  their destinations: " + Neighbours(goals) : ""));
                here.Dispose(); goals.Dispose();
            }

            // The local squad, formed or not.
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>()))
            using (var units = q.ToEntityArray(Allocator.Temp))
            {
                var pts = new NativeList<float2>(Allocator.Temp);
                foreach (var u in units)
                    if (em.GetComponentData<FactionTag>(u).Value == GameSettings.LocalPlayerFaction)
                        pts.Add(em.GetComponentData<LocalTransform>(u).Position.xz);
                sb.AppendLine("local units " + pts.Length + ": " + Neighbours(pts));
                pts.Dispose();
            }
            Directory.CreateDirectory("Temp");
            File.AppendAllText("Temp/formation-probe.txt", sb.ToString());
            Debug.Log("[FormationProbe]\n" + sb);
        }

        /// <summary>Compass of the unit's forward on XZ: 0 = +X, 90 = +Z (the order angle's convention).</summary>
        static float Heading(EntityManager em, Entity u)
        {
            float3 f = math.mul(em.GetComponentData<LocalTransform>(u).Rotation, new float3(0f, 0f, 1f));
            float deg = math.degrees(math.atan2(f.z, f.x));
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>Appends the animator rate and move-clip ground speed of up to three spawned Spearman views.</summary>
        [MenuItem("Waning Border/Debug/Formation Probe: Spearman Animation")]
        public static void SpearmanAnimation()
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.transform.parent != null || !go.name.StartsWith("Entity_") || !go.name.EndsWith("_368")) continue;
                var an = go.GetComponentInChildren<Animator>();
                if (an == null || an.runtimeAnimatorController == null) continue;
                var st = an.GetCurrentAnimatorStateInfo(0);
                string clips = "";
                foreach (var c in an.runtimeAnimatorController.animationClips)
                    if (c != null) clips += string.Format(CultureInfo.InvariantCulture, " {0}:{1:F2}m/s,{2:F2}s", c.name, new Vector2(c.averageSpeed.x, c.averageSpeed.z).magnitude, c.length);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  anim {0}: animator.speed {1:F3}  state speed {2:F3} x mult {3:F3}  moving {4}  view scale {5:F3}  clips{6}",
                    go.name, an.speed, st.speed, st.speedMultiplier, st.IsName("Move"), go.transform.lossyScale.y, clips));
                if (++n >= 3) break;
            }
            Directory.CreateDirectory("Temp");
            File.AppendAllText("Temp/formation-probe.txt", sb.ToString());
        }

        /// <summary>Spawns five Archers beside the local Spearmen (a look at the unit without ageing up).</summary>
        [MenuItem("Waning Border/Debug/Formation Probe: Spawn 5 Archers")]
        public static void SpawnArchers()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            float3 at = default; bool found = false;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<PresentationId>()))
            using (var units = q.ToEntityArray(Allocator.Temp))
                foreach (var u in units)
                    if (em.GetComponentData<FactionTag>(u).Value == GameSettings.LocalPlayerFaction
                        && em.GetComponentData<PresentationId>(u).Id == 368)
                    { at = em.GetComponentData<LocalTransform>(u).Position; found = true; break; }
            if (!found) return;
            for (int i = 0; i < 5; i++)
                TheWaningBorder.Entities.UnitFactory.Create(em, "Alanthor_Archer", at + new float3(3f + i, 0f, 3f), GameSettings.LocalPlayerFaction);
        }

        /// <summary>Photographs the spawned views of one presentation id (Temp/formation-probe-<id>.png).</summary>
        [MenuItem("Waning Border/Debug/Formation Probe: Shoot Archers")]
        public static void ShootArchers()
        {
            Bounds b = default; bool first = true;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.transform.parent != null || !go.name.StartsWith("Entity_") || !go.name.EndsWith("_202")) continue;
                var bb = LegionaryShot.BoundsOf(go);
                if (first) { b = bb; first = false; } else b.Encapsulate(bb);
            }
            if (first) return;
            b.Expand(0.6f);
            LegionaryShot.Shoot(b, 200f, 30f, 1600, 900, "Temp/formation-probe-202.png");
        }

        /// <summary>Photographs the Blue faction's Spearmen (Temp/formation-probe-blue.png).</summary>
        [MenuItem("Waning Border/Debug/Formation Probe: Shoot Blue Spearmen")]
        public static void ShootBlueSpearmen()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var evm = TheWaningBorder.Rendering.EntityViewManager.Instance;
            Bounds b = default; bool first = true;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<PresentationId>()))
            using (var units = q.ToEntityArray(Allocator.Temp))
                foreach (var u in units)
                {
                    if (em.GetComponentData<FactionTag>(u).Value != Faction.Blue || em.GetComponentData<PresentationId>(u).Id != 368) continue;
                    if (evm == null || !evm.TryGetView(u, out var go) || go == null) continue;
                    var bb = LegionaryShot.BoundsOf(go);
                    if (first) { b = bb; first = false; } else if ((bb.center - b.center).magnitude < 12f) b.Encapsulate(bb);
                }
            if (first) return;
            b.Expand(0.5f);
            LegionaryShot.Shoot(b, 200f, 28f, 1600, 900, "Temp/formation-probe-blue.png");
        }

        // ── Marching past an obstacle (2026-11-13) ──────────────────────────
        // "Wall Run: Prepare" finds the nearest impassable ground to the local
        // Spearmen, lays a 24 m course running PAST it at WallRunOffset metres
        // from its edge, and sends the squad to the course's start. "Wall Run:
        // Go" marches the course; "Open Run: Go" marches 24 m over the open
        // ground the squad stands on, for comparison. "Run: Sample" appends the
        // squad's centroid and time to Temp/formation-probe.txt.
        static float3 _runFrom, _runTo;
        static bool _haveRun;
        // Metres between the course and the rock's edge: Temp/wallrun.txt, or 1.5.
        static float WallRunOffset
        {
            get
            {
                if (File.Exists("Temp/wallrun.txt")
                    && float.TryParse(File.ReadAllText("Temp/wallrun.txt").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
                return 1.5f;
            }
        }

        // After a "Go": twice a second, the squad's speed, whether its
        // formation leader is easing, and the worst member-to-spot distance.
        static double _recUntil, _recNext;
        static float3 _recLast;
        static float _recLastT;

        static void StartRecorder(float3 from)
        {
            _recLast = from; _recLastT = Time.timeSinceLevelLoad;
            _recNext = EditorApplication.timeSinceStartup + 0.5;
            _recUntil = EditorApplication.timeSinceStartup + 22.0;
            EditorApplication.update -= Record;
            EditorApplication.update += Record;
        }

        static void Record()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > _recUntil) { EditorApplication.update -= Record; return; }
            if (EditorApplication.timeSinceStartup < _recNext) return;
            _recNext += 0.5;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var squad = new System.Collections.Generic.List<Entity>();
            if (!Spearmen(em, squad, out float3 c)) return;
            float now = Time.timeSinceLevelLoad;
            float speed = now > _recLastT ? math.distance(c.xz, _recLast.xz) / (now - _recLastT) : 0f;
            _recLast = c; _recLastT = now;
            int groups = 0, easing = 0; float worst = 0f;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<FormationGroup>(), ComponentType.ReadOnly<FormationMember>()))
            using (var gs = q.ToEntityArray(Allocator.Temp))
                foreach (var ge in gs)
                {
                    var g = em.GetComponentData<FormationGroup>(ge);
                    var buf = em.GetBuffer<FormationMember>(ge, true);
                    bool mine = false;
                    float3 right = math.cross(new float3(0f, 1f, 0f), g.Facing);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        if (!squad.Contains(buf[i].Unit)) continue;
                        mine = true;
                        float3 spot = g.LeaderPos + right * buf[i].Slot.x + g.Facing * buf[i].Slot.y;
                        float3 pos = em.GetComponentData<LocalTransform>(buf[i].Unit).Position;
                        worst = math.max(worst, math.distance(spot.xz, pos.xz));
                    }
                    if (!mine) continue;
                    groups++; if (g.Easing != 0) easing++;
                }
            File.AppendAllText("Temp/formation-probe.txt", string.Format(CultureInfo.InvariantCulture,
                "  rec t {0:F1}  speed {1:F2} m/s  groups {2} easing {3}  worst offset {4:F2} m{5}", now - _goTime, speed, groups, easing, worst, System.Environment.NewLine));
        }

        static bool Spearmen(EntityManager em, System.Collections.Generic.List<Entity> squad, out float3 centroid)
        {
            centroid = default;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<PresentationId>()))
            using (var units = q.ToEntityArray(Allocator.Temp))
                foreach (var u in units)
                {
                    if (em.GetComponentData<FactionTag>(u).Value != GameSettings.LocalPlayerFaction) continue;
                    if (em.GetComponentData<PresentationId>(u).Id != 368) continue;
                    squad.Add(u); centroid += em.GetComponentData<LocalTransform>(u).Position;
                }
            if (squad.Count == 0) return false;
            centroid /= squad.Count;
            return true;
        }

        [MenuItem("Waning Border/Debug/Formation Probe: Wall Run: Prepare")]
        public static void WallRunPrepare()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var squad = new System.Collections.Generic.List<Entity>();
            if (!Spearmen(em, squad, out float3 c)) return;
            var NQ = typeof(TheWaningBorder.Systems.Navigation.NavGridQuery);
            int2 c0 = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(c);
            bool had = _haveRun; float3 hadFrom = _runFrom, hadTo = _runTo;
            _haveRun = false;
            string note = "no obstacle found";
            // Nearest blocked cell with a straight, standable 24 m course beside it.
            for (int r = 2; r <= 70 && !_haveRun; r++)
                for (int dz = -r; dz <= r && !_haveRun; dz++)
                    for (int dx = -r; dx <= r && !_haveRun; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                        int2 cell = c0 + new int2(dx, dz);
                        if (TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(cell)) continue;
                        float3 wall = TheWaningBorder.Systems.Navigation.NavGridQuery.CellCenter(cell);
                        float3 toSquad = math.normalizesafe(new float3(c.x - wall.x, 0f, c.z - wall.z));
                        float3 along = new float3(-toSquad.z, 0f, toSquad.x);
                        float3 mid = wall + toSquad * (0.5f + WallRunOffset);
                        float3 from = mid - along * 12f, to = mid + along * 12f;
                        bool clear = true;
                        for (int k = 0; k <= 24 && clear; k++)
                        {
                            float3 pt = math.lerp(from, to, k / 24f);
                            if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsWorldStandable(pt)) clear = false;
                        }
                        if (!clear) continue;
                        // Count how much of the course really has rock beside it.
                        int beside = 0;
                        for (int k = 0; k <= 24; k++)
                        {
                            float3 pt = math.lerp(from, to, k / 24f) - toSquad * (WallRunOffset + 0.6f);
                            if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(pt))) beside++;
                        }
                        if (beside < 10) continue;
                        _runFrom = from; _runTo = to; _haveRun = true;
                        note = string.Format(CultureInfo.InvariantCulture, "obstacle at {0:F1} {1:F1}, {2} m away; rock beside {3} of 25 course points", wall.x, wall.z, r, beside);
                    }
            if (!_haveRun && had) { _runFrom = hadFrom; _runTo = hadTo; _haveRun = true; note = "kept the previous course"; }
            File.AppendAllText("Temp/formation-probe.txt", "=== wall run (offset " + WallRunOffset.ToString(CultureInfo.InvariantCulture) + "): " + note + System.Environment.NewLine);
            if (!_haveRun) return;
            FormationMoveCommandHelper.Execute(em, squad, _runFrom, FormationShape.Box, attackMove: false, math.normalizesafe(_runTo - _runFrom), 0);
        }

        static float _goTime;
        static float3 _goFrom;

        [MenuItem("Waning Border/Debug/Formation Probe: Wall Run: Go")]
        public static void WallRunGo()
        {
            if (!_haveRun) return;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var squad = new System.Collections.Generic.List<Entity>();
            if (!Spearmen(em, squad, out float3 c)) return;
            _goTime = Time.timeSinceLevelLoad; _goFrom = c;
            File.AppendAllText("Temp/formation-probe.txt", "=== WALL run go" + System.Environment.NewLine);
            StartRecorder(c);
            FormationMoveCommandHelper.Execute(em, squad, _runTo, FormationShape.Box, attackMove: false);
        }

        [MenuItem("Waning Border/Debug/Formation Probe: Open Run: Go")]
        public static void OpenRunGo()
        {
            if (!_haveRun) return;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var squad = new System.Collections.Generic.List<Entity>();
            if (!Spearmen(em, squad, out float3 c)) return;
            _goTime = Time.timeSinceLevelLoad; _goFrom = c;
            // Back along the same line, but 8 m out from the rock.
            float3 along = math.normalizesafe(_runFrom - _runTo);
            float3 away = new float3(-along.z, 0f, along.x);
            float3 probe = c + away * 8f;
            if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsWorldStandable(probe)) away = -away;
            File.AppendAllText("Temp/formation-probe.txt", "=== OPEN run go" + System.Environment.NewLine);
            StartRecorder(c);
            FormationMoveCommandHelper.Execute(em, squad, c + away * 8f + along * 24f, FormationShape.Box, attackMove: false);
        }

        [MenuItem("Waning Border/Debug/Formation Probe: Run: Sample")]
        public static void RunSample()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var squad = new System.Collections.Generic.List<Entity>();
            if (!Spearmen(em, squad, out float3 c)) return;
            int moving = 0;
            foreach (var u in squad)
                if (em.HasComponent<DesiredDestination>(u) && em.GetComponentData<DesiredDestination>(u).Has != 0) moving++;
            float t = Time.timeSinceLevelLoad - _goTime;
            float d = math.distance(c.xz, _goFrom.xz);
            File.AppendAllText("Temp/formation-probe.txt", string.Format(CultureInfo.InvariantCulture,
                "  t {0:F1}s  travelled {1:F1} m  mean {2:F2} m/s  moving {3}/{4}{5}", t, d, t > 0.1f ? d / t : 0f, moving, squad.Count, System.Environment.NewLine));
        }

        static string Neighbours(NativeList<float2> pts)
        {
            if (pts.Length < 2) return "nearest neighbour n/a";
            float min = float.MaxValue, max = 0f, sum = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                float best = float.MaxValue;
                for (int j = 0; j < pts.Length; j++)
                    if (i != j) best = math.min(best, math.distance(pts[i], pts[j]));
                min = math.min(min, best); max = math.max(max, best); sum += best;
            }
            return string.Format(CultureInfo.InvariantCulture, "nearest neighbour min {0:F2} mean {1:F2} max {2:F2}", min, sum / pts.Length, max);
        }
    }
}
