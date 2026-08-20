// TechTreeViz.jsx
// ---------------------------------------------------------------------------
// Age 0 tech-tree visualization for The Waning Border 1.2.
//
// A single self-contained React component (no external UI deps, inline styles)
// that renders the Age 0 buildings, units, their stat blocks, train rosters,
// research and upgrade paths.
//
// DATA SOURCING RULES (per request):
//   * Numeric stats are read from the actual ScriptableObject .asset files
//     under Assets/GameData/TechTree/  (marked source: "SO").
//   * Where an entity only exists in the design doc (the Crossbowman /
//     Longbowman ranged ladder) its values come from docs/Design/Age_0.md
//     (marked source: "DOC").
//   * When NAMES conflict, the design document wins — so the drawio's
//     Swordsman/Sentinel melee ladder and "Iron/Crystal Survey / Celestar"
//     techs are dropped in favour of the doc's Spearman + the
//     Archer -> Crossbowman -> Longbowman ladder.
//   * When a NUMBER conflicts between the SO and the doc, the SO value is
//     shown (it is the "actual stat") and the doc value is surfaced as a
//     small "doc: X" annotation so the discrepancy stays visible.
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
  blue: "#6c8ebf", // techs / upgrades
  red: "#c76b66", // combat units
  green: "#8bbf6a", // economy units
  purple: "#a98bd0", // support / religious
  doc: "#e0913a", // doc-conflict annotation
};

/* ----------------------------------------------------------- resource icons */
const RES = { Supplies: "🌾", Iron: "⛓️", Crystal: "💎", Veilstone: "🔮", Veilsteel: "⚙️" };
const cost = (c) =>
  Object.entries(c || {})
    .filter(([, v]) => v > 0)
    .map(([k, v]) => `${RES[k] || ""} ${v} ${k}`)
    .join("   ") || "— free —";

/* =================================================================== DATA == */

// Age 0 BUILDINGS — stats from each *.asset ScriptableObject.
const BUILDINGS = [
  {
    id: "Hall",
    name: "Hall",
    kind: "core",
    pid: 100,
    role: "HQ · trains economy · banks resources · researches age-up",
    hp: 1000, docHp: 2400,
    los: 24,
    def: [2, 8, 0, 4],
    radius: 1.6,
    cost: { Supplies: 0 },
    pop: 20, // doc: provides population 20
    trains: ["Worker", "Scout"],
    research: ["Advance to Era II", "Stone Tools", "Wheel Cart"],
    upgradesTo: ["Town Hall", "Trader's Hall", "War Hall"],
  },
  {
    id: "GatherersHut",
    name: "Gatherer's Hut",
    kind: "core",
    pid: 101,
    role: "Area supply aura · Age 0 only · transforms at age-up",
    hp: 300, docHp: 800,
    los: 16,
    def: [2, 2, 0, 0],
    radius: 0.5,
    cost: { Supplies: 120, Iron: 10 },
    aura: "+60 Supplies / min · radius 12",
    research: ["Iron Surveying I-III", "Veilstone Survey I-II", "Veilsteel Survey"],
  },
  {
    id: "Hut",
    name: "House (Hut)",
    kind: "core",
    pid: 102,
    role: "Population housing",
    hp: 650, docHp: 600,
    los: 6, docLos: 14,
    def: [2, 6, 0, 2],
    radius: 1.6,
    cost: { Supplies: 80 },
    pop: 10,
  },
  {
    id: "Barracks",
    name: "Barracks",
    kind: "core",
    pid: 510,
    role: "Trains & upgrades melee infantry",
    hp: 500, docHp: 800,
    los: 18,
    def: [1, 1, 0, 0],
    radius: 1.6,
    cost: { Supplies: 220, Iron: 40 },
    trains: ["Spearman"],
    research: ["Conscription", "Stone Weapons"],
    upgradesTo: ["Garrison"],
  },
  {
    id: "ArcheryRange",
    name: "Archery Range",
    kind: "core",
    pid: 511,
    role: "Ranged ladder — L1 Archer · L2 Crossbowman · L3 Longbowman",
    hp: 500, docHp: 600,
    los: 18,
    def: [1, 1, 0, 0],
    radius: 1.6,
    cost: { Supplies: 180, Iron: 50 },
    trains: ["Archer", "Crossbowman", "Longbowman"],
    research: ["Choreographed Volleys", "Stone-tipped Arrows", "Fletching"],
    upgradesTo: ["Practice Range"],
  },
  // ---- three mutually-exclusive Age 0 choice buildings (start at L1) --------
  {
    id: "VaultOfAlmierra",
    name: "Vault of Almiérra",
    kind: "choice",
    pid: 530,
    role: "Resource bank — compound interest per minute",
    hp: 600, docHp: 1200,
    los: 14,
    def: [0, 8, 0, 0],
    radius: 2,
    cost: { Supplies: 300, Crystal: 100 },
    special: "Interest 25 %/min · Alanthor +30 % · Runai −30 %",
    research: ["Coffers", "Merchant Charters", "Sovereign Bonds", "Iron Subsidies", "Veilstone Monetization", "Veilsteel Bonds"],
  },
  {
    id: "ShrineOfRidan",
    name: "Shrine of Ridan",
    kind: "choice",
    pid: 0,
    role: "Religious · trains Litharch healers · heal aura",
    hp: 600, docHp: 800,
    los: 16,
    def: [0, 6, 0, 0],
    radius: 1.8,
    cost: { Supplies: 300, Crystal: 100 },
    special: "Heal aura 1 %/s (r 10) · Runai +30 % · Feraldis −30 % · +1 RP",
    trains: ["Litharch"],
    research: ["Heightened Masses", "Warrior Priests", "Pious Masses", "Fervored Masses"],
  },
  {
    id: "FiendstoneKeep",
    name: "Fiendstone Keep",
    kind: "choice",
    pid: 540,
    role: "Fortified trainer · supply · arrow volleys",
    hp: 1000, docHp: 2000,
    los: 18,
    def: [2, 2, 0, 0],
    radius: 2.4,
    cost: { Supplies: 300, Crystal: 100 },
    pop: 20,
    special: "Auto-fire 20 dmg / 2 s · range 30 · 4 targets · Feraldis +50 % HP / Alanthor −50 %",
    trains: ["Spearman", "Archer"],
    research: ["Ballista Emplacement", "Trebuchet Emplacement", "Additional Towers", "Reinforced Walls"],
  },
];

