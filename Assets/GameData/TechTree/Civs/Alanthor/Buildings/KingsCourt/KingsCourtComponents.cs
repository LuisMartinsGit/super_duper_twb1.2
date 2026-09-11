// KingsCourtComponents.cs
// The King's Court's identity marker.
//
// -- Why this exists ------------------------------------------------------
// The King's Court is the Alanthor form of the Hall: the SAME entity, renamed
// at age-up, exactly as the House is the Hut's and the Guild is the Gatherer's
// Hut's (CLAUDE.md, "a cultured building is the SAME entity renamed").
//
// It had no marker of any kind. BuildingFactory says so in its own comment --
// "KingsCourt carries no distinguishing tag at all" -- and the consequence was
// that the rename never actually happened: an aged-up Alanthor Hall kept
// answering "Hall" to every id query, and the five techs authored with
// researchAt: KingsCourt could never find a host. Measured over an 11-session
// headless batch on 2026-09-09: the id KingsCourt was instantiated ZERO times,
// and the AI's economy ladder stalled at IronTags -> "no ready KingsCourt
// host" 97 times in a single match, blocking every tech queued behind it.
//
// -- The tag rides ALONGSIDE HallTag, it does not replace it --------------
// HallTag is the faction's lifeline: EliminationSystem and
// VictoryConditionSystem both skip a building carrying it before they run any
// other test, because a Hall is "their own lifeline" and must never be
// mistaken for an ordinary military building. A King's Court is still that
// lifeline. So the aged-up Hall carries BOTH, and only the id it reports
// changes.

using Unity.Entities;

/// <summary>
/// On the Alanthor form of the Hall, and on a King's Court placed directly.
/// Sits beside <see cref="HallTag"/>, never instead of it.
/// </summary>
public struct KingsCourtTag : IComponentData { }
