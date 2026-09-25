// Alanthor wall system: hub (round tower) + segment (data-only graph edge) +
// instances (3 m curtain modules). The curtain runs hub CENTRE to hub CENTRE
// and the hub stands on top of its ends, so a wall cannot have a gap.
// Each segment spawns multiple small wall instances that block the passability grid.
// Instances can be upgraded to towers (ranged attack), gates (friendly-only
// passage) or hubs (the segment splits there, so walls can branch — T and X).

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Factory for Alanthor wall entities.
    /// Walls consist of hubs (connection points), segments (logical graph edges),
    /// and instances (small wall pieces that block pathfinding).
    /// </summary>
    public static class AlanthorWall
    {
        /// <summary>
        /// Each wall PIECE has its own BuildingDefSO, because each has its own
        /// stats and its own art: the hub is a tower, the curtain is a 3 m
        /// module, the gatehouse spans three of them and the tower is a
        /// converted module. They were one SO with `segmentHp` bolted onto the
        /// hub's, which gave the curtain nowhere to put a defence block, a
        /// radius, a line of sight or a prefab of its own.
        /// </summary>
        public const string HubId = "Alanthor_Wall";
        public const string SegmentId = "Alanthor_WallSegment";
        public const string GateId = "Alanthor_WallGate";
        public const string TowerId = "Alanthor_WallTower";

        public const int HubPresentationID = 550;
        // Segment no longer has a visual (data-only graph edge)
        public const int InstancePresentationID = 552;
        public const int TowerPresentationID = 553;
        public const int GatePresentationID = 554;
        /// <summary>A curved segment: one swept mesh along its WallCurvePoint buffer.</summary>
        public const int CurvedSegmentPresentationID = 555;
        /// <summary>A curved segment's cell: pick collider only, no visual.</summary>
        public const int CurveCellPresentationID = 556;
        /// <summary>A cell mounting a ballista (docs/Design/Age_1_Alanthor.md
        /// § Ballista and Trebuchet emplacements).</summary>
        public const int BallistaEmplacementPresentationID = 557;
        /// <summary>A cell mounting a trebuchet.</summary>
        public const int TrebuchetEmplacementPresentationID = 558;

        /// <summary>Length of each wall module along the wall, in meters.
        /// Compact-wall rework (2026-08-09): 3 m curtain modules replace the
        /// old 4 m walkable-rampart tiles. Walls are now solid curtain walls —
        /// no walkable deck.</summary>
        public const float InstanceSpacing = 3f;

        /// <summary>
        /// Extra interpenetration between adjacent curtain copies, as a
        /// fraction of a module. ZERO, and it should stay zero: a tiling
        /// module carries its own overlap. Its timbers set the repeat and its
        /// footing and cloth run LONGER so they lap into the next copy and
        /// hide the joint — WallModuleArt measures the repeat off the timbers
        /// for exactly that reason.
        ///
        /// This exists only for art that does NOT overlap itself, where every
        /// part ends on the same plane and the seams would otherwise show.
        /// </summary>
        public const float ModuleOverlap = 0f;

        /// <summary>Compact curtain-wall cross-section, in meters.</summary>
        public const float WallWidth = 1f;     // masonry thickness across the wall (X)
        public const float WallHeight = 2.6f;  // parapet crown top (solid curtain, no deck)

        /// <summary>
        /// Hub radius, in metres: 0.7 of a wall section (2026-09-21 — 30 %
        /// smaller than the 2026-09-19 value of one full section, which made
        /// the tower the wall's main event and the curtain trim between
        /// towers). The hub is a round tower and the curtain starts exactly
        /// at its rim, so the wall-to-hub spacing is derived from this and
        /// never tuned on its own. Everything about the hub's size flows
        /// from here — including BuildingSizeConfig's "Alanthor_Wall" entry,
        /// which must stay at (int)HubWidth.
        /// </summary>
        public const float HubShrink = 0.7f;
        public const float HubRadius = InstanceSpacing * HubShrink;


        /// <summary>Hub footprint width, in meters — the round tower's
        /// diameter, 2 x 2 build cells (4.2 m of drum inside a 4 m footprint). Hubs are buildings and snap to the
        /// 2 m grid like any other, so this must stay in step with
        /// BuildingSizeConfig's "Alanthor_Wall" entry. The curtain SEGMENTS
        /// between hubs remain freeform, at whatever bearing the hub-to-hub
        /// line has. docs/Design/Build_Grid.md</summary>
        public const float HubWidth = HubRadius * 2f;

        /// <summary>Number of contiguous wall instances a segment-level
        /// Convert-to-Gate replaces with ONE gate entity (3 modules x 3 m =
        /// a 9 m gatehouse). The converted module BECOMES the gate; the
        /// module on each side is destroyed, because the gatehouse's own
        /// masonry occupies that ground (docs/Design/Age_1_Alanthor.md
        /// § The gate is one structure, three modules wide). Consumed by
        /// <see cref="PickGateRegionInstances"/>.</summary>
        public const int GateRegionSpan = 3;

        /// <summary>The gatehouse's span along the wall, in metres.</summary>
        public const float GateSpanMetres = GateRegionSpan * InstanceSpacing;

        /// <summary>
        /// Inset from each hub centre to where the curtain starts, in metres.
        /// ZERO since 2026-09-21: a wall drawn from A to B RUNS from A to B,
        /// its outer edge flush with the hub's centre, and the hub stands on
        /// top of that end.
        ///
        /// It used to be the hub's radius, so the curtain began at the tower's
        /// rim and the tower covered the rest. That made every hub a plug in a
        /// hole: it looked right only while the hub's art was exactly as wide
        /// as the inset, and the moment a hub died it left a hub-wide gap that
        /// reads as "the wall next to it was destroyed too". Overlapping the
        /// hub costs nothing — it is wider than the wall is thick — and a gap
        /// is then impossible by construction.
        /// </summary>
        private const float HubInset = 0f;
        /// <summary>HubInset for the presentation (the swept mesh starts here).</summary>
        public const float HubInsetMetres = HubInset;

        /// <summary>
        /// Create a wall hub entity (the round connection tower).
        /// </summary>
        public static Entity CreateHub(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(HubId);
            byte tier = WallTiers.LevelFor(em, faction);
            int hp = WallTiers.ScaleHp(def.hp, tier);
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(WallTag),
                typeof(WallHubTag),
                typeof(BuildingUpgradeable)
            );

            em.SetComponentData(entity, new PresentationId { Id = HubPresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = hp, Max = hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.AddComponentData(entity, new WallTier { Level = tier });
            // 2 x 2-cell footprint (HubWidth) so build-range / selection /
            // passability use the real size. The presentation draws the tower
            // at HubRadius and the curtain starts at HubInset, so the three
            // agree on where the hub ends.
            em.SetComponentData(entity, new BuildingSize { Width = (int)HubWidth, Height = (int)HubWidth });
            em.SetComponentData(entity, new Radius { Value = HubRadius });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            // Dynamic buffer for tracking connections to other hubs
            em.AddBuffer<WallHubLink>(entity);

            // The hub is a RESEARCH HOST (2026-09-24): the wall's own two
            // upgrades, Battlements and Shielded Ramparts, are bought here
            // rather than at the Hall, and ResearchCommandDirect refuses any
            // building without this buffer -- silently, which is exactly how
            // a button that looks fine does nothing at all.
            // docs/Design/Age_1_Alanthor.md § The four wall levels
            em.AddBuffer<ProductionQueueItem>(entity);

            AdoptOrphanedSegments(em, entity, position, faction);

            return entity;
        }

        static readonly ComponentType[] QT_Segments =
        {
            ComponentType.ReadWrite<WallConnection>(),
            ComponentType.ReadOnly<WallSegmentTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery QC_Segments;

        /// <summary>
        /// Walls fall in sections (docs/Design/Age_1_Alanthor.md): a hub's
        /// death leaves its segments standing on their own cells, with their
        /// <see cref="WallConnection"/> pointing at the dead hub. When a
        /// same-faction hub is then built with the dead hub's centre inside
        /// its footprint — the AI refills its plan slot, a player repairs the
        /// line — those segments become this hub's, and both hubs' link
        /// buffers are updated. Without this, the hub's "Build Wall" / the
        /// AI's WallExtend sees no link to the neighbour and lays a SECOND
        /// segment of cells on top of the standing ones. A terrain seal
        /// (self-segment) re-attaches the same way, which is also what keeps
        /// SealToTerrain from throwing a second seal.
        /// </summary>
        static void AdoptOrphanedSegments(EntityManager em, Entity hub, float3 position, Faction faction)
        {
            var q = QC_Segments.Get(em, QT_Segments);
            if (q.IsEmptyIgnoreFilter) return;

            float half = HubWidth * 0.5f;
            var segments = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (em.GetComponentData<FactionTag>(seg).Value != faction) continue;

                var conn = em.GetComponentData<WallConnection>(seg);
                bool adopted = false;
                if (!em.Exists(conn.HubA) && InsideFootprint(conn.PosA, position, half))
                {
                    conn.HubA = hub; adopted = true;
                }
                if (!em.Exists(conn.HubB) && InsideFootprint(conn.PosB, position, half))
                {
                    conn.HubB = hub; adopted = true;
                }
                if (!adopted) continue;

                em.SetComponentData(seg, conn);

                // Self-segment (a terrain seal): other == hub, a self-link.
                Entity other = conn.HubA == hub ? conn.HubB : conn.HubA;
                em.GetBuffer<WallHubLink>(hub).Add(new WallHubLink { ConnectedHub = other, Segment = seg });

                if (other != hub && em.Exists(other) && em.HasBuffer<WallHubLink>(other))
                {
                    var links = em.GetBuffer<WallHubLink>(other);
                    for (int l = 0; l < links.Length; l++)
                    {
                        if (links[l].Segment != seg) continue;
                        var link = links[l];
                        link.ConnectedHub = hub;
                        links[l] = link;
                        break;
                    }
                }
            }
            segments.Dispose();
        }

        static bool InsideFootprint(float3 p, float3 centre, float half)
            => math.abs(p.x - centre.x) <= half && math.abs(p.z - centre.z) <= half;

        // ── Segment entity ─────────────────────────────────────────────────

        /// <summary>
        /// The one segment archetype every spawn shape shares — straight,
        /// drawn curve, terrain seal, and the halves of a split. A data-only
        /// graph edge (no BuildingSize; the cells carry HP and passability)
        /// that links both hubs' <see cref="WallHubLink"/> buffers. With a
        /// <paramref name="curve"/> it also carries the swept-mesh
        /// presentation and the <see cref="WallCurvePoint"/> buffer. Cells
        /// are NOT spawned here: the caller either spawns them or moves
        /// standing ones onto it.
        /// </summary>
        static Entity CreateSegmentEntity(EntityManager em, Entity hubA, Entity hubB,
            float3 posA, float3 posB, float3 midpoint, quaternion rotation,
            IReadOnlyList<float3> curve, Faction faction)
        {
            var entity = em.CreateEntity(
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(WallTag),
                typeof(WallSegmentTag),
                typeof(WallConnection)
            );

            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(midpoint, rotation, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = 1, Max = 1 }); // structural placeholder
            em.AddComponentData(entity, new WallTier { Level = WallTiers.LevelFor(em, faction) });
            em.SetComponentData(entity, new WallConnection { HubA = hubA, HubB = hubB, PosA = posA, PosB = posB });

            if (curve != null)
            {
                em.AddComponentData(entity, new PresentationId { Id = CurvedSegmentPresentationID });
                var buf = em.AddBuffer<WallCurvePoint>(entity);
                for (int i = 0; i < curve.Count; i++) buf.Add(new WallCurvePoint { Position = curve[i] });
            }

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            // Radius needed for some queries but minimal
            em.AddComponentData(entity, new Radius { Value = 0.1f });

            // Buffer for child instances
            em.AddBuffer<WallInstanceRef>(entity);

            // task-109 Phase 4 / AD-5: segments must carry NetworkedEntity so
            // lockstep payloads (Phase 6 Convert-to-Gate) can address them via
            // the per-tick partitioned NetworkIdGenerator slot range.
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Update hub connection buffers (a self-segment links once).
            AddHubLink(em, hubA, hubB, entity);
            if (hubB != hubA) AddHubLink(em, hubB, hubA, entity);

            return entity;
        }

        static void AddHubLink(EntityManager em, Entity hub, Entity other, Entity segment)
        {
            if (!em.Exists(hub) || !em.HasBuffer<WallHubLink>(hub)) return;
            em.GetBuffer<WallHubLink>(hub).Add(new WallHubLink { ConnectedHub = other, Segment = segment });
        }

        /// <summary>
        /// Remove the WallHubLink entry that references <paramref name="segment"/>
        /// from <paramref name="hub"/>. Tolerates a dead hub.
        /// </summary>
        public static void RemoveHubLink(EntityManager em, Entity hub, Entity segment)
        {
            if (!em.Exists(hub)) return;
            if (!em.HasBuffer<WallHubLink>(hub)) return;

            var links = em.GetBuffer<WallHubLink>(hub);
            for (int i = links.Length - 1; i >= 0; i--)
            {
                if (links[i].Segment == segment)
                {
                    links.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// Create a wall segment connecting two hubs.
        /// The segment is a data-only entity (no visual). It spawns wall instances
        /// along the line between the two hubs, each blocking a grid cell.
        /// Also updates the WallHubLink buffers on both hubs.
        /// </summary>
        public static Entity CreateSegment(EntityManager em, Entity hubA, Entity hubB, Faction faction)
        {
            var posA = em.GetComponentData<LocalTransform>(hubA).Position;
            var posB = em.GetComponentData<LocalTransform>(hubB).Position;

            float3 midpoint = (posA + posB) * 0.5f;
            float3 diff = posB - posA;
            float3 dirFlat = math.normalize(new float3(diff.x, 0f, diff.z));
            quaternion rotation = quaternion.LookRotationSafe(dirFlat, math.up());

            var entity = CreateSegmentEntity(em, hubA, hubB, posA, posB, midpoint, rotation, null, faction);

            // Spawn wall instances along the line
            SpawnInstances(em, entity, posA, posB, dirFlat, rotation, faction);

            return entity;
        }

        /// <summary>
        /// A segment whose modules follow a DRAWN curve instead of the chord
        /// between its hubs (docs/Design/Age_1_Alanthor.md § Drawing walls).
        /// <paramref name="path"/> runs from hub A's centre to hub B's centre;
        /// modules are laid back to back along it from HubInset past A to
        /// HubInset short of B, each turned to the local tangent, so the wall
        /// reads as one continuous curved rampart. The segment entity, its
        /// hub links, buffer, network id and every later rule (gates, towers,
        /// section collapse, garrison) are exactly those of a straight segment.
        /// </summary>
        public static Entity CreateSegmentAlong(EntityManager em, Entity hubA, Entity hubB,
            IReadOnlyList<float3> path, Faction faction)
        {
            if (path == null || path.Count < 2) return CreateSegment(em, hubA, hubB, faction);

            var posA = em.GetComponentData<LocalTransform>(hubA).Position;
            var posB = em.GetComponentData<LocalTransform>(hubB).Position;

            // Arc-length table over the polyline (XZ).
            var cum = ArcTable(path);
            float total = cum[cum.Length - 1];

            float3 midpoint = SampleAlong(path, cum, total * 0.5f, out float3 midTan);
            quaternion midRot = quaternion.LookRotationSafe(midTan, math.up());

            var entity = CreateSegmentEntity(em, hubA, hubB, posA, posB, midpoint, midRot, path, faction);

            float usable = total - 2f * HubInset;
            if (usable < 0.5f)
            {
                var inst = CreateInstance(em, midpoint, midRot, faction, entity);
                MakeCurveCell(em, inst);
                em.GetBuffer<WallInstanceRef>(entity).Add(new WallInstanceRef { Instance = inst, Position = midpoint });
                return entity;
            }

            // The cells are the sim's HP / passability / conversion units,
            // pitched at or under a module length along the arc. They draw
            // nothing: the segment's swept mesh is the wall.
            int count = math.max(1, (int)math.ceil(usable / InstanceSpacing));
            float pitch = usable / count;
            var instances = new Entity[count];
            var positions = new float3[count];
            for (int i = 0; i < count; i++)
            {
                float sAt = HubInset + pitch * (i + 0.5f);
                positions[i] = SampleAlong(path, cum, sAt, out float3 tan);
                instances[i] = CreateInstance(em, positions[i], quaternion.LookRotationSafe(tan, math.up()), faction, entity);
                MakeCurveCell(em, instances[i]);
            }
            var buffer = em.GetBuffer<WallInstanceRef>(entity);
            for (int i = 0; i < count; i++)
                buffer.Add(new WallInstanceRef { Instance = instances[i], Position = positions[i] });
            return entity;
        }

        static void MakeCurveCell(EntityManager em, Entity inst)
        {
            em.SetComponentData(inst, new PresentationId { Id = CurveCellPresentationID });
            em.AddComponent<WallCurveCellTag>(inst);
        }

        // ── Curve geometry (shared with the swept-mesh visual) ─────────────

        /// <summary>Cumulative XZ arc length per point of <paramref name="path"/>.</summary>
        public static float[] ArcTable(IReadOnlyList<float3> path)
        {
            int n = path.Count;
            var cum = new float[n];
            for (int i = 1; i < n; i++)
                cum[i] = cum[i - 1] + math.distance(new float2(path[i].x, path[i].z),
                                                    new float2(path[i - 1].x, path[i - 1].z));
            return cum;
        }

        /// <summary>Position and flat unit tangent at arc length <paramref name="s"/>
        /// along <paramref name="path"/> — public so the swept-mesh visual
        /// samples the curve exactly as the sim placed its cells.</summary>
        public static float3 SampleCurve(IReadOnlyList<float3> path,
            float[] cum, float s, out float3 tangent) => SampleAlong(path, cum, s, out tangent);

        /// <summary>
        /// Arc length along <paramref name="path"/> of the point nearest
        /// <paramref name="p"/> (XZ). This is how a cell's place on its
        /// segment is recovered from its position — cells are positioned
        /// once at spawn and keep that position through a split, so the
        /// swept mesh can open a dead cell's span whatever the segment has
        /// since become.
        /// </summary>
        public static float ArcLengthAlong(IReadOnlyList<float3> path, float[] cum, float3 p)
        {
            float bestD = float.MaxValue, bestS = 0f;
            float2 q = new float2(p.x, p.z);
            for (int i = 1; i < path.Count; i++)
            {
                float2 a = new float2(path[i - 1].x, path[i - 1].z);
                float2 b = new float2(path[i].x, path[i].z);
                float2 ab = b - a;
                float len2 = math.lengthsq(ab);
                float t = len2 > 1e-8f ? math.saturate(math.dot(q - a, ab) / len2) : 0f;
                float d = math.distancesq(q, a + ab * t);
                if (d < bestD) { bestD = d; bestS = cum[i - 1] + (cum[i] - cum[i - 1]) * t; }
            }
            return bestS;
        }

        /// <summary>Position and flat unit tangent at arc length <paramref name="s"/>
        /// along <paramref name="path"/> (clamped to its ends).</summary>
        private static float3 SampleAlong(IReadOnlyList<float3> path,
            float[] cum, float s, out float3 tangent)
        {
            int n = path.Count;
            s = math.clamp(s, 0f, cum[n - 1]);
            int i = 1;
            while (i < n - 1 && cum[i] < s) i++;
            float3 a = path[i - 1], b = path[i];
            float seg = math.max(1e-4f, cum[i] - cum[i - 1]);
            float t = math.saturate((s - cum[i - 1]) / seg);
            float3 d = new float3(b.x - a.x, 0f, b.z - a.z);
            tangent = math.lengthsq(d) > 1e-8f ? math.normalize(d) : new float3(0f, 0f, 1f);
            float3 p = math.lerp(a, b, t);
            p.y = TerrainUtility.GetHeight(p.x, p.z);
            return p;
        }

        /// <summary>
        /// Spawn wall instance entities evenly along the line between two hubs.
        /// Each instance is a 1x1 building that blocks the passability grid.
        /// </summary>
        private static void SpawnInstances(
            EntityManager em, Entity segment,
            float3 posA, float3 posB,
            float3 direction, quaternion rotation,
            Faction faction)
        {
            float distance = math.distance(
                new float2(posA.x, posA.z),
                new float2(posB.x, posB.z));

            float usable = distance - 2f * HubInset;
            if (usable < 0.5f)
            {
                // Hubs too close — spawn one instance at midpoint
                float3 mid = (posA + posB) * 0.5f;
                var inst = CreateInstance(em, mid, rotation, faction, segment);
                var buf = em.GetBuffer<WallInstanceRef>(segment);
                buf.Add(new WallInstanceRef { Instance = inst, Position = mid });
                return;
            }

            // Use ceil so actualSpacing never exceeds InstanceSpacing — each
            // module's masonry is InstanceSpacing (3 m) long, so spacing > 3 m
            // would leave a visible gap. Ceil guarantees touch-or-overlap.
            int count = math.max(1, (int)math.ceil(usable / InstanceSpacing));
            float actualSpacing = usable / count;

            // Collect all instances first, then add to buffer in one go.
            // Each CreateInstance calls em.CreateEntity which is a structural change
            // that invalidates any live buffer handles.
            var instances = new Entity[count];
            var positions = new float3[count];
            for (int i = 0; i < count; i++)
            {
                float t = HubInset + actualSpacing * (i + 0.5f);
                positions[i] = posA + direction * t;
                instances[i] = CreateInstance(em, positions[i], rotation, faction, segment);
            }

            // Now safe to get buffer and populate it (no more structural changes)
            var buffer = em.GetBuffer<WallInstanceRef>(segment);
            for (int i = 0; i < count; i++)
            {
                buffer.Add(new WallInstanceRef { Instance = instances[i], Position = positions[i] });
            }
        }

        /// <summary>
        /// Create a single wall instance entity at the given position.
        /// </summary>
        public static Entity CreateInstance(
            EntityManager em, float3 position, quaternion rotation,
            Faction faction, Entity parentSegment)
        {
            // The curtain segments are the same building def as the hub: hub
            // stats are hp/lineOfSight, segment stats are segmentHp/
            // segmentLineOfSight (docs: one wall, two spawn shapes).
            // The curtain module's own def — not the hub's, and not the
            // hub's `segmentHp` field, which is retired.
            var def = TechCatalog.Building(SegmentId);
            byte tier = WallTiers.LevelFor(em, faction);
            int cellHp = WallTiers.ScaleHp(def.hp, tier);

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(WallTag),
                typeof(WallInstanceTag),
                typeof(WallInstanceParent)
            );

            em.SetComponentData(entity, new PresentationId { Id = InstancePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                position, rotation, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = cellHp, Max = cellHp });
            em.SetComponentData(entity, new LineOfSight { Radius = def.lineOfSight });
            em.AddComponentData(entity, new WallTier { Level = tier });
            // Level 3 curtain: two garrison slots per module
            // (docs/Design/Age_1_Alanthor.md § Garrison slots).
            int slots = WallTiers.GarrisonSlots(tier);
            if (slots > 0)
            {
                var slotBuf = em.AddBuffer<WallGarrisonSlot>(entity);
                for (int i = 0; i < slots; i++) slotBuf.Add(new WallGarrisonSlot { Occupant = Entity.Null });
            }
            // Compact curtain footprint: 1 m thick across the wall, one 3 m
            // module long. Solid obstacle on the passability grid.
            em.SetComponentData(entity, new BuildingSize { Width = (int)WallWidth, Height = (int)InstanceSpacing });
            em.SetComponentData(entity, new Radius { Value = def.radius > 0f ? def.radius : InstanceSpacing * 0.5f });
            em.SetComponentData(entity, new WallInstanceParent { Segment = parentSegment });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            // task-109 Phase 4 / AD-5: instances must carry NetworkedEntity so
            // lockstep payloads (Phase 6 Convert-to-Gate focus instance) can
            // resolve them across peers.
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }

        // ── Cell → hub (branching walls) ───────────────────────────────────

        /// <summary>
        /// True when <paramref name="instance"/> is a plain standing wall cell
        /// that <see cref="ConvertInstanceToHub"/> will accept: on a segment,
        /// finished, and not already a gate or a tower.
        /// </summary>
        public static bool CanConvertInstanceToHub(EntityManager em, Entity instance)
        {
            if (instance == Entity.Null || !em.Exists(instance)) return false;
            if (!em.HasComponent<WallInstanceTag>(instance)) return false;
            if (!em.HasComponent<WallInstanceParent>(instance)) return false;
            if (em.HasComponent<WallGateTag>(instance) || em.HasComponent<WallTowerTag>(instance)) return false;
            if (em.HasComponent<UnderConstruction>(instance)) return false;
            if (em.HasComponent<Health>(instance) && em.GetComponentData<Health>(instance).Value <= 0) return false;
            var segment = em.GetComponentData<WallInstanceParent>(instance).Segment;
            if (!em.Exists(segment) || !em.HasBuffer<WallInstanceRef>(segment)) return false;
            if (em.HasComponent<WallSegmentUpgradeState>(segment)) return false;
            return true;
        }

        /// <summary>
        /// Turn a standing wall cell into a hub, SPLITTING its segment there
        /// (docs/Design/Age_1_Alanthor.md § Branching walls). The hub rises
        /// on the cell's spot exactly (the hub is grid-exempt); the cell under it is
        /// removed; the cells before it stay on the original segment, which
        /// now ends at the new hub, and the cells after it move to a fresh
        /// segment from the new hub to the far hub. Both segments keep
        /// their standing cells, HP and construction state untouched — only
        /// the graph changes — so a wall can be branched (T) or crossed (X)
        /// from any point without rebuilding it. Curved segments split their
        /// drawn curve at the cell; the two halves each pass through the hub.
        /// Returns the hub, or Entity.Null when the cell cannot convert.
        /// Structural: call outside any query iteration.
        /// </summary>
        public static Entity ConvertInstanceToHub(EntityManager em, Entity instance)
        {
            if (!CanConvertInstanceToHub(em, instance)) return Entity.Null;

            var segment = em.GetComponentData<WallInstanceParent>(instance).Segment;
            var faction = em.GetComponentData<FactionTag>(instance).Value;
            float3 cellPos = em.GetComponentData<LocalTransform>(instance).Position;
            // Through the id overload, so the hub's grid exemption applies and
            // the tower rises EXACTLY on the cell it replaces (2026-09-24).
            // It used to snap, which put it up to a metre off the wall's line
            // -- the same kink an inserted run-cap hub used to make.
            float3 hubPos = BuildGrid.Snap(cellPos, HubId);
            hubPos.y = TerrainUtility.GetHeight(hubPos.x, hubPos.z);

            // Snapshot the segment before any structural change.
            var conn = em.GetComponentData<WallConnection>(segment);
            var refs = em.GetBuffer<WallInstanceRef>(segment);
            var cells = new List<WallInstanceRef>(refs.Length);
            int k = -1;
            for (int i = 0; i < refs.Length; i++)
            {
                cells.Add(refs[i]);
                if (refs[i].Instance == instance) k = i;
            }
            if (k < 0) return Entity.Null;

            List<float3> curve = null;
            if (em.HasBuffer<WallCurvePoint>(segment))
            {
                var cb = em.GetBuffer<WallCurvePoint>(segment);
                curve = new List<float3>(cb.Length);
                for (int i = 0; i < cb.Length; i++) curve.Add(cb[i].Position);
                if (curve.Count < 2) curve = null;
            }

            // The converted cell goes; so does any other cell sitting mostly
            // under the tower (a neighbour can be within a pitch of it when
            // the segment is short).
            var before = new List<WallInstanceRef>();
            var after = new List<WallInstanceRef>();
            var swallowed = new List<Entity>();
            float swallowR = HubRadius * 0.5f;
            for (int i = 0; i < cells.Count; i++)
            {
                float dx = cells[i].Position.x - hubPos.x, dz = cells[i].Position.z - hubPos.z;
                bool under = i == k || dx * dx + dz * dz < swallowR * swallowR;
                if (under) { if (em.Exists(cells[i].Instance)) swallowed.Add(cells[i].Instance); }
                else if (i < k) before.Add(cells[i]);
                else after.Add(cells[i]);
            }

            // Split the drawn curve at the cell: [A .. cut] + hub, hub + [cut .. B].
            List<float3> curveA = null, curveB = null;
            if (curve != null)
            {
                var cum = ArcTable(curve);
                float sCut = ArcLengthAlong(curve, cum, cellPos);
                curveA = new List<float3>(); curveB = new List<float3>();
                for (int i = 0; i < curve.Count; i++)
                {
                    if (cum[i] < sCut - 0.5f) curveA.Add(curve[i]);
                    else if (cum[i] > sCut + 0.5f) curveB.Add(curve[i]);
                }
                curveA.Add(hubPos);
                curveB.Insert(0, hubPos);
                if (curveA.Count < 2) curveA.Insert(0, conn.PosA);
                if (curveB.Count < 2) curveB.Add(conn.PosB);
            }

            // Detach the segment from both hubs; the halves re-link below.
            RemoveHubLink(em, conn.HubA, segment);
            RemoveHubLink(em, conn.HubB, segment);

            var hub = CreateHub(em, hubPos, faction);

            // A hub raised THIS way never went through BuildingFactory, so it
            // had neither a NetworkId nor a DisplayName -- which meant every
            // lockstep order aimed at it was dropped by the router guard, the
            // same trap the WallExtend executor's comment describes. It matters
            // more now that a hub is where the wall's upgrades are researched.
            // Deterministic: this runs in the executor on every peer.
            if (!em.HasComponent<NetworkedEntity>(hub))
                em.AddComponentData(hub, new NetworkedEntity
                {
                    NetworkId = NetworkIdGenerator.GetNextId(),
                    SpawnTick = NetworkIdGenerator.CurrentTick,
                });
            if (!em.HasComponent<DisplayName>(hub))
                em.AddComponentData(hub, new DisplayName
                {
                    Value = TheWaningBorder.Core.DisplayNames.ForBuildingFixed(HubId),
                });

            if (before.Count == 0 && after.Count == 0)
            {
                // A one-cell segment: the hub IS the segment now.
                em.DestroyEntity(segment);
            }
            else
            {
                // The original entity keeps the A side (or, when the cell was
                // first in line, becomes the B side); the other side is new.
                bool keepA = before.Count > 0;
                var keep = keepA ? before : after;
                Entity farHub = keepA ? conn.HubA : conn.HubB;
                float3 farPos = keepA ? conn.PosA : conn.PosB;
                var keepCurve = keepA ? curveA : curveB;

                em.SetComponentData(segment, keepA
                    ? new WallConnection { HubA = conn.HubA, HubB = hub, PosA = conn.PosA, PosB = hubPos }
                    : new WallConnection { HubA = hub, HubB = conn.HubB, PosA = hubPos, PosB = conn.PosB });
                RetargetSegment(em, segment, keepCurve, keepA ? conn.PosA : hubPos, keepA ? hubPos : conn.PosB);
                var keepBuf = em.GetBuffer<WallInstanceRef>(segment);
                keepBuf.Clear();
                for (int i = 0; i < keep.Count; i++) keepBuf.Add(keep[i]);
                AddHubLink(em, farHub, hub, segment);
                AddHubLink(em, hub, farHub, segment);

                if (keepA && after.Count > 0)
                {
                    float3 midB; quaternion rotB;
                    SegmentFrame(curveB, hubPos, conn.PosB, out midB, out rotB);
                    var segB = CreateSegmentEntity(em, hub, conn.HubB, hubPos, conn.PosB, midB, rotB, curveB, faction);
                    var bufB = em.GetBuffer<WallInstanceRef>(segB);
                    for (int i = 0; i < after.Count; i++)
                    {
                        bufB.Add(after[i]);
                        if (em.Exists(after[i].Instance))
                            em.SetComponentData(after[i].Instance, new WallInstanceParent { Segment = segB });
                    }
                }
            }

            for (int i = 0; i < swallowed.Count; i++)
                if (em.Exists(swallowed[i]) && !em.HasComponent<BuildingCollapseState>(swallowed[i]))
                    em.DestroyEntity(swallowed[i]);

            return hub;
        }

        /// <summary>Midpoint + heading of a segment: along its curve when it
        /// has one, else the chord.</summary>
        static void SegmentFrame(IReadOnlyList<float3> curve, float3 posA, float3 posB,
            out float3 midpoint, out quaternion rotation)
        {
            if (curve != null && curve.Count >= 2)
            {
                var cum = ArcTable(curve);
                midpoint = SampleAlong(curve, cum, cum[cum.Length - 1] * 0.5f, out float3 tan);
                rotation = quaternion.LookRotationSafe(tan, math.up());
                return;
            }
            midpoint = (posA + posB) * 0.5f;
            float3 d = new float3(posB.x - posA.x, 0f, posB.z - posA.z);
            rotation = quaternion.LookRotationSafe(math.lengthsq(d) > 1e-6f ? math.normalize(d) : new float3(0, 0, 1), math.up());
        }

        /// <summary>Re-aim a kept segment half: new curve buffer (when it has
        /// one) and a transform at the new midpoint, which is what tells the
        /// swept-mesh visual to rebuild.</summary>
        static void RetargetSegment(EntityManager em, Entity segment, IReadOnlyList<float3> curve, float3 posA, float3 posB)
        {
            if (curve != null && em.HasBuffer<WallCurvePoint>(segment))
            {
                var cb = em.GetBuffer<WallCurvePoint>(segment);
                cb.Clear();
                for (int i = 0; i < curve.Count; i++) cb.Add(new WallCurvePoint { Position = curve[i] });
            }
            SegmentFrame(curve, posA, posB, out float3 mid, out quaternion rot);
            em.SetComponentData(segment, LocalTransform.FromPositionRotationScale(mid, rot, 1f));
        }

        /// <summary>Max centre-to-rock distance a hub will throw a terrain
        /// seal across. 2026-08-11: Red's finished chokepoint wall was
        /// flanked anyway — units squeezed through the 2-4 m gap between
        /// the end bastions and the rock face they stood against.</summary>
        public const float TerrainSealRange = 9f;

        /// <summary>
        /// TERRAIN ANCHOR (2026-08-11): when impassable TERRAIN sits within
        /// <see cref="TerrainSealRange"/> of the hub, span the gap with
        /// curtain modules so nothing squeezes between the tower and the
        /// rock. The modules hang off a SELF-SEGMENT (WallConnection.HubA ==
        /// HubB == the hub), so WallSegmentCleanupSystem retires the seal
        /// exactly like any wall piece — its modules die on their own HP and
        /// the segment goes when the last one does (walls fall in sections;
        /// the hub's death leaves the seal standing). Seals the nearest
        /// terrain bearing only; no-op when no terrain is in range, the
        /// footprint already touches, or the hub already carries a seal.
        /// Call once after hub placement.
        /// </summary>
        public static void SealToTerrain(EntityManager em, Entity hub, bool autoConstruct)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return;
            // Never seal against a mask that has not been written yet. An
            // unbaked grid is all-zero (Passable), so this would read "no
            // terrain anywhere" — and a HALF-baked one is worse, because it
            // reads blocked ground that is about to move.
            // docs/Design/Build_Grid.md § The terrain seal
            if (!grid.IsMaskReady) return;
            if (!em.Exists(hub) || !em.HasComponent<LocalTransform>(hub)) return;

            // Already sealed? (self-link in the hub's link buffer)
            if (em.HasBuffer<WallHubLink>(hub))
            {
                var existing = em.GetBuffer<WallHubLink>(hub);
                for (int i = 0; i < existing.Length; i++)
                    if (existing[i].ConnectedHub == hub) return;
            }

            float3 hubPos = em.GetComponentData<LocalTransform>(hub).Position;
            Faction faction = em.HasComponent<FactionTag>(hub)
                ? em.GetComponentData<FactionTag>(hub).Value : Faction.Blue;

            // 16-bearing scan for the nearest SHELTERING terrain face beyond
            // the tower footprint. Terrain only — buildings and razeable
            // obstacles are not shelter and must not be sealed against — and
            // a FACE, not a speck: see IsShelteringTerrain.
            float bestDist = float.MaxValue;
            float3 bestDir = default;
            for (int b = 0; b < 16; b++)
            {
                float ang = (b / 16f) * 2f * math.PI;
                float3 dir = new float3(math.cos(ang), 0f, math.sin(ang));
                for (float d = HubInset + 0.5f; d <= TerrainSealRange; d += 1f)
                {
                    float3 p = hubPos + dir * d;
                    if (!IsShelteringTerrain(grid, p, dir)) continue;
                    if (d < bestDist) { bestDist = d; bestDir = dir; }
                    break; // first sheltering sample decides this bearing
                }
            }
            if (bestDist == float.MaxValue) return;

            float start = HubInset;
            float end = bestDist + 1f;         // overlap into the rock cell
            float span = end - start;
            if (span < 0.5f) return;           // footprint already touches

            quaternion rot = quaternion.LookRotationSafe(bestDir, math.up());
            float3 mid = hubPos + bestDir * (start + span * 0.5f);

            // Self-segment — both endpoints the placing hub.
            var segment = CreateSegmentEntity(em, hub, hub, hubPos, hubPos, mid, rot, null, faction);

            // Curtain modules across the gap — collect first (CreateInstance
            // is structural), then fill the buffer.
            int count = math.max(1, (int)math.ceil(span / InstanceSpacing));
            float spacing = span / count;
            var made = new Entity[count];
            var positions = new float3[count];
            for (int i = 0; i < count; i++)
            {
                float t = start + spacing * (i + 0.5f);
                positions[i] = hubPos + bestDir * t;
                made[i] = CreateInstance(em, positions[i], rot, faction, segment);
            }
            var buf = em.GetBuffer<WallInstanceRef>(segment);
            for (int i = 0; i < count; i++)
                buf.Add(new WallInstanceRef { Instance = made[i], Position = positions[i] });

            if (autoConstruct)
            {
                for (int i = 0; i < count; i++)
                {
                    var inst = made[i];
                    if (!em.Exists(inst)) continue;
                    em.AddComponentData(inst, new UnderConstruction
                    {
                        Progress = 0f,
                        Total = 30f,
                    });
                    em.AddComponent<AutoConstructTag>(inst);
                    if (em.HasComponent<Health>(inst))
                    {
                        var hp = em.GetComponentData<Health>(inst);
                        em.SetComponentData(inst, new Health { Value = 1, Max = hp.Max });
                    }
                }
            }
        }

        /// <summary>
        /// How much blocked ground a bearing must actually find before the hub
        /// will throw a seal at it (2026-09-24). A single
        /// <see cref="PassabilityGrid.TerrainBlocked"/> cell is NOT an
        /// obstacle: slope, water paint and the odd bump leave isolated
        /// blocked cells scattered over ordinary ground, and sealing to one
        /// put a stub of curtain wall at an arbitrary bearing off perfectly
        /// open hubs — the "wall hubs sprout segments in random directions"
        /// report. A rock face you can be flanked around is several cells
        /// deep and several cells wide, so that is what we require:
        ///
        ///   * the cell itself blocked, AND
        ///   * the cell one metre FURTHER along the bearing blocked (depth —
        ///     the face continues away from the hub, it is not a pebble), AND
        ///   * at least one cell blocked to one SIDE of it (breadth — you
        ///     could not simply walk round it).
        ///
        /// Off-grid samples are rejected outright. GetCell answers
        /// TerrainBlocked for anything outside the grid, so without this a hub
        /// built anywhere near the map border sealed itself to the void on
        /// every bearing that ran off the map.
        /// docs/Design/Build_Grid.md § The terrain seal
        /// </summary>
        static bool IsShelteringTerrain(PassabilityGrid grid, float3 p, float3 dir)
        {
            if (!BlockedOnMap(grid, p)) return false;
            if (!BlockedOnMap(grid, p + dir * 1f)) return false;
            float3 side = new float3(-dir.z, 0f, dir.x);
            return BlockedOnMap(grid, p + side * 1f) || BlockedOnMap(grid, p - side * 1f);
        }

        /// <summary>Blocked by TERRAIN and inside the grid. The map edge is
        /// not an obstacle, whatever GetCell says about it.</summary>
        static bool BlockedOnMap(PassabilityGrid grid, float3 p)
        {
            var c = grid.WorldToCell(p);
            if (c.x < 0 || c.y < 0 || c.x >= grid.Width || c.y >= grid.Height) return false;
            return grid.GetCell(c) == PassabilityGrid.TerrainBlocked;
        }

        /// <summary>
        /// True if <paramref name="hubA"/> already has a <c>WallHubLink</c> entry
        /// referencing <paramref name="hubB"/>. O(N) on the link-buffer length
        /// (typically &lt; 8 per hub). Used by <c>WallAutoSegmentSystem</c> to
        /// skip already-connected pairs and avoid duplicate segment formation.
        /// </summary>
        public static bool AreHubsConnected(EntityManager em, Entity hubA, Entity hubB)
        {
            if (!em.Exists(hubA) || !em.Exists(hubB)) return false;
            if (!em.HasBuffer<WallHubLink>(hubA)) return false;
            var links = em.GetBuffer<WallHubLink>(hubA);
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i].ConnectedHub == hubB) return true;
            }
            return false;
        }


        // ── What a cell may become, and where ──────────────────────────────

        /// <summary>Contiguous FREE modules a tower or emplacement needs,
        /// counting itself: one clear module either side. You cannot stud a
        /// wall with towers shoulder to shoulder — the curtain between them
        /// is what makes it a wall (docs/Design/Age_1_Alanthor.md § What a
        /// module may become).</summary>
        public const int FreeRunForTower = 3;

        /// <summary>Contiguous FREE modules a gate needs. The gatehouse eats
        /// three of them; the fourth keeps it from butting straight into the
        /// next fitting.</summary>
        public const int FreeRunForGate = 4;

        /// <summary>
        /// A module nothing has been done to: alive, finished, and not
        /// already a gate, a tower, an emplacement or mid-conversion. A DEAD
        /// module is not free either — a breach is not building space.
        /// </summary>
        public static bool IsFreeCell(EntityManager em, Entity cell)
        {
            if (cell == Entity.Null || !em.Exists(cell)) return false;
            if (!em.HasComponent<WallInstanceTag>(cell)) return false;
            if (em.HasComponent<WallGateTag>(cell)) return false;
            if (em.HasComponent<WallTowerTag>(cell)) return false;
            if (em.HasComponent<EmplacementTag>(cell)) return false;
            if (em.HasComponent<WallUpgradeState>(cell)) return false;
            if (em.HasComponent<UnderConstruction>(cell)) return false;
            if (em.HasComponent<Health>(cell) && em.GetComponentData<Health>(cell).Value <= 0) return false;
            return true;
        }

        /// <summary>
        /// How many contiguous free modules the run containing
        /// <paramref name="cell"/> holds, walking its segment both ways. 0
        /// when the cell itself is not free. This is the whole of the
        /// placement rule: a fitting needs a run of at least N.
        /// </summary>
        public static int FreeRunAround(EntityManager em, Entity cell)
        {
            if (!IsFreeCell(em, cell)) return 0;
            if (!em.HasComponent<WallInstanceParent>(cell)) return 1;
            var segment = em.GetComponentData<WallInstanceParent>(cell).Segment;
            if (!em.Exists(segment) || !em.HasBuffer<WallInstanceRef>(segment)) return 1;

            var refs = em.GetBuffer<WallInstanceRef>(segment);
            int at = -1;
            for (int i = 0; i < refs.Length; i++) if (refs[i].Instance == cell) { at = i; break; }
            if (at < 0) return 1;

            int run = 1;
            for (int i = at - 1; i >= 0 && IsFreeCell(em, refs[i].Instance); i--) run++;
            for (int i = at + 1; i < refs.Length && IsFreeCell(em, refs[i].Instance); i++) run++;
            return run;
        }

        /// <summary>A tower needs masonry under it and a clear run around it.</summary>
        public static bool CanConvertToTower(EntityManager em, Entity cell)
            => WallTiers.AllowsTowers(WallTiers.Of(em, cell))
               && FreeRunAround(em, cell) >= FreeRunForTower;

        /// <summary>An emplacement needs the same clear run a tower does. It
        /// sits on any wall level: the engine is the weapon, not the wall.</summary>
        public static bool CanConvertToEmplacement(EntityManager em, Entity cell)
            => FreeRunAround(em, cell) >= FreeRunForTower;

        /// <summary>A gate needs a longer clear run — see FreeRunForGate.</summary>
        public static bool CanConvertToGate(EntityManager em, Entity cell)
            => FreeRunAround(em, cell) >= FreeRunForGate;

        /// <summary>
        /// Mount an engine on a standing module: the module KEEPS being wall
        /// (its HP, its footprint, its place in the segment) and gains the
        /// emplacement's platform on its crown. EmplacementCrewSystem raises
        /// the engine on it from there, exactly as it does for the
        /// free-standing platform — one system, one pair of entities, whether
        /// the platform stands on the ground or on a wall.
        /// Structural: call outside any query iteration.
        /// </summary>
        public static void MountEmplacement(EntityManager em, Entity cell, bool trebuchet)
        {
            if (cell == Entity.Null || !em.Exists(cell)) return;
            if (em.HasComponent<EmplacementTag>(cell)) return;

            string engineId = trebuchet
                ? EmplacedTrebuchet.Id : EmplacedBallista.Id;
            em.AddComponent<EmplacementTag>(cell);
            em.AddComponentData(cell, new EmplacementCrew
            {
                Engine = Entity.Null,
                Rebuild = 0f,
                RebuildTime = trebuchet
                    ? TrebuchetEmplacement.RebuildSeconds : BallistaEmplacement.RebuildSeconds,
                EngineId = engineId,
                // The engine stands on the crown, not on the ground the
                // module's origin sits on.
                MountHeight = WallHeight + 0.3f,
            });

            // A mounted module is reinforced to carry the engine.
            if (em.HasComponent<Health>(cell))
            {
                var hp = em.GetComponentData<Health>(cell);
                float frac = hp.Max > 0 ? math.saturate((float)hp.Value / hp.Max) : 1f;
                int max = (int)math.round(hp.Max * 1.5f);
                em.SetComponentData(cell, new Health
                { Max = max, Value = math.max(1, (int)math.round(max * frac)) });
            }
            if (em.HasComponent<LineOfSight>(cell))
            {
                var los = em.GetComponentData<LineOfSight>(cell);
                if (los.Radius < 20f) em.SetComponentData(cell, new LineOfSight { Radius = 20f });
            }
            // An engine needs the whole crown: no garrison shares it.
            if (em.HasBuffer<WallGarrisonSlot>(cell))
            {
                WallGarrison.EmptyModule(em, cell);
                em.RemoveComponent<WallGarrisonSlot>(cell);
            }
            if (em.HasComponent<PresentationId>(cell))
                em.SetComponentData(cell, new PresentationId
                {
                    Id = trebuchet ? TrebuchetEmplacementPresentationID : BallistaEmplacementPresentationID,
                });
        }

        // -- Cell -> gate (one structure, three modules wide) ---------------

        /// <summary>
        /// Turn <paramref name="focus"/> into THE gate and destroy the modules
        /// beside it (docs/Design/Age_1_Alanthor.md § The gate is one
        /// structure, three modules wide). The gatehouse's own masonry stands
        /// where they did, so leaving them alive would draw a wall through the
        /// gateway and give the span three health bars instead of one.
        ///
        /// <paramref name="members"/> is the window
        /// <see cref="PickGateRegionInstances"/> picked - the focus plus its
        /// neighbours. The gate inherits the whole window's HP and a footprint
        /// as wide as the modules it swallowed, so a short segment yields a
        /// narrower gatehouse rather than a wider one than it has room for.
        /// Structural: call outside any query iteration.
        /// </summary>
        public static void MakeGate(EntityManager em, Entity focus, IReadOnlyList<Entity> members)
        {
            if (focus == Entity.Null || !em.Exists(focus)) return;

            byte tier = WallTiers.Of(em, focus);
            int swallowed = 0;
            for (int i = 0; i < members.Count; i++)
                if (members[i] != Entity.Null && em.Exists(members[i])) swallowed++;
            if (swallowed == 0) swallowed = 1;
            float span = swallowed * InstanceSpacing;

            // The gatehouse's HP is ITS OWN stat, not the sum of what it
            // replaced: a gate is a heavier structure than the curtain it
            // stands in. A short gate (fewer modules swallowed) is
            // proportionally less of one.
            var gateDef = TechCatalog.Building(GateId);
            int hp = WallTiers.ScaleHp(gateDef.hp * swallowed / (float)GateRegionSpan, tier);

            // The focus becomes the gate.
            em.AddComponent<WallGateTag>(focus);
            if (!em.HasComponent<WallGateState>(focus))
                em.AddComponentData(focus, new WallGateState { IsOpen = 0, RecheckTimer = 0f });
            if (!em.HasComponent<WallGateLock>(focus))
                em.AddComponentData(focus, new WallGateLock { Sealed = 0 });
            em.AddComponentData(focus, new WallGateSpan { Metres = span });
            em.AddComponentData(focus, new WallGateGroup { Leader = focus });
            if (!em.HasComponent<WallGateRegionTag>(focus))
                em.AddComponent<WallGateRegionTag>(focus);
            em.SetComponentData(focus, new Health { Value = hp, Max = hp });
            em.SetComponentData(focus, new BuildingSize
            {
                Width = (int)WallWidth,
                Height = math.max(1, (int)math.round(span)),
            });
            em.SetComponentData(focus, new Radius { Value = span * 0.5f });
            if (gateDef.lineOfSight > 0f)
                em.SetComponentData(focus, new LineOfSight { Radius = gateDef.lineOfSight });
            if (em.HasComponent<PresentationId>(focus))
                em.SetComponentData(focus, new PresentationId { Id = GatePresentationID });
            // A gate is never a garrison position - the men stand on curtain.
            if (em.HasBuffer<WallGarrisonSlot>(focus))
                em.RemoveComponent<WallGarrisonSlot>(focus);

            // The flanks go. Their WallInstanceRef entries stay on the
            // segment (the entry outlives the cell by design), so the swept
            // mesh already knows to leave their spans open.
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m == focus || m == Entity.Null || !em.Exists(m)) continue;
                if (em.HasComponent<BuildingCollapseState>(m)) continue;
                em.DestroyEntity(m);
            }
        }

        static readonly ComponentType[] QT_AllWalls =
        {
            ComponentType.ReadOnly<WallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery QC_AllWalls;

        /// <summary>
        /// Raise every wall piece a faction owns to <paramref name="level"/>
        /// (docs/Design/Age_1_Alanthor.md § The three wall levels): HP scales
        /// off the SO's level-1 numbers, the tier is re-stamped so the visuals
        /// re-clad, and reinforced curtain modules gain their garrison slots.
        /// A wall is never a patchwork of levels, so this runs over the whole
        /// faction the moment a tech lands. Structural: call outside any query
        /// iteration. Returns how many pieces were promoted.
        /// </summary>
        public static int PromoteFactionWalls(EntityManager em, Faction faction, byte level)
        {
            var hubDef = TechCatalog.Building(HubId);
            var segDef = TechCatalog.Building(SegmentId);
            var gateDef = TechCatalog.Building(GateId);
            var towerDef = TechCatalog.Building(TowerId);
            var q = QC_AllWalls.Get(em, QT_AllWalls);
            if (q.IsEmptyIgnoreFilter) return 0;

            var pieces = q.ToEntityArray(Allocator.Temp);
            var touched = new List<Entity>();
            int promoted = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                var e = pieces[i];
                if (!em.Exists(e)) continue;
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                if (WallTiers.Of(em, e) >= level) continue;

                em.AddComponentData(e, new WallTier { Level = level });
                touched.Add(e);
                promoted++;

                // Segments are the graph edge and carry a placeholder HP; only
                // the pieces that actually take damage are rescaled.
                if (em.HasComponent<WallSegmentTag>(e)) continue;

                if (em.HasComponent<Health>(e))
                {
                    // Each piece rescales off its OWN def.
                    float baseHp;
                    if (em.HasComponent<WallGateTag>(e))
                    {
                        float span = em.HasComponent<WallGateSpan>(e)
                            ? em.GetComponentData<WallGateSpan>(e).Metres : GateSpanMetres;
                        baseHp = gateDef.hp * (span / InstanceSpacing) / GateRegionSpan;
                    }
                    else if (em.HasComponent<WallTowerTag>(e)) baseHp = towerDef.hp;
                    else if (em.HasComponent<WallHubTag>(e)) baseHp = hubDef.hp;
                    else baseHp = segDef.hp;
                    int newMax = WallTiers.ScaleHp(baseHp, level);
                    var hp = em.GetComponentData<Health>(e);
                    // Keep the damage taken so far proportional, so a promoted
                    // wall does not heal and a breached one stays breached.
                    float frac = hp.Max > 0 ? math.saturate((float)hp.Value / hp.Max) : 1f;
                    em.SetComponentData(e, new Health
                    {
                        Max = newMax,
                        Value = math.max(1, (int)math.round(newMax * frac)),
                    });
                }

                // Reinforced curtain modules gain their slots (a gate and a
                // hub never do).
                int slots = WallTiers.GarrisonSlots(level);
                if (slots > 0 && em.HasComponent<WallInstanceTag>(e)
                    && !em.HasComponent<WallGateTag>(e) && !em.HasBuffer<WallGarrisonSlot>(e))
                {
                    var buf = em.AddBuffer<WallGarrisonSlot>(e);
                    for (int k = 0; k < slots; k++) buf.Add(new WallGarrisonSlot { Occupant = Entity.Null });
                }
            }
            pieces.Dispose();

            // Re-clad: the visuals read WallTier when they are built.
            var spawn = PresentationSpawnSystem.Instance;
            if (spawn != null)
                for (int i = 0; i < touched.Count; i++)
                    if (em.Exists(touched[i])) spawn.ForceRespawn(touched[i]);
            return promoted;
        }

        /// <summary>
        /// Pick up to 3 contiguous wall instances along the segment, centred
        /// on the <paramref name="focusInstance"/>. If
        /// <paramref name="focusInstance"/> is <c>Entity.Null</c> OR not
        /// present in the segment's <see cref="WallInstanceRef"/> buffer,
        /// the segment midpoint is used as the centre. If the segment has
        /// fewer than 3 instances, every live instance is returned
        /// (cap-at-segment-length per task-109 Phase 1 / R5 — "short-segment
        /// gates allowed"). Compact-wall rework (2026-08-09): the span shrank
        /// from 5 to 3 modules — 3 m modules make a 3-wide gate ~9 m, the
        /// same opening the old 5-wide span gave at 2 m tiles.
        ///
        /// Caller owns the returned <see cref="NativeList{T}"/> and must
        /// <c>Dispose</c> it. The list is populated with at most 3 entries.
        /// Empty if the segment has no live instances or no
        /// <c>WallInstanceRef</c> buffer.
        ///
        /// (task-109 phase 5)
        /// </summary>
        public static NativeList<Entity> PickGateRegionInstances(
            EntityManager em,
            Entity segment,
            Entity focusInstance,
            Allocator allocator)
        {
            var result = new NativeList<Entity>(GateRegionSpan, allocator);

            if (!em.Exists(segment) || !em.HasBuffer<WallInstanceRef>(segment))
                return result;

            var refs = em.GetBuffer<WallInstanceRef>(segment);
            if (refs.Length == 0) return result;

            // Resolve focus index. Default = midpoint of buffer.
            int focusIdx = refs.Length / 2;
            if (focusInstance != Entity.Null)
            {
                for (int i = 0; i < refs.Length; i++)
                {
                    if (refs[i].Instance == focusInstance)
                    {
                        focusIdx = i;
                        break;
                    }
                }
            }

            // Short segment: return every FREE instance (cap-at-segment-length
            // per R5). Free, not merely live — the same reason the window
            // below is confined to the run.
            if (refs.Length <= GateRegionSpan)
            {
                for (int i = 0; i < refs.Length; i++)
                {
                    if (IsFreeCell(em, refs[i].Instance))
                        result.Add(refs[i].Instance);
                }
                return result;
            }

            // Long segment: pick a GateRegionSpan-wide window centred on
            // focusIdx, then re-anchor against either boundary so the window
            // stays valid.
            //
            // The window is confined to the FREE RUN around the focus. The
            // gate DESTROYS the modules it covers, so a window that spilled
            // past the run would quietly demolish the tower or emplacement
            // next door — re-anchoring against the segment's ends is not
            // enough when the obstacle is in the middle of it.
            int runLo = focusIdx, runHi = focusIdx;
            while (runLo > 0 && IsFreeCell(em, refs[runLo - 1].Instance)) runLo--;
            while (runHi < refs.Length - 1 && IsFreeCell(em, refs[runHi + 1].Instance)) runHi++;

            int half = GateRegionSpan / 2;
            int lo = math.max(runLo, focusIdx - half);
            int hi = math.min(runHi, lo + GateRegionSpan - 1);
            lo = math.max(runLo, hi - (GateRegionSpan - 1)); // re-anchor if hi clamped

            for (int i = lo; i <= hi; i++)
            {
                if (em.Exists(refs[i].Instance))
                    result.Add(refs[i].Instance);
            }
            return result;
        }
    }
}
