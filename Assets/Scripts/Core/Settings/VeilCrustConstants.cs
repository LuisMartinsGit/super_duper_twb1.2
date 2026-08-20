// File: Assets/Scripts/Core/Settings/VeilCrustConstants.cs
// Tuning for the Veil's cellular-automaton FIELD — the single source of truth
// the crystal crust is a view of. These were private consts inside
// VeilFieldSystem; they moved here so the Burst CA job (VeilSpreadJob) and the
// system agree on one set of numbers, and so the "field" knobs live next to
// BorderConstants like every other tunable in the project.
//
// The model, in one breath: each pulse, covered cells raise their neighbours
// (GROW), wells replenish their own core (FEED), and any cell whose nearest
// well is not Active loses coverage (DECAY). A break writes 0 + a COOLDOWN;
// while a cell's cooldown is non-zero the CA refuses to grow it, so a punched
// hole stays open for a beat and then the same spread rule refills it (REGROW,
// no special case). NOISE modulates the grow amount per cell per pulse so the
// front advances unevenly / in bursts instead of as a clean expanding disc.
//
// Namespace matches BorderConstants (TheWaningBorder.Core.Config).
// Location: Assets/Scripts/Core/Settings/VeilCrustConstants.cs

namespace TheWaningBorder.Core.Config
{
    public static class VeilCrustConstants
    {
        // ==================== Master switch ====================
        /// <summary>Kill switch for the whole crystal-crust stack (CA field,
        /// seeded discs, tendril bursts, crystal rendering, crust debuffs and
        /// sheet mining). With it off, the curse's presence is carried by the
        /// influence map alone — InfluenceMapSystem deposits curse influence
        /// around the wells and the TWB/Terrain/Lit curse overlay paints the
        /// ground from it; the well nodes are unaffected either way.</summary>
        public const bool VeilFieldEnabled = true;

        /// <summary>Concept revert (2026-08-03): the Veil is INFLUENCE ONLY —
        /// a spreading ground texture with no physical crust. When false, the
        /// field still spreads and paints the terrain (StepCA / Paint*), but
        /// the crust never blocks nav, never debuffs or catches units, never
        /// infects/kills, never swallows iron, and cannot be dug for
        /// veilstone. Veilstone comes from discrete map deposits instead
        /// (VeilstoneOutcroppingBootstrap, iron-style gathering).</summary>
        public const bool CrustPhysical = false;

        // ==================== Field geometry ====================
        // NOTE: the Veil field is REUSED as-is at its established resolution so
        // mining / debuff / terrain-paint stay calibrated. CellSize is the
        // world units per cell; at 4 m a 512-cell playable map is ~128 cells.
        // These are the knobs to dial finer later (a whole pass on its own,
        // because the byte grow/decay rates below are tuned against 4 m cells).
        public const float CellSize = 4f;
        public const int   MaxCellsPerAxis = 192;

        /// <summary>Seconds between CA pulses (the front lurches once per pulse).</summary>
        public const float PulseInterval = 1f;

        // ==================== Spread rates (bytes / pulse) ====================
        /// <summary>At/above this a cell counts as SOLID crust and seeds growth
        /// in its neighbours.</summary>
        public const byte SolidThreshold = 170;

        /// <summary>Coverage an Active well pumps into its own core each pulse.</summary>
        public const byte FeedPerTick = 6;
        /// <summary>Radius (m) of a well's self-feeding core.</summary>
        public const float FeedRadius = 10f;

