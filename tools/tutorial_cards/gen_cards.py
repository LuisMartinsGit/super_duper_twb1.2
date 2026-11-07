#!/usr/bin/env python3
"""Build the tutorial's infographic cards.

    python tools/tutorial_cards/gen_cards.py [--only 12,T2]

Writes docs/Tutorial/Cards/<lang>/card_<id>.png (1600 x 900, en and pt) and a contact sheet,
and copies every card the demo may show to Assets/Resources/UI/TutorialCards/<lang>/, where
the Field Guide loads them on demand (T6 and T7 stay out: they name other editions' content). The tech tree
cards and every stated value are read from the SOs at build time, so re-running after a
balance change is all a card ever needs. Rules text lives here; numbers do not. Needs
Chrome for rendering and Pillow for the contact sheet.
"""
import argparse, glob, hashlib, html, os, re, shutil, subprocess, sys, tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pt import PT

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "Assets")
T = os.path.join(A, "GameData", "TechTree")
LANG = sys.argv[sys.argv.index("--lang") + 1] if "--lang" in sys.argv else "en"
BASE = os.path.join(ROOT, "docs", "Tutorial", "Cards")
OUT = os.path.join(BASE, LANG)
GAME = os.path.join(A, "Resources", "UI", "TutorialCards", LANG)
NOT_IN_GAME = {"T6", "T7"}
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
FONTS = "file:///" + os.path.join(A, "Synty", "InterfaceFantasyMenus", "Fonts").replace(os.sep, "/")
DIAMOND = "file:///" + os.path.join(A, "Synty", "InterfaceFantasyWarriorHUD", "Sprites", "FantasyWarrior", "SPR_FantasyWarrior_Frame_Diamond_01.png").replace(os.sep, "/")

# ── Language ─────────────────────────────────────────────────────────────
MISSING, _translated = set(), set()
def tr(s):
    """English in, this card's language out. A missing key fails the build at the end."""
    if LANG == "en" or s in _translated: return s
    if s not in PT: MISSING.add(s); return s
    _translated.add(PT[s]); return PT[s]
def fmt(s, *args):
    out = tr(s) % args
    _translated.add(out)
    return out

_GAME_NAMES = {}
for _f in glob.glob(os.path.join(A, "Scripts", "Core", "Localization", "Loc.Pt.*.cs")):
    for _k, _v in re.findall(r't\["([^"\n]+)"\] = "([^"\n]+)";', open(_f, encoding="utf8").read()):
        _GAME_NAMES[_k] = _v
def name(s):
    """A unit, building or research name: the game's own translation, else as is."""
    return s if LANG == "en" else _GAME_NAMES.get(s, s)

# ── Data from the SOs ────────────────────────────────────────────────────
def unq(s):
    s = s.strip()
    if len(s) > 1 and s[0] == '"' and s[-1] == '"':
        return re.sub(r"\\x([0-9A-Fa-f]{2})", lambda m: chr(int(m.group(1), 16)), s[1:-1])
    return s.strip("'")

def read_sos():
    units, buildings, techs = {}, {}, []
    for f in glob.glob(os.path.join(T, "**", "*.asset"), recursive=True):
        t = open(f, encoding="utf8", errors="ignore").read().replace("\r\n", "\n")
        def g(k):
            m = re.search(r"^  %s: *(.*)$" % k, t, re.M)
            return unq(m.group(1)) if m else ""
        rel = os.path.relpath(f, T).replace(os.sep, "/")
        if "unitClass:" in t and "attackCooldown" in t:
            units[g("id")] = g("displayName") or g("id")
        elif "researchAt:" in t:
            techs.append(dict(at=g("researchAt"), name=g("displayName") or g("techName") or g("id") or g("name"),
                              lvl=g("minBuildingLevel"), rel=rel))
        elif re.search(r"^  trains:", t, re.M):
            m = re.search(r"^  trains:\n((?:  - .*\n)*)", t, re.M)
            tr = [unq(x) for x in re.findall(r"^  - (.+)$", m.group(1), re.M)] if m else []
            buildings[g("id")] = dict(name=g("displayName") or g("id"), trains=tr, rel=rel)
    return units, buildings, techs

UNITS, BUILDINGS, TECHS = read_sos()

def num(rel, key):
    t = open(os.path.join(A, rel), encoding="utf8", errors="ignore").read()
    m = re.search(r"^  %s: *(\S+)" % key, t, re.M)
    if not m: sys.exit("missing %s in %s" % (key, rel))
    return float(m.group(1))

