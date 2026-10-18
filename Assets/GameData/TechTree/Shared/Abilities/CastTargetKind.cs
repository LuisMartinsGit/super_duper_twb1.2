// CastTargetKind.cs
// What a single-target spell is cast ON — docs/Design/Spells.md §2 and §6.
//
// Shared by both spell pipelines: an ability card carries it as
// AbilityCard.TargetKind (authored on the AbilityDefSO), a sect power derives
// it from its kind and reach (SectActivePowerSpec.Target). A spell with a kind
// other than None is cast by clicking the unit or building itself
// (EntityTargeting); the simulation re-checks the pick with CastTargetRules
// before any cooldown is charged.

namespace TheWaningBorder.Abilities
{
    /// <summary>
    /// The entity a single-target spell may be cast on. "Own" is the caster's
    /// faction only; "Friendly" adds team allies (docs/Design/Teams.md);
    /// "Enemy" is Alliances.AreHostile. APPEND ONLY: the byte value is
    /// serialized on the AbilityDefSO assets.
    /// </summary>
    public enum CastTargetKind : byte
    {
        /// <summary>Not cast on an entity: a ground point, around the caster,
        /// or no target at all.</summary>
        None = 0,
        EnemyUnit = 1,
        OwnUnit = 2,
        FriendlyUnit = 3,
        AnyUnit = 4,
        EnemyBuilding = 5,
        OwnBuilding = 6,
        FriendlyBuilding = 7,
        AnyBuilding = 8,
        EnemyUnitOrBuilding = 9,
    }
}
