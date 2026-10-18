// AbilityCard.cs
// Data-driven ability system — authoring/runtime data model.
//
// Mirrors the "ability cards" authored in the tech-tree calculator
// (tools/calculator). An ability is pure data: an activation kind, a
// targeting shape, cast/duration timings, a radius/range, a list of
// structured effects, and an "aftermath" list of other abilities that are
// cast automatically when this one ends (the chain: Liquid Courage ->
// Veilshift Withdrawal + Life Cling; Automate Facility -> Under Automation).
//
// The runtime AbilitySystems interpret this data generically — adding a new
// ability that reuses existing effect kinds is a data-only change in
// AbilityCatalog. A genuinely new mechanic adds one AbilityEffectKind + a
// handler branch.

namespace TheWaningBorder.Abilities
{
    /// <summary>How an ability is triggered.</summary>
    public enum AbilityActivation : byte
    {
        Active = 0,   // fired by the player / AI / an aftermath chain
        Passive = 1,  // always on (auras, passive self-buffs)
        OnDeath = 2,  // fired when the owner dies
    }

    /// <summary>The shape of an ability's targeting.</summary>
    public enum AbilityTargeting : byte
    {
        SelfCast = 0,      // affects the caster only
        SingleTarget = 1,  // one target entity in range
        Area = 2,          // AoE around a target point
        Aura = 3,          // continuous radius around the caster
        Global = 4,        // whole faction (no range)
    }

    /// <summary>Who an ability's effects apply to (for Aura / Area / Global).</summary>
    public enum AbilityAffects : byte
    {
        Self = 0,
        AlliedCulture = 1,     // allied units of the caster's culture (King's Call → Alanthor)
        AlliedAll = 2,         // all allied units
        AlliedCavalry = 3,     // allied cavalry of the caster's culture
        Enemies = 4,
        EconomicBuildings = 5, // allied economy buildings (Automate Facility)
        AlliedRanged = 6,      // allied ranged units (Choreographed Volleys)
    }

    /// <summary>
    /// One structured effect on an ability. <see cref="Value"/> is interpreted
    /// per <see cref="Kind"/> (percent, flat, seconds, radius, ...).
    /// </summary>
    public struct AbilityEffect
    {
        public AbilityEffectKind Kind;
        public float Value;

        public AbilityEffect(AbilityEffectKind kind, float value) { Kind = kind; Value = value; }
    }

