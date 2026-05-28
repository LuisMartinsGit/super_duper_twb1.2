---
deft:
  id: task-lockstep-tick-payload-mtu-100
  type: improvement
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: task-profound-code-review-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [multiplayer, lockstep, network]
---

# Lockstep — BroadcastTick can approach UDP MTU on burst ticks

## Context

Spun out from [task-082 §C.6](../task-profound-code-review-082/task.md#L1074).
`LockstepManager.BroadcastTick` at
[LockstepManager.cs:612-636](../../../Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L612)
packs every command for a tick into a single UDP datagram:
`sb.Append("|"); sb.Append(cmd.Serialize());`.

A high command burst (e.g. 50 units queued same tick = ~50 commands ×
~80 bytes ≈ **4 KB**) approaches the UDP MTU (typically 1500 bytes on
Ethernet, ~1232 bytes safe across the public internet). UDP
fragmentation will work in principle but lost fragments lose the
whole tick — players issuing rapid clicks on a slow network would
silently desync.

`static-only — needs repro` via UDP MTU tests.

Severity: `wrong-result`. Triage: `spin-out`.

## User Value

Lockstep multiplayer survives high-action moments (large army moves,
build queue rampages, mass right-clicks) without silent desync from
fragmented UDP losses.

## Requirements

- R1: Measure observed tick payload sizes in a stress scenario (50+
  commands/tick). Log the max-byte tick over a sample session.
- R2: If max-byte tick > ~1200 bytes, implement one of:
  - Per-tick chunking — split commands across multiple UDP datagrams
    with a chunk index / chunk count header.
  - Switch to a length-prefixed framing over TCP (or a reliable UDP
    library) for command broadcast.
  - Compress payload (commands are mostly numeric — gzip / per-field
    binary encoding cuts size significantly).
- R3: Whichever option is chosen, document the trade-off in
  `docs/Technical_Reference.md` (latency vs reliability vs complexity).

## Acceptance Criteria

- [ ] Stress test with 50+ commands/tick shows no silent desync over
      100 reruns on a simulated 1% packet loss link.
- [ ] Max tick payload measured and documented.
- [ ] Tick rate budget (kbit/s/player) updated in the multiplayer
      design notes.

## Edge Cases

- Replay format: if the wire format changes, save/replay format may
  also need a version bump.
- LAN vs internet: LAN can tolerate larger MTU; the safe internet ceiling
  is lower. Defaults should target internet.

## Technical Notes

- Coordinate with [task-099](../task-lockstep-command-index-serialization-099/task.md)
  which also touches the wire payload — landing both in the same PR
  may make sense to amortize the format-version bump.
- This is an `improvement` not a `bug` because it's a robustness gap,
  not a current-reproducible defect at typical command rates.
