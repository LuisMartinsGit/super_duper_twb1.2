// TechTreeViz.jsx
// ---------------------------------------------------------------------------
// Tech-tree STRUCTURE visualization for The Waning Border 1.2: the Age 0
// buildings and the Alanthor (Age 1) buildings, the units each one trains,
// the techs each one researches, and the rules that gate them.
//
// A single self-contained React component (no external UI deps, inline styles).
//
// DATA SOURCING RULES
//   * The roster mirrors the ScriptableObjects under Assets/GameData/TechTree/
//     (the single source of game data): building trains[], each tech's
//     researchAt / minBuildingLevel / prerequisites / culture gate, and each
//     unit's minBuildingLevel and weight tags.
//   * This view shows NO stat numbers (costs, HP, damage, times, population).
//     For those open the calculator generated from the SOs:
//     tools/calculator/TechTree.html (built by tools/gen_calculator.py).
//   * Level gates (L1 / L2 / L3) are rules, not stats, so they are shown.
//
// Drop into any React 17/18 project:  import TechTreeViz from './TechTreeViz'
// ---------------------------------------------------------------------------

import React, { useState, useMemo } from "react";

/* ------------------------------------------------------------------ palette */
const C = {
  bg: "#12141c",
  panel: "#1b1e2b",
  panelAlt: "#222637",
  line: "#333a52",
  text: "#e6e9f2",
  dim: "#8a93ad",
  gold: "#d9b45a", // buildings
  blue: "#6c8ebf", // techs
  red: "#c76b66", // units
  purple: "#a98bd0", // choice buildings
  teal: "#5fb3a8", // Alanthor
};

/* =================================================================== DATA == */
// u(name, gate, tags)          a unit a building trains; gate = minBuildingLevel ("" = none)
// t(name, gate, needs, culture) a tech a building researches
const u = (name, gate = "", tags = "") => ({ name, gate, tags });
const t = (name, gate = "", needs = "", culture = "") => ({ name, gate, needs, culture });

