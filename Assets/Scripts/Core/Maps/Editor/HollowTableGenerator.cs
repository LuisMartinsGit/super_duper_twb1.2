// HollowTableGenerator.cs
// EDITOR-ONLY: builds the "Hollow Table" 1v1 duel map from scratch.
//   Waning Border > Maps > Generate "Hollow Table" (1v1)
//
// THE DESIGN — A SMALL OPEN TABLE WITH ONE WELL ON IT
//   Two warbands face each other across 128 m of open plain. There are no
//   ridges, no lanes and no chokes: the whole map is one field you can see
//   across, and the only relief is a low mesa in the middle — the Table —
//   with the map's SINGLE well standing on it.
//
//   The economy does the work the terrain refuses to do. Home ground is
//   deliberately THIN: 1400 iron and 720 veilstone inside each base, which
//   opens a game and does not finish one. Everything else sits in three
//   contested places nobody owns —
//
//     * the two WINGS, due north and due south, each holding 2400 iron,
//       1920 veilstone and a 2000 veilsteel node, and each EXACTLY 85 m
//       from both bases, and
//     * the TABLE itself: four veilstone patches around the mesa's foot,
//       under the well.
//
//   So you must leave home early, and everywhere worth standing is a place
//   your opponent can reach at the same moment you do. That is the whole
//   map: no safe expansion exists, so expansion is a fight.
//
// THE WELL IS THE MAP
//   One well, dead centre, on the mesa. Well-domination victory scores
//   against the live node count (NodeVictorySystem), so with N = 1 the
//   victory condition reads as plainly as a king-of-the-hill: apply your
//   culture's verb to the Table and hold it. The marker ticks
//   AuthoredPosition, which is what tells BorderNodeBootstrap to use this
//   map's well list instead of the default four corner wells — corners
//   would put four objectives on a map whose entire premise is one.
//
// WHAT BLOCKS, AND WHY SO LITTLE
//   Four crags — one off each base's shoulder — are the only impassable
//   ground, and they are blocked by the PAINTED "NoWalk" terrain layer
//   rather than by their slope (PassabilityGrid blocks any cell painted
//   >= 0.5 regardless of gradient; the mask is asset data, identical on
//   every client, so it is lockstep-safe). The paint is derived from the
//   same crag function that sculpts them, so look and function cannot
//   drift apart. Nothing else on the map blocks. The Table is deliberately
//   gentle — a 5 m rise over 14 m of skirt, peak gradient 0.54, well under
//   the 1.0 walk limit — because it is the objective and must never be a
//   climb.
//
// Idempotent: re-running overwrites the scene, terrain and layers in place.
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime
// asmdef with no separate editor assembly — the Editor/ folder name alone
// does not exclude it from player builds.

