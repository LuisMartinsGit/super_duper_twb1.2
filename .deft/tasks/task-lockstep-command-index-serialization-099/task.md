---
deft:
  id: task-lockstep-command-index-serialization-099
  type: bug
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: critical
  source: task-profound-code-review-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [multiplayer, lockstep, determinism]
---

# Lockstep — CommandIndex not serialized → same-player command ordering desync

## Context

Spun out from [task-082 §C.6](../task-profound-code-review-082/task.md#L1074).
**Determinism risk.** `LockstepCommand.Serialize()` at
[LockstepTypes.cs:91](../../../Assets/Scripts/Core/Multiplayer/LockstepTypes.cs#L91)
writes `Type,EntityId,X:R,Y:R,Z:R,TargetId,SecondaryId,BuildingId` — but
`CommandIndex` is NOT in the format string.

On the receiving side,
[LockstepManager.cs:730 (ProcessTickMessage)](../../../Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L730)
sets `cmd.PlayerIndex` and `cmd.Tick` but leaves `CommandIndex = 0`
(default).

[`ProcessTick` at L371-375](../../../Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L371)
then sorts `allCommands` by `(PlayerIndex, CommandIndex)`. Locally-queued
commands have a proper monotonic `CommandIndex` (set at `QueueCommand`
L128). Remote commands all have `CommandIndex = 0`.

`List<T>.Sort` is **unstable** — so the order of multiple same-player
remote commands depends on input order, which is not guaranteed equal
across peers. **Silent desync risk on multi-command ticks.**

Severity: `wrong-result`. Triage: `fix` (not `spin-out` — this should
be high priority since it's a determinism gap).

## User Value

Multiplayer simulations stay in sync when a player issues multiple
commands on the same tick (build queue burst, multi-select move,
shift-queued waypoints).

## Requirements

- R1: `LockstepCommand.Serialize` includes `CommandIndex` in the wire
  payload, after `Tick` or before `Type` (pick a stable position).
- R2: `LockstepCommand.Deserialize` reads it back.
- R3: `ProcessTickMessage` does NOT overwrite `CommandIndex` after
  deserialization.
- R4: Either switch to a stable sort (`OrderBy` + `ToList`) OR add
  `CommandIndex` as a secondary sort key with proper monotonic values
  — both. Stable sort alone is insufficient because input order from
  network is not stable.

## Acceptance Criteria

- [ ] Wire payload includes CommandIndex; round-trip serialize/deserialize
      preserves the value.
- [ ] Multi-command-burst repro across two clients: shift-queue 5 build
      orders on tick T; both clients process them in the same order
      across 100 reruns.
- [ ] No regression in single-command-per-tick traffic.

## Edge Cases

- Tick boundary: if a player queues commands across two ticks, each tick's
  commands have their own monotonic CommandIndex range. The
  `(PlayerIndex, Tick, CommandIndex)` triple is the unique key.
- Replay logs: any saved replay using the old wire format will lose
  ordering on reload. Add a format version field if replay compatibility
  matters.

## Technical Notes

- This belongs to the same family as task-087 (commandrouter lockstep
  drift) but is a distinct bug — keep them separate so the fix lands
  with its own desync repro test.
- Coordinate review with [task-100](../task-lockstep-tick-payload-mtu-100/task.md)
  which touches the same `BroadcastTick` codepath.
