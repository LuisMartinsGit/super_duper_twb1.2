// HeroLevelComponents.cs
// Hero levels 1..10. Canon: docs/Design/Heroes.md §1.
//
// Filed with HeroLevelSystem rather than in Scripts/Components/, per the
// co-location rule in CLAUDE.md: a component belongs beside the system that
// owns it. UnitRank is the deliberate counterpart one folder over — bought
// veterancy for everyone, versus earned levels for heroes alone.

using Unity.Entities;

/// <summary>
/// A hero's current level, 1..10. Present only on units carrying
/// <c>UniqueUnitTag</c>; sect unique units are not heroes and never carry it.
///
/// Never falls while the hero is alive. It CAN come back lower after a death,
/// but only because the player chose the cheap revival (Heroes.md §4) — that
/// is a new hero entity at a lower level, not a demotion of a live one.
/// </summary>
public struct HeroLevel : IComponentData
{
    public byte Value;
}

/// <summary>
/// Banked kill experience. The level is DERIVED from this
/// (<c>HeroProgressionConfig.LevelForXp</c>) rather than stored independently,
/// so the two can never disagree.
/// </summary>
public struct HeroExperience : IComponentData
{
    public int Xp;
}

/// <summary>
/// Stamped on a corpse once its kill has paid out, so the award fires exactly
/// once.
///
/// The death systems all detect a kill by polling for <c>Health.Value &lt;= 0</c>,
/// and a corpse stays at zero HP for as many frames as it takes DeathSystem to
/// clear it. Without this marker a hero standing over a body would collect its
/// XP every frame until the body vanished, which at 60 fps is roughly "kill
/// one archer, reach level 10".
/// </summary>
public struct HeroXpAwarded : IComponentData { }

/// <summary>
/// Marks a unit conjured by an ability rather than trained — King Lexor's
/// Honour thy Pledge today (Heroes.md §3).
///
/// It carries two rules that both exist to stop a summon feeding itself:
/// a temporary unit is worth NO experience to whoever kills it, and it earns
/// none for its owner. Without the first, an opponent farms Lexor's own
/// summons; without the second, Lexor summons an army, lets it fight, and
/// levels off units he paid nothing for.
///
/// <c>TimeToLive</c> counts down to a clean expiry — see PledgeArmySystem.
/// </summary>
public struct TemporarySummon : IComponentData
{
    public float TimeToLive;
}
