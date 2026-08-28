// Canon: docs/Design/Age_1_Feraldis.md.

using Unity.Entities;

/// <summary>
/// Feraldis Hall of Axes — the culture's ranged house (Archer, Hunter and
/// Firethrower). Its throwers hurl axes and fire rather than loosing arrows,
/// which is why it is a hall and not a range.
///
/// It is a building in its OWN RIGHT, not a cultured Archery Range: the
/// Archery Range is Alanthor-only and era 2 (2026-08-27), so there is no
/// shared entity for Feraldis to rename. Replaces the retired "Thrower Camp"
/// name, which only ever existed as a rename that no longer applies.
/// </summary>
public struct HallOfAxesTag : IComponentData { }
