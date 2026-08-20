// TwinSpansGenerator.cs
// EDITOR-ONLY: builds the "Twin Spans" 3v3 team map from scratch.
//   Waning Border > Maps > Generate "Twin Spans" (3v3)
//
// THE DESIGN — TWO SHORES, TWO CROSSINGS, FOUR WELLS ON THE CROSSINGS
//   A river runs the full width of the map. It is a real wall: the channel
//   is under water and the banks above the waterline are steeper than the
//   walk limit, so roughly 45 m of ground says no. Two stone bridges, at
//   x = -56 and x = +56, are the ONLY way to the far shore.
//
//   Three warbands hold each shore, in a line at z = ±116: flanks at
//   x = ±112, centre at x = 0. Each bridge therefore sits between two
//   teammates, so no crossing is one player's private problem.
//
//   Home economy is deliberately GENEROUS AND SAFE — 2200 iron, 1200
//   veilstone and a 1500 veilsteel node inside every base plateau, plus a
//   shared expansion between each pair of neighbours, all of it behind the
//   base line where nothing can reach it without crossing first. You are
//   not pushed out by hunger on this map.
//
//   What pulls you out is the wells. There are FOUR, one at each
//   bridgehead — (±78, ±40) — sitting ~15 m off the deck's landward corner.
//   A wild well throws BorderConstants.MainNodeSpreadRadius (22 m) of haze
//   around itself, so the curse washes over the approach to every bridge:
//   you cross through blight, or you deal with the well first. Well-
//   domination victory then reads directly off the map — claiming all four
//   means holding both bridgeheads on BOTH shores at once, which is exactly
//   "we have taken the river".
//
//   The wells sit on the OUTBOARD side of each bridge on purpose: the lane
//   between the two crossings stays clean, so an army can shift laterally
//   along its own shore without wading through curse ground.
//
// HOW THE RIVER AND THE BRIDGES ACTUALLY WORK
//   * The water rule. PassabilityGrid blocks any cell whose terrain sits at
//     or below the water line — but ONLY when a WaterPlane exists (a
//     hand-authored map without one gets waterLevel = float.MinValue and no
//     water rule at all). So this map ships a WaterPlane at y = 6.5; that
//     component IS the barrier, and the visible surface is a separate quad.
//     Delete the WaterPlane and the river becomes a walkable ditch.
//   * The bank. Above the waterline the bank still falls 10 m over 10 m of
//     run — a sampled gradient around 1.4, comfortably past the 1.0 walk
//     limit — so there is no walkable shelf sneaking along the water's edge.
//     Blocked band and water band overlap, so the barrier is continuous.
//   * The crossings. BridgeSurface treats every child MeshFilter as an
//     oriented box and reports the highest deck under a point.
//     PassabilityGrid forces cells the deck covers PASSABLE (deck-only),
//     and the movement integrator only admits a unit onto a deck within
//     BridgeSurface.MountStepLimit (1.25 m) of its feet. Two consequences
//     this map is built around:
//       - The deck sits 0.8 m over the plain, not 3 m, so the step onto it
//         always fits inside that limit.
//       - The ground at both landward ends is flattened by an APRON pad, so
//         the cosmetic ground roll (±2.4 m) can never push the step out of
//         reach and make a bridge silently unusable.
//     The kerbs are 0.4 m for the same reason: if a nav cell centre lands on
//     one, the unit rises 0.4 m and nobody notices — a proper parapet would
//     be a metre-high ledge for units to climb.
//
// Idempotent: re-running overwrites the scene, terrain and layers in place.
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime
// asmdef with no separate editor assembly — the Editor/ folder name alone
// does not exclude it from player builds.

