# The Vault and the Ledger: proposal (2026-11-06)

**Status: proposal, not canon.** Nothing here is implemented, and the current
rules in [Age_0.md](Age_0.md) and [Heroes.md](Heroes.md) stand until the
decisions at the end are made. As in every design doc, the numbers belong on the
SOs; this page says what each number does and who holds it.

## Why change them

**The Vault asks nothing of you after it ages you up.** It earns on a capped
deposit, stops earning silently at a hard time limit, and is filled by small
fixed deposits with a long lock after each one. Nothing tells you to come back,
and what it pays is small next to what a late-game bank holds. It should be a key
part of playing Alanthor, and it is the first building an Alanthor player ever
finishes, so it is well placed to be.

**The Ledger is a unique unit with one small job.** It boosts one building for a
third of the time. It has no levels and no revival, and nothing connects it to
the Vault, even though both are about the crown's money.

## The idea

Alanthor is the culture that wins by holding on. **Its money should defend it**:
the Vault is where the crown's wealth sits, the realm draws on it under attack,
and the enemy has a reason to break the walls to reach it. The Ledger is the
official who runs it.

## The Vault

### Bonds replace the open deposit

- **Buying a bond.** Choose a resource, an amount (as a share of the bank, not
  fixed buttons) and a **term**: short, medium or long. A longer term pays a
  better rate. One bond runs at a time; levels and research may allow more.
- **Maturity.** When a bond matures, the player gets a notice and chooses to
  **cash out** or **roll over** into a new term. This is the decision that brings
  the player back every few minutes, and it replaces the silent time limit.
- **Breaking a bond.** Cashing out early returns the deposit without its
  interest, so money is never trapped in an emergency. This replaces the lock.
- **Always visible.** A top-bar chip shows the bond, its time left and its payout,
  and a click selects the Vault. The Vault no longer has to be on screen to be
  remembered.

The existing Vault research stays with the same meaning: the three grades raise
the rate, and the three resource techs open iron, veilstone and veilsteel bonds.
The level multiplier stays too.

### The treasury pays for the defence

While a bond is running, the realm may draw on it:

- **Walls and towers repair themselves**, paid from the bond's interest.
- **Emergency levy:** a Vault action, usable while your territory is under attack,
  that spends the bond to train units faster for a short time.

Both draw on the interest first, so using them costs you the bond's profit, then
its principal. Spending it is a real choice against cashing out.

### The Vault is worth attacking

Destroying the Vault lets the attacker **loot a share of the running bond**; the
rest is lost. Today the deposit just vanishes, which gives the attacker no reason
to go for it and no story for the defender. An Alanthor player now wants the Vault
behind the walls, which is where the culture wants to be. Culture stays permanent
([Overview.md](Overview.md)): losing the Vault costs money, never the age.

## The Ledger

> **2026-11-07: superseded for the Ledger's role.** [Heroes.md](Heroes.md) makes it a fighting
> hero that levels from kills, so this section's non-fighting, money-levelled Ledger does
> not stand. The Vault proposal above is untouched.

The Ledger becomes a **real hero**, one per faction, trained at the Fortress as it
is today. It does not fight, and it levels from **money its work earns**, not
kills. This is the first hero to break the kills-only rule in Heroes.md.

| Level | Ability | Kind | What it does |
|---|---|---|---|
| 1 | **Audit** | Passive aura | Extractors in the Ledger's territory pay more. This replaces babysitting one building, so where the Ledger stands becomes the decision |
| 1 | **Automate Facility** | Active | Kept: one building produces more for a short time, with its lockout |
| 3 | **Compound Interest** | Passive | While the Ledger stands beside the Vault, the running bond earns a better rate. The Ledger chooses between the Vault and the field |
| 6 | **War Requisition** | Active | Turns part of the running bond into instant wall and tower repairs in its territory, at a discount over the treasury's automatic repairs |
| 10 | **Crown Credit** | Active | For a short time the faction may overspend, up to the value of the bond. The debt is taken from later income |

**Dying.** The Ledger uses the same revival choice as King Lexor, and its death
is a real loss because the aura and the bond bonus stop. Being worth hunting, it
needs escort, which is a job for the walls and the Sentinels.

**The name.** Your lore note lists "Ledger" as trade jargon to avoid. If it
becomes a hero, a title such as **the Chancellor** or **the Royal Treasurer** reads
better. Code names can stay, as they did for the Muster Rolls.

## What the AI needs

- Buying bonds, by difficulty, replacing today's deposit shares.
- Rolling over or cashing out at maturity, breaking a bond when in danger.
- Placing the Ledger: beside the Vault in peace, at the richest extractors when
  short of income.
- Using the emergency levy under attack.
- Defending the Vault as a priority target.

## Decisions for you

1. **Loot share:** should a destroyed Vault pay the attacker, or just lose the
   money? I recommend paying the attacker.
2. **Automatic repairs:** should the treasury's wall and tower repairs be always
   on, or a toggle on the Vault? I recommend a toggle, on by default.
3. **XP from money:** may the Ledger level from what it earns? If so, the same
   door opens for the other support heroes (healing for the Surgeon, and so on).
4. **Crown Credit:** is negative bank acceptable? It is the most dramatic ability
   here, but it touches the economy code and every "can I afford this" check. A
   safer version gives a large temporary discount instead.
5. **Rename the Ledger?** And if so, to which title?

Once these are settled, this page merges into Age_0.md, Age_1_Alanthor.md and
Heroes.md, and the numbers go on the SOs.
