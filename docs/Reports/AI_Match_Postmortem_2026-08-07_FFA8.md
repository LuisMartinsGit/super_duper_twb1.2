# 8-player FFA postmortem — 2026-08-07 (second match)

**Source:** `Logs/AI_{Blue,Green,Orange,Purple,Red,Teal,White,Yellow}.log`,
~45 minutes. No `Player_*.log` → all-AI observer match. Follow-up to
[AI_Match_Postmortem_2026-08-07.md](AI_Match_Postmortem_2026-08-07.md),
first run with the wave / ritualist / curse-dormancy changes in.

> **The result has an asterisk: Blue was the only Expert AI. The other
> seven were Normal.** Expert gets a 0.25 s think interval (vs 2.0 s), 30
> workers (vs 18), a 30-hut economy target (vs 14), a 32 army cap (vs 20)
> and a 180 s first-attack gate (vs 360 s). Blue winning is not primarily a
> strategic result — treat this as a 1-vs-7 handicap match.

---

## 1. Outcome

| Faction | Strategy | Diff | Wave 1 at | Last with an army | Fate |
|---|---|---|---|---|---|
| Yellow | Turtle | Normal | 04:48 | **04:51** | dead at 5 min |
| White | TechRush | Normal | 05:09 | 07:52 | |
| Red | Rush | Normal | 04:48 | 12:54 | |
| Green | Turtle | Normal | 04:50 | 14:55 | |
| Purple | Defensive | Normal | 04:49 | 15:55 | |
| Teal | EcoBoom | Normal | 04:50 | 18:56 | |
| Orange | Rush | Normal | **08:46** | 42:02 | runner-up |
| **Blue** | Aggressive | **Expert** | **08:51** | 44:03 | **winner** |

**Six of eight were militarily dead by minute 19.** The match then ran
another 26 minutes. It was a 2-player game from minute 19 and a 1-player
game from minute 42.

Final state — only Blue had an army; everyone else finished on `military 0`.
Orange died rich (4 417 iron, 3 247 veilstone, **0 supplies**), which is the
tell for the failure mode in §3.

---

## 2. What the last round's fixes did

### 2.1 Curse dormancy — works, and the three phases are real

Curse territory, sampled from any faction's SNAPSHOT line (it is a global
figure):

| Time | This match | Previous match |
|---|---|---|
| 00:50 | 11.4 % | 11.3 % |
| 15:55 | **12.4 %** | ~26 % |
| 25:58 | **14.3 %** | ~45 % |
| 30:59 | 23.1 % | ~58 % |
| 36:00 | 35.9 % | 70.6 % |
| 41:02 | 50.1 % | 91.9 % (saturated) |
| 44:03 | 57.2 % | 91.9 % |

Flat at 11–14 % for **26 minutes**, then a clean takeoff. That takeoff is
not a timer — it is Blue's Corruptor pushes waking wells one at a time,
exactly as §2.8 specifies. The curse became a consequence of play instead
of weather. Nothing starved: no faction hit the 0-income veilstone wall
that killed three players last match.

### 2.2 Waves flow

| | This match | Previous |
|---|---|---|
| Blue waves LAUNCHED | **22** | 4 (best faction) |
| Orange waves LAUNCHED | 10 | — |
| Longest single wave | ~60 s | **32 minutes** |

Blue ran waves 4→5→6→7 at a clean 60 s cadence with 40–49 units committed,
and the new arrival counter is live and meaningful: `wave 5 reinforced with
12 unit(s) (33 already committed, 9 on the objective)`. The "one wave that
never ends and blocks every wave behind it" pathology is gone.

Note: `SPENT` is rarer than expected (Blue 1, Orange 6) because a new wave
usually launches on the cadence gate before the old one goes idle, which
simply overwrites the target. That is fine — re-targeting every 60 s is the
behaviour we wanted. The 150 s lifetime cap is a backstop that mostly does
not need to fire.

### 2.3 Verbs partially land

Late-game gaps between Corruptor re-dispatches reach 151 s, 171 s, 211 s and
226 s — long enough for a 40 s channel plus the 60 s vulnerability window.
Combined with the curse takeoff at minute 26+, wells were being woken and
worked. The loop is closed for the first time. It is still far from
reliable — see §3.1.

---

## 3. What is still broken

### 3.1 The escort tramples its own ritualist — FIXED this round

Blue dispatched its Corruptor **73 times** and trained only **one**
Iconoclast all match, so the ritualist was never dying — it was being
interrupted. Binning the 73 dispatches by escort size and measuring the gap
to the next re-dispatch:

| Escort size | Mean gap to re-dispatch | Samples |
|---|---|---|
| 12+ | **18.5 s** | 63 |
| 8–11 | 35.2 s | 7 |
| < 8 | **123.0 s** | 2 |

