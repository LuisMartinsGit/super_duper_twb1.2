// ScoutVisionComponents.cs
// Expanding scout vision (Age of Mythology Atlantean-Oracle model — see
// docs/Design/Navigation_And_Formations.md sibling docs / GAME_MANUAL):
// a scout has a SMALL line of sight while moving; once it stands still for
// a short settle delay, its LOS blooms outward over several seconds up to
// a large ceiling. Moving again snaps it back instantly. Scouting becomes
// perch-and-bloom: pick a vantage, wait for the circle, hop to the next.
//
// The live radius is written into LineOfSight.Radius, so fog stamping,
// minimap, and the AI's IntelSystem all inherit the behavior untouched.
// Applied to UnitClass.Scout only (LineOfSight also gates worker auto-find
// and builder auto-chain on other classes — scouts have neither).
//
// Deterministic: fixed-step dt integration + position-delta test, no
// wall-clock, lockstep-safe.
//
// Global namespace per project ECS-component convention.

using Unity.Entities;
using Unity.Mathematics;