        /// <summary>Coverage a starved cell (nearest well not Active) loses per pulse.</summary>
        public const byte DecayPerTick = 6;
        /// <summary>Coverage a SUPPRESSED cell (player influence / hearth /
        /// cleanse aura ground) loses per pulse — 2026-08-04 readability
        /// pass: pushing the curse back must be VISIBLE (~18 s per cell from
        /// full), not a 40-second crawl.</summary>
        public const byte SuppressDecayPerTick = 14;
        /// <summary>A DESTROYED well's crust COLLAPSES violently (2026-08-04
        /// playtest: at 1/pulse the crust read as "never goes back" — full
        /// saturation took 4+ minutes per cell to clear). ~25 s from solid
        /// to clean; the drama is the payoff, and the well's death also
        /// bursts a veilstone loot ring (NodeStateDeathInterceptSystem).
        /// The RETURN is the violent half of escalation — the heartbeat's
        /// shrinking dormant windows make every regrowth harder than the
        /// last.</summary>
        public const byte DestroyedDecayPerTick = 10;

        /// <summary>A Cleansed (Purified) well clamps coverage to zero within
        /// this radius (m) — sanctified ground.</summary>
        public const float SanctifyRadius = 18f;

        // Sustain tether (2026-08-04, "the curse still does not recede"): an
        // ACTIVE feeder holds crust only within this radius of itself. Beyond
        // it crust starves at DecayPerTick even while the well lives, and
        // tendrils refuse to extend — the curse is a territory anchored on
        // its wells and pockets, not one-way map paint. Kill the feeder and
        // everything it held collapses (DestroyedDecayPerTick). The reach
        // grows along the escalation ramp, so the late-game curse projects
        // further than the early one — and has further to fall.
        public const float SustainRadiusBase = 55f;
        public const float SustainRadiusEscalated = 85f;

        // ==================== Tendril heartbeat ====================
        // The front does NOT creep continuously. It holds still for a random
        // dormant window, then throws out fast-growing crystal TENDRILS over a
        // short burst, then goes still again — a breathing curse on a clock.
        // Timing is deterministic (seeded RNG + fixed dt), never wall-clock.

        // TIMING NOTE: tuned so an un-mined map takes ~1 hour to fully crust.
        // Fill time scales ~linearly with the dormant window — after a test,
        // scale DormantMin/Max by (60 / observed_minutes) to fine-calibrate to
        // your map size + well count (which set the distance to cover).
        //
        // RECALIBRATED 2026-08-07 (120/200 → 190/320). The 4-AI match logged
        // 11.3 % coverage at 00:51 and 91.9 % at 38:00, i.e. the map saturated
        // in ~38 minutes against a 60-minute target — the front was running
        // ~1.6x too fast, so the windows are scaled by 60/38 ≈ 1.58.
        //
        // Why that mattered more than the number suggests: the curse's effect
        // on the economy is a CLIFF, not a slope. Miners auto-flee crust at
        // ExposureFleeSeconds (3 s), so while there is clean ground the tax is
        // tempo, and the moment there isn't, income is exactly zero. In that
        // match three of four factions hit `emaS=0.0/s emaI=0.0/s` between
        // minutes 14 and 20 and never recovered — 30+ minutes of standing
        // still with buildings intact and nothing to do. Reaching the cliff at
        // ~60 min instead of ~38 leaves the mid-game intact.
        //
        // Escalation (EscalationRampSeconds / EscalationFloor) is deliberately
        // NOT touched in the same pass — it was doing exactly what it says on
        // top of a base rate that was already overshooting, and changing two
        // multipliers at once makes the next playtest unattributable.
        //
        // ⚠ BASIS SUPERSEDED SAME DAY by well dormancy (canon §2.8). The 38-min
        // measurement above was taken with EVERY well feeding from minute 0,
        // which no longer happens: wells now enter play dormant and wake one at
        // a time as players verb them, so the early map does not creep at all
        // and the awakened map has far fewer feeders than the one that was
        // measured. The 1.58x slowdown therefore now applies only to the LATE
        // phase — the phase that is *supposed* to bite, and that the player
        // chose by touching a well.
        //
        // RECOMMENDATION (open, needs a call): revert to 120/200. Dormancy is a
        // strictly stronger version of the same fix — it removes the early
        // curse outright rather than slowing it — and stacking the rate nerf on
        // top only weakens the one phase that should be dangerous. Kept at
        // 190/320 for now so the change is a deliberate decision rather than a
        // side effect. See docs/Reports/AI_Match_Postmortem_2026-08-07.md §8.
        /// <summary>Min/max seconds the front sits still between bursts (random each cycle).</summary>
        public const float DormantMinSeconds = 190f;
        public const float DormantMaxSeconds = 320f;

