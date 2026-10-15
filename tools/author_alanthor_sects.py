"""author_alanthor_sects.py -- the Alanthor sect units, heroes, buildings and
research as SOs (docs/Design/Sects.md section 4, Religion.md 3-4, 2026-10-15).

Run from the repo root:  python tools/author_alanthor_sects.py

What it does
  * rewrites the four sect UNIT SOs in place (Artificer, Scar Guard,
    Sallyguard, Collector) -- stats, cost, tags, display name;
  * creates the four sect HERO SOs (Archivist of Hours, Surgeon, Warden,
    Guildmaster) beside them and registers them in TechTreeCatalog.asset;
  * rewrites the four sect BUILDING SOs (Reliquary, Mending Hall, Stonehold,
    Treasury): trains = [unit, hero], one research, maxPerFaction 1;
  * rewrites the two renamed research SOs (Cartography, Charter of Works).

The SO is the single source of game data (CLAUDE.md): every number below is
a first-pass balance value, and once written the SO wins -- re-running this
script OVERWRITES hand edits to the fields it owns. Hero ability lists are
written by the ability step of the same script (abilities()).

GUIDs: an existing .meta keeps its GUID; a new file gets
md5("twb-alanthor-sects::" + relpath) so a re-run is stable.
"""
import hashlib
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SECTS = os.path.join(ROOT, "Assets", "GameData", "TechTree", "Sects")
CATALOG = os.path.join(ROOT, "Assets", "Resources", "TechTreeCatalog.asset")
S_UNIT = "b6fc2bbfed21e12438424e2ff0b5dd06"
S_ABIL = "b3ecc1043fa62454ebb9521a55d0673d"
ABILITY_CATALOG = os.path.join(ROOT, "Assets", "Resources", "AbilityCatalog.asset")

HDR = ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
       "MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
       "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n"
       "  m_Enabled: 1\n  m_EditorHideFlags: 0\n")