// Age 0 UNITS — stats from each *.asset ScriptableObject (or DOC where noted).
const UNITS = [
  {
    id: "Worker", name: "Worker", cls: "human_support", type: "economy", source: "SO", pid: 200,
    trainer: "Hall",
    hp: 70, speed: 6, train: 25, docTrain: 5, dmg: 2, dmgType: "melee",
    armor: "infantry_light", def: [0, 0, 0, 0], cd: 0, range: 1, los: 14,
    cost: { Supplies: 50 }, pop: 1,
    extra: "Build 1.0 · Gather 1.0 · Carry 1 (+5 Wheel Cart)",
    notes: "Unified Builder + Miner.",
  },
  {
    id: "Scout", name: "Scout", cls: "human_scout", type: "economy", source: "SO", pid: 206,
    trainer: "Hall",
    hp: 60, speed: 6, train: 26, docTrain: 4, dmg: 10, docDmg: 2, dmgType: "melee",
    armor: "infantry_light", def: [0, 0, 0, 0], cd: 0, range: 1, los: 40,
    cost: { Supplies: 55 }, pop: 1,
    notes: "Extreme vision (LoS 40) is the role.",
  },
  {
    id: "Spearman", name: "Spearman", cls: "human_melee", type: "combat", source: "SO", pid: 0,
    trainer: "Barracks",
    hp: 120, speed: 5.5, train: 22, docTrain: 7, dmg: 10, dmgType: "melee",
    armor: "infantry_heavy", def: [1, 0, 0, 0], cd: 1.5, range: 1.5, los: 16,
    cost: { Supplies: 80, Iron: 30 }, pop: 1,
    extra: "Bonus vs Cavalry +15",
    notes: "Replaces the drawio 'Swordsman'. Ladder → Seasoned → Veteran → Elite.",
  },
  {
    id: "Archer", name: "Archer", cls: "human_ranged", type: "combat", source: "SO", pid: 202,
    trainer: "ArcheryRange", tier: "L1",
    hp: 90, speed: 5.2, train: 20, docTrain: 15, dmg: 17, dmgType: "ranged",
    armor: "ranged", def: [0, 1, 0, 0], cd: 0, docCd: 2.0, range: 25, minRange: 1, docMinRange: 10, los: 30,
    cost: { Supplies: 50, Iron: 25 }, pop: 1,
    notes: "Baseline ranged. Active skill after Choreographed Volleys.",
  },
  {
    id: "Crossbowman", name: "Crossbowman", cls: "human_ranged", type: "combat", source: "DOC", pid: null,
    trainer: "ArcheryRange", tier: "L2",
    hp: 70, speed: 3.5, train: 18, dmg: 18, dmgType: "ranged",
    armor: "ranged", def: [0, 1, 0, 0], cd: 3.0, range: 18, minRange: 6, los: 22,
    cost: { Supplies: 40, Iron: 35 }, pop: 1,
    notes: "Doc-only (no SO yet). Slow heavy-hitter vs high-HP/armor. PLAYTEST PLACEHOLDER.",
  },
  {
    id: "Longbowman", name: "Longbowman", cls: "human_ranged", type: "combat", source: "DOC", pid: null,
    trainer: "ArcheryRange", tier: "L3",
    hp: 55, speed: 4, train: 25, dmg: 25, dmgType: "ranged",
    armor: "ranged", def: [0, 1, 0, 0], cd: 3.5, range: 40, minRange: 12, los: 35,
    cost: { Supplies: 50, Iron: 40 }, pop: 1,
    notes: "Doc-only (no SO yet). Long-range sniper. PLAYTEST PLACEHOLDER.",
  },
  {
    id: "Litharch", name: "Litharch", cls: "human_support", type: "support", source: "SO", pid: 207,
    trainer: "ShrineOfRidan",
    hp: 120, speed: 5.5, train: 24, docTrain: 7, dmg: 10, docDmg: 0, dmgType: "magic",
    armor: "ranged", def: [0, 0, 0, 2], cd: 0, range: 10, los: 20,
    cost: { Supplies: 100, Iron: 25, Crystal: 10 }, pop: 1,
    heal: 6,
    notes: "Doc: pure healer (0 dmg) until Warrior Priests tech.",
  },
];

