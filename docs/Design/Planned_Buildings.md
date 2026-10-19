# Planned Buildings

> **Doc version: 2026-10-19. Canon for what a build order creates, and for the
> Delete button.** *(Implemented — see §5.)* **§6 (2026-10-19): every plan
> belongs to a worker's build queue; any other order to that worker cancels
> and refunds its plans.**

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

### 1.1 What a site sees (2026-10-16)

A building's line of sight **switches on only when it is finished**. Until
then every site — whatever it will become — sees only the ground it stands
on: a circle whose radius is **half the diagonal of its footprint**,
√(width² + depth²) / 2 — just round its corners. A Hut (2 × 2 tiles) sees
√2 tiles ≈ 2.8 m; a 4 × 4 building 2√2 tiles. The finished building's own sight comes from its SO
(`lineOfSight`, or its level SO's). A plan sees nothing at all (§2).

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
- `PlannedBuildingSystem` — breaks ground when a worker is in range; resumes
  an idle worker's build queue; the ownership sweep of §6.
- `PlannedBuildingVisualSystem` + `FogVisibilitySyncSystem` — white preview,
  owner-only.
- `CommandRouter.IssueDelete` / `DeleteDirect`, lockstep `DeleteEntity = 54`;
  the header button and key in `GameUIManager`.
- Workers only adopt construction sites of their own faction now (they used to
  adopt any site within 8 m of an order).

## 6. A plan belongs to a worker (2026-10-19)

A plan exists only as an entry in some worker's **build queue** — its current
build order or one queued behind it. There is no such thing as a plan nobody
is going to build.

| Event | What happens to the worker's plans |
|---|---|
| **Shift + click** another placement | **Appended** to the queue of every selected worker. Never replaces. |
| A placement **without Shift** while the worker is busy | Replaces: the worker's earlier queue is dropped (its plans cancelled, below), the new plan becomes its order. |
| **Any other order** to the worker — move, attack, attack-move, patrol, stop, hold, repair, heal, convert, a ritual, The Call, a formation order — from the player or the AI | The worker's whole build queue is dropped. Every **plan** in it is **cancelled and refunded in full**, unless another worker still has that plan queued (then it stays, owned by that worker). |
| The worker **dies** (or otherwise stops being a live worker of that faction) | Same: its plans that no other worker holds are cancelled and refunded. |
| Ground already **broken** (a site) | Not a plan any more: dropping the queue frees the worker but the **site stays**, as before — finish it, or Delete it for a full refund (§4). |

**The ownership sweep.** Once a second the simulation checks every plan
against every live worker's build order and queue:

- A plan placed with **no worker at all** (a building lever with nothing
  selected, an AI placement that found no free hands) is offered to the
  faction's nearest idle worker; if none takes it within a few seconds it is
  cancelled and refunded, and the owner is told why.
- A plan that **had** a worker and lost it (its last holder died, or a stuck
  plan in a game saved before this rule) is cancelled and refunded after a
  short grace.

The grace periods are engine timing (they only cover the tick a lockstep
placement takes to reach its worker), not tuning, and live in
`PlannedBuildings` as constants.

**Queues resume on their own.** A worker that finishes or loses its current
job starts the next entry of its queue on the next tick — whether or not
anyone else in the world happens to be building at that moment.

**Delete** is unchanged: a selected plan is cancelled and refunded (§4), and
the workers that had it queued simply drop it.

Code: `PlannedBuildings.ReleaseWorker` (called from every order's command
cleanup — `CommandHelper.ClearAllCommands` and `CommandCleanup.ClearWorkOrders`),
`PlannedBuildings.ResolveOrderTarget` (binds a lockstep Build order issued in
the same tick as its placement to the plan it created), the `PlanOwnership`
component and `PlannedBuildingSystem.SweepOwnership`; the append / replace
flag rides the lockstep `Build` command's `SecondaryTargetId` (bit 0 =
replace; 0 = append, so older replays read as before).
