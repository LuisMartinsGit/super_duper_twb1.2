// SunderedCrownGenerator.cs
// EDITOR-ONLY: builds the "Sundered Crown" 4-player map from scratch.
//   Waning Border > Maps > Generate "Sundered Crown" (4P)
//
// THE DESIGN
//   Four warbands start in the four corners. Four mountain ridges run out
//   along the cardinal axes from the middle to the map edge, so each corner
//   is walled off from BOTH its neighbours. The ridges stop short of the
//   centre, leaving one open bowl in the middle of the map — the only ground
//   that connects all four players. In that bowl stands a single Veilstone
//   well on a low, fully traversable hill.
//
//   So: you cannot expand sideways, you cannot rush a neighbour's base
//   without going through the middle, and the middle is also the only verb
//   node on the map. Every path to victory runs through one piece of ground.
//
// HOW IMPASSABILITY WORKS HERE — THE "NoWalk" PAINT
//   The mountains are blocked by a PAINTED TERRAIN LAYER, not by their
//   shape. PassabilityGrid scans the terrain's layer palette for an asset
//   whose name contains "nowalk" (case-insensitive) and treats any cell
//   whose painted weight of that layer is >= 0.5 as impassable REGARDLESS
//   OF SLOPE. The mask comes from asset data, identical on every client, so
//   it is lockstep-safe.
//
//   This is why the ridges can be short and broad and still be walls. The
//   alternative — leaning on the slope rule (impassable above gradient 1.0
//   over a 3 m span) — chains the mountains' looks to their function: drop
//   the height and you must narrow them in proportion or they silently stop
//   blocking. Painting the intent instead decouples the two, so ridge height
//   and width are now purely aesthetic knobs.
//
//   Consequence to respect: NoWalk blocks even perfectly flat ground, so it
//   must never be painted anywhere players need to walk. The paint is
//   derived from the same RidgeFraction that sculpts the ridges, which is
//   zero inside RidgeInner — the centre bowl can never be caught by it.
//
//   The centre is deliberately FLAT and unpainted: a 6 m plinth spread over
//   a 48 m radius (peak gradient 0.19, five times under the slope limit).
//   It has to stay trivially walkable — it is the only ground connecting the
//   four players and the only way to the well.
//
//   There is NO water rule on this map. PassabilityGrid only applies its 4 m
//   water line when a WaterPlane exists or the terrain is procedural; a
//   hand-authored terrain with neither gets waterLevel = float.MinValue.
//   All ground here still sits at y >= 10 so the map stays correct even if a
//   WaterPlane is added later.
//
// Idempotent: re-running overwrites the scene, terrain and layers in place.
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime
// asmdef with no separate editor assembly — the Editor/ folder name alone
// does not exclude it from player builds (same reason MapInfoBaker does it).

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class SunderedCrownGenerator
    {
        // ── Identity ────────────────────────────────────────────────────
        private const string MapName   = "Sundered Crown";
        private const string SceneName = "SunderedCrown";
        private const string MapFolder = "Assets/GameData/Scenes/Maps/Sundered Crown";

        // ── Terrain geometry ────────────────────────────────────────────
        /// <summary>
        /// THE ONE SIZE KNOB. Every horizontal dimension on this map — extent,
        /// radii, marker offsets, resolutions, tree count — is expressed
        /// through this, so the layout stays proportional at any scale.
        /// Halving it halves the map and keeps the plan identical.
        ///
        /// Heights are deliberately NOT scaled: mountain and plinth heights
        /// were tuned by eye against the game camera, and shrinking them with
        /// the footprint would undo that. A smaller map therefore reads as
        /// slightly more mountainous, which is the intent.
        /// </summary>
        private const float MapScale = 0.5f;

        // 256 m square at MapScale 0.5. Four corner economies plus a
        // contested middle, with the diagonal short enough that a wave is a
        // commitment rather than a commute.
        private const int   TerrainSize   = (int)(512 * MapScale);
        private const int   TerrainHeight = 120;   // world units for normalized 1.0
        // Resolutions scale with the footprint so texel density per metre
        // stays constant — 1 heightmap sample/m, 1 alphamap texel/m, one
        // detail cell per 2 m — instead of getting finer as the map shrinks.
        private const int   HeightmapRes  = (int)(512 * MapScale) + 1;   // must be 2^n + 1
        private const int   AlphamapRes   = (int)(512 * MapScale);
        private const int   DetailRes     = (int)(256 * MapScale);
        private const int   DetailPerPatch = 16;

        // Height budget, in world metres, all measured off the base plain.
        private const float BaseHeight   = 10f;  // the plain every player builds on
        private const float RidgeHeight  = 25f;  // mountain peak above the plain
        private const float CrownHeight  = 6f;   // centre plinth — deliberately flat
        private const float RollAmplitude = 1.4f; // cosmetic undulation

        // Radii, in metres from map centre. All scaled so the plan is
        // identical at any MapScale.
        private const float CrownRadius   = 48f * MapScale;  // flat centre platform
        private const float RidgeInner    = 78f * MapScale;  // ridges start here
        // Free to be whatever LOOKS right: the NoWalk paint does the blocking,
        // so ridge width is no longer hostage to the slope budget.
        private const float RidgeHalfWidth = 35f * MapScale;
        private const float BaseRadius    = 150f * MapScale; // corner start, per axis
        private const float PlateauRadius = 52f * MapScale;  // flat build space
        /// <summary>Length of the ramp at a ridge's inner end, so it rises out
        /// of the bowl instead of being a cliff dropped on the plain.</summary>
        private const float RidgeRunIn    = 26f * MapScale;

        /// <summary>
        /// Ridge strength (0..1) at or above which the ground is painted
        /// NoWalk. Chosen so the blocked band is ~23 m of each 35 m flank —
        /// a 46 m thick wall — leaving the outer ~12 m as walkable scree so
        /// the mountains have feet rather than meeting the plain at a seam.
        /// </summary>
        private const float NoWalkFrom = 0.20f;
        private const float NoWalkFull = 0.34f;

        // ── Content density ─────────────────────────────────────────────
        // Scales with AREA (MapScale squared) so woodland density per hectare
        // is unchanged — a linear scale would leave the smaller map twice as
        // densely forested.
        private const int TreeCount  = (int)(2600 * MapScale * MapScale);

        /// <summary>Overall tree size multiplier — the one knob for "bigger /
        /// smaller trees". Applied to width and height together, so it does
        /// not disturb the squat proportion below.</summary>
        private const float TreeScale = 0.5f;

        /// <summary>Height as a fraction of width. Synty trees are modelled
        /// tall for close-up use and read as stretched at RTS camera
        /// distance; 0.5 gives a squat canopy that still covers ground.</summary>
        private const float TreeHeightRatio = 0.5f;

        /// <summary>Overall grass size multiplier — the one knob for "bigger /
        /// smaller grass", applied to every blade dimension.</summary>
        private const float GrassScale = 2.0f;
        // Ground cover. Deliberately thin: "not very dense" is both the look
        // asked for and what keeps waving grass (which cannot instance)
        // affordable across a 512 m map.
        private const int   DetailDensity = 4;     // instances per detail cell at full weight
        private const float GrassCutoff   = 0.50f; // noise below this grows nothing

        private const int RngSeed = 0x5CC0;

        private static readonly string SharedRoot = "Assets/GameData/Scenes/Maps/Shared";
        private static readonly string SyntyEnv =
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments";

        /// <summary>Corner starts, clockwise from south-west. Faction order
        /// matches the lobby's default slot order.</summary>
        private static readonly (Faction faction, float x, float z)[] Starts =
        {
            (Faction.Blue,   -BaseRadius, -BaseRadius),
            (Faction.Red,     BaseRadius, -BaseRadius),
            (Faction.Green,  -BaseRadius,  BaseRadius),
            (Faction.Yellow,  BaseRadius,  BaseRadius),
        };

        [MenuItem("Waning Border/Maps/Generate \"Sundered Crown\" (4P)")]
        public static void Generate()
        {
            if (!EditorUtility.DisplayDialog(
                    "Generate Sundered Crown",
                    $"This creates (or overwrites) the map at:\n{MapFolder}\n\n" +
                    "The open scene will be replaced. Save anything you care about first.",
                    "Generate", "Cancel"))
                return;

            // AssetDatabase-AWARE folder creation. Directory.CreateDirectory
            // alone puts the folder on disk but leaves it unknown to the
            // AssetDatabase, and AssetDatabase.CreateAsset into an unimported
            // folder fails — which is exactly how the first run of this tool
            // died: the map folder appeared, empty, and nothing else was
            // written.
            MapAssetFolders.Ensure(MapFolder);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            try
            {
                EditorUtility.DisplayProgressBar("Sundered Crown", "Sculpting terrain…", 0.1f);
                var terrain = BuildTerrain();

                EditorUtility.DisplayProgressBar("Sundered Crown", "Painting ground…", 0.4f);
                PaintGround(terrain.terrainData);

                // Flora is the one step that depends on third-party prefabs
                // validating against Unity's tree/detail prototype rules, so
                // it must not be able to take the whole map down with it. A
                // map with no bushes is a map; a map that failed to save is
                // nothing.
                EditorUtility.DisplayProgressBar("Sundered Crown", "Scattering flora…", 0.6f);
                try { ScatterFlora(terrain); }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[SunderedCrown] Flora pass failed — map continues without " +
                                     $"trees/detail. {e.GetType().Name}: {e.Message}");
                }

                EditorUtility.DisplayProgressBar("Sundered Crown", "Placing markers…", 0.8f);
                PlaceMarkers();
                BuildLighting();

                EditorUtility.DisplayProgressBar("Sundered Crown", "Saving…", 0.9f);
                string scenePath = $"{MapFolder}/{SceneName}.unity";
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                    throw new System.IO.IOException($"SaveScene refused to write {scenePath}");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // REGISTER FIRST, DECORATE AFTER. Being in the lobby depends
                // only on the scene existing in Build Settings; the thumbnail
                // and slot image are presentation. The previous ordering had
                // registration downstream of the bakes, so when MapInfoBaker
                // threw the map was fully built on disk and still invisible
                // to the lobby. Cosmetics must never gate availability.
                EditorUtility.DisplayProgressBar("Sundered Crown", "Registering map…", 0.93f);
                bool registered = RegisterInBuildSettings(scenePath);

                // Both bakes are non-fatal for the same reason: a map with no
                // thumbnail is playable, a map that failed to register is not.
                EditorUtility.DisplayProgressBar("Sundered Crown", "Baking lobby data…", 0.96f);
                try { MapInfoBaker.Bake(); }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[SunderedCrown] MapInfo bake failed — the map is still " +
                                     $"playable and in the lobby, but without a thumbnail or " +
                                     $"player-count. {e.GetType().Name}: {e.Message}");
                }
                try { MapLobbyImageBaker.Bake(); }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[SunderedCrown] Lobby slot image failed. " +
                                     $"{e.GetType().Name}: {e.Message}");
                }

                ReportLobbyReadiness(scenePath, registered);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SunderedCrown] Generation FAILED: {e}");
                EditorUtility.DisplayDialog("Generate Sundered Crown",
                    $"Generation failed:\n\n{e.GetType().Name}: {e.Message}\n\n" +
                    "See the Console for the full stack trace.", "OK");
                throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Put the scene in Build Settings, enabled. This — and only this —
        /// is what makes the map appear in the skirmish lobby: MapRegistry
        /// builds its list from the Build Settings scenes under MapsRoot.
        ///
        /// MapSceneSync normally does it via EditorApplication.delayCall on
        /// import, but delayCall has not fired yet while we are still inside
        /// the generate, so we do it inline and let MapSceneSync no-op later.
        /// </summary>
        private static bool RegisterInBuildSettings(string scenePath)
        {
            scenePath = scenePath.Replace('\\', '/');
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (int i = 0; i < scenes.Count; i++)
            {
                if (!string.Equals(scenes[i].path.Replace('\\', '/'), scenePath,
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                if (scenes[i].enabled) return true;
                scenes[i] = new EditorBuildSettingsScene(scenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
                return true;
            }

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[SunderedCrown] Registered {scenePath} in Build Settings — " +
                      "the skirmish lobby reads its map list from there.");
            return true;
        }

        /// <summary>
        /// Report exactly what a player will see, in plain terms. The lobby
        /// reads MapRegistry, which reads Build Settings, which MapSceneSync
        /// populates from disk — three hops where a silent miss just means
        /// the map never appears.
        /// </summary>
        private static void ReportLobbyReadiness(string scenePath, bool inBuildSettings)
        {
            var info = AssetDatabase.LoadAssetAtPath<MapInfo>($"{MapFolder}/{MapName} MapInfo.asset");
            string thumb = info != null && info.Thumbnail != null ? "yes" : "NO";
            string lobbyPng = File.Exists($"{MapFolder}/{MapName} Lobby.png") ? "yes" : "NO";

            Debug.Log($"[SunderedCrown] LOBBY READY: \"{MapName}\"\n" +
                      $"  scene            {scenePath}\n" +
                      $"  in Build Settings {inBuildSettings}\n" +
                      $"  MapInfo asset     {(info != null ? "yes" : "NO")}\n" +
                      $"  player count      {(info != null ? info.PlayerCount.ToString() : "?")}\n" +
                      $"  thumbnail         {thumb}\n" +
                      $"  lobby slot image  {lobbyPng}\n" +
                      "The skirmish dropdown reads Build Settings at runtime, so the entry " +
                      "appears the next time the menu scene loads.");
        }

        // ════════════════════════════════════════════════════════════════
        // TERRAIN
        // ════════════════════════════════════════════════════════════════

        private static Terrain BuildTerrain()
        {
            // Unity silently ROUNDS heightmapResolution to the nearest 2^n+1.
            // If MapScale produces anything else, the terrain would end up a
            // different size than the array SculptHeights builds and SetHeights
            // throws — or worse, quietly writes a mismatched heightmap. Valid
            // MapScale values are powers of two: 0.25 -> 129, 0.5 -> 257,
            // 1.0 -> 513.
            int n = HeightmapRes - 1;
            if (n < 32 || (n & (n - 1)) != 0)
                throw new System.ArgumentException(
                    $"MapScale {MapScale} gives heightmapResolution {HeightmapRes}, which is not " +
                    "2^n+1. Use a power-of-two MapScale (0.25, 0.5, 1.0).");

            var data = new TerrainData
            {
                heightmapResolution = HeightmapRes,
                alphamapResolution  = AlphamapRes,
                baseMapResolution   = 1024,
            };
            data.SetDetailResolution(DetailRes, DetailPerPatch);
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);

            // THE WIND. These four are the entire sway mechanism for terrain
            // detail — a WindZone does NOT drive grass (it only affects trees
            // with wind-aware shaders), so there is deliberately no WindZone
            // in this scene. URP 17 ships Shaders/Terrain/WavingGrass.shader,
            // which DetailRenderMode.Grass routes through, so these do apply.
            //
            // Set ABOVE Unity's 0.5 defaults on purpose. The first pass used
            // 0.28/0.20/0.32 — a "light breeze" that is invisible on 0.4 m
            // grass viewed from a camera 15-80 units up. Motion has to be
            // exaggerated to read at all at RTS altitude; what looks like a
            // gale from 2 m away is a gentle ripple from 60 m.
            data.wavingGrassStrength = 0.70f;   // how far a blade bends
            data.wavingGrassAmount   = 0.60f;   // how much of the blade bends
            data.wavingGrassSpeed    = 0.55f;   // gust cadence
            data.wavingGrassTint     = new Color(0.86f, 0.90f, 0.80f, 1f);

            data.SetHeights(0, 0, SculptHeights());

            string dataPath = $"{MapFolder}/{SceneName} TerrainData.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            // Centre the terrain on the world origin so map centre == (0,0).
            // Marker maths, the AI's hall-anchored placement rings and
            // TerrainUtility.GetPlayableBounds all assume an origin-centred
            // playable area.
            go.transform.position = new Vector3(-TerrainSize / 2f, 0f, -TerrainSize / 2f);
            go.isStatic = true;

            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 400f;
            terrain.treeDistance = 320f;
            terrain.treeBillboardDistance = 120f;
            // Detail draw distance is measured from the CAMERA, and this game's
            // camera zooms out to 80 units (CameraController.maxZoom) looking
            // down at an angle — so ground across the screen sits well past
            // 140 m and the grass simply was not drawn. 250 is Unity's ceiling.
            terrain.detailObjectDistance = 250f;
            terrain.detailObjectDensity = 0.80f;
            terrain.drawInstanced = true;
            return terrain;
        }

        /// <summary>
        /// The whole map in one height function. Composed as
        /// max(plain+roll, crown, ridges) so features never cancel each other
        /// out — a ridge crossing rolling ground stays a full-height ridge.
        /// </summary>
        private static float[,] SculptHeights()
        {
            var h = new float[HeightmapRes, HeightmapRes];
            float half = TerrainSize / 2f;
            float step = TerrainSize / (float)(HeightmapRes - 1);

            for (int zi = 0; zi < HeightmapRes; zi++)
            {
                float wz = -half + zi * step;
                for (int xi = 0; xi < HeightmapRes; xi++)
                {
                    float wx = -half + xi * step;

                    // 1. The plain, with gentle cosmetic undulation. Two
                    //    long wavelengths keep the peak gradient near 0.15 —
                    //    two orders under the 1.0 walk limit, so decoration
                    //    can never accidentally wall a lane off.
                    float roll =
                        Mathf.Sin(wx * 0.021f) * Mathf.Cos(wz * 0.017f) * RollAmplitude +
                        Mathf.Sin((wx + wz) * 0.009f) * (RollAmplitude * 0.6f);
                    float y = BaseHeight + roll;

                    // 2. Corner plateaus — flatten the build space so a Hall
                    //    and its hut belt never fight the terrain.
                    foreach (var s in Starts)
                    {
                        float d = Mathf.Sqrt((wx - s.x) * (wx - s.x) + (wz - s.z) * (wz - s.z));
                        if (d >= PlateauRadius) continue;
                        // Flat core, feathered rim so the plateau blends out.
                        float t = Mathf.InverseLerp(PlateauRadius, PlateauRadius * 0.72f, d);
                        y = Mathf.Lerp(y, BaseHeight + 1.5f, SmoothStep(t));
                    }

                    // 3. The Crown — a FLAT centre platform, not a hill. Peak
                    //    gradient of the smoothstep dome is 1.5*H/R =
                    //    1.5*6/48 = 0.19, five times under MaxWalkableSlope.
                    //    This ground MUST stay trivially walkable: it is the
                    //    only route to the well and the only ground that
                    //    joins the four players. The 6 m is a plinth to seat
                    //    the well on and read as "the middle", not a climb.
                    float dc = Mathf.Sqrt(wx * wx + wz * wz);
                    if (dc < CrownRadius)
                    {
                        float t = SmoothStep(Mathf.InverseLerp(CrownRadius, 0f, dc));
                        y = Mathf.Max(y, BaseHeight + CrownHeight * t);
                    }

                    // 4. The four ridges. Each runs along a cardinal axis from
                    //    RidgeInner out past the map edge. Shape here is
                    //    purely visual — the wall is the NoWalk paint the
                    //    ground pass derives from this same function.
                    y = Mathf.Max(y, RidgeAt(wx, wz));
                    y = Mathf.Max(y, RidgeAt(wz, wx));   // same wall, rotated 90 degrees

                    h[zi, xi] = Mathf.Clamp01(y / TerrainHeight);
                }
            }
            return h;
        }

        /// <summary>
        /// Height contributed by the ridge pair running along the ALONG axis
        /// (both directions) and walled across the ACROSS axis. Called twice
        /// with the arguments swapped to produce all four arms.
        /// </summary>
        private static float RidgeAt(float along, float across)
        {
            float dAlong = Mathf.Abs(along);
            if (dAlong < RidgeInner) return 0f;          // centre bowl stays open

            float dAcross = Mathf.Abs(across);
            if (dAcross >= RidgeHalfWidth) return 0f;

            // Cross-section: full height at the spine, smoothstep down to the foot.
            float cross = SmoothStep(Mathf.InverseLerp(RidgeHalfWidth, 0f, dAcross));

            // Longitudinal: rise out of the bowl over RidgeRunIn so the inner
            // end is a real slope rather than a cliff dropped on the plain.
            float lon = SmoothStep(Mathf.InverseLerp(RidgeInner, RidgeInner + RidgeRunIn, dAlong));

            return BaseHeight + RidgeHeight * cross * lon;
        }

        /// <summary>
        /// How much of the full ridge height this point sits under, 0..1.
        /// The paint pass derives the NoWalk mask from this so the blocked
        /// ground is defined by the SAME function that sculpted the ridges —
        /// they cannot drift apart the way a hand-painted mask would.
        /// </summary>
        private static float RidgeFraction(float wx, float wz)
        {
            float a = RidgeAt(wx, wz);
            float b = RidgeAt(wz, wx);
            float peak = Mathf.Max(a, b);
            if (peak <= 0f) return 0f;
            return Mathf.Clamp01((peak - BaseHeight) / RidgeHeight);
        }

        private static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // ════════════════════════════════════════════════════════════════
        // GROUND PAINT
        // ════════════════════════════════════════════════════════════════

        private static void PaintGround(TerrainData data)
        {
            var layers = new List<TerrainLayer>();
            var grass  = MakeLayer("GrassSubstance001_COMPILED", "Grass", 24f);
            var grass2 = MakeLayer("GrassSubstance002_COMPILED", "Meadow", 30f);
            var rock   = MakeLayer("RockSubstance003_COMPILED", "Rock", 34f);
            var ground = MakeLayer("GroundSubstance002_COMPILED", "Dirt", 20f);
            // THE BLOCKING LAYER. The asset name is the contract —
            // PassabilityGrid.LoadNoWalkMask looks for "nowalk" in the layer
            // name and nothing else, so renaming this asset silently unblocks
            // every mountain on the map. It wears the same rock texture as
            // the walkable scree at a coarser tile, so the wall and its feet
            // read as one massif in game while staying distinguishable in the
            // terrain palette.
            var noWalk = MakeLayer("RockSubstance003_COMPILED", "NoWalk", 26f);

            foreach (var l in new[] { grass, grass2, rock, ground, noWalk })
                if (l != null) layers.Add(l);

            if (layers.Count == 0)
            {
                Debug.LogWarning("[SunderedCrown] No terrain layers could be built from " +
                                 $"{SharedRoot} — the terrain will render untextured. " +
                                 "Check that the Shared substance folders still contain " +
                                 "*_basecolor.tga files.");
                return;
            }
            data.terrainLayers = layers.ToArray();

            int iGrass  = 0;
            int iMeadow = layers.Count > 1 ? 1 : 0;
            int iRock   = layers.Count > 2 ? 2 : 0;
            int iDirt   = layers.Count > 3 ? 3 : iGrass;
            int iNoWalk = layers.Count > 4 ? 4 : -1;

            if (iNoWalk < 0)
                Debug.LogError("[SunderedCrown] The NoWalk layer could not be built, so the " +
                               "mountains WILL NOT BLOCK — every corner is open to its " +
                               "neighbours and the map's whole premise is gone. Check that " +
                               $"{SharedRoot}/RockSubstance003_COMPILED_graph_0 still has its " +
                               "basecolor texture.");

            int res = data.alphamapResolution;
            var map = new float[res, res, layers.Count];
            float half = TerrainSize / 2f;

            for (int z = 0; z < res; z++)
            {
                float wz = -half + (z / (float)(res - 1)) * TerrainSize;
                for (int x = 0; x < res; x++)
                {
                    float wx = -half + (x / (float)(res - 1)) * TerrainSize;

                    float nx = (x + 0.5f) / res;
                    float nz = (z + 0.5f) / res;
                    float steep = data.GetSteepness(nx, nz);          // degrees
                    float height = data.GetInterpolatedHeight(nx, nz); // world metres

                    var w = new float[layers.Count];

                    // THE WALL. NoWalk is computed first and every other layer
                    // is then scaled into what is left, so after
                    // normalization the NoWalk weight is EXACTLY this value.
                    // That matters: PassabilityGrid blocks at a painted weight
                    // of >= 0.5, so if the other layers were free to dilute it
                    // the mountains would block in patches — the worst
                    // possible failure, since it looks fine and plays broken.
                    float noWalkW = iNoWalk >= 0
                        ? SmoothStep(Mathf.InverseLerp(NoWalkFrom, NoWalkFull, RidgeFraction(wx, wz)))
                        : 0f;
                    float rest = 1f - noWalkW;

                    // Rock owns the steep, high ground the wall stands on —
                    // its scree feet — so the massif reads as one mass.
                    float rockW = Mathf.InverseLerp(18f, 34f, steep);
                    rockW = Mathf.Max(rockW, Mathf.InverseLerp(BaseHeight + 5f,
                                                               BaseHeight + 16f, height));

                    // The Crown wears dirt so the objective reads at a glance.
                    float dc = Mathf.Sqrt(wx * wx + wz * wz);
                    float crownW = (1f - rockW) * Mathf.InverseLerp(CrownRadius, CrownRadius * 0.45f, dc);

                    // Meadow breaks up the plain with a soft organic mask.
                    float meadowW = (1f - rockW) * (1f - crownW) *
                        Mathf.Clamp01(Mathf.PerlinNoise(wx * 0.010f + 13.7f, wz * 0.010f + 4.2f) * 1.6f - 0.45f);

                    float grassW = Mathf.Max(0f, 1f - rockW - crownW - meadowW);

                    float other = rockW + crownW + meadowW + grassW;
                    float k = other > 0.0001f ? rest / other : 0f;

                    if (iNoWalk >= 0) w[iNoWalk] += noWalkW;
                    w[iRock]   += rockW * k;
                    w[iDirt]   += crownW * k;
                    w[iMeadow] += meadowW * k;
                    w[iGrass]  += grassW * k;

                    float sum = 0f;
                    for (int i = 0; i < w.Length; i++) sum += w[i];
                    if (sum <= 0.0001f) { w[iGrass] = 1f; sum = 1f; }
                    for (int i = 0; i < w.Length; i++) map[z, x, i] = w[i] / sum;
                }
            }
            data.SetAlphamaps(0, 0, map);
        }

        /// <summary>
        /// Build (or rebuild) a TerrainLayer asset from one of the Shared
        /// substance folders. Returns null when the source textures are
        /// missing rather than throwing, so a partial Shared folder degrades
        /// to fewer layers instead of failing the whole generate.
        /// </summary>
        private static TerrainLayer MakeLayer(string substance, string niceName, float tile)
        {
            string dir = $"{SharedRoot}/{substance}_graph_0";
            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{substance}_basecolor.tga");
            if (diffuse == null) return null;
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{substance}_normal.tga");

            string path = $"{MapFolder}/{niceName}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = diffuse;
            layer.normalMapTexture = normal;
            layer.tileSize = new Vector2(tile, tile);
            layer.tileOffset = Vector2.zero;

            ApplyMatteSurface(layer);
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>
        /// KILL THE SPARKLE. Force a terrain layer to a matte surface.
        ///
        /// Symptom: ground "covered in diamonds" — sharp white specular
        /// points scattered across the grass.
        ///
        /// Cause: with no mask map assigned, the terrain shader takes
        /// smoothness from the DIFFUSE TEXTURE'S ALPHA, not from the
        /// m_Smoothness scalar — so a layer showing m_Smoothness: 0 can still
        /// be mirror-finish. These Substance basecolor TGAs carry no
        /// meaningful alpha (Substance often packs height there, and where
        /// there is no alpha at all it samples as 1.0), so smoothness lands
        /// somewhere between "wet" and "chrome". Every high-frequency wrinkle
        /// in the normal map then catches the sun as a pinpoint highlight.
        ///
        /// Fix: remap the diffuse ALPHA range to zero — w is the alpha
        /// channel of the remap — which forces alpha-derived smoothness to 0
        /// whatever the texture holds, using public API rather than poking
        /// the private m_SmoothnessSource field.
        /// </summary>
        private static void ApplyMatteSurface(TerrainLayer layer)
        {
            var min = layer.diffuseRemapMin;
            var max = layer.diffuseRemapMax;

            // A layer that was never configured can carry an all-zero max
            // remap; writing that back would render the ground black. Treat
            // a degenerate RGB remap as identity, and otherwise leave the
            // author's colour grading alone — we are only here for alpha.
            if (max.x <= 0f && max.y <= 0f && max.z <= 0f)
                max = new Vector4(1f, 1f, 1f, max.w);

            layer.diffuseRemapMin = new Vector4(min.x, min.y, min.z, 0f);
            layer.diffuseRemapMax = new Vector4(max.x, max.y, max.z, 0f); // alpha -> 0

            // Belt and braces: the scalar path, in case a URP version reads it
            // instead. Ground in this game is matte — shiny grass and wet rock
            // are both wrong for the art direction.
            layer.smoothness = 0f;
            layer.metallic = 0f;
            layer.specular = Color.black;

            // Half-strength normals. Even at smoothness 0 these Substance maps
            // are dense enough to shimmer as the camera moves: a 2K normal
            // tiled every ~24 m puts many texels inside one screen pixel at
            // RTS altitude.
            layer.normalScale = 0.5f;
        }

        /// <summary>
        /// General utility (works on ANY open map, not just this one): apply
        /// the matte fix to every layer of the active terrain. Here so a
        /// shiny map can be repaired in one click instead of a full
        /// regenerate — and so the older hand-built maps can be fixed too,
        /// since they were authored against the same Substance textures.
        /// </summary>
        [MenuItem("Waning Border/Maps/Fix Terrain Layer Shine (Open Scene)")]
        public static void FixTerrainShine()
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null)
            {
                EditorUtility.DisplayDialog("Fix Terrain Layer Shine",
                    "No active Terrain in the open scene.", "OK");
                return;
            }

            var layers = terrain.terrainData.terrainLayers;
            int n = 0;
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                ApplyMatteSurface(layer);
                EditorUtility.SetDirty(layer);
                n++;
            }
            AssetDatabase.SaveAssets();

            Debug.Log($"[SunderedCrown] Matte fix applied to {n} terrain layer(s) on " +
                      $"'{terrain.name}'. Albedo-alpha smoothness forced to 0, normals at " +
                      "half strength.", terrain);
        }

        // ════════════════════════════════════════════════════════════════
        // FLORA
        // ════════════════════════════════════════════════════════════════

        private static void ScatterFlora(Terrain terrain)
        {
            var data = terrain.terrainData;

            var treePrefabs = FindPrefabs(new[]
            {
                "SM_Env_Tree_", "SM_Env_Big_Tree_01", "SM_Env_Pine_", "SM_Env_Dead_Tree_",
            }, 14);
            // Bushes and boulders ride in the TREE layer, not the detail
            // layer: they are prop-scale meshes, and the detail layer is now
            // reserved for terrain grass (which must stay textured quads to
            // catch the wind). Terrain trees also give us distance culling and
            // billboarding for free, which loose GameObjects would not.
            var rockPrefabs = FindPrefabs(new[]
            {
                "SM_Env_Rock_", "SM_Env_Boulder_", "SM_Env_Bush_",
            }, 12);

            var protoPrefabs = new List<GameObject>();
            protoPrefabs.AddRange(treePrefabs);
            protoPrefabs.AddRange(rockPrefabs);
            if (protoPrefabs.Count == 0)
            {
                Debug.LogWarning($"[SunderedCrown] No Synty environment prefabs found under {SyntyEnv} — " +
                                 "the map will generate without trees or rocks.");
            }
            else
            {
                // Unity validates tree/detail prototypes on ASSIGNMENT and
                // throws on anything it dislikes (missing renderer, unusable
                // material). Trees and detail are therefore attempted
                // independently — losing the grass should not also lose the
                // forest.
                try
                {
                    data.treePrototypes = BuildTreePrototypes(protoPrefabs);
                    data.SetTreeInstances(BuildTrees(data, treePrefabs.Count, protoPrefabs.Count), true);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[SunderedCrown] Tree pass failed, continuing without trees. " +
                                     $"{e.GetType().Name}: {e.Message}");
                }
            }

            // Ground cover uses Unity's own terrain GRASS — textured quads
            // driven by the terrain's waving-grass wind, not mesh prototypes.
            // That is the system that actually sways.
            try { PaintDetails(data); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SunderedCrown] Grass pass failed, continuing without " +
                                 $"ground cover. {e.GetType().Name}: {e.Message}");
            }
        }

        private static TreePrototype[] BuildTreePrototypes(List<GameObject> prefabs)
        {
            var protos = new TreePrototype[prefabs.Count];
            for (int i = 0; i < prefabs.Count; i++)
                protos[i] = new TreePrototype { prefab = prefabs[i], bendFactor = 0f };
            return protos;
        }

        private static TreeInstance[] BuildTrees(TerrainData data, int treeProtoCount, int totalProtos)
        {
            var rng = new System.Random(RngSeed);
            var list = new List<TreeInstance>(TreeCount);
            float half = TerrainSize / 2f;

            for (int i = 0; i < TreeCount; i++)
            {
                float wx = (float)(rng.NextDouble() * TerrainSize - half);
                float wz = (float)(rng.NextDouble() * TerrainSize - half);
                if (!IsPlantable(data, wx, wz, out float steep, out float height)) continue;

                // Rocks go on the high steep ground, trees on the low flats —
                // so the mountains read as scree and the plains as woodland.
                // Thresholds track the 25 m ridge height: at the old 75 m
                // they were tuned for a massif three times as tall and would
                // now put woodland over the entire wall.
                bool wantRock = steep > 18f || height > BaseHeight + 8f;
                int proto;
                if (wantRock && totalProtos > treeProtoCount)
                    proto = treeProtoCount + rng.Next(totalProtos - treeProtoCount);
                else if (treeProtoCount > 0)
                    proto = rng.Next(treeProtoCount);
                else
                    proto = rng.Next(totalProtos);

                float scale = (0.85f + (float)rng.NextDouble() * 0.55f) * TreeScale;
                list.Add(new TreeInstance
                {
                    position = new Vector3((wx + half) / TerrainSize, 0f, (wz + half) / TerrainSize),
                    prototypeIndex = proto,
                    // Two separate knobs: TreeScale sets overall size,
                    // TreeHeightRatio keeps the squat silhouette that fixed
                    // the "stretched" look. Changing one never disturbs the
                    // other.
                    widthScale = scale,
                    heightScale = scale * TreeHeightRatio,
                    rotation = (float)(rng.NextDouble() * Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
            return list.ToArray();
        }

        /// <summary>
        /// Unity terrain grass: alpha-cut quads rendered by the waving-grass
        /// shader and animated by the TerrainData wavingGrass* parameters.
        /// No mesh prototypes and no WindZone involved — this is the terrain
        /// system's own grass, which is the only ground cover that actually
        /// moves.
        /// </summary>
        private static void PaintDetails(TerrainData data)
        {
            var grassTex = BuildGrassTexture();
            if (grassTex == null)
            {
                Debug.LogWarning("[SunderedCrown] Grass texture could not be written — " +
                                 "skipping ground cover.");
                return;
            }

            // Two prototypes off one texture: a short common tuft and a
            // slightly taller, yellower, rarer one. Cheaper than two textures
            // and enough variation that the field does not read as a stamp.
            var protos = new[]
            {
                new DetailPrototype
                {
                    usePrototypeMesh = false,          // NOT a mesh — terrain grass
                    prototypeTexture = grassTex,
                    // DetailRenderMode.Grass builds crossed quads and runs the
                    // waving shader. GrassBillboard would also wave but always
                    // turns to face the camera, which spins visibly when you
                    // rotate an RTS view.
                    renderMode = DetailRenderMode.Grass,
                    useInstancing = false,             // invalid outside VertexLit
                    minWidth = 0.55f * GrassScale, maxWidth = 0.95f * GrassScale,
                    minHeight = 0.40f * GrassScale, maxHeight = 0.62f * GrassScale,
                    noiseSpread = 14f,
                    healthyColor = new Color(0.72f, 0.84f, 0.52f, 1f),
                    dryColor     = new Color(0.80f, 0.80f, 0.46f, 1f),
                },
                new DetailPrototype
                {
                    usePrototypeMesh = false,
                    prototypeTexture = grassTex,
                    renderMode = DetailRenderMode.Grass,
                    useInstancing = false,
                    minWidth = 0.45f * GrassScale, maxWidth = 0.75f * GrassScale,
                    minHeight = 0.48f * GrassScale, maxHeight = 0.78f * GrassScale,
                    noiseSpread = 22f,
                    healthyColor = new Color(0.66f, 0.78f, 0.44f, 1f),
                    dryColor     = new Color(0.86f, 0.82f, 0.50f, 1f),
                },
            };
            data.detailPrototypes = protos;

            int res = data.detailResolution;
            float half = TerrainSize / 2f;
            var rng = new System.Random(RngSeed ^ 0x1234);

            for (int p = 0; p < protos.Length; p++)
            {
                var layer = new int[res, res];
                float freq = 0.02f + p * 0.006f;
                for (int z = 0; z < res; z++)
                {
                    float wz = -half + (z / (float)(res - 1)) * TerrainSize;
                    for (int x = 0; x < res; x++)
                    {
                        float wx = -half + (x / (float)(res - 1)) * TerrainSize;
                        if (!IsPlantable(data, wx, wz, out float steep, out _)) continue;
                        if (steep > 20f) continue;              // no grass on the crags
                        if (RidgeFraction(wx, wz) > NoWalkFrom) continue;  // nor on the wall

                        // Sparse by intent. The cutoff leaves roughly the top
                        // third of the noise field as grass, so the plain is
                        // patchy meadow rather than a wall-to-wall lawn — and
                        // sparse is also what pays for the wind, since waving
                        // grass cannot be GPU-instanced.
                        float n = Mathf.PerlinNoise(wx * freq + p * 31.4f, wz * freq + p * 17.1f);
                        if (n < GrassCutoff) continue;
                        layer[z, x] = Mathf.RoundToInt(
                            DetailDensity * (n - GrassCutoff) / (1f - GrassCutoff));
                    }
                }
                data.SetDetailLayer(0, 0, p, layer);
            }
        }

        /// <summary>
        /// Draw the grass blade sheet the terrain detail system needs.
        ///
        /// Generated rather than sourced because the project has no grass
        /// billboard art: the Shared "GrassSubstance" maps are seamless GROUND
        /// textures with no alpha, and Synty's grass is mesh prefabs. Unity's
        /// grass renderer needs an alpha-cut blade sheet, so one is written
        /// into the map folder. Self-contained, deterministic, and tuned to
        /// the short sparse look this map wants.
        ///
        /// Blades are drawn bottom-anchored: the terrain grass shader treats
        /// the bottom edge of the texture as the rooted end and bends the top,
        /// so a blade that does not reach v=0 appears to hover.
        /// </summary>
        private static Texture2D BuildGrassTexture()
        {
            const int W = 256, H = 256;
            const int Blades = 18;

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.4f, 0.55f, 0.25f, 0f);

            var rng = new System.Random(RngSeed ^ 0x6A55);
            for (int b = 0; b < Blades; b++)
            {
                // A TUFT, not a scatter. Every blade is rooted in a narrow
                // band at the bottom centre and fans outward toward its tip,
                // so the quad reads as one clump of grass.
                //
                // The first version rooted blades at random x across the full
                // width with big gaps between them. Each rendered quad is the
                // WHOLE texture, so that produced a few thin lines smeared
                // over half a metre — nearly invisible at RTS camera height,
                // which is why the grass could not be seen at all.
                float baseX = W * 0.5f + (float)(rng.NextDouble() - 0.5) * 46f;
                float tipDx = (float)(rng.NextDouble() - 0.5) * 190f;  // fan out to fill
                float height = H * (0.62f + (float)rng.NextDouble() * 0.36f);
                float halfW = 5.0f + (float)rng.NextDouble() * 4.0f;
                float hue = 0.30f + (float)rng.NextDouble() * 0.16f;

                int steps = Mathf.CeilToInt(height);
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;                 // 0 root -> 1 tip
                    float y = t * height;
                    // Quadratic lean so the blade curves instead of shearing.
                    float x = baseX + tipDx * t * t;
                    float w = halfW * (1f - t * 0.92f);         // taper to a point
                    if (w < 0.5f) w = 0.5f;

                    // Darker at the root, brighter at the tip — reads as depth
                    // once thousands of these overlap.
                    float shade = 0.55f + 0.45f * t;
                    var col = new Color(hue * 0.72f * shade, (0.42f + hue * 0.55f) * shade,
                                        0.20f * shade, 1f);

                    int y0 = Mathf.RoundToInt(y);
                    if (y0 < 0 || y0 >= H) continue;
                    int xs = Mathf.FloorToInt(x - w), xe = Mathf.CeilToInt(x + w);
                    for (int xi = xs; xi <= xe; xi++)
                    {
                        if (xi < 0 || xi >= W) continue;
                        // Soft edge so the alpha cutout does not alias into a
                        // staircase at the camera distances this map plays at.
                        float d = Mathf.Abs(xi - x);
                        float a = Mathf.Clamp01((w - d) + 0.5f);
                        if (a <= 0f) continue;
                        int idx = y0 * W + xi;
                        if (a > px[idx].a) px[idx] = new Color(col.r, col.g, col.b, a);
                    }
                }
            }

            tex.SetPixels(px);
            tex.Apply();

            string path = $"{MapFolder}/GrassBlades.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Keep flora off the ground the player needs: the build plateaus and
        /// the Crown itself. A base ringed with trees or a well you cannot
        /// see over is a worse map, however pretty the screenshot.
        /// </summary>
        private static bool IsPlantable(TerrainData data, float wx, float wz,
                                        out float steepness, out float height)
        {
            float half = TerrainSize / 2f;
            float nx = Mathf.Clamp01((wx + half) / TerrainSize);
            float nz = Mathf.Clamp01((wz + half) / TerrainSize);
            steepness = data.GetSteepness(nx, nz);
            height = data.GetInterpolatedHeight(nx, nz);

            float dc = Mathf.Sqrt(wx * wx + wz * wz);
            if (dc < CrownRadius + 10f * MapScale) return false;   // keep the objective clear

            foreach (var s in Starts)
            {
                float dx = wx - s.x, dz = wz - s.z;
                float clear = PlateauRadius + 14f * MapScale;
                if (dx * dx + dz * dz < clear * clear)
                    return false;                                   // keep build space clear
            }
            return true;
        }

        private static List<GameObject> FindPrefabs(string[] namePrefixes, int max)
        {
            var found = new List<GameObject>();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { SyntyEnv });
            foreach (var guid in guids)
            {
                if (found.Count >= max) break;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                foreach (var prefix in namePrefixes)
                {
                    if (!file.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) found.Add(go);
                    break;
                }
            }
            return found;
        }

        // ════════════════════════════════════════════════════════════════
        // MARKERS
        // ════════════════════════════════════════════════════════════════

        private static void PlaceMarkers()
        {
            var root = new GameObject("Markers");

            // ── The objective: one well, on the Crown, dead centre ───────
            // BorderNodeMarker presence disables the procedural placement
            // loop entirely, so this is the ONLY well on the map — which is
            // the whole design.
            Marker<BorderNodeMarker>(root, "Well — The Crown", new Vector3(0f, 0f, 0f));

            // ── Per-corner economy ──────────────────────────────────────
            // Generous by intent: the brief asks for ample supply all game,
            // and a corner that runs dry has nowhere to expand to except
            // through a mountain. Each start gets two iron patches and two
            // veilstone patches inside its own bowl, plus a veilsteel node
            // out toward the middle to pull play forward.
            foreach (var s in Starts)
            {
                var inward = new Vector2(-s.x, -s.z).normalized;   // toward map centre
                var lateral = new Vector2(-inward.y, inward.x);

                // Offsets are authored at MapScale 1 and scaled here, so the
                // economy keeps the same relative geometry at any map size.
                Vector3 At(float fwdRaw, float sideRaw)
                {
                    float fwd = fwdRaw * MapScale, side = sideRaw * MapScale;
                    return AtScaled(fwd, side);
                }

                Vector3 AtScaled(float fwd, float side) => new Vector3(
                    s.x + inward.x * fwd + lateral.x * side, 0f,
                    s.z + inward.y * fwd + lateral.y * side);

                var iron1 = Marker<IronPatchMarker>(root, $"Iron A — {s.faction}", At(-18f, -34f));
                iron1.DepositCount = 34; iron1.Spread = 9f;
                var iron2 = Marker<IronPatchMarker>(root, $"Iron B — {s.faction}", At(-18f, 34f));
                iron2.DepositCount = 34; iron2.Spread = 9f;

                var vs1 = Marker<VeilstoneOutcroppingMarker>(root, $"Veilstone A — {s.faction}", At(30f, -30f));
                vs1.NodeCount = 26; vs1.VeilstonePerNode = 30; vs1.Spread = 8f;
                var vs2 = Marker<VeilstoneOutcroppingMarker>(root, $"Veilstone B — {s.faction}", At(30f, 30f));
                vs2.NodeCount = 26; vs2.VeilstonePerNode = 30; vs2.Spread = 8f;

                // Second ring, further out — the reason to leave the plateau.
                var iron3 = Marker<IronPatchMarker>(root, $"Iron Forward — {s.faction}", At(74f, 0f));
                iron3.DepositCount = 30; iron3.Spread = 10f;

                Marker<VeilsteelDepositMarker>(root, $"Veilsteel — {s.faction}", At(58f, -52f))
                    .Amount = 1800;
            }

            // ── Contested middle ────────────────────────────────────────
            // Four rich patches ringing the Crown, one facing each player.
            // They sit inside the bowl, so taking one means standing in the
            // open where all three rivals can reach you.
            var ring = new[]
            {
                new Vector3(0f, 0f, -RidgeInner + 8f * MapScale),
                new Vector3(0f, 0f,  RidgeInner - 8f * MapScale),
                new Vector3(-RidgeInner + 8f * MapScale, 0f, 0f),
                new Vector3( RidgeInner - 8f * MapScale, 0f, 0f),
            };
            for (int i = 0; i < ring.Length; i++)
            {
                var v = Marker<VeilstoneOutcroppingMarker>(root, $"Veilstone — Crown {i + 1}", ring[i]);
                v.NodeCount = 30; v.VeilstonePerNode = 40; v.Spread = 10f;
            }

            // ── Player starts ───────────────────────────────────────────
            foreach (var s in Starts)
                Marker<PlayerStartMarker>(root, $"Start — {s.faction}",
                    new Vector3(s.x, 0f, s.z)).Faction = s.faction;
        }

        /// <summary>Create a marker and drop it onto the terrain surface.</summary>
        private static T Marker<T>(GameObject parent, string name, Vector3 pos) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            pos.y = SampleHeight(pos.x, pos.z);
            go.transform.position = pos;
            return go.AddComponent<T>();
        }

        private static float SampleHeight(float x, float z)
        {
            var t = Terrain.activeTerrain;
            if (t == null) return 0f;
            return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
        }

        // ════════════════════════════════════════════════════════════════
        // LIGHTING / AMBIENCE
        // ════════════════════════════════════════════════════════════════

        private static void BuildLighting()
        {
            // GameBootstrap adds a DayNightCycle at runtime if none exists and
            // will drive this light, so the values here are the editor-preview
            // look rather than the final in-match lighting.
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.957f, 0.882f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(46f, 138f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor    = new Color(0.53f, 0.60f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.44f);
            RenderSettings.ambientGroundColor  = new Color(0.24f, 0.22f, 0.20f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0016f;
            RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.72f);
        }
    }
}
#endif
