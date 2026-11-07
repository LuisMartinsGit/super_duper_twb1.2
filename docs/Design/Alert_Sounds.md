# Alert sounds (2026-11-07)

Sounds that tell a player to look up. They are presentation only: the simulation enqueues a
signal and never reads anything back, so they cannot affect lockstep.

## What plays when

| Sound | When |
|-------|------|
| **Under attack** (horn) | A hostile weapon hit lands on the local player's unit or building: melee, arrows, towers, siege, curse units |
| **Shardroot** (deep bell) | It is unearthed, changes hands (picked up, dropped, carrier killed), detonates, is enshrined, its Temple falls, the curse takes it; the local player learns where it lies or sees it move |
| **Monument** (chimes) | The local player takes or loses a monument, or explores its ruins |
| **Countdown** (double toll) | A Temple or Sanctum victory countdown starts, reaches a warning mark, or breaks |
| **Victory / Defeat** (fanfare / falling horn) | The local player's match is decided |

## Rules

- **The horn is about attacks, not damage.** Cursed-ground decay, burns on cursed ground and
  HP-cost abilities do not sound it, because it is posted where a hostile hit is resolved
  (`CombatDamageHelper.ApplyBonusDamageOnHit`). Spell damage that does not pass through a weapon
  hit does not sound it either.
- **The horn does not spam.** It repeats slowly while a fight stays in one area and sooner when
  a new area is hit, with a minimum gap between any two. The timings and the area radius are on
  `AlertSounds.asset`.
- **Map events do not stack.** One moment often posts several signals, so a short minimum gap
  keeps them to one sound (`AlertSounds.asset`).
- **Sounds follow the notices.** A monument changing hands between two other players has no
  notice and so no sound.
- **Spectators** hear map events and the fanfare, never the horn. Nothing plays during a replay
  seek.
- **A match that merely stops is silent.** A dropped connection or a replay running out reaches
  the same end screen with no fanfare (`MatchLifecycle.MatchDecided`).
- **Volume** follows the master volume. There is no separate alert slider.

## Where it lives

`Scripts/Systems/Audio/AlertSounds.cs` plays, with its levels and clips on `AlertSounds.asset`
beside it. `SimSignals.Alert` and `SimSignals.UnderAttack` are the queues, and `SimSignalPump`
drains them. The clips are `GameData/Audio/Alerts/Alert_*.wav`, synthesised by
`tools/audio/gen_alert_sounds.py`, so they come from no asset pack. Swap one by replacing its
clip on the asset.