const UNIT_COLOR = { economy: C.green, combat: C.red, support: C.purple };

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

function Stat({ label, value, doc }) {
  if (value === undefined || value === null || value === "") return null;
  return (
    <div style={{ display: "flex", justifyContent: "space-between", gap: 8, padding: "2px 0", borderBottom: `1px solid ${C.line}` }}>
      <span style={{ color: C.dim, fontSize: 12 }}>{label}</span>
      <span style={{ color: C.text, fontSize: 12, fontWeight: 600, fontVariantNumeric: "tabular-nums" }}>
        {value}
        {doc !== undefined && doc !== null && String(doc) !== String(value) && (
          <span style={{ color: C.doc, fontWeight: 400, marginLeft: 6, fontSize: 11 }} title="Design-doc value differs from the ScriptableObject">
            (doc: {doc})
          </span>
        )}
      </span>
    </div>
  );
}

const DEF_LABELS = ["Melee", "Ranged", "Siege", "Magic"];
function DefenseRow({ def }) {
  if (!def) return null;
  return (
    <div style={{ display: "flex", gap: 6, marginTop: 6 }}>
      {def.map((v, i) => (
        <div key={i} title={DEF_LABELS[i] + " armor"} style={{
          flex: 1, textAlign: "center", fontSize: 11, padding: "3px 0", borderRadius: 4,
          background: v > 0 ? "rgba(139,191,106,0.15)" : "rgba(255,255,255,0.03)",
          border: `1px solid ${v > 0 ? C.green : C.line}`, color: C.text,
        }}>
          <div style={{ color: C.dim, fontSize: 9 }}>{DEF_LABELS[i][0]}</div>
          <div style={{ fontWeight: 600 }}>{v}</div>
        </div>
      ))}
    </div>
  );
}

