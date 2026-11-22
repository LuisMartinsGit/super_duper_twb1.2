# Unification decisions (Age 0 + Alanthor), 2026-10-03

Standing rules (user):
- The SO assets are the single source of truth; everything else derives from them.
- On a conflict, the SO wins by default.
- Delete `Resources/TechTree.json`.
- Generate the calculator from the SOs.
- Strip numbers from the design docs.
- Remove code-side stat tables wherever an SO holds the value.

## Decisions
111. **A wave is the whole army, no exceptions (2026-11-22, developer: "waves must be comprised
     of the full army. no exceptions.").** In a 149-minute match two Rush Experts held 200 to
     290 soldiers each and attacked with 20 to 60: the rest were raiding parties, sister
     armies, claim squads, Sanctum and monument squads, economy responders, wall posts,
     Shardroot strikes, soldiers who were in a fight when the wave formed and soldiers late
     to the muster. With `sendWholeArmy` (`SimpleAISystem.asset`) a wave that forms while no
     other is out now drafts every soldier the faction has and takes each off its old duty
     (`StripOtherDuties`); it never splits into a raid party or sister armies; nobody is left
     at the muster; the raid timer is off; and a Shardroot strike no longer pulls soldiers
     out of a live wave (only the countdown strike of decision 110 does). The one soldier
     left is a unit garrisoned inside a tower. Units trained while the wave is out still
     follow it as reinforcement groups. The headless harness gained `-twbMaxStart`
     (`twb-run.ps1 -MaxStart`): every bank starts at the cap, as the lobby option does.
110. **The countdown strike owns the army (2026-11-22, batch 28).** Decision 108 sent every
    soldier at an enshrined Shardroot, and the wave machinery called them back: "108 more join
    the group of 171 still mustering", "132 unit(s) not at the muster point stay for the next
    group" within a minute of the march order, so four armies of 150 to 190 never stood at the
    Temple together and the bearer ascended. While the strike is on, the wall guard, the
    Sanctum army, monuments, the curse sortie and the attack waves do not run for that faction
    (`CountdownStrikeOwnsArmy`, `SimpleAISystem.cs`). The enshrine price was raised in the same
    pass (iron and veilstone, `ShardrootEmpowerment.asset`): batch 28's bearers formed the
    decision 109 price as fast as the old one.
109. **The enshrine price asks for iron too (2026-11-20, batch 27).** With the price reachable
    again (decision 106) all twelve matches of batch 27 ended by ascension, median minute 52:
    supplies and veilstone alone form too easily in a late economy. The price now has an iron
    part (`enshrineIron` on `ShardrootEmpowerment.asset`, beside `enshrineSupplies` and
    `enshrineVeilstone`), which is the resource the army also wants, so enshrining costs
    soldiers. The AI's save, its price ETA and the order's refusal text already read the whole
    price.
108. **An enshrined Shardroot is a clock: every rival soldier marches (2026-11-20, batch 27).**
    On Fiendstone Pass five rivals with armies of a hundred and more each logged "saving an
    army for the Shardroot" against the Temple's defenders and lost together. Each weighed
    only its own idle soldiers against the defence. While a hostile ascension countdown runs
    and the Shardroot is in an enemy Temple, the AI's Shardroot strike drafts every combat
    unit (claim squads and verb units excepted), ignores the launch margin, the minimum party
    and the tier's wave cap, and re-sends idle members every think
    (`SimpleAISystem.Shardroot.cs`, `TickShardroot`). Game_AI.md § 6l.
107. **A hard cap on production buildings (2026-11-20, batch 27).** Decision 101 bounded the
    rate governor's waiver but the other paths (province step, home floor, rebuilt trainers)
    still added trainers: Experts ended with 35 to 41 for two hundred units. Past
    `productionMaxTotal` (`SimpleAISystem.asset`) no path places another production building
    (`ProductionGate`). Checked in the same batch and NOT bugs: every "gateless" wall is a
    choke left open between its tower hubs (decision 81) and every such player's army left
    home; "trainer queue full" on empty Farm slots is the Fortress already training Workers
    as fast as it can (the log line now appears only when none is queued).
106. **What is being saved is not a glut (2026-11-20, batch 26, Yiel Lymwérra).** The AI's
    Market sold any resource above its glut level, and the Shardroot's enshrine price asks for
    more supplies than that level: a bearer's supplies were sold back down every think (289
    sales in one match), the price never formed, and the bearer waited at home for 55 minutes
    with a full-population army and one wave in an hour. Every pending savings goal
    (`AIPivotalReserve.PendingTotal`) is now added to the level a glut sale keeps
    (`SimpleAISystem.TickMarket`).

105. **An army that can win, and an economy behind it (2026-11-20, a developer match against an
    Expert Rush: "an aggressive AI that never made a meaningful attack").** It sent 22, 23 and 29
    soldiers at a player it had itself counted at 47, in bodies of three to twelve, from one
    territory and a bank short of supplies beside idle iron.
    (a) **A wave is sized against the army it has seen.** A wave waits at home, and the army
    target rises, while its strength is below `waveVsArmyRatio` times the strongest hostile
    field army sighted in the last `waveVsArmySightingSeconds` (soldiers seen and alive, summed
    per faction). This is the enemy's ARMY, not the defence at the objective, which is the hold
    decision 91 removed; it ends after `waveVsArmyMaxHoldSeconds` or at a full population, and a
    Rush's first wave goes regardless.
    (b) **No raids while the army is the weaker one** (the same test, on the whole army).
    (c) **Whoever misses the muster stays for the next group** (amends decision 95: sent after
    the army at once, they marched as a body of four or eleven).
    (d) **A curse sortie when landlocked.** For `curseClearLandlockedSeconds` after a claim round
    finds nothing claimable, the curse sortie goes while a wave is out and at a node not yet
    seen; it picks its soldiers before the wave drafts, and a wave no longer dissolves it.
    (e) **The market raises veilstone from the other resource down to its short level** (it kept
    twice that, so iron under double the level was never sold for the supplies the army lacked).
    (f) **The Vault takes what the army cannot use**: a resource is idle, whatever the army's
    size or the unit rate, while the army's purchases are stopped for a different resource.
    (g) The Farm staffing logs why no Worker was trained for an empty slot.
    All on `SimpleAISystem.asset`. Code: `SimpleAISystem.Military.cs` (`KnownFieldArmy`,
    `TryLaunchAttack`, `TickRaids`), `CurseClear.cs`, `Farms.cs`, `EconomyDrive.cs`.

