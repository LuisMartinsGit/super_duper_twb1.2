// Authors a real visual prefab for every unit and building that still spawned
// as a primitive capsule/cube, as a placeholder-material capsule, through a
// PrefabPaths row whose file no longer exists, or by BORROWING another unit's
// art through a shared presentationId (2026-10-12).
//
// Everything is kit-bashed from art already in the project:
//   - humanoids: prefab VARIANTS of the ExplosiveLLC Warrior Pack characters
//     (the Berserker / Sentinel / Crossbowman / Iconoclast idiom): a tinted
//     copy of the sibling's URP material + a copy of the sibling's animator
//     controller, saved beside the unit's SO.
//   - engines, wagons, buildings: nested instances of Synty POLYGON Fantasy
//     Kingdom prefabs (the WatchTower / Fortress idiom).
//   - cavalry: the procedural OutriderVisual rig BAKED into a prefab (its
//     per-part materials saved as assets) and re-tinted per culture.
// Each prefab is saved in its entity's own folder beside the SO, and the SO's
// prefab / presentationId / animatorController fields are written here, so
// the only C# the pass touches is the PresentationID constants.
//
// Run: Waning Border > Art > Author Missing Unit And Building Prefabs.
// Idempotent — re-running overwrites the prefabs in place (GUIDs survive).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class MissingVisualsAuthor
    {
        const string TT = "Assets/GameData/TechTree/";
        const string SY = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        const string EX = "Assets/Tools/ExplosiveLLC/Warrior Pack Bundle 2 FREE/";
        const string PlayerClothMatPath = "Assets/GameData/Art/Placeholders/PlayerColor_Cloth.mat";
        const string CatapultFxPath = "Assets/Resources/Prefabs/Effects/FX_CatapultShot.prefab";

        // ExplosiveLLC bases + the sibling whose converted material / controller we copy.
        const string Base2H     = EX + "2 Handed Warrior Mecanim Animation Pack/Prefabs/2Handed Warrior.prefab";
        const string BaseArcher = EX + "Archer Warrior Mecanim Animation Pack/Prefabs/Archer Warrior.prefab";
        const string BaseKnight = EX + "Knight Warrior Mecanim Animation Pack/Prefabs/Knight Warrior.prefab";
        const string BaseMage   = EX + "Mage Warrior Mecanim Animation Pack/Prefabs/Mage Warrior.prefab";
        const string Sib2H      = TT + "Civs/Feraldis/Buildings/Longhouse/Units/Berserker/";
        const string SibArcher  = TT + "Civs/Alanthor/Buildings/ArcheryRange/Units/Crossbowman/";
        const string SibKnight  = TT + "Civs/Alanthor/Buildings/Garrison/Units/Sentinel/";
        const string SibMage    = TT + "Civs/Feraldis/Units/Iconoclast/";
        const string WorkerPrefab = TT + "Age0/Buildings/Fortress/Units/Worker/Worker.prefab";
        const string WorkerCtrl   = TT + "Age0/Buildings/Fortress/Units/Worker/Worker.controller";

        static readonly List<string> Report = new List<string>();
        static int _failures;

        [MenuItem("Waning Border/Art/Author Missing Unit And Building Prefabs")]
        public static void AuthorAll()
        {
            Report.Clear(); _failures = 0;
            EnsurePlayerClothMaterial();

            // ───────────── UNITS: humanoid variants ─────────────
            Humanoid("Feraldis_Spearman",      TT + "Civs/Feraldis/Buildings/WarHall/Units/Spearman/",       "Spearman",          BaseKnight, SibKnight, "Sentinel",    new Color(0.60f, 0.48f, 0.40f), 1.00f, 431);
            Humanoid("Feraldis_Bloodletter",   TT + "Civs/Feraldis/Buildings/WarHall/Units/Bloodletter/",    "Bloodletter",       Base2H,     Sib2H,     "Berserker",   new Color(0.80f, 0.28f, 0.26f), 1.00f, 342);
            Humanoid("Feraldis_Suicidal",      TT + "Civs/Feraldis/Buildings/WarHall/Units/Suicidal/",       "Suicidal",          BaseKnight, SibKnight, "Sentinel",    new Color(0.42f, 0.28f, 0.26f), 1.06f, 343);
            Humanoid("Feraldis_Archer",        TT + "Civs/Feraldis/Buildings/HallOfAxes/Units/Archer/",      "Archer",            BaseArcher, SibArcher, "Crossbowman", new Color(0.50f, 0.44f, 0.40f), 1.00f, 428);
            Humanoid("Feraldis_Firethrower",   TT + "Civs/Feraldis/Buildings/HallOfAxes/Units/Firethrower/", "Firethrower",       BaseMage,   SibMage,   "Iconoclast",  new Color(0.95f, 0.58f, 0.28f), 1.00f, 344);
            Humanoid("Feraldis_Overseer",      TT + "Civs/Feraldis/Buildings/Fortress/Units/Overseer/",      "Overseer",          BaseKnight, SibKnight, "Sentinel",    new Color(0.34f, 0.24f, 0.24f), 1.04f, 427);
            Humanoid("Feraldis_HarkonFiend",   TT + "Civs/Feraldis/Buildings/Fortress/Units/HarkonFiend/",   "HarkonFiend",       Base2H,     Sib2H,     "Berserker",   new Color(0.62f, 0.14f, 0.14f), 1.18f, 425);
            WorkerVariant("Feraldis_Slave",    TT + "Civs/Feraldis/Buildings/Fortress/Units/Slave/",         "Slave",             new Color(0.58f, 0.52f, 0.50f), 426);
            Humanoid("Runai_Spearman",         TT + "Civs/Runai/Buildings/ThessarasBazaar/Units/Spearman/",  "Spearman",          BaseKnight, SibKnight, "Sentinel",    new Color(0.96f, 0.86f, 0.66f), 1.00f, 330);
            Humanoid("Sect_ArchivistAdept",    TT + "Sects/Silence/Units/ArchivistAdept/",                   "ArchivistAdept",    BaseMage,   SibMage,   "Iconoclast",  new Color(0.38f, 0.42f, 0.78f), 1.00f, 373);
            Humanoid("Sect_Judicator",         TT + "Sects/Justice/Units/Judicator/",                        "Judicator",         BaseKnight, SibKnight, "Sentinel",    new Color(0.96f, 0.82f, 0.46f), 1.04f, 377);
            Humanoid("Sect_Inquisitor",        TT + "Sects/Justice/Units/Inquisitor/",                       "Inquisitor",        BaseMage,   SibMage,   "Iconoclast",  new Color(0.82f, 0.76f, 0.60f), 1.00f, 406);
            Humanoid("Sect_VaultKeeper",       TT + "Sects/Veneration/Units/VaultKeeper/",                   "VaultKeeper",       BaseKnight, SibKnight, "Sentinel",    new Color(0.92f, 0.92f, 0.98f), 1.04f, 375);
            Humanoid("Sect_GlassmarkArcanist", TT + "Sects/Witness/Units/GlassmarkArcanist/",                "GlassmarkArcanist", BaseMage,   SibMage,   "Iconoclast",  new Color(0.56f, 0.90f, 0.96f), 1.00f, 376);
            Humanoid("Sect_Warbreaker",        TT + "Sects/War/Units/Warbreaker/",                           "Warbreaker",        Base2H,     Sib2H,     "Berserker",   new Color(0.72f, 0.32f, 0.22f), 1.10f, 407);
            Humanoid("Sect_Ashblade",          TT + "Sects/Ash/Units/Ashblade/",                             "Ashblade",          Base2H,     Sib2H,     "Berserker",   new Color(0.52f, 0.46f, 0.42f), 1.00f, 378);
            Humanoid("Sect_Nullblade",         TT + "Sects/Ruin/Units/Nullblade/",                           "Nullblade",         Base2H,     Sib2H,     "Berserker",   new Color(0.48f, 0.32f, 0.64f), 1.00f, 381);
            Humanoid("Sect_Chaincaster",       TT + "Sects/Wrath/Units/Chaincaster/",                        "Chaincaster",       BaseMage,   SibMage,   "Iconoclast",  new Color(0.48f, 0.48f, 0.54f), 1.00f, 380);
            WorkerVariant("Sect_Tinker",       TT + "Sects/Renewal/Units/Tinker/",                           "Tinker",            new Color(0.82f, 0.62f, 0.36f), 405);

            // ───────────── UNITS: engines and wagons ─────────────
            Trebuchet();
            RunaiCatapult();
            CorpseEngine();
            RamRig();
            HarpoonRig();
            WarRig();
            SiegeRam();
            WarChariot();
            BakedRider("Feraldis_Raider", TT + "Civs/Feraldis/Buildings/Pasture/Units/Raider/",       "Raider", new Color(0.30f, 0.16f, 0.14f), 0.55f, 341);
            BakedRider("Runai_Raider",    TT + "Civs/Runai/Buildings/ThessarasBazaar/Units/Raider/",  "Raider", new Color(0.92f, 0.78f, 0.52f), 0.45f, 332);
            Caravan();
            BazaarWagon();

            // ───────────── UNITS: re-points only ─────────────
            WireSo(TT + "Border/Units/Crystalling/Crystalling.asset", Load<GameObject>("Assets/Resources/Prefabs/Border/Units/Crystallings.prefab"), 0, null, "Crystalling");
            WireSo(TT + "Border/Units/Veilstinger/Veilstinger.asset", Load<GameObject>("Assets/Resources/Prefabs/Border/Units/Veilstingers.prefab"), 0, null, "Veilstinger");
            // The Alanthor Catapult already has real art; it only needed a pid of
            // its own (it was spawning on 350 = Runai_Outpost → cube/capsule).
            WireSo(TT + "Civs/Alanthor/Buildings/SiegeYard/Units/Catapult/Catapult.asset", Load<GameObject>(TT + "Civs/Alanthor/Buildings/SiegeYard/Units/Catapult/AlanthorCatapult.prefab"), 420, null, "Alanthor_Catapult");

            // ───────────── BUILDINGS ─────────────
            // Feraldis buildings are NOT authored here any more (2026-10-13):
            // Feraldis shares no art with Alanthor, so the Synty kit-bashes this
            // pass used to produce for the Great Forge, Hall of Axes, Rig Yard,
            // Engine Works, War Totem, Longhouse, Hunting Lodge and Logging
            // Station were replaced by original Blender models
            // (tools/blender/feraldis/, wired by FeraldisArtAuthor). Re-running
            // this pass must never put the kit-bashes back.
            RunaiOutpost();
            RunaiTradeHub();
            ThessarasBazaar();
            RunaiSiegeWorkshop();
            RunaiTradingPost();
            RunaiVault();
            RunaiVeilsteelFoundry();
            AlanthorTradingOutpost();

            // ───────────── SECT BUILDINGS with no SO yet (art only) ─────────────
            HushVault();
            Tribunal();
            ChoirHall();
            GlassSpire();
            AshPyre();
            Ruinworks();
            ChainAltar();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MissingVisuals] done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report));
        }

        // ═════════════════════════════════════════════════════════════════
        // Humanoids
        // ═════════════════════════════════════════════════════════════════

        /// <summary>A tinted prefab VARIANT of an ExplosiveLLC Warrior character.</summary>
        static void Humanoid(string id, string folder, string name, string basePrefab, string sibFolder, string sibName,
                             Color tint, float scale, int pid)
        {
            try
            {
                Directory.CreateDirectory(folder + "Materials");
                var baseGo = Load<GameObject>(basePrefab);
                if (baseGo == null) { Fail(id, "base prefab missing " + basePrefab); return; }

                // Material: copy the sibling's converted URP material, tint it.
                string matPath = folder + "Materials/" + name + ".mat";
                var mat = Load<Material>(matPath);
                if (mat == null)
                {
                    var sibMat = Load<Material>(sibFolder + "Materials/" + sibName + ".mat");
                    if (sibMat != null) { AssetDatabase.CopyAsset(sibFolder + "Materials/" + sibName + ".mat", matPath); }
                    else
                    {
                        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        AssetDatabase.CreateAsset(m, matPath);
                    }
                    mat = Load<Material>(matPath);
                }
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
                EditorUtility.SetDirty(mat);

                // Controller: copy of the sibling's (same clips, same UnitAnimationSync params).
                string ctrlPath = folder + name + ".controller";
                if (Load<RuntimeAnimatorController>(ctrlPath) == null)
                    AssetDatabase.CopyAsset(sibFolder + sibName + ".controller", ctrlPath);
                var ctrl = Load<RuntimeAnimatorController>(ctrlPath);

                var root = (GameObject)PrefabUtility.InstantiatePrefab(baseGo);
                root.name = name;
                root.transform.localScale = Vector3.one * scale;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                }
                var anim = root.GetComponentInChildren<Animator>(true);
                if (anim != null) { anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; }

                var prefab = Save(root, folder + name + ".prefab");
                WireSo(folder + name + ".asset", prefab, pid, ctrl, id);
            }
            catch (Exception e) { Fail(id, e.ToString()); }
        }

        /// <summary>A tinted variant of the Age 0 Worker (MPFB human + Worker.controller) for economy units.</summary>
        static void WorkerVariant(string id, string folder, string name, Color tint, int pid)
        {
            try
            {
                Directory.CreateDirectory(folder + "Materials");
                var baseGo = Load<GameObject>(WorkerPrefab);
                if (baseGo == null) { Fail(id, "Worker prefab missing"); return; }
                var root = (GameObject)PrefabUtility.InstantiatePrefab(baseGo);
                root.name = name;
                var cache = new Dictionary<Material, Material>();
                int n = 0;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var src = mats[i]; if (src == null) continue;
                        if (!cache.TryGetValue(src, out var dst))
                        {
                            string p = folder + "Materials/" + name + (n == 0 ? "" : n.ToString()) + ".mat"; n++;
                            dst = Load<Material>(p);
                            if (dst == null) { dst = new Material(src); AssetDatabase.CreateAsset(dst, p); dst = Load<Material>(p); }
                            else dst.CopyPropertiesFromMaterial(src);
                            if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", dst.GetColor("_BaseColor") * tint);
                            else if (dst.HasProperty("_Color")) dst.SetColor("_Color", dst.GetColor("_Color") * tint);
                            EditorUtility.SetDirty(dst);
                            cache[src] = dst;
                        }
                        mats[i] = dst;
                    }
                    r.sharedMaterials = mats;
                }
                var ctrl = Load<RuntimeAnimatorController>(WorkerCtrl);
                var anim = root.GetComponentInChildren<Animator>(true);
                if (anim != null) { anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; }
                var prefab = Save(root, folder + name + ".prefab");
                WireSo(folder + name + ".asset", prefab, pid, ctrl, id);
            }
            catch (Exception e) { Fail(id, e.ToString()); }
        }

        // ═════════════════════════════════════════════════════════════════
        // Engines and wagons (Synty kit-bashes)
        // ═════════════════════════════════════════════════════════════════

        static void Trebuchet()
        {
            string f = TT + "Civs/Alanthor/Buildings/SiegeYard/Units/Trebuchet/";
            try
            {
                var root = new GameObject("Trebuchet");
                var body = Add(root, "SiegeEngines/SM_Wep_Trebuchet_01.prefab", Vector3.zero, 0, 0.7f);
                var vis = root.AddComponent<TheWaningBorder.Rendering.CatapultVisual>();
                vis.ArmChildName = "SM_Wep_Trebuchet_Arm_01";
                vis.ReleasedAngle = 70f; vis.MuzzleHeight = 3.2f; vis.MuzzleForward = 0.8f;
                vis.ShotFxTemplate = ShotFx(root, new Vector3(0, 3.2f, 0.8f));
                var prefab = Save(root, f + "Trebuchet.prefab");
                WireSo(f + "Trebuchet.asset", prefab, 433, null, "Alanthor_Trebuchet");
                WireSo(TT + "Civs/Alanthor/Buildings/TrebuchetEmplacement/Units/EmplacedTrebuchet/EmplacedTrebuchet.asset", prefab, 433, null, "Alanthor_EmplacedTrebuchet");
            }
            catch (Exception e) { Fail("Alanthor_Trebuchet", e.ToString()); }
        }

        static void RunaiCatapult()
        {
            string f = TT + "Civs/Runai/Buildings/SiegeWorkshop/Units/Catapult/";
            try
            {
                var root = new GameObject("Catapult");
                Add(root, "SiegeEngines/SM_Wep_Catapult_01.prefab", Vector3.zero, 0, 0.7f);
                Add(root, "Props/Banners/SM_Prop_Flag_05.prefab", new Vector3(-0.9f, 0.6f, -1.2f), 0, 0.6f);
                var vis = root.AddComponent<TheWaningBorder.Rendering.CatapultVisual>();
                vis.ArmChildName = "SM_Wep_Catapult_Arm_01";
                vis.ReleasedAngle = 75f; vis.MuzzleHeight = 2.4f; vis.MuzzleForward = 1.2f;
                vis.ShotFxTemplate = ShotFx(root, new Vector3(0, 2.4f, 1.2f));
                var prefab = Save(root, f + "Catapult.prefab");
                WireSo(f + "Catapult.asset", prefab, 337, null, "Runai_Catapult");
            }
            catch (Exception e) { Fail("Runai_Catapult", e.ToString()); }
        }

        static void CorpseEngine()
        {
            string f = TT + "Civs/Feraldis/Buildings/SiegeYard/Units/CorpseEngine/";
            try
            {
                var root = new GameObject("CorpseEngine");
                Add(root, "SiegeEngines/SM_Wep_Catapult_01.prefab", Vector3.zero, 0, 0.7f);
                // The ammunition: a corpse pile on the deck and a caged man slung off the back.
                Add(root, "Props/DeadBodies/SM_Prop_Dead_Body_Pile_01.prefab", new Vector3(0.7f, 0.55f, -0.9f), 35, 0.6f);
                Add(root, "Props/SM_Prop_Cage_Man_01.prefab", new Vector3(-0.8f, 0.55f, -1.1f), 180, 0.55f);
                Add(root, "Props/SM_Prop_Candle_Skull_01.prefab", new Vector3(0.0f, 0.75f, -1.5f), 0, 1.0f);
                Add(root, "Props/SM_Prop_BloodPool_02.prefab", new Vector3(0.3f, 0.02f, -0.6f), 0, 0.8f);
                var vis = root.AddComponent<TheWaningBorder.Rendering.CatapultVisual>();
                vis.ArmChildName = "SM_Wep_Catapult_Arm_01";
                vis.ReleasedAngle = 75f; vis.MuzzleHeight = 2.4f; vis.MuzzleForward = 1.2f;
                vis.ShotFxTemplate = ShotFx(root, new Vector3(0, 2.4f, 1.2f));
                var prefab = Save(root, f + "CorpseEngine.prefab");
                WireSo(f + "CorpseEngine.asset", prefab, 421, null, "Feraldis_CorpseEngine");
            }
            catch (Exception e) { Fail("Feraldis_CorpseEngine", e.ToString()); }
        }

        static void RamRig()
        {
            string f = TT + "Civs/Feraldis/Buildings/SiegeYard/Units/RamRig/";
            try
            {
                var root = new GameObject("RamRig");
                // A fireworks cart IS an explosive on wheels; pile powder barrels on it.
                Add(root, "Vehicles/SM_Veh_Cart_Fireworks_01_Preset.prefab", Vector3.zero, 0, 0.75f);
                Add(root, "Props/SM_Prop_Barrel_Stack_01.prefab", new Vector3(0, 0.55f, -0.3f), 0, 0.55f);
                Add(root, "Props/SM_Prop_Torch_02.prefab", new Vector3(0.55f, 0.6f, -1.0f), 0, 0.8f);
                Add(root, "Props/SM_Prop_Candle_Skull_01.prefab", new Vector3(-0.5f, 0.6f, -1.0f), 0, 1.0f);
                var prefab = Save(root, f + "RamRig.prefab");
                WireSo(f + "RamRig.asset", prefab, 422, null, "Feraldis_RamRig");
            }
            catch (Exception e) { Fail("Feraldis_RamRig", e.ToString()); }
        }

        static void HarpoonRig()
        {
            string f = TT + "Civs/Feraldis/Buildings/SiegeYard/Units/HarpoonRig/";
            try
            {
                var root = new GameObject("HarpoonRig");
                Add(root, "SiegeEngines/SM_Wep_Ballista_Mobile_01.prefab", Vector3.zero, 0, 0.75f);
                // Barbed-wire coils and spare harpoons hung off the frame.
                Add(root, "Props/SM_Prop_Chain_Hanging_02.prefab", new Vector3(0.6f, 1.3f, -0.6f), 0, 0.7f);
                Add(root, "Props/SM_Prop_Chain_Hanging_03.prefab", new Vector3(-0.6f, 1.3f, -0.6f), 0, 0.7f);
                Add(root, "Weapons/SM_Wep_Spear_06.prefab", new Vector3(-0.75f, 0.5f, -0.3f), 0, 0.8f, new Vector3(80, 0, 0));
                Add(root, "Weapons/SM_Wep_Spear_06.prefab", new Vector3(0.75f, 0.5f, -0.3f), 0, 0.8f, new Vector3(80, 0, 0));
                var vis = root.AddComponent<TheWaningBorder.Rendering.CatapultVisual>();
                vis.BoltChildName = "SM_Wep_Ballista_Mobile_Bolt_01";
                vis.ReleasedAngle = 0f; vis.MuzzleHeight = 1.4f; vis.MuzzleForward = 1.2f;
                var prefab = Save(root, f + "HarpoonRig.prefab");
                WireSo(f + "HarpoonRig.asset", prefab, 423, null, "Feraldis_HarpoonRig");
            }
            catch (Exception e) { Fail("Feraldis_HarpoonRig", e.ToString()); }
        }

        static void WarRig()
        {
            string f = TT + "Civs/Feraldis/Buildings/SiegeYard/Units/WarRig/";
            try
            {
                var root = new GameObject("WarRig");
                // A furnace-driven land-ship: an armoured wagon, a cannon turret on the roof,
                // a smokestack, a ram log at the bow and shields along the flanks.
                Add(root, "Vehicles/SM_Veh_TraderWagon_01.prefab", Vector3.zero, 0, 0.9f);
                Add(root, "SiegeEngines/SM_Wep_Cannon_01.prefab", new Vector3(0, 2.35f, 0.3f), 0, 0.55f);
                Add(root, "Buildings/House/SM_Bld_House_Chimney_Stone_01.prefab", new Vector3(0, 2.3f, -1.5f), 0, 0.55f);
                Add(root, "FX/FX_Smoke_Chimney_01.prefab", new Vector3(0, 3.4f, -1.5f), 0, 0.6f);
                Add(root, "SiegeEngines/SM_Wep_Rammer_Alt_Cap_01.prefab", new Vector3(0, 0.9f, 2.4f), 0, 0.6f);
                Add(root, "Weapons/SM_Wep_Shield_05.prefab", new Vector3(1.25f, 1.2f, 0.6f), 90, 0.9f);
                Add(root, "Weapons/SM_Wep_Shield_05.prefab", new Vector3(1.25f, 1.2f, -0.6f), 90, 0.9f);
                Add(root, "Weapons/SM_Wep_Shield_05.prefab", new Vector3(-1.25f, 1.2f, 0.6f), -90, 0.9f);
                Add(root, "Weapons/SM_Wep_Shield_05.prefab", new Vector3(-1.25f, 1.2f, -0.6f), -90, 0.9f);
                Add(root, "Props/Banners/SM_Prop_Battle_Banner_02.prefab", new Vector3(0.9f, 2.2f, -1.9f), 0, 0.6f);
                var prefab = Save(root, f + "WarRig.prefab");
                WireSo(f + "WarRig.asset", prefab, 424, null, "Feraldis_WarRig");
            }
            catch (Exception e) { Fail("Feraldis_WarRig", e.ToString()); }
        }

        static void SiegeRam()
        {
            string f = TT + "Civs/Feraldis/Buildings/SiegeYard/Units/SiegeRam/";
            try
            {
                var root = new GameObject("SiegeRam");
                Add(root, "SiegeEngines/SM_Wep_Rammer_01.prefab", Vector3.zero, 0, 0.7f);
                Add(root, "Props/SM_Prop_Animal_Head_Boar_01.prefab", new Vector3(0, 1.0f, 2.2f), 0, 0.8f);
                var prefab = Save(root, f + "SiegeRam.prefab");
                WireSo(f + "SiegeRam.asset", prefab, 340, null, "Feraldis_SiegeRam");
            }
            catch (Exception e) { Fail("Feraldis_SiegeRam", e.ToString()); }
        }

        static void WarChariot()
        {
            string f = TT + "Civs/Feraldis/Buildings/Pasture/Units/WarChariot/";
            try
            {
                Directory.CreateDirectory(f + "Materials");
                var root = new GameObject("WarChariot");
                // The horse and charioteer are the baked Outrider rig; the chariot
                // is a Synty cart with axe blades on the wheels, hitched behind.
                var rider = BakeOutrider(f + "Materials/", "WarChariot", new Color(0.30f, 0.16f, 0.14f), 0.55f);
                rider.transform.SetParent(root.transform, false);
                rider.transform.localPosition = new Vector3(0, 0, 0.9f);
                var cart = Add(root, "Vehicles/SM_Veh_Cart_02.prefab", new Vector3(0, 0, -1.3f), 0, 0.6f);
                Add(root, "Weapons/SM_Wep_Axe_07.prefab", new Vector3(0.75f, 0.35f, -1.3f), 0, 1.1f, new Vector3(0, 0, 90));
                Add(root, "Weapons/SM_Wep_Axe_07.prefab", new Vector3(-0.75f, 0.35f, -1.3f), 0, 1.1f, new Vector3(0, 0, -90));
                Add(root, "Props/Banners/SM_Prop_Battle_Banner_03.prefab", new Vector3(0, 0.5f, -1.9f), 0, 0.5f);
                Add(root, "Props/SM_Prop_BloodPool_01.prefab", new Vector3(0, 0.02f, -2.2f), 0, 0.6f);
                var prefab = Save(root, f + "WarChariot.prefab");
                WireSo(f + "WarChariot.asset", prefab, 430, null, "Feraldis_WarChariot");
            }
            catch (Exception e) { Fail("Feraldis_WarChariot", e.ToString()); }
        }

        static void BakedRider(string id, string folder, string name, Color tint, float blend, int pid)
        {
            try
            {
                Directory.CreateDirectory(folder + "Materials");
                var root = BakeOutrider(folder + "Materials/", name, tint, blend);
                root.name = name;
                var prefab = Save(root, folder + name + ".prefab");
                WireSo(folder + name + ".asset", prefab, pid, null, id);
            }
            catch (Exception e) { Fail(id, e.ToString()); }
        }

        /// <summary>
        /// Runs the procedural Outrider builder once and turns its in-memory
        /// per-part materials into assets (deduplicated by colour/metal/smooth)
        /// so the rig survives as a prefab. Blends every part toward the tint.
        /// </summary>
        static GameObject BakeOutrider(string matFolder, string name, Color tint, float blend)
        {
            var go = TheWaningBorder.Rendering.OutriderVisual.Build(name.GetHashCode() & 0xffff);
            var byKey = new Dictionary<string, Material>();
            int n = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (m == null) continue;
                    Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                    c = Color.Lerp(c, tint * c.grayscale * 1.6f + tint * 0.2f, blend);
                    float metal = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0;
                    float smooth = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0;
                    string key = $"{c.r:F2},{c.g:F2},{c.b:F2},{metal:F2},{smooth:F2}";
                    if (!byKey.TryGetValue(key, out var asset))
                    {
                        string p = matFolder + name + "_Part" + (n++).ToString("00") + ".mat";
                        asset = Load<Material>(p);
                        if (asset == null) { asset = new Material(m); AssetDatabase.CreateAsset(asset, p); asset = Load<Material>(p); }
                        else asset.CopyPropertiesFromMaterial(m);
                        if (asset.HasProperty("_BaseColor")) asset.SetColor("_BaseColor", c);
                        if (asset.HasProperty("_Color")) asset.SetColor("_Color", c);
                        EditorUtility.SetDirty(asset);
                        byKey[key] = asset;
                    }
                    mats[i] = asset;
                }
                r.sharedMaterials = mats;
            }
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
            return go;
        }

        static void Caravan()
        {
            string f = TT + "Civs/Runai/Units/Caravan/";
            try
            {
                var root = new GameObject("Caravan");
                Add(root, "Vehicles/SM_Veh_TraderWagon_01.prefab", Vector3.zero, 0, 0.7f);
                Add(root, "Props/SM_Prop_Sack_Stack_01.prefab", new Vector3(0, 1.75f, -0.4f), 0, 0.6f);
                Add(root, "Props/Banners/SM_Prop_Flag_09.prefab", new Vector3(0.6f, 1.7f, -1.4f), 0, 0.5f);
                var prefab = Save(root, f + "Caravan.prefab");
                WireSo(f + "Caravan.asset", prefab, 432, null, "Runai_Caravan");
            }
            catch (Exception e) { Fail("Runai_Caravan", e.ToString()); }
        }

        static void BazaarWagon()
        {
            string f = TT + "Civs/Runai/Units/BazaarWagon/";
            try
            {
                var root = new GameObject("BazaarWagon");
                Add(root, "Vehicles/SM_Veh_Cart_03.prefab", Vector3.zero, 0, 0.7f);
                Add(root, "Props/SM_Prop_Market_Tray_01.prefab", new Vector3(0, 0.6f, -0.2f), 0, 0.6f);
                Add(root, "Props/SM_Prop_Awning_03.prefab", new Vector3(0, 1.6f, -0.3f), 0, 0.55f);
                Add(root, "Props/SM_Prop_Rug_02.prefab", new Vector3(0.0f, 0.62f, -0.9f), 0, 0.5f);
                Add(root, "Props/SM_Prop_Pot_Spice_01.prefab", new Vector3(0.4f, 0.62f, 0.3f), 0, 0.7f);
                var prefab = Save(root, f + "BazaarWagon.prefab");
                WireSo(f + "BazaarWagon.asset", prefab, 410, null, "Runai_BazaarWagon");
            }
            catch (Exception e) { Fail("Runai_BazaarWagon", e.ToString()); }
        }

        // ═════════════════════════════════════════════════════════════════
        // Buildings (Synty kit-bashes). Pivot at ground centre; the spawn
        // path snug-fits the whole root to the building's grid footprint, so
        // only proportions matter here. A PlayerColor_Banner child is the
        // named player-colour slot (BuildingFactionColorMarker).
        // ═════════════════════════════════════════════════════════════════

        static void RunaiOutpost()
        {
            string f = TT + "Civs/Runai/Buildings/Outpost/";
            try
            {
                var root = new GameObject("Outpost");
                Add(root, "Buildings/SM_Bld_Wooden_Tower_01.prefab", Vector3.zero, 0, 1f);
                Add(root, "Buildings/SM_Bld_Tent_Small_01.prefab", new Vector3(4.5f, 0, 1.5f), -40, 1f);
                Add(root, "Props/SM_Prop_Fence_03.prefab", new Vector3(-3.5f, 0, 3.5f), 0, 1f);
                Add(root, "Props/SM_Prop_Crate_Stack_01.prefab", new Vector3(-3.0f, 0, -2.5f), 20, 1f);
                Add(root, "Props/SM_Prop_Torch_01.prefab", new Vector3(2.5f, 0, -2.5f), 0, 1f);
                Banner(root, new Vector3(-4.0f, 0, 0.5f), 0);
                var prefab = Save(root, f + "Outpost.prefab");
                WireSo(f + "Outpost.asset", prefab, 350, null, "Runai_Outpost");
            }
            catch (Exception e) { Fail("Runai_Outpost", e.ToString()); }
        }

        static void RunaiTradeHub()
        {
            string f = TT + "Civs/Runai/Buildings/TradeHub/";
            try
            {
                var root = new GameObject("TradeHub");
                Add(root, "Buildings/SM_Bld_Tent_Large_01.prefab", Vector3.zero, 0, 1f);
                Add(root, "Props/SM_Prop_Market_Stall_02.prefab", new Vector3(6.0f, 0, 2.0f), -90, 1f);
                Add(root, "Props/SM_Prop_Market_Stall_05.prefab", new Vector3(-6.0f, 0, 2.0f), 90, 1f);
                Add(root, "Vehicles/SM_Veh_TraderWagon_01.prefab", new Vector3(0, 0, -6.0f), 90, 0.9f);
                Add(root, "Props/SM_Prop_Crate_Stack_03.prefab", new Vector3(3.5f, 0, 5.5f), 0, 1f);
                Add(root, "Props/SM_Prop_Sack_Stack_02.prefab", new Vector3(-3.5f, 0, 5.5f), 0, 1f);
                Add(root, "Props/SM_Prop_Rug_03.prefab", new Vector3(0, 0.02f, 5.0f), 0, 1.2f);
                Banner(root, new Vector3(5.5f, 0, -3.5f), 0);
                var prefab = Save(root, f + "TradeHub.prefab");
                WireSo(f + "TradeHub.asset", prefab, 351, null, "Runai_TradeHub");
            }
            catch (Exception e) { Fail("Runai_TradeHub", e.ToString()); }
        }

        static void ThessarasBazaar()
        {
            string f = TT + "Civs/Runai/Buildings/ThessarasBazaar/";
            try
            {
                var root = new GameObject("ThessarasBazaar");
                Add(root, "Buildings/SM_Bld_Tent_Round_01.prefab", Vector3.zero, 0, 1.1f);
                Add(root, "Buildings/SM_Bld_Tent_Open_01.prefab", new Vector3(6.5f, 0, 0), -90, 1f);
                Add(root, "Props/SM_Prop_Market_Stall_07.prefab", new Vector3(-6.0f, 0, 1.0f), 90, 1f);
                Add(root, "Props/SM_Prop_Market_Stall_09.prefab", new Vector3(0, 0, 6.5f), 180, 1f);
                Add(root, "Props/SM_Prop_Jewellery_Display_01.prefab", new Vector3(6.3f, 0, 0.3f), 0, 1f);
                Add(root, "Props/SM_Prop_Rug_01.prefab", new Vector3(0, 0.02f, -5.5f), 0, 1.2f);
                Add(root, "Props/SM_Prop_Rug_06.prefab", new Vector3(-3.0f, 0.02f, 5.0f), 30, 1f);
                Add(root, "Props/SM_Prop_Pot_01.prefab", new Vector3(3.0f, 0, -5.5f), 0, 1.2f);
                Add(root, "Props/SM_Prop_Bunting_01.prefab", new Vector3(0, 3.2f, 3.5f), 0, 1f);
                Banner(root, new Vector3(-5.5f, 0, -4.5f), 0);
                var prefab = Save(root, f + "ThessarasBazaar.prefab");
                WireSo(f + "ThessarasBazaar.asset", prefab, 352, null, "ThessarasBazaar");
            }
            catch (Exception e) { Fail("ThessarasBazaar", e.ToString()); }
        }

        static void RunaiSiegeWorkshop()
        {
            string f = TT + "Civs/Runai/Buildings/SiegeWorkshop/";
            try
            {
                var root = new GameObject("SiegeWorkshop");
                Add(root, "Buildings/SM_Bld_Tent_Open_02.prefab", Vector3.zero, 0, 1.1f);
                Add(root, "SiegeEngines/SM_Wep_Catapult_01.prefab", new Vector3(5.5f, 0, -1.0f), 30, 0.8f);
                Add(root, "SiegeEngines/SM_Wep_Rock_01.prefab", new Vector3(-5.0f, 0, 3.0f), 0, 1f);
                Add(root, "Props/SM_Prop_Workbench_Forge_01_Preset.prefab", new Vector3(-5.0f, 0, -2.5f), 90, 1f);
                Add(root, "Props/SM_Prop_Ladder_01.prefab", new Vector3(2.0f, 0, 4.5f), 180, 1f, new Vector3(-15, 0, 0));
                Add(root, "Props/SM_Prop_Barrel_Stack_01.prefab", new Vector3(-2.0f, 0, 5.0f), 0, 1f);
                Add(root, "Props/SM_Prop_Beam_05.prefab", new Vector3(3.0f, 0, 5.0f), 10, 1f);
                Banner(root, new Vector3(6.0f, 0, 3.5f), 0);
                var prefab = Save(root, f + "SiegeWorkshop.prefab");
                WireSo(f + "SiegeWorkshop.asset", prefab, 353, null, "Runai_SiegeWorkshop");
            }
            catch (Exception e) { Fail("Runai_SiegeWorkshop", e.ToString()); }
        }

        static void RunaiTradingPost()
        {
            string f = TT + "Civs/Runai/Buildings/TradingPost/";
            try
            {
                var root = new GameObject("TradingPost");
                Add(root, "Props/SM_Prop_Market_Stall_01.prefab", Vector3.zero, 0, 1.2f);
                Add(root, "Buildings/SM_Bld_Tent_Single_01.prefab", new Vector3(-3.5f, 0, -1.0f), 40, 1f);
                Add(root, "Props/SM_Prop_Crate_Stack_02.prefab", new Vector3(3.0f, 0, 1.0f), 0, 1f);
                Add(root, "Props/SM_Prop_Sign_Wood_03.prefab", new Vector3(3.0f, 0, -2.0f), -30, 1f);
                Add(root, "Props/SM_Prop_Sack_01.prefab", new Vector3(-1.5f, 0, 2.5f), 0, 1f);
                Banner(root, new Vector3(2.0f, 0, 3.0f), 0);
                var prefab = Save(root, f + "TradingPost.prefab");
                WireSo(f + "TradingPost.asset", prefab, 355, null, "Runai_TradingPost");
            }
            catch (Exception e) { Fail("Runai_TradingPost", e.ToString()); }
        }

        static void RunaiVault()
        {
            string f = TT + "Civs/Runai/Buildings/Vault/";
            try
            {
                var root = new GameObject("Vault");
                Add(root, "Buildings/SM_Bld_Tent_Square_01.prefab", Vector3.zero, 0, 1f);
                Add(root, "Props/SM_Prop_Crate_Stack_04.prefab", new Vector3(4.5f, 0, 1.5f), 0, 1f);
                Add(root, "Props/SM_Prop_Sack_Stack_01.prefab", new Vector3(-4.5f, 0, 1.5f), 0, 1f);
                Add(root, "Props/SM_Prop_Barrel_Stack_03.prefab", new Vector3(0, 0, 5.0f), 0, 1f);
                Add(root, "Props/SM_Prop_Ore_Gold_01.prefab", new Vector3(3.0f, 0, -4.0f), 0, 1f);
                Banner(root, new Vector3(-4.0f, 0, -4.0f), 0);
                var prefab = Save(root, f + "Vault.prefab");
                WireSo(f + "Vault.asset", prefab, 365, null, "Runai_Vault");
            }
            catch (Exception e) { Fail("Runai_Vault", e.ToString()); }
        }

        static void RunaiVeilsteelFoundry()
        {
            string f = TT + "Civs/Runai/Buildings/VeilsteelFoundry/";
            try
            {
                var root = new GameObject("VeilsteelFoundry");
                Add(root, "Buildings/Presets/SM_Bld_Preset_Blacksmith_01_Optimized.prefab", Vector3.zero, 0, 1f);
                Add(root, "Props/SM_Prop_Forge_01.prefab", new Vector3(5.5f, 0, 1.0f), -90, 1.1f);
                Add(root, "Props/SM_Prop_Anvil_02.prefab", new Vector3(4.0f, 0, -1.0f), 0, 1f);
                Add(root, "Items/SM_Item_Crystal_03.prefab", new Vector3(-5.0f, 0, 2.5f), 0, 1.6f);
                Add(root, "Items/SM_Item_Crystal_05.prefab", new Vector3(-6.0f, 0, 1.0f), 60, 1.3f);
                Add(root, "Props/SM_Prop_Ore_Gem_01.prefab", new Vector3(-4.5f, 0, -2.5f), 0, 1f);
                Add(root, "Buildings/House/SM_Bld_House_Chimney_Stone_01.prefab", new Vector3(0, 3.0f, -4.5f), 0, 1.4f);
                Add(root, "FX/FX_Smoke_Chimney_01.prefab", new Vector3(0, 6.5f, -4.5f), 0, 1f);
                Banner(root, new Vector3(6.5f, 0, 4.5f), 0);
                var prefab = Save(root, f + "VeilsteelFoundry.prefab");
                WireSo(f + "VeilsteelFoundry.asset", prefab, 366, null, "Runai_VeilsteelFoundry");
            }
            catch (Exception e) { Fail("Runai_VeilsteelFoundry", e.ToString()); }
        }

        static void AlanthorTradingOutpost()
        {
            string f = TT + "Civs/Alanthor/Buildings/TradingOutpost/";
            try
            {
                var root = new GameObject("TradingOutpost");
                Add(root, "Buildings/SM_Bld_Tent_Open_01.prefab", Vector3.zero, 0, 1f);
                Add(root, "Props/SM_Prop_Market_Stall_03.prefab", new Vector3(4.0f, 0, 0), -90, 1f);
                Add(root, "Props/SM_Prop_Crate_Stack_01.prefab", new Vector3(-4.0f, 0, 1.0f), 0, 1f);
                Add(root, "Props/SM_Prop_Sign_Wood_08.prefab", new Vector3(-3.5f, 0, -2.5f), 30, 1f);
                Add(root, "Items/SM_Item_Crystal_02.prefab", new Vector3(3.5f, 0, 3.0f), 0, 1.2f);
                Add(root, "Props/SM_Prop_Sack_Stack_01.prefab", new Vector3(0, 0, 3.5f), 0, 1f);
                Banner(root, new Vector3(4.5f, 0, -3.0f), 0);
                var prefab = Save(root, f + "TradingOutpost.prefab");
                WireSo(f + "TradingOutpost.asset", prefab, 523, null, "Alanthor_TradingOutpost");
            }
            catch (Exception e) { Fail("Alanthor_TradingOutpost", e.ToString()); }
        }

        // ───────────── Sect buildings that exist only in the design yet ─────────────

        static void HushVault()
        {
            string f = TT + "Sects/Silence/Buildings/HushVault/";
            try
            {
                var root = new GameObject("HushVault");
                Add(root, "Castle/SM_Bld_Castle_Wall_Tower_S_01.prefab", Vector3.zero, 0, 1f);
                Add(root, "Props/SM_Prop_Gravestone_01.prefab", new Vector3(3.5f, 0, 2.0f), 20, 1f);
                Add(root, "Props/SM_Prop_Gravestone_03.prefab", new Vector3(-3.5f, 0, 2.0f), -20, 1f);
                Add(root, "Props/SM_Prop_Candle_Melted_Group_01.prefab", new Vector3(0, 0, 3.5f), 0, 1.2f);
                Add(root, "Props/SM_Prop_Cross_01.prefab", new Vector3(0, 0, -3.5f), 0, 1f);
                Banner(root, new Vector3(4.0f, 0, -2.5f), 0);
                Save(root, f + "HushVault.prefab");
                Report.Add("OK Sect_HushVault (no SO) -> " + f + "HushVault.prefab");
            }
            catch (Exception e) { Fail("Sect_HushVault", e.ToString()); }
        }

        static void Tribunal()
        {
            string f = TT + "Sects/Justice/Buildings/Tribunal/";
            try
            {
                var root = new GameObject("Tribunal");
                Add(root, "Props/SM_Prop_Stage_01.prefab", Vector3.zero, 0, 1.3f);
                Add(root, "Props/SM_Prop_Church_Lectern_01.prefab", new Vector3(0, 1.0f, -1.0f), 0, 1.2f);
                Add(root, "Props/SM_Prop_Gallows_Single_01.prefab", new Vector3(4.5f, 0, -1.0f), 0, 1f);
                Add(root, "Props/SM_Prop_Throne_Plinth_01.prefab", new Vector3(-4.5f, 0, -1.0f), 0, 1f);
                Add(root, "Props/Banners/SM_Prop_Banner_02.prefab", new Vector3(2.5f, 0, 3.5f), 0, 1f);
                Add(root, "Props/Banners/SM_Prop_Banner_02.prefab", new Vector3(-2.5f, 0, 3.5f), 0, 1f);
                Banner(root, new Vector3(0, 0, 4.0f), 0);
                Save(root, f + "Tribunal.prefab");
                Report.Add("OK Sect_Tribunal (no SO) -> " + f + "Tribunal.prefab");
            }
            catch (Exception e) { Fail("Sect_Tribunal", e.ToString()); }
        }

        static void ChoirHall()
        {
            string f = TT + "Sects/Veneration/Buildings/ChoirHall/";
            try
            {
                var root = new GameObject("ChoirHall");
                Add(root, "Buildings/Presets/SM_Bld_Preset_Church_01_A_Optimized.prefab", Vector3.zero, 0, 1f);
                Add(root, "Props/SM_Prop_Bell_01.prefab", new Vector3(5.5f, 0, 2.0f), 0, 1f);
                Add(root, "Props/SM_Prop_Candle_Melted_Group_01.prefab", new Vector3(-5.5f, 0, 2.0f), 0, 1.2f);
                Banner(root, new Vector3(6.0f, 0, -3.0f), 0);
                Save(root, f + "ChoirHall.prefab");
                Report.Add("OK Sect_ChoirHall (no SO) -> " + f + "ChoirHall.prefab");
            }
            catch (Exception e) { Fail("Sect_ChoirHall", e.ToString()); }
        }

        static void GlassSpire()
        {
            string f = TT + "Sects/Witness/Buildings/GlassSpire/";
            try
            {
                var root = new GameObject("GlassSpire");
                Add(root, "Buildings/House/SM_Bld_House_Tower_Stone_01.prefab", Vector3.zero, 0, 1f);
                Add(root, "Buildings/House/SM_Bld_House_Tower_01.prefab", new Vector3(0, 3.0f, 0), 0, 1f);
                Add(root, "Buildings/House/SM_Bld_House_Tower_Window_01.prefab", new Vector3(0, 6.0f, 0), 0, 1f);
                Add(root, "Buildings/House/SM_Bld_House_Tower_HeightCap_01.prefab", new Vector3(0, 9.0f, 0), 0, 1f);
                Add(root, "Items/SM_Item_Crystal_04.prefab", new Vector3(0, 9.4f, 0), 0, 2.2f);
                Add(root, "Props/SM_Prop_Mirror_01.prefab", new Vector3(2.5f, 0, 2.0f), -40, 1.3f);
                Add(root, "Props/SM_Prop_Mirror_02.prefab", new Vector3(-2.5f, 0, 2.0f), 40, 1.3f);
                Banner(root, new Vector3(3.0f, 0, -2.5f), 0);
                Save(root, f + "GlassSpire.prefab");
                Report.Add("OK Sect_GlassSpire (no SO) -> " + f + "GlassSpire.prefab");
            }
            catch (Exception e) { Fail("Sect_GlassSpire", e.ToString()); }
        }

        static void AshPyre()
        {
            string f = TT + "Sects/Ash/Buildings/AshPyre/";
            try
            {
                var root = new GameObject("AshPyre");
                Add(root, "Props/SM_Prop_FirePit_01.prefab", Vector3.zero, 0, 2.2f);
                Add(root, "Props/SM_Prop_Wood_Pile_02.prefab", new Vector3(3.5f, 0, 1.0f), 0, 1.2f);
                Add(root, "Environments/SM_Env_Tree_Burnt_01.prefab", new Vector3(-4.0f, 0, 2.0f), 0, 0.8f);
                Add(root, "Environments/SM_Env_Tree_Burnt_02.prefab", new Vector3(3.0f, 0, -3.5f), 90, 0.7f);
                Add(root, "Props/SM_Prop_Cauldron_03.prefab", new Vector3(-3.0f, 0, -3.0f), 0, 1.2f);
                Add(root, "Props/SM_Prop_Candle_Skull_01.prefab", new Vector3(1.8f, 0, 2.6f), 0, 1.3f);
                Add(root, "Props/SM_Prop_Candle_Skull_01.prefab", new Vector3(-1.8f, 0, 2.6f), 60, 1.3f);
                Add(root, "Buildings/SM_Bld_Tent_Round_01_Burnt.prefab", new Vector3(0, 0, -5.5f), 180, 0.8f);
                Banner(root, new Vector3(4.5f, 0, 3.5f), 0);
                Save(root, f + "AshPyre.prefab");
                Report.Add("OK Sect_AshPyre (no SO) -> " + f + "AshPyre.prefab");
            }
            catch (Exception e) { Fail("Sect_AshPyre", e.ToString()); }
        }

        static void Ruinworks()
        {
            string f = TT + "Sects/Ruin/Buildings/Ruinworks/";
            try
            {
                var root = new GameObject("Ruinworks");
                Add(root, "SiegeEngines/SM_Wep_Siege_Tower_Fallen_01.prefab", Vector3.zero, 0, 0.8f);
                Add(root, "Props/SM_Prop_Tether_Beam_01.prefab", new Vector3(4.5f, 0, 2.0f), 0, 1f);
                Add(root, "Props/SM_Prop_Tether_Beam_03.prefab", new Vector3(-4.5f, 0, 2.0f), 0, 1f);
                Add(root, "Props/SM_Prop_Ladder_Tied_01.prefab", new Vector3(3.0f, 0, -3.5f), 0, 1f, new Vector3(-20, 0, 0));
                Add(root, "Props/SM_Prop_Grinding_Wheel_01.prefab", new Vector3(-3.0f, 0, -3.5f), 30, 1.2f);
                Add(root, "Weapons/SM_Wep_Hammer_05.prefab", new Vector3(-2.0f, 0.6f, 4.0f), 0, 1.4f, new Vector3(0, 0, 70));
                Add(root, "Props/SM_Prop_Crate_Wood_01.prefab", new Vector3(1.5f, 0, 4.5f), 20, 1f);
                Banner(root, new Vector3(5.0f, 0, -3.0f), 0);
                Save(root, f + "Ruinworks.prefab");
                Report.Add("OK Sect_Ruinworks (no SO) -> " + f + "Ruinworks.prefab");
            }
            catch (Exception e) { Fail("Sect_Ruinworks", e.ToString()); }
        }

        static void ChainAltar()
        {
            string f = TT + "Sects/Wrath/Buildings/ChainAltar/";
            try
            {
                var root = new GameObject("ChainAltar");
                Add(root, "Props/SM_Prop_Throne_Plinth_01.prefab", Vector3.zero, 0, 1.5f);
                Add(root, "Props/SM_Prop_Cross_01.prefab", new Vector3(0, 0.6f, -0.8f), 0, 1.3f);
                Add(root, "Props/SM_Prop_Cage_Man_01.prefab", new Vector3(3.5f, 0, 1.0f), -30, 1f);
                Add(root, "Props/SM_Prop_Cage_01.prefab", new Vector3(-3.5f, 0, 1.0f), 30, 1f);
                Add(root, "Props/SM_Prop_Chain_Hanging_01.prefab", new Vector3(2.0f, 3.0f, -1.0f), 0, 1.2f);
                Add(root, "Props/SM_Prop_Chain_Hanging_02.prefab", new Vector3(-2.0f, 3.0f, -1.0f), 0, 1.2f);
                Add(root, "Props/SM_Prop_Chain_Hanging_03.prefab", new Vector3(0, 3.2f, 1.5f), 0, 1.2f);
                Add(root, "Props/SM_Prop_Candle_Skull_01.prefab", new Vector3(1.5f, 0, 2.8f), 0, 1.3f);
                Add(root, "Props/SM_Prop_Candle_Skull_01.prefab", new Vector3(-1.5f, 0, 2.8f), 90, 1.3f);
                Add(root, "Props/SM_Prop_BloodPool_03.prefab", new Vector3(0, 0.02f, 2.0f), 0, 1.5f);
                Banner(root, new Vector3(4.0f, 0, -2.5f), 0);
                Save(root, f + "ChainAltar.prefab");
                Report.Add("OK Sect_ChainAltar (no SO) -> " + f + "ChainAltar.prefab");
            }
            catch (Exception e) { Fail("Sect_ChainAltar", e.ToString()); }
        }

        // ═════════════════════════════════════════════════════════════════
        // Helpers
        // ═════════════════════════════════════════════════════════════════

        static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path);

        /// <summary>Nested instance of a Synty prefab (path relative to the pack's Prefabs/).</summary>
        static GameObject Add(GameObject parent, string rel, Vector3 pos, float yaw, float scale, Vector3? euler = null, string rename = null)
        {
            var src = Load<GameObject>(SY + rel);
            if (src == null) { Report.Add("   missing part " + rel); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            var e = euler ?? Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(e.x, yaw + e.y, e.z);
            go.transform.localScale = Vector3.one * scale;
            if (rename != null) go.name = rename;
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
            return go;
        }

        /// <summary>The named player-colour slot: a battle banner in a flat material called PlayerColor_Cloth.</summary>
        static void Banner(GameObject parent, Vector3 pos, float yaw)
        {
            var go = Add(parent, "Props/Banners/SM_Prop_Battle_Banner_01.prefab", pos, yaw, 1.1f, null, "PlayerColor_Banner");
            if (go == null) return;
            var cloth = Load<Material>(PlayerClothMatPath);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = cloth;
                r.sharedMaterials = mats;
            }
        }

        static void EnsurePlayerClothMaterial()
        {
            if (Load<Material>(PlayerClothMatPath) != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerClothMatPath));
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PlayerColor_Cloth" };
            m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(m, PlayerClothMatPath);
        }

        /// <summary>The Alanthor catapult's shot effect, as an inactive child the CatapultVisual clones per shot.</summary>
        static GameObject ShotFx(GameObject root, Vector3 pos)
        {
            var src = Load<GameObject>(CatapultFxPath);
            if (src == null) return null;
            var fx = (GameObject)PrefabUtility.InstantiatePrefab(src);
            fx.name = "ShotFx";
            fx.transform.SetParent(root.transform, false);
            fx.transform.localPosition = pos;
            fx.SetActive(false);
            return fx;
        }

        static GameObject Save(GameObject root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            UnityEngine.Object.DestroyImmediate(root);
            if (!ok || asset == null) throw new Exception("SaveAsPrefabAsset failed for " + path);
            return asset;
        }

        /// <summary>Writes prefab / presentationId (when non-zero) / animatorController on a Unit- or BuildingDefSO.</summary>
        static void WireSo(string soPath, GameObject prefab, int pid, RuntimeAnimatorController ctrl, string id)
        {
            var so = Load<ScriptableObject>(soPath);
            if (so == null) { Fail(id, "SO missing " + soPath); return; }
            if (prefab == null) { Fail(id, "prefab null for " + soPath); return; }
            var s = new SerializedObject(so);
            var pPrefab = s.FindProperty("prefab");
            if (pPrefab == null) { Fail(id, "no prefab field on " + soPath); return; }
            pPrefab.objectReferenceValue = prefab;
            var pPid = s.FindProperty("presentationId");
            int finalPid = pPid != null ? pPid.intValue : 0;
            if (pid != 0 && pPid != null) { pPid.intValue = pid; finalPid = pid; }
            var pCtrl = s.FindProperty("animatorController");
            if (pCtrl != null && ctrl != null) pCtrl.objectReferenceValue = ctrl;
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            Report.Add($"OK {id} pid {finalPid} -> {AssetDatabase.GetAssetPath(prefab)}");
        }

        static void Fail(string id, string why)
        {
            _failures++;
            Report.Add("FAIL " + id + ": " + why);
            Debug.LogError("[MissingVisuals] " + id + ": " + why);
        }
    }
}