        /// <summary>Length of the tendril burst. Reach ≈ BurstDuration / substep.</summary>
        public const float BurstDurationSeconds = 1.2f;
        /// <summary>How often a tendril extends one cell during a burst.
        /// 1.2 s / 0.3 s ≈ 4 substeps → ~4-cell (≈16 m) fingers.</summary>
        public const float BurstSubstepSeconds = 0.3f;

        /// <summary>HYBRID ramp: the "early" tendrils (top slice by noise) begin
        /// this many seconds before the main burst, so the front ramps up rather
        /// than snapping out all at once.</summary>
        public const float EarlyLeadSeconds = 0.4f;

        // Tendril site selection. A frontier TIP cell (touching solid, but not
        // buried in it) extends only if its per-cycle spatial noise passes the
        // threshold — that sparse, coherent selection is what makes fingers
        // instead of a uniform advancing wall. The noise reseeds each cycle so
        // tendrils appear in different places over time.
        /// <summary>Spatial frequency of the tendril-site noise (cycles per cell).</summary>
        public const float TendrilNoiseFrequency = 0.22f;
        /// <summary>Noise cutoff for a cell to be a tendril path (higher = sparser fingers).</summary>
        public const float TendrilThreshold = 0.55f;
        /// <summary>Higher cutoff picking the ~top-20% "early" tendrils for the hybrid ramp.</summary>
        public const float EarlyTendrilThreshold = 0.78f;
        /// <summary>A cell only extends if it has between 1 and this many solid
        /// neighbours — i.e. it's a thin protrusion tip. Cells buried in the
        /// front (3-4 solid neighbours) never grow, so the mass advances as
        /// fingers, not as a thickening slab.</summary>
        public const int TipMaxSolidNeighbors = 2;

        // ==================== Break + regrow ====================
        /// <summary>Default world radius (m) a player/debug break clears.</summary>
        public const float DefaultBreakRadius = 8f;

        /// <summary>Pulses a broken cell stays locked (no grow) before the
        /// spread rule is allowed to refill it. At PulseInterval = 1 s this is
        /// the regrow delay in seconds.</summary>
        public const byte BreakCooldownPulses = 18;

        // ==================== Classification / chunking (Phase 2) ====================
        /// <summary>Cells per chunk edge (a chunk is ChunkSize x ChunkSize cells).
        /// Small enough that a single rebake stays cheap.</summary>
        public const int ChunkSize = 32;

        /// <summary>A crust cell within this many cells (Chebyshev) of clean
        /// ground is FRONTIER (the interactive band); deeper crust is INTERIOR.
        /// 2 keeps the band 1-2 cells deep so breaking the outer ring never
        /// exposes a bare gap for a frame.</summary>
        public const int FrontierBandDepth = 2;

        // ==================== Influence interaction (§2.6) ====================
        /// <summary>Normalised influence strength (0..1) a cell needs for a
        /// culture's field to act on it — matches the "is MY influence ≥ 0.5
        /// here?" rule the rest of the game uses.</summary>
        public const float InfluenceThreshold = 0.5f;

        /// <summary>§2.5b escalation (2026-08-04): curse influence deposits
        /// grow by this fraction per minute of match time. VERY small on
        /// purpose — a thin "just enough" influence rim that held at minute
        /// 10 is overrun by minute 30, while anchored cores (towers, dense
        /// building clusters) keep winning. Applies to every curse deposit
        /// source (nodes, creatures, the crust footprint).</summary>
        public const float CurseInfluenceGrowthPerMinute = 0.006f; // 2026-08-04: 0.015 read as "never recedes"

