# Planned Buildings

> **Doc version: 2026-10-01. Canon for what a build order creates, and for the
> Delete button.** *(Implemented — see §5.)*

---

## 1. Order → plan → site

A build order no longer drops a construction site on the map. It creates a
**plan**:

| Stage | What it is | Who sees it |
|---|---|---|
| **Plan** | A white, translucent preview of the building. Paid in full. | **Its owner only** |
| **Site** (unbuilt) | The real under-construction building, rising as it is built | Everyone (fog permitting) |
| **Building** | Finished | Everyone |

A plan turns into a site **the moment a worker arrives and starts building**
("breaks ground").

## 2. What a plan is not

A plan has no footprint in the world:

- **Invisible to enemies**: no model, no minimap blip, no intel, fog or no fog.
- **No collision role**: it does not block pathing or hold ground for anyone.
- **It does nothing**: no health, it cannot be attacked, does not lock or claim
  territory, pays no income and counts toward no victory or elimination
  check.
- **It does reserve its owner's tiles**: the same player cannot plan (or place)
  a second building over cells one of their plans already covers.

Its owner can still click it, so it can be cancelled (§4).

## 3. Two players, one spot

Two players from opposing teams **may plan the same location**; neither sees
the other's plan. **The first to break ground keeps the spot.** At that moment
every other faction's plan overlapping the new site is **cancelled and
refunded in full**, and its owner is told why.

Breaking ground also re-checks the spot against the world as it is then: a
plan whose ground was lost, or which a real building or resource node now
covers, is cancelled and refunded instead — **after a grace period
(2026-10-05)**: a refusal at break-ground is usually transient (ground still
being claimed at match start, a unit standing on the footprint), so the plan
keeps its spot and retries for a short while before it is cancelled. This
replaced cancel-on-first-refusal, which had the AI paying for and refunding
the same hut dozens of times a minute. A spot whose plan was cancelled this
way is remembered for a while, and the AI's site search avoids it.

## 4. The Delete button

One button, top-right of the selection header, and the **Delete** key:

| Selected | Delete does |
|---|---|
| A plan | Cancels it — **everything paid refunded** |
| A construction site | Cancels it — **everything paid refunded** |
| A finished building | Demolishes it (no refund) |
| A unit | Kills it |

Only your own. The tooltip says which of these the current selection gets.

## 5. Implementation

- `PlannedBuilding` (Scripts/Components) — the plan entity: building id, yaw,
  paid cost and Religion Points; deliberately **no** `BuildingTag`, `Health` or
  `UnderConstruction`, so everything that reacts to a building ignores it.
- `PlannedBuildings` (Scripts/Entities/Buildings) — create, own-plan overlap,
  `BreakGround`, `Cancel`, and tag→id counts so the AI's building counts include
  plans.
- `CommandRouter.PlaceBuildingDirect` spends, then creates a plan for every
  worker-raised building (landmarks still self-construct as sites);
  `CreateConstructionSite` is the shared site tail.
- `PlannedBuildingSystem` — breaks ground when a worker is in range; idle
  workers adopt plans no one is heading for.
- `PlannedBuildingVisualSystem` + `FogVisibilitySyncSystem` — white preview,
  owner-only.
- `CommandRouter.IssueDelete` / `DeleteDirect`, lockstep `DeleteEntity = 54`;
  the header button and key in `GameUIManager`.
- Workers only adopt construction sites of their own faction now (they used to
  adopt any site within 8 m of an order).