const BUILDINGS = [
  // ---------------------------------------------------------------- Age 0 --
  {
    id: "Fortress", name: "Shelter", age: "age0", kind: "core",
    role: "The capital (id Fortress). Becomes the Fortress automatically at age-up for every culture. No Hall, King's Court or Town Hall.",
    trains: [u("Worker", "", "Infantry, Light"), u("Scout", "", "Infantry, Light")],
    research: [t("Stone Tools"), t("Armed Scouts")],
    becomes: ["Fortress (every culture)"],
  },
  {
    id: "Barracks", name: "Barracks", age: "age0", kind: "core",
    role: "Melee infantry. Age 0 is the melee age: there is no Archery Range before age-up.",
    trains: [u("Spearman", "", "Infantry, Heavy")],
    research: [t("Conscription"), t("Stone Weapons")],
    becomes: ["Garrison (Alanthor)"],
  },
  {
    id: "Hut", name: "House", age: "age0", kind: "core",
    role: "Population housing (id Hut).",
    becomes: ["House Lvl 1-3 (Alanthor)"],
  },
  {
    id: "GatherersHut", name: "Gatherer's Hut", age: "age0", kind: "core",
    role: "Territory income. No research in Age 0.",
    becomes: ["Guild (Alanthor)"],
  },
  {
    id: "Mine", name: "Mine", age: "age0", kind: "core",
    role: "Iron mine on an iron node. An Age 0 building for every culture.",
    research: [t("Deep Shafts"), t("Rich Seams", "", "Deep Shafts")],
  },
  {
    id: "VeilstoneMine", name: "Veilstone Mine", age: "age0", kind: "core",
    role: "Veilstone mine on an outcrop. An Age 0 building for every culture.",
    becomes: ["Trading Outpost (Alanthor)"],
  },
  {
    id: "Palisade", name: "Palisade", age: "age0", kind: "core",
    role: "Timber wall every culture builds: hubs joined by Palisade Sections; a section converts to a Wall Gate. Never joins a Stone Wall.",
    parts: ["Palisade Section", "Wall Gate (conversion)"],
  },
  // ---- the three Age 0 choice buildings -----------------------------------
  {
    id: "VaultOfAlmierra", name: "Vault of Almiérra", age: "age0", kind: "choice",
    role: "Resource bank: interest applies from L1 and grows with the Vault's level.",
    research: [
      t("Coffers"), t("Merchant Charters"), t("Sovereign Bonds"), t("Iron Subsidies"),
      t("Veilstone Monetization", "L2"), t("Veilsteel Bonds", "L3"),
    ],
  },
  {
    id: "TempleOfRidan", name: "Temple of Ridan", age: "age0", kind: "choice",
    role: "Religious building. One per faction, no levels. Hosts the Litharch and the heal ladder (the Shrine of Ridan is gone).",
    trains: [u("Litharch", "", "Ranged")],
    research: [
      t("Heightened Masses"), t("Pious Masses", "", "Heightened Masses"),
      t("Fervored Masses", "", "Pious Masses"), t("Warrior Priests"),
    ],
  },
  {
    id: "FiendstoneKeep", name: "Fiendstone Keep", age: "age0", kind: "choice",
    role: "Fortified trainer. Awaiting the Feraldis pass; the tech SO's minBuildingLevel is its only research gate.",
    trains: [u("Spearman", "", "Infantry, Heavy")],
    research: [
      t("Ballista Emplacement"), t("Trebuchet Emplacement", "", "Ballista Emplacement"),
      t("Additional Towers"), t("Reinforced Walls"),
    ],
  },

  // ------------------------------------------------------- Alanthor Age 1 --
  {
    id: "Fortress", name: "Fortress", age: "alanthor", kind: "core", levels: "L1-L3",
    role: "The Shelter after age-up (same entity, id Fortress).",
    trains: [
      u("Worker", "", "Infantry, Light"), u("Scout", "", "Infantry, Light"),
      u("Ledger", "L2", "Infantry, Light"), u("King Lexor", "L3", "Cavalry, Heavy - hero"),
    ],
    research: [
      t("Stone Tools", "", "", "all"), t("Armed Scouts", "", "", "all"),
      t("Scouting Celestarii", "", "", "Alanthor"),
      t("Iron Tools", "L2", "", "Alanthor"), t("Mason Guild", "L2", "", "Alanthor"),
      t("Veilstone Tools", "L3", "", "Alanthor"), t("Veilsteel Tools", "L3", "", "Alanthor"),
    ],
  },
  {
    id: "Barracks", name: "Garrison", age: "alanthor", kind: "core", levels: "L1-L3",
    role: "The Barracks, cultured. Alanthor techs here are culture-gated.",
    trains: [
      u("Spearman", "", "Infantry, Heavy"), u("Swordsman", "L1", "Infantry, Heavy"),
      u("Nobleman", "L2", "Infantry, Heavy"), u("Sentinel", "L3", "Infantry, Heavy"),
    ],
    research: [
      t("Conscription", "", "", "all"), t("Stone Weapons", "", "", "all"),
      t("Iron Weapons", "L1", "Stone Weapons", "Alanthor"),
      t("Veilstone Weapons", "L2", "Iron Weapons", "Alanthor"),
      t("Shard-infused Weapons", "L3", "Veilstone Weapons", "Alanthor"),
      t("Iron Plate", "L1", "", "Alanthor"),
      t("Veilstone Plate", "L2", "Iron Plate", "Alanthor"),
      t("Shard Plate", "L3", "Veilstone Plate", "Alanthor"),
      t("Seasoned Infantry", "L1", "", "Alanthor"),
      t("Veteran Infantry", "L2", "Seasoned Infantry", "Alanthor"),
      t("Elite Infantry", "L3", "Veteran Infantry", "Alanthor"),
      t("Charge", "L2", "", "Alanthor"),
      t("Shield Wall", "L3", "Charge", "Alanthor"),
    ],
  },
  {
    id: "ArcheryRange", name: "Archery Range", age: "alanthor", kind: "core", levels: "L1-L3",
    role: "An Age 1 Alanthor building (no Age 0 form, no cultured rename).",
    trains: [
      u("Archer", "", "Ranged, Light"), u("Crossbowman", "L2", "Ranged, Light"),
      u("Longbowman", "L3", "Ranged, Light"),
    ],
    research: [
      t("Fletching"), t("Choreographed Volleys"),
      t("Stone-Tipped Arrows"),
      t("Iron-Tipped Arrows", "L1", "Stone-Tipped Arrows", "Alanthor"),
      t("Veilstone-Tipped Arrows", "L2", "Iron-Tipped Arrows", "Alanthor"),
      t("Shard-Tipped Arrows", "L3", "Veilstone-Tipped Arrows", "Alanthor"),
      t("Iron Brigandine", "L1", "", "Alanthor"),
      t("Veilstone Brigandine", "L2", "Iron Brigandine", "Alanthor"),
      t("Shard Brigandine", "L3", "Veilstone Brigandine", "Alanthor"),
      t("Seasoned Archers", "L1", "", "Alanthor"),
      t("Veteran Archers", "L2", "Seasoned Archers", "Alanthor"),
      t("Elite Archers", "L3", "Veteran Archers", "Alanthor"),
      t("Arrow Volley", "", "", "Alanthor"),
      t("Arrow Shower", "L2", "Arrow Volley", "Alanthor"),
      t("Deploy Stakes", "L3", "", "Alanthor"),
    ],
  },
  {
    id: "Alanthor_RoyalStable", name: "Royal Stable", age: "alanthor", kind: "core", levels: "L1-L3",
    role: "Alanthor cavalry.",
    trains: [u("Outrider", "L1", "Cavalry, Light"), u("Cataphract", "L3", "Cavalry, Heavy")],
    research: [
      t("Stone-Barded Lances", "", "", "Alanthor"),
      t("Iron-Barded Lances", "L1", "Stone-Barded Lances", "Alanthor"),
      t("Veilstone Lances", "L2", "Iron-Barded Lances", "Alanthor"),
      t("Shard-infused Lances", "L3", "Veilstone Lances", "Alanthor"),
      t("Iron Barding", "L1", "", "Alanthor"),
      t("Veilstone Barding", "L2", "Iron Barding", "Alanthor"),
      t("Shard Barding", "L3", "Veilstone Barding", "Alanthor"),
      t("Seasoned Cavalry", "L1", "", "Alanthor"),
      t("Veteran Cavalry", "L2", "Seasoned Cavalry", "Alanthor"),
      t("Elite Cavalry", "L3", "Veteran Cavalry", "Alanthor"),
      t("Charge (cavalry)", "L1", "", "Alanthor"),
      t("War Horn", "L2", "", "Alanthor"),
      t("Full Gallop", "L3", "War Horn", "Alanthor"),
    ],
  },
  {
    id: "Alanthor_SiegeYard", name: "Siege Yard", age: "alanthor", kind: "core", levels: "L1-L3",
    role: "Alanthor siege. All four engines coexist.",
    trains: [
      u("Ballista", "L1", "Ranged, Siege, Heavy"), u("Catapult", "L1", "Ranged, Siege, Heavy"),
      u("Battering Ram", "L2", "Siege, Heavy"), u("Trebuchet", "L3", "Ranged, Siege, Heavy"),
    ],
    research: [
      t("Stone Shot", "", "", "Alanthor"),
      t("Iron Shot", "L1", "Stone Shot", "Alanthor"),
      t("Veilstone Shot", "L2", "Iron Shot", "Alanthor"),
      t("Shard-infused Shot", "L3", "Veilstone Shot", "Alanthor"),
      t("Iron Plating", "L1", "", "Alanthor"),
      t("Veilstone Plating", "L2", "Iron Plating", "Alanthor"),
      t("Shard Plating", "L3", "Veilstone Plating", "Alanthor"),
      t("Seasoned Crews", "L1", "", "Alanthor"),
      t("Veteran Crews", "L2", "Seasoned Crews", "Alanthor"),
      t("Elite Crews", "L3", "Veteran Crews", "Alanthor"),
      t("Reinforced Bolts", "L1", "", "Alanthor"),
      t("Iron-Shod Ram", "L2", "", "Alanthor"),
      t("Ranging Shot", "L2", "", "Alanthor"),
      t("Siege Screens", "L3", "Ranging Shot", "Alanthor"),
      t("Counterweight Tuning", "L3", "", "Alanthor"),
    ],
  },
  {
    id: "GatherersHut", name: "Guild", age: "alanthor", kind: "core", levels: "L1-L3",
    role: "The Gatherer's Hut, cultured. Guild research is Alanthor-gated; the Surveys let Guild huts produce veilstone and veilsteel.",
    research: [
      t("Iron Surveying I", "", "", "Alanthor"),
      t("Iron Survey II", "L2", "Iron Surveying I", "Alanthor"),
      t("Iron Survey III", "L3", "Iron Survey II", "Alanthor"),
      t("Veilstone Survey I", "L2", "Iron Surveying I", "Alanthor"),
      t("Veilstone Survey II", "L3", "Veilstone Survey I", "Alanthor"),
      t("Veilsteel Survey", "L3", "Veilstone Survey II", "Alanthor"),
      t("Iron Reinforcements", "", "", "Alanthor"),
      t("Veilstone Walls", "L2", "Iron Reinforcements", "Alanthor"),
      t("Veilsteel Pylons", "L3", "Veilstone Walls", "Alanthor"),
    ],
  },
  {
    id: "Alanthor_TradingOutpost", name: "Trading Outpost", age: "alanthor", kind: "core",
    role: "What an Alanthor Veilstone Mine becomes at age-up; Alanthor never mine veilstone. Sits on the outcrop and buys veilstone, or (toggled) forges veilstone into veilsteel.",
    research: [
      t("Trade Agreements I", "", "", "Alanthor"), t("Trade Agreements II", "", "", "Alanthor"),
      t("Trade Agreements III", "", "", "Alanthor"),
      t("Veilsteel Forging", "", "", "Alanthor"), t("Veilsteel Export", "", "", "Alanthor"),
    ],
  },
  {
    id: "Hut", name: "House", age: "alanthor", kind: "core", levels: "L1-L3",
    role: "The House, cultured (House - Lvl 1 / 2 / 3). Population housing.",
  },
  {
    id: "Alanthor_Tower", name: "Watch Tower", age: "alanthor", kind: "core", levels: "L1-L3",
    role: "Stand-alone tower, directly buildable. Garrison slots; units inside add arrows.",
  },
  {
    id: "Alanthor_Wall", name: "Stone Wall", age: "alanthor", kind: "wall", levels: "L1-L3",
    role: "Alanthor-only hub-and-segment wall. Never joins a Palisade. Tower and Gate are conversions of a wall piece.",
    parts: ["Wall Segment", "Wall Tower (conversion)", "Wall Gate (conversion)"],
    research: [
      t("Battlements", "", "", "Alanthor"),
      t("Shielded Ramparts", "", "Battlements", "Alanthor"),
    ],
  },
  {
    id: "Alanthor_BallistaEmplacement", name: "Ballista Emplacement", age: "alanthor", kind: "wall",
    role: "Worker-built on a Stone Wall of L2 or higher.",
    trains: [u("Emplaced Ballista", "L1", "Ranged, Siege, Heavy")],
  },
  {
    id: "Alanthor_TrebuchetEmplacement", name: "Trebuchet Emplacement", age: "alanthor", kind: "wall",
    role: "Worker-built on a Stone Wall of L3.",
    trains: [u("Emplaced Trebuchet", "L1", "Ranged, Siege, Heavy")],
  },
];