/* -------------------------------------------------------------- unit card -- */
function UnitCard({ u }) {
  const color = UNIT_COLOR[u.type];
  return (
    <div style={{
      background: C.panelAlt, border: `1px solid ${C.line}`, borderTop: `3px solid ${color}`,
      borderRadius: 8, padding: 12, width: 250, boxSizing: "border-box",
    }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
        <strong style={{ color: C.text, fontSize: 14 }}>{u.name}</strong>
        <span style={{ display: "flex", gap: 6, alignItems: "center" }}>
          {u.tier && <Chip color={C.blue}>{u.tier}</Chip>}
          <span style={{
            fontSize: 9, padding: "1px 5px", borderRadius: 4, color: u.source === "SO" ? C.green : C.doc,
            border: `1px solid ${u.source === "SO" ? C.green : C.doc}`,
          }} title={u.source === "SO" ? "Stats from ScriptableObject" : "Stats from design doc (no SO yet)"}>{u.source}</span>
        </span>
      </div>
      <div style={{ color: C.dim, fontSize: 11, margin: "2px 0 8px" }}>{u.cls}</div>

      <Stat label="HP" value={u.hp} />
      <Stat label="Damage" value={`${u.dmg} ${u.dmgType}`} doc={u.docDmg !== undefined ? `${u.docDmg} ${u.dmgType}` : undefined} />
      <Stat label="Cooldown" value={u.cd ? `${u.cd}s` : "—"} doc={u.docCd ? `${u.docCd}s` : undefined} />
      <Stat label="Range" value={u.minRange ? `${u.minRange}–${u.range}` : u.range} doc={u.docMinRange ? `${u.docMinRange}–${u.range}` : undefined} />
      <Stat label="Speed" value={u.speed} />
      <Stat label="Line of Sight" value={u.los} />
      <Stat label="Train time" value={`${u.train}s`} doc={u.docTrain !== undefined ? `${u.docTrain}s` : undefined} />
      <Stat label="Heal / s" value={u.heal} />
      <Stat label="Pop" value={u.pop} />
      <Stat label="Cost" value={cost(u.cost)} />
      <DefenseRow def={u.def} />
      {u.extra && <div style={{ color: C.text, fontSize: 11, marginTop: 8 }}>{u.extra}</div>}
      {u.notes && <div style={{ color: C.dim, fontSize: 11, marginTop: 6, fontStyle: "italic" }}>{u.notes}</div>}
    </div>
  );
}

/* ---------------------------------------------------------- building card -- */
function BuildingCard({ b, units }) {
  const trained = (b.trains || []).map((t) => units.find((u) => u.id === t)).filter(Boolean);
  const color = b.kind === "choice" ? C.purple : C.gold;
  return (
    <div style={{
      background: C.panel, border: `1px solid ${C.line}`, borderLeft: `4px solid ${color}`,
      borderRadius: 10, padding: 14, width: 320, boxSizing: "border-box",
    }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
        <strong style={{ color: C.text, fontSize: 16 }}>{b.name}</strong>
        <span style={{ color: C.dim, fontSize: 10 }}>#{b.pid} · {b.kind === "choice" ? "choice L1" : "L0"}</span>
      </div>
      <div style={{ color: C.dim, fontSize: 12, margin: "4px 0 10px" }}>{b.role}</div>

      <Stat label="HP" value={b.hp} doc={b.docHp} />
      <Stat label="Line of Sight" value={b.los} doc={b.docLos} />
      <Stat label="Radius" value={b.radius} />
      <Stat label="Provides pop" value={b.pop} />
      <Stat label="Build cost" value={cost(b.cost)} />
      <DefenseRow def={b.def} />

      {b.aura && <div style={{ marginTop: 8, fontSize: 11, color: C.green }}>🌾 {b.aura}</div>}
      {b.special && <div style={{ marginTop: 8, fontSize: 11, color: C.text }}>✦ {b.special}</div>}

      {trained.length > 0 && (
        <Section title="Trains">
          {trained.map((u) => (
            <Chip key={u.id} color={UNIT_COLOR[u.type]}>{u.name}{u.tier ? ` · ${u.tier}` : ""}</Chip>
          ))}
        </Section>
      )}
      {b.research?.length > 0 && (
        <Section title="Research">
          {b.research.map((r) => <Chip key={r} color={C.blue}>{r}</Chip>)}
        </Section>
      )}
      {b.upgradesTo?.length > 0 && (
        <Section title="Age-up → ">
          {b.upgradesTo.map((r) => <Chip key={r} color={C.gold}>{r}</Chip>)}
        </Section>
      )}
    </div>
  );
}

function Section({ title, children }) {
  return (
    <div style={{ marginTop: 10 }}>
      <div style={{ color: C.dim, fontSize: 10, textTransform: "uppercase", letterSpacing: 0.6, marginBottom: 2 }}>{title}</div>
      <div>{children}</div>
    </div>
  );
}

/* ==================================================================== app == */
export default function TechTreeViz() {
  const [tab, setTab] = useState("all"); // all | buildings | units
  const [q, setQ] = useState("");

  const filteredBuildings = useMemo(
    () => BUILDINGS.filter((b) => !q || (b.name + b.role).toLowerCase().includes(q.toLowerCase())),
    [q]
  );
  const filteredUnits = useMemo(
    () => UNITS.filter((u) => !q || (u.name + u.cls + (u.notes || "")).toLowerCase().includes(q.toLowerCase())),
    [q]
  );

  const core = filteredBuildings.filter((b) => b.kind === "core");
  const choice = filteredBuildings.filter((b) => b.kind === "choice");

  return (
    <div style={{
      background: C.bg, color: C.text, minHeight: "100vh", padding: "24px 28px",
      fontFamily: "'Segoe UI', system-ui, sans-serif",
    }}>
      <header style={{ marginBottom: 18 }}>
        <h1 style={{ margin: 0, fontSize: 24, letterSpacing: 0.5 }}>
          The Waning Border — <span style={{ color: C.gold }}>Age 0</span> Tech Tree
        </h1>
        <p style={{ color: C.dim, margin: "6px 0 0", fontSize: 13, maxWidth: 780 }}>
          Stats read from the live ScriptableObjects in{" "}
          <code style={{ color: C.blue }}>Assets/GameData/TechTree/</code>. Names follow the
          design doc where they conflict; a <span style={{ color: C.doc }}>(doc: X)</span> marker
          flags any stat where <code style={{ color: C.blue }}>docs/Design/Age_0.md</code> disagrees.
        </p>
      </header>

      {/* controls + legend */}
      <div style={{ display: "flex", flexWrap: "wrap", gap: 10, alignItems: "center", marginBottom: 20 }}>
        {["all", "buildings", "units"].map((t) => (
          <button key={t} onClick={() => setTab(t)} style={{
            background: tab === t ? C.gold : C.panel, color: tab === t ? C.bg : C.text,
            border: `1px solid ${C.line}`, borderRadius: 6, padding: "6px 14px",
            cursor: "pointer", fontSize: 13, fontWeight: 600, textTransform: "capitalize",
          }}>{t}</button>
        ))}
        <input
          value={q} onChange={(e) => setQ(e.target.value)} placeholder="filter…"
          style={{
            background: C.panel, color: C.text, border: `1px solid ${C.line}`,
            borderRadius: 6, padding: "6px 12px", fontSize: 13, minWidth: 160,
          }}
        />
        <div style={{ flex: 1 }} />
        <Legend />
      </div>

      {/* buildings */}
      {tab !== "units" && (
        <>
          <SectionHeader color={C.gold}>Core Buildings <em style={{ color: C.dim, fontWeight: 400, fontSize: 13 }}>(pre-culture L0)</em></SectionHeader>
          <Row>{core.map((b) => <BuildingCard key={b.id} b={b} units={UNITS} />)}</Row>

          <SectionHeader color={C.purple}>Choice Buildings <em style={{ color: C.dim, fontWeight: 400, fontSize: 13 }}>(pick one · start L1 · unlock age-up)</em></SectionHeader>
          <Row>{choice.map((b) => <BuildingCard key={b.id} b={b} units={UNITS} />)}</Row>
        </>
      )}

      {/* units */}
      {tab !== "buildings" && (
        <>
          <SectionHeader color={C.red}>Units</SectionHeader>
          {["Hall", "Barracks", "ArcheryRange", "ShrineOfRidan"].map((tr) => {
            const group = filteredUnits.filter((u) => u.trainer === tr);
            if (!group.length) return null;
            const bname = BUILDINGS.find((b) => b.id === tr)?.name || tr;
            return (
              <div key={tr} style={{ marginBottom: 8 }}>
                <div style={{ color: C.dim, fontSize: 12, margin: "10px 0 6px" }}>
                  trained at <strong style={{ color: C.gold }}>{bname}</strong>
                </div>
                <Row>{group.map((u) => <UnitCard key={u.id} u={u} />)}</Row>
              </div>
            );
          })}
        </>
      )}

      <footer style={{ color: C.dim, fontSize: 11, marginTop: 30, borderTop: `1px solid ${C.line}`, paddingTop: 12 }}>
        Damage model: <code>finalDamage = baseDamage × dmgTypeVsArmor × (1 − defense / (defense + 100))</code>.
        &nbsp;Crossbowman / Longbowman are design-doc PLAYTEST PLACEHOLDER values (no ScriptableObject yet).
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
    ["Economy unit", C.green], ["Combat unit", C.red], ["Support unit", C.purple],
    ["Building / tech", C.gold], ["doc conflict", C.doc],
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
