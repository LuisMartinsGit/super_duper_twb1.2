# Sect Chapel Units

Canonical source for the **sect-unique units**: which unit each sect's
chapel trains, costs, and caps. Sect framing (12 sects, chapels as the
adoption mechanism, 6 Temple slots) lives in the sect redesign; this doc
covers the unit layer wired 2026-08-11.

## The rule

**A sect's chapel trains that sect's unique unit.** Chapels are real
2x2 buildings raised beside the Temple by adoption (they carry a training
queue from birth); the unit appears on the chapel's action panel once the
sect is adopted — there is no other way to obtain it. Cap: **2 alive (or
queued) per sect per faction** — these are elite specialists, not a line.

## Full roster — ALL 12 SECTS LIVE (2026-08-11)

The `IsImplemented` rollout gate is retired: every sect ships a chapel,
a passive lever, an active power, and its chapel unit.

| Sect | Unit | Kit | Cost |
|------|------|-----|------|
| Antiquity | **Lorekeeper** | Stealth reveal + Reliquary intel hub | 120s 40i |
| Renewal | **Scar Guard** | Rapid Mend (self-heal), 170 HP tank | 140s 50i 20v |
| Fortitude | **Stone Warden** | Fortify (armor + root self), 200 HP | 150s 60i 20v |
| Reclamation | **Golem Autark** | Arcane Pulse (AOE), 320 HP walking siege | 200s 80i 40v |
| Silence | **Archivist Adept** | Dispel (strip buffs) | 130s 40i 25v |
| Justice | **Judicator** | Condemn (+25% damage taken) | 130s 40i 25v |
| Veneration | **Vault Keeper** | Safeguard (AOE armor) | 140s 50i 20v |
| Witness | **Glassmark Arcanist** | Mirror Shield (reflect) | 150s 40i 30v |
| War | **Warbreaker** | War Cry kit, 260 HP shock elite | 180s 70i 30v |
| Ash | **Ashblade** | Ignite (fire damage x3) | 150s 60i 20v |
| Ruin | **Nullblade** | Void Strike (+40 next hit) | 150s 60i 25v |
| Wrath | **Chaincaster** | Chain Bind (root) | 130s 40i 25v |

FlameWarden and Brandbreaker remain **reserve kits** (factories exist,
unmapped). Numbers are placeholders seeded in
`TechCatalog.ApplyUnitDefaults` (owner tunes; an authored UnitDefSO
overrides them).

## AI adoption strategy

`AlanthorSectPriority` orders all 12 for a defensive economic culture:
Renewal, Fortitude, Justice, Antiquity, Reclamation, Veneration, War,
Witness, Silence, Ash, Ruin, Wrath. The Temple's 6 chapel slots and the
RP economy cap how deep a match gets — the order IS the strategy.

## AI conformance

The Alanthor endgame trains its adopted sects' units automatically —
one attempt per think tick, capped at 2 per sect, skipped while the
faction is saving toward a pivotal purchase (AIPivotalReserve).