ASCENSION_MIN = int(num("GameData/TechTree/Age0/Buildings/TempleOfRidan/ShardrootEmpowerment.asset", "ascensionSeconds") // 60)
SECT_RP = int(num("Scripts/Economy/FactionReligionPoints.asset", "sectUnitRp"))

def unit_names(ids, keep=lambda i: True):
    return [name(UNITS.get(i, re.sub(r"^(Feraldis|Runai|Alanthor|Sect)_", "", i))) for i in ids if keep(i)]

def techs_at(at, culture=None, exclude=("Civs/Feraldis", "Civs/Runai")):
    out = []
    for x in TECHS:
        if x["at"] != at: continue
        if culture:
            if ("Civs/" + culture) not in x["rel"]: continue
        elif any(e in x["rel"] for e in exclude): continue
        out.append(x)
    out.sort(key=lambda x: (int(x["lvl"] or 0), x["name"]))
    return out

def level_groups(items):
    groups = {}
    for x in items: groups.setdefault(int(x["lvl"] or 0), []).append(x["name"])
    return groups

# ── Building blocks ──────────────────────────────────────────────────────
E = html.escape
RES = dict(s=("#8fc46a", "Supplies"), i=("#b4bcc8", "Iron"), v=("#55d5e0", "Veilstone"), vs=("#e8edf5", "Veilsteel"))

def points(items, cols=1):
    rows = "".join('<div class="pt"><b>%s</b><span>%s</span></div>' % (tr(h), tr(b)) for h, b in items)
    return '<div class="pts c%d">%s</div>' % (cols, rows)

def flow(steps, color="var(--gold)"):
    out = []
    for i, (h, b) in enumerate(steps):
        if i: out.append('<div class="arrow" style="color:%s">&#10148;</div>' % color)
        out.append('<div class="step" style="border-top-color:%s"><b>%s</b><span>%s</span></div>' % (color, tr(h), tr(b)))
    return '<div class="flow">%s</div>' % "".join(out)

def tiles(items, cols=3):
    cells = "".join('<div class="tile" style="border-top-color:%s"><b style="color:%s">%s</b><span>%s</span></div>' % (c, c, tr(h), tr(b)) for h, b, c in items)
    return '<div class="tiles" style="grid-template-columns:repeat(%d,1fr)">%s</div>' % (cols, cells)

def keys(items):
    return '<div class="keys">%s</div>' % "".join('<div class="key"><kbd>%s</kbd><span>%s</span></div>' % (tr(k), tr(v)) for k, v in items)

def note(text):
    return '<div class="note">%s</div>' % tr(text)

def tree(cols):
    """cols: [(title, color, [(sub, [items])])]"""
    out = []
    for title, color, subs in cols:
        body = ""
        for sub, items in subs:
            if sub: body += '<div class="tsub">%s</div>' % E(tr(sub))
            body += "".join('<div class="ti">%s</div>' % E(name(i)) for i in items)
        out.append('<div class="tcol" style="border-top-color:%s"><div class="tt" style="color:%s">%s</div>%s</div>' % (color, color, E(tr(title) if (LANG == "en" or title in PT) else name(title)), body))
    tallest = max(sum(len(items) + (1 if sub else 0) for sub, items in subs) for _, _, subs in cols)
    return '<div class="tree%s" style="grid-template-columns:repeat(%d,1fr)">%s</div>' % (" roomy" if tallest <= 12 else "", len(cols), "".join(out))

def meter_svg():
    s = """<svg viewBox="0 0 720 210" class="svg">
<rect x="20" y="24" width="680" height="38" rx="4" fill="#080c13" stroke="#5c6778"/>
<rect x="22" y="26" width="440" height="34" fill="#4c8fe0"/>
<text x="360" y="50" fill="#efe6d2" text-anchor="middle" font-size="22" font-family="Museum">OWNERSHIP METER</text>
<text x="20" y="90" fill="#a9b1bd" font-size="21">0: changes hands</text><text x="700" y="90" fill="#a9b1bd" font-size="21" text-anchor="end">100: yours</text>
<g font-size="22"><rect x="20" y="112" width="24" height="24" fill="#4c8fe0"/><text x="56" y="132" fill="#efe6d2">Your soldiers alone: rises, by the population standing there</text>
<rect x="20" y="146" width="24" height="24" fill="#e0654f"/><text x="56" y="166" fill="#efe6d2">Two armies: the stronger side pushes, by the difference</text>
<rect x="20" y="180" width="24" height="24" fill="#5c6778"/><text x="56" y="200" fill="#efe6d2">Nobody and no building: it decays</text></g></svg>"""
    for k in ("OWNERSHIP METER", "0: changes hands", "100: yours", "Your soldiers alone: rises, by the population standing there",
              "Two armies: the stronger side pushes, by the difference", "Nobody and no building: it decays"):
        s = s.replace(">" + k + "<", ">" + tr(k) + "<")
    return s

def triangle_svg():
    def node(x, y, t, c):
        return '<g><circle cx="%d" cy="%d" r="66" fill="#0e1521" stroke="%s" stroke-width="4"/><text x="%d" y="%d" text-anchor="middle" fill="#efe6d2" font-size="24" font-family="Ortica">%s</text></g>' % (x, y, c, x, y + 8, t)
    def arrow(x1, y1, x2, y2, t, tx, ty):
        return '<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#e0b65a" stroke-width="4" marker-end="url(#a)"/><text x="%d" y="%d" fill="#a9b1bd" font-size="19" text-anchor="middle">%s</text>' % (x1, y1, x2, y2, tx, ty, t)
    return '<svg viewBox="0 0 620 470" class="svg"><defs><marker id="a" markerUnits="userSpaceOnUse" markerWidth="24" markerHeight="24" refX="20" refY="12" orient="auto"><path d="M0 0L24 12L0 24z" fill="#e0b65a"/></marker></defs>' + \
        arrow(250, 125, 150, 305, tr("beats"), 130, 195) + arrow(205, 375, 412, 375, tr("beats"), 310, 410) + arrow(460, 305, 365, 125, tr("beats"), 490, 195) + \
        node(310, 76, tr("Spearmen"), "#8fc46a") + node(490, 375, tr("Archers"), "#55d5e0") + node(130, 375, tr("Cavalry"), "#e0654f") + "</svg>"

def herobar():
    return '<div class="hb"><div class="dia">7</div><div class="hp"><i></i></div><div class="xp"><i></i></div></div>'

# ── The cards ────────────────────────────────────────────────────────────
CH = {1: "Controls", 2: "Economy", 3: "Army", 4: "Culture", 5: "Territory", 6: "The Curse", 7: "Religion", 8: "Heroes and Victory", "T": "Tech Tree"}
BLUE, GOLD, CURSE, CYAN, RED, GREEN = "#4c8fe0", "#e0b65a", "#a76be0", "#55d5e0", "#e0654f", "#8fc46a"
cards = []
def card(cid, ch, title, lede, body, accent=GOLD): cards.append(dict(id=cid, ch=ch, title=tr(title), lede=tr(lede), body=body, accent=accent))

card("01", 1, "Moving the Camera and Selecting", "Everything you do starts with looking and clicking.",
     '<div class="two">' + keys([("Screen edge", "Pan the view (arrow keys too)"), ("Middle drag", "Drag the view"), ("Scroll", "Zoom in and out"), ("Minimap", "Click to jump there")]) +
     keys([("Left-click", "Select one unit or building"), ("Drag a box", "Select many"), ("Right-click", "Move, attack or use: it depends on the target"), ("Ctrl + number", "Save a control group")]) + "</div>")
card("02", 1, "Giving Orders", "Your units keep working from a list of orders.",
     '<div class="two">' + keys([("Shift + right-click", "Queue orders. The route is drawn on the ground"), ("A, then click", "Attack-move: fight anything on the way"), ("Right-drag", "Face the formation; length sets its width"), ("X", "Cycle Box, Line, Wedge, Staggered")]) +
     keys([("G", "Aggressive: chases what it sees"), ("D", "Defensive: stays close to its post"), ("H", "Hold: does not chase at all"), ("Delete", "Cancel a plan with a full refund, or demolish")]) + "</div>" +
     note("A queued move is obeyed over the stance. If you want a fight on the way, use attack-move."))
card("03", 2, "The Four Resources", "Each is rarer than the last, and each comes from somewhere different.",
     tiles([("Supplies", "From every territory you hold, and Gatherer's Huts on supply nodes. Pays for almost everything.", GREEN),
            ("Iron", "From Mines on iron nodes. Arms and armour.", "#b4bcc8"),
            ("Veilstone", "Crystal of the curse. Alanthor buys it at Trading Outposts instead of mining it after age-up.", CYAN),
            ("Veilsteel", "Never mined: it is made. Alanthor forges it from veilstone at a Trading Outpost.", "#e8edf5")], 4))
card("04", 2, "Nobody Gathers: Ground Pays", "There are no carrying workers and no drop-off points. Your bank fills by itself.",
     flow([("Hold ground", "Every territory you hold pays a trickle"), ("Find nodes", "Each node type is set by the territory"), ("Build an extractor", "Gatherer's Hut or Mine: pays far more, and locks the ground"), ("Node runs dry", "Each has a finite reserve. Most extractors collapse with it")]) +
     note("Each node shows what it has left. Plan your next territory before this one runs out."))
card("05", 2, "Workers and Building Plans", "Workers only build and repair. A build order costs you now and rises later.",
     flow([("Plan", "White preview only you can see. Already paid for"), ("Site", "A worker breaks ground. Now everyone can see it, and attack it"), ("Building", "Finished, and working")]) +
     points([("Shift", "Hold while placing to keep placing"), ("Cancel", "Delete a plan or a site for a full refund"), ("Your ground only", "You can build only in territory you own"), ("Population", "Huts and the Fortress raise your cap, more as they level")], 2))
card("06", 3, "Who Beats Whom", "Every unit has a counter. Select one and read Bonus vs in its stats.",
     '<div class="two aligned"><div>' + triangle_svg() + "</div><div>" +
     points([("Spearmen stop cavalry", "Cheap, and always worth having."), ("Cavalry hunts archers", "It closes the distance before they can do much."), ("Archers shred infantry", "Spearmen included. Crossbowmen also punish heavy cavalry."), ("Siege breaks buildings", "Only siege can damage a wall.")]) + "</div></div>")
card("07", 3, "How a Fight Is Decided", "Damage is simple: damage, minus armour, plus any bonus. Position does the rest.",
     tiles([("Armour", "Taken off every hit, so light attacks barely scratch heavy armour.", "#b4bcc8"),
            ("Bonus vs", "Extra damage against a tag, which ignores armour.", GOLD),
            ("High ground", "Attacking from above hits harder. Ramps are the map's chokepoints.", GREEN),
            ("Flanks", "Hits from behind a unit's front do more.", RED),
            ("On a wall", "Troops on a wall-walk carry more armour and hit harder.", CYAN),
            ("Formation", "Melee in front, ranged behind, at the slowest member's speed.", BLUE)], 3))
card("08", 3, "Levels, Research and Ranks", "Three ways to make your army better. None of them is a new age.",
     tiles([("Building levels", "Up to level 3. Levelling one Barracks levels every Barracks you own, and opens new units and research.", GOLD),
            ("Research", "Faction-wide and permanent. Applies to every unit it names, now and later.", CYAN),
            ("Unit ranks", "Pay to promote a single unit. The rank stays with that soldier until it dies.", GREEN)], 3))
card("09", 3, "Scouting and the Fog", "You only know what you can see.",
     tiles([("Unexplored", "Black. You know nothing about it.", "#5c6778"), ("Explored", "You remember the ground, but not who stands there now.", BLUE), ("In sight", "Live: only here do you see enemies, and only here can you aim powers.", GOLD)], 3) +
     points([("Scouts", "They see far and do not claim ground. An idle Scout with Scouting Celestarii reveals a random patch by itself."), ("Field Intelligence", "A late Fortress technology: marks every enemy in territories your soldiers stand in, on explored ground.")], 2))
card("10", 5, "Walls and Gates", "Alanthor's signature. Draw them, then only siege can break them.",
     flow([("Palisade", "Timber, from Age 0, for every culture"), ("Stone Wall", "Alanthor, after age-up. Levels up with research at the Wall Hub"), ("Gate or tower", "Convert a wall piece: it can't be placed alone")], CYAN) +
     points([("Hubs and segments", "Hubs snap to the grid; drawn walls link them together."), ("Your gates only", "Your units walk through; enemies have to break them."), ("Garrison", "Soldiers on the wall-walk fight from above it."), ("Fall in sections", "A wall breaks piece by piece.")], 2), CYAN)
card("11", 5, "Ground Belongs to Whoever Stands on It", "From age-up, soldiers claim territory just by standing there.",
     '<div class="two aligned"><div>' + meter_svg() + "</div><div>" +
     points([("Who claims", "Soldiers, cavalry, siege and heroes. Workers and scouts do not."), ("Where", "Only ground that borders your Fortress-connected territory."), ("Cut off?", "Ground cut off from your Fortress slowly wears down."), ("No limit", "You hold exactly what you can defend.")]) + "</div></div>", BLUE)
card("12", 5, "Hold It, Lock It, or Lose It", "A territory's buildings decide how hard it is to take.",
     tiles([("Empty", "No soldiers, no buildings: it decays back to nobody.", "#5c6778"),
            ("Held", "Any finished building stops the decay, but an enemy army still drains it.", BLUE),
            ("Locked", "An extractor on a node, or a Fortress: the meter can't be drained at all until every lock is razed.", GOLD)], 3) +
     note("<b>Lose a territory and every building you had in it collapses.</b>"), BLUE)
card("13", 5, "Not All Ground Is Equal", "Territory types decide what nodes it holds, and so what it's worth.",
     tiles([("Start", "Your home: supply, iron and a veilstone outcrop.", GOLD), ("Normal", "Supply, sometimes iron or veilstone.", GREEN), ("Iron rich", "More iron than anywhere else.", "#b4bcc8"),
            ("Veilstone rich", "Starts cursed. The richest ground and the hardest to take.", CURSE), ("Empty", "Nothing but room to pass through.", "#5c6778"), ("Sanctum", "Pays Religion Points and veilsteel, and shortens sect cooldowns.", CYAN)], 3) +
     note("Node purity varies too: pure pays more, poor pays less."), BLUE)
card("14", 4, "Your Age-Up Is a Building", "There are only two ages. Age 0 is short: the same units and buildings for everyone.",
     flow([("Build the landmark", "Vault of Almi&eacute;rra for Alanthor. Workers speed it up"), ("Defend the site", "If it falls before it finishes, everything is lost"), ("Your culture opens", "The Shelter becomes a Fortress, new buildings and units unlock, and claiming opens")]) +
     note("Culture is permanent: it stays even if the landmark and the Fortress fall."))
card("15", 4, "The Three Cultures", "Each has its own relationship to the map. All three are built on the same Age 0.",
     tiles([("Alanthor: Defense", "Walls, towers and long-range archers. Holds ground and sells what it holds.", BLUE),
            ("Feraldis: Military", "Industrial dark-magic raiders. Slaves staff extractors, and raiding pays late supplies.", RED),
            ("Runai: Economy", "Caravans haul stock along roads to markets. Moves armies and lives inside the curse.", GOLD)], 3) +
     note("The demo plays Alanthor. Feraldis and Runai come with later editions."))
card("16", 6, "The Curse", "A force on the map, not a player. It claims ground and it wants the Shardroot back.",
     tiles([("Curse nodes", "Rise on resource nodes. Each locks its territory and fields a garrison that grows.", CURSE),
            ("Waves", "It sends raiding waves at players, and hunts whoever holds the Shardroot.", RED),
            ("Your reward", "Curse kills earn Religion Points. Razing a node unlocks its territory.", CYAN)], 3) +
     note("It's never gone for good: with no node left, it raises a new one after a few minutes."), CURSE)
card("17", 6, "Cursed Ground", "The purple is dangerous for units and for buildings.",
     '<div class="two">' + points([("Units", "Slowed and burned while they stand in it."), ("Buildings", "Finished buildings decay: they lose HP, take more damage and work slower.")]) +
     points([("Litharch: Cleansing Ward", "Plants a building that pulses and clears a growing circle. The curse goes for wards first."), ("Litharch: Cleanse Pulse", "One strong burst that clears ground and leaves nothing behind.")]) + "</div>" +
     note("Hold the territory and cursed ground slowly fades from its edge, but never inside a living node's circle."), CURSE)
card("18", 7, "Where Religion Points Come From", "Religion Points (RP) buy everything in the religion layer.",
     tiles([("Curse kills", "The last hit is paid. The first points come cheap.", CURSE), ("Monuments", "A held monument trickles points.", GOLD), ("Sanctums", "A held Sanctum pays RP every minute.", CYAN),
            ("The Temple", "Trickles points, adds to curse kills, and its Tithe buys RP for resources.", GREEN), ("Sect units", "They earn RP from the weapon damage they deal.", BLUE), ("Razed nodes", "Destroying a curse node pays a full RP.", RED)], 3), CYAN)
card("19", 7, "Temple, Chapels and Sects", "Six chapel slots. A real match develops two or three sects deeply.",
     flow([("Temple of Ridan", "Costs only resources. Trains the Litharch"), ("Chapel", "Spend RP to adopt a sect. Cheaper from your own culture"), ("Powers", "A, B and a wildcard, unlocked and levelled with RP"), ("Set passive", "Complete sets of sects earn extra passives")], CYAN) +
     keys([("R", "Open the quick religion menu"), ("T", "Open the full religion tree"), ("F1-F6", "Cast powers (Shift or Ctrl for B and wildcard)")]), CYAN)
card("20", 7, "What a Sect Gives You", "Each sect is a small army and a playstyle.",
     tiles([("Sect building", "Built by workers. It has a standing effect and sells the sect's research.", CYAN),
            ("Sect unit", fmt("Far stronger than a normal unit. Costs %d RP each on top of resources. Up to 20.", SECT_RP), GOLD),
            ("Sect hero", "One per sect, recruited at the sect building.", RED)], 3) +
     points([("The Temple's other offers", "Trade one RP for resources, or buy the Petriarchy's Army: a large force of sect units."), ("Spend carefully", "RP is the scarcest thing you own. A cancelled sect unit refunds its RP.")], 2), CYAN)
ALS = [("Antiquity", "Registry", "Reliquary", "Artificer", "Archivist of Hours", "Damage on your ground, shut buildings down, turn armies on themselves"),
       ("Renewal", "Hospices", "Mending Hall", "Scar Guard", "Surgeon", "Heal, protect from death, bring back the fallen"),
       ("Fortitude", "Wardens", "Stonehold", "Sallyguard", "Warden", "Armed structures, tougher buildings, raise a fort"),
       ("Prosperity", "Guild", "Treasury", "Collector", "Guildmaster", "Bounties, faster production, instant income")]
card("21", 7, "The House of Vaelith", "Alanthor's four sects. Their magic is time: the record, rewind, stored charge, accelerated work.",
     tiles([(fmt("%s: the %s", tr(a), tr(b)), fmt("<i>%s</i><br>Building: %s<br>Unit: %s<br>Hero: %s", tr(p), name(c), name(d), name(e)), col) for (a, b, c, d, e, p), col in zip(ALS, [CYAN, GREEN, BLUE, GOLD])], 4), CYAN)
card("22", 8, "Monuments and Ruins", "Contested objectives that pay from the very first minute.",
     flow([("Capture", "Any unit, even a worker, standing in the circle takes it"), ("Hold", "Trickles Religion Points. An enemy inside freezes the timer"), ("Explore", "Pay once to gain a hidden buff, yours for good")], GOLD) +
     note("<b>One monument knows where the Shardroot lies hidden.</b> Whoever explores it learns the spot."))
card("23", 8, "Heroes", "The strongest units in the game. They level from kills, and they never lose a level.",
     tiles([("Stronger", "Any hero beats any other unit one on one, and is about even with other heroes.", GOLD),
            ("Levels 1-10", "Earned by killing, or by being nearby when a kill lands.", GREEN),
            ("Never reset", "A fallen hero trains again at the same level. It costs more the higher it stood.", BLUE)], 3) +
     '<div class="two aligned"><div>' + herobar() + '</div><div>' + points([("Everyone sees the bar", "Diamond is level, the long bar is health in owner colour, gold is XP.")]) + "</div></div>" +
     note("Abilities below level 10 cast themselves within the hero's sight. Just put your hero in the right place."), RED)
card("24", 8, "Level 10: The Game Enders", "The one hero ability you cast yourself. Long cooldown, no sight limit.",
     '<table class="ult">' + "".join("<tr><td>%s</td><td>%s</td><td>%s</td></tr>" % (tr(r[0]), tr(r[1]), tr(r[2])) for r in [
        ("King Lexor", "Call to the Banners", "A great army of veterans answers around the king for a time."),
        ("Ledger", "Foreclosure", "Takes half of every enemy's stockpile."),
        ("Archivist", "Stop the Hours", "Freezes every enemy unit on the map. Enemy heroes lose their abilities for a time."),
        ("Surgeon", "The Great Rewind", "Heals your whole army, and the units you lost in the last minute come back."),
        ("Warden", "The Realm Holds", "Enemies on your ground lose most of their health. Your buildings can't be hurt for a time."),
        ("Guildmaster", "Buy Out", "Enemy buildings in a wide circle fall and pay you. Capitals lose half their health.")]) + "</table>", RED)
card("25", 8, "The Shardroot", "Hidden on the map from the start. Only a hero can take it, and then the whole map comes for it.",
     tiles([("Keep it", "The hero is Ascended: very hard to kill, cleaving, hurling crowds into the air. If it dies anyway, it explodes and kills everything nearby.", RED),
            ("Drop it", "Set it down safely. The hero loses its power and any hero can take it.", "#efe6d2"),
            ("Stow it", fmt("Pay to enshrine it in your Temple. Your army is empowered and a %d-minute countdown to victory begins.", ASCENSION_MIN), CYAN)], 3), CURSE)
card("26", 8, "Three Ways to Win", "A match ends one of three ways.",
     tiles([("Last one standing", "A faction is out with no Fortress, military building or Worker left, or after too long with no territory.", RED),
            ("Temple victory", fmt("Keep a Temple holding the Shardroot standing for %d minutes. Everyone can see the timer.", ASCENSION_MIN), CYAN),
            ("Sanctum dominion", "Hold every Sanctum on the map through a countdown. Lose one and it's cancelled.", GOLD)], 3) +
     note("After the match, the Game Review shows standings, charts and an animated map of how it happened."))

# ── Tech tree, read from the SOs ─────────────────────────────────────────
def roster(bid, keep): return unit_names(BUILDINGS.get(bid, {}).get("trains", []), keep)
is_age0 = lambda i: not i.startswith(("Alanthor_", "Feraldis_", "Runai_", "Sect_")) and i not in ("Ledger", "King Lexor")
is_al = lambda i: i.startswith("Alanthor_") or i in ("Ledger", "King Lexor")

card("T1", "T", "Tech Tree: Age 0", "Everyone starts here, with the same options. Build the landmark to leave it.",
     tree([("Shelter", GOLD, [("Trains", roster("Fortress", is_age0)), ("Research", [x["name"] for x in techs_at("Fortress") if "Age0" in x["rel"]])]),
           ("Barracks", RED, [("Trains", roster("Barracks", is_age0)), ("Research", [x["name"] for x in techs_at("Barracks") if "Age0" in x["rel"]])]),
           ("Economy", GREEN, [("Build", ["Hut", "Gatherer's Hut", "Mine", "Veilstone Mine"]), ("Research at the Mine", [x["name"] for x in techs_at("Mine") if "Age0" in x["rel"]])]),
           ("Defence and faith", CYAN, [("Build", ["Palisade", "Temple of Ridan"]), ("Temple trains", roster("TempleOfRidan", lambda i: True)), ("Temple research", [x["name"] for x in techs_at("TempleOfRidan")][:6])]),
           ("Leave Age 0", BLUE, [("Alanthor landmark", ["Vault of Almi\u00e9rra"])])]))
card("T2", "T", "Tech Tree: Alanthor Armies", "Age-up renames what you already have and adds three new military buildings.",
     tree([("Fortress", GOLD, [("Trains", roster("Fortress", is_al))]),
           ("Garrison (Barracks)", RED, [("Trains", roster("Barracks", is_al))]),
           ("Archery Range", CYAN, [("Trains", roster("ArcheryRange", lambda i: True))]),
           ("Royal Stable", GREEN, [("Trains", roster("Alanthor_RoyalStable", lambda i: True))]),
           ("Siege Yard", BLUE, [("Trains", roster("Alanthor_SiegeYard", lambda i: True))])]))
def research_col(title, color, at, limit=None, culture="Alanthor"):
    pool = [x for x in TECHS if x["at"] == at and "Civs/Feraldis" not in x["rel"] and "Civs/Runai" not in x["rel"]]
    pool.sort(key=lambda x: (int(x["lvl"] or 0), x["name"]))
    subs = []
    for lv, names in sorted(level_groups(pool).items()):
        subs.append((fmt("Level %d", lv) if lv else "Any level", sorted(names)))
    return (title, color, subs)
card("T3", "T", "Tech Tree: Alanthor Military Research", "Level the building, then buy what it offers. Weapons and armour climb stone, iron, veilstone, shard.",
     tree([research_col("Garrison", RED, "Barracks"), research_col("Archery Range", CYAN, "ArcheryRange"), research_col("Royal Stable", GREEN, "Alanthor_RoyalStable"), research_col("Siege Yard", BLUE, "Alanthor_SiegeYard")]))
card("T4", "T", "Tech Tree: Alanthor Economy and Defence", "Territories pay. These decide how much, and how long you can keep them.",
     tree([research_col("Fortress", GOLD, "Fortress"), research_col("Guild (Gatherer's Hut)", GREEN, "GatherersHut"), research_col("Mine", "#b4bcc8", "Mine"),
           research_col("Trading Outpost", CYAN, "Alanthor_TradingOutpost"), research_col("Vault", GOLD, "VaultOfAlmierra"), research_col("Wall Hub", BLUE, "Alanthor_Wall")]))
card("T5", "T", "Tech Tree: The Sect Branch", "Temple, then chapel, then the sect's own building. Each building trains a unit and a hero and sells one research.",
     tree([("Temple of Ridan", CYAN, [("Trains", roster("TempleOfRidan", lambda i: True)), ("Research", [x["name"] for x in techs_at("TempleOfRidan")])])] +
          [(b["name"], c, [("Trains", unit_names(b["trains"])), ("Research", [x["name"] for x in TECHS if x["at"] == i])])
           for i, c in [("Sect_Reliquary", CYAN), ("Sect_MendingHall", GREEN), ("Sect_Stonehold", BLUE), ("Sect_Treasury", GOLD)] for b in [BUILDINGS[i]]]))
def culture_tree(culture, color):
    cols = []
    for bid, b in sorted(BUILDINGS.items(), key=lambda kv: kv[1]["name"]):
        own = [i for i in b["trains"] if i.startswith(culture + "_")]
        if ("Civs/" + culture) in b["rel"] or own:
            if own: cols.append(("Fortress" if bid == "Fortress" else b["name"], color, [("Trains", unit_names(own))]))
    return cols
card("T6", "T", "Tech Tree: Feraldis", "Edition B and up. Armies by building, read from the game's data.", tree(culture_tree("Feraldis", RED)), RED)
card("T7", "T", "Tech Tree: Runai", "Edition C. Armies by building, read from the game's data.", tree(culture_tree("Runai", GOLD)), GOLD)

# ── Rendering ────────────────────────────────────────────────────────────
CSS = """
@font-face{font-family:Ortica;src:url("%(f)s/Ortica/Ortica-Bold.otf")}
@font-face{font-family:Museum;src:url("%(f)s/LT Museum/LTMuseum-Bold.ttf")}
@font-face{font-family:Alegreya;src:url("%(f)s/Alegreya Sans/AlegreyaSans-Medium.ttf")}
:root{--gold:#e0b65a;--cream:#efe6d2;--dim:#a9b1bd;--line:#3a4658}
*{box-sizing:border-box;margin:0;padding:0}
html,body{width:1600px;height:900px;overflow:hidden}
body{background:radial-gradient(1200px 520px at 50%% -8%%,#2c2740 0%%,#141a2b 55%%,#0b111c 100%%);color:var(--cream);font:27px/1.3 Alegreya,sans-serif;padding:40px 56px 30px;display:flex;flex-direction:column}
.tag{font:400 20px Museum,serif;letter-spacing:4px;color:var(--dim);text-transform:uppercase}
h1{font:400 62px/1.08 Ortica,serif;color:var(--accent);margin:6px 0 8px}
.lede{font-size:31px;margin-bottom:24px;color:var(--cream)}
.body{flex:1;display:flex;flex-direction:column;gap:20px;justify-content:center;min-height:0}
.foot{display:flex;justify-content:space-between;font:400 18px Museum,serif;color:#6e7888;letter-spacing:3px;margin-top:14px}
.tiles{display:grid;gap:18px}
.tile{background:linear-gradient(180deg,#1b2638,#141c2a);border:1px solid var(--line);border-top:4px solid;border-radius:6px;padding:22px 24px}
.tile b{display:block;font:400 32px/1.12 Ortica,serif;margin-bottom:8px}
.tile span{font-size:26px}
.pts{display:grid;gap:14px 22px}.pts.c2{grid-template-columns:1fr 1fr}
.pt{background:#101825;border-left:4px solid var(--accent);padding:12px 18px;border-radius:0 5px 5px 0}
.pt b{display:block;font:400 27px Ortica,serif;color:var(--cream);margin-bottom:2px}.pt span{font-size:25px;color:#d5d2c9}
.flow{display:flex;align-items:stretch;gap:10px}
.step{flex:1;background:linear-gradient(180deg,#1b2638,#141c2a);border:1px solid var(--line);border-top:4px solid;border-radius:6px;padding:20px}
.step b{display:block;font:400 31px/1.1 Ortica,serif;margin-bottom:8px}.step span{font-size:25px;color:#d5d2c9}
.arrow{align-self:center;font-size:34px}
.two{display:grid;grid-template-columns:1fr 1fr;gap:26px}.aligned{align-items:center}
.keys{display:flex;flex-direction:column;gap:14px}
.key{display:flex;align-items:center;gap:18px}
kbd{flex:none;min-width:150px;text-align:center;font:400 23px Museum,serif;background:#0e1521;border:1px solid #6b5f42;border-bottom-width:3px;border-radius:5px;padding:9px 14px;color:var(--gold)}
.key span{font-size:27px}
.note{background:#0e1521;border:1px solid var(--line);border-radius:5px;padding:14px 22px;font-size:27px;text-align:center}
.svg{width:100%%;display:block;font-family:Alegreya}
.tree{display:grid;gap:12px;align-items:start}
.tcol{background:linear-gradient(180deg,#1b2638,#141c2a);border:1px solid var(--line);border-top:4px solid;border-radius:6px;padding:14px 16px;overflow:hidden}
.tt{font:400 25px/1.1 Ortica,serif;margin-bottom:6px}
.tsub{font:400 15px Museum,serif;letter-spacing:2px;text-transform:uppercase;color:var(--dim);margin:9px 0 2px}
.ti{font-size:19.5px;line-height:1.2;color:#e3dfd3}
.roomy .ti{font-size:26px;line-height:1.3}.roomy .tt{font-size:29px}.roomy .tsub{font-size:17px;margin-top:14px}.roomy .tcol{padding:20px 18px 24px}
.ult{width:100%%;border-collapse:collapse}.ult td{padding:14px 12px;border-top:1px solid #2c3647;font-size:26px}.ult tr:first-child td{border-top:0}
.ult td:first-child{font:400 30px Ortica,serif;white-space:nowrap}.ult td:nth-child(2){font:400 30px Ortica,serif;color:var(--accent);white-space:nowrap}
.hb{position:relative;width:420px;height:105px;margin:0 auto}
.dia{position:absolute;left:0;top:0;width:105px;height:105px;background:url("%(d)s") center/contain no-repeat;display:flex;align-items:center;justify-content:center;font:400 44px Ortica,serif;color:#fff;text-shadow:0 2px 3px #000}
.hp{position:absolute;left:112px;top:30px;width:306px;height:36px;background:#080c13;border:2px solid #8d7a50;border-radius:3px;overflow:hidden}.hp i{display:block;height:100%%;width:72%%;background:#4c8fe0}
.xp{position:absolute;left:112px;top:72px;width:306px;height:12px;background:#080c13;border:1px solid #5b5240;overflow:hidden}.xp i{display:block;height:100%%;width:45%%;background:#e0b65a}
""" % dict(f=FONTS, d=DIAMOND)

def page(c, index, total):
    label = tr("Tech Tree") if c["ch"] == "T" else fmt("Chapter %s: %s", c["ch"], tr(CH[c["ch"]]))
    return '<!doctype html><html><head><meta charset="utf-8"><style>%s</style></head><body style="--accent:%s"><div class="tag">%s</div><h1>%s</h1><div class="lede">%s</div><div class="body">%s</div><div class="foot"><span>THE WANING BORDER</span><span>%s</span></div></body></html>' % (CSS, c["accent"], E(label), c["title"], c["lede"], c["body"], c["id"])

def render(c, i, n, tmp):
    h = os.path.join(tmp, c["id"] + ".html")
    open(h, "w", encoding="utf8").write(page(c, i, n))
    png = os.path.join(OUT, "card_%s.png" % c["id"])
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files",
                    "--window-size=1600,900", "--virtual-time-budget=2500", "--user-data-dir=" + os.path.join(tmp, "chrome"),
                    "--screenshot=" + png, "file:///" + h.replace(os.sep, "/")], capture_output=True, timeout=120)
    return png