        // Per-cell influence effect codes fed to the CA (VeilField._influence).
        public const byte InfluenceNone = 0;
        /// <summary>Alanthor/Runai: curse can't grow here AND existing crust
        /// decays (curse-immune safe zone / reclaim).</summary>
        public const byte InfluenceSuppress = 1;
        // (Feraldis "corrupt" = 2, added when Feraldis is implemented.)

        // ==================== Worker ward ====================
        /// <summary>The veil never GROWS into cells within this world radius of
        /// a worker (MinerTag). Diggers at the face can't be enveloped by a
        /// burst and sealed inside the wall. Existing crust/haze is unaffected
        /// (infection still ticks); military units get no ward — the wall
        /// catches them (see catch-conversion). 2 cells wide, comfortably
        /// covering worker drift between ward refreshes.</summary>
        public const float WorkerWardRadius = 8f;

        // ==================== Miner infection ====================
        // Neglect a miner digging at the veil edge and the curse takes root:
        // after a sustained exposure it erupts into a hostile curse creature.
        /// <summary>Veil saturation at/above which a miner's cell counts as
        /// "near the curse" for infection. Below CrustThreshold (80) because the
        /// crust is impassable now — miners dig from the HAZE just outside it,
        /// so infection reads that haze, not the solid crust they can't stand on.</summary>
        public const byte InfectionNearThreshold = 30;
        /// <summary>Cumulative seconds of haze exposure before a miner turns.</summary>
        public const float InfectionSeconds = 120f;
        /// <summary>Recovery rate multiplier while a miner is clear of haze
        /// (× the exposure step). 1 = sheds a full charge in the same 2 min it
        /// took to build — walking away in time saves the miner.</summary>
        public const float InfectionRecoverMul = 1f;
        /// <summary>Match-elapsed seconds below which an eruption is a Crystalling
        /// (early game). Between this and <see cref="InfectionMidMaxSeconds"/> it
        /// is a Veilstinger; beyond, a Godsplinter (the late-game terror).</summary>
        public const double InfectionEarlyMaxSeconds = 900.0;  // 15 min
        public const double InfectionMidMaxSeconds = 1800.0;   // 30 min

        // The GPU-instanced crystal-mesh renderer (and its constants) was
        // removed 2026-07-25 — the Veil's only visual body is now the terrain
        // overlay driven through the influence mask (InfluenceMaskTexture +
        // TWB/Terrain/Lit).

        // ==================== §2.5b Exposure model (2026-08-03 rev.2) ====================
        // The curse as HOSTILE GROUND: walkable, but it taxes you. Threatens
        // map control (travel cost, exposure damage, building crumble), never
        // insta-kills, and pays veilstone wherever it touches ground
        // (precipitation). Mutually exclusive with CrustPhysical.

        /// <summary>Master switch for the hostile-ground layer: exposure DOT,
        /// stat debuff on crust, and building crumble (VeilExposureSystem).</summary>
        public const bool ExposureEnabled = true;
        /// <summary>Crusted cells stamp a FINITE, saturation-scaled nav cost
        /// (VeilNavStampSystem) — pathing prefers clean ground, cuts through
        /// when worth it. (CrustPhysical=true overrides this with the old
        /// impassable wall.)</summary>
        public const bool TravelCostEnabled = true;

