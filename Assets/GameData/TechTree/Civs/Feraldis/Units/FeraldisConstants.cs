// Tuning knobs for the Feraldis fire-and-blood mechanics.
// Canon: docs/Design/Age_1_Feraldis.md — "Blood, Frenzy & War Totems".
// Every value here is an explicit playtest knob; the design doc records
// the intent, this file records the number.

namespace TheWaningBorder.Core.Config
{
    public static class FeraldisConstants
    {
        // ==================== Frenzy on blood ====================

        /// <summary>Normalized BloodMap strength a cell needs before it
        /// counts as "bloodsoaked" for frenzy purposes. Matches the mask's
        /// display threshold (InfluenceMaskTexture BloodStart) so players
        /// frenzy exactly on the blood they can SEE.</summary>
        public const float FrenzyBloodThreshold = 0.10f;

        /// <summary>Attack-damage multiplier while frenzied (+25 %).</summary>
        public const float FrenzyDamageMult = 1.25f;

        /// <summary>Attack-cooldown multiplier while frenzied — the
        /// reciprocal of +20 % attack speed.</summary>
        public const float FrenzyCooldownMult = 1f / 1.20f;

        /// <summary>Seconds the buff lingers after the unit steps off the
        /// blood. Stops the buff strobing at a pool's ragged edge.</summary>
        public const float FrenzyLingerSeconds = 1.0f;

        /// <summary>Seconds between frenzy re-scans. BloodMap is a managed
        /// 128² grid sampled on the main thread, so this runs on a slow
        /// pulse rather than every frame; the linger window is longer than
        /// the pulse, so the buff never gaps for a unit standing still.</summary>
        public const float FrenzyScanInterval = 0.5f;

        // ==================== Bloodletter ====================

        /// <summary>Radius of the Bloodletter's whirl strike.</summary>
        public const float WhirlRadius = 2.5f;

        /// <summary>Bleeding damage per second applied by the whirl.</summary>
        public const float BleedDamagePerSecond = 2f;

        /// <summary>Bleeding duration; refreshed (never stacked) per hit.</summary>
        public const float BleedDuration = 5f;

        /// <summary>
        /// Blood dripped onto the ground per second by a bleeding unit
        /// (BloodMap.AddBlood amounts). Bleeding is DOT *and* blood — a
        /// bleeding unit paints the ground Feraldis fights on whether it
        /// dies or not. Design rule, 2026-08-05 rev.2.
        /// </summary>
        public const float BleedBloodPerSecond = 0.12f;

        // ==================== Axe Thrower ====================

        /// <summary>Bleed the Axe Thrower's landed shots inflict.</summary>
        public const float AxeBleedDamagePerSecond = 2f;
        public const float AxeBleedDuration = 5f;

        // ==================== Firethrower ====================

        /// <summary>Blood saturation an impact needs before the ground
        /// ignites. Matches the frenzy threshold, so anything that reads as
        /// "bloody" to a Feraldis unit is also flammable.</summary>
        public const float IgnitionBloodThreshold = 0.10f;

        /// <summary>Radius of ground the blood-fire consumes and burns.</summary>
        public const float IgnitionRadius = 6f;

        /// <summary>Burn damage per second inside an ignited patch.</summary>
        public const float IgnitionDamagePerSecond = 12f;

        /// <summary>How long an ignited blood patch burns.</summary>
        public const float IgnitionSeconds = 5f;

        // ==================== Raider ====================

        /// <summary>Damage-over-time the Raider leaves on enemy BUILDINGS.
        /// Refreshed per hit, never stacked.</summary>
        public const float RaiderBuildingDotPerSecond = 4f;
        public const float RaiderBuildingDotDuration = 8f;

        // ==================== War Chariot ====================

        /// <summary>Blood laid down per second by a moving War Chariot.</summary>
        public const float ChariotTrailBloodPerSecond = 0.30f;

        /// <summary>Minimum distance the chariot must travel between trail
        /// splats, so a parked chariot does not pool blood under itself.</summary>
        public const float ChariotTrailMinStep = 2.0f;

        // ==================== Suicidal ====================

        /// <summary>Distance to the nearest enemy at which the Suicidal
        /// detonates on its own.</summary>
        public const float SuicideTriggerRadius = 2.5f;

        /// <summary>Blast radius (enemies only — Feraldis does not
        /// friendly-fire its own charge).</summary>
        public const float SuicideBlastRadius = 6f;