META = """fileFormatVersion: 2
guid: %s
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  spriteMode: 1
  spriteMeshType: 0
  spritePixelsToUnits: 100
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 0
  alphaIsTransparency: 0
  textureType: 8
  textureShape: 1
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    overridden: 0
  - serializedVersion: 3
    buildTarget: Standalone
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: 25
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    overridden: 1
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
  userData:
  assetBundleName:
  assetBundleVariant:
"""

def install(card_id):
    """Copy a card into the game, with a meta whose guid follows from its path."""
    if card_id in NOT_IN_GAME: return
    os.makedirs(GAME, exist_ok=True)
    fn = "card_%s.png" % card_id
    dst = os.path.join(GAME, fn)
    shutil.copyfile(os.path.join(OUT, fn), dst)
    guid = hashlib.md5(("TutorialCards/%s/%s" % (LANG, fn)).encode()).hexdigest()
    open(dst + ".meta", "w", encoding="utf8", newline="\n").write(META % guid)

def folder_meta(path, key):
    if not os.path.exists(path + ".meta"):
        guid = hashlib.md5(key.encode()).hexdigest()
        open(path + ".meta", "w", encoding="utf8", newline="\n").write(
            "fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid)

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--only", default=""); ap.add_argument("--lang", default=""); a = ap.parse_args()
    if not a.lang:   # both languages, each in its own pass: card text is built at import
        for lang in ("en", "pt"):
            extra = ["--only", a.only] if a.only else []
            subprocess.run([sys.executable, os.path.abspath(__file__), "--lang", lang] + extra, check=True)
        return
    if MISSING:
        sys.exit("Missing %s translations:\n  " % LANG + "\n  ".join(sorted(MISSING)))
    os.makedirs(OUT, exist_ok=True)
    only = set(a.only.split(",")) if a.only else None
    tmp = tempfile.mkdtemp(prefix="twbcards")
    for i, c in enumerate(cards):
        if only and c["id"] not in only: continue
        render(c, i, len(cards), tmp)
        install(c["id"])
    root = os.path.dirname(GAME)
    folder_meta(root, "TutorialCards"); folder_meta(GAME, "TutorialCards/" + LANG)
    if MISSING:
        sys.exit("Missing %s translations:\n  " % LANG + "\n  ".join(sorted(MISSING)))
    from PIL import Image
    paths = [os.path.join(OUT, "card_%s.png" % c["id"]) for c in cards]
    paths = [p for p in paths if os.path.exists(p)]
    cols = 6; tw, th = 400, 225
    rows = (len(paths) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * (tw + 8) + 8, rows * (th + 8) + 8), (8, 11, 18))
    for k, p in enumerate(paths):
        sheet.paste(Image.open(p).convert("RGB").resize((tw, th), Image.LANCZOS), (8 + (k % cols) * (tw + 8), 8 + (k // cols) * (th + 8)))
    sheet.save(os.path.join(BASE, "_contact_sheet_%s.png" % LANG))
    print(LANG, len(paths), "cards")

if __name__ == "__main__":
    main()
