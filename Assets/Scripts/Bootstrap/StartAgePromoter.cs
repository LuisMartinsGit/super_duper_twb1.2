//
// Skirmish lobby "Start Age" pre-promoter. Reads GameSettings.StartAge after
// PlayerSpawnSystem has placed every faction's capital and, for StartAge > 0,
// applies the chosen Alanthor loadout to every faction:
//   • Capital culture stamped to Alanthor and BuildingUpgradeState bumped via
//     BuildingUpgradeSystem.ApplyLevel (same recompute path the in-game
//     L1→L2→L3 upgrade uses, so stats and visuals stay consistent).
//   • Temple of Ridan spawned at a fixed offset from the capital (the
//     Temple has no levels).
//   • The culture's landmark (Vault of Almiérra / Fiendstone Keep) placed
//     at another offset.
//   • FactionEra bumped on the bank (Age N → Era N+1).
//   • Bonus resources stocked via FactionEconomy.Add, scaled by age.
//   • FactionColors and PresentationSpawnSystem refreshed so culture tones
//     show immediately.
//
// AI build-order skipping lives in AIBootstrap.CreateAIBrain — when
// GameSettings.StartAge > 0 each new brain starts with StepIndex past the
// end of its build order so SimpleAISystem enters the maintenance loop
// (continuous training + LaunchAttack) immediately.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Buildings;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Bootstrap
{
    public static class StartAgePromoter
    {

        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_HallTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagLocalTransform;

        #endregion
        // Kept only for its LENGTH: the seeded draw that used to pick from
        // it still runs so the RNG stream is unchanged (see PromoteFaction).
        private static readonly string[] ChoiceBuildings =
        {
            "TempleOfRidan",   // content unused, length kept
            "VaultOfAlmierra",
            "FiendstoneKeep",
        };

        /// <summary>The landmark that ages a faction into this culture;
        /// null for Runai until Thessara's Crossing exists.</summary>
        private static string LandmarkFor(byte culture) =>
            culture == Cultures.Alanthor ? "VaultOfAlmierra"
            : culture == Cultures.Feraldis ? "FiendstoneKeep"
            : null;

        // Fixed offsets from the Hall's centre for the auto-placed buildings.
        // Temple north, choice building south — keeps them inside the cleared
        // spawn area and far enough apart to avoid footprint overlap.
        private static readonly float3 TempleOffset = new(0f, 0f, 18f);
        private static readonly float3 ChoiceOffset = new(0f, 0f, -18f);

        /// <summary>
        /// Barracks. Without it a promoted faction has NO military production:
        /// the 2026-08-07 Age-4 match had all four factions logging
        /// "floor blocked ... deficit 5 x Spearman (trainer missing...)" and
        /// finishing on military 0-2 despite full Era-5 tech and a stocked
        /// bank. Starting at the top of the tech tree with no way to build an
        /// army is not a late-game start, it is a stalemate.
        /// </summary>
        private static readonly float3 BarracksOffset = new(18f, 0f, 0f);

        /// <summary>
        /// Apply <see cref="GameSettings.StartAge"/> to every faction with a
        /// freshly-spawned Hall. No-op when StartAge == Age0. Safe to call
        /// multiple times — the FactionProgress.Culture check at the start
        /// of <see cref="PromoteFaction"/> short-circuits if the Hall is
        /// already cultured. Run AFTER PlayerSpawnSystem.SpawnAllFactions
        /// and BEFORE AIBootstrap.InitializeAIPlayers so AI brains can read
        /// the promoted state on first tick.
        /// </summary>
        public static void PromoteAllFactions()
        {
            if (GameSettings.StartAge == SkirmishStartAge.Age0) return;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            int targetLevel = (int)GameSettings.StartAge; // Age1→L1, Age2→L2, Age3→L3.

            // Snapshot every spawned Hall before mutating — promotion does
            // structural changes (BuildingFactory.Create for Temple/choice)
            // that would invalidate a live SystemAPI query iterator.
            var hallQuery = QC_HallTagFactionTagLocalTransform.Get(em, QT_HallTagFactionTagLocalTransform);
            using var hallEntities = hallQuery.ToEntityArray(Allocator.Temp);
            using var hallFactions = hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hallTransforms = hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Deterministic RNG so the chosen building is reproducible from
            // GameSettings.SpawnSeed (same seed → same choice per faction).
            uint seed = (uint)(GameSettings.SpawnSeed ^ 0xA6E0A6EDu);
            if (seed == 0) seed = 1;
            var rng = new Unity.Mathematics.Random(seed);

            for (int i = 0; i < hallEntities.Length; i++)
            {
                Entity hall = hallEntities[i];
                Faction faction = hallFactions[i].Value;
                float3 hallPos = hallTransforms[i].Position;

                if (faction == Faction.Border) continue; // border has no Hall but defensive

                PromoteFaction(em, faction, hall, hallPos, targetLevel, ref rng);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // PER-FACTION PROMOTION
        // ──────────────────────────────────────────────────────────────────

        private static void PromoteFaction(
            EntityManager em, Faction faction, Entity hall, float3 hallPos,
            int targetLevel, ref Unity.Mathematics.Random rng)
        {
            // Cultures.None would leave the faction at a raised era with none
            // of the buildings or units that era implies, so fall back to the
            // pre-configurable default.
            byte culture = GameSettings.StartCulture == Cultures.None
                ? Cultures.Alanthor
                : GameSettings.StartCulture;

            // Short-circuit if already promoted (defensive — calling twice
            // would double-stock resources and place duplicate Temples).
            if (em.HasComponent<FactionProgress>(hall))
            {
                var fp = em.GetComponentData<FactionProgress>(hall);
                if (fp.Culture != Cultures.None) return;
                fp.Culture = culture;
                em.SetComponentData(hall, fp);
            }

            // Promote the Hall through L1..targetLevel using the same path the
            // in-game upgrade uses — captures base stats once, then applies
            // each level so HP / attack / population scale identically to a
            // player-driven upgrade. (BuildingUpgradeSystem.ApplyLevel reads
            // BuildingUpgradeState.{BaseHpMax, BaseAttackCooldown,
            // BasePopulationProvider} which we stamp here.)
            // Capped at the building ladder's top: the lobby offers ages past
            // the last building level (Age 4 against a max of L3), and an
            // uncapped loop read past the end of the level tables.
            EnsureUpgradeStateBase(em, hall);
            int hallLevel = math.min(targetLevel, TheWaningBorder.Core.Settings.BuildingUpgradeConfig.MaxLevel);
            for (int lvl = 1; lvl <= hallLevel; lvl++)
                BuildingUpgradeSystem.ApplyLevel(em, hall, (byte)lvl);

            // Bump faction era. Age N → Era N+1, mirroring the in-game age-up
            // (initial age-up → Era 2).
            if (FactionEconomy.TryGetBank(em, faction, out var bankEntity))
            {
                if (!em.HasComponent<FactionEra>(bankEntity))
                    em.AddComponentData(bankEntity, new FactionEra { Value = targetLevel + 1 });
                else
                    em.SetComponentData(bankEntity, new FactionEra { Value = targetLevel + 1 });

                FactionReligionPointsHelper.AwardAgeUp(em, faction, newAge: targetLevel + 1);
            }

            // Register culture so visuals/tones use its palette.
            FactionColors.SetFactionCulture(faction, culture);

            // ── The culture side effects a real age-up performs ──────────
            // Reused from AgeUpSystem rather than reimplemented: a promoted
            // faction that skipped these is subtly broken in ways that only
            // show up mid-match. Feraldis is the sharp case — its Workers
            // cannot gather and its Houses provide no population, so without
            // the hut transform and the pop override a "start as Feraldis"
            // faction has an economy that silently does nothing.
            TheWaningBorder.Systems.Work.AgeUpSystem
                .TransformGathererHutsForCulture(em, faction, culture);
            TheWaningBorder.Systems.Work.AgeUpSystem
                .TransformHutsForCulture(em, faction, culture);
            TheWaningBorder.Entities.TradingOutpost.ConvertMinesForCulture(em, faction, culture);
            // The Shelter is the Age 0 form of the capital; a faction that
            // starts in Age 1 has already passed the moment it becomes the
            // Fortress, so it is renamed here too.
            TheWaningBorder.Systems.Work.AgeUpSystem
                .TransformCapitalForCulture(em, hall, culture);

            if (FactionEconomy.TryGetBank(em, faction, out var cultureBank))
            {
                if (culture == Cultures.Runai && !em.HasComponent<RunaiPopOverride>(cultureBank))
                    em.AddComponent<RunaiPopOverride>(cultureBank);
                if (culture == Cultures.Feraldis && !em.HasComponent<FeraldisPopOverride>(cultureBank))
                    em.AddComponent<FeraldisPopOverride>(cultureBank);
            }

            // Spawn the Temple of Ridan so the promoted faction has its
            // religious layer (chapels, Litharch) from the first second.
            BuildingFactory.Create(em, "TempleOfRidan", hallPos + TempleOffset, faction);

            // The landmark that WOULD have aged this faction up (Age_0.md
            // § Age-up by landmark): the culture decides it, it is no longer
            // a random pick. The draw is kept so the seeded stream every
            // later pick reads stays where it was.
            rng.NextInt(0, ChoiceBuildings.Length);
            string chosen = LandmarkFor(culture);
            if (chosen != null)
                BuildingFactory.Create(em, chosen, hallPos + ChoiceOffset, faction);

            // Military production — see BarracksOffset. The Barracks is where
            // FindTrainerForUnit routes the melee line, so this is what makes
            // the AI's army floor reachable at all.
            BuildingFactory.Create(em, "Barracks", hallPos + BarracksOffset, faction);

            // Stock bonus resources proportional to the age (so the player
            // doesn't start Era 4 with an Era 1 economy).
            FactionEconomy.Add(em, faction, ResourceBonusForAge(targetLevel));

            // Refresh culture visuals on every owned building (Hall + new
            // Temple + new choice + the starting workers' tone).
            if (PresentationSpawnSystem.Instance != null)
                PresentationSpawnSystem.Instance.RefreshFactionVisuals(faction);

            // AILogger, not TWBLog: TWBLog is [Conditional("TWB_VERBOSE")] and
            // compiles out, so "what age/culture did this match actually start
            // at?" was unanswerable from a postmortem.
            TheWaningBorder.AI.AILogger.Log(faction, "STARTAGE",
                $"promoted to Age {targetLevel} ({CultureConfig.GetName(culture)}) — " +
                $"Fortress L{hallLevel}, Temple, Era {targetLevel + 1}, " +
                $"Barracks, choice {chosen}");
            TWBLog.Log($"[StartAgePromoter] Faction {faction} promoted to Age {targetLevel} " +
                      $"({CultureConfig.GetName(culture)}). Fortress L{hallLevel}, " +
                      $"Temple, choice: {chosen}");
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Stamp a BuildingUpgradeState with captured base stats if absent.
        /// Mirrors UpgradeBuildingCommandHelper.Execute lines 59-75 — same
        /// base capture path, so subsequent BuildingUpgradeSystem.ApplyLevel
        /// calls scale stats identically to a player-driven upgrade.
        /// </summary>
        private static void EnsureUpgradeStateBase(EntityManager em, Entity building)
        {
            if (em.HasComponent<BuildingUpgradeState>(building)) return;

            int baseHp = em.HasComponent<Health>(building)
                ? em.GetComponentData<Health>(building).Max : 0;
            float baseAtkCd = em.HasComponent<BuildingRangedAttack>(building)
                ? em.GetComponentData<BuildingRangedAttack>(building).Cooldown : 0f;
            int basePop = em.HasComponent<PopulationProvider>(building)
                ? em.GetComponentData<PopulationProvider>(building).Amount : 0;

            em.AddComponentData(building, new BuildingUpgradeState
            {
                Level                  = 0,
                BaseHpMax              = baseHp,
                BaseAttackCooldown     = baseAtkCd,
                BasePopulationProvider = basePop,
            });
        }

        /// <summary>
        /// Resource bonus stocked per age. Hand-tuned so the player has a
        /// sensible economy to support the building level they spawned at —
        /// Age 1 covers a few Huts, Age 2 covers Barracks + Temple
        /// upgrades, Age 3 stocks veilsteel so culture-unique units are
        /// immediately trainable.
        /// </summary>
        private static Cost ResourceBonusForAge(int age) => age switch
        {
            1 => Cost.Of(supplies: 200, iron: 50),
            2 => Cost.Of(supplies: 500, iron: 150, veilstone: 50),
            3 => Cost.Of(supplies: 1000, iron: 300, veilstone: 100, veilsteel: 30),
            // Age 4 opens the ritualists, and both cost 300 supplies + 150
            // iron on top of everything else an Era-5 army wants. Stocked
            // generously on purpose: this option exists to test the LATE game,
            // and starting it broke would just move the grind rather than
            // remove it.
            4 => Cost.Of(supplies: 2500, iron: 800, veilstone: 400, veilsteel: 120),
            _ => default,
        };
    }
}