        /// <summary>Blast damage before armor and defense.</summary>
        public const int SuicideBlastDamage = 45;

        /// <summary>Blood deposited by a detonation, as BloodMap.AddBlood
        /// amounts. Several overlapping splats — one full-strength splat is
        /// only ~2.5 m across, and the design calls for a LARGE pool.</summary>
        public const float SuicideBloodAmount = 1f;

        /// <summary>Number of blood splats rung around the blast centre in
        /// addition to the centre splat.</summary>
        public const int SuicideBloodRingCount = 6;

        /// <summary>Radius of that ring of splats.</summary>
        public const float SuicideBloodRingRadius = 3.5f;

        // ==================== Berserker Death Frenzy ====================

        /// <summary>Seconds the Berserker holds at 1 HP, unkillable.</summary>
        public const float DeathFrenzySeconds = 5f;

        /// <summary>
        /// The Plunderer's own miniature last stand. It has ONE hit point —
        /// anything at all kills it — and in exchange it thrashes for two
        /// seconds before it goes down. Same machinery as the Berserker's
        /// Death Frenzy, a fraction of the length.
        /// </summary>
        public const float PlundererFrenzySeconds = 2f;

        /// <summary>Attack-damage multiplier during the last stand.</summary>
        public const float DeathFrenzyDamageMult = 1.5f;

        /// <summary>Move-speed multiplier during the last stand.</summary>
        public const float DeathFrenzySpeedMult = 1.5f;

        // ==================== Raider Camp / Plunderer ====================

        /// <summary>Seconds between Plunderer spawns at a Raider Camp.
        /// 5 -> 12 -> 22 across playtests. The 2026-08-05 PM match had the
        /// lone Feraldis on 10,378 supplies while every rival sat at 0-504
        /// and one was drained to nothing: the raid economy was winning the
        /// game on its own. With the 5-per-camp cap this is the knob that
        /// sets how fast a camp refills after losses.</summary>
        /// 22 -> 60 (2026-08-07): with the army-floor bug fixed, Feraldis had
        /// raiders AND a real army and was unbeatable. Raiders cannot be
        /// nerfed on stats — 1 HP and negligible attack already, and the
        /// death-frenzy spree IS the unit — so throughput is the only knob
        /// left. A camp still sustains CampPlundererCap bodies; it just takes
        /// far longer to refill, making raiders a persistent nuisance rather
        /// than a free standing army.
        ///
        /// This is the BASE rate on purpose. A Raider Camp technology should
        /// buy it back down, so raider tempo is something Feraldis invests in
        /// rather than something it is given.
        public const float CampSpawnInterval = 60f;

        /// <summary>Live Plunderers one camp may sustain. The camp pauses at
        /// the cap and resumes as they die.</summary>
        public const int CampPlundererCap = 5;

        /// <summary>Base Supplies/s a raiding Plunderer drains from its
        /// victim's bank. See FeraldisPlunderSystem for what "raiding" means
        /// (live target in engage range, outside own influence, outside curse
        /// influence).
        ///
        /// 5 -> 2 (2026-08-05 PM): at 5/s a full camp out-earned every other
        /// economy in the game combined and emptied its victims' banks
        /// outright. Raiding should be a strong pressure tool, not the whole
        /// win condition.</summary>
        public const float PlunderSuppliesPerSecond = 2f;

        /// <summary>Secondary resources are taken at a fraction of the supply
        /// rate — a raid is mostly food and loot, not ore.</summary>
        public const float PlunderIronFraction = 0.30f;
        public const float PlunderVeilstoneFraction = 0.15f;
        public const float PlunderVeilsteelFraction = 0.04f;

        /// <summary>Take multipliers from the Raiding survey ladder.</summary>
        public const float RaidingTier1Mult = 1.6f;
        public const float RaidingTier2Mult = 2.4f;
        public const float RaidingTier3Mult = 3.4f;

        /// <summary>Influence strength at or above which ground counts as
        /// "held" — a Plunderer standing on its owner's held ground, or on
        /// curse-held ground, earns nothing.</summary>
        public const float PlunderInfluenceBlock = 0.5f;

        /// <summary>Seconds between plunder ticks. Income is accrued in a
        /// float purse and banked as whole units.</summary>
        public const float PlunderTickInterval = 1f;

