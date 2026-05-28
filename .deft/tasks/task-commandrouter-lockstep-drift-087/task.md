---
deft:
  id: task-commandrouter-lockstep-drift-087
  type: bug
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: spin-out
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [multiplayer, lockstep, commandrouter, determinism]
---

# CommandRouter — lockstep behaviour vs comment drift on Equipment / GodPower / Rally

## Context

Spun out from [task-082 §A row "CommandRouter.IssueEquipmentUpgrade / IssueGodPower"](../task-profound-code-review-082/task.md#§A-Architecture--ECS-hygiene)
and the rally-point row.

Three command paths show drift between their header doc and their
runtime behaviour:

1. [CommandRouter.cs:393 (IssueEquipmentUpgrade)](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L393)
   — header comment says "multiplayer logs and drops", but the code
   returns `true` after `QueueEquipmentUpgradeForLockstep`. Doc and
   behaviour disagree.

2. [CommandRouter.cs:458 (IssueGodPower)](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L458)
   — similar: header comment says "multiplayer wiring is a follow-up",
   code returns `true` after queueing. The queued command will reach
   peers if the LockstepCommand schema actually carries the payload,
   but it's unclear from this code whether the schema is complete.

3. [CommandRouter.cs:344 (SetRallyPoint)](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L344)
   — admits explicitly: "Lockstep queue currently doesn't replicate
   `targetEntity`". Single-player rally on a resource node auto-issues
   gather; multiplayer falls back to position-only. Determinism gap.

## User Value

Multiplayer behaviour matches single-player on equipment / god-power /
rally-on-resource. Comments and code agree. No silent determinism gaps.

## Requirements

- R1: Audit each of the three commands' `LockstepCommand` payload
  variant — does the schema carry the data needed to replay on every
  peer? If not, extend the schema.
- R2: Wire `targetEntity` through `QueueRallyPointForLockstep` (uses
  `LockstepCommand.SecondaryTargetId` per the Gather precedent at
  CommandRouter.LockstepQueue.cs:155-164).
- R3: Update header comments to match actual behaviour after the
  schema is complete.

## File:line anchor (inherited from §A row)

- [CommandRouter.cs:393](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L393)
- [CommandRouter.cs:458](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L458)
- [CommandRouter.cs:344](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L344)

## Out of Scope

- Other lockstep commands. This stub covers only the three rows from
  task-082 §A.
- Lockstep protocol design — touched only where these three commands
  need the schema extended.