The channel is 40 s. With a full escort it never once survived; it only
landed after the bodyguard thinned out. **The bigger the escort, the faster
the verb fails.**

Mechanism: `CommitArmy` sent every escort to the exact `wellPos`. A
channelling ritualist sits with `DesiredDestination.Has = 0`, and
`SteeringSystem` keeps separation *"at full strength so units still push
apart inside the cluster"*
([SteeringSystem.cs:250](../../Assets/Scripts/Systems/Navigation/SteeringSystem.cs#L250)).
So a dozen escorts converging on the ritualist's tile shove it radially
outward, the 5 s re-commit ratchets it further, and nothing pulls it back.
Past `CorruptCancelRange` (10 m) the channel breaks and the approach
restarts.

**Fixed:** escorts now take slots on a ring at `EscortStandoffRadius`
(14 m) — outside the cancel range, tight enough to intercept the defenders
the well spawns. Applied to both `AIFeraldisEndgameSystem.CommitArmy` and
the `AIAlanthorEndgameSystem` Scholar escort, which had the identical bug
against a 35 s channel.

*Caveat: the `< 8` bucket is only 2 samples. The 63-sample top bucket and
the monotonic trend carry the conclusion, but confirm the effect size next
match.*

### 3.2 Wave 1 is a suicide run — the single biggest killer

Wave-1 timing predicts the whole match:

- Launched at **~04:50** → Yellow, Red, Green, Purple, Teal, White. All dead
  within 14 minutes.
- Launched at **~08:50** → Orange and Blue. Both survived to the end.

`WaveBaseUnits = 5` on Normal is *exactly the starting army size*, and
`FirstAttackEarliestSeconds = 360` fires before any faction has real
production. So at the gate the AI ships its entire starting army, loses it,
and has nothing left. Yellow is the pure case: wave 1 at 04:48 with `min 5`,
`military 0` from 04:51 onward, and 34 subsequent `floor blocked` lines
because it had no way to rebuild.

Orange and Blue survived only because they did *not* have 5 idle units at
the gate and were forced to keep building.

**Recommended:** wave 1 must not be satisfiable by the starting army.
Either raise `WaveBaseUnits` above the starting count, or gate the first
wave on a production building existing plus N units *trained since spawn*.

### 3.3 Build orders deadlock

`STUCK` lines per faction: Green **33**, Yellow **33**, Red **32**, White
**27** — versus 0–1 for Blue, Purple, Teal, and 1 for Orange. Same shape
every time:

```
[44:39.8] STUCK: BuildBuilding:ShrineOfAhridan blocked 1980s at 2751s (afford=False, idleBuilders=0)
```

A step blocked for **33 minutes** with no fallback. Three of the four
worst-STUCK factions are also the three earliest deaths. The anti-stagnation
path exists but is not catching `afford=False && idleBuilders=0`.

Also still present from the last report: `step 0 SKIPPED (no trainer for
Worker)` at 00:00 on Yellow, plus `no trainer for Spearman/Archer` skips —
build orders still ask for units before their trainer exists.

### 3.4 Zombie factions — unchanged, and now the dominant time sink

Purple sat on `supplies 18, iron 0, veilstone 0, military 0` from 18:56 to
44:03 — **25 minutes frozen**, logging the same line every 60 s. Green the
same at 21 supplies. Nothing eliminates them, because
`VictoryConditionSystem` still only retires a faction at *zero completed
buildings*.

**No victory ever fired.** "Blue wins" is your read of the board, not the
game's — all eight factions were still "alive" by the building test and all
eight logged to the final second. This is now the highest-value open item:
the match was decided at minute 19 and ran to 45.

### 3.5 Supplies, not veilstone, are now the bottleneck

Last match the losers starved of veilstone at 92 % curse. This match the
curse never got there, and they starved of **supplies** instead — Orange
finished with 4 417 iron and 3 247 veilstone against **0 supplies** and no
army. Whatever kills these factions now is a food economy failure
(gatherer-hut coverage, worker counts, or supply drain from conscription),
not the curse. That is a different investigation and a genuinely new
signal — the curse work moved the bottleneck rather than hiding it.

---

## 4. Priorities

1. **Retire factions on economic death** (report #3). 25 minutes of frozen
   zombies is the worst thing in this log, and it makes "who won" unreadable.
2. **Fix wave 1** (§3.2). Cheapest large win available — six of eight deaths
   trace to it.
3. **Victory/defeat UI** (report #2). The game still cannot tell anyone it
   ended.
4. **Un-stick build orders** (§3.3) — fallback when `afford=False &&
   idleBuilders=0` persists.
5. **Investigate the supply economy** (§3.5) — new bottleneck, no diagnosis yet.
6. Re-run with **all eight on the same difficulty** before drawing any
   balance conclusion from an FFA.