        // Exposure DOT: units on crust accrue exposure seconds; damage starts
        // only after the grace window, so crossing a finger is free and thin
        // base-ring haze essentially cannot kill a worker.
        public const float ExposureGraceSeconds = 5f;
        /// <summary>Off-crust recovery rate (× accrual) — leave and you shed
        /// exposure twice as fast as you gained it.</summary>
        public const float ExposureRecoverMul = 2f;
        /// <summary>Workers (MinerTag) auto-flee toward their nearest Hall
        /// once their exposure crosses this — BEFORE the damage grace ends,
        /// so an unattended worker never dies to haze (the §2.5b promise:
        /// early neglect costs tempo, not corpses).</summary>
        public const float ExposureFleeSeconds = 3f;
        /// <summary>Damage/s at exactly CrustThreshold saturation…</summary>
        public const float ExposureDpsMin = 1f;
        /// <summary>…scaling linearly to this at full (255) saturation.</summary>
        public const float ExposureDpsMax = 6f;

        // Travel cost (nav cost-field byte units; clean ground ≈ 0, impassable
        // = 255). Deep crust is a SOFT wall.
        public const byte TravelCostMin = 24;
        public const byte TravelCostMax = 120;

        /// <summary>Damage/s to a completed building standing in DEEP crust
        /// (engulfed by later growth) — loud and slow, savable by reclaiming
        /// the ground. 8/s ≈ 5 min to crumble a 2 400 HP Hall.</summary>
        public const float CrumbleDps = 8f;

        // Escalation: the heartbeat's dormant windows shrink over match time —
        // an Age 0 nuisance becomes a late-game terrain force.
        public const float EscalationRampSeconds = 2400f; // full effect at 40 min
        public const float EscalationFloor = 0.5f;        // windows shrink to 50 % (0.35 was frantic — 2026-08-04)

        // Cleanse aura (2026-08-04): heroes and Litharchs burn the curse
        // away around them — walking consecration. Saturation drops by
        // CleanseAuraPerPulse each maintenance pulse inside the radius, so
        // full crust clears in ~3 s under the aura.
        public const float CleanseAuraRadius = 12f;
        public const byte CleanseAuraPerPulse = 80;
        /// <summary>The Holy Scholar (Alanthor's purify ritualist) is a
        /// walking FONT — a much larger cleanse circle than the hero aura,
        /// and it drains blood pools inside it too. The escort fights on
        /// ground the Scholar keeps clean (2026-08-04 purify flow).</summary>
        public const float HolyScholarCleanseRadius = 26f;

        /// <summary>Age 0 hearth: every completed Hall suppresses the veil in
        /// this radius (grow-block + decay, same as influence — veil-only, no
        /// territory claim). Culture influence supersedes it at age-up.
        ///
        /// RAISED 2026-08-07, 20 → 34. This is the only UNCONDITIONAL clean
        /// ground in the game, and 20 m barely cleared the Hall's own
        /// footprint. Every other suppressor is downstream of something you
        /// lose first: culture influence needs an economy, the Cleansed-well
        /// sanctify disc and the Scholar font need the verb chain, hero auras
        /// need heroes. So a faction that fell behind entered a death spiral —
        /// lose ground → lose influence → lose more ground — and the
        /// 2026-08-07 match caught all three losers in it, sitting at 0 income
        /// with 15+ gatherer huts standing and every deposit on crust they
        /// flee from on arrival.
        ///
        /// 34 m keeps a Hall's inner gathering ring workable no matter how bad
        /// the map gets, which is the floor a losing player rebuilds an army
        /// from. It is still far inside SustainRadiusBase (55) — the curse
        /// still owns the field, it just cannot starve a base to a standstill.
        ///
        /// Follow-up worth considering (NOT done here — new mechanic, not a
        /// tuning change): let an actively-worked deposit suppress the crust
        /// under it, so re-taking a lost patch is a decision rather than an
        /// impossibility.</summary>
        public const float HallHearthRadius = 34f;

