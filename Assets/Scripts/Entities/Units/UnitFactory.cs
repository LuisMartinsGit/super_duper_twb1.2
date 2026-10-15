using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Unified factory for creating all unit types.
    ///
    /// Provides a single entry point for spawning units by ID,
    /// with automatic stat loading from TechTreeDB.
    ///
    /// All per-unit data (EM/ECB constructors, UnitClass, PresentationId) lives
    /// in ONE recipe table below — adding a unit means adding ONE entry instead
    /// of editing four parallel switch statements.
    ///
    /// Usage:
    ///   Entity unit = UnitFactory.Create(em, "Archer", position, faction);
    /// </summary>
    public static class UnitFactory
    {
        private readonly struct UnitRecipe
        {
            public readonly Func<EntityManager, float3, Faction, Entity> CreateEm;
            public readonly Func<EntityCommandBuffer, float3, Faction, Entity> CreateEcb;
            public readonly UnitClass Class;
            public readonly int PresentationId;

            public UnitRecipe(Func<EntityManager, float3, Faction, Entity> createEm,
                              Func<EntityCommandBuffer, float3, Faction, Entity> createEcb,
                              UnitClass unitClass, int presentationId)
            {
                CreateEm = createEm;
                CreateEcb = createEcb;
                Class = unitClass;
                PresentationId = presentationId;
            }
        }

        /// <summary>
        /// Single source of truth: id -> (EM ctor, ECB ctor, class, presentation id).
        /// Unknown ids fall back to Swordsman / Melee / 201 (see CreateDefault).
        /// </summary>
        private static readonly Dictionary<string, UnitRecipe> Recipes = BuildRecipes();

        private static Dictionary<string, UnitRecipe> BuildRecipes()
        {
            var r = new Dictionary<string, UnitRecipe>();

            r["Worker"] = new UnitRecipe(Worker.Create, Worker.Create, UnitClass.Economy, 200);

            // "Swordsman" is NOT a recipe id anymore (calculator 2026-08: the
            // Swordsman is Alanthor-only, id "Alanthor_Swordsman") — but the
            // Swordsman creator remains the unknown-id fallback (CreateDefault),
            // so stray "Swordsman" spawns in scenarios still resolve.
            // Age 0 design-canon line unit (Age_0.md): anti-cavalry spear
            // infantry (pid 330 polearm visuals).
            r["Spearman"]    = new UnitRecipe(Spearman.Create, Spearman.Create, UnitClass.Melee, 368);
            // Alanthor_Archer is canonical (2026-08-27 — the Archery Range left the
            // Age 0 roster, so the bare "Archer" id is Alanthor's light archer and
            // nobody else's). "Archer" stays as an alias so existing scenario /
            // bootstrap spawn calls and saved games keep resolving.
            r["Alanthor_Archer"] = r["Archer"] = new UnitRecipe(Archer.Create, Archer.Create, UnitClass.Ranged, 202);
            // Calculator 2026-08: crossbow/longbow are Alanthor Practice Range
            // tiers only. The bare ids stay as aliases of the Alanthor creators
            // for reference stability (old build orders / scenarios).
            r["Crossbowman"] = new UnitRecipe(AlanthorCrossbowman.Create, AlanthorCrossbowman.Create, UnitClass.Ranged, 335);
            r["Longbowman"]  = new UnitRecipe(Longbowman.Create, Longbowman.Create, UnitClass.Ranged, 205);
            r["Scout"]       = new UnitRecipe(Scout.Create, Scout.Create, UnitClass.Scout, 206);
            r["Litharch"]    = new UnitRecipe(Litharch.Create, Litharch.Create, UnitClass.Support, 207);

            var berserker = new UnitRecipe(Berserker.Create, Berserker.Create, UnitClass.Melee, 210);
            r["Berserker"] = r["Feraldis_Berserker"] = berserker;

            // Veilstone border units
            r["Crystalling"]  = new UnitRecipe(Crystalling.Create, Crystalling.Create, UnitClass.Melee, 320);
            r["Veilstinger"]  = new UnitRecipe(Veilstinger.Create, Veilstinger.Create, UnitClass.Ranged, 321);
            r["Godsplinter"]  = new UnitRecipe(Godsplinter.Create, Godsplinter.Create, UnitClass.Siege, 322);

            // Runai culture units
            r["Runai_Spearman"]   = new UnitRecipe(Spearman.Create, Spearman.Create, UnitClass.Melee, 330);
            // The 2026-10-13 redesign (docs/Design/Age_1_Runai.md §6) — RunaiUnits.cs.
            r["Runai_Skirmisher"]    = new UnitRecipe(RunaiSkirmisher.Create, RunaiSkirmisher.Create, UnitClass.Ranged, RunaiSkirmisher.PresentationID);
            r["Runai_Raider"]        = new UnitRecipe(RunaiRaider.Create, RunaiRaider.Create, UnitClass.Melee, RunaiRaider.PresentationID);
            r["Runai_Catapult"]      = new UnitRecipe(RunaiCatapult.Create, RunaiCatapult.Create, UnitClass.Siege, RunaiCatapult.PresentationID);
            r["Runai_CaravanMaster"] = new UnitRecipe(RunaiCaravanMaster.Create, RunaiCaravanMaster.Create, UnitClass.Melee, RunaiCaravanMaster.PresentationID);
            r["Runai_Wayfinder"]     = new UnitRecipe(RunaiWayfinder.Create, RunaiWayfinder.Create, UnitClass.Melee, RunaiWayfinder.PresentationID);
            r["Runai_RoadWarden"]    = new UnitRecipe(RunaiRoadWarden.Create, RunaiRoadWarden.Create, UnitClass.Melee, RunaiRoadWarden.PresentationID);
            r["Runai_Sandstrider"]   = new UnitRecipe(RunaiSandstrider.Create, RunaiSandstrider.Create, UnitClass.Melee, RunaiSandstrider.PresentationID);
            r["Runai_HorseArcher"]   = new UnitRecipe(RunaiHorseArcher.Create, RunaiHorseArcher.Create, UnitClass.Ranged, RunaiHorseArcher.PresentationID);
            r["Runai_CamelLancer"]   = new UnitRecipe(RunaiCamelLancer.Create, RunaiCamelLancer.Create, UnitClass.Melee, RunaiCamelLancer.PresentationID);
            r["Runai_WarWagon"]      = new UnitRecipe(RunaiWarWagon.Create, RunaiWarWagon.Create, UnitClass.Siege, RunaiWarWagon.PresentationID);
            r["Runai_SiegeCaravan"]  = new UnitRecipe(RunaiSiegeCaravan.Create, RunaiSiegeCaravan.Create, UnitClass.Siege, RunaiSiegeCaravan.PresentationID);
            r["Runai_TraderWarrior"] = new UnitRecipe(RunaiTraderWarriorUnit.Create, RunaiTraderWarriorUnit.Create, UnitClass.Melee, RunaiTraderWarriorUnit.PresentationID);
            r["Runai_DuneGuard"]     = new UnitRecipe(RunaiDuneGuard.Create, RunaiDuneGuard.Create, UnitClass.Melee, RunaiDuneGuard.PresentationID);
            r["Runai_Slinger"]       = new UnitRecipe(RunaiSlinger.Create, RunaiSlinger.Create, UnitClass.Ranged, RunaiSlinger.PresentationID);
            r["Runai_SunArcher"]     = new UnitRecipe(RunaiSunArcher.Create, RunaiSunArcher.Create, UnitClass.Ranged, RunaiSunArcher.PresentationID);
            r["Runai_SkyBarge"]      = new UnitRecipe(RunaiSkyBarge.Create, RunaiSkyBarge.Create, UnitClass.Siege, RunaiSkyBarge.PresentationID);
            r["Runai_EscortGuard"]   = new UnitRecipe(RunaiEscortUnits.CreateGuard, RunaiEscortUnits.CreateGuard, UnitClass.Melee, RunaiEscortUnits.GuardPID);
            r["Runai_EscortArcher"]  = new UnitRecipe(RunaiEscortUnits.CreateArcher, RunaiEscortUnits.CreateArcher, UnitClass.Ranged, RunaiEscortUnits.ArcherPID);
            r["Runai_EscortRider"]   = new UnitRecipe(RunaiEscortUnits.CreateRider, RunaiEscortUnits.CreateRider, UnitClass.Melee, RunaiEscortUnits.RiderPID);
            r["Runai_MirageRider"]   = new UnitRecipe(RunaiMirageRider.Create, RunaiMirageRider.Create, UnitClass.Melee, RunaiMirageRider.PresentationID);
            r["Runai_Acolyte"]    = new UnitRecipe(Acolyte.Create, Acolyte.Create, UnitClass.Magic, 384);
            // The caravan: spawned by RunaiTradeSystem at its depot, never trained.
            // Registered so the spawn routes through the factory (UnitTypeId /
            // DisplayName / counter stamps) and the validator stays quiet.
            r["Runai_Caravan"]    = new UnitRecipe(RunaiCaravanUnit.Create, RunaiCaravanUnit.Create, UnitClass.Economy, RunaiCaravanUnit.PresentationID);
            // Packed Bazaar: spawned by BazaarPackSystem, never trained — same
            // arrangement as the Caravan above, registered so the validator
            // stays quiet and any non-pack spawn path still works.
            r["Runai_BazaarWagon"] = new UnitRecipe(BazaarWagon.Create, BazaarWagon.Create, UnitClass.Economy, BazaarWagon.PresentationID);

            // Alanthor culture units
            r["Alanthor_Sentinel"]    = new UnitRecipe(Sentinel.Create, Sentinel.Create, UnitClass.Melee, 334);
            r["Alanthor_Crossbowman"] = new UnitRecipe(AlanthorCrossbowman.Create, AlanthorCrossbowman.Create, UnitClass.Ranged, 335);
            // Garrison Lv 1 line infantry (canonical id; the bare "Swordsman"
            // id was retired — see the fallback note above).
            r["Alanthor_Swordsman"]   = new UnitRecipe(Swordsman.Create, Swordsman.Create, UnitClass.Melee, 201);
            r["Alanthor_Longbowman"]  = new UnitRecipe(Longbowman.Create, Longbowman.Create, UnitClass.Ranged, 205);
            r["Alanthor_Cataphract"]  = new UnitRecipe(Cataphract.Create, Cataphract.Create, UnitClass.Melee, 336);
            r["Alanthor_Outrider"]    = new UnitRecipe(Outrider.Create, Outrider.Create, UnitClass.Melee, Outrider.PresentationID);
            // Siege Yard bolt-thrower: single-target flat bolt, +30 vs Building.
            var ballista              = new UnitRecipe(Ballista.Create, Ballista.Create, UnitClass.Siege, Ballista.PresentationID);
            // Ballista and Catapult are SEPARATE units as of 2026-08-27 — the
            // Siege Yard trains all four of Ballista / Battering Ram / Catapult /
            // Trebuchet. The Catapult is no longer an alias of the Ballista.
            // NOTE the class is AlanthorCatapult: plain `Catapult` is the RUNAI
            // catapult. Same player-facing word, different entity, stats and prefab.
            r["Alanthor_Ballista"] = ballista;
            r["Alanthor_Catapult"] = new UnitRecipe(AlanthorCatapult.Create, AlanthorCatapult.Create, UnitClass.Siege, 420);
            // Garrison Lv 2 elite duelist infantry (calculator 2026-08).
            r["Alanthor_Nobleman"]    = new UnitRecipe(Nobleman.Create, Nobleman.Create, UnitClass.Melee, 346);
            // Siege Yard Lv 2/3 additions (calculator 2026-08): anti-building
            // ram + long-range trebuchet round out the Alanthor siege line.
            r["Alanthor_BatteringRam"] = new UnitRecipe(BatteringRam.Create, BatteringRam.Create, UnitClass.Siege, 347);
            r["Alanthor_Trebuchet"]   = new UnitRecipe(Trebuchet.Create, Trebuchet.Create, UnitClass.Siege, Trebuchet.PresentationID);
            // Alanthor capital units, trained at the Fortress (data-driven
            // abilities; placeholder art).
            r["Ledger"]               = new UnitRecipe(Ledger.Create, Ledger.Create, UnitClass.Economy, Ledger.PresentationID);
            var kingLexor             = new UnitRecipe(KingLexor.Create, KingLexor.Create, UnitClass.Melee, KingLexor.PresentationID);
            r["King Lexor"] = r["KingLexor"] = kingLexor;

            // Feraldis culture units
            // The fire-and-blood combat roster (design 2026-08-05): Spearman
            // at L1, Bloodletter + Suicidal at L2, Berserker at L3. The
            // Spearman gets its OWN creator deliberately — the shared
            // Spearman.Create reads the base "Spearman" def, so routing this
            // id there would have handed Feraldis the cultureless stat block.
            r["Feraldis_Spearman"]     = new UnitRecipe(FeraldisSpearman.Create, FeraldisSpearman.Create, UnitClass.Melee, FeraldisSpearman.PresentationID);
            r["Feraldis_Bloodletter"]  = new UnitRecipe(Bloodletter.Create, Bloodletter.Create, UnitClass.Melee, Bloodletter.PresentationID);
            r["Feraldis_Suicidal"]     = new UnitRecipe(Suicidal.Create, Suicidal.Create, UnitClass.Melee, Suicidal.PresentationID);
            // Thrower Camp roster: range-for-violence ladder.
            r["Feraldis_Archer"]       = new UnitRecipe(FeraldisArcher.Create, FeraldisArcher.Create, UnitClass.Ranged, FeraldisArcher.PresentationID);
            // "Hunter" is the AXE THROWER — id kept for reference stability.
            r["Feraldis_Hunter"]       = new UnitRecipe(Hunter.Create, Hunter.Create, UnitClass.Ranged, 338);
            r["Feraldis_Firethrower"]  = new UnitRecipe(Firethrower.Create, Firethrower.Create, UnitClass.Ranged, Firethrower.PresentationID);
            // Raider Camp output — the Feraldis economy. Free, uncontrollable,
            // never trained by the player (RaiderCampSystem spawns it).
            r["Feraldis_Plunderer"]    = new UnitRecipe(Plunderer.Create, Plunderer.Create, UnitClass.Melee, Plunderer.PresentationID);
            // Pasture roster: light + heavy cavalry.
            r["Feraldis_Raider"]       = new UnitRecipe(FeraldisRaider.Create, FeraldisRaider.Create, UnitClass.Melee, FeraldisRaider.PresentationID);
            r["Feraldis_WarChariot"]   = new UnitRecipe(WarChariot.Create, WarChariot.Create, UnitClass.Melee, WarChariot.PresentationID);
            // RETIRED 2026-08-05 rev.2 — replaced by the War Chariot. Recipe
            // kept so any stray reference still resolves to a real unit.
            r["Feraldis_WarboarRider"] = new UnitRecipe(WarboarRider.Create, WarboarRider.Create, UnitClass.Melee, 339);
            r["Feraldis_SiegeRam"]     = new UnitRecipe(SiegeRam.Create, SiegeRam.Create, UnitClass.Siege, 340);
            r["Feraldis_Iconoclast"]   = new UnitRecipe(Iconoclast.Create, Iconoclast.Create, UnitClass.Melee, 386);
            // The 2026-10-12 redesign (docs/Design/Age_1_Feraldis.md §3, §7-8).
            r["Feraldis_Slave"]        = new UnitRecipe(FeraldisSlave.Create, FeraldisSlave.Create, UnitClass.Economy, FeraldisSlave.PresentationID);
            r["Feraldis_Overseer"]     = new UnitRecipe(FeraldisOverseer.Create, FeraldisOverseer.Create, UnitClass.Melee, FeraldisOverseer.PresentationID);
            r["Feraldis_HarkonFiend"]  = new UnitRecipe(HarkonFiend.Create, HarkonFiend.Create, UnitClass.Melee, HarkonFiend.PresentationID);
            r["Feraldis_RamRig"]       = new UnitRecipe(FeraldisRamRig.Create, FeraldisRamRig.Create, UnitClass.Siege, FeraldisRamRig.PresentationID);
            r["Feraldis_HarpoonRig"]   = new UnitRecipe(FeraldisHarpoonRig.Create, FeraldisHarpoonRig.Create, UnitClass.Siege, FeraldisHarpoonRig.PresentationID);
            r["Feraldis_CorpseEngine"] = new UnitRecipe(FeraldisCorpseEngine.Create, FeraldisCorpseEngine.Create, UnitClass.Siege, FeraldisCorpseEngine.PresentationID);
            r["Feraldis_WarRig"]       = new UnitRecipe(FeraldisWarRig.Create, FeraldisWarRig.Create, UnitClass.Siege, FeraldisWarRig.PresentationID);

            // Sect unique units
            // Alanthor sect units (limit 20) and heroes (unique) — Sects.md §4.
            r["Sect_ScarGuard"]         = new UnitRecipe(ScarGuard.Create, ScarGuard.Create, UnitClass.Melee, ScarGuard.PresentationID);
            r["Sect_Collector"]         = new UnitRecipe(Collector.Create, Collector.Create, UnitClass.Melee, Collector.PresentationID);
            r["Sect_Sallyguard"]        = new UnitRecipe(Sallyguard.Create, Sallyguard.Create, UnitClass.Melee, Sallyguard.PresentationID);
            r["Sect_ArchivistOfHours"]  = new UnitRecipe(ArchivistOfHours.Create, ArchivistOfHours.Create, UnitClass.Ranged, ArchivistOfHours.PresentationID);
            r["Sect_Surgeon"]           = new UnitRecipe(Surgeon.Create, Surgeon.Create, UnitClass.Melee, Surgeon.PresentationID);
            r["Sect_Warden"]            = new UnitRecipe(Warden.Create, Warden.Create, UnitClass.Melee, Warden.PresentationID);
            r["Sect_Guildmaster"]       = new UnitRecipe(Guildmaster.Create, Guildmaster.Create, UnitClass.Melee, Guildmaster.PresentationID);
            r["Sect_ArchivistAdept"]    = new UnitRecipe(ArchivistAdept.Create, ArchivistAdept.Create, UnitClass.Magic, 373);
            r["Sect_FlameWarden"]       = new UnitRecipe(FlameWarden.Create, FlameWarden.Create, UnitClass.Melee, 374);
            r["Sect_VaultKeeper"]       = new UnitRecipe(VaultKeeper.Create, VaultKeeper.Create, UnitClass.Melee, 375);
            r["Sect_GlassmarkArcanist"] = new UnitRecipe(GlassmarkArcanist.Create, GlassmarkArcanist.Create, UnitClass.Magic, 376);
            r["Sect_Judicator"]         = new UnitRecipe(Judicator.Create, Judicator.Create, UnitClass.Melee, 377);
            r["Sect_Ashblade"]          = new UnitRecipe(Ashblade.Create, Ashblade.Create, UnitClass.Melee, 378);
            r["Sect_Brandbreaker"]      = new UnitRecipe(Brandbreaker.Create, Brandbreaker.Create, UnitClass.Siege, 379);
            r["Sect_Chaincaster"]       = new UnitRecipe(Chaincaster.Create, Chaincaster.Create, UnitClass.Magic, 380);
            r["Sect_Nullblade"]         = new UnitRecipe(Nullblade.Create, Nullblade.Create, UnitClass.Melee, 381);
            // Antiquity unit lever (task-063, implemented 2026-07-05).
            r["Sect_Artificer"]         = new UnitRecipe(Artificer.Create, Artificer.Create, UnitClass.Ranged, Artificer.PresentationID);
            // New-roster sect unit levers (playable-sect rollout 2026-07-05):
            // Renewal's Tinker, Justice's Inquisitor, War's Warbreaker —
            // trained at the Temple of Ridan once the sect is adopted.
            r["Sect_Inquisitor"]        = new UnitRecipe(Inquisitor.Create, Inquisitor.Create, UnitClass.Support, Inquisitor.PresentationID);
            r["Sect_Warbreaker"]        = new UnitRecipe(Warbreaker.Create, Warbreaker.Create, UnitClass.Melee, Warbreaker.PresentationID);

            return r;
        }

        /// <summary>
        /// Create a unit by its ID string.
        /// Automatically loads stats from TechTreeDB if available.
        /// </summary>
        /// <param name="em">EntityManager</param>
        /// <param name="unitId">Unit type: "Worker", "Swordsman", "Archer", "Scout", "Litharch"</param>
        /// <param name="position">World position to spawn at</param>
        /// <param name="faction">Faction the unit belongs to</param>
        /// <returns>Created entity</returns>
        public static Entity Create(EntityManager em, string unitId, float3 position, Faction faction)
        {
            Entity entity = Recipes.TryGetValue(unitId, out var recipe)
                ? recipe.CreateEm(em, position, faction)
                : CreateDefault(em, unitId, position, faction);

            // Assign network ID for multiplayer lockstep synchronization
            // Skip for deferred entities (created via ECB wrapper like Litharch)
            if (entity.Index >= 0)
            {
                em.AddComponentData(entity, new NetworkedEntity
                {
                    NetworkId = NetworkIdGenerator.GetNextId(),
                    SpawnTick = NetworkIdGenerator.CurrentTick
                });

                StampDisplayName(em, entity, unitId);

                // Exact requested id — the generic tech-effects engine matches
                // "unit:X" targets against this stamp.
                em.AddComponentData(entity, new UnitTypeId { Value = unitId });

                StampCounterTags(em, entity, unitId);

                // Pre-add the transient combat/movement set DISABLED, so the
                // unit keeps one archetype for life — see TransientState.cs
                // (the 7,782-archetype BlockAllocator crash).
                TransientState.PreAddUnitSet(em, entity);
            }

            return entity;
        }

        /// <summary>
        /// Target-side tags + attacker-side tag bonuses from the SO def,
        /// stamped CENTRALLY so every unit participates in the counter
        /// system (docs/Design/Combat_Pacing.md). A handful of factories
        /// stamp the same values themselves (Spearman, the Border units) —
        /// add-or-set semantics make that a harmless overwrite with the
        /// same data. Before this, only the three Border factories stamped
        /// UnitTagsData at all, so tag bonuses like the Spearman's
        /// +15 vs Cavalry never landed on player cavalry.
        /// </summary>
        private static void StampCounterTags(EntityManager em, Entity entity, string unitId)
        {
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return;

            uint mask = UnitTagParse.Mask(def.tags);
            if (mask != 0)
                em.AddComponentData(entity, new UnitTagsData { Mask = mask });

            var bonus = UnitTagParse.Bonus(def.bonusVsTags);
            if (!bonus.IsEmpty)
                em.AddComponentData(entity, bonus);

            // Target preference (Combat_Pacing.md § Target preference) —
            // the anti-army siege engines' picks, from the SO.
            var pref = UnitTagParse.Preference(def.preferTargets);
            if (!pref.IsEmpty)
                em.AddComponentData(entity, pref);
        }

        /// <summary>
        /// Record the exact name of what was asked for. The selection UI used to
        /// re-derive this from PresentationId, but PIDs select the VISUAL and are
        /// deliberately shared (Outrider/Cataphract both 336, Caravan/Tinker both
        /// 405), so units were mislabeled as each other or fell through to a bare
        /// "Unit". The id the caller passed is unambiguous.
        /// </summary>
        private static void StampDisplayName(EntityManager em, Entity entity, string unitId)
        {
            em.AddComponentData(entity, new DisplayName
            {
                Value = TheWaningBorder.Core.DisplayNames.ForUnitFixed(unitId)
            });
        }

        /// <summary>
        /// Create a unit using EntityCommandBuffer for deferred creation.
        /// Useful when creating units from within system updates.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, string unitId, float3 position, Faction faction)
        {
            Entity entity = Recipes.TryGetValue(unitId, out var recipe)
                ? recipe.CreateEcb(ecb, position, faction)
                : CreateDefault(ecb, unitId, position, faction);

            // Assign network ID for multiplayer lockstep synchronization
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = NetworkIdGenerator.CurrentTick
            });

            ecb.AddComponent(entity, new DisplayName
            {
                Value = TheWaningBorder.Core.DisplayNames.ForUnitFixed(unitId)
            });

            // Exact requested id — the generic tech-effects engine matches
            // "unit:X" targets against this stamp.
            ecb.AddComponent(entity, new UnitTypeId { Value = unitId });

            // Counter-system stamps — same contract as the EM path's
            // StampCounterTags (ECB AddComponent is add-or-set at playback).
            if (TechCatalog.TryGetUnit(unitId, out var def) && def != null)
            {
                uint mask = UnitTagParse.Mask(def.tags);
                if (mask != 0)
                    ecb.AddComponent(entity, new UnitTagsData { Mask = mask });

                var bonus = UnitTagParse.Bonus(def.bonusVsTags);
                if (!bonus.IsEmpty)
                    ecb.AddComponent(entity, bonus);

                var pref = UnitTagParse.Preference(def.preferTargets);
                if (!pref.IsEmpty)
                    ecb.AddComponent(entity, pref);
            }

            // Same transient pre-add as the EM path — see TransientState.cs.
            TransientState.PreAddUnitSet(ecb, entity);

            return entity;
        }

        /// <summary>
        /// Get population cost for a unit type.
        /// Delegates to PopulationHelper as the single source of truth.
        /// </summary>
        public static int GetPopulationCost(string unitId)
        {
            return PopulationHelper.GetUnitPopulationCost(unitId);
        }

        /// <summary>True when a spawn recipe exists for the unit id — the
        /// TechTreeValidator uses this to catch catalog defs that would
        /// spawn the default husk (trainable in UI, broken at spawn).</summary>
        public static bool HasRecipe(string unitId)
            => !string.IsNullOrEmpty(unitId) && Recipes.ContainsKey(unitId);

        /// <summary>
        /// Get the UnitClass for a unit type.
        /// </summary>
        public static UnitClass GetUnitClass(string unitId)
        {
            return Recipes.TryGetValue(unitId, out var recipe) ? recipe.Class : UnitClass.Melee;
        }

        /// <summary>
        /// Get the PresentationId for a unit type.
        /// </summary>
        public static int GetPresentationId(string unitId)
        {
            return Recipes.TryGetValue(unitId, out var recipe) ? recipe.PresentationId : 201;
        }

        /// <summary>
        /// Default unit creation for unknown types.
        /// Falls back to Swordsman stats.
        /// </summary>
        private static Entity CreateDefault(EntityManager em, string unitId, float3 position, Faction faction)
        {
            return Swordsman.Create(em, position, faction);
        }

        private static Entity CreateDefault(EntityCommandBuffer ecb, string unitId, float3 position, Faction faction)
        {
            return Swordsman.Create(ecb, position, faction);
        }
    }
}