    /// <summary>
    /// The finite set of effect mechanics the ability engine knows how to
    /// execute. New abilities that reuse these are data-only.
    /// </summary>
    public enum AbilityEffectKind : byte
    {
        None = 0,
        AttackPct = 1,          // +Value% outgoing damage (SpellBuff.DamageMultiplier)
        ArmorPct = 2,           // +Value% of the target's own armor for the incoming damage type (SpellBuff.ArmorPct)
        ArmorFlat = 3,          // +Value flat armor
        DamageTakenPct = 4,     // Value% change to incoming damage. -90 = 90% reduction (Liquid Courage)
        MoveSpeedPct = 5,       // Value% move speed change. -50 = -50% (Veilshift Withdrawal)
        SelfDoTPctOverDuration = 6, // deal Value% of max HP to self, spread over the ability's duration
        HpFloor = 7,            // clamp HP so it never drops below Value (Life Cling: 1)
        ChargeBonusFlat = 8,    // +Value flat damage on charge attacks (King's Call: +20)
        RevealFog = 9,          // reveal fog of war in radius (Use Celestar). Value = reveal radius override (0 = card.Radius)
        ResourceYieldPct = 10,  // +Value% resource yield on a targeted eco building (Automate Facility)
        NoAutomation = 11,      // marks a building as un-automatable for the duration (Under Automation)
        LosRampWhileStill = 12, // passive: line-of-sight grows while stationary (Scout Sight)
        ChargeDamagePct = 13,   // +Value% damage on the NEXT charge hit, for allied cavalry in radius (War Horn)
        DisarmWhileBuffed = 14, // the affected units cannot attack for the duration (Full Gallop's sprint)
        DeployFieldHospital = 15, // spawn a temporary healing building at the caster (Litharch)
        SummonPledgeArmy = 16,  // spawn a temporary army around the caster, scaled by hero level (Honour thy Pledge)
        FireRatePct = 17,       // +Value% fire rate for allied RANGED units in radius (Choreographed Volleys: 100 = double)
        ShardboundFury = 18,    // the Shardbound King hurls every enemy in radius skyward, damages buildings (ShardboundFury.Cast)
        // Harkon Fiend (docs/Design/Age_1_Feraldis.md §8) — HarkonAbilities.
        FeraldisWarcry = 19,    // allied Feraldis units in Radius frenzy for the duration
        FeraldisLeap = 20,      // jump to the aimed point, slow enemies where he lands
        FeraldisRaze = 21,      // destroy the hostile building at the aimed point (capitals lose a share)
        FeraldisBloodOath = 22, // allied units in Radius cannot drop below 1 HP for the duration
        FeraldisFieldSacrifice = 23, // the Slave dies where it stands and leaves a blood pool (FeraldisSacrifice)
        FeraldisRigUnload = 24, // the War Rig lets its passengers out (WarRig.Unload)
        FeraldisOvercharge = 25, // the War Rig's cannon burst, paid in HP (FeraldisCombatSystems.Overcharge)
        // The Runai Wayfinder and Caravan Master (docs/Design/Age_1_Runai.md §6.1, §8) — RunaiAbilities.
        RunaiWayfinding = 26,      // allies in Radius move Value% faster for the duration
        RunaiMirage = 27,          // Value decoy riders appear around the caster for the duration
        RunaiCaravanOfSwords = 28, // every caravan of the faction is armed and toughened for the duration
        RunaiExodus = 29,          // after the cast, allies in Radius are carried to the nearest market
        RunaiScatter = 30,         // caravans in Radius run to the nearest market
        RunaiTheCall = 31,         // escorts of the caster's and bordering territories come to the caster (§8.1)
        RunaiReturnToWagons = 32,  // the faction's called escorts walk back to their caravans
        // The Alanthor sect heroes (docs/Design/Sects.md §4, 2026-10-15) —
        // SectHeroAbilities. Auras (Passive) run in SectHeroAuraSystem.
        SectCatalogued = 33,       // aura: slowed enemies in Radius take +Value% damage (Archivist L1)
        SectStampOfRecord = 34,    // the aimed enemy's abilities all go on cooldown (Archivist L3)
        SectRecall = 35,           // caster and allies in Radius jump back to where they stood 5 s ago (Archivist L6)
        SectCauterize = 36,        // aura: allies in Radius are immune to damage over time (Surgeon L1)
        SectBloodletting = 37,     // allies in Radius drop to 35% HP and attack Value% faster (Surgeon L3)
        SectLancet = 38,           // the aimed enemy bleeds and cannot be healed (Surgeon L6)
        SectDrillmaster = 39,      // aura: Sallyguards in Radius charge twice as fast (Warden L1)
        SectSallyForth = 40,       // allies in Radius gain speed; Sallyguards fully charged (Warden L3)
        SectChallenge = 41,        // enemies in Radius must attack the caster (Warden L6)
        SectCalledIn = 42,         // aura: wounded enemies in Radius are slowed Value% (Guildmaster L1)
        SectStake = 43,            // channel: the extractor under the caster pays double (Guildmaster L3)
        SectLiquidate = 44,        // demolish the aimed own building for Value% of its worth (Guildmaster L6)
        // The curse (Territory_Claims.md §6.9.5, 2026-10-16) — VeilOffering.
        CurseVeilOffering = 45,    // the creature dies; ground within Radius x its claim size turns cursed
        // The Litharch's Cleanse (Territory_Claims.md §6.9.7, 2026-10-18) —
        // LitharchCleanse. CleanseGround starts the channel; the other three
        // are its parameters, read off the card, and do nothing on their own.
        CleanseGround = 46,        // channel: the aimed Radius circle is cleared, then grows by Value m² a second
        CleanseFalloff = 47,       // width (m) of the falloff band outside the cleared circle
        CleanseMaxRadius = 48,     // the cleared circle stops growing at this radius (m)
        CleanseHeal = 49,          // HP a second healed on allies inside the starting circle while it channels
    }