        /// <summary>
        /// Radius (m) within which another live outcropping means "this patch
        /// still has buds". Patches spawn at a 5 m spread (procedural
        /// fallback) or a 7 m marker default, so 18 m clears the widest patch
        /// with margin while staying far below the distance between two
        /// separate patches — a patch cannot be mistaken for its neighbour.
        /// </summary>
        public const float PatchCohesionRadius = 18f;
        /// <summary>Seconds between the corruption ping and the curse node
        /// actually rising (2026-08-04 — telegraphed, never a gotcha).</summary>
        public const float CorruptionTelegraphSeconds = 15f;
        /// <summary>Seconds between the blood-contamination ping/announcement
        /// and the creatures spawning.</summary>
        public const float BloodSpawnTelegraphSeconds = 25f;
        public const float PocketRadius = 12f;
        public const byte PocketCoreSaturation = 150;
        /// <summary>RESISTANT by design (playtest 2026-08-03: 300 HP died to
        /// the starting army in seconds) — clearing one is an investment.</summary>
        public const float SmallNodeHealth = 1800f;
        /// <summary>Damage/s to a SmallNode whose cell is suppressed (hearth /
        /// ward / influence) — starving one out takes ~90 s of coverage.</summary>
        public const float SmallNodeStarveDps = 20f;
        public const int PocketResidueNodes = 5;
        public const int PocketResiduePerNode = 40;

        // Blood & the curse (rev.3): blood inside influence fades; outside
        // it is ETERNAL. Where an eternal pool soaks CURSED ground the curse
        // quickens — the site births crystal creatures scaled by pool size
        // and the birth consumes the pool (BloodCurseSpawnSystem).
        public const bool BloodCurseSpawnsEnabled = true;
        /// <summary>No blood-curse births before this match time — the
        /// opening minutes are for securing, not fighting spawners.</summary>
        public const float BloodSpawnGraceSeconds = 300f;
        /// <summary>Seconds between spawn-site scans (one site per scan).</summary>
        public const float BloodSpawnInterval = 30f;
        /// <summary>Min normalized blood (0..1) at the site cell.</summary>
        public const float BloodSpawnThreshold = 0.25f;
        /// <summary>World radius summed as "the pool" (and drained on birth).</summary>
        public const float BloodPoolRadius = 10f;
        /// <summary>Max live Border creatures — the anti-snowball lid.</summary>
        public const int BloodSpawnCap = 12;
        /// <summary>No blood-curse births within this radius of any Hall —
        /// battle pools at a doorstep must not turn a base into a permanent
        /// spawner nest (log-proven degenerate grind, 2026-08-04).</summary>
        public const float BloodSpawnHallKeepOut = 60f;
        // Wave COMPOSITION (2026-08-04 retune — a first wave of 6 Godsplinters
        // was a table-flip): waves are a mix, never monotier. Mostly
        // Crystallings (1 + pool/PerCrystalling, cap 3); Veilstingers only
        // from sizeable pools (>= VeilstingerMin, pool/PerVeilstinger, cap
        // 2); at most ONE Godsplinter per wave and only where a LARGE battle
        // occurred (>= GodsplinterMin summed blood).
        public const float BloodPoolPerCrystalling = 3f;
        public const float BloodPoolVeilstingerMin = 6f;
        public const float BloodPoolPerVeilstinger = 6f;
        public const float BloodPoolGodsplinterMin = 12f;

        // Precipitation — the Veil precipitates veilstone where it touches
        // ground. Crust transitions spawn outcropping nodes on a token
        // budget: recede → residue on clean ground (safe), advance →
        // eruption in the haze (greed tier, richer with depth). Break-cleared
        // cells (cooldown ticking) never pay — explicit actions (pocket
        // collapse) carry their own reward.
        public const float PrecipitationInterval = 30f; // seconds per budget refill
        public const int PrecipitationBudget = 3;       // max nodes per interval
        public const float ResidueChance = 0.04f;       // per organically receded cell
        public const float EruptionChance = 0.015f;     // per newly crusted cell
        public const int ResidueVeilstone = 25;
        public const int EruptionVeilstoneMin = 30;
        public const int EruptionVeilstoneMax = 80;     // at full local saturation
    }
}
