// WorldSnapshot.cs
// The ECS half of a saved game: every simulation entity and its components,
// written with Unity's SerializeUtility and read back into a freshly booted
// map. docs/Design/Replays_And_Saves.md §3
//
// WHAT IS LEFT OUT, AND WHY
//   Entities carrying a component with a pointer in it — every native container
//   (NavCostField, NavFlowCache, VeilField…) and every blob reference
//   (DirectionTable, TraversalProfile, PortalGraph). SerializeUtility cannot
//   write a raw pointer, and all of these except VeilField are rebuilt from the
//   map and the restored buildings by the systems that own them. The test is
//   structural (a pointer field anywhere inside the type), not a list, so a new
//   native singleton is excluded the day it is added.
//
// RESTORING INTO A BOOTED WORLD
//   SerializeUtility can only load into an EMPTY world, so the snapshot is
//   loaded into a staging world and moved across. The booted world already holds
//   the map-derived singletons its bootstrap systems made; a snapshot entity
//   with exactly the same archetype as one of those is dropped — the live one
//   wins, because it was built for this world.
//
// Presentation: PresentationViewSpawned is switched OFF on every entity, so the
// view layer spawns a GameObject for everything after the load.

using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;

namespace TheWaningBorder.Core.Save
{
    public static class WorldSnapshot
    {
        /// <summary>Serialize every snapshot-able entity of <paramref name="em"/>.</summary>
        public static unsafe byte[] Capture(EntityManager em, out int entityCount, out string excludedSummary)
        {
            em.CompleteAllTrackedJobs();
            var excluded = UnserializableTypesIn(em);
            excludedSummary = Describe(excluded);

            var desc = new EntityQueryDesc
            {
                None = excluded.ToArray(),
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab,
            };
            // One-shot query for a save, disposed below — not a per-tick path.
            var query = em.CreateEntityQuery(desc);
            using var entities = query.ToEntityArray(Allocator.Temp);
            query.Dispose();
            entityCount = entities.Length;

            var staging = new Unity.Entities.World("TWB Save Staging", WorldFlags.Staging);
            try
            {
                var sem = staging.EntityManager;
                sem.CopyEntitiesFrom(em, entities);

                // Every view must respawn in the loaded world.
                var viewQ = sem.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadWrite<PresentationViewSpawned>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState
                              | EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab,
                });
                using (var withView = viewQ.ToEntityArray(Allocator.Temp))
                    foreach (var e in withView) sem.SetComponentEnabled<PresentationViewSpawned>(e, false);
                viewQ.Dispose();

                using var writer = new MemoryBinaryWriter();
                SerializeUtility.SerializeWorld(sem, writer, out object[] refs);
                if (refs != null && refs.Length > 0)
                    UnityEngine.Debug.LogWarning($"[Snapshot] {refs.Length} managed object reference(s) in the world " +
                                                 "were not saved.");
                var bytes = new byte[writer.Length];
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)writer.Data, bytes, 0, writer.Length);
                return bytes;
            }
            finally
            {
                staging.Dispose();
            }
        }

        /// <summary>
        /// Load <paramref name="bytes"/> into <paramref name="em"/>. Returns the
        /// number of entities moved in.
        /// </summary>
        public static unsafe int Restore(EntityManager em, byte[] bytes)
        {
            em.CompleteAllTrackedJobs();
            var staging = new Unity.Entities.World("TWB Load Staging", WorldFlags.Staging);
            try
            {
                var sem = staging.EntityManager;
                fixed (byte* p = bytes)
                {
                    using var reader = new MemoryBinaryReader(p, bytes.Length);
                    var tx = sem.BeginExclusiveEntityTransaction();
                    SerializeUtility.DeserializeWorld(tx, reader);
                    sem.EndExclusiveEntityTransaction();
                }

                // The booted world's own singletons win over the saved copies.
                var live = new HashSet<string>();
                using (var archetypes = new NativeList<EntityArchetype>(Allocator.Temp))
                {
                    em.GetAllArchetypes(archetypes);
                    for (int i = 0; i < archetypes.Length; i++)
                        if (archetypes[i].ChunkCount > 0 && !IsSystemArchetype(archetypes[i]))
                            live.Add(Signature(archetypes[i]));
                }
                int dropped = 0;
                using (var all = sem.GetAllEntities(Allocator.Temp))
                {
                    var drop = new NativeList<Entity>(Allocator.Temp);
                    foreach (var e in all)
                        if (live.Contains(Signature(sem.GetChunk(e).Archetype))) drop.Add(e);
                    dropped = drop.Length;
                    if (drop.Length > 0) sem.DestroyEntity(drop.AsArray());
                    drop.Dispose();
                }

                int moved = sem.UniversalQuery.CalculateEntityCount();
                em.MoveEntitiesFrom(sem);
                if (dropped > 0)
                    UnityEngine.Debug.Log($"[Snapshot] {dropped} saved entit{(dropped == 1 ? "y" : "ies")} " +
                                          "dropped in favour of the booted world's own singletons.");
                return moved;
            }
            finally
            {
                staging.Dispose();
            }
        }

        // ── type rules ───────────────────────────────────────────────────

        static List<ComponentType> UnserializableTypesIn(EntityManager em)
        {
            var result = new List<ComponentType>();
            var seen = new HashSet<TypeIndex>();
            using var archetypes = new NativeList<EntityArchetype>(Allocator.Temp);
            em.GetAllArchetypes(archetypes);
            for (int a = 0; a < archetypes.Length; a++)
            {
                using var types = archetypes[a].GetComponentTypes(Allocator.Temp);
                for (int i = 0; i < types.Length; i++)
                {
                    var ti = types[i].TypeIndex;
                    if (!seen.Add(ti)) continue;
                    var t = TypeManager.GetType(ti);
                    if (t == null) continue;
                    if (t.Name == "SystemInstance") continue;   // internal to Entities
                    if (!PointerFree(t, new HashSet<Type>()))
                        result.Add(ComponentType.ReadOnly(ti));
                }
            }
            return result;
        }

        /// <summary>SerializeUtility's own rule: no pointer field anywhere inside.</summary>
        static bool PointerFree(Type t, HashSet<Type> visiting)
        {
            if (t.IsPrimitive || t.IsEnum) return true;
            if (t.IsPointer || t == typeof(IntPtr) || t == typeof(UIntPtr)) return false;
            if (!t.IsValueType) return true;      // managed component: not our case
            if (!visiting.Add(t)) return true;
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var ft = f.FieldType;
                if (ft.IsPointer || ft == typeof(IntPtr) || ft == typeof(UIntPtr)) return false;
                if (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum && !PointerFree(ft, visiting)) return false;
            }
            return true;
        }

        static bool IsSystemArchetype(EntityArchetype a)
        {
            using var types = a.GetComponentTypes(Allocator.Temp);
            for (int i = 0; i < types.Length; i++)
                if (TypeManager.GetType(types[i].TypeIndex)?.Name == "SystemInstance") return true;
            return false;
        }

        static string Signature(EntityArchetype a)
        {
            using var types = a.GetComponentTypes(Allocator.Temp);
            var ids = new List<int>(types.Length);
            for (int i = 0; i < types.Length; i++) ids.Add(types[i].TypeIndex.Value);
            ids.Sort();
            return string.Join(",", ids);
        }

        static string Describe(List<ComponentType> types)
        {
            var names = new List<string>();
            foreach (var c in types) names.Add(TypeManager.GetType(c.TypeIndex)?.Name ?? "?");
            names.Sort();
            return string.Join(", ", names);
        }
    }
}
