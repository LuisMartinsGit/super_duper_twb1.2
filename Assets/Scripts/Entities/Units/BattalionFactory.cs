// BattalionFactory.cs
// Creates a BFME2-style battalion: 1 invisible leader + N visible members
// Location: Assets/Scripts/Entities/Units/BattalionFactory.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Static factory for creating battalion entities.
    /// A battalion consists of an invisible leader (pathfinds normally)
    /// and 15 visible member entities (lerp to formation slots, no pathfinding).
    /// </summary>
    public static class BattalionFactory
    {
        private const int DefaultColumns = 3;
        private const int DefaultRows = 5;
        private const float DefaultSpacing = 1.5f;
        private const float DefaultFollowSpeed = 8f;
        private const float DefaultLeashDistance = 10f;

        /// <summary>
        /// Spawn a complete battalion: 1 invisible leader + (Columns * Rows) visible members.
        /// Returns the leader entity.
        /// </summary>
        public static Entity SpawnBattalion(EntityManager em, string unitId, float3 spawnPos, Faction faction)
        {
            int cols = DefaultColumns;
            int rows = DefaultRows;
            int memberCount = cols * rows;

            // Load unit stats for leader speed / line-of-sight from TechTreeDB
            float speed = 3.5f;
            float los = 10f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit(unitId, out var def))
            {
                if (def.speed > 0) speed = def.speed;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            // ── Create invisible leader entity ──
            var leader = em.CreateEntity(
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(UnitTag),
                typeof(Health),
                typeof(MoveSpeed),
                typeof(LineOfSight),
                typeof(Target),
                typeof(Radius),
                typeof(DesiredDestination),
                typeof(GuardPoint),
                typeof(SmoothedDirection),
                typeof(StuckState),
                typeof(BattalionTag),
                typeof(BattalionLeader),
                typeof(BattalionStanceData),
                typeof(PopulationCost)
            );

            em.SetComponentData(leader, LocalTransform.FromPositionRotationScale(spawnPos, quaternion.identity, 1f));
            em.SetComponentData(leader, new FactionTag { Value = faction });
            em.SetComponentData(leader, new UnitTag { Class = UnitFactory.GetUnitClass(unitId) });
            em.SetComponentData(leader, new Health { Value = 1, Max = 1 }); // Dummy HP; real HP tracked via members
            em.SetComponentData(leader, new MoveSpeed { Value = speed });
            em.SetComponentData(leader, new LineOfSight { Radius = los });
            em.SetComponentData(leader, new Target { Value = Entity.Null });
            em.SetComponentData(leader, new Radius { Value = 0.5f });
            em.SetComponentData(leader, new DesiredDestination { Has = 0 });
            em.SetComponentData(leader, new GuardPoint { Has = 0 });
            em.SetComponentData(leader, new SmoothedDirection { Value = float3.zero });
            em.SetComponentData(leader, new StuckState { Counter = 0, LastAttempt = 0 });
            em.SetComponentData(leader, new PopulationCost { Amount = 0 }); // Pop cost on members
            em.SetComponentData(leader, new BattalionLeader
            {
                Columns = cols,
                Rows = rows,
                Spacing = DefaultSpacing,
                FollowSpeed = DefaultFollowSpeed,
                LeashDistance = DefaultLeashDistance,
                UnitId = new FixedString64Bytes(unitId)
            });
            em.SetComponentData(leader, new BattalionStanceData { Value = BattalionStance.Default });

            // NO PresentationId — leader is invisible (no mesh/collider)

            // Add member buffer (populated AFTER all structural changes)
            em.AddBuffer<BattalionMember>(leader);

            // ── Create member entities ──
            // IMPORTANT: Collect in temp array first — UnitFactory.Create() and
            // AddComponentData() perform structural changes that invalidate DynamicBuffer refs.
            var members = new Entity[memberCount];

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    // Compute initial slot position relative to leader (centered)
                    float3 slotOffset = BattalionFormation.ComputeSlotOffset(
                        col, row, cols, rows, DefaultSpacing);
                    float3 memberPos = spawnPos + slotOffset;

                    // Create unit via standard factory (gets PresentationId, collider, etc.)
                    Entity member = UnitFactory.Create(em, unitId, memberPos, faction);

                    // Add battalion membership
                    em.AddComponentData(member, new BattalionTag());
                    em.AddComponentData(member, new BattalionMemberData
                    {
                        Leader = leader,
                        Column = col,
                        Row = row
                    });

                    // Remove movement components — members must NOT use MovementSystem
                    if (em.HasComponent<DesiredDestination>(member))
                        em.RemoveComponent<DesiredDestination>(member);
                    if (em.HasComponent<GuardPoint>(member))
                        em.RemoveComponent<GuardPoint>(member);
                    if (em.HasComponent<SmoothedDirection>(member))
                        em.RemoveComponent<SmoothedDirection>(member);
                    if (em.HasComponent<StuckState>(member))
                        em.RemoveComponent<StuckState>(member);

                    members[row * cols + col] = member;
                }
            }

            // Populate buffer AFTER all structural changes — ref is now stable
            var memberBuffer = em.GetBuffer<BattalionMember>(leader);
            for (int i = 0; i < memberCount; i++)
                memberBuffer.Add(new BattalionMember { Value = members[i] });

            return leader;
        }
    }
}