const AGE_LABEL = { age0: "Age 0 (every culture)", alanthor: "Alanthor (Age 1)" };

/* ============================================================ small pieces = */

function Chip({ children, color = C.blue, title }) {
  return (
    <span
      title={title}
      style={{
        display: "inline-block", fontSize: 11, lineHeight: 1.4,
        padding: "2px 7px", margin: "2px 4px 2px 0", borderRadius: 5,
        background: "rgba(108,142,191,0.12)", border: `1px solid ${color}`,
        color: C.text, whiteSpace: "nowrap",
      }}
    >
      {children}
    </span>
  );
}

function Gate({ gate }) {
  if (!gate) return null;
  return <span style={{ color: C.gold, marginLeft: 5, fontWeight: 600 }} title="Building-level gate (SO minBuildingLevel)">{gate}</span>;
}

function Section({ title, children }) {
  return (
    <div style={{ marginTop: 10 }}>
      <div style={{ color: C.dim, fontSize: 10, textTransform: "uppercase", letterSpacing: 0.6, marginBottom: 2 }}>{title}</div>
      <div>{children}</div>
    </div>
  );
}

/* ---------------------------------------------------------- building card -- */
function BuildingCard({ b }) {
  const color = b.kind === "choice" ? C.purple : b.age === "alanthor" ? C.teal : C.gold;
  return (
    <div style={{
      background: C.panel, border: `1px solid ${C.line}`, borderLeft: `4px solid ${color}`,
      borderRadius: 10, padding: 14, width: 340, boxSizing: "border-box",
    }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
        <strong style={{ color: C.text, fontSize: 16 }}>{b.name}</strong>
        <span style={{ color: C.dim, fontSize: 10 }}>{b.id}{b.levels ? ` - ${b.levels}` : ""}</span>
      </div>
      <div style={{ color: C.dim, fontSize: 12, margin: "4px 0 6px" }}>{b.role}</div>

      {b.trains?.length > 0 && (
        <Section title="Trains">
          {b.trains.map((x) => (
            <Chip key={x.name} color={C.red} title={x.tags}>{x.name}<Gate gate={x.gate} /></Chip>
          ))}
        </Section>
      )}
      {b.parts?.length > 0 && (
        <Section title="Pieces">
          {b.parts.map((p) => <Chip key={p} color={C.gold}>{p}</Chip>)}
        </Section>
      )}
      {b.research?.length > 0 && (
        <Section title="Research">
          {b.research.map((x) => (
            <Chip key={x.name} color={C.blue} title={x.needs ? `requires ${x.needs}` : undefined}>
              {x.name}<Gate gate={x.gate} />
              {x.needs && <span style={{ color: C.dim, marginLeft: 5 }}>after {x.needs}</span>}
              {x.culture === "Alanthor" && <span style={{ color: C.teal, marginLeft: 5 }}>A</span>}
            </Chip>
          ))}
        </Section>
      )}
      {b.becomes?.length > 0 && (
        <Section title="At age-up becomes">
          {b.becomes.map((r) => <Chip key={r} color={C.teal}>{r}</Chip>)}
        </Section>
      )}
    </div>
  );
}

/* ==================================================================== app == */
export default function TechTreeViz() {
  const [tab, setTab] = useState("all"); // all | age0 | alanthor
  const [q, setQ] = useState("");

  const shown = useMemo(() => {
    const ql = q.toLowerCase();
    const hay = (b) => [b.name, b.role, ...(b.trains || []).map((x) => x.name), ...(b.research || []).map((x) => x.name)].join(" ").toLowerCase();
    return BUILDINGS.filter((b) => (tab === "all" || b.age === tab) && (!ql || hay(b).includes(ql)));
  }, [tab, q]);

  return (
    <div style={{
      background: C.bg, color: C.text, minHeight: "100vh", padding: "24px 28px",
      fontFamily: "'Segoe UI', system-ui, sans-serif",
    }}>
      <header style={{ marginBottom: 18 }}>
        <h1 style={{ margin: 0, fontSize: 24, letterSpacing: 0.5 }}>
          The Waning Border - <span style={{ color: C.gold }}>Age 0</span> and <span style={{ color: C.teal }}>Alanthor</span> Tech Tree
        </h1>
        <p style={{ color: C.dim, margin: "6px 0 0", fontSize: 13, maxWidth: 820 }}>
          Structure only: which building trains which unit and researches which tech, with the
          building-level gates (<span style={{ color: C.gold }}>L1 / L2 / L3</span>), prerequisites and
          culture gates (<span style={{ color: C.teal }}>A</span> = Alanthor only) from the
          ScriptableObjects in <code style={{ color: C.blue }}>Assets/GameData/TechTree/</code>.
          For costs, stats and times see the generated calculator{" "}
          <code style={{ color: C.blue }}>tools/calculator/TechTree.html</code>.
        </p>
      </header>

      {/* controls + legend */}
      <div style={{ display: "flex", flexWrap: "wrap", gap: 10, alignItems: "center", marginBottom: 20 }}>
        {["all", "age0", "alanthor"].map((k) => (
          <button key={k} onClick={() => setTab(k)} style={{
            background: tab === k ? C.gold : C.panel, color: tab === k ? C.bg : C.text,
            border: `1px solid ${C.line}`, borderRadius: 6, padding: "6px 14px",
            cursor: "pointer", fontSize: 13, fontWeight: 600,
          }}>{k === "all" ? "All" : AGE_LABEL[k]}</button>
        ))}
        <input
          value={q} onChange={(e) => setQ(e.target.value)} placeholder="filter..."
          style={{
            background: C.panel, color: C.text, border: `1px solid ${C.line}`,
            borderRadius: 6, padding: "6px 12px", fontSize: 13, minWidth: 160,
          }}
        />
        <div style={{ flex: 1 }} />
        <Legend />
      </div>

      {["age0", "alanthor"].map((age) => {
        const group = shown.filter((b) => b.age === age);
        if (!group.length) return null;
        return (
          <div key={age}>
            <SectionHeader color={age === "alanthor" ? C.teal : C.gold}>{AGE_LABEL[age]}</SectionHeader>
            <Row>{group.map((b) => <BuildingCard key={b.age + b.id} b={b} />)}</Row>
          </div>
        );
      })}

      <footer style={{ color: C.dim, fontSize: 11, marginTop: 30, borderTop: `1px solid ${C.line}`, paddingTop: 12 }}>
        No numbers here by design. Every cost, stat and time lives on the SOs; read them in
        tools/calculator/TechTree.html (generated by tools/gen_calculator.py).
        Runai and Feraldis are not shown.
      </footer>
    </div>
  );
}

function Row({ children }) {
  return <div style={{ display: "flex", flexWrap: "wrap", gap: 14, alignItems: "flex-start" }}>{children}</div>;
}
function SectionHeader({ children, color }) {
  return (
    <h2 style={{ fontSize: 15, margin: "22px 0 12px", color, borderLeft: `3px solid ${color}`, paddingLeft: 8 }}>
      {children}
    </h2>
  );
}
function Legend() {
  const items = [
    ["Age 0 building", C.gold], ["Choice building", C.purple], ["Alanthor building", C.teal],
    ["Unit", C.red], ["Tech", C.blue],
  ];
  return (
    <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
      {items.map(([label, col]) => (
        <span key={label} style={{ display: "flex", alignItems: "center", gap: 5, fontSize: 11, color: C.dim }}>
          <span style={{ width: 11, height: 11, borderRadius: 3, background: col, display: "inline-block" }} />
          {label}
        </span>
      ))}
    </div>
  );
}
