// Timer-based age-up system — ticks AgeUpState.Remaining and runs
// completion logic (era set, hall scale, culture effects, RP grant)
// when the timer expires.

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Economy;
using OutpostSites = TheWaningBorder.Entities.TradingOutpost;   // avoids the DC0062 Entities.ForEach misread

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Ticks AgeUpState.Remaining on Hall entities each frame.
    /// When Remaining reaches 0, applies all age-up completion effects:
    ///   1. Set FactionProgress.Culture on the Hall
    ///   3. Set FactionEra to 2 on the faction bank entity
    ///   4. Grant RP if a Temple exists
    ///   5. Per-culture hut transforms (huts always persist; Alanthor huts get
    ///      an OPTIONAL convert choice and remain fully functional/buildable)
    ///   6. Remove AgeUpState component
    ///
    /// NOTE: Not Burst-compiled — accesses managed FactionColors and Debug.Log.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct AgeUpSystem : ISystem
    {
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_GathererHutTagFactionTag =
        {
            ComponentType.ReadOnly<GathererHutTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_GathererHutTagFactionTag;

        #endregion
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AgeUpState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Collect completed halls (structural changes can't happen during iteration)
            var completed = new NativeList<Entity>(Allocator.Temp);

            foreach (var (ageUp, entity) in SystemAPI
                .Query<RefRW<AgeUpState>>()
                .WithAll<HallTag>()
                .WithEntityAccess())
            {
                ageUp.ValueRW.Remaining -= dt;

                if (ageUp.ValueRO.Remaining <= 0f)
                {
                    completed.Add(entity);
                }
            }

            // Process completed age-ups
            for (int i = 0; i < completed.Length; i++)
            {
                Entity hallEntity = completed[i];
                if (!em.Exists(hallEntity)) continue;
                if (!em.HasComponent<AgeUpState>(hallEntity)) continue;

                var ageUpState = em.GetComponentData<AgeUpState>(hallEntity);
                byte culture = ageUpState.Culture;

                // Determine faction
                Faction faction = Faction.Blue;
                if (em.HasComponent<FactionTag>(hallEntity))
                    faction = em.GetComponentData<FactionTag>(hallEntity).Value;

                // 1. Set FactionProgress.Culture on the Hall
                if (em.HasComponent<FactionProgress>(hallEntity))
                {
                    var progress = em.GetComponentData<FactionProgress>(hallEntity);
                    progress.Culture = culture;
                    em.SetComponentData(hallEntity, progress);
                }

                // 2. (Removed 2026-10-20.) The capital used to be scaled 1.3x
                //    here. Its visual is fitted to its build-grid footprint, so
                //    the scale made the Fortress spill 30 % past its own cells at
                //    every age-up. The Shelter -> Fortress change is the art's
                //    Lv0 -> Lv1 branch, not the transform.

                // 2b. THE SHELTER BECOMES THE FORTRESS. Same entity, renamed —
                //     the rule the House and the Guild already follow.
                TransformCapitalForCulture(em, hallEntity, culture);

                // 3. Set FactionEra to 2 and award the Age-2 RP bonus.
                //    task-063: RP economy is sect-adoption-driven now (6/8/10
                //    per age + ⌊leftover/2⌋ carryover).
                //    Temple existence is no longer a gate on the per-age award.
                if (FactionEconomy.TryGetBank(em, faction, out var bankEntity))
                {
                    if (em.HasComponent<FactionEra>(bankEntity))
                        em.SetComponentData(bankEntity, new FactionEra { Value = 2 });

                    FactionReligionPointsHelper.AwardAgeUp(em, faction, newAge: 2);
                }
                UnityEngine.Debug.Log($"[AgeUp] COMPLETE — {faction} era 2, culture {culture}.");

                // 4. Per-culture hut transform (design §1.4 "transform, don't replace").
                //    Phase 1 of task-066 removes the old self-destruct paths; Phases 2-3
                //    add the wall-anchor / wagon-burst / raider-spawn behaviors. For now
                //    the huts simply persist across age-up — no auto-destruction.
                TransformGathererHutsForCulture(em, faction, culture);
                TransformHutsForCulture(em, faction, culture);
                // Alanthor do not mine veilstone: every Veilstone Mine becomes a Trading Outpost
                // (docs/Design/Veilstone_Economy.md §3.1).
                OutpostSites.ConvertMinesForCulture(em, faction, culture);
                // The Runai do not mine it either: their Veilstone Mines become
                // Sanctuaries in place (docs/Design/Age_1_Runai.md §5.1).
                TheWaningBorder.Entities.Sanctuary.ConvertMinesForCulture(em, faction, culture);

                // 4b. Runai: the full population cap at age-up (no Houses — the Hut
                //     is the Waystation, docs/Design/Age_1_Runai.md §2).
                if (culture == Cultures.Runai)
                {
                    if (FactionEconomy.TryGetBank(em, faction, out var runaiBank))
                    {
                        if (!em.HasComponent<RunaiPopOverride>(runaiBank))
                            em.AddComponent<RunaiPopOverride>(runaiBank);
                    }
                }

                // 4c. Feraldis: instant 200-pop override (Houses don't contribute pop;
                //     they become raider-spawn buildings per design §5.1).
                if (culture == Cultures.Feraldis)
                {
                    if (FactionEconomy.TryGetBank(em, faction, out var feraldisBank))
                    {
                        if (!em.HasComponent<FeraldisPopOverride>(feraldisBank))
                            em.AddComponent<FeraldisPopOverride>(feraldisBank);
                    }
                }

                // 5. Register culture with FactionColors (idempotent — may already be set by UI popup)
                FactionColors.SetFactionCulture(faction, culture);

                // 5b. NO wall promotion (2026-10-02). The Alanthor pick
                // unlocks the Stone Wall as a NEW building; the palisades the
                // faction raised in Age 0 are a different building and stay
                // timber (docs/Design/Age_0.md § Palisade). Lv2 and Lv3 are
                // bought at the Wall Hub (TechEffectSystem).

                // 6. Rebuild building visuals with culture tone — on the next
                //    presentation frame, not inside this tick (a visual rebuild
                //    here stalls every lockstep peer).
                PresentationSpawnSystem.RequestFactionRefresh(faction);

                // 7. Remove AgeUpState — age-up is complete
                em.RemoveComponent<AgeUpState>(hallEntity);

                // Post-game chart milestone: culture chosen (Era 2).
                TheWaningBorder.Core.Diagnostics.GameStatsTracker.RecordEvent(
                    faction, TheWaningBorder.Core.Diagnostics.GameEventKind.CultureChosen, culture);

            }

            completed.Dispose();
        }

        /// <summary>
        /// Per-culture transform of faction-owned Gatherer's Huts at age-up.
        /// Alanthor → tag each hut with <see cref="GathererHutAgeUpChoice"/>;
        ///            the player then converts each hut individually into
        ///            either a Wall Hub or a Watch Tower via the action panel
        ///            (task-109 phase 2). Cost / timer canonicalised in
        ///            docs/Design/Age_1_Alanthor.md.
        /// Runai    → caravan-wagon with income decay (task-066 Phase 2 — stub).
        /// Feraldis → persists with income + raider-spawn tag (task-066 Phase 3 — stub).
        /// </summary>
        /// <summary>Public because StartAgePromoter drives it from the
        /// Bootstrap assembly; `internal` only ever worked while every
        /// script compiled into one assembly.</summary>
        public static void TransformGathererHutsForCulture(EntityManager em, Faction faction, byte culture)
        {
            if (culture == Cultures.Alanthor)
            {
                // Tag every faction-owned Gatherer's Hut so the action panel
                // surfaces the Convert-to-Wall-Hub / Convert-to-Watch-Tower
                // choice. Idempotent — already-tagged huts and huts already
                // mid-conversion are skipped (this method also fires from
                // save-load paths in future tasks).
                var query = QC_GathererHutTagFactionTag.Get(em, QT_GathererHutTagFactionTag);
                using var entities = query.ToEntityArray(Allocator.Temp);
                using var tags = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

                for (int i = 0; i < entities.Length; i++)
                {
                    if (tags[i].Value != faction) continue;
                    var e = entities[i];
                    if (em.HasComponent<GathererHutAgeUpChoice>(e)) continue;
                    if (em.HasComponent<GathererHutConverting>(e)) continue;
                    em.AddComponent<GathererHutAgeUpChoice>(e);
                }
            }
            // Feraldis: the Gatherer's Hut stays a supply extractor (the Work
            // Camp), staffed by slaves — the Raider Camp is retired
            // (docs/Design/Age_1_Feraldis.md §2-3, 2026-10-12). Nothing to do.
            // Runai branch is still a stub (task-066 phase 2).
        }

        /// <summary>
        /// Per-culture transform of faction-owned Huts (Houses) at age-up.
        /// Runai    → houses are removed (population comes from RunaiPopOverride).
        /// Alanthor → houses persist (standard pop ladder).
        /// Feraldis → houses become raider-spawn buildings (Phase 3 — task-066).
        /// Phase 1 (task-066): no destruction; behaviors are stubs.
        /// </summary>
        /// <summary>
        /// The Shelter becomes the Fortress (docs/Design/Age_0.md § The
        /// Shelter, 2026-10-03). Every culture's capital is the Fortress after
        /// age-up; there is no King's Court, Town Hall or other cultured HQ.
        ///
        /// This is a RENAME, not a replacement: the entity, its health, its
        /// level, its position and its HallTag / FortressTag all survive, and
        /// its id stays "Fortress" — only the name the player sees changes.
        ///
        /// Idempotent, because StartAgePromoter can drive the same transform
        /// for a match that begins in Age 1 and the age-up path may then run
        /// over the same entity.
        ///
        /// Public because StartAgePromoter drives it from the Bootstrap
        /// assembly; `internal` only ever worked while every script compiled
        /// into one assembly.
        /// </summary>
        public static void TransformCapitalForCulture(EntityManager em, Entity capital, byte culture)
        {
            if (culture == Cultures.None) return;
            if (!em.Exists(capital)) return;
            TheWaningBorder.Entities.Fortress.ApplyAgedName(em, capital);
        }

        /// <summary>Public because StartAgePromoter drives it from the
        /// Bootstrap assembly; `internal` only ever worked while every
        /// script compiled into one assembly.</summary>
        public static void TransformHutsForCulture(EntityManager em, Faction faction, byte culture)
        {
            // FERALDIS: the House becomes the Slave Pens (it sets the slave
            // cap — Feraldis slaves). It spawns nothing: the House raider
            // waves are retired (docs/Design/Age_1_Feraldis.md §2, 2026-10-12).
            // Workers become Slaves in FeraldisCultureRetrofitSystem.

            // RUNAI: every standing House collapses (docs/Design/Age_1_Runai.md
            // §2) — the Runai need no housing (full cap at age-up), and the
            // Hut they build from now on is the Waystation tower. Destroyed as
            // the Delete order does it: health to 0, no one gets kill credit.
            if (culture == Cultures.Runai)
            {
                var q = QC_HutTagFactionTag.Get(em, QT_HutTagFactionTag);
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    if (facs[i].Value != faction || em.HasComponent<GathererHutTag>(e)) continue;
                    if (!em.HasComponent<Health>(e)) continue;
                    if (em.HasComponent<LastDamagedByFaction>(e))
                        em.SetComponentEnabled<LastDamagedByFaction>(e, false);
                    var hp = em.GetComponentData<Health>(e);
                    hp.Value = 0;
                    em.SetComponentData(e, hp);
                }
            }
        }

        static readonly ComponentType[] QT_HutTagFactionTag =
        {
            ComponentType.ReadOnly<HutTag>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_HutTagFactionTag;
    }
}