    /// <summary>
    /// A complete ability definition. Managed data held in
    /// <see cref="AbilityCatalog"/>; units reference abilities by catalog
    /// index (see <c>UnitAbilities</c>).
    /// </summary>
    public sealed class AbilityCard
    {
        public string Name;
        public AbilityActivation Activation;
        public AbilityTargeting Targeting;
        public AbilityAffects Affects;
        public float CastTime;       // seconds of channel before effects apply (0 = instant). Interruptible - docs/Design/Spells.md
        public float Duration;       // seconds the effect lasts (-1 = permanent / always-on passive)
        public float Cooldown;       // seconds before it can be recast, charged when the cast COMPLETES (0 = auto, see EffectiveCooldown)
        public float Radius;         // units (Aura/Area)
        public float Range;          // units (SingleTarget/Area cast range; 0 = unlimited for an aimed card)
        public AbilityEffect[] Effects;
        public string[] Aftermath;   // ability names auto-cast when this ends

        /// <summary>
        /// Damage this ability deals per victim, 0 for abilities that deal
        /// none. Every point of it lands through SpellDamage.Apply, so armor
        /// for <see cref="DamageType"/>, Invulnerable, Liquid Courage and the
        /// death-ward floors all apply. docs/Design/Spells.md.
        /// </summary>
        public float Damage;

        /// <summary>Which armor column the damage is measured against. Magic
        /// unless the design names another (True ignores armor).</summary>
        public DamageType DamageType = DamageType.Magic;

        /// <summary>
        /// True when the player picks the ground point the ability lands on
        /// (the targeting ring). False for an Area ability that forms around
        /// the caster wherever he stands (War Horn, Honour thy Pledge,
        /// Shardbound Fury). Range 0 on an aimed card means no range limit.
        /// </summary>
        public bool AimedAtPoint;

        /// <summary>
        /// Hero level at which this ability becomes available. 1 = always, and
        /// that is every ability shipped before 2026-09-08, so nothing already
        /// authored changes behaviour.
        ///
        /// Below this level the ability is NOT CASTABLE AND NOT SHOWN, rather
        /// than shown greyed out: a button the player cannot explain is worse
        /// than no button. See docs/Design/Heroes.md §2.
        /// </summary>
        public int UnlocksAtLevel = 1;

        /// <summary>
        /// The effect played when this ability lands (AbilityDefSO.vfxPrefab,
        /// the <c>&lt;Name&gt;_Effect</c> prefab in the ability's own folder).
        /// Presentation only — AbilityVfxPlayer spawns it on the affected
        /// entity for the ability's duration, or once for an instant one, and
        /// keeps it on the caster for a passive aura. Null on the code-seed
        /// fallback cards.
        /// </summary>
        public UnityEngine.GameObject Vfx;

        public bool IsPassive => Activation == AbilityActivation.Passive;
        public bool IsPermanent => Duration < 0f;

        /// <summary>
        /// Grace added to the duration when a card leaves Cooldown at 0
        /// ("auto"): the ability can be recast one second after its effect
        /// ends. The cooldown is charged when the cast completes, so the auto
        /// value counts from the moment the effect lands.
        /// </summary>
        public const float AutoCooldownGrace = 1f;

        /// <summary>
        /// The cooldown the engine actually charges, and the one every UI
        /// must show: the authored Cooldown, or for "auto" (0) the effect's
        /// duration plus <see cref="AutoCooldownGrace"/>.
        /// </summary>
        public float EffectiveCooldown
            => Cooldown > 0f ? Cooldown : (Duration > 0f ? Duration : 0f) + AutoCooldownGrace;

        /// <summary>True when the player must pick a ground point to cast it.</summary>
        public bool IsAimed => Targeting == AbilityTargeting.Area && AimedAtPoint && Radius > 0f;

        public bool HasEffect(AbilityEffectKind kind)
        {
            if (Effects == null) return false;
            for (int i = 0; i < Effects.Length; i++) if (Effects[i].Kind == kind) return true;
            return false;
        }

        public float EffectValue(AbilityEffectKind kind, float fallback = 0f)
        {
            if (Effects == null) return fallback;
            for (int i = 0; i < Effects.Length; i++) if (Effects[i].Kind == kind) return Effects[i].Value;
            return fallback;
        }
    }
}