META = ("fileFormatVersion: 2\nguid: {guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n"
        "  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def rel(p):
    return os.path.relpath(p, ROOT).replace("\\", "/")


def guid_for(path):
    meta = path + ".meta"
    if os.path.exists(meta):
        m = re.search(r"^guid: ([0-9a-f]{32})", open(meta, encoding="utf-8").read(), re.M)
        if m:
            return m.group(1)
    return hashlib.md5(("twb-alanthor-sects::" + rel(path)).encode()).hexdigest()


def ys(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def read(p):
    return open(p, encoding="utf-8").read()


def write(p, s):
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(s)


def set_field(s, key, value):
    """Replace a top-level (two-space) scalar field; append it if absent."""
    pat = re.compile(r"^  " + re.escape(key) + r":.*$", re.M)
    line = "  " + key + ": " + value
    if pat.search(s):
        return pat.sub(lambda _m: line, s, count=1)
    return s.rstrip("\n") + "\n" + line + "\n"


def set_block(s, key, lines):
    """Replace a top-level block field (list or mapping) with `lines`."""
    pat = re.compile(r"^  " + re.escape(key) + r":.*\n((?:    .*\n|  - .*\n)*)", re.M)
    body = "  " + key + ":" + ("" if lines else " []") + "\n" + "".join(l + "\n" for l in lines)
    if pat.search(s):
        return pat.sub(lambda _m: body, s, count=1)
    return s.rstrip("\n") + "\n" + body


def cost_lines(supplies=0, iron=0, veilstone=0, veilsteel=0):
    return ["    Supplies: %d" % supplies, "    Iron: %d" % iron,
            "    Veilstone: %d" % veilstone, "    Veilsteel: %d" % veilsteel]


def defense_lines(melee=0, ranged=0, siege=0, magic=0):
    return ["    melee: %d" % melee, "    ranged: %d" % ranged,
            "    siege: %d" % siege, "    magic: %d" % magic]


def list_lines(items):
    return ["  - " + i for i in items]


# --------------------------------------------------------------------------
# Units (limit 20) and heroes (unique)
# --------------------------------------------------------------------------

UNITS = [
    # path, id, name, class, hp, speed, train, dmg, dmgType, armor, defense,
    # cooldown, range, los, radius, cost, pop, tags, bonusVs, pid
    dict(path="Antiquity/Units/Artificer/Artificer.asset", id="Sect_Artificer",
         name="Artificer", cls="ranged", hp=95, speed=2.6, train=24, dmg=24,
         dtype="ranged", armor="infantry_light", defense=(1, 2, 0, 2), cd=3.0,
         rng=14, los=16, radius=0.5, aim=0.8, cost=(70, 50, 10, 0), pop=1,
         tags=["Infantry", "Ranged"], bonus=["Heavy"]),
    dict(path="Renewal/Units/ScarGuard/ScarGuard.asset", id="Sect_ScarGuard",
         name="Scar Guard", cls="melee", hp=170, speed=3.2, train=22, dmg=16,
         dtype="melee", armor="infantry_heavy", defense=(4, 3, 0, 1), cd=1.2,
         rng=0, los=10, radius=0.5, aim=0, cost=(80, 45, 0, 0), pop=1,
         tags=["Infantry", "Heavy"], bonus=[]),
    dict(path="Fortitude/Units/Sallyguard/Sallyguard.asset", id="Sect_Sallyguard",
         name="Sallyguard", cls="melee", hp=150, speed=3.0, train=22, dmg=17,
         dtype="melee", armor="infantry_heavy", defense=(4, 2, 1, 1), cd=1.6,
         rng=0, los=11, radius=0.5, aim=0, cost=(75, 45, 0, 0), pop=1,
         tags=["Infantry", "Heavy"], bonus=["Siege"]),
    dict(path="Prosperity/Units/Collector/Collector.asset", id="Sect_Collector",
         name="Collector", cls="melee", hp=95, speed=3.8, train=18, dmg=11,
         dtype="melee", armor="infantry_light", defense=(1, 1, 0, 0), cd=1.0,
         rng=0, los=12, radius=0.45, aim=0, cost=(60, 25, 0, 0), pop=1,
         tags=["Infantry", "Light"], bonus=[]),
]

HEROES = [
    dict(path="Antiquity/Units/ArchivistOfHours/ArchivistOfHours.asset",
         id="Sect_ArchivistOfHours", name="Archivist of Hours", cls="ranged",
         hp=430, speed=3.0, train=40, dmg=26, dtype="magic", armor="infantry_light",
         defense=(3, 3, 0, 5), cd=1.8, rng=12, los=18, radius=0.55, aim=0.6,
         cost=(150, 80, 60, 0), pop=2, tags=["Infantry", "Ranged", "Hero"], bonus=[],
         prefab_from="Antiquity/Units/Artificer/Artificer.asset", pid=450),
    dict(path="Renewal/Units/Surgeon/Surgeon.asset", id="Sect_Surgeon",
         name="Surgeon", cls="melee", hp=520, speed=3.2, train=40, dmg=28,
         dtype="melee", armor="infantry_heavy", defense=(4, 3, 0, 3), cd=1.3,
         rng=0, los=14, radius=0.55, aim=0, cost=(150, 80, 60, 0), pop=2,
         tags=["Infantry", "Hero"], bonus=[],
         prefab_from="Renewal/Units/ScarGuard/ScarGuard.asset", pid=451),
    dict(path="Fortitude/Units/Warden/Warden.asset", id="Sect_Warden",
         name="Warden", cls="melee", hp=760, speed=2.8, train=40, dmg=30,
         dtype="melee", armor="infantry_heavy", defense=(7, 6, 2, 3), cd=1.5,
         rng=0, los=16, radius=0.6, aim=0, cost=(150, 100, 50, 0), pop=2,
         tags=["Infantry", "Heavy", "Hero"], bonus=["Siege"],
         prefab_from="Fortitude/Units/Sallyguard/Sallyguard.asset", pid=452),
    dict(path="Prosperity/Units/Guildmaster/Guildmaster.asset", id="Sect_Guildmaster",
         name="Guildmaster", cls="melee", hp=460, speed=3.6, train=40, dmg=22,
         dtype="melee", armor="infantry_light", defense=(3, 3, 0, 2), cd=1.1,
         rng=0, los=16, radius=0.55, aim=0, cost=(180, 60, 40, 0), pop=2,
         tags=["Infantry", "Hero"], bonus=[],
         prefab_from="Prosperity/Units/Collector/Collector.asset", pid=453),
]

HERO_ABILITIES = {
    "Sect_ArchivistOfHours": ["Catalogued", "Stamp of Record", "Recall"],
    "Sect_Surgeon": ["Cauterize", "Bloodletting", "Lancet"],
    "Sect_Warden": ["Drillmaster", "Sally Forth", "Challenge"],
    "Sect_Guildmaster": ["Called In", "Stake", "Liquidate"],
}


def unit_fields(s, u, abilities=None):
    s = set_field(s, "m_Name", os.path.splitext(os.path.basename(u["path"]))[0])
    s = set_field(s, "id", u["id"])
    s = set_field(s, "displayName", u["name"])
    s = set_field(s, "unitClass", u["cls"])
    s = set_field(s, "hp", str(u["hp"]))
    s = set_field(s, "speed", str(u["speed"]))
    s = set_field(s, "trainingTime", str(u["train"]))
    s = set_field(s, "damage", str(u["dmg"]))
    s = set_field(s, "damageType", u["dtype"])
    s = set_field(s, "armorType", u["armor"])
    s = set_block(s, "defense", defense_lines(*u["defense"]))
    s = set_field(s, "attackCooldown", str(u["cd"]))
    s = set_field(s, "attackRange", str(u["rng"]))
    s = set_field(s, "lineOfSight", str(u["los"]))
    s = set_field(s, "radius", str(u["radius"]))
    s = set_field(s, "aimTime", str(u["aim"]))
    s = set_block(s, "cost", cost_lines(*u["cost"]))
    s = set_field(s, "populationCost", str(u["pop"]))
    s = set_field(s, "minBuildingLevel", "0")
    s = set_block(s, "tags", ["  - '%s'" % t for t in u["tags"]])
    s = set_block(s, "bonusVsTags", ["  - '%s'" % t for t in u["bonus"]])
    if abilities is not None:
        s = set_block(s, "abilities", list_lines(abilities))
    return s


def hero_asset(h):
    src = read(os.path.join(SECTS, h["prefab_from"]))
    prefab = re.search(r"^  prefab: (.*)$", src, re.M).group(1)
    body = (HDR + "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n" % S_UNIT
            + "  m_Name: x\n  m_EditorClassIdentifier: \n  id: x\n  displayName: x\n"
            "  unitClass: melee\n  hp: 1\n  speed: 1\n  trainingTime: 1\n  damage: 1\n"
            "  damageType: melee\n  armorType: infantry_heavy\n  defense:\n    melee: 0\n"
            "    ranged: 0\n    siege: 0\n    magic: 0\n  attackCooldown: 1\n  attackRange: 0\n"
            "  minAttackRange: 0\n  lineOfSight: 1\n  radius: 0.5\n  aimTime: 0\n  trajectory: \n"
            "  projectileSpeed: 0\n  cost:\n    Supplies: 0\n    Iron: 0\n    Veilstone: 0\n"
            "    Veilsteel: 0\n  populationCost: 1\n  minBuildingLevel: 0\n  buildSpeed: 0\n"
            "  gatheringSpeed: 0\n  healsPerSecond: 0\n  siegeRange: 0\n  siegeCooldown: 0\n"
            "  aoeRadius: 0\n  tags: []\n  bonusVsTags: []\n  abilities: []\n"
            "  prefab: " + prefab + "\n  presentationId: %d\n  animatorController: {fileID: 0}\n" % h["pid"])
    return unit_fields(body, h, HERO_ABILITIES[h["id"]])


# --------------------------------------------------------------------------
# Buildings and research
# --------------------------------------------------------------------------

BUILDINGS = [
    dict(path="Antiquity/Buildings/Reliquary/Reliquary.asset", id="Sect_Reliquary",
         name="Reliquary", role="Antiquity sect building. Trains the Artificer, recruits the Archivist of Hours.",
         trains=["Sect_Artificer", "Sect_ArchivistOfHours"], research=["Cartography"]),
    dict(path="Renewal/Buildings/MendingHall/MendingHall.asset", id="Sect_MendingHall",
         name="Mending Hall", role="Renewal sect building. Trains the Scar Guard, recruits the Surgeon.",
         trains=["Sect_ScarGuard", "Sect_Surgeon"], research=["FieldHospital"]),
    dict(path="Fortitude/Buildings/Stonehold/Stonehold.asset", id="Sect_Stonehold",
         name="Stonehold", role="Fortitude sect building. Trains the Sallyguard, recruits the Warden.",
         trains=["Sect_Sallyguard", "Sect_Warden"], research=["DeepFoundations"]),
    dict(path="Prosperity/Buildings/Treasury/Treasury.asset", id="Sect_Treasury",
         name="Treasury", role="Prosperity sect building. Trains the Collector, recruits the Guildmaster.",
         trains=["Sect_Collector", "Sect_Guildmaster"], research=["CharterOfWorks"]),
]

RESEARCH = [
    dict(path="Antiquity/Buildings/Reliquary/Research/Cartography.asset", id="Cartography",
         name="Cartography", role="Utility", at="Sect_Reliquary",
         desc="Sect of Antiquity. Capturing a territory explores all of it."),
    dict(path="Prosperity/Buildings/Treasury/Research/CharterOfWorks.asset", id="CharterOfWorks",
         name="Charter of Works", role="Economy", at="Sect_Treasury",
         desc="Sect of Prosperity. Extractors cost less to build and to level up."),
]


# --------------------------------------------------------------------------
# Hero abilities (AbilityDefSO). Kinds: AbilityEffectKind 33-44.
# activation 0 Active / 1 Passive; targeting 0 SelfCast, 2 Area, 3 Aura;
# affects 2 AlliedAll, 4 Enemies. aimed = Area + aimedAtPoint (picks a target).
# --------------------------------------------------------------------------

ABILITIES = [
    # folder (under Sects/), asset name, display, kind, value, duration, cooldown, radius, unlock, activation, targeting, affects, aimed
    ("Antiquity/Units/ArchivistOfHours/Abilities/Catalogued", "Catalogued", "Catalogued", 33, 20, -1, 0, 15, 1, 1, 3, 4, 0),
    ("Antiquity/Units/ArchivistOfHours/Abilities/StampOfRecord", "StampOfRecord", "Stamp of Record", 34, 0, 0, 60, 3, 3, 0, 2, 4, 1),
    ("Antiquity/Units/ArchivistOfHours/Abilities/Recall", "Recall", "Recall", 35, 0, 0, 120, 8, 6, 0, 0, 2, 0),
    ("Renewal/Units/Surgeon/Abilities/Cauterize", "Cauterize", "Cauterize", 36, 0, -1, 0, 15, 1, 1, 3, 2, 0),
    ("Renewal/Units/Surgeon/Abilities/Bloodletting", "Bloodletting", "Bloodletting", 37, 25, 10, 60, 8, 3, 0, 0, 2, 0),
    ("Renewal/Units/Surgeon/Abilities/Lancet", "Lancet", "Lancet", 38, 0, 10, 60, 3, 6, 0, 2, 4, 1),
    ("Fortitude/Units/Warden/Abilities/Drillmaster", "Drillmaster", "Drillmaster", 39, 0, -1, 0, 15, 1, 1, 3, 2, 0),
    ("Fortitude/Units/Warden/Abilities/SallyForth", "SallyForth", "Sally Forth", 40, 0, 8, 75, 15, 3, 0, 0, 2, 0),
    ("Fortitude/Units/Warden/Abilities/Challenge", "Challenge", "Challenge", 41, 0, 6, 120, 8, 6, 0, 0, 4, 0),
    ("Prosperity/Units/Guildmaster/Abilities/CalledIn", "CalledIn", "Called In", 42, 20, -1, 0, 15, 1, 1, 3, 4, 0),
    ("Prosperity/Units/Guildmaster/Abilities/Stake", "Stake", "Stake", 43, 0, 30, 60, 4, 3, 0, 0, 0, 0),
    ("Prosperity/Units/Guildmaster/Abilities/Liquidate", "Liquidate", "Liquidate", 44, 50, 0, 180, 3, 6, 0, 2, 0, 1),
]


def num(v):
    return ("%g" % v)


def ability_asset(name, display, kind, value, duration, cooldown, radius, unlock, activation, targeting, affects, aimed):
    lines = [
        "  m_Script: {fileID: 11500000, guid: %s, type: 3}" % S_ABIL,
        "  m_Name: %s" % name,
        "  m_EditorClassIdentifier: ",
        "  abilityName: %s" % ys(display),
        "  activation: %d" % activation,
        "  targeting: %d" % targeting,
        "  affects: %d" % affects,
        "  castTime: 0",
        "  duration: %s" % num(duration),
        "  cooldown: %s" % num(cooldown),
        "  radius: %s" % num(radius),
        "  range: 0",
        "  effects:",
        "  - kind: %d" % kind,
        "    value: %s" % num(value),
        "  aftermath: []",
        "  unlocksAtLevel: %d" % unlock,
        "  damage: 0",
        "  damageType: 0",
        "  aimedAtPoint: %d" % aimed,
        "  icon: {fileID: 0}",
        "  vfxPrefab: {fileID: 0}",
    ]
    return HDR + "\n".join(lines) + "\n"


def register_abilities(guids):
    s = read(ABILITY_CATALOG)
    m = re.search(r"^  abilities:\n((?:  - .*\n)*)", s, re.M)
    block = m.group(1)
    for g in guids:   # APPEND ONLY: the catalog index is the network identity
        if g not in block:
            block += "  - {fileID: 11400000, guid: %s, type: 2}\n" % g
    s = s[:m.start(1)] + block + s[m.end(1):]
    write(ABILITY_CATALOG, s)


def register_units(guids):
    s = read(CATALOG)
    m = re.search(r"^  units:\n((?:  - .*\n)*)", s, re.M)
    block = m.group(1)
    for g in guids:
        if g not in block:
            block += "  - {fileID: 11400000, guid: %s, type: 2}\n" % g
    s = s[:m.start(1)] + block + s[m.end(1):]
    write(CATALOG, s)


def main():
    for u in UNITS:
        p = os.path.join(SECTS, u["path"])
        write(p, unit_fields(read(p), u))
        print("unit    ", rel(p))

    new_guids = []
    for h in HEROES:
        p = os.path.join(SECTS, h["path"])
        os.makedirs(os.path.dirname(p), exist_ok=True)
        g = guid_for(p)
        write(p, hero_asset(h))
        if not os.path.exists(p + ".meta"):
            write(p + ".meta", META.format(guid=g))
        new_guids.append(g)
        print("hero    ", rel(p), g)
    register_units(new_guids)

    for b in BUILDINGS:
        p = os.path.join(SECTS, b["path"])
        s = read(p)
        s = set_field(s, "m_Name", os.path.splitext(os.path.basename(b["path"]))[0])
        s = set_field(s, "id", b["id"])
        s = set_field(s, "displayName", b["name"])
        s = set_field(s, "role", ys(b["role"]))
        s = set_block(s, "trains", list_lines(b["trains"]))
        s = set_block(s, "research", list_lines(b["research"]))
        s = set_field(s, "maxPerFaction", "1")
        write(p, s)
        print("building", rel(p))

    abil_guids = []
    for folder, name, *rest in ABILITIES:
        p = os.path.join(SECTS, folder, name + ".asset")
        os.makedirs(os.path.dirname(p), exist_ok=True)
        g = guid_for(p)
        write(p, ability_asset(name, *rest))
        if not os.path.exists(p + ".meta"):
            write(p + ".meta", META.format(guid=g))
        abil_guids.append(g)
        print("ability ", rel(p), g)
    register_abilities(abil_guids)

    for r in RESEARCH:
        p = os.path.join(SECTS, r["path"])
        s = read(p)
        s = set_field(s, "m_Name", r["id"])
        s = set_field(s, "id", r["id"])
        s = set_field(s, "displayName", ys(r["name"]))
        s = set_field(s, "role", r["role"])
        s = set_field(s, "desc", ys(r["desc"]))
        s = set_field(s, "researchAt", r["at"])
        write(p, s)
        print("research", rel(p))


if __name__ == "__main__":
    main()
