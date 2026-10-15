using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Feraldis
{
    /// <summary>
    /// Every number of the Feraldis economy and religion
    /// (docs/Design/Age_1_Feraldis.md §3-5). The asset is FeraldisEconomy.asset,
    /// beside <see cref="FeraldisEconomy"/>. No field initialisers: the values
    /// live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Feraldis/FeraldisEconomy",
                     fileName = "FeraldisEconomy")]
    public sealed class FeraldisEconomyConfig : ScriptableObject, IComponentConfig
    {
        // ── crews (§3.1) ─────────────────────────────────────────────────
        /// <summary>Crew slots on a Work Camp (the Feraldis Gatherer's Hut).</summary>
        public int crewSlotsWorkCamp;
        /// <summary>Crew slots on an iron Mine.</summary>
        public int crewSlotsMine;
        /// <summary>Crew slots on a Veilstone Mine.</summary>
        public int crewSlotsVeilstoneMine;
        /// <summary>Crew slots on the Great Forge (stokers).</summary>
        public int crewSlotsForge;
        /// <summary>Metres from the building's centre within which a staffing slave counts as crew.</summary>
        public float crewRadius;
        /// <summary>Share of its slot income an extractor with NO crew pays (0..1).</summary>
        public float crewIncomeAtZero;
        /// <summary>Share of its slot income a FULL crew earns (above 1 = out-earns Alanthor's).</summary>
        public float crewIncomeAtFull;
        /// <summary>Metres within which an Overseer drives slaves.</summary>
        public float overseerRadius;
        /// <summary>Work multiplier for slaves near an Overseer (1.25 = +25 %).</summary>
        public float overseerWorkMultiplier;
        /// <summary>Work multiplier per Chains tier (Iron, Veilstone, Veilsteel), multiplied together.</summary>
        public float[] chainsWorkMultipliers;
        /// <summary>Metres from a building's edge at which a walking errand is done.</summary>
        public float errandReach;

        // ── slave cap (§3.2) ─────────────────────────────────────────────
        /// <summary>Slaves the Fortress itself allows, before any Slave Pens.</summary>
        public int slaveCapFortress;
        /// <summary>Extra slaves every Slave Pen allows once Pen Expansion is researched.</summary>
        public int penExpansionBonus;

        // ── raiding (§3.4-3.5) ───────────────────────────────────────────
        /// <summary>Share of a razed enemy building's price (incl. bought levels) paid to the razer. Seeded 0.25.</summary>
        public float razeBountyShare;
        /// <summary>Share of a killed enemy unit's whole cost (all four resources) paid to the killer, as supplies.</summary>
        public float pillageShare;
        /// <summary>Supplies a second drained from an enemy faction while its extractor / Outpost / Fortress is being hit.</summary>
        public float siphonPerSecond;
        /// <summary>Seconds after the last hit during which a building still counts as being raided.</summary>
        public float siphonWindowSeconds;

        // ── the curse pays veilstone (§3.6) ──────────────────────────────
        public int curseKillVeilstoneCrystalling;
        public int curseKillVeilstoneVeilstinger;
        public int curseKillVeilstoneGodsplinter;
        /// <summary>Veilstone a destroyed curse node pays a Feraldis destroyer.</summary>
        public int curseNodeVeilstone;
        /// <summary>Veilsteel a destroyed curse node pays a Feraldis destroyer.</summary>
        public int curseNodeVeilsteel;

        // ── Melt Down (§4.3) ─────────────────────────────────────────────
        /// <summary>Share of a soldier's training cost refunded at full HP (scaled by HP fraction).</summary>
        public float meltRefundShare;

        // ── religion: drawing blood (§5, Religion.md §7) ─────────────────
        /// <summary>Religion pts per resource unit of a sacrificed unit's cost (all four resources summed).
        /// Altar AND field sacrifice; no Altar needed.</summary>
        public float sacrificePtsPerCost;
        /// <summary>Multiplier on a sacrifice's pts when it is made at the Blood Altar or within
        /// <see cref="altarSacrificeRadius"/> of one of the faction's finished Altars.</summary>
        public float altarSacrificeMultiplier;
        /// <summary>Metres from a finished Blood Altar's centre within which a field sacrifice
        /// counts as made at the Altar.</summary>
        public float altarSacrificeRadius;
        /// <summary>Religion pts per resource unit of an enemy unit's cost (all four resources
        /// summed) paid to the Feraldis faction that lands the killing blow.</summary>
        public float killPtsPerCost;
        /// <summary>The fewest pts any enemy unit kill pays (a unit that cost nothing, a summon).</summary>
        public int killPtsMin;
        /// <summary>Radius of the permanent blood pool under the Blood Altar.</summary>
        public float altarBloodRadius;
        /// <summary>Blood (0..1 per splat) re-added under the Altar every refresh.</summary>
        public float altarBloodAmount;
        /// <summary>Seconds between Altar pool refreshes.</summary>
        public float altarBloodInterval;
        /// <summary>Blood (0..1) a field-sacrificed slave leaves where it dies.</summary>
        public float fieldSacrificeBlood;
        /// <summary>Splats in the ring a field sacrifice paints around its centre.</summary>
        public int fieldSacrificeRing;
    }
}