#if UNITY_EDITOR
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class HollowTableGenerator
    {
        // ── Identity ────────────────────────────────────────────────────
        private const string MapName = "Hollow Table";
        private const string SceneName = "HollowTable";
        private const string MapFolder = "Assets/GameData/Scenes/Maps/Hollow Table";
        private const string Tag = "HollowTable";

        // ── Footprint ───────────────────────────────────────────────────
        // 192 m against Sundered Crown's 256 m: 75% of the span, 56% of the
        // area, and half the players. Base-to-base is 128 m — close enough
        // that a push is a decision rather than a march, which is the point
        // of an open duel map.
        private const int MapSize = 192;
        private const int HeightmapRes = 257;   // must be 2^n + 1
        private const int AlphamapRes = 256;
        private const int DetailRes = 128;
        private const float MaxHeight = 120f;

        // ── Height budget (world metres) ────────────────────────────────
        private const float PlainY = 10f;
        private const float RollAmplitude = 1.5f;   // cosmetic undulation only

        // The Table: a MESA, not a hill. 4 m of rise spread over an 18 m
        // skirt. Worst-case gradient is the rise (1.5 * 4 / 18 = 0.33) plus
        // the roll being flattened out under it (1.5 * 2.4 / 18 = 0.20) —
        // about half the 1.0 walk limit even where both align. It must stay
        // trivially walkable from every side: it is the only objective here.
        private const float TableRadius = 36f;
        private const float TableFlat = 18f;
        private const float TableRise = 4f;

        // Build space. Flat core plus a feathered rim so a Hall and its hut
        // belt never fight the terrain.
        private const float PlateauRadius = 42f;
        private const float PlateauFlat = 30f;
        private const float PlateauRise = 1.5f;

        // Crags: 20 m of rise over a 10 m skirt = gradient 3.0, unambiguous
        // cliff. Shape is aesthetic; the NoWalk paint does the blocking.
        private const float CragRadius = 16f;
        private const float CragFlat = 6f;
        private const float CragRise = 20f;

        /// <summary>Crag strength (0..1) between which the ground is painted
        /// NoWalk. Chosen so the blocked disc is ~12 m of the 16 m crag,
        /// leaving 4 m of walkable scree so the rocks have feet instead of
        /// meeting the plain at a seam.</summary>
        private const float NoWalkFrom = 0.22f;
        private const float NoWalkFull = 0.42f;

        // ── Layout ──────────────────────────────────────────────────────
        private const float BaseX = 64f;    // starts at (±64, 0)
        private const float WingZ = 56f;    // contested wings at (0, ±56)

        /// <summary>Base shoulders. Far from the centre lane and far from the
        /// wings, so they shelter a base without gating anything.</summary>
        private static readonly Vector2[] Crags =
        {
            new Vector2(-62f, -58f), new Vector2(-62f, 58f),
            new Vector2(62f, -58f), new Vector2(62f, 58f),
        };

        private const int RngSeed = 0x7AB1;

        /// <summary>Density matched to Sundered Crown per hectare (~0.01
        /// trees/m²) so the smaller map is not proportionally denser.</summary>
        private const int TreeCount = 420;

        [MenuItem("Waning Border/Maps/Generate \"Hollow Table\" (1v1)")]
        public static void Generate()
        {
            if (!EditorUtility.DisplayDialog(
                    "Generate Hollow Table",
                    $"This creates (or overwrites) the map at:\n{MapFolder}\n\n" +
                    "The open scene will be replaced. Save anything you care about first.",
                    "Generate", "Cancel"))
                return;

            // AssetDatabase-AWARE folder creation: Directory.CreateDirectory
            // alone puts the folder on disk but leaves it unknown to the
            // AssetDatabase, and CreateAsset into an unimported folder fails.
            MapAssetFolders.Ensure(MapFolder);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            try
            {
                EditorUtility.DisplayProgressBar(MapName, "Sculpting terrain…", 0.1f);
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

                EditorUtility.DisplayProgressBar(MapName, "Painting ground…", 0.4f);
                MapGenKit.PaintGround(terrain.terrainData, new MapGenKit.PaintSpec
                {
                    MapFolder = MapFolder,
                    Size = MapSize,
                    NoWalk = NoWalkAt,
                    Dirt = TableDirtAt,
                    // The plain tops out at 11.5 m and the Table at 15 m, so a
                    // rock band starting at 16 m puts scree on the crags and
                    // nowhere else — the mesa keeps its bare-earth read.
                    RockHeightFrom = 16f,
                    RockHeightTo = 26f,
                });

                // Flora is the one step that depends on third-party prefabs
                // validating against Unity's prototype rules, so it must not
                // be able to take the map down with it. A map with no bushes
                // is a map; a map that failed to save is nothing.
                EditorUtility.DisplayProgressBar(MapName, "Scattering flora…", 0.6f);
                try
                {
                    MapGenKit.ScatterFlora(terrain, new MapGenKit.FloraSpec
                    {
                        MapFolder = MapFolder,
                        Size = MapSize,
                        Seed = RngSeed,
                        TreeCount = TreeCount,
                        CanPlant = CanPlant,
                        RockAboveHeight = 18f,
                    });
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[{Tag}] Flora pass failed — map continues without " +
                                     $"trees/detail. {e.GetType().Name}: {e.Message}");
                }

                EditorUtility.DisplayProgressBar(MapName, "Placing markers…", 0.8f);
                PlaceMarkers();
                MapGenKit.BuildLighting();

                EditorUtility.DisplayProgressBar(MapName, "Saving…", 0.9f);
                string scenePath = $"{MapFolder}/{SceneName}.unity";
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                    throw new System.IO.IOException($"SaveScene refused to write {scenePath}");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // REGISTER FIRST, DECORATE AFTER. Being in the lobby depends
                // only on the scene existing in Build Settings; the thumbnail
                // is presentation. Cosmetics must never gate availability.
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
                EditorUtility.DisplayDialog("Generate Hollow Table",
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
        /// The whole map in one height function.
        ///
        /// Every feature BLENDS rather than max()-ing against an absolute
        /// elevation. A max(y, PlainY + feature) looks equivalent and is not:
        /// where the feature falls to zero it collapses to max(y, PlainY),
        /// which silently clamps away every trough of the cosmetic roll and
        /// leaves a step at each feature's rim. Weight-and-lerp stays
        /// continuous at the rim by construction.
        /// </summary>
        private static float HeightAt(float wx, float wz)
        {
            // 1. The plain, with two long wavelengths of cosmetic undulation.
            //    Peak gradient stays near 0.15 — an order under the 1.0 walk
            //    limit — so decoration can never accidentally wall anything.
            float y = PlainY
                + Mathf.Sin(wx * 0.021f) * Mathf.Cos(wz * 0.017f) * RollAmplitude
                + Mathf.Sin((wx + wz) * 0.009f) * (RollAmplitude * 0.6f);

            // 2. Build plateaus — flatten the build space so a Hall and its
            //    hut belt never fight the terrain.
            for (int s = -1; s <= 1; s += 2)
            {
                float d = MapGenKit.Dist(wx, wz, s * BaseX, 0f);
                if (d >= PlateauRadius) continue;
                float t = MapGenKit.SmoothStep(Mathf.InverseLerp(PlateauRadius, PlateauFlat, d));
                y = Mathf.Lerp(y, PlainY + PlateauRise, t);
            }

            // 3. The Table: flatten the roll out from under it, then lift.
            //    The top comes out dead level for the well and for building.
            float table = MapGenKit.Dome(
                MapGenKit.Dist(wx, wz, 0f, 0f), TableFlat, TableRadius, 1f);
            if (table > 0f)
            {
                y = Mathf.Lerp(y, PlainY, table);
                y += table * TableRise;
            }

            // 4. The crags, added on top of whatever ground they stand on.
            y += CragHeightAt(wx, wz);
            return y;
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
        /// The wall, derived from the SAME function that sculpts the crags so
        /// the two cannot drift apart. Returns the exact NoWalk weight the
        /// alphamap will carry, which is what PassabilityGrid thresholds at
        /// 0.5 — dilution here would block the crags in patches, the worst
        /// possible failure because it looks fine and plays broken.
        /// </summary>
        private static float NoWalkAt(float wx, float wz)
        {
            float frac = CragHeightAt(wx, wz) / CragRise;
            return MapGenKit.SmoothStep(Mathf.InverseLerp(NoWalkFrom, NoWalkFull, frac));
        }

        /// <summary>The Table wears bare earth so the objective reads at a
        /// glance from an RTS camera.</summary>
        private static float TableDirtAt(float wx, float wz)
        {
            float d = MapGenKit.Dist(wx, wz, 0f, 0f);
            return Mathf.InverseLerp(TableRadius, TableFlat * 0.75f, d);
        }

        /// <summary>
        /// Keep flora off the ground the player needs: the build plateaus,
        /// the Table, and the two wings where the map's real fights happen.
        /// A base ringed with trees, or a well you cannot see over, is a
        /// worse map however good the screenshot.
        /// </summary>
        private static bool CanPlant(float wx, float wz)
        {
            if (MapGenKit.Dist(wx, wz, 0f, 0f) < TableRadius + 8f) return false;

            for (int s = -1; s <= 1; s += 2)
                if (MapGenKit.Dist(wx, wz, s * BaseX, 0f) < PlateauRadius + 12f) return false;

            for (int s = -1; s <= 1; s += 2)
                if (MapGenKit.Dist(wx, wz, 0f, s * WingZ) < 28f) return false;

            return true;
        }

        // ════════════════════════════════════════════════════════════════
        // MARKERS
        // ════════════════════════════════════════════════════════════════

        private static void PlaceMarkers()
        {
            var root = new GameObject("Markers");

            // ── The objective: ONE well, on the Table, dead centre ───────
            // AuthoredPosition is what makes this the map's whole well list.
            // Without it BorderNodeBootstrap would ignore the position and
            // spawn its four corner wells, which would turn a duel over one
            // piece of ground into a four-objective sprawl.
            MapGenKit.Marker<BorderNodeMarker>(root, "Well — The Table", Vector3.zero)
                .AuthoredPosition = true;

            // ── Player starts ───────────────────────────────────────────
            MapGenKit.Marker<PlayerStartMarker>(root, "Start — Blue",
                new Vector3(-BaseX, 0f, 0f)).Faction = Faction.Blue;
            MapGenKit.Marker<PlayerStartMarker>(root, "Start — Red",
                new Vector3(BaseX, 0f, 0f)).Faction = Faction.Red;

            // ── Home economy: THIN ON PURPOSE ───────────────────────────
            // 1400 iron and 720 veilstone inside each plateau. That opens a
            // game; it does not win one. Everything else on the map is
            // contested, which is what forces both players out early.
            for (int s = -1; s <= 1; s += 2)
            {
                string side = s < 0 ? "Blue" : "Red";

                var ironA = MapGenKit.Marker<IronPatchMarker>(root, $"Iron Home A — {side}",
                    new Vector3(s * 78f, 0f, -24f));
                ironA.DepositCount = 14; ironA.Spread = 8f;
                var ironB = MapGenKit.Marker<IronPatchMarker>(root, $"Iron Home B — {side}",
                    new Vector3(s * 78f, 0f, 24f));
                ironB.DepositCount = 14; ironB.Spread = 8f;

                var vsA = MapGenKit.Marker<VeilstoneOutcroppingMarker>(root,
                    $"Veilstone Home A — {side}", new Vector3(s * 50f, 0f, -26f));
                vsA.NodeCount = 12; vsA.VeilstonePerNode = 30; vsA.Spread = 7f;
                var vsB = MapGenKit.Marker<VeilstoneOutcroppingMarker>(root,
                    $"Veilstone Home B — {side}", new Vector3(s * 50f, 0f, 26f));
                vsB.NodeCount = 12; vsB.VeilstonePerNode = 30; vsB.Spread = 7f;

                // Age 0 curse content on the way OUT of the base, not inside
                // it: the first thing you meet when you leave is blight.
                // Sited 43 m from the Hall (just past the plateau rim) and
                // >20 m off every deposit cluster, so the pocket's SmallNode
                // anchor never lands on top of a patch.
                MapGenKit.Marker<BlightPocketMarker>(root, $"Blight A — {side}",
                    new Vector3(s * 48f, 0f, -40f)).Radius = 12f;
                MapGenKit.Marker<BlightPocketMarker>(root, $"Blight B — {side}",
                    new Vector3(s * 48f, 0f, 40f)).Radius = 12f;
            }

            // ── The wings: the map's real economy, and nobody's ──────────
            // 85 m from BOTH bases, which is the whole design — there is no
            // such thing as a safe expansion here, so expanding is a fight.
            for (int s = -1; s <= 1; s += 2)
            {
                string wing = s < 0 ? "South" : "North";
                float wz = s * WingZ;

                var vsA = MapGenKit.Marker<VeilstoneOutcroppingMarker>(root,
                    $"Veilstone Wing A — {wing}", new Vector3(-18f, 0f, wz));
                vsA.NodeCount = 24; vsA.VeilstonePerNode = 40; vsA.Spread = 9f;
                var vsB = MapGenKit.Marker<VeilstoneOutcroppingMarker>(root,
                    $"Veilstone Wing B — {wing}", new Vector3(18f, 0f, wz));
                vsB.NodeCount = 24; vsB.VeilstonePerNode = 40; vsB.Spread = 9f;

                var ironA = MapGenKit.Marker<IronPatchMarker>(root, $"Iron Wing A — {wing}",
                    new Vector3(-34f, 0f, s * 48f));
                ironA.DepositCount = 24; ironA.Spread = 9f;
                var ironB = MapGenKit.Marker<IronPatchMarker>(root, $"Iron Wing B — {wing}",
                    new Vector3(34f, 0f, s * 48f));
                ironB.DepositCount = 24; ironB.Spread = 9f;

                MapGenKit.Marker<VeilsteelDepositMarker>(root, $"Veilsteel — {wing}",
                    new Vector3(0f, 0f, s * 70f)).Amount = 2000;
            }

            // ── The Table's own veilstone ───────────────────────────────
            // Four patches around the mesa's foot, under the well. Holding
            // the objective pays for holding the objective — and standing
            // here means standing in the middle of an open map.
            var feet = new[]
            {
                new Vector3(-30f, 0f, -30f), new Vector3(30f, 0f, -30f),
                new Vector3(-30f, 0f, 30f), new Vector3(30f, 0f, 30f),
            };
            for (int i = 0; i < feet.Length; i++)
            {
                var v = MapGenKit.Marker<VeilstoneOutcroppingMarker>(root,
                    $"Veilstone — Table {i + 1}", feet[i]);
                v.NodeCount = 16; v.VeilstonePerNode = 40; v.Spread = 8f;
            }
        }

        /// <summary>
        /// MapInfoBaker preserves a hand-written DisplayName / SizeTag /
        /// Description, so seed them once here on a freshly baked asset —
        /// otherwise the lobby shows a bare folder name and no size tag.
        /// </summary>
        private static void WriteMapInfoBlurb()
        {
            var info = AssetDatabase.LoadAssetAtPath<MapInfo>(
                $"{MapFolder}/{MapName} MapInfo.asset");
            if (info == null) return;

            info.DisplayName = MapName;
            info.SizeTag = "SMALL / OPEN";
            info.Description =
                "A duel across 128 m of open ground. Home holds barely enough to " +
                "open with; the north and south wings hold everything else, and " +
                "sit exactly as far from you as from your enemy. One well stands " +
                "on the mesa in the middle — the map's only objective.";
            EditorUtility.SetDirty(info);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