        /// <summary>
        /// How close a Plunderer must be to its victim to earn. Without this
        /// the only requirement was "has a target", and the patrol driver
        /// assigns targets out to 200 m — so a Plunderer could stand one step
        /// outside its own border and print money at zero risk. The design
        /// says Feraldis has to be in someone's FACE to be paid; this is that
        /// sentence in code. Generous enough to survive approach jitter and
        /// a victim stepping away mid-swing.
        /// </summary>
        public const float PlunderEngageRadius = 6f;

        /// <summary>
        /// Fraction of the normal take earned against a NON-player victim
        /// (curse creatures, neutrals), where resources are generated rather
        /// than stolen. This exists only so a boxed-in Feraldis is not
        /// hard-softlocked with zero economy — it must never be competitive
        /// with actually raiding a player, or parking raiders on curse
        /// spawns becomes the optimal build.
        /// </summary>
        public const float PlunderFloorFraction = 0.25f;

        // ==================== Feraldis Worker ====================

        /// <summary>
        /// Feraldis Workers BUILD ONLY — they never gather (their faction's
        /// income is what Plunderers steal, and ore comes from Mines that
        /// need no workers). In exchange they are real light infantry rather
        /// than helpless civilians.
        /// </summary>
        public const int FeraldisWorkerHP = 110;
        public const int FeraldisWorkerDamage = 9;
        public const float FeraldisWorkerAttackCooldown = 1.6f;

        // ==================== Feraldis Scout + Eagle ====================

        /// <summary>
        /// Feraldis Scouts give up the huge scout sight (and its settle-ramp)
        /// for ordinary unit vision. Their range comes from the eagle instead.
        /// </summary>
        public const float FeraldisScoutLos = 18f;

        /// <summary>The eagle's own sight radius — the real scouting tool.</summary>
        public const float EagleLos = 30f;

        /// <summary>How far out the eagle circles its scout.</summary>
        public const float EagleOrbitRadius = 14f;

        /// <summary>Radians/second the eagle sweeps around its scout.</summary>
        public const float EagleOrbitSpeed = 0.6f;

        /// <summary>How much the orbit radius breathes in and out, and how
        /// fast — this is what makes the circling read as a living bird
        /// rather than a turntable.</summary>
        public const float EagleOrbitWobble = 6f;
        public const float EagleWobbleSpeed = 0.37f;

        /// <summary>Height the eagle flies above its scout.</summary>
        public const float EagleHeight = 9f;

        // ==================== Corruptor / well corruption ====================

        /// <summary>Seconds the Corruptor channels to corrupt a well. Longer
        /// than Alanthor's purify (35 s) — breaking a well is the loudest
        /// thing a Feraldis player can do and should be answerable.</summary>
        public const float CorruptionChannelTime = 40f;

        /// <summary>How long the well stays VULNERABLE once corruption lands.
        /// This is the whole window in which the army has to kill 4000 HP,
        /// while the curse fights back.</summary>
        public const float CorruptionVulnerableSeconds = 60f;

        /// <summary>
        /// Extra seconds a corrupted well can stay open while it is ACTIVELY
        /// losing health. The base window is generous enough to reach the
        /// well; this is what lets an assault that is genuinely landing
        /// damage finish the job on a 4000 HP target instead of watching it
        /// reseal at 20 % health. Chip damage alone cannot exceed this, so a
        /// single archer cannot hold a well open indefinitely.
        /// </summary>
        public const float CorruptionMaxHeldSeconds = 120f;

        /// <summary>Range the Corruptor must be within to channel, and the
        /// distance at which the channel breaks. Mirrors the purify ritual.</summary>
        public const float CorruptRange = 6f;

        /// <summary>
        /// How far the Corruptor may be from the well before the channel
        /// breaks. RAISED 10 → 14 on measured evidence (2026-08-07, four-way
        /// Feraldis Age-4 match, the first run with ritual diagnostics):
        ///
        ///   started at 6.0 m → BROKEN at 1.8 s, "drifted to 10.0 m"
        ///   started at 6.0 m → BROKEN at 21.1 s, "drifted to 10.0 m"
        ///   started at 4.1 m → survived the full 40 s and completed
        ///
        /// A channel that begins at the edge of CorruptRange has only 4 m of
        /// tolerance, and a ritualist standing still on the Crown slides
        /// radially outward under jostling — the logged dy of 0.9 m at 6 m and
        /// 2.3 m at 10 m matches the dome's own profile exactly, so the drift
        /// is real movement down the hill, not noise. In a four-way melee over
        /// a single well, 4 m is lost in under two seconds.
        ///
        /// 14 m gives 8 m of tolerance from the worst-case start. The intended
        /// counterplay is unaffected and demonstrably still works: the same
        /// match killed a Corruptor mid-channel at 13.3 s. Killing the caster
        /// should end a ritual; brushing past him should not.
        /// </summary>
        public const float CorruptCancelRange = 14f;

