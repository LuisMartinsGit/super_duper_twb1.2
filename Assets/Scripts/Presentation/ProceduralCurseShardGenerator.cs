// ProceduralCurseShardGenerator.cs
// Builds the per-tile visual for the Crystal-faction "cursed ground" —
// Iteration 2 (2026-05-21): VOXEL GRID rewrite.
//
// Geometry pivot (task-cursed-ground-luminous-crystals-111, Iteration 2):
//   The previous shard / starburst / scatter-carpet layers are replaced
//   with a SineVFX "Living Particles"-style voxel-block grid. Each
//   cursed-ground tile spawns a small grid of upright rectangular
//   prisms centred on integer world coordinates. Adjacent tiles share
//   the same world-grid origin, so their grids tile seamlessly into
//   one continuous block field — no per-tile cluster silhouette.
//
//   Block width: 0.85 m on a 1.0 m grid step → ~0.15 m gap shows as
//   the dark grid line. Height varies per (gridX, gridZ) cell, hashed
//   from integer coords so adjacent tiles' shared cells produce the
//   exact same height. Inner cells (close to a node centre): 0.4-1.2 m.
//   Outer cells: 0.15-0.5 m.
//
//   The class name "ProceduralCurseShardGenerator" is kept stable to
//   avoid .meta churn / call-site renames; internally the shape is
//   now a grid of blocks ("voxels"), not shards. See task spec for
//   the visual rationale.
//
// Determinism / multiplayer:
//   Per-cell heights are hashed from integer world-grid coords via
//   Unity.Mathematics.Random — peer-deterministic without any
//   per-tile RNG seed. Cell-claim dedup is a static HashSet<int2>
//   on managed code (presentation only, not gameplay), so two peers
//   walking the same tile-spawn order produce identical block fields.
//   Hero-light selection still uses `Entity.Index % 10 == 0 && bucket
//   <= 2` (deterministic).
//
// Perf:
//   - One shared Mesh (Unity primitive Cube) across every block.
//   - 5 shared Materials (one per gradient bucket). SRP batcher
//     batches per material → ~5 SetPass calls for the entire
//     cursed-area block population.
//   - No Colliders on blocks — they don't block click-selection.
//   - No shadow casting.
//   - Growth animator self-destructs after the 1 s growth window
//     completes — steady-state per-frame cost from animators is zero.
//   - Recession animator only exists during the ~0.7 s death window.
//   - Pulse driver still drives emission via MaterialPropertyBlock.
//
// Death-handoff (item 7):
//   When a cursed-ground entity is destroyed, the standard PSS
//   cleanup path checks for a CurseBlockRecessionAnimator on the GO
//   before calling Destroy(). If present, PSS calls
//   animator.BeginDeath() and skips immediate destruction — the
//   animator owns the GO's final cleanup, detaches it from
//   EntityViewManager tracking, and destroys after the recession
//   animation finishes.
//
// Location: Assets/Scripts/Presentation/ProceduralCurseShardGenerator.cs

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Static factory for the procedural visual of a single cursed-ground tile.
    /// Builds a voxel-block grid (Iteration 2) snapped to integer world coords
    /// so adjacent tiles tile seamlessly. Registers the cluster with
    /// <see cref="CursedPulseDriver"/> for the wavefront pulse animation.
    /// </summary>
    public static class ProceduralCurseShardGenerator
    {
        // ---- Color palette (unchanged from Iteration 1) ----

        // Inner ring — purple/violet.
        // Base #5C2A7A → (0.361, 0.165, 0.478)
        // Emission #9B5BE0 → (0.608, 0.357, 0.878)
        private static readonly Color InnerBase     = new Color(0.361f, 0.165f, 0.478f);
        private static readonly Color InnerEmission = new Color(0.608f, 0.357f, 0.878f);
        private const float InnerEmissionIntensity  = 0.50f;

        // Outer ring — sickly green.
        // Base #3D6B2A → (0.239, 0.420, 0.165)
        // Emission #7AC83A → (0.478, 0.784, 0.227)
        private static readonly Color OuterBase     = new Color(0.239f, 0.420f, 0.165f);
        private static readonly Color OuterEmission = new Color(0.478f, 0.784f, 0.227f);
        private const float OuterEmissionIntensity  = 0.25f;

        // ---- Voxel grid tunables ----

        /// <summary>World-space size of one grid cell. Blocks snap to integer
        /// multiples of this so adjacent tiles tile seamlessly.</summary>
        private const float GridStep = 1.0f;

        /// <summary>Block XZ footprint as a fraction of grid step. 0.85 means a
        /// 0.85m-wide block on a 1.0m grid leaves a 0.15m dark gap.</summary>
        private const float BlockWidthRatio = 0.85f;

        /// <summary>Radius around the tile centre that the grid covers (m).
        /// 2.5 m at 1.0 m step → roughly a 5×5 cell window per tile.</summary>
        private const float GridCoverRadius = 2.5f;

        /// <summary>Inner-cell height range (m).</summary>
        private const float BlockHeightInnerMin = 0.40f;
        private const float BlockHeightInnerMax = 1.20f;

        /// <summary>Outer-cell height range (m).</summary>
        private const float BlockHeightOuterMin = 0.15f;
        private const float BlockHeightOuterMax = 0.50f;

        /// <summary>Fallback spread radius if the owning node lookup fails.</summary>
        private const float FallbackSpreadRadius = 15f;

        /// <summary>Number of gradient buckets for shared materials.</summary>
        private const int GradientBucketCount = 5;

        // ---- Splatmap filter ----

        private const float CurseSplatThreshold = 0.25f;

        // ---- Growth animation tunables (item 5) ----

        private const float GrowthDuration = 1.0f;

        // ---- Recession animation tunables (item 7) ----

        public const float RecessionDuration = 0.7f;

        // ---- Hero-light tunables (unchanged from Iteration 1) ----

        private const int HeroLightModulo = 10;
        private const int HeroLightMaxBucket = 2;
        private const float HeroLightYOffset   = 0.6f;
        private const float HeroLightRange     = 2.8f;
        private const float HeroLightIntensity = 1.5f;

        // ---- Shared mesh + material caches ----

        private static Mesh _blockMesh;
        private static readonly Material[] _blockMaterials = new Material[GradientBucketCount];
        private static readonly Color[] _blockBaseEmissions = new Color[GradientBucketCount];
        private static readonly bool[]  _blockBaseEmissionsBuilt = new bool[GradientBucketCount];
        private static Shader _litShader;

        // One-time runtime notice that URP Bloom must be on.
        private static bool _bloomNoticeFired;

        // ---- Cell-claim dedup (open question 1) ----
        //
        // Adjacent cursed-ground tiles' grids overlap at the seam. Without
        // dedup we'd render two blocks at the same world cell → z-fighting
        // on the top face. This static HashSet records every (gridX, gridZ)
        // cell that already has a block. Each cluster passes its claimed
        // cells to its animator(s) so they can free the cells on destroy.

        private static readonly HashSet<int2> _claimedCells = new HashSet<int2>(4096);

        // ---- Splatmap caching ----

        private static Terrain _cachedTerrain;
        private static int _cachedCurseLayerIndex = -2;

        private static bool TryGetCurseLayerIndex(out Terrain terrain, out int curseIndex)
        {
            if (_cachedCurseLayerIndex == -2)
            {
                _cachedTerrain = Terrain.activeTerrain;
                if (_cachedTerrain == null || _cachedTerrain.terrainData == null)
                {
                    _cachedCurseLayerIndex = -1;
                }
                else
                {
                    int found = -1;
                    var layers = _cachedTerrain.terrainData.terrainLayers;
                    for (int i = 0; i < layers.Length; i++)
                    {
                        if (layers[i] != null && layers[i].name != null
                            && layers[i].name.IndexOf("Curse", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = i;
                            break;
                        }
                    }
                    _cachedCurseLayerIndex = found;
                }
            }
            terrain = _cachedTerrain;
            curseIndex = _cachedCurseLayerIndex;
            return terrain != null && curseIndex >= 0;
        }

        private static Shader LitShader =>
            _litShader != null
                ? _litShader
                : (_litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

        // ====================================================================
        //  PUBLIC API
        // ====================================================================

        /// <summary>
        /// Build the voxel-block cluster GameObject for a single cursed-ground tile.
        /// Reads <see cref="OwnerNode"/> + the node's <see cref="CrystalNode"/>
        /// to compute the per-tile gradient. Falls back to an approximate
        /// gradient if either lookup fails. Registers the cluster with
        /// <see cref="CursedPulseDriver"/> for the wavefront pulse.
        /// </summary>
        public static GameObject Create(Vector3 worldPos, Entity tileEntity, EntityManager em)
        {
            // 0. One-time bloom prerequisite notice.
            if (!_bloomNoticeFired)
            {
                _bloomNoticeFired = true;
                Debug.Log(
                    "[CurseBlocks] First cursed-ground voxel block spawned. " +
                    "Verify URP Bloom is enabled in the active Volume profile — " +
                    "emission without bloom reads flat.");
            }

            // 1. Distance + node-centre lookup.
            ComputeDistance(worldPos, tileEntity, em,
                out float distanceFromCenter, out float normalisedDistance,
                out Vector3 nodeCenter, out float maxRadius);

            // 2. Root GameObject. Anchored at the tile world position.
            var root = new GameObject($"CursedGround_{tileEntity.Index}");
            root.transform.position = worldPos;

            // 3. Splatmap chunk pre-fetch — same approach as Iteration 1.
            bool haveSplatFilter = TryGetCurseLayerIndex(out var splatTerrain, out int curseLayer);
            float[,,] splatChunk = null;
            int splatOX = 0, splatOZ = 0, splatW = 0, splatH = 0;
            Vector3 terrainPos = Vector3.zero;
            float invSizeX = 0f, invSizeZ = 0f;
            int alphaRes = 0;
            if (haveSplatFilter)
            {
                var td = splatTerrain.terrainData;
                terrainPos = splatTerrain.transform.position;
                alphaRes = td.alphamapResolution;
                invSizeX = 1f / td.size.x;
                invSizeZ = 1f / td.size.z;

                float chunkRadius = GridCoverRadius + 1.0f; // +1m margin
                float ax = (worldPos.x - terrainPos.x) * invSizeX * alphaRes;
                float az = (worldPos.z - terrainPos.z) * invSizeZ * alphaRes;
                float arx = chunkRadius * invSizeX * alphaRes;
                float arz = chunkRadius * invSizeZ * alphaRes;
                splatOX = math.max(0, (int)math.floor(ax - arx));
                splatOZ = math.max(0, (int)math.floor(az - arz));
                int maxX = math.min(alphaRes, (int)math.ceil(ax + arx) + 1);
                int maxZ = math.min(alphaRes, (int)math.ceil(az + arz) + 1);
                splatW = maxX - splatOX;
                splatH = maxZ - splatOZ;
                if (splatW > 0 && splatH > 0)
                {
                    splatChunk = td.GetAlphamaps(splatOX, splatOZ, splatW, splatH);
                }
                else
                {
                    haveSplatFilter = false;
                }
            }

            // 4. Walk the integer-grid window around the tile centre.
            //    Cells snap to integer multiples of GridStep so adjacent tiles
            //    share the exact same cell coordinates at their seams.
            int gridMinX = (int)math.floor((worldPos.x - GridCoverRadius) / GridStep);
            int gridMaxX = (int)math.floor((worldPos.x + GridCoverRadius) / GridStep);
            int gridMinZ = (int)math.floor((worldPos.z - GridCoverRadius) / GridStep);
            int gridMaxZ = (int)math.floor((worldPos.z + GridCoverRadius) / GridStep);

            var renderers = new List<MeshRenderer>(32);
            var targetHeights = new List<float>(32);
            var claimedByThisCluster = new List<int2>(32);
            var blockDepths = new List<int>(32);

            // Mesh + per-cluster bucket choice for hero-light + pulse driver.
            var sharedMesh = GetBlockMesh();
            int clusterBucket = math.clamp(
                (int)(normalisedDistance * GradientBucketCount),
                0, GradientBucketCount - 1);

            // Node-centre cell coords for BFS-depth assignment (Chebyshev
            // distance from node centre cell). All blocks share this
            // reference, so depths are consistent across cluster spawns.
            int nodeCellX = (int)math.floor(nodeCenter.x / GridStep);
            int nodeCellZ = (int)math.floor(nodeCenter.z / GridStep);

            for (int gz = gridMinZ; gz <= gridMaxZ; gz++)
            {
                for (int gx = gridMinX; gx <= gridMaxX; gx++)
                {
                    // 4a. Cell-centre world position.
                    float cellCenterX = (gx + 0.5f) * GridStep;
                    float cellCenterZ = (gz + 0.5f) * GridStep;

                    // 4b. Skip cells outside the per-tile coverage disc — keeps
                    //     the grid window roughly circular per tile rather than
                    //     square.
                    float dxFromTile = cellCenterX - worldPos.x;
                    float dzFromTile = cellCenterZ - worldPos.z;
                    if (dxFromTile * dxFromTile + dzFromTile * dzFromTile
                        > GridCoverRadius * GridCoverRadius) continue;

                    // 4c. Splatmap gate — only spawn where the curse is painted.
                    if (haveSplatFilter)
                    {
                        int ax = (int)((cellCenterX - terrainPos.x) * invSizeX * alphaRes) - splatOX;
                        int az = (int)((cellCenterZ - terrainPos.z) * invSizeZ * alphaRes) - splatOZ;
                        if (ax < 0 || az < 0 || ax >= splatW || az >= splatH) continue;
                        if (splatChunk[az, ax, curseLayer] < CurseSplatThreshold) continue;
                    }

                    // 4d. Cell-claim dedup — skip if another cluster already
                    //     spawned a block at this world cell.
                    var cellKey = new int2(gx, gz);
                    if (_claimedCells.Contains(cellKey)) continue;

                    // 4e. Deterministic per-cell height. Hash the cell coords
                    //     (NOT the tile entity) so shared cells across tiles
                    //     produce the same height — multiplayer-safe and
                    //     seam-stable.
                    uint cellHash = ((uint)gx * 374761393u) ^ ((uint)gz * 668265263u) | 1u;
                    var cellRng = new Unity.Mathematics.Random(cellHash);

                    // 4f. Compute the cell's normalised distance against the
                    //     OWNING NODE (not the tile) so adjacent tiles' shared
                    //     cells get the same height-range bucket.
                    float cellDistX = cellCenterX - nodeCenter.x;
                    float cellDistZ = cellCenterZ - nodeCenter.z;
                    float cellDist = math.sqrt(cellDistX * cellDistX + cellDistZ * cellDistZ);
                    float cellNorm = math.saturate(cellDist / math.max(0.01f, maxRadius));

                    float hMin = math.lerp(BlockHeightInnerMin, BlockHeightOuterMin, cellNorm);
                    float hMax = math.lerp(BlockHeightInnerMax, BlockHeightOuterMax, cellNorm);
                    float targetHeight = cellRng.NextFloat(hMin, hMax);

                    // 4g. Per-cell bucket → material. Each block can sit in a
                    //     different bucket from its neighbours (open question
                    //     3). SRP batcher batches per material so this stays
                    //     cheap.
                    int cellBucket = math.clamp(
                        (int)(cellNorm * GradientBucketCount),
                        0, GradientBucketCount - 1);
                    var sharedMaterial = GetBlockMaterial(cellBucket);

                    // 4h. Spawn the block GameObject. The hex prism mesh is
                    //     anchored at y=0 and rises to y=1 in local space,
                    //     so we set localPosition.y = 0 (no half-height
                    //     offset like the old cube needed).
                    var block = new GameObject($"Block_{gx}_{gz}");
                    block.transform.SetParent(root.transform, worldPositionStays: false);
                    block.transform.localPosition = new Vector3(
                        cellCenterX - worldPos.x,
                        0f,
                        cellCenterZ - worldPos.z);
                    // Deterministic per-cell Y rotation so the hex prisms
                    // don't all face the same direction (would read as a
                    // pattern). Snapped to 6 discrete angles since the
                    // prism has 6-fold rotational symmetry — finer steps
                    // would only matter if facets cast distinguishable
                    // shadows, which they don't.
                    float yRotDeg = cellRng.NextInt(0, 6) * 60f
                                  + cellRng.NextFloat(-12f, 12f);
                    block.transform.localRotation = Quaternion.AngleAxis(yRotDeg, Vector3.up);
                    // Start scaled to zero on Y so the growth animator can
                    // ramp it up. X/Z width is GridStep * BlockWidthRatio.
                    float blockW = GridStep * BlockWidthRatio;
                    block.transform.localScale = new Vector3(blockW, 0f, blockW);

                    var mf = block.AddComponent<MeshFilter>();
                    mf.sharedMesh = sharedMesh;
                    var mr = block.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = sharedMaterial;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    renderers.Add(mr);
                    targetHeights.Add(targetHeight);

                    // BFS ring depth = Chebyshev distance from node-centre
                    // cell. Drives the per-block pulse phase in the driver:
                    // depth 0 fires first, depth 1 next, etc.
                    int depth = math.max(math.abs(gx - nodeCellX),
                                         math.abs(gz - nodeCellZ));
                    blockDepths.Add(depth);

                    _claimedCells.Add(cellKey);
                    claimedByThisCluster.Add(cellKey);
                }
            }

            // 5. If nothing spawned (whole grid outside the splat / all cells
            //    claimed by a neighbour), return an empty root. PSS still
            //    tracks it so the entity gets a registered view.
            if (renderers.Count == 0)
            {
                return root;
            }

            // 6. Hero-light selection — same rule as Iteration 1.
            Light heroLight = null;
            float baseLightIntensity = 0f;
            bool isHeroTile = (tileEntity.Index % HeroLightModulo) == 0
                           && clusterBucket <= HeroLightMaxBucket;
            if (isHeroTile)
            {
                var lightGo = new GameObject("CurseLight");
                lightGo.transform.SetParent(root.transform, worldPositionStays: false);
                lightGo.transform.localPosition = new Vector3(0f, HeroLightYOffset, 0f);

                heroLight = lightGo.AddComponent<Light>();
                heroLight.type = LightType.Point;
                heroLight.range = HeroLightRange;
                heroLight.intensity = HeroLightIntensity;
                heroLight.color = GetHeroLightColorForBucket(clusterBucket);
                heroLight.shadows = LightShadows.None;
                heroLight.bounceIntensity = 0f;
                heroLight.renderMode = LightRenderMode.ForcePixel;
                baseLightIntensity = HeroLightIntensity;
            }

            // 7. Register with the pulse driver. Each block carries its own
            //    BFS ring depth so the driver lights blocks ring-by-ring
            //    (chain reaction from node centre outward). Hero-light pulse
            //    uses the depth of the cluster centre cell.
            var sharedEmission = GetBlockBaseEmission(clusterBucket);
            int tileCellX = (int)math.floor(worldPos.x / GridStep);
            int tileCellZ = (int)math.floor(worldPos.z / GridStep);
            int heroDepth = math.max(math.abs(tileCellX - nodeCellX),
                                     math.abs(tileCellZ - nodeCellZ));
            CursedPulseDriver.Register(
                root.transform,
                renderers.ToArray(),
                blockDepths.ToArray(),
                heroLight,
                heroDepth,
                sharedEmission,
                baseLightIntensity);

            // 8. Attach the growth animator (item 5). It scales each block's
            //    Y from 0 → target over GrowthDuration, emits a brief
            //    particle burst at the block tops, and self-destructs.
            var growth = root.AddComponent<CurseBlockGrowthAnimator>();
            growth.Init(
                blocks: renderers,
                targetHeights: targetHeights,
                duration: GrowthDuration,
                emissionColor: sharedEmission);

            // 9. Attach the recession animator IN-WAITING (item 7). It only
            //    animates once PSS hands off death; until then it does nothing
            //    per frame.
            var recess = root.AddComponent<CurseBlockRecessionAnimator>();
            recess.Init(
                blocks: renderers,
                duration: RecessionDuration,
                nodeCenter: nodeCenter,
                claimedCells: claimedByThisCluster);

            return root;
        }

        /// <summary>
        /// Releases cells claimed by a cluster back to the static pool.
        /// Called from the growth or recession animator on destroy.
        /// </summary>
        public static void ReleaseClaimedCells(IList<int2> cells)
        {
            if (cells == null) return;
            for (int i = 0; i < cells.Count; i++) _claimedCells.Remove(cells[i]);
        }

        /// <summary>
        /// Color the per-tile hero Point Light should use, sampled from the
        /// same purple→green gradient the block materials use.
        /// </summary>
        private static Color GetHeroLightColorForBucket(int bucket)
        {
            bucket = math.clamp(bucket, 0, GradientBucketCount - 1);
            float t = (bucket + 0.5f) / GradientBucketCount;
            return Color.Lerp(InnerEmission, OuterEmission, t);
        }

        // ====================================================================
        //  GRADIENT / DISTANCE HELPERS
        // ====================================================================

        /// <summary>
        /// Resolves the tile's distance from the owning node centre, both in
        /// raw metres (for pulse phase) and normalised to [0, 1] against
        /// SpreadRadius. Also returns the node centre + max radius so the
        /// caller can compute PER-CELL distances against the node (not the
        /// tile) for height + bucket assignment.
        /// </summary>
        private static void ComputeDistance(Vector3 tilePos, Entity tileEntity, EntityManager em,
            out float distanceFromCenter, out float normalisedDistance,
            out Vector3 nodeCenter, out float maxRadius)
        {
            distanceFromCenter = FallbackSpreadRadius;
            normalisedDistance = 1f;
            nodeCenter = tilePos;
            maxRadius = FallbackSpreadRadius;

            if (em == default) return;
            if (!em.Exists(tileEntity)) return;
            if (!em.HasComponent<OwnerNode>(tileEntity)) return;

            var owner = em.GetComponentData<OwnerNode>(tileEntity).Value;
            if (owner == Entity.Null || !em.Exists(owner)) return;

            if (em.HasComponent<CrystalNode>(owner))
            {
                var cn = em.GetComponentData<CrystalNode>(owner);
                if (cn.SpreadRadius > 0.01f) maxRadius = cn.SpreadRadius;
            }

            if (!em.HasComponent<LocalTransform>(owner)) return;
            var np = em.GetComponentData<LocalTransform>(owner).Position;
            nodeCenter = new Vector3(np.x, np.y, np.z);

            float dx = tilePos.x - nodeCenter.x;
            float dz = tilePos.z - nodeCenter.z;
            distanceFromCenter = math.sqrt(dx * dx + dz * dz);
            normalisedDistance = math.saturate(distanceFromCenter / maxRadius);
        }

        // ====================================================================
        //  SHARED MESH (tapered hexagonal crystal prism, built once)
        // ====================================================================

        /// <summary>
        /// Returns the shared tapered-hex-prism mesh used by every block.
        /// 6 sides, base radius 0.5 × top radius 0.32 (tapered to 64%),
        /// height 1.0 anchored at <c>y = 0</c> (rises into +Y). Per-face
        /// vertex duplication so RecalculateNormals produces sharp facets
        /// rather than smooth cylinder shading — the crystal reads
        /// faceted, not lathed. Generated lazily on first request.
        /// </summary>
        private static Mesh GetBlockMesh()
        {
            if (_blockMesh != null) return _blockMesh;

            const int sides = 6;
            const float baseR = 0.5f;
            const float topR  = 0.32f;
            const float bottomY = 0f;
            const float topY    = 1f;

            // Pre-compute the two rings of points.
            var baseRing = new Vector3[sides];
            var topRing  = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * (Mathf.PI * 2f / sides);
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                baseRing[i] = new Vector3(c * baseR, bottomY, s * baseR);
                topRing[i]  = new Vector3(c * topR,  topY,    s * topR);
            }

            // Per-face vert duplication for sharp facets.
            // - 6 side quads × 4 verts each = 24 side verts
            // - 6 top-cap verts (one ring) + 1 centre = 7 cap verts
            // - 6 bottom-cap verts + 1 centre = 7 cap verts
            // Total: 38 verts, 20 tris (12 side + 4 top + 4 bottom).
            var verts = new List<Vector3>(38);
            var tris  = new List<int>(20 * 3);

            // Side faces — 6 quads, 4 unique verts each so the side
            // normals come out perpendicular to each face (faceted).
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                int baseIdx = verts.Count;
                verts.Add(baseRing[i]); // 0: bottom-current
                verts.Add(baseRing[j]); // 1: bottom-next
                verts.Add(topRing[j]);  // 2: top-next
                verts.Add(topRing[i]);  // 3: top-current
                // CCW from outside: (0, 2, 1) + (0, 3, 2)
                tris.Add(baseIdx + 0); tris.Add(baseIdx + 2); tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 0); tris.Add(baseIdx + 3); tris.Add(baseIdx + 2);
            }

            // Top cap — fan from centre, all verts in y=topY plane so the
            // cap reads as one flat face.
            {
                int topCenterIdx = verts.Count;
                verts.Add(new Vector3(0f, topY, 0f));
                int firstIdx = verts.Count;
                for (int i = 0; i < sides; i++) verts.Add(topRing[i]);
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    tris.Add(topCenterIdx);
                    tris.Add(firstIdx + i);
                    tris.Add(firstIdx + j);
                }
            }

            // Bottom cap — reversed winding so normals face -Y.
            {
                int botCenterIdx = verts.Count;
                verts.Add(new Vector3(0f, bottomY, 0f));
                int firstIdx = verts.Count;
                for (int i = 0; i < sides; i++) verts.Add(baseRing[i]);
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    tris.Add(botCenterIdx);
                    tris.Add(firstIdx + j);
                    tris.Add(firstIdx + i);
                }
            }

            var mesh = new Mesh
            {
                name = "CurseCrystalHexPrism",
                vertices = verts.ToArray(),
                triangles = tris.ToArray(),
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _blockMesh = mesh;
            return _blockMesh;
        }

        // ====================================================================
        //  SHARED MATERIALS (5 buckets across the purple→green gradient)
        // ====================================================================

        private static Material GetBlockMaterial(int bucket)
        {
            bucket = math.clamp(bucket, 0, GradientBucketCount - 1);
            if (_blockMaterials[bucket] != null) return _blockMaterials[bucket];

            float t = (bucket + 0.5f) / GradientBucketCount;
            Color baseColor       = Color.Lerp(InnerBase,     OuterBase,     t);
            Color emissionColor   = Color.Lerp(InnerEmission, OuterEmission, t);
            float emissionIntensity = math.lerp(InnerEmissionIntensity, OuterEmissionIntensity, t);

            var mat = new Material(LitShader) { name = $"CurseBlockMat_{bucket}" };

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", baseColor);
            mat.color = baseColor;

            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0.35f);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.65f);

            Color bakedEmission = emissionColor * emissionIntensity;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", bakedEmission);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            _blockBaseEmissions[bucket] = bakedEmission;
            _blockBaseEmissionsBuilt[bucket] = true;

            _blockMaterials[bucket] = mat;
            return mat;
        }

        private static Color GetBlockBaseEmission(int bucket)
        {
            bucket = math.clamp(bucket, 0, GradientBucketCount - 1);
            if (!_blockBaseEmissionsBuilt[bucket]) GetBlockMaterial(bucket);
            return _blockBaseEmissions[bucket];
        }
    }
}