104. **A wallet borrows a limited share and pays it back; the savings pot is nobody's to spend
    (2026-11-20, developer: "a pot can only overspend a percentage of what's available. There
    must also be a payback... having more units now will mean having less later on, which is
    what happens to a human").** The AI's three wallets (advancement, military, economy) lent to
    each other without limit and were rescaled to the bank every think, so each could spend the
    whole bank and a wallet that held back lost its share to the one that overspent; and of all
    the AI's spenders only the building-level buyer left the savings pot alone, so a pot filled
    and was emptied again (a developer match: the production-level pot twice within reach of its
    price, twice drained by sect buildings and towers, neither level bought).
    (a) **Limit.** A wallet short of a price may owe the other two at most
    `walletOverspendShare` of what they hold, per resource, counting what it already owes.
    (b) **Payback.** What it borrows is a debt to the wallet that lent it. While it owes,
    `walletDebtRepayShare` of its own income goes to its lenders before it may spend it. The
    BUDGET log line names what each wallet owes.
    (c) **Spending past the wallets** (a purchase paid from the bank) is charged to the wallets
    as income is shared, no longer in proportion to what each holds.
    (d) **The pot.** Every AI spender that pays the bank directly asks
    `AIBudget.CanAffordLeavingSavings`: the price must be payable with the pot left whole. The
    exceptions pay from the whole bank: the purchase the pot is saving for, the gate of a wall
    that would otherwise seal its owner in, and the Shardroot. Both values are on
    `AIBudget.asset`; the pot's share of income stays the tier's `savingsIncomeShare`.

103. **Economic upgrades are bought (2026-11-20, developer: "economic upgrades always compensate
    their cost").** The AI refused the Farm and Mine levels whenever the added income would not
    repay the price inside a short window, and in a developer match an Expert refused the Farm
    level several hundred times and the Mine level over a thousand. The windows
    (`farmLevelPaybackMinutes`, `mineLevelPaybackMinutes` on `AIBuildingUpgradeSystem.asset`) are
    lengthened so that any level a normal match repays is bought; the test stays only to refuse
    a level with next to nothing to improve.

102. **The AI keeps its Vault bars (2026-11-20, developer: "that vault is there explicitly to
    allow players to have a reliable income in the late game").** The AI withdrew, losing every
    bar, whenever its savings were paused, and savings pause for one extractor owed a rebuild or
    for a defended base. Batch 25: 269 withdrawals across 56 players, 259 of them for "economy
    in distress" and nearly all with the income holding; 42 players filled a first bar at some
    point and 16 still held one at the end. The AI now withdraws only when the Vault is falling,
    when its supply income has COLLAPSED and the army cannot be paid, or (as before) when it
    defends or rebuilds a lost army and the bank cannot pay. An extractor owed a rebuild no
    longer stops deposits either; a collapsed income still does (`SimpleAISystem.TickVault`,
    `VaultWithdrawReason`).

101. **Idle trainers buy no more trainers; a gate must fit between the hubs (2026-11-20, batch 25,
    developer: "it has more buildings than units").** Two corrections, no new rule.
    (a) The Expert unit-rate governor (decision 86) let an extra production building pass the
    saturation gate whenever the rate was behind and the bank could pay. Behind for want of iron,
    it spent the iron on a further building, fell further behind and built again (one Expert
    ended with more production buildings than a Hard player has buildings of every kind). The
    waiver now also needs the trainers the faction HAS to be busy at the tier's
    `productionSaturationThreshold`: idle trainers mean money is the limit
    (`SimpleAISystem.ProductionGate`).
    (b) Decision 81's "a gate must fit or the way stays open" measured the gate stretch hub
    centre to hub centre and rounded up, so a narrow ramp passed with fewer real modules than
    a gatehouse takes and was sealed (the same two seats in both Fiendstone Pass matches). The
    test now takes the hub footprint off the stretch and counts whole modules only
    (`AIWallPlanner.EmitChokeLine`).

100. **A tier that can fight is not kited (2026-11-20, developer: "Having a 30hp unit kite
    lots of 10 HP units and killing them all is too broken to be allowed by a hard/hardest
    AI").** On the tiers whose profile carries tactics skill, in `SimpleAISystem.Tactics.cs`:
    (a) a weak threat never takes over the army. One member hit and fighting back used to
    put the whole army into the engagement; the threat is now weighed all the same, and one
    under `engageMinThreatFraction` gets a DETACHMENT (`AnswerHarasser`): the shooters and
    cavalry that can reach it, nearest first, until they outweigh it by `harassAnswerOdds`,
    while foot that ran out of the formation is called back and the army marches on.
    (b) every engagement keeps a LEDGER (`ChaseNotPaying`): health lost against hostile
    health destroyed, read off the last health seen for each member and each hostile in the
    fight (a hostile that walks away counts nothing). After `chaseReviewSeconds`, against a
    foe under `chaseWeakFraction` of its strength, or on cursed ground it was not sent to,
    an army that has lost more than `chaseLossRatio` times what it destroyed breaks off,
    resumes its march and passes weak hostiles by for `chaseIgnoreSeconds`. The ledger is
    what judges a fight on cursed ground once it has begun; `CurseFightWorthIt` (decision
    99) only decides whether to start one. All values on `SimpleAISystem.asset`. Lower tiers
    keep the old behaviour: being kited is the bad play they are meant to show
    (Game_AI.md § 2a). Not covered: the skilled target assignment may still give a foot
    soldier a target it cannot catch during a full engagement. Game_AI.md § 6e.
99. **An army fights only what its soldiers see, and keeps off cursed ground (2026-11-20,
    developer: "AI is sending armies directly into cursed ground and they run around
    aimlessly there until they die. Armies should avoid cursed ground unless they are aimed
    at a target inside the cursed area", then "The army should only attack when the target
    is within LOS of the units that make up the army. Until then it should steer as a
    formation").** Three changes in `SimpleAISystem`: (a) the legs the route planner never
    saw now go through it (`March`): the strike leg from the stage point, a close-range
    launch and a reinforcement group's direct strike; (b) a regroup re-forms the army on its
    current route leg instead of re-planning it straight at the destination; (c) the tactics
    layer takes a target only when at least one member has it inside its own line of sight
    (`SeenByArmy`), and one standing on cursed ground only when the fight pays for itself,
    unless the army's objective is on cursed ground too (developer: "the AI must evaluate if
    the risk is worth it. 10 soldiers in the curse are losing a total 30 HP per second, to
    kill an enemy that might not be worth the HP loss"). `CurseFightWorthIt` prices it: the
    hostile health round the target, over the weapon damage a second of the members who
    would go in, is the length of the fight; past the curse's grace window each of them
    loses the mean exposure damage a second; the fight is taken when the hostile health
    destroyed, times `curseFightWorth` (`SimpleAISystem.asset`), is at least that loss. A
    flank point is pushed off cursed ground. A threat to the army's engines is always
    answered. Not covered yet: defence responses and recalls home go in straight lines, and
    a kiting step is not checked against cursed ground. Game_AI.md § 6k.
98. **A waiting formation slows down, it does not stop (2026-11-20, developer: "Sometimes
    issuing a command causes the formation to stop completely"; applies to every formation,
    the player's and the AI's).** The re-form wait and the convoy wait of 2026-11-17 set the
    leader's speed to nothing while any one member was off its spot, or while a sister
    formation was behind. Both now walk at `columnWaitSpeed` of the group's pace, and the
    re-form wait starts only when more than `columnWaitFraction` of the members lag
    (`FormationGroupSystem.asset`). Navigation_And_Formations.md § 2.
97. **At most `towersPerTerritoryCap` Watch Towers per territory (2026-11-20, developer:
    "limit to 3 towers per territory").** One cap per faction per territory, the home
    included, asked by every rule that builds a tower: the home ring, province coverage,
    emergency defence, Sanctum fortification and the Alanthor endgame's defensive towers
    (`SimpleAISystem.TowerCapReached`, `SimpleAISystem.asset`). Standing and planned towers
    count; Wall Towers do not. Game_AI.md § 5g.
96. **Siege engines: one for every twenty soldiers at most (2026-11-20, developer: "attack
    waves ... have too many battering rams, should be 1 per every 20 soldiers max").**
    `siegeMaxArmyShare` is lowered to that ratio and is now rounded DOWN where the siege
    program and the production spread turn it into a count; the fixed engine floor
    (`siegeTrainFloor`) and the war's engine demand (`keystoneSiegeMin`) drop to one engine.
    The same share is applied to the body that marches: a wave or a reinforcement group
    leaves its surplus engines at home (`TrimSiegeToShare`). Game_AI.md § 4.
95. **The army leaves when most of it has mustered; the rest follow as a formed group
    (2026-11-20, developer: "attack waves are too small", "AI keeps all troops in their
    main base at all times").** Measured on batch 24: half the musters timed out and left a
    median of 17 soldiers at home. `musterGatherFraction` is lowered, so the army goes as
    soon as that share stands at the muster point; whoever is not there when it goes is left
    out of it (never trailing as a straggler) and the next reinforcement group is due at
    once, so they follow as a formed body and merge with the army in the field. Supersedes
    "the army musters completely" (2026-11-17). Game_AI.md § 6.
94. **Hero levels grant stats; level 10 is worth 20 to 30 Swordsmen (2026-11-20, developer:
    "Heroes themselves should be worth around 20-30 units themselves when at max level", then
    "Swordsman, grow by level").** Each level above the first adds a share of the hero's
    level-1 health and weapon damage (`HeroLevelSystem.asset`); level 1 is unchanged. The
    growth is applied by `HeroLevelSystem` from `HeroLevel` against a new `HeroLevelApplied`
    marker, so levelling in the field and returning from a death at a kept level take the same
    path. Ability damage does not grow. Heroes.md § 2.
93. **Sect units are heroic: tougher and dearer in religion points (2026-11-20, developer:
    "They are heroic units so they should have 30-40% more effective hit points ... Make them
    cost more religion points").** Every sect line unit's health is raised by about a third on
    its unit SO (armour is a flat subtraction, so health is effective health). The religion
    price of a sect unit (`sectUnitRp`), of a sect hero (`heroRp`) and of the Petriarchy's three
    armies (`armySmallRp` / `armyMediumRp` / `armyLargeRp`) is raised on
    `FactionReligionPoints.asset`; the armies moved with the unit price so an army never
    undercuts the same units bought singly. **Open:** a hero at level 10 is to be worth 20 to 30
    units — settled by decision 94.
92. **Every sect feature is switched on (2026-11-20, developer: "Turn on all sect features").**
    Sect line units and sect heroes can be trained again (`sectUnitsEnabled` on
    `FactionReligionPoints.asset`), and from Age 1 the Temple offers the adopted sects' units,
    the Petriarchy's Support and Army and its whole research list (the Masses ladder, the
    Litharch ladder, Warrior Priests) — `TempleOffer.asset`. Age 0 is unchanged: the Litharch
    only. Reverses decision 78 (e)'s "leave hidden" and the 2026-11-17 switch-off. **Open:**
    the sect units still carry their pre-decision-71 stats.
91. **One army, fed in the field, razing inward (2026-11-19, developer: "Do it", to the findings
    of the version-by-version investigation; and his insight: "the AI (at higher levels) must
    secure the newly acquired territories with towers before proceeding and take the enemy's
    economy (outskirt territories) so that it can out-produce the enemy").** The same match on
    every release from 0.0.70 and on the 16 November build showed the tagged versions and 16
    November still conquering (bodies of 65-69 soldiers and 8 engines at the capitals), and
    today's build never bringing more than 11 soldiers or any engine within 30 m of one; tower
    count, siege share, building HP and outskirts targeting, each changed alone, restored
    nothing. What differed was the shape of the attack. (a) **Reinforcement groups are back**
    (removed 2026-11-18): while an army lives, the soldiers gathered at home leave as a formed
    group of at least `reinforceMinGroup`, march in formation and merge into the army; units a
    timed-out muster left behind go with the next group. (b) **Outskirts first**:
    `warTargetsKeystones` is off, so a war razes the victim's outlying buildings, objective to
    objective, and goes for the capital once the victim is crippled (the seen-keystone and
    start-capital targets of decisions 86 and 90 apply then). (c) **Secure it first**: on a
    tier with `secureProvincesFirst` (Hard, Expert) a newly held province gets its first towers
    before its extractors. (d) **No strength holds at home** (`waveStrengthHolds` off): a wave
    that has its size is not kept home for reading weaker than the defence. The one-Fortress
    siege rule (decision 69) is unchanged. Game_AI.md § 6ah.
90. **The AI knows where every player started (2026-11-19, developer: "The AI players should know
    the starting position of the other players").** In the decision-89 match Red's war on Blue
    held all game and its waves arrived, but it had never seen Blue's keystones behind the moat,
    so it razed only outlying buildings and no capital ever fell. A war wave now marches on the
    victim's nearest SEEN keystone (decision 86) and, with none seen, on the victim's capital at
    its start position (`TryVictimStartCapital` over `FindEnemyStartHall`). Start positions are
    public, so this is nothing a player does not know. Game_AI.md § 6ah.
89. **On the way, an army fights only soldiers (2026-11-19, developer: "Yes", to the proposal
    after the decision-88 match).** Expert's 147-strong main army was recalled 137 strong to
    chase a single Red worker 55 m off its march, skirmished near home for three minutes and
    never left. A mustering or staging army now takes contact only with enemy combat units (or
    anything that comes at its engines, decision 88); a lone worker, scout or building on the way
    is left to the members beside it, and a member fighting one no longer pulls the whole army
    into an engagement (`marchFightsSoldiersOnly`). At the objective it fights everything as
    before. Game_AI.md § 6ah.
88. **The whole army attacks, and it escorts its engines (2026-11-19, developer, after a
    four-tier Moatwatch match in which every wave of 60-90 broke on a home army of 45-130 plus
    towers: "rules 1 - Send all of the army to attack. 2 - marching armies escort the siege
    engines and attack anything that comes close to them").** (a) No standing home guard
    (`sendWholeArmy`), and as a wave goes the claim squads are released into the army; they
    re-form between waves. The tier caps on wave size (Easy, Normal) stay. (b) A hostile unit
    within `siegeEscortRadius` of one of an army's engines is the army's target at once, whatever
    its strength; a formation already marches at its slowest member, so the engines stay inside
    it. Game_AI.md § 6ah.
87. **The "beaten" verdict and its finishing mode are removed (2026-11-19, developer, after a
    match in which Yellow called him beaten at 13:23 and sent its army at him in groups of 28-47
    that never arrived: "why did it assume I'm beaten??", then "remove this mechanic").** The
    verdict (Game_AI.md § 6u) read land and SEEN soldiers only, so a player banking resources
    instead of fielding an army looked beaten. Gone with it: the finishing push (every wave for
    the capital, the next army sent whenever ten stood at home, faster follow-ups, the token home
    guard, the press past a timeout, the higher retreat ratio, the Conquest plan it forced, the
    Sanctum garrisons and monument squads it pulled) and the ten `finish*` settings. A weak
    neighbour is only the weaker target (decision 84).
86. **The hardest AI must be overwhelming (2026-11-19, developer, after batch 18: "attacks are
    failing because they come too late and too far apart. The AI switches target too often and
    does not follow through"; "if I need a 100 unit army every 3-5 minutes... it has to be"; on
    the plan: armies grow and leave whole, one war until the victim falls, "the capital and the
    religion buildings", retreat later, no timeout while fighting).** Game_AI.md § 6ah. (a) A
    unit-rate governor on the Expert profile: behind its target, units come first in the budget,
    surplus spending yields and extra production is allowed. (b) A war holds until its victim
    falls; only being beaten, a game-ending threat or the Shardroot holder interrupts it; no
    Sanctum detours while it is on. (c) War waves go for the victim's capital, Temple, landmark
    and sect buildings, with every idle engine. (d) At war an army retreats only when badly
    outmatched and never while besieging its victim with engines; fighting missions do not time
    out.
85. **The Vault's bars pay a trickle (2026-11-19, developer: "Instead of being a 5% multiplier, it
    acts as a trickle income of whatever resource is stored, and it can't store supplies. No
    resource type locked behind research", with the first two bars' sizes and per-level
    payments, then "do third and fourth bars as well").** Measured first: a full supply bar paid
    back in minutes, the iron bar in about an hour and the veilstone bar in about three, and no
    AI ever opened bar 2. (a) A bar stores iron or veilstone — whichever is first paid into it —
    never supplies. (b) A FULL bar pays its `vaultBarIncomePerMinute` of that resource every
    minute at the Vault's level; a part-filled bar pays nothing. (c) Sizes and payments are on
    `VaultOfAlmierra_Lvl1..3`; bars 3 and 4 continue the developer's two (each pays its size back
    a little faster at level 3). (d) The Coffers still open bars 2-4 in order; no resource is
    locked behind research. (e) `vaultBarBonus`, `vaultBarResource` and the income multiplier
    are deleted; `VaultBarSystem` pays the trickle as `IncomeSource.VaultBar`; the save format
    is 14. (f) Then (developer: "The last bar can be filled with supplies. Add the research to
    the AI. Don't make it prohibitively expensive"): the last bar also takes supplies; the
    Coffers are much cheaper and on the AI's economy research ladder; the AI deposits any
    resource an open bar takes. Age_0.md § Vault of Almiérra.
84. **The AI rushes, fields its army and picks the weaker player first (2026-11-19, developer:
    "11 minutes with 27 units is not an expert rush AI", "8 minutes is too late for expert!",
    "Add thresholds to the leader rule and the weaker rule. A player is weaker if it has 70%
    less score and stronger if it has 130% score", "do the AI changes as proposed").** Game_AI.md
    § 6ag. (a) Target order: the attacker beating us, a game-ending threat, the Shardroot
    holder, the beaten, a weaker rival, then a stronger one; weaker / stronger by the board
    score the AI knows against its own (`preyScoreShare`, `threatScoreRatio`), read as at most
    70 % and at least 130 % of its own. (b) Rush and Aggressive raise an Age 0 army, attack
    earlier, and a Rush skips the growing-wave bar early; their short waves take the claim
    squads. (c) Every production line trains; a trainer refusal spreads to the other rows.
    (d) The capital's savings pause while the army is far under target. (e) Farms pass the
    open-site cap, Farm levels are valued full, the Market comes with an iron / veilstone glut.
83. **The Farm grows its own hands (2026-11-19, developer: "Farms gradually produce their own
    workers ~1 per minute if they are not full. Workers produced in farms start garrisoned
    automatically. The worker who builds the farm is also automatically garrisoned unless it
    has another action in queue").** In batch 17 and the developer's match after it, every AI's
    Farms stood 60-90 % empty to minute 15: Workers come only from the Fortress, and the
    supplies to train them were what the empty Farms should have been making.
    (a) A finished, living Farm with a free slot produces a Worker every
    `garrisonWorkerSeconds` (new field on the building SO, set on `Farm.asset`), free, straight
    into the slot, made exactly as a trained Worker (research and passives applied). It needs
    population room; without it the Farm waits at the mark. A full Farm's clock restarts.
    (b) The Workers building a Farm go inside when it is finished — the one that finishes it
    and its crewmates — unless they have a queued build or command; a full Farm leaves them
    outside. For every player, the AI included.
    (c) **Open (not decided)**: whether the produced Worker should cost supplies; it is free.
82. **Melee units THROW the torch: a short ranged attack on buildings (2026-11-19, developer:
    "Melee units attack with a torch ranged attack now so the slot rule no longer applies. They
    should have 2 tiles of range").** Decision 79's torch kept its damage but was struck from
    melee reach, so only the ring of attack slots round a building could burn it — about a
    dozen men on a 2 x 2 economy building, the rest of a raid standing behind them.
    (a) A melee unit with a torch above 0 (and melee damage — not the curse's units, not a
    ram) attacks a building from **two build-grid cells of its edge** (`torchReachCells` on
    `MeleeCombatSystem.asset`), stops wherever it first reaches that band, and lands its
    torch on its own attack cooldown (`CombatDamageHelper.ThrowsTorch`, read by the
    `MeleeCombatSystem` plan job).
    (b) **The attack-slot rule no longer applies to them** (`BuildingAttackSlotSystem` skips
    a torch thrower); slots remain for the ram and any melee unit with no torch.
    Navigation_And_Formations.md § 4.1, Combat_Pacing.md § Buildings.
    (c) **It is seen** (developer: "a visible torch, with a red-orange streak, a point light
    and a flash when it lands"): the hit lands on the swing in the simulation, and the
    presentation then draws a burning torch tumbling to the building's edge with a red-orange
    streak and a light, and a flash with a light where it lands — `TorchThrows` (the
    sim-to-presentation queue, never read by the simulation) and `TorchThrowVisual` (its look
    on `TorchThrowVisual.asset`), drawn only where the viewer has vision.
    (d) **Torch values raised to the Age of Empires IV scale** (developer: "raise torch values
    (check AOE4)"), keeping decision 79's role order — Workers and Scouts least, heroes most;
    the values are on the unit SOs.
    (e) **Open**: no throwing animation (the unit plays its attack clip); worker repair under
    attack is unchanged.
81. **The batch-17 AI pass: spend, field, march, expand (2026-11-19, developer: "players are
    not spending all their income", "something is limiting the amount of units", "red player
    has a huge army but never attacks on the Fiendstone map", "players attack with small
    armies", "the players should be building secondary bases on the other territories and
    securing them with towers"; then "The market being at the floor price should not stop the
    players from selling. They just have to sell more of it." and "Do them all. Do not change
    unit prices. Make sure that if a unit can't be trained due to lack of resources, it always
    has a fallback of the same unit type").** No unit price changed.
    (a) **Every unit has a same-type stand-in.** A unit the money cannot pay for (its budget,
    the bank, a savings hold, King Lexor's veilstone) trains the next unit down its own line
    instead: infantry, ranged, cavalry and siege each fall back only inside themselves
    (`compositionRoles` `fallbackUnitId` on `SimpleAISystem.asset`; the Catapult now falls back
    to the Ram). Each line ends in a unit with no veilstone price. The stand-in passes the
    basics cap and counts, for the rest of the think, toward the role it replaces. The old
    paths that swapped an unaffordable role unit for a unit of another line, or left the army
    "saving", are gone (Game_AI.md § 5d). Code: `TryTrainUnitBudgeted`.
    (b) **The Market's floor never stops a sale.** A seller at the floor price sells more lots
    for the same veilstone; nothing refuses the sale. The AI sells its glut at any price
    (`marketSellMinPercent` is deleted).
    (c) **One Fortress means no Fortress step in a province.** The AI's territory walk read its
    own Fortress ceiling (8) instead of the Fortress SO's `maxPerFaction` (1, decision 69), so
    every province reserved a Fortress spot and held its towers and production for it. A
    province now goes extractors, towers, production (Game_AI.md § 5g).
    (d) **A wall never seals its owner in.** A chokepoint gate stretch too short for a
    gatehouse (`AlanthorWall.FreeRunForGate` modules) is left open between its two tower hubs;
    a chokepoint wall wants only the gates it planned; no tower or emplacement converts while a
    gate is still owed and can be cut (Game_AI.md § 3c).
    (e) **The home guard shrinks when it is not needed.** The standing army floor is scaled
    down while the home stands defended by its towers, and further while the faction is at its
    population cap or banks a large reserve; a wave sends side armies only when it is big
    enough for each to matter (`standingFloorDefendedScale`, `standingFloorFullScale`,
    `standingFloorRichSupplies`, `sisterMinWaveUnits`). The muster rule of 2026-11-17 (units not
    at the muster point stay home) is unchanged.
80. **The Temple has no chapels (2026-11-18, developer: "Remove the chapels and chapel slots
    from the temple").** Adopting a sect builds nothing: adoption, the sect's level and its
    unlocked powers were already held on the faction bank (`SectAdoptionState`), so the
    chapels were decoration. Removed: the chapel entities, their art and site decals
    (`Sects/Shared/Chapel/`, the legacy `Resources/Prefabs/Buildings/Chapels/`), the Temple's
    six slots (`TempleChapelSlot`, `TempleOwner`, `TempleChapelRing`, `TempleChapelBuildSystem`,
    `TempleCascadeDestroySystem`), and the slot-bound Shardroot allocation, which nothing in
    the game issued any more (lockstep slot 39 `SectShardrootAlloc` is kept as a no-op). The
    cap on adopted sects stays (`SectConfig.MaxAdoptedSects`). What the player saw as the
    "chapel level" is now called the **sect level** (same RP prices, same rule). **Open:** the
    seven sects with no sect building trained their unit at their chapel; with sect units
    switched off (`sectUnitsEnabled`) nothing is lost today, but they have no trainer until
    they get a building.
79. **Buildings fall the Age of Empires IV way: torches, scratches, siege (2026-11-18, developer:
    "We need to update the armor/damage values to couple with the new units' stats", then
    "Apply these stats. Also rework the building HP. Fortress must have 5000 before
    upgrades").** Replaces decision 62's shares (`meleeVsBuilding` / `rangedVsBuilding` are
    deleted from `MeleeCombatSystem.asset`).
    (a) **The torch**: a melee hit on a building lands the attacker's `torchDamage` (new field
    on the unit SO, stamped as `TorchDamage` by `UnitFactory`) less the building's melee
    armour, instead of its anti-unit damage (`CombatDamageHelper.ScaleVsBuilding`). Set by
    role: Workers / Scouts least, light soldiers, heavy soldiers, sect soldiers, heroes most;
    0 = cannot burn. Every melee unit SO of every culture carries one.
    (b) **Arrows**: building ranged armour at or above a bow's whole attack, so an arrow lands
    one point. Melee armour 0, siege armour unchanged, on the Age 0, Alanthor and sect
    buildings (and the B/C landmarks). The curse's Veilstinger gets a bonus vs Building so its
    pressure on a base survives.
    (c) **Armour is applied at all**: `BuildingFactory` now stamps every building's Defense
    from its SO. Until now only the Mine, the Veilstone Mine, the Renewal raised structures,
    the Healing Ward and the Great Forge set one; every other building fought at 0 armour
    whatever its SO said.
    (d) **Hit points by role** (Combat_Pacing.md § Buildings): the Fortress is the developer's
    figure before its levels, the keystones (Temple, landmarks) next, the four production
    buildings equal, defences, economy, the Watch Tower least. Walls, the Gate and the Wall
    Tower are unchanged (siege-only). Feraldis and Runai culture buildings are untouched
    until their own pass.
    (e) **Open**: chapels still carry a code-side HP and no SO; magic damage (the sect casters)
    hits buildings in full.
78. **The in-match UI pass for Age 0 and Alanthor (2026-11-18, developer's UI list).**
    (a) **Resource panel**: the per-minute rate sits UNDER each amount in
    the same font, with no "/min" (the hover says it is per minute); hovering any row lists its
    income sources; Religion shows an RP-a-minute rate when a Temple, monument or Sanctum pays;
    the panel no longer widens, so nothing in the dock (the selection header included) moves;
    the separator follows the panel's width; the Idle button sits centred under the housing count; the expand toggle is gone (developer: "the expanding resource panel becomes obsolete, which is good! keep the layout").
    (b) **The Religion menu is locked in Age 0** (Religion_Menu.md). (c) **The worker has two
    build panels**: the 4 x 4 palette with fixed rows — Economy, Military, Defense, and a
    reserved purple Research row (developer: "maybe a building with super advanced expensive
    tech, and a blacksmith" — undecided) — and a 3 x 4 religion panel beside it: in Age 0 the
    age-up landmarks and the Temple, from Age 1 one framed column per culture's sect cluster
    (Alanthor, Feraldis, Runai) (Overview.md § actions panel). (d) **A new Age 0 Watch Tower**
    (`Lookout`): cheap, sees far, no attack, no levels, never converted, buildable in every
    age; the Alanthor tower is renamed the **Battle Tower** (id unchanged). Alanthor keep the
    Palisade after age-up. (e) **The Temple offers only the Litharch in Age 0, and the Litharch
    and Warrior Priests from Age 1** (`TempleOffer.asset`); the Masses ladder, the Litharch
    ladder (decision 77), Petriarchy Support / Army and the Temple's sect units are hidden and
    refused for everyone, the AI included, until the developer gives them a home ("Leave hidden
    for now"). (f) **The Litharch casts nothing in Age 0** — Cleanse, Cleanse Pulse and the
    Healing Ward carry `requiresAgeUp`. (g) **The ability bar** shows only for an owned unit
    with a castable ability and never over a worker's build menu. (h) **Units**: formations on
    the top row, stances on the bottom; the stances read **Aggressive / Standard / Defensive**
    (Standard = the old Defensive, Defensive = the old Hold Position; display only, Stances.md
    § 9).
77. **The Litharch gets an upgrade ladder; its tiers open with the sects (2026-11-18, developer:
    "go over the litharch's upgrades and stats, and propose something akin to what we have in the
    other units"; "Litharch tiers unlock with the max level of sects adopted").** The Temple has
    no levels (decision 20), so its RESEARCH level is the highest level among the faction's
    adopted sects (0 with none) — `ResearchHostLevel`, read by the panel, the AI and the research
    command. On it, three tiers of Litharch research like every trainer's: a heal ladder, an
    armour ladder (melee + ranged), veteran / elite health, and skills — Swift Rites and Long
    Reach at II, Twin Blessing at III (Age_0.md § Litharch). Base stats unchanged. Numbers on
    the tech SOs in `TempleOfRidan/Research/`.
76. **The Litharch's wards, and one action grid (2026-11-18, developer: "The burst animation for
    the cleanse/cleanse burst is too polluting. I need it to look like the field hospital's
    aura, but in blue color"; "Field hospital needs a long cooldown (shared amongst all
    litharchs). Change it to Healing ward and make it a building that occupies 1 tile, using the
    same mechanics as the cleanse"; "all buildings should preserve the 4x4 action grid").**
    (a) **The Cleansing Ward and Cleanse Pulse wear a calm blue aura** (a blue copy of the old
    hospital aura, `CleanseWard_Aura.prefab`) instead of the burst rings; the ward's pulses show
    no burst. (b) **The Field Hospital is the Healing Ward**: a one-cell building planted where
    aimed, standing until destroyed and pulsing heals to allies in its radius (numbers on
    `RenewalHealingWard.asset`, `HealingWard.asset`, `HealingWardSystem.asset`); still the Sect
    of Renewal's research at the Mending Hall (tech id `FieldHospital` kept for saves). **Its
    cooldown is shared by all of a faction's Litharchs** — one ward starts it for every
    Litharch, and a new Litharch inherits what is left (Sects.md § Renewal). *Superseded the
    same day: the ward heals slowly, stands until destroyed but breaks at one blow (1 HP on its SO), and each Litharch keeps its own
    cooldown (developer: "the cooldown can be per litharch (the shared one was for permanent
    wards)").* (c) **Every
    building's actions panel is a fixed 4 × 4 grid**: row 0 units (or the building's own
    commands), rows 1-2 research (each ladder in a fixed cell), row 3 overflow, the level-up
    always bottom-right, more than fifteen cells paged; empty cells keep their place
    (Overview.md § The actions panel is a 4 x 4 grid). The 3 × 5 building layouts are gone.
75. **The Battering Ram is open to swords, and the AI stops overbuilding it (2026-11-18,
    developer, after a Moatwatch match: "still an overabundance of battering rams"; "it is too
    hardy vs melee"; "do 1-5. 5 means repairing over time").** (a) **The spill-over leak is
    closed**: when the AI's wanted unit is blocked, the other lines it trains on stop at their
    share of the army and siege at the siege cap — the Siege Yard had trained Rams until refused
    (Red: 1 planned, 62 fielded). (b) **The AI scores a buildings-only attacker as no danger and
    no prize unless it is working a building** (it used to pour its melee into idle Rams).
    (c) **The Ram never gains melee armour**: Iron, Veilstone and Reinforced Plating add ranged
    armour only to it (melee and ranged to the other engines). (d) **Lower HP**: base and
    Iron-Shod Ram's bonus cut (values on `BatteringRam.asset` and `IronShodRam.asset`).
    (e) **Field Repair works**: every siege engine of the researching faction repairs itself over
    time (`FieldRepair.asset`, `RepairHpPerSecond`) — it used to cost a level-3 research and do
    nothing.
74. **The AI micros at a player's pace, and siege does not back away (2026-11-18, developer:
    "archers are fleeing every melee unit ... It causes fights to spread outwards and penalizes
    catapults and archers who need to constantly keep moving to hit targets"; "Shouldn't the
    solution be to restrict the AI's APM to human levels when microing?"; "Implement it").**
    (a) **An action budget**: every AI micro order (kiting pull-back, unit ability cast,
    counter-aware re-target, firing-line move, flank) spends one action; one order to a group is
    one action; each tier earns `actionsPerMinute`, banks `actionBurst` and reacts after
    `reactionSeconds` (the `tactics` block on the four difficulty profiles). Plain army orders are
    free. (b) **Kiting is a group pull-back** behind the own melee line (or toward the own army),
    one order per group of threatened shooters, never straight away from each attacker; per-unit
    kiting on every reload is gone; Normal kites too now (slowly), Easy still does not
    (`AITactics.asset`, Game_AI.md § 6e). (c) **Siege inside its minimum range drops the target
    and takes one beyond it**, or stands; it never walks away — every player's engines, not only
    the AI's (Combat_Pacing.md § Minimum range; `RangedCombatSystem`, `TargetingSystem`).
73. **Less late income, and siege is a support arm (2026-11-18, developer, after a Moatwatch
    match where every AI reached the population cap around twenty minutes and then banked
    supplies and iron to the bank cap: "option a"; "There is too much iron income in mid-late
    game"; "Rams don't need to be replaced, they can still play a role. I just don't think siege
    should take up 30-50% of an army").** (a) **Late supplies come down, early supplies stay**:
    the Guild's level 2 and 3 `slotIncomePerMinute` (`Civs/Alanthor/Buildings/Guild/Guild_Lvl2..3`)
    and the Farm's level 2 and 3 `suppliesPerWorkerPerMinute` (`Farm_Lvl2..3`) are lowered;
    level 1, claims and the capital are untouched (the AI is short of supplies before the cap).
    (b) **Mid-late iron comes down, early iron stays**: the Alanthor Mine's level 2 and 3
    `slotIncomePerMinute` (`Civs/Alanthor/Buildings/Mine/Mine_Lvl2..3`), and on
    `TerritoryIncomeSystem.asset` the iron a held territory pays (`claimIronPerMinute`) and a spent
    Mine's share (`depletedMineShare`); the level 1 Mine, `ironYieldMultiplier` and the Mine
    research multipliers (which also pay the Veilstone Mines) are untouched. (c) **The Battering
    Ram costs supplies as well as iron** (`BatteringRam.asset` `cost`), so an iron glut no longer
    makes it the cheapest thing to train. (d) **The AI's siege share is capped**: every siege
    role together (Ram, Catapult, Trebuchet, Ballista) holds at most `siegeMaxArmyShare` of the
    army plan (`SimpleAISystem.asset`), the excess going to the other lines; the siege program's
    engine floor obeys the same share; the Siegecraft doctrine's `doctrineSiege` weight and the
    Ram row's `baseShare` are halved; a role with nothing of its line trainable hands its share
    to the basic infantry instead of being dropped (dropping it let a lone Ram row become the
    whole non-basic share); and an unaffordable unit is never stood in for by an engine — the
    stand-in is the basic infantry inside the basics cap, or the army waits. Rams are kept.
    (e) The AI's measured income (`AIBudget`) is now taken over the real time between two budget
    ticks; it divided by the think interval, and throttled thinks inflated it several times over.
    (f) The Catapult is back on the Siege Yard (`SiegeYard.asset` `trains[]`, decision 52 cut it);
    the AI plans it as the engine against massed foot. Game_AI.md § 5d.
72. **Workers build and repair by the Age of Empires II crew rule (2026-11-17, developer: "Make a
    worker SO!"; "Do points 1-4 from before. Make sure repairs stop if there are no funds").**
    (a) A Worker's build and repair rates are stats on its unit SO (`Worker.asset`
    `buildSpeed`, `repairSpeed`, copied onto `CanBuild`); they were constants in the two
    systems. (b) **Diminishing returns**: n builders on one site finish in 3T / (n + 2) — one
    takes T, four take T / 2, eight take 0.3 T (`BuildingConstructionSystem.CrewShare`), and the
    same share applies to n repairers on one building. (c) **Repairs are paid as the HP goes
    in**: each HP costs `repairCostShare` of the building's price over its max HP
    (`BuildingRepairSystem.asset`, with the repair reach), once whoever restores it — it used
    to charge each arriving Worker for the whole repair, at a premium over the price. With no
    funds the repair STOPS, and the player is told. Repair also ran at double its stated speed
    (every tick rounded up); the fraction of an HP is carried now. (d) The Worker moves slower
    than a soldier (its speed on the SO; about 0.8 of a Spearman's, the Age of Empires ratio).
    (e) **Support units reviewed the same day** (developer: "Apply all"): the Litharch is a
    fragile, slow healer — fewer HP than a Spearman, the Swordsman's pace so it cannot outrun
    the army it heals, a gentler heal and a dearer price; the Scout (on foot, not mounted) is
    still the fastest infantry but by a quarter, not a half. Values on `Litharch.asset` and
    `Scout.asset`. (f) **The Ledger is DISABLED until further notice** (developer): it is off
    the Fortress's `trains[]`, and the router now refuses any unit missing from its trainer's
    roster, for every command source; its Foreclosure (half of every hostile bank, every ten
    minutes, cast by every level-10 Ledger the moment it was ready) was most of what the long
    test matches spent.
71. **AoE-style units and flat upgrades (2026-11-17, developer: "we are using percentages that
    compound"; "all upgrades are in point format and very small"; "Remove the shard tier (let's
    reserve it for Sect units only)"; "use AOE speeds"; "apply the stats").** The arrow, lance and
    siege tiers (+15 %, +30 %, +75 %) multiplied each other and every flat bonus before them: a
    fully upgraded Archer went from 8 to 32 damage and killed a Cataphract four times faster than
    at base, while infantry ladders were flat. Now, for the Garrison, Archery Range, Royal Stable
    and Siege Yard: (a) **every upgrade is a small flat number**: weapons / arrows / lances +1
    damage a tier (siege shot +5), armour +1 a tier, range +1 tile (2 m) a tier, speed a small
    flat step; no percentages. (b) **Three tiers, one per building level** (Stone L1, Iron L2,
    Veilstone L3), researched; **the Shard tier is gone** — it is reserved for sect units.
    (c) **Armour lines are army-wide:** the Archery Range's Brigandines give EVERY unit +1 ranged
    armour, the Garrison's Gambeson / Hauberk / Harness every unit +1 melee armour; Barding and
    Plating stay with cavalry and siege. (d) **Each level-up gives its building's units flat HP
    and bonus damage** (L2 +10 HP, +2 bonus; L3 +15 HP, +3 bonus — the `Veteran*` / `Elite*`
    grant techs), so counters keep pace with armour. (e) **Units unlock by building level:**
    L1 Archer, Spearman, Outrider, Battering Ram; L2 Swordsman, Crossbowman, Catapult; L3
    Longbowman, Cataphract, Trebuchet (the Longbowman and Catapult return from decision 52). A
    unit unlocked at L1 has three skills (one a level), at L2 two, at L3 one, each researched at
    its level. (f) **Base stats follow Age of Empires IV** at 1 tile = 2 m — costs (AoE food →
    supplies, wood → iron, gold → veilstone), HP, damage, bonus, attack speed, range and
    **movement speed**, kept in AoE IV's proportions but raised ×1.6 the same day (developer: "keep the relative speeds, but increase them so that the spearman moves at 2 tiles (4 m) per second") — AoE's absolute pace read as sluggish at our camera and on our maps. Bonus damage may
    name a compound tag ("Light+Infantry": the target must carry both). All numbers are on the
    unit and tech SOs. (g) Still to build (next build): unit **turning rates** for cavalry and
    siege, and the new skills (Double Shot, Arrow Shower, the automatic Choreographed Volleys,
    Pavise, Fire Arrows, Brace, Couched Lance, Field Repair, Incendiary Shot, Rapid Deployment),
    plus the four-row building panel (units / skills / upgrades / level-up). Arrow Volley and
    the Swordsman's Counter are cut. Age_1_Alanthor.md and Combat_Pacing.md defer to this.
70. **Dominion needs all Sanctums but one (2026-11-17, developer: "OK" to "dominion on most
    Sanctums").** In the Expert batches almost no AI ever held more than one Sanctum; holding
    every one at once happened once in 72 matches. On a map with **three or more** Sanctums
    the dominion countdown starts when one side holds **all but one** of them; on a map with
    one or two it still takes **all**. A cursed Sanctum counts for nobody, as before. The
    count is `SanctumSystem.DominionNeeds`; the AI reads the same number (Game_AI.md § 6t).
69. **A player falls with its Fortress (2026-11-17, developer: "players die when their
    starting fortress dies"; "a fortress must be nearly unkillable unless there is a
    considerable force attacking it (and it must have siege)"; "Players cannot build other
    fortresses, you get one only"; "A fallen fortress leaves a rubble that can be rebuilt";
    "After the fortress has died, players have 5 minutes to rebuild their fortress").**
    Conquest never finished in the Expert batches: a beaten faction lived on through any
    Barracks or Worker it had left. (a) **One Fortress per player, ever** (`maxPerFaction` 1
    on `Fortress.asset`): the starting Shelter, which becomes the Fortress at age-up, is the
    only one. No second Fortress may be raised anywhere; expansion Fortresses are gone.
    (b) **The Fortress is nearly unkillable without siege.** A SIEGE hit lands
    `siegeShare` of its damage and every engine's hit lands, so the damage grows with the
    siege brought (developer, same day: "siege damage must scale with the amount of siege
    equipment" — a first cut that held siege back until a large army stood round the
    Fortress was dropped). As in Age of Empires IV (developer: "do like AOE"), an arrow or
    bolt barely scratches it (`rangedShare`) and a melee blow burns it slowly
    (`meleeShare`). All on `FortressFallSystem.asset`. (c) **A fallen Fortress leaves rubble** where it stood
    (`KeystoneRubble`). The rubble is the only place the Fortress can be built again, the
    old facing kept, and it may be rebuilt there whoever holds the ground. **The rebuild costs
    a SHARE of the price** (`rubbleRebuildCostShare` on `FortressFallSystem.asset`, read by
    `BuildCosts.For` while the owner's rubble lies; developer, 2026-11-17: "landmarks also cost
    resources to rebuild" — it was free for a few hours: "the challenge is defending the
    workers and the repair site before the countdown expires"). **It is rebuilt
    from a button over the rubble** (developer: "a button like the one on the resource nodes.
    Not in the builder UI panel"): with Workers selected, pointing at the rubble raises the
    node-button (`ActionsPanelPrefabBinder.NodeButton.cs`); the Fortress is never in the build
    palette. (d) **The KEYSTONES** (same day, developer: "The same rule applies to the temple
    and the vault. All three must be dead for 5 minutes for a defeat to occur"; then "Sect
    buildings are added to landmarks list"): the Fortress, the Temple of Ridan, the culture's
    landmark (Vault of Almierra, Fiendstone Keep, Thessara's Crossing) and every sect building
    (Reliquary, Mending Hall, Stonehold, Treasury, Muster Yard) are one each, ever; each leaves
    rubble when it falls (`KeystoneRubble`), is built again only there, for the share above,
    from the button over it (a fallen one stays in the palette greyed; the landmark's button is
    made by the node button). A sect building still needs its sect adopted and a standing
    Temple to be raised again.
    No landmark builds itself any more, the age-up one included (developer: "Completely
    remove the auto build from the vault"): Workers raise it, like every keystone rebuild (a
    free self-building Vault kept a faction with nothing else alive for 100 minutes in the
    first test batch).
    The siege-only damage of (b) is the Fortress's alone. (e) **Five minutes to rebuild**
    (`fortressRebuildGraceSeconds` on `EliminationSystem.asset`): a faction that has had its
    Fortress is ELIMINATED once its keystones have ALL been down — fallen, or never built —
    for the grace without one FINISHED again on its rubble (a site does not count). Any one
    standing stops the clock. No early elimination for a faction with no way to rebuild (no Workers, which
    train only at the Fortress): the timer runs out. Its other buildings, Workers and army
    no longer keep it alive. A faction that never had a Fortress (tutorial scenes) keeps
    the old lifelines (a military building or a Worker). The capital's territory LOCK and
    every other Fortress rule stand for the one Fortress.
68. **Shorter supply nodes, four Farms, and an AI that attacks (2026-11-16, developer: "matches
    bog down to stalemates and no one ever finishes a game"; "reduce the lifetime of supply
    nodes"; "the AI is playing too defensive ... not attacking in formed armies anymore";
    "reduce farms to 4").** Measured in the developer's Trine Hollow v2 match (0.0.90, 44
    minutes, Rush Hard and Economic Hard): neither AI launched a single wave. Each kept a
    30-soldier Sanctum army reserved out of an army of 30-45, mustered it for five to ten
    minutes, lost it in one or two, and reserved the next; any hit on a building near home
    put it in Defend, which disbanded every mission and recalled the army (Red 38 times,
    Green 43). The base under siege was paid for by its Farms, which sat behind its towers.
    (a) **Supply nodes hold half as much** (`supplyNodeReserve` on
    `TerritoryIncomeSystem.asset`; rich and poor nodes scale from it). (b) **At most four
    Farms per player** (`maxPerFaction` on `Farm.asset`). (c) **No dedicated Sanctum army.**
    A Sanctum is a WAVE objective: a held Sanctum under threat or one just lost comes first;
    otherwise the best Sanctum not ours takes the wave on the Sanctum road, or on the Mixed
    road when it lies about as near as the base the wave would strike. The wave brings the
    claim's head count, holds on the Sanctum until the claim lands, leaves a **garrison**
    (`sanctumGarrisonSize`) and moves on. The garrison hunts hostiles in the territory, tops
    itself up from soldiers passing through, raises towers, and calls the next wave at once
    when the hostile strength there outweighs it. The Sanctum road no longer holds the
    waves. (d) **Defend is for a real attack:** a building near home under attack puts the
    AI in Defend only when the enemy round it weighs what the Hall ring already asks
    (`defendThreatThreshold`); a smaller raid is the economy defence's and the army stays
    out. (e) **The all-in takes the Sanctum garrisons** with it, as the finishing push
    already did. Reinforcements stay armies that join the main army (the 4 Nov rule); the
    Sanctum's own reinforcement packets are gone with its army. (f) **Waves grow with the
    game** (developer, after the batches: "the wave size over time must have an upwards
    trend!!"): the median wave sat at 25 soldiers from minute 20 to 65 because a wave left as
    soon as ten were idle, so armies bled away and never grew past about 45. A wave is measured
    in POPULATION (cavalry, rams and engines cost 2-5) and must reach a minimum that rises with
    the minutes played (`waveMinPopBase`, `waveMinPopPerMinute`), with no cap but population
    itself: once the faction has no population left to grow into (`waveFullPopSlack`),
    everything drafted goes (developer's math: Farm Workers, builders and scouts leave about
    240 of the 300 population for the army). The all-in obeys it too: armed at minute 22, it
    launched a wave a minute as soon as twelve soldiers stood idle, which kept every army near
    45. (g) **The Market AI buys only for
    a blocked army** (a resource the army's purchases were refused for), never sells below
    `marketSellMinPercent` to fund a purchase (decision 81: that gate is deleted — a sale at
    the floor is never refused), and never buys above `marketBuyMaxPercent`:
    the first rule sold iron at the floor and bought supplies at twice their price 1,622 times
    in six matches. Game_AI.md § 6t, § 5i, § 2j.
67. **The Market trades for veilstone at prices every player shares (2026-11-16, developer:
    "Every resource can be bought using veilstone. Every resource can be sold using
    veilstone. Selling decreases the veilstone value of a material, buying increases it. The
    values are shared across all players, so a player who dumps supplies is actually
    favouring the purchase for other players"; "AI should monitor and try to take advantage
    of manipulating the markets").** Seen in the 15 Nov six-player match (0.0.90): three
    Expert AIs ended with 17,000-30,000 iron and under 2,000 supplies, and the old Market
    (supplies in, iron and veilstone out, fixed prices) had no way back. (a) **Veilstone is
    the money**: supplies and iron are bought with it and sold for it; veilstone itself is
    not traded. (b) **Instant lots** (developer's choice over per-minute rates): a click
    trades one lot, Shift five, Ctrl twenty. The per-minute rates and the surplus-to-iron
    mode (decisions 55 d, 56 c, 59 a-b) are **removed**; their lockstep slots 59 and 60 are
    kept as no-ops. (c) **Each lot moves the price** — up when bought, down when sold — and
    **there is one price per resource for the whole match**. (d) A sale pays a share of the
    price, so buying and selling back loses; prices have a floor and a ceiling and **drift
    back** to their starting value (developer's choice over prices that stay). (e) **The AI
    reads the shared prices every think**: sells a glut, sells into a spike, buys a need,
    buys a dip. Every number is on `Market.asset` (lots, starting prices, step, seller's
    share, floor, ceiling, drift) and `SimpleAISystem.asset` (the AI's levels). Lockstep
    command `MarketTrade`; save format 12. Age_1_Alanthor.md § Market, Game_AI.md § 2j.
66. **The religion menu is a set of power reserves (2026-11-15, developer: "I need it all to
    look like you are filling up a power reserve"; "all buttons should be pictographic"; "must
    not cross the minimap").** Decision 63 made progress automatic; the menu now shows it as
    liquid. A **font** at the top fills toward the next point and pours into one **vessel** per
    sect; a sect's three powers stand beside its vessel at the height that unlocks them and
    light up when the liquid reaches them, and marks II and III on the vessel are the levels.
    Gold is faith (the liquid and whatever it has lit); pale outlines are things to click and
    place (Tithe, adopt, sect building, hero). Every button is a pictogram; the words are in
    the tooltips. The panel hangs from the top of the right edge so it never reaches the
    minimap. The pictograms are drawn by the menu itself (`ReligionPanelBinder.Icons.cs`) and
    are stand-ins until the powers have art. Religion_Menu.md, top block.
65. **The AI saves for its objectives (2026-11-15, developer: "The AI should save for the
    upgrades!!!"; "all tiers should save for objectives. easier tiers should take longer to
    save and save for the wrong objectives sometimes").** Measured on Broken Oath v2 (0.0.89,
    four Hard AIs, 52 minutes): three capitals never left level 1, no production building
    levelled, so no army got a tier and no Trebuchet (Siege Yard level 3) was ever trainable.
    The old saving was a HOLD that asked every spender to wait; the army, huts, walls and
    research drained the bank regardless, and any raid paused it. (a) **A savings pot**
    (`AIBudget.SetSavingsGoal`): while an objective is being saved for, a share of every
    resource's income is taken off the top and is in no wallet, until the pot holds the price.
    (b) **The objectives**: the capital's next level, then one production line's level at a
    time (`AIBuildingUpgradeSystem.SaveForLevel`); a line keeps the pot until it is bought.
    (c) **Every tier saves**, at its own `savingsIncomeShare` (the four difficulty profiles):
    a lower tier saves slower. (d) **A lower tier sometimes saves for the wrong thing**: each
    time the pot is free and a sound objective asks for it, the tier's
    `savingsWrongObjectiveChance` is rolled; on a miss a House, Watch Tower or Vault level
    takes the pot instead. Hard and Expert never miss. (e) **A raid no longer pauses savings**:
    only a collapsed income, an extractor owed a rebuild, or a base being defended does.
    (f) **Battering Rams are not sent after units**: a ram can only hit buildings, and the
    army's focus orders named enemy units for every member; rams now wait behind the army on
    an attack-move and take any building in reach (`SimpleAISystem.Tactics.cs`). Six Hard
    matches after: 21 of 24 capitals reached level 2 (typically minute 10 to 20), 20 of 24 AIs
    levelled a production building, and trebuchets were fielded in four of six. Not addressed:
    matches still seldom close inside 52 minutes, and the Siege Yard is saved for after the
    Range and Stable. Game_AI.md § 6ad.
64. **Iron keeps pace with supplies (2026-11-15, developer: "I am more concerned about the iron
    deficit"; "AI is breaking down due to iron shortages even when it has the whole map
    controlled").** Measured over six 0.0.88 AI matches: from minute 20 a faction's supply
    income keeps growing while its iron income is flat, at about 3.3 supplies for each iron,
    and an army costs about 2.2 for each iron. So the median bank climbs to 4,000 supplies
    while the median iron falls to 200, and four factions in ten sit under 100 iron by minute
    40. (a) **Mines yield more** (`ironYieldMultiplier` on `TerritoryIncomeSystem.asset`), and
    **iron nodes hold more by the same share**, so they last as long as before. (b) **Held
    territory pays more iron** (`claimIronPerMinute`). (c) **The AI uses the Market's surplus
    mode** (decision 59): while its iron is below `marketIronShort` and its army is not waiting
    on supplies, everything above `marketSurplusReserve` supplies becomes iron
    (`SimpleAISystem.Farms.cs`). The Market's price is unchanged. (d) **The AI works all its
    iron** (developer: "is the AI upgrading the iron extractors? Is it building more in the
    terrains it conquers?" - it was not): the difficulty tiers' node share
    (`extractorShare`, 56 % for Normal) no longer applies to iron Mines, which have a share of
    their own (`mineShare` on the four profiles: all of them except Easy at three quarters);
    and the Mine level-up is judged on a payback window of its own
    (`mineLevelPaybackMinutes`), the Farm's window having refused it nearly every time. Not addressed: the AI builds
    its Market late (the Market costs iron) and has no use for a veilstone glut.
63. **Religion progresses by itself; powers reordered; the tree is gone (2026-11-15,
    developer: "the player only selects the sect and things unlock by themselves as points
    roll in"; "gaining points drives progress, but the points are not spent on progress. They
    are rather spent on sect units / heroes or traded for resources or armies at the
    Temple").** (a) Adoption is free and gives nothing on the spot. (b) Every religion point a
    faction EARNS is shared equally between its unfinished adopted sects; each sect's ladder
    (`sectStepRp`: first power, second power, level II + hero, third power, level III) is
    derived from what it has received (`PerSectState.Progress`, `SectProgression`). A refund
    is not an earning (`FactionReligionPointsHelper.Grant` vs `Refund`). (c) The balance stays
    a currency: **sect line units cost a point again** (`sectUnitRp` back to 1, reversing
    2026-11-14), heroes `heroRp`, plus the Temple's trades and armies. (d) The unlock /
    chapel-level purchases are retired (their command kinds parse and buy nothing). (e)
    **Unlock order**: a sect's first power is never a damage power (Sects.md §3a): Embargo,
    Overtime and Emergency Repairs come first; Trespass, Bounty and Ward Discharge second.
    (f) The religion tree (T) is removed; the quick menu shows progress percentages.
    (g) **Ward Discharge** is reworked (wisps as charges, Sects.md § Fortitude) and
    **Revive** repaired. Save format 11. The AI still adopts by its old pacing and no longer
    buys unlocks; how many sects it should hold under the equal split is untuned.
62. **[Superseded by 79: torches replace the shares.] Soldiers chip buildings; they do not take them down (2026-11-15, developer: "most melee
    and ranged units are dealing too much damage to structures. A squad of 15 archers is taking
    down towers").** A melee or ranged (non-siege) hit on any building lands only a **share** of
    its damage: `meleeVsBuilding` and `rangedVsBuilding` on `MeleeCombatSystem.asset`. The
    **"every hit does at least 1" floor no longer applies to buildings** - it was what let a bow
    line add up - and the fraction of a point left over is carried on the building to the next
    hit (`BuildingDamageState.ChipCarry`), so the share is exact. Siege, magic and true damage
    are untouched, and **the curse's units are not scaled** (its pressure on a base is its
    design). `CombatDamageHelper.ScaleVsBuilding`, called by `MeleeCombatSystem` and
    `ProjectileSystem` (which held a code constant and the floor). Save format 10.
61. **Every unit has more health; fights last longer (2026-11-15, developer: "overall HP must be
    raised. I want archers to hurt, but give enough time for a counter").** The 0.0.87 balance
    batch (six AI matches) had a typical soldier dead two minutes after it was trained and the
    Archer returning over three times its price. Rule: **no damage value changes** (archers hurt
    as much as before); every unit SO's `hp` is raised by one factor, and **ranged line units by
    a smaller one**, so an archer line that is reached still breaks quickly while whatever
    walks into its fire lives long enough to arrive. Heroes, siege engines, workers and the
    curse's units take the full factor. Buildings are unchanged. The values are on the unit
    SOs only. Heals, spell damage bands and building weapons were NOT rescaled and are
    therefore relatively weaker; revisit after the next batch.
60. **The Vault's bars belong to resources; its levels raise them; two panels; one-click religion
    (2026-11-15, developer).** (a) **Each Vault bar takes and boosts one resource**
    (`vaultBarResource` on the Vault SO): bars 1 and 2 supplies, bar 3 iron, bar 4 veilstone. A
    deposit of a resource goes only to its own open bars; the player no longer chooses a boost,
    and **Iron Accounts and Veilstone Accounts are deleted**. (b) **Bars open in order** through
    the Coffer chain, as before. (c) **The Vault's two level-ups raise the bonus of a full bar and
    the capacity of every bar** (`vaultBarCapacity`, `vaultBarBonus` on
    `VaultOfAlmierra_Lvl1..3`): the developer set the first and last capacities and the bonus
    steps; the middle level's capacities are halfway between, and bar 2's final capacity was
    read as double its first (the note said "1000"). A bar that was full is part-filled after a
    level-up. Sub-bars still pay as they fill. (d) **The Vault is two square panels**: the
    actions panel on the 4 x 4 grid, and a second square beside it for the four bars
    (Age_0.md § Vault of Almiérra). The Market uses the same 4 x 4 grid. (e) **Every religion
    purchase is one click** (Religion_Menu.md §3); the inline confirm is gone. `VaultStorage`
    lost its boost fields (save format 9, same bump as decision 59).
59. **Late-game iron, the Market and Vault panels, and Bounty (2026-11-15, developer).** From
    the review of a 55-minute Fiendstone Pass v2 game: every iron node on the map ran dry
    between minutes 20 and 55, the player's home iron by minute 33, and from then the player sat
    on the supply cap with no iron to rebuild an army. The developer approved all six fixes and
    asked for both panels to be reworked ("buggy and impede proper play").
    (a) **The Market's rate step is larger** (`rateStep` on `Market.asset`); a click moves one
    step, Shift five, Ctrl twenty. (b) **Surplus to iron:** the Market has a second setting, a
    supply **reserve** (`marketReserveDefault`, stepped by `marketReserveStep`) and an on/off
    switch. While it is on, every second, after the set rates are bought, every supply above the
    reserve is turned into iron at the fixed price. A lockstep command, `SetMarketSurplus`; the
    state rides `MarketRates`. (c) **Territory claims pay more iron** (`claimIronPerMinute` on
    `TerritoryIncomeSystem.asset`). (d) **Iron warnings:** the holder is told once when an iron
    node in their ground falls below `lowIronNodeWarnShare` of its reserve, and every "ran dry"
    notice for iron says how many iron nodes are left in their ground. Presentation only.
    (e) **A spent iron Mine stays standing** (partly reverses decision 56 for iron, as the
    Gatherer's Hut line already did for supplies): it is marked `DepletedMine` (keeping the
    spent node's purity), works no node, pays `depletedMineShare` of the rate it earned there
    (its level's rung, purity, the Mine research, its crew), drawn from no reserve, shows as
    "(Depleted)", and holds but no longer locks its territory. A Mine that still reaches another
    live iron node keeps working that one. Veilstone Mines and Runai Sanctuaries still collapse.
    (f) **Iron nodes hold more** (`ironNodeReserve`). (g) **The Market panel is one column per
    resource** — plus, the resource icon with its rate, minus — and a third column for the
    surplus mode (plus / minus move the reserve, the supplies icon turns it on and off).
    (h) **The Vault panel is one column per resource too** — a deposit plus over the resource
    icon that chooses what the next bar boosts — with Withdraw All beside them, its research on
    the third row, and its four bars in the large grid's free fourth row, where they no longer
    cover the buttons (they were placed for the old authored grid). (i) **Bounty no longer kills
    outright** (developer: "instantly wiping out an army is not fun"; "units that die pay YOU
    half their cost, not the owner"): every hostile unit it reaches takes a share of its maximum
    health and is marked for a few seconds; a marked unit that dies, to anything, pays the
    **caster** half its cost. The sect power's circles are smaller than the sect ladder's and its
    cooldown longer; the Bounty Hunter strikes harder but less often; the Guildmaster's L1
    Bounty strikes like the power. The shares, the mark window, the circles and the hunter's
    interval are `ProsperityBounty` / `Collector` constants beside the other sect-power numbers,
    which still live in code (`SectLeverEffects`), not on an SO. Save format 9.
58. **Half-size units: reach, sight and the v2 maps (2026-11-14, developer).** **(a) and (b)
    REVERTED the same day (developer: "revert the unit scaling, including speed, LOS, range;
    revert the camera position"): unit speed, radius, reach and sight are their 0.0.85 values
    again, `UnitWorldScale.asset` is 1, the formation / steering / animation distances that were
    halved with the units and the camera distances (`CameraController.asset`) are restored, and so
    are the AI ranged-line, stance and Litharch distances. (c) and (d) stand: the v2 maps, the
    building-room probe, the node-spacing cuts and the MirrorPartners fixes.** (a) Units are half
    size and half speed (unit SO `radius` and `speed`); **the 2 m build grid and every building
    footprint are unchanged, on purpose.** (b) **Reach and sight follow the units**, halved on the
    SOs: every unit's `lineOfSight`; a ranged or siege unit's `attackRange` and `minAttackRange`
    (melee reach is unchanged); `siegeRange`, `aoeRadius`, `healRange`; every building's and
    building level's `lineOfSight`, never below the footprint's half side + 2 m (a building sees
    past its own walls) nor above its old value; every enabled authored building attack's
    `range`. The distances that pair with them halve too: the AI's ranged line
    (`AITactics.asset`), the stance leash, melee-engage and assist radii
    (`UnitStanceSystem.asset`), the Litharch's heal search. Ability and sect-power radii are
    not part of this pass. (c) **The maps get v2s**: each map's concept on half the ground with
    the players closer, redrawn so every claimable territory holds its nodes, a Farm with its
    whole field and Houses (the home a full base), proven by the layout kit's building-room
    probe; the originals stay, Yiel Lymwérra has no v2 — [Maps/V2_Maps.md](Maps/V2_Maps.md).
    (d) For them, `homeNodeMinOffset` and the start clearing are cut (assets), and
    `TerritoryResources.MirrorPartners` no longer gives up its mirrored node layout over a
    node-less territory (Sanctum, Empty) that straddles an axis.
57. **The Farm's crop ring, and Farm levels (2026-11-11, developer).** (a) **Every Farm has a crop
    ring**: a circle reaching the Farm SO's `cropRingMeters` out from its footprint edge, less the
    footprint. (b) **A Farm is paid by the ring's free share**: supplies a minute = Workers inside
    × supplies per Worker × the share of the ring not covered by an obstacle, rounded down. The
    obstacles are every building's footprint (any faction, finished or under construction — not
    plans), impassable ground (cliffs, water, steep slopes, trees, rocks, the map edge), resource
    and curse nodes, and ~~every other Farm's ring — an overlap counts against both Farms~~ **the nearer Farm's half of an overlap (amendment below)**. Roads
    and units are not obstacles. **Amended same day (developer): where two rings overlap the land is
    split straight down the middle** — each point belongs to the nearer Farm (exact tie: the lower
    entity index) and is free land for it; nothing is lost by both. Fields of the same owner or of
    allies merge with no boundary between; against a hostile Farm the fence / wall runs along the
    split. The share is counted on a fixed 1 m lattice in whole per-mille,
    recomputed on a cadence by the simulation and kept on the Farm, so every lockstep peer pays the
    same. (c) **Farms level L1-L3** like every Alanthor building (`Farm_Lvl1..3`, faction-wide, L1
    free at age-up, queued through the production queue). A level raises only the supplies per
    Worker (on the level SOs, where the value moved from the Farm SO); the ring and the slots never
    change. (d) **The player sees the field while placing**: placing a Farm shows **Yield NN%** at
    the cursor with its ring drawn; placing any building over one of the player's Farm rings shows
    each affected Farm's **now → after** percentage. Both read the same function the simulation
    pays with. A Farm's badge and the Economy panel show its field. (e) The AI picks the best-field
    site among several, refuses poor ones, keeps its other buildings off its Farm rings while it
    can, and levels Farms as an economy ladder. (f) **Addendum, same day: the level-3 low stone
    wall is real.** It is laid out by the simulation round the ring's rim (and, against a hostile
    Farm, along the split down the middle), with a gap wherever a simulation road crosses it and one facing the Farm's
    door, open to everyone. Each piece blocks movement, has HP and armour, takes damage from any
    attacker (not covered by the Wall Rule), is never auto-acquired, and rebuilds itself after a
    while once the Farm is out of combat (never on a unit or a building). Pieces are not buildings
    and never count against any Farm's yield. The level-2 fence stays drawn only.
    [Age_1_Alanthor.md § Farm](Age_1_Alanthor.md).
56. **Nodes are finite again, twice as long; the Market has no ceiling; a full Farm out-earns a
    level 3 Guild (2026-11-11, developer, after the first decision-55 test).** Supersedes decision
    55 (a). (a) **Every node holds a finite reserve again** — supply, iron and veilstone — drained
    by what its extractor pays, as before decision 55, but **each reserve is double** what it was
    (`TerritoryIncomeSystem.asset`). (b) A spent node collapses with its extractor, as before; the
    Gatherer's Hut line still stands as Depleted. There is **no grace window and no node held by
    trade**: the Trading Outpost stays retired (decision 55 b). (c) **The Market buys any amount a
    minute**: no rate ceiling; the only limit is what the bank can pay at its fixed prices. (d) **A
    Farm with all five Workers pays more than a level 3 Guild hut on a Pure node** (the Farm SO's
    supplies per Worker). (e) The AI buys its military building levels on a schedule by tier, like
    research, instead of weighing them against soldiers (the tier techs now come only from levels,
    decision 53), and runs its Market from its supply surplus.
55. **Nodes never run out; Farms and the Market (2026-11-11, developer, after player feedback:
    "the economy is convoluted — nodes that get spent and disappear, markets everywhere").**
    Supersedes decisions 45, 49, 50 and 51 and the finite-reserve rules of
    [Territory_Claims.md § 11.3-11.4](Territory_Claims.md). (a) **A node is never spent**: no
    reserve, no collapse, no grace window, no remaining bar. An extractor on a node pays its SO
    slot-income ladder for as long as it stands. (b) **The Trading Outpost is retired** with Trade
    Charters, Trade Ledgers and Merchant Houses. (c) **The Farm** (Alanthor, Age 1): built on the
    player's own ground, on no node; **at most six per player**; it holds **up to five Workers**
    and pays supplies in proportion to the Workers inside it (an empty Farm pays nothing). Workers
    are ordered in and ejected like a garrison; a destroyed Farm ejects them. Price, footprint,
    supplies per Worker and the cap are on the Farm SO. (d) **The Market** (Alanthor, Age 1, one per
    player): the player sets how much iron and how much veilstone a minute it buys; it pays
    supplies for them at **fixed prices** on the Market SO. The price never moves with the nodes a
    player holds, or with anything else. When the bank cannot pay, nothing is bought that tick.
    (e) **The intent**: Farms and the Market let a player live without a single node, but holding
    nodes is by far the richer income — a Mine's iron costs several full Farms' worth of supplies
    at the Market. (f) Feraldis and Runai (editions B / C) lose finite nodes too; their own
    staffing and haul rules are untouched.
54. **The Objectives panel names every way to win (2026-11-11, developer, after player feedback:
    "map objectives are not clear").** *(Panel REMOVED 2026-11-17, developer: "Remove the
    objectives and the resource overview panels" — with the per-territory Economy panel of
    decision 56 (g) and the top-centre landmark buttons / age-up pill; the landmarks are placed
    from the Worker's Age 0 build palette.)* After the two opening steps (a special building; a
    culture and age-up) the panel lists the live victories with their progress: conquest (rivals
    left), **Sanctum dominion** (Sanctums held / on the map, and the countdown while it runs), and
    **the Shardroot** (monuments explored, who carries it, the ascension countdown). The old
    "cleanse the curse nodes" line is gone — that victory no longer exists
    ([Territory_Claims.md § 7](Territory_Claims.md)).
53. **Fewer researches: a building's level carries its unit tiers (2026-11-11, developer, after
    player feedback: "too many upgrades").** The weapon / armour / experience tier techs of the
    Garrison, Archery Range, Royal Stable and Siege Yard, and the Fortress's tool tiers, are no
    longer researched: **each building level grants them** (`BuildingLevelDefSO.grantsTechs`) the
    moment the faction reaches that level of that building type — L1 the stone tier and Seasoned,
    L2 the iron and veilstone tiers and Veteran, L3 the shard tier and Elite (the Fortress: L2 iron
    tools, L3 veilstone and shard tools). The level-up prices absorb part of the techs' prices.
    What is still researched changes how a unit plays: Garrison — Charge, Shield Wall; Archery
    Range — Fletching, Arrow Volley, Choreographed Volleys, Deploy Stakes; Royal Stable — Charge,
    War Horn, Full Gallop; Siege Yard — Iron-Shod Ram, Counterweight Tuning, Ranging Shot, Siege
    Screens. Arrow Shower and Reinforced Bolts are cut. About 40 researches remain in Age 0 +
    Alanthor, from 87.
52. **One job per unit (2026-11-11, developer, after player feedback: "too many units of each
    kind without a clear role").** The Alanthor army is two units a kind: **Spearman** (cheap line,
    beats cavalry) and **Swordsman** (heavy line); **Archer** (cheap, range) and **Crossbowman**
    (beats armour); **Outrider** (raider) and **Cataphract** (shock); **Battering Ram** (cheap, close)
    and **Trebuchet** (long range). The **Sentinel, Nobleman, Longbowman, Ballista and Catapult
    leave every roster** (the wall emplacements stay — they are wall features, not units you
    train). The Swordsman grows into the old heavy-infantry role through the Garrison's levels
    (decision 53). Battalions are the next step, after these (developer).
51. **Trading Outposts carry the endgame (2026-11-10, developer: "players in the endgame must earn
    more than players in the early game, but must also venture out and fight for resources").**
    The posts' base trickle is doubled (`TradingOutpostSystem.asset`): a node's four posts, levelled
    and researched, out-earn the mine that worked it, for good. Up to four posts per node is the
    ceiling, so late income grows only with the nodes a player holds — the endgame is won by
    taking and keeping ground, not by sitting on it.
50. **A spent node waits five minutes for a Trading Outpost; every post costs the same
    (2026-11-10, developer).** (a) A node that runs dry with no post beside it is no longer
    destroyed at once: its extractor collapses and the node stays alive for
    `depletedGraceSeconds` (TerritoryIncomeSystem.asset), accepting Trading Outposts and no
    extractor (`NodeDepletedGrace`). A finished post in that window holds it like any node held
    by trade; at the end of the window it collapses, unless a post is still rising beside it,
    which it waits for. (b) **The per-node price ramp is gone**: every Trading Outpost costs its
    SO price. (c) The AI raises a post beside every own node in its window first, past the
    extractor share and the surplus test, saving for it strictly when short; its regular post
    order is window nodes, then iron, veilstone, supply.
    ([Territory_Claims.md § 11.3](Territory_Claims.md)).
49. **Late iron from the Trading Outposts (2026-11-10, developer, after the 0.0.82 rerun).** Iron
    Mines run dry at about minute 25 and late iron fell to ~40 % of mid-game (claims and a 5/min
    post trickle). (a) **A Trading Outpost's trickle climbs steeply with its level** (the level
    SOs' `tradeRateMultiplier`), so a node's posts, levelled, carry its income after the Mine is
    gone. (b) **Two researches at the Trading Outpost** raise every post's trickle: Trade Ledgers
    (post level 2), then Merchant Houses (post level 3, replaces the ledgers' multiplier);
    multipliers on `TradingOutpostSystem.asset`, prices on the tech SOs. (c) **Hard and Expert
    AIs level their Mines and Trading Outposts early** (`earlyExtractorLevels` on the tier
    profile: past the level clock and the value gate), so a node pays at full level and its posts
    at full trickle before it runs dry; every AI researches the two techs from its economy ladder.
48. **Veilstone is for elite and siege; Veilstone Mines level up (2026-11-10, developer, after
    the 1-human-vs-3-Normal Moatwatch game in which every AI stalled).** Every faction earns
    about five supplies per veilstone, at every tier (Expert earns only ~24 % more veilstone than
    Normal, from holding more ground), while the line units were priced mostly in veilstone — so
    supplies piled up and armies stopped growing for EVERYONE, a map fact rather than a player
    choice. (a) **Line units cost no veilstone** (Swordsman, Crossbowman, Longbowman, Outrider);
    **elite units keep about a third** of it (Sentinel, Nobleman, Cataphract); siege engines and
    heroes keep theirs. Each moved unit of veilstone became supplies and iron of equal score
    value. Prices on the unit SOs. (b) **Veilstone Mine levels are a real income ladder** (the
    level SOs' `slotIncomePerMinute`), and the AI levels them like Mines and Guilds. Veilstone
    scarcity is now a decision: an elite- or siege-heavy army needs outcrops held and mines
    levelled. (c) The AI's veilstone-starved army plan holds back only units that cost veilstone.
47. **The Sect of Prosperity reworked (2026-11-10, developer).** Bounty becomes a buy-out:
    every hostile unit it reaches dies at once and its owner is paid its cost (heroes immune).
    A = Bounty in an area growing with the chapel level; B = Overtime in an area; ★ = Plenty
    raises a capturable Counting House (monument capture rules) that trickles supplies + iron,
    then + veilstone, then + religion points, with Fortify's climbing cooldown. Treasury bonus
    25 % -> 15 %. The Collector is renamed the Bounty Hunter and auto-casts Bounty on one weaker
    unit (Run Down retired). Guildmaster: L1 Bounty (area by hero level, replaces Called In),
    L3 Stake +5 % until the building is damaged (90 s), L6 Liquidate unchanged, L10 Windfall
    (40 s: every death near him pays 10 % of its cost; 300 s; replaces Buy Out).
    ([Sects.md § Sect of Prosperity](Sects.md)).
46. **The Vault's four bars (2026-11-10, developer).** The Vault of Almiérra no longer pays
    interest. It holds four bars of `vaultBarCapacity` each, filled in order with any resource
    one for one; each bar boosts one resource the player picks as it starts filling, and a FULL
    bar adds `vaultBarBonus` to that resource's income (stacking). A withdrawal returns
    everything as paid in and empties every bar. Research replaces the banking grades and
    resource unlocks: Second / Third / Fourth Coffer open bars 2-4, Iron / Veilstone Accounts
    let a bar boost iron / veilstone (expensive; prices on the tech SOs). The Vault's levels
    give HP only; `interestPerMinute`, `interestPrincipalCap`, `interestMaxSeconds`, the three
    grade rates and the level `interestMultiplier` are deleted, and so is the deposit lock.
    Supersedes decision 41 ([Age_0.md § Vault](Age_0.md)).
45. **Trade holds the last unit; the 0.0.79 AI behaviour pass (2026-11-10, developer, after
    the 36-match tier x personality batch).** (a) **A node with a finished Trading Outpost beside
    it is never spent**: extraction stops at one unit left, the extractor on it collapses, and the
    node stays for its posts; a node with no post collapses at zero; a held node whose last post
    falls collapses at once; posts may still be built beside a held node, and no extractor may
    ([Territory_Claims.md § 11.3](Territory_Claims.md)). The Gatherer's Hut's Depleted
    exception is withdrawn. (b) **Stop Extracting is removed** (the toggle, `ExtractorStopped`, the
    `SetExtractorStopped` order; the lockstep slot is kept as a no-op for old replays); tutorial
    mission 2 step 2b is now "Research Trade Charters at the Fortress". (c) **A personality is
    played in full on every tier**: `personalityWeight` and the blending toward Balanced are
    deleted. (d) **Turtle and Defensive wall on every tier but Beginner**, past the tier's
    `wallsForbidden`; a chokepoint wall plan whose every slot is unbuildable (Moatwatch's fords)
    is redrawn along the home border. (e) **Sect powers**: the tier's sect limit caps adoption
    only, never the power unlocks and chapel levels; any unit locked on a hostile unit marks a
    fight site, the curse counts at the capital, and fight sites last `fightSiteSeconds`.
    (f) **Rebuild is a real loss**: the army must be below half its recent peak as well as half
    its desired size. (g) **Composition**: `basicsMaxShare` lowered; a locked Ballista hands its
    share to the Catapult, not the Sentinel; the Catapult's base share raised. (h) **A Trading
    Outpost the share allows saves for itself** against unit purchases. (i) **The territory
    development step respects the extractor share** (it filled every node, so every tier beat its
    target). (j) Easy attacks in bigger, rarer waves; Normal's first attack is earlier.
44. **AI tiers by extractor count; Trade Charters at Fortress L1 (2026-11-09, developer, after
    the 0.0.78 batch).** (a) Expert works every node it can, as soon as it can; each tier
    below works 25 % fewer of the nodes it holds (Hard, Normal, Easy, Beginner), so its
    extraction income lands at that share of Expert's — a COUNT of extractors, never a cut to
    what an extractor pays ([Game_AI.md § 2c](Game_AI.md)). (b) Every tier researches Trade
    Charters and builds Trading Outposts under the same share. (c) **Trade Charters is a
    Fortress L1 tech** (was L3) and cheaper (the SO).
43. **Economy pass (2026-11-09, developer, after playing 0.0.77).** (a) **The Guild surveys
    (Iron Surveying I-III, Veilstone Survey I-II) and the Supply Surveys are removed** — no
    income that comes from no node; the late-game income is the Trading Outpost. (b) **Node
    reserves cut** (supply / iron / veilstone on `TerritoryIncomeSystem.asset`) so worked nodes
    run dry within the match. (c) **One territory paid ~1600 supplies a minute**: the Pure
    purity bonus and the Trading Outpost trickle are lowered (`TerritoryResources.asset`,
    `TradingOutpostSystem.asset`). (d) **Extractors drain 1:1 and at a constant rate**: every
    unit a slot pays is a unit taken from its node, at the full rate until the node is empty
    (already the code; restated as a rule). (e) **Every resource node is laid where all four
    Trading Outpost side slots are buildable** (supply and iron as well as veilstone), and
    those footprints are reserved. (f) **The Veilstone Mine gets the iron Mine's
    improvements**: Deep Shafts / Rich Seams raise it too, and it levels L1-L3
    (`Civs/Alanthor/Buildings/VeilstoneMine/VeilstoneMine_Lvl1..3`). (g) **No floating income
    panels over the world**: a collapsible Economy panel on screen lists what each held
    territory pays (`UI/Ingame/Panels/EconomyPanel.cs`; removed 2026-11-17, see decision 54). Tutorial mission 2 step 2a is now
    "Level up your Guilds".
42. **One crystal, nodes worth saving, ground that holds (2026-11-09, developer).** A player
    reported veilstone and veilsteel as confusing and indistinguishable. Decided:
    (a) **Veilsteel is removed.** Veilstone is the only crystal resource. Every veilsteel
    price on an SO became veilstone at **2 veilstone per veilsteel**, added to the item's
    veilstone price. Every veilsteel SOURCE now pays veilstone at the same ratio. These are:
    the Sanctum trickle, the Vault, the Feraldis curse-node bounty and Great Forge Veilsteel
    bay (now iron → veilstone), the Runai Sanctuary's religion-points trade (now religion
    points → veilstone), the Shardroot enshrine price and the Ascended bounty. Veilsteel Survey
    is cut (Veilstone Survey I-II remain). The Score ladder loses its veilsteel rung.
    No UI names veilsteel.
    (b) **Alanthor keep their Veilstone Mines at age-up** (the conversion to Trading Outposts is
    gone), and may build new ones.
    (c) **The Trading Outpost is a late research, not a trade.** *Trade Charters*
    (Fortress research, Fortress L3) unlocks it. Up to four posts stand on the four sides of
    ANY resource node (supply, iron or veilstone), and each trickles that node's resource
    while the node lives. The trickle is **never drawn from the node**. A spent node still
    collapses (Territory_Claims.md § 11.3) and takes its posts with it. While a finished
    post stands beside a node, that node's own empty-node trickle stops (the post replaces
    it), so it no longer drains a node nobody works. There is no trade menu, no routes, no
    Hold, no Buy / Forge / Sell, and the Trade Agreements, Swift Caravans and Veilsteel
    Forging / Export techs are deleted. Buying and selling go through the Temple (Tithe and
    its resource trade). The trickle rates are on `TradingOutpostSystem.asset`, the post's
    level multiplier on `TradingOutpost_Lvl1..3`, and the per-node cost ramp stays.
    (d) **Stop extracting.** Every extractor (Gatherer's Hut line, Mine, Veilstone Mine) has a
    toggle that stops it drawing on its node. The building stands, keeps holding and locking
    the ground, and keeps every survey it pays. The node then pays as an empty node, or
    nothing at all while a post stands beside it. The decision this creates: harvest more,
    or stop now and keep the post's trickle.
    (e) **Ground no longer decays when its soldiers leave** (Territory_Claims.md § 2.2), and
    **cut-off ground no longer wears down** (§ 10). A territory changes hands only by
    conquest, or decays when **the curse holds two or more nodes inside it**.
41. **Vault interest is SIMPLE and CAPPED (2026-10-05, developer):** compounding made the Vault a money printer (2,645 iron deposited became 7.5 million eleven minutes later), so decision 12's "25% interest" model is superseded. (a) Interest is paid on the stored PRINCIPAL — the amount held as of the last deposit — and never on interest already earned; the payout grows linearly. (b) Only the principal up to the Vault SO's `interestPrincipalCap` earns; anything stored above it earns nothing but is still paid out. (c) The banking grades REPLACE the rate with the Vault SO's `coffersRate` / `merchantChartersRate` / `sovereignBondsRate` (the code's hard-coded 50 / 75 / 100% ladder is deleted); the level SOs' `interestMultiplier` still multiplies on top. (d) The base rate `interestPerMinute` and the cap are re-tuned on `Age0/Buildings/VaultOfAlmierra/VaultOfAlmierra.asset`; the techs' descriptions stop stating a number. Code: `VaultStorage.Principal`, set by `CommandRouter.VaultTransferDirect` and read by `VaultInterestSystem`. Rules: [Age_0.md § Vault of Almiérra](Age_0.md), [Age_1_Alanthor.md § Vault](Age_1_Alanthor.md).
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
12. **Vault of Almierra:** keep what ships: 600 HP, 25% interest applying from L1, upgrades 210S+50I / 427S+100I. Move all of it into the Vault SO and level SOs. *(2026-10-05: the interest MODEL and rate are superseded by decision 41 — simple, capped, re-tuned on the SO; "applying from L1" and the level multipliers stand.)*
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