        // --- Defence waves while a well is corrupted ---

        /// <summary>Seconds between defender spawns at the start / end of the
        /// vulnerability window. The pressure ramps as the well weakens.</summary>
        public const float CorruptionWaveMaxInterval = 6f;
        public const float CorruptionWaveMinInterval = 2.5f;

        /// <summary>Defenders spawned per wave tick — "moderately large".</summary>
        public const int CorruptionWaveBurst = 3;

        /// <summary>Hard cap on defenders spawned for one corruption, so a
        /// long fight cannot spiral without limit.</summary>
        public const int CorruptionMaxDefenders = 30;

        /// <summary>Spawn ring radius around the well.</summary>
        public const float CorruptionSpawnRadius = 6f;

        /// <summary>
        /// Chance a given defender is a Veilstinger (ranged) rather than a
        /// Crystalling, once the window is past halfway.
        ///
        /// GODSPLINTERS ARE DELIBERATELY ABSENT from corruption waves. They
        /// are magic-siege-tank class (420 HP / 34 dmg / 26 range even after
        /// the 2026-08-04 nerf) and putting them in a wave that already has
        /// to be survived while killing a 4000 HP well made the whole
        /// objective impossible.
        /// </summary>
        public const float CorruptionVeilstingerChance = 0.25f;

        // ==================== Marching influence ====================

        /// <summary>
        /// Influence a Feraldis military unit or raider leaks per second into
        /// the ground it stands on. This is the culture's SECOND territory
        /// mechanic and the one that actually shapes the map: Alanthor claims
        /// by building outward from home, Runai by trade lanes — Feraldis
        /// claims by WALKING ON YOU. Its border grows toward wherever its
        /// army is, which is by definition toward the enemy.
        ///
        /// Deliberately far weaker per-source than a War Totem: an army
        /// passing through smudges a corridor, it does not plant a border.
        /// Holding ground is what makes it stick.
        /// </summary>
        public const float MarchInfluencePerSecond = 1.6f;

        /// <summary>Radius each marching unit stamps.</summary>
        public const float MarchInfluenceRadius = 7f;

        /// <summary>Seconds between march-influence pulses. PlayerInfluenceMap
        /// is managed main-thread state, so this runs on a slow tick.</summary>
        public const float MarchInfluenceInterval = 0.5f;

        // ==================== The Warpath ====================

        /// <summary>
        /// Crust saturation burned away per second beneath a Feraldis
        /// military unit. THE CULTURE'S ANSWER TO THE CURSE, and deliberately
        /// the mirror image of Alanthor's.
        ///
        /// Alanthor turtles and treats the crust as a free outer wall — the
        /// curse defends their flanks for them. Feraldis is the aggressor, so
        /// the same crust is a moat around every target it wants to reach. It
        /// cannot out-turtle the curse and it should not try. Instead its army
        /// BURNS A LANE: crust dies under a Feraldis advance, so an attack
        /// carves its own corridor and the corridor stays open while the army
        /// holds it.
        ///
        /// Consequences that make this a real decision rather than a free
        /// pass: the lane only exists where the army IS, it closes behind
        /// them once they move on (the veil regrows), and burning is the one
        /// thing a Feraldis player can do to the curse — they still cannot
        /// hold ground against it the way Alanthor can.
        /// </summary>
        public const float WarpathBurnPerSecond = 26f;

        /// <summary>Radius of the burn under each marching unit. Narrow on
        /// purpose: this is a corridor, not a cleansing aura.</summary>
        public const float WarpathBurnRadius = 6f;

        /// <summary>Seconds between warpath pulses (VeilField is sim state on
        /// the main thread).</summary>
        public const float WarpathInterval = 0.5f;

