// EntityCreator.cs
// Shared abstraction that lets unit/building factory code use a single
// implementation for both EntityManager and EntityCommandBuffer paths.
// Location: Assets/Scripts/Entities/EntityCreator.cs
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

        public Entity CreateEntity() => _em.CreateEntity();

        public void AddComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
            => _em.AddComponentData(entity, value);

        public void AddComponent<T>(Entity entity)
            where T : unmanaged, IComponentData
            => _em.AddComponent<T>(entity);

        public void AddBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData
            => _em.AddBuffer<T>(entity);
    }

    /// <summary>EntityCommandBuffer-backed creator. Deferred creation.</summary>
    public readonly struct EcbCreator : IEntityCreator
    {
        private readonly EntityCommandBuffer _ecb;
        public EcbCreator(EntityCommandBuffer ecb) { _ecb = ecb; }

        public Entity CreateEntity() => _ecb.CreateEntity();

        public void AddComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
            => _ecb.AddComponent(entity, value);

        public void AddComponent<T>(Entity entity)
            where T : unmanaged, IComponentData
            => _ecb.AddComponent<T>(entity);

        public void AddBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData
            => _ecb.AddBuffer<T>(entity);
    }
}
