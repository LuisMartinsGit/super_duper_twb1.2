// SnapshotState.cs
// The non-ECS half of a saved game: simulation state that lives in statics and
// in system fields, which WorldSnapshot cannot see.
// docs/Design/Replays_And_Saves.md §3
//
// THREE KINDS OF SECTION
//   static:<Type>   the static fields of a listed owner (territory meters, curse
//                   wrath, the blood map, hero ledgers, cultures, clocks…)
//   sys:<Type>      every field of a simulation SYSTEM, managed or ISystem —
//                   timers (SimCadence.Periodic), fractional carries, RNG
//                   streams, the curse brain. Found by reflection, so a system
//                   added tomorrow is saved without touching this file.
//   ext:<key>       anything another assembly registers (spawn positions from
//                   Bootstrap) or that needs custom handling (fog, research).
//
// SYSTEMS THAT ARE NOT SAVED
//   * the AI (TheWaningBorder.AI): it is outside the simulation and re-plans
//     from the restored board;
//   * presentation / UI / rendering;
//   * OptOut below: the nav, stamp and cache stack. Their fields describe
//     native data that is rebuilt from the map and the restored buildings —
//     restoring "already stamped" over an unstamped field would leave every
//     building out of the cost field.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core.Multiplayer;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Core.Save
{
    public static class SnapshotState
    {
        /// <summary>Owners whose STATIC fields are match state.</summary>
        static readonly Type[] StaticOwners =
        {
            typeof(TheWaningBorder.World.Regions.TerritoryOwnership),
            typeof(TheWaningBorder.Influence.PlayerInfluenceMap),
            typeof(TheWaningBorder.Influence.BloodMap),
            typeof(TheWaningBorder.Systems.Border.CurseWrath),
            typeof(TheWaningBorder.Abilities.HeroTrainLimit),
            typeof(TheWaningBorder.Abilities.HeroRevival),
            typeof(TheWaningBorder.Abilities.AlanthorActiveHelper),
            typeof(FactionColors),
            typeof(TheWaningBorder.Core.Commands.Issuing.SelectionOrders),
            typeof(TheWaningBorder.Core.Diagnostics.MatchScore),
            typeof(TheWaningBorder.Economy.EconomyLedger),
            typeof(SimClock),
            typeof(NetworkIdGenerator),
            typeof(TheWaningBorder.Entities.ShardrootEmpowerment),
        };

        /// <summary>Systems whose state is derived and rebuilt after a load.</summary>
        static readonly HashSet<string> OptOut = new HashSet<string>
        {
            "NavGridBootstrapSystem", "TerrainCostBakeSystem", "TraversalProfileBootstrapSystem",
            "CostFieldStampSystem", "BuildingCostStampSystem", "VeilNavStampSystem",
            "IncrementalPortalRebuildSystem", "PortalGraphBuildSystem", "FlowSegmentSystem",
            "SpatialHashRebuildSystem", "NavRequestSchedulerSystem", "WallPortalDetectionSystem",
            "GoalFlowFieldSystem", "PassabilityBuildingSync", "DeterminismReplaySystem",
            "PresentationViewTagSeedSystem", "SectUnitLeverSystem", "SectFortitudeHpSystem",
            "VeilFieldSystem",
        };

        static readonly string[] ExcludedNamespaces =
        {
            "TheWaningBorder.AI", "TheWaningBorder.Rendering", "TheWaningBorder.UI",
            "TheWaningBorder.Input", "TheWaningBorder.Presentation",
        };

        // ── extension sections ───────────────────────────────────────────

        struct Ext { public Func<byte[]> Save; public Action<byte[]> Load; }
        static readonly Dictionary<string, Ext> _ext = new Dictionary<string, Ext>();

        /// <summary>Register a section another assembly owns (written at save,
        /// handed back at load). Re-registering a key replaces it.</summary>
        public static void RegisterSection(string key, Func<byte[]> save, Action<byte[]> load)
            => _ext[key] = new Ext { Save = save, Load = load };

        // ── capture ──────────────────────────────────────────────────────

        public static byte[] Capture(EntityWorld world, out string report)
        {
            var codec = new SnapshotCodec
            {
                EntityToId = EntityIds(world.EntityManager, out _),
                SavedSimEpoch = SimCadence.Epoch,
                SavedMatchEpoch = MatchLifecycle.MatchEpoch,
            };
            var sections = new List<(string, byte[])>();

            foreach (var t in StaticOwners)
                sections.Add(("static:" + t.FullName, Block(w => codec.WriteFields(w, StaticFields(t), null))));

            int systems = 0;
            foreach (var t in SimulationSystemTypes())
            {
                var statics = StaticFields(t);
                if (statics.Count > 0)
                    sections.Add(("sysstatic:" + t.FullName, Block(w => codec.WriteFields(w, statics, null))));

                byte[] inst = t.IsValueType ? CaptureUnmanaged(world, t, codec) : CaptureManaged(world, t, codec);
                if (inst != null) { sections.Add(("sys:" + t.FullName, inst)); systems++; }
            }

            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null)
                sections.Add(("inst:FactionResearchState",
                    Block(w => codec.WriteFields(w, InstanceFields(research.GetType()), research))));

            var fog = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;
            var revealed = fog != null ? fog.ExportRevealed() : null;
            if (revealed != null) sections.Add(("ext:fog", revealed));

            foreach (var kv in _ext)
            {
                byte[] b = null;
                try { b = kv.Value.Save?.Invoke(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
                if (b != null) sections.Add(("ext:" + kv.Key, b));
            }

            report = $"{systems} systems, {StaticOwners.Length} static owners, {_ext.Count} extension section(s)";
            return Block(w =>
            {
                w.Write(codec.SavedSimEpoch);
                w.Write(codec.SavedMatchEpoch);
                w.Write(sections.Count);
                foreach (var (key, bytes) in sections)
                {
                    w.Write(key);
                    w.Write(bytes.Length);
                    w.Write(bytes);
                }
            });
        }

        // ── restore ──────────────────────────────────────────────────────

        public static void Restore(EntityWorld world, byte[] data, out string report)
        {
            var codec = new SnapshotCodec
            {
                NewSimEpoch = SimCadence.Epoch,
                NewMatchEpoch = MatchLifecycle.MatchEpoch,
            };
            EntityIds(world.EntityManager, out codec.IdToEntity);

            var byKey = new Dictionary<string, byte[]>();
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                codec.SavedSimEpoch = r.ReadInt32();
                codec.SavedMatchEpoch = r.ReadInt32();
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    string key = r.ReadString();
                    int len = r.ReadInt32();
                    byKey[key] = r.ReadBytes(len);
                }
            }

            int fields = 0, systems = 0;
            foreach (var t in StaticOwners)
                if (byKey.TryGetValue("static:" + t.FullName, out var b))
                    fields += Read(b, rd => codec.ReadFields(rd, StaticFields(t), null));

            foreach (var t in SimulationSystemTypes())
            {
                if (byKey.TryGetValue("sysstatic:" + t.FullName, out var sb))
                    fields += Read(sb, rd => codec.ReadFields(rd, StaticFields(t), null));
                if (!byKey.TryGetValue("sys:" + t.FullName, out var ib)) continue;
                // Epoch latches are translated for every system: the only one
                // that allocates native containers inside its new-match block
                // (VeilNavStampSystem) is in OptOut. Set KeepOwnLifecycle for a
                // system that ever needs its own reset to run on load.
                codec.KeepOwnLifecycle = false;
                fields += t.IsValueType ? RestoreUnmanaged(world, t, codec, ib) : RestoreManaged(world, t, codec, ib);
                codec.KeepOwnLifecycle = false;
                systems++;
            }

            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null && byKey.TryGetValue("inst:FactionResearchState", out var rb))
                fields += Read(rb, rd => codec.ReadFields(rd, InstanceFields(research.GetType()), research));

            var fog = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;
            if (fog != null && byKey.TryGetValue("ext:fog", out var fb)) fog.ImportRevealed(fb);

            foreach (var kv in _ext)
                if (byKey.TryGetValue("ext:" + kv.Key, out var eb))
                {
                    try { kv.Value.Load?.Invoke(eb); }
                    catch (Exception e) { UnityEngine.Debug.LogException(e); }
                }

            report = $"{systems} systems, {fields} fields restored"
                     + (codec.Skipped.Count > 0 ? $", {codec.Skipped.Count} lifecycle field(s) left to their owners" : "");
            if (codec.Skipped.Count > 0)
                UnityEngine.Debug.Log("[Snapshot] Left to their owners' own setup: " + string.Join(", ", codec.Skipped));
        }

        // ── systems ──────────────────────────────────────────────────────

        static List<Type> _systemTypes;

        static List<Type> SimulationSystemTypes()
        {
            if (_systemTypes != null) return _systemTypes;
            var list = new List<Type>();
            Type[] all;
            try { all = typeof(SnapshotState).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { all = Array.FindAll(e.Types, x => x != null); }
            foreach (var t in all)
            {
                if (t.IsAbstract || t.IsGenericTypeDefinition) continue;
                bool managed = t.IsClass && t.IsSubclassOf(typeof(SystemBase)) && !t.IsSubclassOf(typeof(ComponentSystemGroup));
                bool unmanaged = t.IsValueType && typeof(ISystem).IsAssignableFrom(t);
                if (!managed && !unmanaged) continue;
                if (OptOut.Contains(t.Name)) continue;
                string ns = t.Namespace ?? "";
                bool skip = false;
                foreach (var x in ExcludedNamespaces) if (ns.StartsWith(x)) { skip = true; break; }
                if (skip || !InSimulationGroup(t)) continue;
                list.Add(t);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            _systemTypes = list;
            return list;
        }

        static bool InSimulationGroup(Type t)
        {
            for (int guard = 0; guard < 16 && t != null; guard++)
            {
                var attr = t.GetCustomAttribute<UpdateInGroupAttribute>(true);
                if (attr == null) return true;                     // default group: Simulation
                var g = attr.GroupType;
                if (typeof(SimulationSystemGroup).IsAssignableFrom(g)) return true;
                if (typeof(InitializationSystemGroup).IsAssignableFrom(g)
                    || typeof(PresentationSystemGroup).IsAssignableFrom(g)) return false;
                t = g;                                             // a custom group: where does IT live?
            }
            return false;
        }

        static byte[] CaptureManaged(EntityWorld world, Type t, SnapshotCodec codec)
        {
            var sys = world.GetExistingSystemManaged(t);
            if (sys == null) return null;
            return Block(w => codec.WriteFields(w, InstanceFields(t), sys));
        }

        static int RestoreManaged(EntityWorld world, Type t, SnapshotCodec codec, byte[] data)
        {
            var sys = world.GetExistingSystemManaged(t);
            if (sys == null) return 0;
            return Read(data, rd => codec.ReadFields(rd, InstanceFields(t), sys));
        }

        static readonly MethodInfo _capU = typeof(SnapshotState).GetMethod(nameof(CaptureU), BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo _restU = typeof(SnapshotState).GetMethod(nameof(RestoreU), BindingFlags.NonPublic | BindingFlags.Static);

        static byte[] CaptureUnmanaged(EntityWorld world, Type t, SnapshotCodec codec)
        {
            var h = world.GetExistingSystem(t);
            if (h == SystemHandle.Null) return null;
            try { return (byte[])_capU.MakeGenericMethod(t).Invoke(null, new object[] { world, h, codec }); }
            catch (Exception e) { UnityEngine.Debug.LogWarning($"[Snapshot] {t.Name}: {e.InnerException?.Message ?? e.Message}"); return null; }
        }

        static int RestoreUnmanaged(EntityWorld world, Type t, SnapshotCodec codec, byte[] data)
        {
            var h = world.GetExistingSystem(t);
            if (h == SystemHandle.Null) return 0;
            try { return (int)_restU.MakeGenericMethod(t).Invoke(null, new object[] { world, h, codec, data }); }
            catch (Exception e) { UnityEngine.Debug.LogWarning($"[Snapshot] {t.Name}: {e.InnerException?.Message ?? e.Message}"); return 0; }
        }

        static byte[] CaptureU<T>(EntityWorld world, SystemHandle h, SnapshotCodec codec) where T : unmanaged, ISystem
        {
            object boxed = world.Unmanaged.GetUnsafeSystemRef<T>(h);
            return Block(w => codec.WriteFields(w, InstanceFields(typeof(T)), boxed));
        }

        static int RestoreU<T>(EntityWorld world, SystemHandle h, SnapshotCodec codec, byte[] data) where T : unmanaged, ISystem
        {
            ref T sys = ref world.Unmanaged.GetUnsafeSystemRef<T>(h);
            object boxed = sys;
            int n = Read(data, rd => codec.ReadFields(rd, InstanceFields(typeof(T)), boxed));
            sys = (T)boxed;
            return n;
        }

        // ── helpers ──────────────────────────────────────────────────────

        static List<FieldInfo> InstanceFields(Type t)
        {
            var list = new List<FieldInfo>();
            foreach (var f in SnapshotCodec.FieldsOf(t))
            {
                // Engine base classes (SystemBase, MonoBehaviour…) are not ours.
                string ns = f.DeclaringType?.Namespace ?? "";
                if (ns.StartsWith("Unity") || ns.StartsWith("System")) continue;
                list.Add(f);
            }
            return list;
        }

        static List<FieldInfo> StaticFields(Type t)
        {
            var list = new List<FieldInfo>();
            foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsLiteral) continue;
                // readonly values and strings cannot be written back; readonly
                // collections can (they are filled in place).
                if (f.IsInitOnly && (f.FieldType.IsValueType || f.FieldType == typeof(string))) continue;
                list.Add(f);
            }
            return list;
        }

        /// <summary>NetworkId → entity (and back) for every networked entity.</summary>
        static Dictionary<Entity, int> EntityIds(EntityManager em, out Dictionary<int, Entity> reverse)
        {
            var map = new Dictionary<Entity, int>();
            reverse = new Dictionary<int, Entity>();
            var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<NetworkedEntity>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });
            using var es = q.ToEntityArray(Allocator.Temp);
            using var ids = q.ToComponentDataArray<NetworkedEntity>(Allocator.Temp);
            q.Dispose();
            for (int i = 0; i < es.Length; i++)
            {
                map[es[i]] = ids[i].NetworkId;
                reverse[ids[i].NetworkId] = es[i];
            }
            return map;
        }

        static byte[] Block(Action<BinaryWriter> write)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) write(w);
            return ms.ToArray();
        }

        static int Read(byte[] data, Func<BinaryReader, int> read)
        {
            using var r = new BinaryReader(new MemoryStream(data));
            return read(r);
        }
    }
}