        /// <summary>
        /// Veilstone paid to a Feraldis faction for each CELL of crust its
        /// warpath actually destroys (a cell crossing from crust down to
        /// clear). Canon: Curse_And_Shardroot.md §2.6 — "Feraldis is the ONLY
        /// culture that earns veilstone FROM the curse; they keep the wall and
        /// get rich off it." This is that promise, paid on destruction rather
        /// than on mining.
        ///
        /// It is self-limiting by construction: a cell can only be cleared
        /// once until the veil regrows over it, so an army parked on clean
        /// ground earns nothing and the income tracks how much curse the
        /// faction is actually deleting.
        /// </summary>
        public const float VeilstonePerCellCleared = 0.9f;

        /// <summary>A War Totem burns crust around itself far harder than a
        /// marching soldier — this is what lets a planted totem actually HOLD
        /// its ground instead of being swallowed. Per second.</summary>
        public const float TotemBurnPerSecond = 60f;

        /// <summary>Radius the totem keeps clear. Comfortably wider than its
        /// own influence ring at zero Fervor, so it never suffocates.</summary>
        public const float TotemBurnRadius = 16f;

        // ==================== War Totem ====================

        /// <summary>Minimum normalized blood under a War Totem's centre for
        /// placement to be legal.</summary>
        public const float TotemPlacementBloodThreshold = 0.15f;

        /// <summary>Radius the totem drinks blood from.</summary>
        public const float TotemDrinkRadius = 10f;

        /// <summary>Seconds between drink pulses.</summary>
        public const float TotemDrinkInterval = 2f;

        /// <summary>Fraction of the blood in radius consumed per pulse.</summary>
        public const float TotemDrinkFraction = 0.05f;

        /// <summary>Fervor banked per pulse when standing on FULLY saturated
        /// blood, scaled down by the actual mean saturation in radius. At
        /// 2.5/pulse on a 2 s pulse a totem on a rich pool maxes out in ~80 s
        /// — roughly the same time its 5 %-per-pulse drinking empties that
        /// pool, so "the totem grows as the blood runs out" reads correctly.</summary>
        public const float TotemFervorPerPulse = 2.5f;

        /// <summary>Fervor ceiling — the totem stops gaining past this.</summary>
        public const float TotemFervorMax = 100f;

        // ── Totem aura (2026-08-07) — a totem must pay for itself ────────
        // Planting on blood is no longer enough: the totem projects a healing
        // + combat buff around itself, and SUSTAINING that aura drains the
        // pool it stands on. Run the pool dry and it collapses. This turns a
        // totem from free furniture into a decision — plant it where a real
        // battle happened, hold ground with it, and watch it eat its own fuel.

        /// <summary>Radius (m) of the totem's healing / buff aura.</summary>
        public const float TotemAuraRadius = 18f;

        /// <summary>HP per second restored to friendly units inside the aura.</summary>
        public const float TotemAuraHealPerSecond = 6f;

        /// <summary>Fractional attack bonus inside the aura (0.20 = +20 %).</summary>
        public const float TotemAuraAttackBonus = 0.20f;

        /// <summary>Blood drained per second to sustain the aura, on top of
        /// the Fervor drink. The aura is the expensive half: a totem on a thin
        /// pool now burns through it instead of squatting forever.</summary>
        public const float TotemAuraBloodPerSecond = 0.06f;

        /// <summary>Normalized blood under the totem below which it counts as
        /// DRY. Deliberately under TotemPlacementBloodThreshold (0.15) so a
        /// totem does not start dying the instant it is planted on a legal
        /// but modest pool.</summary>
        public const float TotemDryBloodThreshold = 0.03f;

        /// <summary>Seconds a totem survives with no blood before collapsing.
        /// One minute — long enough to be a warning, short enough that totem
        /// spam on thin blood costs more than it returns.</summary>
        public const float TotemDryLifetime = 60f;

        // ── Plunder for the whole army (2026-08-07) ──────────────────────

        /// <summary>Share of a dedicated Plunderer's take that an ordinary
        /// Feraldis warrior earns while raiding. Compensation for the raider
        /// throughput nerf: raiding stops being a unit you build and becomes
        /// what the Feraldis army DOES. Below 1 so the Plunderer keeps its
        /// job — it is still the specialist.</summary>
        public const float PlunderWarriorFraction = 0.45f;

        /// <summary>Influence deposit rate at zero Fervor / at max Fervor.</summary>
        public const float TotemInfluenceRateMin = 6f;
        public const float TotemInfluenceRateMax = 15f;

        /// <summary>Influence radius at zero Fervor / at max Fervor.</summary>
        public const float TotemInfluenceRadiusMin = 12f;
        public const float TotemInfluenceRadiusMax = 24f;
    }
}