#if UNITY_EDITOR
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class TwinSpansGenerator
    {
        // ── Identity ────────────────────────────────────────────────────
        private const string MapName = "Twin Spans";
        private const string SceneName = "TwinSpans";
        private const string MapFolder = "Assets/GameData/Scenes/Maps/Twin Spans";
        private const string Tag = "TwinSpans";

        // ── Footprint ───────────────────────────────────────────────────
        // 352 m for six players — 1.4x Sundered Crown's span for 1.5x the
        // warbands, so ground per player is roughly unchanged while the map
        // gains the depth a river and two rear economies need.
        private const int MapSize = 352;
        private const int HeightmapRes = 513;   // must be 2^n + 1
        private const int AlphamapRes = 352;
        private const int DetailRes = 176;
        private const float MaxHeight = 120f;

        // ── Height budget (world metres) ────────────────────────────────
        private const float PlainY = 12f;
        private const float RollAmplitude = 1.5f;

        /// <summary>How far the channel floor sits below the plain.</summary>
        private const float RiverDepth = 10f;
        /// <summary>Half-width of the flat channel floor.</summary>
        private const float ChannelHalf = 15f;
        /// <summary>|z| at which the bank has fully climbed back to the plain.
        /// The 10 m run against a 10 m fall is what makes the bank steeper
        /// than the walk limit — widen it and the river stops blocking.</summary>
        private const float BankTop = 25f;
        /// <summary>Water line. Must sit between the channel floor and the
        /// plain, and this is the value the scene's WaterPlane carries — the
        /// two are the same number twice and must stay that way.</summary>
        private const float WaterY = 6.5f;

        private const float PlateauRadius = 46f;
        private const float PlateauFlat = 34f;
        private const float PlateauRise = 1.5f;

        private const float CragRadius = 20f;
        private const float CragFlat = 7f;
        private const float CragRise = 24f;
        private const float NoWalkFrom = 0.22f;
        private const float NoWalkFull = 0.42f;

        // ── Layout ──────────────────────────────────────────────────────
        private const float BridgeX = 56f;      // crossings at x = ±56
        private const float DeckHalfWidth = 8f; // 16 m of road
        private const float DeckReachZ = 34f;   // deck runs z ∈ [-34, 34]
        private const float DeckTopY = PlainY + 0.8f;
        private const float DeckThickness = 0.7f;

        /// <summary>Flat pad under each bridgehead. Centred past the bank so
        /// the deck's whole landward overlap stands on dead-level ground —
        /// this is what guarantees the 0.8 m mount step.</summary>
        private const float ApronZ = 32f;
        private const float ApronFlat = 14f;
        private const float ApronRadius = 26f;

        private const float BaseZ = 116f;       // both base lines
        private const float FlankX = 112f;      // flank bases
        private const float WellX = 78f;        // wells outboard of each bridge
        private const float WellZ = 40f;

        /// <summary>Outer-flank crags. They pinch the wide walk around the
        /// map edge without touching any base, well or crossing.</summary>
        private static readonly Vector2[] Crags =
        {
            new Vector2(-158f, -62f), new Vector2(158f, -62f),
            new Vector2(-158f, 62f), new Vector2(158f, 62f),
        };

        /// <summary>South shore, west to east, then north shore mirrored
        /// straight across. Slots 1-3 and 4-6 in the lobby's default order,
        /// so a stock 6-slot lobby lines up as three against three.</summary>
        private static readonly (Faction faction, float x, float z)[] Starts =
        {
            (Faction.Blue, -FlankX, -BaseZ),
            (Faction.Red, 0f, -BaseZ),
            (Faction.Green, FlankX, -BaseZ),
            (Faction.Yellow, -FlankX, BaseZ),
            (Faction.Purple, 0f, BaseZ),
            (Faction.Orange, FlankX, BaseZ),
        };

        private const int RngSeed = 0x5A9D;
        private const int TreeCount = 1200;

        [MenuItem("Waning Border/Maps/Generate \"Twin Spans\" (3v3)")]
        public static void Generate()
        {
            if (!EditorUtility.DisplayDialog(
                    "Generate Twin Spans",
                    $"This creates (or overwrites) the map at:\n{MapFolder}\n\n" +
                    "The open scene will be replaced. Save anything you care about first.",
                    "Generate", "Cancel"))
                return;

            MapAssetFolders.Ensure(MapFolder);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            try
            {
                EditorUtility.DisplayProgressBar(MapName, "Cutting the river…", 0.1f);
                var terrain = MapGenKit.BuildTerrain(new MapGenKit.TerrainSpec
                {
                    MapFolder = MapFolder,
                    SceneName = SceneName,
                    Size = MapSize,
                    HeightmapRes = HeightmapRes,
                    AlphamapRes = AlphamapRes,
                    DetailRes = DetailRes,
                    MaxHeight = MaxHeight,
                    Height = HeightAt,
                });

                EditorUtility.DisplayProgressBar(MapName, "Painting ground…", 0.35f);
                MapGenKit.PaintGround(terrain.terrainData, new MapGenKit.PaintSpec
                {
                    MapFolder = MapFolder,
                    Size = MapSize,
                    NoWalk = NoWalkAt,
                    Dirt = BareGroundAt,
                    // The plain tops out near 14.4 m; rock starts above that
                    // so the crags wear scree and the shore keeps its grass.
                    // The steepness band still paints the river gorge rocky.
                    RockHeightFrom = 18f,
                    RockHeightTo = 30f,
                });

                EditorUtility.DisplayProgressBar(MapName, "Scattering flora…", 0.55f);
                try
                {
                    MapGenKit.ScatterFlora(terrain, new MapGenKit.FloraSpec
                    {
                        MapFolder = MapFolder,
                        Size = MapSize,
                        Seed = RngSeed,
                        TreeCount = TreeCount,
                        CanPlant = CanPlant,
                        RockAboveHeight = 20f,
                    });
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[{Tag}] Flora pass failed — map continues without " +
                                     $"trees/detail. {e.GetType().Name}: {e.Message}");
                }

                EditorUtility.DisplayProgressBar(MapName, "Raising the spans…", 0.7f);
                BuildWater();
                BuildBridge(-BridgeX, "Bridge — West Span");
                BuildBridge(BridgeX, "Bridge — East Span");

                EditorUtility.DisplayProgressBar(MapName, "Placing markers…", 0.8f);
                PlaceMarkers();
                MapGenKit.BuildLighting();

                EditorUtility.DisplayProgressBar(MapName, "Saving…", 0.9f);
                string scenePath = $"{MapFolder}/{SceneName}.unity";
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                    throw new System.IO.IOException($"SaveScene refused to write {scenePath}");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayProgressBar(MapName, "Registering map…", 0.93f);
                bool registered = MapGenKit.RegisterInBuildSettings(scenePath);

                EditorUtility.DisplayProgressBar(MapName, "Baking lobby data…", 0.96f);
                MapGenKit.BakeLobbyAssets(Tag);
                WriteMapInfoBlurb();

                MapGenKit.ReportLobbyReadiness(Tag, MapName, MapFolder, scenePath, registered);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[{Tag}] Generation FAILED: {e}");
                EditorUtility.DisplayDialog("Generate Twin Spans",
                    $"Generation failed:\n\n{e.GetType().Name}: {e.Message}\n\n" +
                    "See the Console for the full stack trace.", "OK");
                throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ════════════════════════════════════════════════════════════════
        // TERRAIN
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Order matters here. Roll, then plateaus, then the bridgehead
        /// aprons — all of which shape the PLAIN — and only then does the
        /// river subtract its channel, so the cut passes cleanly through
        /// whatever is above it. Crags are ADDED last.
        ///
        /// Nothing max()-es against an absolute elevation: max(y, PlainY +
        /// feature) collapses to max(y, PlainY) wherever the feature is zero,
        /// which clamps away every trough of the cosmetic roll and leaves a
        /// step at the feature's rim. Blend or add; never clamp.
        /// </summary>
        private static float HeightAt(float wx, float wz)
        {
            float y = PlainY
                + Mathf.Sin(wx * 0.019f) * Mathf.Cos(wz * 0.015f) * RollAmplitude
                + Mathf.Sin((wx + wz) * 0.008f) * (RollAmplitude * 0.6f);

            // Build plateaus.
            for (int i = 0; i < Starts.Length; i++)
            {
                float d = MapGenKit.Dist(wx, wz, Starts[i].x, Starts[i].z);
                if (d >= PlateauRadius) continue;
                float t = MapGenKit.SmoothStep(Mathf.InverseLerp(PlateauRadius, PlateauFlat, d));
                y = Mathf.Lerp(y, PlainY + PlateauRise, t);
            }

            // Bridgehead aprons — dead level, so the deck's mount step is
            // exactly DeckTopY - PlainY wherever a unit steps on.
            float apron = ApronWeightAt(wx, wz);
            if (apron > 0f) y = Mathf.Lerp(y, PlainY, apron);

            // The river. A subtracted depression rather than a min() against
            // a profile: min() leaves a value discontinuity at the bank top
            // wherever the roll sits above the plain, and that reads as a
            // metre-high step running the width of the map.
            y -= RiverDepthAt(wz);

            // Crags. All four stand well clear of the river, so adding after
            // the cut cannot accidentally bridge the channel with rock.
            y += CragHeightAt(wx, wz);
            return y;
        }

        /// <summary>Metres of ground removed by the river at this z.</summary>
        private static float RiverDepthAt(float wz)
        {
            float a = Mathf.Abs(wz);
            if (a >= BankTop) return 0f;
            if (a <= ChannelHalf) return RiverDepth;
            return RiverDepth * (1f - MapGenKit.SmoothStep(
                Mathf.InverseLerp(ChannelHalf, BankTop, a)));
        }

        private static float ApronWeightAt(float wx, float wz)
        {
            float best = 0f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float w = MapGenKit.Dome(
                        MapGenKit.Dist(wx, wz, sx * BridgeX, sz * ApronZ),
                        ApronFlat, ApronRadius, 1f);
                    if (w > best) best = w;
                }
            }
            return best;
        }

        private static float CragHeightAt(float wx, float wz)
        {
            float best = 0f;
            for (int i = 0; i < Crags.Length; i++)
            {
                float h = MapGenKit.Dome(
                    MapGenKit.Dist(wx, wz, Crags[i].x, Crags[i].y),
                    CragFlat, CragRadius, CragRise);
                if (h > best) best = h;
            }
            return best;
        }

        /// <summary>
        /// The only painted wall on this map is the crags — the river blocks
        /// through water and slope, which are terrain facts rather than
        /// paint. Deriving the mask from the crag function keeps the rock's
        /// look and its blocking from ever drifting apart.
        /// </summary>
        private static float NoWalkAt(float wx, float wz)
        {
            float frac = CragHeightAt(wx, wz) / CragRise;
            return MapGenKit.SmoothStep(Mathf.InverseLerp(NoWalkFrom, NoWalkFull, frac));
        }

        /// <summary>
        /// Bare ground: the river channel and its shore, plus a road down the
        /// axis of each crossing. The road is not decoration — it is the one
        /// thing that tells a player at a glance where the two ways across
        /// are, on a map whose whole shape is "there are two ways across".
        /// </summary>
        private static float BareGroundAt(float wx, float wz)
        {
            float river = Mathf.InverseLerp(BankTop + 5f, ChannelHalf, Mathf.Abs(wz));

            float road = 0f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                float lateral = Mathf.InverseLerp(11f, 6f, Mathf.Abs(wx - sx * BridgeX));
                float along = Mathf.InverseLerp(74f, 62f, Mathf.Abs(wz));
                road = Mathf.Max(road, lateral * along);
            }

            return Mathf.Max(river, road);
        }

        /// <summary>
        /// Keep flora out of the water and off the ground the map is played
        /// on: build plateaus, both bridgeheads, and the wells (a well you
        /// cannot see over is a worse objective, however good the screenshot).
        /// The shore ledge above the waterline stays wooded — that is where
        /// the river reads as a place rather than a gap.
        /// </summary>
        private static bool CanPlant(float wx, float wz)
        {
            if (Mathf.Abs(wz) < 21f) return false;               // in the water

            for (int i = 0; i < Starts.Length; i++)
                if (MapGenKit.Dist(wx, wz, Starts[i].x, Starts[i].z) < PlateauRadius + 12f)
                    return false;

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    if (MapGenKit.Dist(wx, wz, sx * BridgeX, sz * ApronZ) < ApronRadius + 6f)
                        return false;
                    if (MapGenKit.Dist(wx, wz, sx * WellX, sz * WellZ) < 26f)
                        return false;
                }
            }
            return true;
        }

        // ════════════════════════════════════════════════════════════════
        // WATER
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// The WaterPlane component is the barrier; the quad is the picture.
        ///
        /// They are separate objects on purpose. WaterPlane only builds its
        /// own mesh inside Initialize(), which the procedural generator calls
        /// and a hand-authored map never does — so the component here stays
        /// inert except as the water-level oracle PassabilityGrid reads, and
        /// the visible surface is an ordinary quad we control.
        ///
        /// The quad covers only the river band, not the whole map: a
        /// map-wide transparent plane would be hidden by terrain everywhere
        /// else and pay for the privilege in overdraw.
        /// </summary>
        private static void BuildWater()
        {
            var root = new GameObject("Water");
            var plane = root.AddComponent<WaterPlane>();
            plane.waterLevel = WaterY;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "WaterSurface";
            quad.transform.SetParent(root.transform, false);
            // Unity's Quad lies in XY facing -Z; +90° about X lays it flat
            // with its normal pointing up.
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.position = new Vector3(0f, WaterY, 0f);
            quad.transform.localScale = new Vector3(MapSize, (BankTop + 6f) * 2f, 1f);
            quad.isStatic = true;

            // No collider: ground clicks must fall through to the terrain, and
            // a unit ordered to the far bank should not be able to path-pick a
            // point on the water surface.
            var col = quad.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            var mr = quad.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = MakeWaterMaterial();
        }

        private static Material MakeWaterMaterial()
        {
            string path = $"{MapFolder}/RiverWater.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                // URP Lit rather than the project's Custom/AnimatedWater: that
                // shader is a built-in-pipeline CGPROGRAM and would light
                // wrong under URP. A material ASSET also keeps the shader out
                // of the stripper's reach, which a runtime Shader.Find does
                // not.
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", new Color(0.13f, 0.32f, 0.42f, 0.78f));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.13f, 0.32f, 0.42f, 0.78f));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.92f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.1f);

            // URP's transparent setup is a set of properties AND keywords AND
            // a render queue; setting only the surface property leaves the
            // material opaque at runtime.
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);   // 0 opaque, 1 transparent
                mat.SetFloat("_Blend", 0f);     // alpha
                mat.SetFloat("_ZWrite", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ════════════════════════════════════════════════════════════════
        // BRIDGES
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// One crossing, built from axis-aligned boxes under a BridgeSurface
        /// root. BridgeSurface reads every child MeshFilter as an oriented box
        /// via mesh.bounds — exact for scaled cube primitives, which is why
        /// bridges here are cubes and not sculpted meshes.
        ///
        /// Piece heights are the load-bearing part of this method:
        ///   deck top  = DeckTopY   (0.8 m over the plain — inside the 1.25 m
        ///               mount step, and the apron guarantees the plain is
        ///               really at PlainY under both ends)
        ///   kerb top  = deck + 0.4 (a cell centre landing on a kerb lifts a
        ///               unit 0.4 m, which nobody sees; a real parapet would
        ///               be a ledge to climb)
        ///   piers/abutments stay BELOW the deck, so the highest surface under
        ///               any point on the bridge is always the road.
        /// </summary>
        private static void BuildBridge(float bx, string name)
        {
            var root = new GameObject(name);
            root.transform.position = new Vector3(bx, 0f, 0f);
            root.AddComponent<BridgeSurface>();
            root.isStatic = true;

            var stone = MakeBridgeMaterial();

            // Deck: spans both banks with 9 m of overlap onto flat apron at
            // each end, so the walk-on and walk-off cells are ordinary ground.
            Box(root, stone, "Deck",
                new Vector3(0f, DeckTopY - DeckThickness * 0.5f, 0f),
                new Vector3(DeckHalfWidth * 2f, DeckThickness, DeckReachZ * 2f));

            // Kerbs.
            for (int s = -1; s <= 1; s += 2)
                Box(root, stone, s < 0 ? "Kerb West" : "Kerb East",
                    new Vector3(s * (DeckHalfWidth - 0.3f), DeckTopY + 0.2f, 0f),
                    new Vector3(0.6f, 0.4f, DeckReachZ * 2f));

            // Piers, standing on the channel floor.
            float floorY = PlainY - RiverDepth;
            float pierH = (DeckTopY - DeckThickness) - floorY;
            foreach (float pz in new[] { -11f, 11f })
            {
                foreach (float px in new[] { -5f, 5f })
                    Box(root, stone, $"Pier {px:0}/{pz:0}",
                        new Vector3(px, floorY + pierH * 0.5f, pz),
                        new Vector3(3.4f, pierH, 3.4f));
            }

            // Abutments where the deck meets the bank — they hide the gap
            // between a flat deck and a sloping bank.
            float bankY = PlainY - RiverDepthAt(22f);
            float abutH = (DeckTopY - DeckThickness) - bankY;
            for (int s = -1; s <= 1; s += 2)
                Box(root, stone, s < 0 ? "Abutment South" : "Abutment North",
                    new Vector3(0f, bankY + abutH * 0.5f, s * 22f),
                    new Vector3(DeckHalfWidth * 2f, abutH, 6f));
        }

        /// <summary>Child box in the bridge's LOCAL space (root sits at the
        /// crossing's x, so pieces are authored around zero).</summary>
        private static void Box(GameObject parent, Material mat, string name,
                                Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.isStatic = true;
            // Colliders stay: the shipped 4FFA/8FFA bridge prefabs carry them,
            // and ground picking snaps its hit's Y through TerrainUtility,
            // which already consults BridgeSurface — so a click on the deck
            // resolves to a point on the deck.
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Material MakeBridgeMaterial()
        {
            string path = $"{MapFolder}/BridgeStone.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            const string dir = MapGenKit.SharedRoot + "/BricksSubstance006_COMPILED_graph_0";
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{dir}/BricksSubstance006_COMPILED_basecolor.tga");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{dir}/BricksSubstance006_COMPILED_normal.tga");

            if (albedo != null) mat.SetTexture("_BaseMap", albedo);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            mat.SetColor("_BaseColor", new Color(0.74f, 0.72f, 0.68f, 1f));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            // Cube UVs are 0..1 per face, so a scaled cube stretches the
            // texture; tiling it back roughly restores stone-sized masonry.
            mat.SetTextureScale("_BaseMap", new Vector2(4f, 4f));

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ════════════════════════════════════════════════════════════════
        // MARKERS
        // ════════════════════════════════════════════════════════════════

        private static void PlaceMarkers()
        {
            var root = new GameObject("Markers");

            // ── Player starts ───────────────────────────────────────────
            for (int i = 0; i < Starts.Length; i++)
            {
                var s = Starts[i];
                MapGenKit.Marker<PlayerStartMarker>(root, $"Start — {s.faction}",
                    new Vector3(s.x, 0f, s.z)).Faction = s.faction;
            }

            // ── The objectives: one well at each of the four bridgeheads ─
            // AuthoredPosition is what hands the well list to this map. The
            // default four CORNER wells would put every objective as far from
            // the river as the map allows, which on a map about a river is
            // exactly backwards.
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    string span = sx < 0 ? "West" : "East";
                    string shore = sz < 0 ? "South" : "North";
                    MapGenKit.Marker<BorderNodeMarker>(root,
                        $"Well — {span} Span, {shore} bank",
                        new Vector3(sx * WellX, 0f, sz * WellZ))
                        .AuthoredPosition = true;
                }
            }

            // ── Home economy: generous, and behind the line ──────────────
            // 2200 iron, 1200 veilstone and a veilsteel node per base, all
            // inside the plateau. Nothing here is contestable without
            // crossing first, which is the point: on this map you are pulled
            // out by the wells, not pushed out by hunger.
            for (int i = 0; i < Starts.Length; i++)
            {
                var st = Starts[i];
                float bx = st.x, bz = st.z;
                float back = Mathf.Sign(bz);   // away from the river
                string who = st.faction.ToString();

                // ±30 rather than ±34: the shared expansions sit at x = ±56 on
                // this same line, and 34 would leave only 22 m between two
                // iron patches whose spreads already claim 19 of it.
                var ironA = MapGenKit.Marker<IronPatchMarker>(root, $"Iron A — {who}",
                    new Vector3(bx - 30f, 0f, bz + back * 18f));
                ironA.DepositCount = 22; ironA.Spread = 9f;
                var ironB = MapGenKit.Marker<IronPatchMarker>(root, $"Iron B — {who}",
                    new Vector3(bx + 30f, 0f, bz + back * 18f));
                ironB.DepositCount = 22; ironB.Spread = 9f;

                var vsA = MapGenKit.Marker<VeilstoneOutcroppingMarker>(root,
                    $"Veilstone A — {who}", new Vector3(bx - 28f, 0f, bz - back * 26f));
                vsA.NodeCount = 20; vsA.VeilstonePerNode = 30; vsA.Spread = 8f;
                var vsB = MapGenKit.Marker<VeilstoneOutcroppingMarker>(root,
                    $"Veilstone B — {who}", new Vector3(bx + 28f, 0f, bz - back * 26f));
                vsB.NodeCount = 20; vsB.VeilstonePerNode = 30; vsB.Spread = 8f;

                MapGenKit.Marker<VeilsteelDepositMarker>(root, $"Veilsteel — {who}",
                    new Vector3(bx, 0f, bz + back * 44f)).Amount = 1500;

                // Age 0 curse content between the base and the river — the
                // first blight a team meets is on its own shore, long before
                // anyone reaches a well.
                MapGenKit.Marker<BlightPocketMarker>(root, $"Blight — {who}",
                    new Vector3(bx, 0f, bz - back * 54f)).Radius = 12f;
            }

            // ── Shared shore expansions ─────────────────────────────────
            // One pair between each neighbouring pair of teammates, on the
            // base line. Safe from the far shore, and deliberately equidistant
            // from both neighbours so a team has to talk about who takes it.
            for (int sz = -1; sz <= 1; sz += 2)
            {
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    string shore = sz < 0 ? "South" : "North";
                    string side = sx < 0 ? "West" : "East";

                    var iron = MapGenKit.Marker<IronPatchMarker>(root,
                        $"Iron Expansion — {shore} {side}",
                        new Vector3(sx * 56f, 0f, sz * 134f));
                    iron.DepositCount = 24; iron.Spread = 10f;

                    var vs = MapGenKit.Marker<VeilstoneOutcroppingMarker>(root,
                        $"Veilstone Expansion — {shore} {side}",
                        new Vector3(sx * 56f, 0f, sz * 120f));
                    vs.NodeCount = 24; vs.VeilstonePerNode = 40; vs.Spread = 9f;
                }
            }
        }

        private static void WriteMapInfoBlurb()
        {
            var info = AssetDatabase.LoadAssetAtPath<MapInfo>(
                $"{MapFolder}/{MapName} MapInfo.asset");
            if (info == null) return;

            info.DisplayName = MapName;
            info.SizeTag = "LARGE / 3v3";

            // THE TEAM LAYOUT. A map called 3v3 has to SAY who is on whose
            // side, or the lobby opens it as a six-way free-for-all and the
            // three warbands sharing a shore spend the match fighting each
            // other instead of crossing the river. Read off the baked starts by
            // shore rather than by array position: MapInfoBaker's order comes
            // from MapMarkerRegistry and is not this file's Starts order.
            // South bank -> team 1, north bank -> team 2. docs/Design/Teams.md
            if (info.PlayerStarts != null && info.PlayerStarts.Length > 0)
            {
                var teams = new int[info.PlayerStarts.Length];
                for (int i = 0; i < teams.Length; i++)
                    teams[i] = info.PlayerStarts[i].y < 0.5f ? 1 : 2;
                info.PlayerStartTeams = teams;
            }

            info.Description =
                "Three warbands to a shore, split by a river nothing fords. Two " +
                "stone spans are the only way over, and a well stands at each of " +
                "the four bridgeheads — cross through the blight, or break the " +
                "well first. Home ground is rich and safe; the river is not.";
            EditorUtility.SetDirty(info);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
