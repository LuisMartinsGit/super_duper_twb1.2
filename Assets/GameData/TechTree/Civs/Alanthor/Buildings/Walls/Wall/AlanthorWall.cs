// Alanthor wall system: hub (3x3 squat bastion) + segment (data-only graph edge) + instances (3x1 curtain modules)
// Walls form the backbone of Alanthor economy — enclosed areas generate income.
// Each segment spawns multiple small wall instances that block the passability grid.
// Instances can be upgraded to towers (ranged attack) or gates (friendly-only passage).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Factory for Alanthor wall entities.
    /// Walls consist of hubs (connection points), segments (logical graph edges),
    /// and instances (small wall pieces that block pathfinding).
    /// </summary>
    public static class AlanthorWall
    {
        public const int HubPresentationID = 550;
        // Segment no longer has a visual (data-only graph edge)
        public const int InstancePresentationID = 552;
        public const int TowerPresentationID = 553;
        public const int GatePresentationID = 554;

        /// <summary>Length of each wall module along the wall, in meters.
        /// Compact-wall rework (2026-08-09): 3 m curtain modules replace the
        /// old 4 m walkable-rampart tiles. Walls are now solid curtain walls —
        /// no walkable deck.</summary>
        public const float InstanceSpacing = 3f;

        /// <summary>Compact curtain-wall cross-section, in meters.</summary>
        public const float WallWidth = 1f;     // masonry thickness across the wall (X)
        public const float WallHeight = 2.6f;  // parapet crown top (solid curtain, no deck)

        /// <summary>Hub footprint width, in meters. 4 x 4 build cells — hubs
        /// are buildings and snap to the 2 m grid like any other, so this must
        /// stay in step with BuildingSizeConfig's "Alanthor_Wall" entry
        /// (doubled 2026-08-13 with every other building footprint).
        /// The curtain SEGMENTS between hubs remain freeform, at whatever
        /// bearing the hub-to-hub line has. docs/Design/Build_Grid.md</summary>
        public const float HubWidth = 8f;

        /// <summary>Number of contiguous wall instances a segment-level
        /// Convert-to-Gate replaces (compact-wall rework: 3 modules x 3 m
        /// = ~9 m gatehouse). Consumed by
        /// <see cref="PickGateRegionInstances"/>.</summary>
        public const int GateRegionSpan = 3;

        /// <summary>Inset from each hub center to the first wall module, in meters.
        /// Half the hub footprint so a module's near edge meets the bastion face
        /// instead of overlapping the hub core.</summary>
        private const float HubInset = HubWidth * 0.5f;

        /// <summary>
        /// Create a wall hub entity (the cylinder connection point).
        /// </summary>
        public static Entity CreateHub(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("Alanthor_Wall");
            float hp = def.hp;
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
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            // Compact hub: a squat 2x2-cell bastion footprint so build-range /
            // selection / passability use the real size.
            em.SetComponentData(entity, new BuildingSize { Width = (int)HubWidth, Height = (int)HubWidth });
            em.SetComponentData(entity, new Radius { Value = HubWidth * 0.5f });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            // Dynamic buffer for tracking connections to other hubs
            em.AddBuffer<WallHubLink>(entity);

            return entity;
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

            // Segment entity: data-only graph edge (no PresentationId, no BuildingSize)
            var entity = em.CreateEntity(
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(WallTag),
                typeof(WallSegmentTag),
                typeof(WallConnection)
            );

            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                midpoint, rotation, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = 1, Max = 1 }); // structural placeholder
            em.SetComponentData(entity, new WallConnection { HubA = hubA, HubB = hubB });

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

            // Update hub connection buffers
            if (em.HasBuffer<WallHubLink>(hubA))
            {
                var bufA = em.GetBuffer<WallHubLink>(hubA);
                bufA.Add(new WallHubLink { ConnectedHub = hubB, Segment = entity });
            }

            if (em.HasBuffer<WallHubLink>(hubB))
            {
                var bufB = em.GetBuffer<WallHubLink>(hubB);
                bufB.Add(new WallHubLink { ConnectedHub = hubA, Segment = entity });
            }

            // Spawn wall instances along the line
            SpawnInstances(em, entity, posA, posB, dirFlat, rotation, faction);

            return entity;
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
                buf.Add(new WallInstanceRef { Instance = inst });
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
            for (int i = 0; i < count; i++)
            {
                float t = HubInset + actualSpacing * (i + 0.5f);
                float3 pos = posA + direction * t;
                instances[i] = CreateInstance(em, pos, rotation, faction, segment);
            }

            // Now safe to get buffer and populate it (no more structural changes)
            var buffer = em.GetBuffer<WallInstanceRef>(segment);
            for (int i = 0; i < count; i++)
            {
                buffer.Add(new WallInstanceRef { Instance = instances[i] });
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
            var def = TechCatalog.Building("Alanthor_Wall");

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
            em.SetComponentData(entity, new Health { Value = (int)def.segmentHp, Max = (int)def.segmentHp });
            em.SetComponentData(entity, new LineOfSight { Radius = def.segmentLineOfSight });
            // Compact curtain footprint: 1 m thick across the wall, one 3 m
            // module long. Solid obstacle on the passability grid.
            em.SetComponentData(entity, new BuildingSize { Width = (int)WallWidth, Height = (int)InstanceSpacing });
            em.SetComponentData(entity, new Radius { Value = InstanceSpacing * 0.5f });
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

        /// <summary>Max centre-to-rock distance a hub will throw a terrain
        /// seal across. 2026-08-11: Red's finished chokepoint wall was
        /// flanked anyway — units squeezed through the 2-4 m gap between
        /// the end bastions and the rock face they stood against.</summary>
        public const float TerrainSealRange = 9f;

        /// <summary>
        /// TERRAIN ANCHOR (2026-08-11): when impassable TERRAIN sits within
        /// <see cref="TerrainSealRange"/> of the hub, span the gap with
        /// curtain modules so nothing squeezes between the bastion and the
        /// rock. The modules hang off a SELF-SEGMENT (WallConnection.HubA ==
        /// HubB == the hub), so WallSegmentCleanupSystem cascades the seal
        /// exactly like any wall piece — hub dies, seal dies; modules die,
        /// segment dies. Seals the nearest terrain bearing only; no-op when
        /// no terrain is in range, the footprint already touches, or the
        /// hub already carries a seal. Call once after hub placement.
        /// </summary>
        public static void SealToTerrain(EntityManager em, Entity hub, bool autoConstruct)
        {
            var grid = TheWaningBorder.World.Terrain.PassabilityGrid.Instance;
            if (grid == null) return;
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

            // 16-bearing scan for the nearest terrain-blocked cell beyond
            // the bastion footprint. Terrain only — buildings and razeable
            // obstacles are not shelter and must not be sealed against.
            float bestDist = float.MaxValue;
            float3 bestDir = default;
            for (int b = 0; b < 16; b++)
            {
                float ang = (b / 16f) * 2f * math.PI;
                float3 dir = new float3(math.cos(ang), 0f, math.sin(ang));
                for (float d = HubInset + 0.5f; d <= TerrainSealRange; d += 1f)
                {
                    float3 p = hubPos + dir * d;
                    if (grid.GetCell(grid.WorldToCell(p))
                        != TheWaningBorder.World.Terrain.PassabilityGrid.TerrainBlocked)
                        continue;
                    if (d < bestDist) { bestDist = d; bestDir = dir; }
                    break; // first blocked sample decides this bearing
                }
            }
            if (bestDist == float.MaxValue) return;

            float start = HubInset;
            float end = bestDist + 1f;         // overlap into the rock cell
            float span = end - start;
            if (span < 0.5f) return;           // footprint already touches

            quaternion rot = quaternion.LookRotationSafe(bestDir, math.up());
            float3 mid = hubPos + bestDir * (start + span * 0.5f);

            // Self-segment — mirrors CreateSegment's archetype, both
            // endpoints the placing hub.
            var segment = em.CreateEntity(
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(WallTag),
                typeof(WallSegmentTag),
                typeof(WallConnection)
            );
            em.SetComponentData(segment, LocalTransform.FromPositionRotationScale(mid, rot, 1f));
            em.SetComponentData(segment, new FactionTag { Value = faction });
            em.SetComponentData(segment, new BuildingTag { IsBase = 0 });
            em.SetComponentData(segment, new Health { Value = 1, Max = 1 });
            em.SetComponentData(segment, new WallConnection { HubA = hub, HubB = hub });
            em.AddComponentData(segment, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(segment, new Radius { Value = 0.1f });
            em.AddBuffer<WallInstanceRef>(segment);
            em.AddComponentData(segment, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Curtain modules across the gap — collect first (CreateInstance
            // is structural), then fill the buffer.
            int count = math.max(1, (int)math.ceil(span / InstanceSpacing));
            float spacing = span / count;
            var made = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                float t = start + spacing * (i + 0.5f);
                made[i] = CreateInstance(em, hubPos + bestDir * t, rot, faction, segment);
            }
            var buf = em.GetBuffer<WallInstanceRef>(segment);
            for (int i = 0; i < count; i++)
                buf.Add(new WallInstanceRef { Instance = made[i] });

            // Register on the hub's link buffer (self-link) so the
            // hub-death cascade and the seal-dedup check both see it.
            if (em.HasBuffer<WallHubLink>(hub))
            {
                var links = em.GetBuffer<WallHubLink>(hub);
                links.Add(new WallHubLink { ConnectedHub = hub, Segment = segment });
            }

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

            // Short segment: return every live instance unconditionally
            // (cap-at-segment-length per R5).
            if (refs.Length <= GateRegionSpan)
            {
                for (int i = 0; i < refs.Length; i++)
                {
                    if (em.Exists(refs[i].Instance))
                        result.Add(refs[i].Instance);
                }
                return result;
            }

            // Long segment: pick a GateRegionSpan-wide window centred on
            // focusIdx, then re-anchor against either boundary so the window
            // stays valid.
            int half = GateRegionSpan / 2;
            int lo = math.max(0, focusIdx - half);
            int hi = math.min(refs.Length - 1, lo + GateRegionSpan - 1);
            lo = math.max(0, hi - (GateRegionSpan - 1)); // re-anchor if hi clamped

            for (int i = lo; i <= hi; i++)
            {
                if (em.Exists(refs[i].Instance))
                    result.Add(refs[i].Instance);
            }
            return result;
        }
    }
}
