// EntityCreator.cs
// Shared abstraction that lets unit/building factory code use a single
// implementation for both EntityManager and EntityCommandBuffer paths.
//
// Fix #219: Every unit file used to contain two Create methods (one for
// EntityManager, one for EntityCommandBuffer) that were 90%+ identical.
// Across 30+ types that was thousands of duplicated lines and any stat
// change had to be applied twice. The factories now share a single
// CreateInternal<TCreator>(...) generic method and the two public Create
// overloads just pass the right wrapper.
//
// Generic struct constraint (`where TCreator : struct, IEntityCreator`)
// lets the JIT specialize the method per concrete creator type, avoiding
// interface dispatch and boxing.

using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Abstraction over the "create entity and add components" operation.
    /// Two concrete implementations: <see cref="EmCreator"/> wraps an
    /// EntityManager, <see cref="EcbCreator"/> wraps an EntityCommandBuffer.
    /// </summary>
    public interface IEntityCreator
    {
        Entity CreateEntity();

        void AddComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData;

        /// <summary>Add a tag component (zero-size IComponentData).</summary>
        void AddComponent<T>(Entity entity)
            where T : unmanaged, IComponentData;

        /// <summary>Add an empty DynamicBuffer. Callers populate it via EntityManager post-creation if needed.</summary>
        void AddBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData;
    }

    /// <summary>EntityManager-backed creator. Immediate creation.</summary>
    public readonly struct EmCreator : IEntityCreator
    {
        private readonly EntityManager _em;
        public EmCreator(EntityManager em) { _em = em; }

        public Entity CreateEntity()
        {
            var e = _em.CreateEntity();
            EntityBuild.Track(e, ecb: false);
            return e;
        }

        public void AddComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
            => EntityBuild.Add(_em, entity, value);

        public void AddComponent<T>(Entity entity)
            where T : unmanaged, IComponentData
        {
            if (!EntityBuild.RecordType(entity, ComponentType.ReadWrite<T>(), ecb: false))
                _em.AddComponent<T>(entity);
        }

        public void AddBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (!EntityBuild.RecordType(entity, ComponentType.ReadWrite<T>(), ecb: false))
                _em.AddBuffer<T>(entity);
        }
    }

    /// <summary>EntityCommandBuffer-backed creator. Deferred creation.</summary>
    public readonly struct EcbCreator : IEntityCreator
    {
        private readonly EntityCommandBuffer _ecb;
        public EcbCreator(EntityCommandBuffer ecb) { _ecb = ecb; }

        public Entity CreateEntity()
        {
            var e = _ecb.CreateEntity();
            EntityBuild.Track(e, ecb: true);
            return e;
        }

        public void AddComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
            => EntityBuild.Add(_ecb, entity, value);

        public void AddComponent<T>(Entity entity)
            where T : unmanaged, IComponentData
        {
            if (!EntityBuild.RecordType(entity, ComponentType.ReadWrite<T>(), ecb: true))
                _ecb.AddComponent<T>(entity);
        }

        public void AddBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (!EntityBuild.RecordType(entity, ComponentType.ReadWrite<T>(), ecb: true))
                _ecb.AddBuffer<T>(entity);
        }
    }

    /// <summary>
    /// Builds a new entity in ONE structural change instead of one per
    /// component (2026-10-15).
    ///
    /// WHY: a factory used to add its ~20 components one at a time, then the
    /// dispatcher added ~10 more and the five transient sets. Every prefix of
    /// that sequence is a distinct archetype, so each unit type left 20-40
    /// archetypes behind that no entity ever lives in. A 78-minute 8-player
    /// match ended with ~1,950 archetypes, ~1,800 of them empty, and every
    /// broad query walks all of them. Each spawn also moved the entity ~30
    /// times.
    ///
    /// HOW: UnitFactory / BuildingFactory open a scope around a spawn. While
    /// it is open, EmCreator / EcbCreator and Add() RECORD the types and
    /// values of every entity the scope created instead of adding them.
    /// Closing the scope commits each entity:
    ///   * EntityManager path: one SetArchetype straight to the final type
    ///     set (cached per set), then the values in recorded order (so a
    ///     repeated add still ends on the last value), then the follow-up
    ///     work (disabling the transient enableables).
    ///   * ECB path: the type set in sorted batches of 15 (ComponentTypeSet's
    ///     capacity), then the values, then the follow-up work.
    /// The final archetype, the values and the entity creation order are
    /// exactly what the per-component adds produced, and the intermediate
    /// chunks are empty either way, so chunk layout (and with it every
    /// query's iteration order) is unchanged: lockstep-safe.
    ///
    /// Outside a scope, or for an entity the scope did not create, every call
    /// applies immediately, exactly as before.
    ///
    /// TWO COMMITS PER SPAWN, by design: every factory wraps its creator call
    /// in <see cref="Done"/>, which commits what the factory recorded the
    /// moment it returns. A factory may wrap another (the Feraldis Slave
    /// builds a Worker, then stamps it with plain EntityManager calls) and
    /// must find the components there; deferring past the factory broke
    /// exactly that. The entity stays tracked, so the dispatcher's own stamps
    /// (network id, names, counter tags, the transient sets) are recorded
    /// again and land in a second single change when the scope closes.
    /// </summary>
    public static class EntityBuild
    {
        private sealed class Pending
        {
            public Entity Entity;
            public readonly List<ComponentType> Types = new List<ComponentType>(64);
            public readonly List<Action<EntityManager, Entity>> EmSets = new List<Action<EntityManager, Entity>>(32);
            public readonly List<Action<EntityCommandBuffer, Entity>> EcbSets = new List<Action<EntityCommandBuffer, Entity>>(32);
            public readonly List<Action<EntityManager, Entity>> EmAfter = new List<Action<EntityManager, Entity>>(2);
            public readonly List<Action<EntityCommandBuffer, Entity>> EcbAfter = new List<Action<EntityCommandBuffer, Entity>>(2);
        }

        private sealed class Frame
        {
            public bool Ecb;
            public EntityManager Em;
            public EntityCommandBuffer Cb;
            public readonly List<Pending> Items = new List<Pending>(4);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly int _depth;
            internal Scope(int depth) { _depth = depth; }
            public void Dispose() => End(_depth);
        }

        // Main thread only: factories never run in jobs.
        private static readonly List<Frame> _frames = new List<Frame>();
        private static readonly Stack<Frame> _framePool = new Stack<Frame>();
        private static readonly Stack<Pending> _pendingPool = new Stack<Pending>();

        // EntityArchetype handles belong to one world; the cache is dropped
        // whenever the world changes (a new match builds a new world).
        private static readonly Dictionary<string, EntityArchetype> _archetypes = new Dictionary<string, EntityArchetype>();
        private static ulong _archetypeWorld;
        private static readonly StringBuilder _key = new StringBuilder(512);
        private static readonly List<ComponentType> _merged = new List<ComponentType>(128);
        private static readonly Comparison<ComponentType> ByTypeIndex =
            (a, b) => a.TypeIndex.Value.CompareTo(b.TypeIndex.Value);

        public static Scope Begin(EntityManager em) => Push(false, em, default);

        public static Scope Begin(EntityCommandBuffer ecb) => Push(true, default, ecb);

        private static Scope Push(bool ecb, EntityManager em, EntityCommandBuffer cb)
        {
            var f = _framePool.Count > 0 ? _framePool.Pop() : new Frame();
            f.Ecb = ecb; f.Em = em; f.Cb = cb;
            f.Items.Clear();
            _frames.Add(f);
            return new Scope(_frames.Count);
        }

        /// <summary>Called by the creators: an entity created inside an open
        /// scope of the same kind is built deferred.</summary>
        internal static void Track(Entity e, bool ecb)
        {
            if (_frames.Count == 0) return;
            var f = _frames[_frames.Count - 1];
            if (f.Ecb != ecb) return;
            var p = _pendingPool.Count > 0 ? _pendingPool.Pop() : new Pending();
            p.Entity = e;
            f.Items.Add(p);
        }

        private static Pending Find(Entity e, bool ecb)
        {
            if (_frames.Count == 0) return null;
            var f = _frames[_frames.Count - 1];
            if (f.Ecb != ecb) return null;
            for (int i = f.Items.Count - 1; i >= 0; i--)
                if (f.Items[i].Entity == e) return f.Items[i];
            return null;
        }

        /// <summary>Record a type with no value (tag, buffer, zero-initialised
        /// component). False when the entity is not being built.</summary>
        internal static bool RecordType(Entity e, ComponentType type, bool ecb)
        {
            var p = Find(e, ecb);
            if (p == null) return false;
            p.Types.Add(type);
            return true;
        }

        /// <summary>AddComponentData that records when the entity is being
        /// built, and applies immediately otherwise.</summary>
        public static void Add<T>(EntityManager em, Entity e, T value) where T : unmanaged, IComponentData
        {
            var p = Find(e, false);
            if (p == null) { em.AddComponentData(e, value); return; }
            var type = ComponentType.ReadWrite<T>();
            p.Types.Add(type);
            if (!type.IsZeroSized) p.EmSets.Add((m, x) => m.SetComponentData(x, value));
        }

        /// <summary>ECB twin of the EntityManager Add.</summary>
        public static void Add<T>(EntityCommandBuffer ecb, Entity e, T value) where T : unmanaged, IComponentData
        {
            var p = Find(e, true);
            if (p == null) { ecb.AddComponent(e, value); return; }
            var type = ComponentType.ReadWrite<T>();
            p.Types.Add(type);
            if (!type.IsZeroSized) p.EcbSets.Add((c, x) => c.SetComponent(x, value));
        }

        // ── Direct-EntityManager factories (2026-10-15) ─────────────────
        // ~30 building factories never moved to IEntityCreator: they call
        // em.CreateEntity(types) / em.AddComponentData / ecb.AddComponent
        // straight, so each of their ~10 adds was its own structural change
        // and its own (empty) archetype — the +BuildingSize / +ArmorTypeData /
        // +Defense / +ProductionState / +VeilstoneMineTag / +ChoiceBuildingTag
        // NEWARCH lines. These wrappers give them the creator's contract with
        // a one-word edit per call: inside an open scope they RECORD, outside
        // one they apply immediately, exactly as before.

        /// <summary>em.CreateEntity(types), recorded into the open scope.
        /// The entity is created empty and gets <paramref name="types"/> (and
        /// everything recorded after) in the scope's single change.</summary>
        public static Entity CreateEntity(EntityManager em, params ComponentType[] types)
        {
            if (!Open(false)) return em.CreateEntity(types);
            var e = em.CreateEntity();
            Track(e, ecb: false);
            Find(e, false).Types.AddRange(types);
            return e;
        }

        /// <summary>ecb.CreateEntity(), recorded into the open scope.</summary>
        public static Entity CreateEntity(EntityCommandBuffer ecb)
        {
            var e = ecb.CreateEntity();
            Track(e, ecb: true);
            return e;
        }

        /// <summary>AddBuffer (left empty), recorded when being built.</summary>
        public static void AddBuffer<T>(EntityManager em, Entity e) where T : unmanaged, IBufferElementData
        {
            if (!RecordType(e, ComponentType.ReadWrite<T>(), ecb: false)) em.AddBuffer<T>(e);
        }

        public static void AddBuffer<T>(EntityCommandBuffer ecb, Entity e) where T : unmanaged, IBufferElementData
        {
            if (!RecordType(e, ComponentType.ReadWrite<T>(), ecb: true)) ecb.AddBuffer<T>(e);
        }

        private static bool Open(bool ecb)
            => _frames.Count > 0 && _frames[_frames.Count - 1].Ecb == ecb;

        /// <summary>Record a block of types plus the work that must run once
        /// they exist (e.g. disabling enableables). False when the entity is
        /// not being built; the caller then does it immediately.</summary>
        public static bool RecordSet(EntityManager em, Entity e, ComponentType[] types, Action<EntityManager, Entity> after)
        {
            var p = Find(e, false);
            if (p == null) return false;
            p.Types.AddRange(types);
            if (after != null) p.EmAfter.Add(after);
            return true;
        }

        public static bool RecordSet(EntityCommandBuffer ecb, Entity e, ComponentType[] types, Action<EntityCommandBuffer, Entity> after)
        {
            var p = Find(e, true);
            if (p == null) return false;
            p.Types.AddRange(types);
            if (after != null) p.EcbAfter.Add(after);
            return true;
        }

        /// <summary>Commit everything recorded so far in the open scope and
        /// return <paramref name="e"/>. Wrapped around every factory's creator
        /// call. Entities stay tracked, so later stamps are recorded again.</summary>
        public static Entity Done(Entity e)
        {
            if (_frames.Count == 0) return e;
            var f = _frames[_frames.Count - 1];
            for (int i = 0; i < f.Items.Count; i++)
            {
                var p = f.Items[i];
                if (f.Ecb) CommitEcb(f.Cb, p); else CommitEm(f.Em, p);
                Reset(p);
            }
            return e;
        }

        private static void Reset(Pending p)
        {
            p.Types.Clear(); p.EmSets.Clear(); p.EcbSets.Clear(); p.EmAfter.Clear(); p.EcbAfter.Clear();
        }

        private static void End(int depth)
        {
            if (_frames.Count != depth)
                UnityEngine.Debug.LogError($"[EntityBuild] scope closed out of order ({_frames.Count} open, closing {depth}).");
            if (_frames.Count == 0) return;
            var f = _frames[_frames.Count - 1];
            _frames.RemoveAt(_frames.Count - 1);
            // Commit in creation order, the order the per-component adds
            // used to land in.
            for (int i = 0; i < f.Items.Count; i++)
            {
                var p = f.Items[i];
                if (f.Ecb) CommitEcb(f.Cb, p); else CommitEm(f.Em, p);
                Reset(p);
                p.Entity = Entity.Null;
                _pendingPool.Push(p);
            }
            f.Items.Clear();
            f.Em = default; f.Cb = default;
            _framePool.Push(f);
        }

        private static bool Empty(Pending p)
            => p.Types.Count == 0 && p.EmSets.Count == 0 && p.EcbSets.Count == 0
               && p.EmAfter.Count == 0 && p.EcbAfter.Count == 0;

        private static void CommitEm(EntityManager em, Pending p)
        {
            if (Empty(p) || !em.Exists(p.Entity)) return;
            // Union with what the entity already has: a wrapper may have
            // added something directly while the scope was open.
            _merged.Clear();
            using (var have = em.GetComponentTypes(p.Entity, Allocator.Temp))
                for (int i = 0; i < have.Length; i++) _merged.Add(have[i]);
            _merged.AddRange(p.Types);
            em.SetArchetype(p.Entity, ArchetypeFor(em, _merged));
            for (int i = 0; i < p.EmSets.Count; i++) p.EmSets[i](em, p.Entity);
            for (int i = 0; i < p.EmAfter.Count; i++) p.EmAfter[i](em, p.Entity);
        }

        private static void CommitEcb(EntityCommandBuffer ecb, Pending p)
        {
            if (Empty(p)) return;
            SortDistinct(p.Types);
            var batch = new FixedList128Bytes<ComponentType>();
            for (int i = 0; i < p.Types.Count; i++)
            {
                batch.Add(p.Types[i]);
                if (batch.Length == 15) { ecb.AddComponent(p.Entity, new ComponentTypeSet(batch)); batch.Clear(); }
            }
            if (batch.Length > 0) ecb.AddComponent(p.Entity, new ComponentTypeSet(batch));
            for (int i = 0; i < p.EcbSets.Count; i++) p.EcbSets[i](ecb, p.Entity);
            for (int i = 0; i < p.EcbAfter.Count; i++) p.EcbAfter[i](ecb, p.Entity);
        }

        /// <summary>Sort by type index and drop duplicates (a factory may add
        /// the same type twice; the archetype has it once).</summary>
        private static void SortDistinct(List<ComponentType> types)
        {
            types.Sort(ByTypeIndex);
            int w = 0;
            for (int i = 0; i < types.Count; i++)
                if (w == 0 || types[i].TypeIndex != types[w - 1].TypeIndex) types[w++] = types[i];
            types.RemoveRange(w, types.Count - w);
        }

        private static EntityArchetype ArchetypeFor(EntityManager em, List<ComponentType> types)
        {
            ulong world = em.WorldUnmanaged.SequenceNumber;
            if (world != _archetypeWorld) { _archetypes.Clear(); _archetypeWorld = world; }
            SortDistinct(types);
            _key.Clear();
            for (int i = 0; i < types.Count; i++) _key.Append(types[i].TypeIndex.Value).Append(',');
            string key = _key.ToString();
            if (!_archetypes.TryGetValue(key, out var arch) || !arch.Valid)
            {
                arch = em.CreateArchetype(types.ToArray());
                _archetypes[key] = arch;
            }
            return arch;
        }
    }
}
