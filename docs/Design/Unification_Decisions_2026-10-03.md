# Unification decisions (Age 0 + Alanthor), 2026-10-03

Standing rules (user):
- The SO assets are the single source of truth; everything else derives from them.
- On a conflict, the SO wins by default.
- Delete `Resources/TechTree.json`.
- Generate the calculator from the SOs.
- Strip numbers from the design docs.
- Remove code-side stat tables wherever an SO holds the value.

## Decisions
40. **Trading Outposts beside the outcrop, four per outcrop (2026-10-04, developer):** *"On age up, mines in veilstone are moved to the adjacent space. 4 traders posts can be built around a veilstone slot. on NSEW. this way, we can quadruple the amount of veilstone available without adding more slots and while also steeping the cost for building and upgrading more trade posts."* Alanthor only (Runai and Feraldis keep mining outcrops). Supersedes decision 28's on-outcrop placement. (a) An Outpost stands on one of an outcrop's four side slots (N / E / S / W), flush against it, one per side; the outcrop stays an impassable node. (b) At age-up each Veilstone Mine becomes the outcrop's FIRST post, moved to the free side nearest the capital (ties N, E, S, W); the other three sides can then be built. (c) The cost ramp is PER OUTCROP: the 2nd / 3rd / 4th post beside the same outcrop costs more to build AND to level; the first post beside a new outcrop is base price again. Multipliers on `TradingOutpostSystem.asset` (`outcropRampMultipliers`). (d) The Outpost gets three levels, `TradingOutpost_Lvl1..3` level SOs, raising its trade rate (`tradeRateMultiplier`); level prices follow the same ramp. Rules: [Veilstone_Economy.md §3.1](Veilstone_Economy.md), [Build_Grid.md § The Trading Outpost's side slots](Build_Grid.md), [Age_1_Alanthor.md § Trading Outpost](Age_1_Alanthor.md).
39. **Hut income compensation (after the 0.0.33 batch):** removing the hidden +60/min per hut slowed age-up from 5:32 to 7:52. So the slot income goes up by the same amount: Gatherer's Hut 110/160/260 per minute, Alanthor Guild 130/160/260. The values are on the SOs.
38. **Fiendstone Keep:** leave it for the Feraldis pass (out of scope; move it out of Age0/ then).
Everything else in the inventories (minor stat differences such as the Barracks/Hut/Gatherer's Hut defense values): the SO wins by the default rule.
37. **Fervored Masses:** veilstone only. 53 Vs converted at Regions.md's rates (Vs = 4 supply-value, V = 1.5 per supply-value) = 318 V, so the price becomes **678 V**. Keep "no veilsteel in Age 0 costs" in Age_0.md.
36. **Palisade gate:** keep 40S+15I (same as the stone wall gate).
35. **Tags and armour:** fix the SOs to follow Combat_Pacing: add weight tags to the Outrider, Cataphract, ranged and siege units (propose Light/Heavy per unit for the user to see), and set emplacement siege armour to 0.
34. **Code-only values become SO fields**, copied from today's shipped values, and the code tables are deleted: unit population cost, build times (Royal Stable, Siege Yard, Wall Hub, Vault, Keep, Palisade), footprints (BuildingSizeConfig), territory income ladders (huts 50/100/200, Alanthor 70/100/200, mines 140/200/400) and the wall-level HP multipliers (1.6/2.3/3.0).
33. **Trainers:** every trainer is listed in its building SO's trains[]. The Ledger and King Lexor train at the Fortress. CUT the Holy Scholar (unit, SO, Temple append, AI purify code).
32. **Siege roster:** keep all four (Ram, Ballista, Catapult, Trebuchet). Un-retire the Catapult in the FactionPopulation code and fix the doc's "Catapult replaced Ballista".
31. **Per-battalion upgrades:** DROPPED. Faction-wide techs are the design; remove the text from Overview.md and the Alanthor doc.
30. **Roster:** the SO roster is canonical. Remove the doc-only entities (Royal Guard, L2/L3 cavalry tiers, Academy, Wheel cart, Stone Ledgers, Crucible). Swordsman, Longbowman and the Royal Stable are no longer "TBD".
29. **Alanthor tech effects:** SO values (Fletching +5 m, Mason Guild +30%, War Horn/Full Gallop 15 m, Ranging Shot 60 s). The code must apply Charge's +50% speed.
28. *(Placement superseded by decision 40, 2026-10-04: the Outpost now stands beside the outcrop, up to four per outcrop.)* **Trading Outpost:** it snaps ON the outcrop, but its model must read as standing BESIDE it (an art/visual requirement). Keep the on-outcrop placement for now. 1 s cycle. Add it to Age_1_Alanthor.md and Build_Grid.md, and fix the SO description.
27. **L1 is free:** zero the Garrison, Archery Range and Guild L1 prices. (Also check the Age 0 Hut and Gatherer's Hut L1 code-table prices against the same rule.)
26. **Emplacements:** SO build times (Ballista 35 s, Trebuchet 55 s, worker-built). Ballista needs wall L2+, Trebuchet L3.
25. **Wall docs:** delete the stale wall sections in Age_1_Alanthor.md. GAME_MANUAL follows the code (read the real spacing and snap values from code).
24. **Watch Tower:** SO stats (700 HP, 6/13/0/3) PLUS the 4-slot garrison from Age_1_Alanthor.md/JSON (units inside add arrows). This is a new SO field plus code; use the doc's garrison rule as written.
23. **Armour/weapon ladders:** keep the raised SO prices (T3 140 Vs, T4 weapon/arrow 170 Vs). Fix Veilstone_Economy's "same costs".
22. **Unit unlocks:** Cataphract at Royal Stable L3, Sentinel at Garrison L3 (the SO values).
21. **Tech gates:** the SO `minBuildingLevel` is the ONLY gate, set to the docs' level requirements (Guild L2/L3 for Survey II/III etc., the Masses, Vault and emplacement techs per the docs). BuildingActionLayouts reads it; delete the faction-age 2/3 gates, which are never reached (that bug hid the Tools, Surveys, Walls and Pylons from players).
20. **Temple:** no Temple levels. Delete TempleLevelConfig and the AI's Temple leveling. Set the SO maxPerFaction to 1.
19. **Starting army:** 5 Spearmen, 1 Scout and 3 Workers. No Archers (Age 0 is the melee age). Update PlayerSpawnSystem and Age_0.md.
18. **Retaliatory Measures:** CUT every reference (Hut.asset entry, UI slot, effect handler; the JSON goes anyway).
17. **Guild Surveys:** KEEP Veilstone Survey I-II and Veilsteel Survey. Add an exception to Veilstone_Economy.md: Guild huts may produce veilstone and veilsteel as a research reward.
16. **Gatherer's Hut techs:** move the Alanthor Guild line (Surveys, Reinforcements, Walls, Pylons) to Civs/Alanthor/Buildings/Guild/Research and the Feraldis Raiding/Plunder line to Civs/Feraldis, and culture-gate both. The hut has no research in Age 0.
15. **Mines:** the iron Mine and the Veilstone Mine are Age 0 buildings for everyone (100S+10V). Move the Mine SO to Age0/Buildings/Mine/ and register the VeilstoneMine SO in the catalog (drop the code seed). Alanthor's Veilstone Mines become Trading Outposts at age-up. Fix Age_0.md.
14. **Tech effects:** Stone Tools gives +15% build speed; Conscription gives 15% faster training. Fix the docs and the Loc key.
13. **Scouts:** Scouts start at 0 damage. Armed Scouts sets damage 2, stored as an effect in the tech SO, and it applies to existing AND newly trained Scouts (fix the bug where new ones come out at 0).
12. **Vault of Almierra:** keep what ships: 600 HP, 25% interest applying from L1, upgrades 210S+50I / 427S+100I. Move all of it into the Vault SO and level SOs.
11. **Capital footprint:** 10x10 m (5x5 cells) for both the Shelter and the Fortress. Add both, and the Palisade, to Build_Grid.md. Fix Age_0.md.
10. **Capital income:** 200/min from the SO (50 per 15 s). Drop TerritoryIncomeSystem's extra flat 50/min. Fix Veilstone_Economy §5.
9. **Gatherer's Hut:** it pays ONLY the territory slot income (50/100/200 per minute; Alanthor 70/100/200). Remove the construction safety-net SuppliesIncome and the unused SO suppliesPerTick field. HP stays 300.
8. **House:** keep the SO values: pop 6, HP 650, LoS 6. Fix Age_0.md and Territory_Claims (the opening cap is 10+6 = 16).
7. **Research_Era2:** DELETE the SO and every reference (including the UI/AI id skips).
6. **Shrine of Ridan:** PURGE everywhere. The Litharch and the heal ladder belong to the Temple.
5b. The Shelter turns into the Fortress **automatically at age-up** (Age 0 = Shelter, Age 1 = Fortress).
5c. The Hall and King's Court techs (Stone Tools, Armed Scouts, Iron Tools, Mason Guild, Scouting Celestarii, Veilstone Tools) all research at the capital: the Shelter in Age 0, the Fortress after. Alanthor-only techs stay Alanthor-gated.
5d. The **Fortress keeps levels L1-L3** (the existing 3 Alanthor level SOs, renamed "Fortress - Lvl N"). ~~Territory_Claims §10's limit counts Fortress levels.~~ *(2026-10-04: the territory limit is deleted — Territory_Claims.md §10; a faction holds what it can defend.)*
5. **Capital naming (NEW DESIGN):** the Age 0 starting building is the **Shelter**, which upgrades into the **Fortress**. There is no Hall, no King's Court and no Town Hall. The KingsCourt SO is retired, along with the "King's Court" rename and the "Town Hall" level names. (When and how it upgrades: see item 5b.)
4. **Hall:** DELETE everywhere. Its techs move to Fortress/Research with `researchAt: Fortress`.
3. **Unit stats:** the SO and Combat_Pacing win, train times included. The age docs drop their stat blocks.
2. **Prices:** the SO prices (Regions.md resource domains) are canon. The docs stop stating prices. This covers the Worker at 60S, the tech re-pricings and the Alanthor unit and level prices.
1. **Smelter (Alanthor_Smelter):** REMOVE every reference in code, JSON, calculator, docs, AI and upgrade config. SmelterTag stays only for the Runai Foundry (out of scope).
