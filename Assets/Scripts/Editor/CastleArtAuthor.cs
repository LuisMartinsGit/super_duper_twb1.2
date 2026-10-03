// CastleArtAuthor.cs
// Authors the stone wall set, the Alanthor watch tower and the Fortress from
// the Synty POLYGON Fantasy Kingdom castle kit, and binds them
// (docs/Design/Age_1_Alanthor.md § The wall's art, Art_Direction.md).
//
//   Waning Border > Art > Author Castle Prefabs
//   Waning Border > Art > Preview Castle Prefabs   (renders to Temp/castle_preview/)
//
// Re-runnable: every prefab is rebuilt from scratch and every binding
// re-written, so tuning the layout below and running the menu again is the
// whole workflow.
//
// THE KIT'S GRAMMAR (measured with Preview Kit Pieces):
//   * walls, battlements, hoardings: 5 m long, pivot at their +X end, running
//     to -X; walls 5 m tall.
//   * Wall_Block_01: a 5.5 m solid cube, pivot at its base centre.
//   * ROUND TOWERS are four quarter-round Wall_Corner_{S,M} pieces turned
//     about a shared centre that sits CentreX metres along -X from the pivot
//     (S 2.5, M 3.75). Base_Slope is the flared plinth below a storey,
//     Battlements_*_Corner the crown, Floor_Stone_Round the floor cap (its
//     centre IS its pivot). The Wall_Tower_* pieces are BARTIZANS that hang off
//     a wall top — never stand them on the ground (the corbel reads upside down).
//
// WALL SET (Wall/Stone/, bound per level in Wall/WallModuleArt.asset; level 0,
// the timber palisade, keeps its own art). The game fits a piece to its 3 m
// module, so the kit's 5 m is drawn at 0.6 and every piece shares that scale.
// Mesh nodes named "Curtain..." are what the fitter measures the length from —
// the solid body, which spans the module exactly.
//   EVERY level is 4 m deep (= the hub's 2x2 cells) with its walk 3 m up, and
//   SYMMETRICAL — both faces carry the same masonry, parapet and (level 3)
//   roofed hoarding gallery, so a wall has no inside (docs/Design/
//   Age_1_Alanthor.md § The stone wall). Hubs are low round bastions flush
//   with the walk at levels 1-2 and round towers under a slate cone at 3.
//
// WATCH TOWER (Civs/Alanthor/Buildings/Tower/WatchTower.prefab -> Tower.asset)
// and FORTRESS (Age0/Buildings/Fortress/Fortress.prefab -> Fortress.asset,
// presentation id 105) follow the variant layout BuildingVariantVisual reads:
//   Lv0                 construction rise (numbered pieces) / the Age 0 look
//   Alanthor/Lv1..Lv3   the culture's three levels
//   Runai, Feraldis     empty for now
// Ownership colour lives on nodes named "Stripe_*" AND on the kit's slate
// roof material (Castle_Roof_01), which takes the player's colour as a tint
// (2026-10-02). Roof pieces are therefore NOT named "Roof*": that name makes
// BuildingFactionColorMarker paint every material on the node solid slate,
// finials and gallery timber included.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class CastleArtAuthor
    {
        const string Kit = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        const string WallDir = "Assets/GameData/TechTree/Age0/Buildings/Wall/";
        // The stone kit is Alanthor's (the palisade is the Age 0 wall), so it
        // lives with the Stone Wall in Civs/Alanthor; the art config stays
        // beside its class in the shared Age 0 wall folder.
        const string AlanthorWallDir = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Wall/";
        const string StoneDir = AlanthorWallDir + "Stone/";
        const string TowerDir = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Tower/";
        const string FortressDir = "Assets/GameData/TechTree/Age0/Buildings/Fortress/";
        const int FortressPresentationId = 105;

        /// <summary>The kit's module: walls are 5 m long, 5 m tall.</summary>
        const float M = 5f;
        /// <summary>The kit's solid block is 5.5 m square.</summary>
        const float B = 5.5f;
        /// <summary>The game draws the kit at 0.6 (5 m kit module -> 3 m game module).</summary>
        const float GameScale = 0.6f;
        /// <summary>The Fortress's main mass, in blocks per side.</summary>
        const int FortGrid = 3;

        // Kit pieces.
        const string Wall = "Castle/SM_Bld_Castle_Wall_01";
        const string WallSlit = "Castle/SM_Bld_Castle_Wall_Arrowslit_01";
        const string WallRough = "Castle/SM_Bld_Castle_Wall_03";
        const string Block = "Castle/SM_Bld_Castle_Wall_Block_01";
        const string Batt = "Castle/SM_Bld_Castle_Battlements_01";
        const string BattLow = "Castle/SM_Bld_Castle_Battlements_02";
        const string Hoard = "Castle/SM_Bld_Castle_Hoarding_01";
        const string Gate = "Castle/SM_Bld_Castle_Wall_Gate_01";
        const string FloorSlab = "Castle/SM_Bld_Castle_Floor_Stone_01";
        /// <summary>A plain slate cone (Round_06 is an onion dome — not this).</summary>
        const string ConeRoof = "Castle/SM_Bld_Castle_Roof_Cap_Round_05";
        const float ConeRoofWidth = 2.28f, ConeRoofHeight = 6.87f;
        /// <summary>A square slate pyramid (Square_06 is domed — not this).</summary>
        const string PyramidRoof = "Castle/SM_Bld_Castle_Roof_Cap_Square_05";
        const float PyramidRoofWidth = 2.74f, PyramidRoofHeight = 7.42f;
        const string Banner = "Props/Banners/SM_Prop_Battle_Banner_01";
        const string Lamp = "Props/SM_Prop_Bracket_Lamp_01";

        /// <summary>Round tower sizes: the quarter pieces and their geometry.</summary>
        sealed class Round
        {
            public string Quarter, Slope, Crown, Floor;
            /// <summary>Distance along -X from a quarter's pivot to the tower centre.</summary>
            public float CentreX;
            /// <summary>Outer radius of the tower body.</summary>
            public float Radius;
        }
        static readonly Round S = new Round
        {
            Quarter = "Castle/SM_Bld_Castle_Wall_Corner_S_01", Slope = "Castle/SM_Bld_Castle_Wall_Corner_S_Base_Slope_01",
            Crown = "Castle/SM_Bld_Castle_Battlements_S_Corner_01", Floor = "Castle/SM_Bld_Castle_Floor_Stone_Round_S_01",
            CentreX = 2.5f, Radius = 2.75f,
        };
        static readonly Round Md = new Round
        {
            Quarter = "Castle/SM_Bld_Castle_Wall_Corner_M_01", Slope = "Castle/SM_Bld_Castle_Wall_Corner_M_Base_Slope_01",
            Crown = "Castle/SM_Bld_Castle_Battlements_M_Corner_01", Floor = "Castle/SM_Bld_Castle_Floor_Stone_Round_M_01",
            CentreX = 3.75f, Radius = 4f,
        };

        /// <summary>Wall depth across the wall, kit metres: 4 m in game at
        /// EVERY level (AlanthorWall.StoneWallDepth) — the hub's 2x2 cells and
        /// a walkway three infantry wide. A level changes the dressing, never
        /// the size.</summary>
        static float Depth(int lv) => TheWaningBorder.Entities.AlanthorWall.StoneWallDepth / GameScale;

        /// <summary>Height of the wall-walk, kit metres: the kit's 5 m wall,
        /// 3 m in game (AlanthorWall.DeckHeight).</summary>
        static float WalkHeight => TheWaningBorder.Entities.AlanthorWall.DeckHeight / GameScale;

        static readonly HashSet<string> _wallKitPieces = new HashSet<string>();
        static bool _collectWall;

        [MenuItem("Waning Border/Art/Author Castle Prefabs")]
        public static void AuthorAll()
        {
            if (!AssetDatabase.IsValidFolder(StoneDir.TrimEnd('/')))
                AssetDatabase.CreateFolder(AlanthorWallDir.TrimEnd('/'), "Stone");
            _wallKitPieces.Clear();

            var curtain = new GameObject[4];
            var hub = new GameObject[4];
            var gate = new GameObject[4];
            var tower = new GameObject[4];
            var mount = new GameObject[4];
            _collectWall = true;
            for (int lv = 1; lv <= 3; lv++)
            {
                curtain[lv] = Save(BuildCurtain(lv), StoneDir + $"Curtain_L{lv}.prefab");
                hub[lv] = Save(BuildHub(lv), StoneDir + $"Hub_L{lv}.prefab");
                gate[lv] = Save(BuildGate(lv), StoneDir + $"Gate_L{lv}.prefab");
                tower[lv] = Save(BuildWallTower(lv), StoneDir + $"Tower_L{lv}.prefab");
                mount[lv] = Save(BuildMount(lv), StoneDir + $"Mount_L{lv}.prefab");
            }
            _collectWall = false;
            MakeWallMeshesReadable();
            BindWallConfig(curtain, hub, gate, tower, mount);

            var watch = Save(BuildWatchTower(), TowerDir + "WatchTower.prefab");
            BindBuilding(TowerDir + "Tower.asset", watch, presentationId: null);

            var fortress = Save(BuildFortress(), FortressDir + "Fortress.prefab");
            BindBuilding(FortressDir + "Fortress.asset", fortress, FortressPresentationId);

            AssetDatabase.SaveAssets();
            Debug.Log("[CastleArtAuthor] wall set (3 levels x 5 parts), watch tower and Fortress authored and bound.");
        }

        // ── Wall set ────────────────────────────────────────────────────

        /// <summary>
        /// One run of stone wall from x0 to x1 (kit metres, along X), Depth
        /// across: a solid body whose top IS the wall-walk (named "Curtain_Body",
        /// the part the fitter measures), the level's dressing on BOTH faces,
        /// and the walk's edges — low
        /// parapets (L1), battlements (L2), battlements plus a roofed hoarding
        /// gallery (L3). Every piece on one face has its twin on the other,
        /// turned 180 degrees, so the run reads the same from either side.
        /// </summary>
        static void WallRun(Transform parent, string tag, float x0, float x1, int lv)
        {
            float len = x1 - x0, half = Depth(lv) * 0.5f, mid = (x0 + x1) * 0.5f;
            // The BODY is the module's defining part (its "Curtain…" name is
            // what WallModuleArt measures the pitch from): it spans exactly
            // x0..x1, so the module's length IS the run's, and the curtain
            // baker clamps every other piece to it — the kit faces and
            // parapets reach a little past their 5 m and used to overlap the
            // next module coplanar and z-fight (2026-10-02).
            var body = Put(Block, parent, $"Curtain_Body{tag}", new Vector3(mid, 0f, 0f));
            // The kit block is 5.5 m tall; squash it to the walk height so
            // its top is the surface units stand on.
            body.transform.localScale = new Vector3(len / B, WalkHeight / B, Depth(lv) / B);
            string face = lv == 1 ? Wall : lv == 2 ? WallSlit : WallRough;
            Span(face, parent, $"FaceA{tag}", new Vector3(x0, 0f, -half), new Vector3(x1, 0f, -half));
            Span(face, parent, $"FaceB{tag}", new Vector3(x1, 0f, half), new Vector3(x0, 0f, half));

            string edge = lv == 1 ? BattLow : Batt;
            float inset = lv == 1 ? 0.3f : 0.25f;
            Span(edge, parent, $"EdgeA{tag}", new Vector3(x0, M, -half + inset), new Vector3(x1, M, -half + inset));
            Span(edge, parent, $"EdgeB{tag}", new Vector3(x1, M, half - inset), new Vector3(x0, M, half - inset));
            if (lv >= 3)
            {
                Span(Hoard, parent, $"HoardingA{tag}", new Vector3(x1, M, -half), new Vector3(x0, M, -half));
                Span(Hoard, parent, $"HoardingB{tag}", new Vector3(x0, M, half), new Vector3(x1, M, half));
            }
        }

        static GameObject BuildCurtain(int lv)
        {
            var root = new GameObject($"Curtain_Stone_L{lv}");
            WallRun(root.transform, "", -M * 0.5f, M * 0.5f, lv);
            // The module runs along kit X — say so: it is deeper (4 m) than
            // it is long (3 m), so WallModuleArt cannot tell from its shape.
            var along = Child(root.transform, "Along");
            along.localRotation = Quaternion.Euler(0f, 90f, 0f);   // local +Z -> +X
            return root;
        }

        /// <summary>
        /// The hub. Levels 1-2: a low round BASTION exactly as tall as the
        /// walk, so the wall-walk runs straight over it — a junction, not a
        /// tower. Level 3 (Shielded): a round TOWER under a slate cone, the
        /// hub that shoots (docs/Design/Age_1_Alanthor.md § The stone wall).
        /// Round, because a hub joins walls at any bearing and has no front.
        /// </summary>
        static GameObject BuildHub(int lv)
        {
            var root = new GameObject($"Hub_Stone_L{lv}");
            if (lv < 3)
            {
                // A drum a little wider than the 4 m wall, one walk high.
                float diameter = 4.6f / GameScale;
                float s = diameter / (S.Radius * 2f);
                float top = RoundTower(root.transform, "Hub", S, Vector3.zero, storeys: 1, slope: false,
                                       crown: true, floor: true, scale: s, heightScale: WalkHeight / (M * s));
                if (lv == 2)
                {
                    Put(Banner, root.transform, "Stripe_BannerA", new Vector3(0f, top - 1.6f, -S.Radius * s - 0.1f), 180f);
                    Put(Banner, root.transform, "Stripe_BannerB", new Vector3(0f, top - 1.6f, S.Radius * s + 0.1f));
                }
                return root;
            }
            float scale = 5.4f / GameScale / (S.Radius * 2f);
            float towerTop = RoundTower(root.transform, "Hub", S, Vector3.zero, TowerStoreys(scale), slope: false,
                                        crown: true, floor: true, scale: scale);
            ConeOn(root.transform, Vector3.zero, towerTop, S.Radius * scale);
            Put(Banner, root.transform, "Stripe_BannerA",
                new Vector3(0f, towerTop - 2.6f, -S.Radius * scale - 0.1f), 180f);
            Put(Banner, root.transform, "Stripe_BannerB",
                new Vector3(0f, towerTop - 2.6f, S.Radius * scale + 0.1f));
            return root;
        }

        static GameObject BuildGate(int lv)
        {
            var root = new GameObject($"Gate_Stone_L{lv}");
            float half = Depth(lv) * 0.5f;
            // Deep flanks, the open passage between them, gate leaves on BOTH
            // faces (a gate has no inside either), the walk carried over the
            // passage. Flanks + passage are 15 kit metres: fitted to the 9 m
            // gate span that is the shared 0.6 scale.
            WallRun(root.transform, "L", -M * 1.5f, -M * 0.5f, lv);
            WallRun(root.transform, "R", M * 0.5f, M * 1.5f, lv);
            HidePortcullis(Put(Gate, root.transform, "Curtain_Gate", new Vector3(M * 0.5f, 0f, -half)));
            HidePortcullis(Put(Gate, root.transform, "GateB", new Vector3(-M * 0.5f, 0f, half), 180f));
            var deck = Put(FloorSlab, root.transform, "PassageRoof", new Vector3(M * 0.5f, M + 0.25f, half));
            deck.transform.localScale = new Vector3(1f, 1f, Depth(lv) / M);
            string edge = lv == 1 ? BattLow : Batt;
            Span(edge, root.transform, "GateEdgeA",
                 new Vector3(-M * 0.5f, M + 0.45f, -half + 0.25f), new Vector3(M * 0.5f, M + 0.45f, -half + 0.25f));
            Span(edge, root.transform, "GateEdgeB",
                 new Vector3(M * 0.5f, M + 0.45f, half - 0.25f), new Vector3(-M * 0.5f, M + 0.45f, half - 0.25f));
            if (lv >= 3)
            {
                Put(Banner, root.transform, "Stripe_BannerL", new Vector3(-3.2f, M - 1.6f, -half - 0.2f), 180f);
                Put(Banner, root.transform, "Stripe_BannerR", new Vector3(3.2f, M - 1.6f, -half - 0.2f), 180f);
                Put(Banner, root.transform, "Stripe_BannerBL", new Vector3(3.2f, M - 1.6f, half + 0.2f));
                Put(Banner, root.transform, "Stripe_BannerBR", new Vector3(-3.2f, M - 1.6f, half + 0.2f));
            }
            return root;
        }

        /// <summary>A wall module with a round tower standing astride it.</summary>
        static GameObject BuildWallTower(int lv)
        {
            var root = new GameObject($"WallTower_Stone_L{lv}");
            WallRun(root.transform, "", -M * 0.5f, M * 0.5f, lv);
            // The tower is a little wider than the wall it stands in.
            float scale = (Depth(lv) + 1.6f) / (S.Radius * 2f);
            float top = RoundTower(root.transform, "Turret", S, Vector3.zero, storeys: TowerStoreys(scale),
                                   slope: false, crown: true, floor: true, scale: scale);
            if (lv >= 3) ConeOn(root.transform, Vector3.zero, top, S.Radius * scale);
            if (lv >= 2)
            {
                Put(Banner, root.transform, "Stripe_BannerA",
                    new Vector3(0f, top - 2.4f, -S.Radius * scale - 0.1f), 180f);
                Put(Banner, root.transform, "Stripe_BannerB",
                    new Vector3(0f, top - 2.4f, S.Radius * scale + 0.1f));
            }
            return root;
        }

        /// <summary>A solid bastion block the engine stands on, with an empty
        /// "Deck" marker on top; the wall runs through it along Z.</summary>
        static GameObject BuildMount(int lv)
        {
            var root = new GameObject($"Mount_Stone_L{lv}");
            float depth = Mathf.Max(B, Depth(lv) + 1f);
            Put(Block, root.transform, "Curtain_Bastion", Vector3.zero).transform.localScale =
                new Vector3(depth / B, WalkHeight / B, 1f);   // top = the deck the engine stands on
            string parapet = lv == 1 ? BattLow : Batt;
            float h = depth * 0.5f;
            Span(parapet, root.transform, "ParapetFront", new Vector3(h - 0.25f, M, -M * 0.5f), new Vector3(h - 0.25f, M, M * 0.5f));
            Span(parapet, root.transform, "ParapetBack", new Vector3(-h + 0.25f, M, M * 0.5f), new Vector3(-h + 0.25f, M, -M * 0.5f));
            if (lv >= 3)
            {
                Put(Banner, root.transform, "Stripe_BannerA", new Vector3(h + 0.1f, M - 2.2f, 0f), 90f);
                Put(Banner, root.transform, "Stripe_BannerB", new Vector3(-h - 0.1f, M - 2.2f, 0f), -90f);
            }
            var deck = new GameObject("Deck");
            deck.transform.SetParent(root.transform, false);
            deck.transform.localPosition = new Vector3(0f, M, 0f);
            return root;
        }

        // ── Watch tower (Alanthor_Tower) ────────────────────────────────

        static GameObject BuildWatchTower()
        {
            var root = new GameObject("WatchTower");
            var lv0 = Child(root.transform, "Lv0");
            RoundTower(lv0, "Tower", S, Vector3.zero, storeys: 1, slope: true, crown: false, floor: true,
                       scale: 1f, risePrefix: true);

            var alan = Child(root.transform, "Alanthor");
            var l1 = Child(alan, "Lv1");
            RoundTower(l1, "Tower", S, Vector3.zero, storeys: 1, slope: true, crown: true, floor: true, scale: 1f);
            Put(Lamp, l1, "Lamp", new Vector3(0f, M * 1.5f, -S.Radius - 0.05f), 180f);

            var l2 = Child(alan, "Lv2");
            float t2 = RoundTower(l2, "Tower", S, Vector3.zero, storeys: 2, slope: true, crown: true, floor: true, scale: 1f);
            Put(Lamp, l2, "Lamp", new Vector3(0f, M * 1.5f, -S.Radius - 0.05f), 180f);
            Put(Banner, l2, "Stripe_Banner", new Vector3(0f, t2 - 3.2f, -S.Radius - 0.1f), 180f);

            var l3 = Child(alan, "Lv3");
            float t3 = RoundTower(l3, "Tower", S, Vector3.zero, storeys: 3, slope: true, crown: true, floor: true, scale: 1f);
            ConeOn(l3, Vector3.zero, t3, S.Radius);
            Put(Lamp, l3, "Lamp", new Vector3(0f, M * 1.5f, -S.Radius - 0.05f), 180f);
            Put(Banner, l3, "Stripe_BannerA", new Vector3(0f, t3 - 3.2f, -S.Radius - 0.1f), 180f);
            Put(Banner, l3, "Stripe_BannerB", new Vector3(0f, t3 - 3.2f, S.Radius + 0.1f));

            Child(root.transform, "Runai");
            Child(root.transform, "Feraldis");
            return root;
        }

        // ── Fortress ────────────────────────────────────────────────────

        /// <summary>
        /// One SOLID mass, not a courtyard: a broad keep of solid blocks
        /// (3 x 3 = 16.5 kit metres square), round towers fused into its
        /// corners, the gatehouse in its south face, and from L2 an inner keep
        /// two blocks wide rising from the middle of its roof.
        /// </summary>
        static GameObject BuildFortress()
        {
            var root = new GameObject("Fortress");

            // Lv0 — the Age 0 Fortress (and its construction rise).
            var lv0 = Child(root.transform, "Lv0");
            Keep(lv0, "1_", storeys: 1);
            Perimeter(lv0, "2_", FortGrid, height: M);
            GateFace(lv0, "2_");
            CornerTowers(lv0, "3_", storeys: 1, cone: false);

            var alan = Child(root.transform, "Alanthor");

            // Alanthor's keep carries the wall's roofed hoarding gallery round
            // its whole battlement from Lv1, slate cones on its corner towers,
            // and a roofed inner keep from Lv2 (Art_Direction.md: roofs are
            // dark slate; docs/Design/Age_1_Alanthor.md § The wall's art).
            var l1 = Child(alan, "Lv1");
            Keep(l1, "", storeys: 2);
            Perimeter(l1, "", FortGrid, height: 2 * M);
            Galleries(l1, "", FortGrid, height: 2 * M);
            GateFace(l1, "");
            CornerTowers(l1, "", storeys: 2, cone: true);

            var l2 = Child(alan, "Lv2");
            Keep(l2, "", storeys: 2);
            Perimeter(l2, "", FortGrid, height: 2 * M);
            Galleries(l2, "", FortGrid, height: 2 * M);
            GateFace(l2, "");
            InnerKeep(l2, baseY: 2 * M, storeys: 1, roof: true);
            CornerTowers(l2, "", storeys: 2, cone: true);
            GateBanners(l2);

            var l3 = Child(alan, "Lv3");
            Keep(l3, "", storeys: 2);
            Perimeter(l3, "", FortGrid, height: 2 * M);
            Galleries(l3, "", FortGrid, height: 2 * M);
            GateFace(l3, "");
            InnerKeep(l3, baseY: 2 * M, storeys: 2, roof: true, galleries: true);
            CornerTowers(l3, "", storeys: 2, cone: true);
            GateBanners(l3);

            Child(root.transform, "Runai");
            Child(root.transform, "Feraldis");
            return root;
        }

        /// <summary>The main mass: FortGrid x FortGrid solid blocks, storeys high.</summary>
        static void Keep(Transform parent, string prefix, int storeys)
        {
            float o = (FortGrid - 1) * 0.5f * B;
            for (int s = 0; s < storeys; s++)
                for (int i = 0; i < FortGrid; i++)
                    for (int j = 0; j < FortGrid; j++)
                        Put(Block, parent, $"{prefix}Keep_{s}_{i}_{j}", new Vector3(i * B - o, s * M, j * B - o));
        }

        /// <summary>Battlements all round the top edge of a grid x grid block mass.</summary>
        static void Perimeter(Transform parent, string prefix, int grid, float height)
        {
            float e = grid * B * 0.5f - 0.25f;
            var c = new[] { new Vector3(-e, height, e), new Vector3(e, height, e), new Vector3(e, height, -e), new Vector3(-e, height, -e) };
            for (int side = 0; side < 4; side++)
            {
                Vector3 a = c[side], b = c[(side + 1) % 4];
                for (int k = 0; k < grid; k++)
                    Span(Batt, parent, $"{prefix}Batt_{side}_{k}",
                         Vector3.Lerp(a, b, k / (float)grid), Vector3.Lerp(a, b, (k + 1) / (float)grid));
            }
        }

        /// <summary>
        /// The wall's roofed hoarding gallery, hung round the outside of a
        /// grid x grid block mass at <paramref name="height"/> — the same
        /// piece the Shielded wall carries. Laid in the same corner order as
        /// <see cref="Perimeter"/>, so every side overhangs outward.
        /// </summary>
        static void Galleries(Transform parent, string prefix, int grid, float height)
        {
            float e = grid * B * 0.5f;
            var c = new[] { new Vector3(-e, height, e), new Vector3(e, height, e), new Vector3(e, height, -e), new Vector3(-e, height, -e) };
            for (int side = 0; side < 4; side++)
            {
                Vector3 a = c[side], b = c[(side + 1) % 4];
                for (int k = 0; k < grid; k++)
                    Span(Hoard, parent, $"{prefix}Gallery_{side}_{k}",
                         Vector3.Lerp(a, b, k / (float)grid), Vector3.Lerp(a, b, (k + 1) / (float)grid));
            }
        }

        static float FortEdge => FortGrid * B * 0.5f;
        /// <summary>The Fortress's corner towers: the small round tower a
        /// little enlarged — fused into the keep's corners, not standing
        /// apart from it.</summary>
        const float FortTowerScale = 1.25f;

        static void GateFace(Transform parent, string prefix)
            => Put(Gate, parent, $"{prefix}Gatehouse", new Vector3(M * 0.5f, 0f, -FortEdge - 0.3f));

        static void GateBanners(Transform parent)
        {
            Put(Banner, parent, "Stripe_GateBannerL", new Vector3(-3.4f, M + 1.2f, -FortEdge - 0.15f), 180f);
            Put(Banner, parent, "Stripe_GateBannerR", new Vector3(3.4f, M + 1.2f, -FortEdge - 0.15f), 180f);
        }

        /// <summary>Round towers fused into the four corners of the keep,
        /// one storey above its roof.</summary>
        static void CornerTowers(Transform parent, string prefix, int storeys, bool cone)
        {
            float e = FortEdge;
            var corners = new[] { new Vector3(-e, 0, e), new Vector3(e, 0, e), new Vector3(e, 0, -e), new Vector3(-e, 0, -e) };
            for (int i = 0; i < corners.Length; i++)
            {
                float top = RoundTower(parent, $"{prefix}Tower{i}", S, corners[i], storeys, slope: false,
                                       crown: true, floor: true, scale: FortTowerScale, risePrefix: prefix.Length > 0);
                if (cone) ConeOn(parent, corners[i], top, S.Radius * FortTowerScale);
            }
        }

        /// <summary>The inner keep: a 2 x 2 block mass rising from the middle
        /// of the roof — two thirds of the width of the whole — battlemented,
        /// with a slate pyramid roof at the top level.</summary>
        static void InnerKeep(Transform parent, float baseY, int storeys, bool roof, bool galleries = false)
        {
            for (int s = 0; s < storeys; s++)
                for (int i = 0; i < 2; i++)
                    for (int j = 0; j < 2; j++)
                        Put(Block, parent, $"InnerKeep_{s}_{i}_{j}", new Vector3((i - 0.5f) * B, baseY + s * M, (j - 0.5f) * B));
            float top = baseY + storeys * M;
            var batt = Child(parent, "InnerBatt");
            Perimeter(batt, "", grid: 2, height: top);
            if (galleries) Galleries(batt, "Inner", grid: 2, height: top);
            if (roof)
            {
                var r = Put(PyramidRoof, parent, "Spire_InnerKeep", new Vector3(0f, top - 0.2f, 0f));
                float k = (2 * B * 0.92f) / PyramidRoofWidth;
                r.transform.localScale = new Vector3(k, (2 * B * 0.75f) / PyramidRoofHeight, k);
            }
        }

        // ── Round tower builder ─────────────────────────────────────────

        /// <summary>
        /// A round tower centred on <paramref name="centre"/>: four quarter
        /// pieces per storey turned about the centre, an optional flared plinth
        /// (Base_Slope, the first storey's height), a floor cap and an optional
        /// battlement crown. Returns the height of its top (crown included),
        /// in the parent's space.
        /// </summary>
        static float RoundTower(Transform parent, string name, Round r, Vector3 centre, int storeys,
                                bool slope, bool crown, bool floor, float scale, bool risePrefix = false,
                                float heightScale = 1f)
        {
            var root = Child(parent, name);
            root.localPosition = centre;
            // heightScale squashes the tower without narrowing it — a drum
            // as tall as the wall-walk, not a scaled-down tower.
            root.localScale = new Vector3(scale, scale * heightScale, scale);
            float y = 0f;
            int n = 1;
            string Rise(string s) => risePrefix ? $"{n}_{s}" : s;
            if (slope)
            {
                Quarters(root, r.Slope, Rise("Plinth"), r, M);   // spans [-5, 0] from its pivot
                y = M; n++;
            }
            for (int s = 0; s < storeys; s++, n++)
            {
                Quarters(root, r.Quarter, Rise($"Storey{s}"), r, y);
                y += M;
            }
            if (floor) Quarters(root, r.Floor, Rise("Floor"), r, y, floorPiece: true);
            if (crown) Quarters(root, r.Crown, Rise("Crown"), r, y);
            return centre.y + (y + (crown ? 1.38f : 0f)) * scale * heightScale;
        }

        static void Quarters(Transform tower, string piece, string name, Round r, float y, bool floorPiece = false)
        {
            for (int k = 0; k < 4; k++)
            {
                var rot = Quaternion.Euler(0f, k * 90f, 0f);
                // Wall quarters turn about a centre CentreX along -X from their
                // pivot; the round floor's centre IS its pivot.
                Vector3 offset = floorPiece ? Vector3.zero : -(rot * new Vector3(-r.CentreX, 0f, 0f));
                Put(piece, tower, $"{name}_{k}", offset + new Vector3(0f, y, 0f), k * 90f);
            }
        }

        /// <summary>Storeys a wall-set tower needs to stand about one and a
        /// half wall heights tall at this scale.</summary>
        static int TowerStoreys(float scale) => Mathf.Max(1, Mathf.CeilToInt(7.5f / (M * scale)));

        /// <summary>A slate cone roof sized to a tower of radius
        /// <paramref name="radius"/>: as wide as the crown, 2.4 radii tall.</summary>
        static void ConeOn(Transform parent, Vector3 centre, float top, float radius)
        {
            var cone = Put(ConeRoof, parent, "Spire_Cone", new Vector3(centre.x, top - 1.0f, centre.z));
            float k = (radius * 2f * 0.95f) / ConeRoofWidth;
            cone.transform.localScale = new Vector3(k, (radius * 2.4f) / ConeRoofHeight, k);
        }

        // ── Placement helpers ───────────────────────────────────────────

        /// <summary>A wall gate's leaves are the doors that open and close
        /// (WallGateDoors); the kit's portcullis would stay down behind them
        /// and bar an open gate, so it is switched off.</summary>
        static void HidePortcullis(GameObject gate)
        {
            if (gate == null) return;
            foreach (var t in gate.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf("Portcullis", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    t.gameObject.SetActive(false);
        }

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>A kit piece as a nested prefab instance. While the wall set
        /// is being built, the piece is remembered so its meshes are made
        /// Read/Write (the wall bakes them into one mesh).</summary>
        static GameObject Put(string piece, Transform parent, string name, Vector3 pos, float yaw = 0f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + piece + ".prefab");
            if (prefab == null) { Debug.LogError($"[CastleArtAuthor] missing kit piece {piece}"); return null; }
            if (_collectWall) _wallKitPieces.Add(piece);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col, true);
            return go;
        }

        /// <summary>A 5 m kit piece (pivot at its +X end, running to -X) laid
        /// from <paramref name="a"/> to <paramref name="b"/>, stretched to fit.</summary>
        static GameObject Span(string piece, Transform parent, string name, Vector3 a, Vector3 b)
        {
            Vector3 d = (a - b).normalized;   // local -X must point from b back to a
            float yaw = Mathf.Atan2(d.z, -d.x) * Mathf.Rad2Deg;
            var go = Put(piece, parent, name, b, yaw);
            if (go != null)
            {
                float len = Vector3.Distance(a, b);
                if (Mathf.Abs(len - M) > 0.05f)
                    go.transform.localScale = new Vector3(len / M, 1f, 1f);
            }
            return go;
        }

        static GameObject Save(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ── Binding ─────────────────────────────────────────────────────

        static void MakeWallMeshesReadable()
        {
            var models = new HashSet<string>();
            foreach (var piece in _wallKitPieces)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + piece + ".prefab");
                if (prefab == null) continue;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) models.Add(AssetDatabase.GetAssetPath(mf.sharedMesh));
            }
            foreach (var path in models)
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
                {
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                }
            }
        }

        static void BindWallConfig(GameObject[] curtain, GameObject[] hub, GameObject[] gate,
                                   GameObject[] tower, GameObject[] mount)
        {
            const string path = WallDir + "WallModuleArt.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Rendering.WallModuleArtConfig>(path);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<TheWaningBorder.Rendering.WallModuleArtConfig>();
                AssetDatabase.CreateAsset(cfg, path);
            }
            cfg.curtain = curtain;
            cfg.hub = hub;
            cfg.gate = gate;
            cfg.tower = tower;
            cfg.ballistaMount = mount;
            cfg.trebuchetMount = mount;
            EditorUtility.SetDirty(cfg);
        }

        static void BindBuilding(string soPath, GameObject prefab, int? presentationId)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
            if (so == null) { Debug.LogError($"[CastleArtAuthor] no SO at {soPath}"); return; }
            var s = new SerializedObject(so);
            var p = s.FindProperty("prefab");
            if (p != null) p.objectReferenceValue = prefab;
            else Debug.LogError($"[CastleArtAuthor] {soPath} has no 'prefab' field");
            if (presentationId.HasValue)
            {
                var id = s.FindProperty("presentationId");
                if (id != null) id.intValue = presentationId.Value;
            }
            s.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
