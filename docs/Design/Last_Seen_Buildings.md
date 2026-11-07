# Last-seen buildings (2026-11-07)

Developer's bug report: "Nothing should update without direct vision. I explore location A, then
I lose sight of it. If a new building is erected and upgraded there, I should only see it when I
have vision again."

**Status: implemented. Code: `GameSystems/Rendering/Buildings/LastSeenBuildings.cs`, decided in
`FogVisibilitySyncSystem`. Test: `-twbLastSeenTest` (`Bootstrap/Headless/HeadlessLastSeenTest.cs`).**

## The rule

The player sees other factions' buildings as they last saw them, not as they are.

1. **A building appears only after it has been directly seen.** Explored ground is not enough: one
   erected there later stays unknown until the player has vision of it.
2. **Out of vision, a seen building stays frozen as last seen:** its model, level and construction
   state. Nothing about it changes on screen or on the minimap.
3. **Regaining vision updates it at once** to its current state.
4. **A building destroyed out of sight stays on screen** until the player sees that spot again.
5. **Clicking a remembered building shows its name and level as last seen,** with no health bar
   and no actions.
6. **The player's own and allied buildings stay live,** because allies share sight (Teams.md).

## Boundaries

- This is presentation only. It writes nothing to the simulation, so it cannot cause a desync.
- The AI keeps its own scouting memory and is unaffected.
- A spectator sees whatever its current view shows, as today.
- A loaded save has no memory, so it starts from the ground the player had explored.
- A destroyed building's ghost cannot be clicked: there is no entity left to describe.
