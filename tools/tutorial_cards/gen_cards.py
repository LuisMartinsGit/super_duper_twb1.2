#!/usr/bin/env python3
"""Build the tutorial's infographic cards.

    python tools/tutorial_cards/gen_cards.py [--only 12,T2]

Writes Assets/Resources/TutorialCards/<lang>/card_<id>.png (1600 x 900, en and pt), which git
tracks and the Field Guide loads on demand, and a contact sheet in docs/Tutorial/Cards/. T6 and
T7 name other editions' content, so they stay out of the game, in docs/Tutorial/Cards/<lang>/.
The tech tree cards and every stated value are read from the SOs at build time, so re-running
after a balance change is all a card ever needs. Rules text lives here; numbers do not.
Every panel, key, icon, bar and ornament is a sprite from the Synty kits, and the fonts are the
kits' too, so regenerating needs Assets/Synty on disk (it is not in git). Needs Chrome for
rendering and Pillow for the contact sheet. A card whose content overflows fails the build.
"""
import argparse, glob, hashlib, html, math, os, re, shutil, subprocess, sys, tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pt import PT

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "Assets")
T = os.path.join(A, "GameData", "TechTree")
LANG = sys.argv[sys.argv.index("--lang") + 1] if "--lang" in sys.argv else "en"
BASE = os.path.join(ROOT, "docs", "Tutorial", "Cards")
OUT = os.path.join(BASE, LANG)
GAME = os.path.join(A, "Resources", "TutorialCards", LANG)
NOT_IN_GAME = {"T6", "T7"}
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
FONTS = "file:///" + os.path.join(A, "Synty", "InterfaceFantasyMenus", "Fonts").replace(os.sep, "/")
OVERFLOW = []

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
    return [name(re.sub(r"^(Feraldis|Runai|Alanthor|Sect)_", "", UNITS.get(i, i))) for i in ids if keep(i)]

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

BLUE, GOLD, CURSE, CYAN, RED, GREEN = "#4c8fe0", "#e0b65a", "#a76be0", "#55d5e0", "#e0654f", "#8fc46a"

# ── Synty sprites ────────────────────────────────────────────────────────
# Every panel, key, icon, bar and ornament on a card is a sprite from the Synty kits the game
# itself uses. A missing sprite stops the build rather than leaving a plain box.
SYNTY = os.path.join(A, "Synty")
FW = "InterfaceFantasyWarriorHUD/Sprites/"
FM = "InterfaceFantasyMenus/Sprites/"
MK = "InterfaceCore/Sprites/Icons_Input/MouseKeyboard/ICON_Input_PC_"

def sprite(rel):
    p = os.path.join(SYNTY, rel.replace("/", os.sep) + ".png")
    if not os.path.exists(p): sys.exit("missing Synty sprite: " + rel)
    return "file:///" + p.replace(os.sep, "/")

def _icon_index():
    out = {}
    for folder, kit in [("Icons_Stats", FW), ("Icons_Map", FW), ("Icons_Status", FW), ("Icons_Elements", FW), ("Icons_Weapons", FW), ("Icons_Resources", FW), ("Icons_Menu", FM), ("Icons_FantasyMenus", FM)]:
        for f in glob.glob(os.path.join(SYNTY, kit.replace("/", os.sep), folder, "*.png")):
            b = os.path.basename(f)[:-4]
            if re.search(r"_(Stroke|Underlay|Side|Border|Mask)$", b): continue
            white = b.endswith("_Clean")
            k = re.sub(r"^(ICON|SPR)_(FantasyWarrior_|FantasyMenus_)?(SM_)?|_Clean$", "", b)
            if k not in out or white:
                out[k] = ("file:///" + f.replace(os.sep, "/"), white)
    return out
ICONS = _icon_index()

def icon(key, color=None, cls="ic"):
    if key not in ICONS: sys.exit("missing Synty icon: " + key)
    url, white = ICONS[key]
    if white:
        return '<i class="%s" style="background:%s;-webkit-mask-image:url(\'%s\')"></i>' % (cls, color or "var(--gold)", url)
    return '<i class="%s" style="background-image:url(\'%s\')"></i>' % (cls, url)

def ring(key, color=None):
    return '<span class="ring">%s</span>' % icon(key, color)

# The icon of every heading on every card, keyed by the English heading. No icon stops the build.
HEAD = {
    "Supplies": "Item_Bread_01", "Iron": "Item_Ingot_Iron_01", "Veilstone": "Item_Crystal_01", "Veilsteel": "Item_Gem_04",
    "Hold ground": "Map_Flag_01", "Find nodes": "Item_Magnifying_Glass_01", "Build an extractor": "Item_Pickaxe_01", "Node runs dry": "Status_Time_01",
    "Plan": "Item_Parchment_01", "Site": "Item_Hammer_01", "Building": "Map_Home_01",
    "Shift": "Status_Up_01", "Cancel": "Menu_Delete_01", "Your ground only": "Map_Flag_01", "Population": "Chr_Attach_Soldier_01",
    "Spearmen stop cavalry": "Status_Defense_01", "Cavalry hunts archers": "Map_Horse_01", "Archers shred infantry": "Stat_Accuracy_01", "Siege breaks buildings": "Status_DefenseBroken_01",
    "Armour": "Status_Armour_01", "Bonus vs": "Status_AttackUp_01", "High ground": "Status_Up_01", "Flanks": "Status_Critical_01", "On a wall": "Status_FortifiedDefense_01", "Formation": "Icon_Flags_01",
    "Building levels": "Map_Home_01", "Research": "Item_Book_02", "Unit ranks": "Icon_Medal_01",
    "Unexplored": "Map_Unknown_01", "Explored": "Item_Parchment_02", "In sight": "Status_Cursed_02", "Scouts": "Item_Magnifying_Glass_01", "Field Intelligence": "Status_Targeted_01",
    "Palisade": "Item_Wood_01", "Stone Wall": "Item_Rock_01", "Gate or tower": "Map_Home_01", "Hubs and segments": "Menu_Link_01", "Your gates only": "Map_Key_01", "Garrison": "Status_FortifiedDefense_01", "Fall in sections": "Status_DefenseBroken_01",
    "Who claims": "Chr_Attach_Soldier_01", "Where": "Map_Target_01", "Cut off?": "Status_Down_01", "No limit": "Map_Star_01",
    "Empty": "Map_Unknown_01", "Held": "Status_Defense_01", "Locked": "Map_Lock_01",
    "Start": "Map_Home_01", "Normal": "Map_Forage_01", "Iron rich": "Item_Ingot_Iron_01", "Veilstone rich": "Item_Crystal_03", "Sanctum": "Stat_Magic_01",
    "Build the landmark": "Item_Hammer_02", "Defend the site": "Status_Armour_01", "Your culture opens": "Chr_Attach_King_Crown_01",
    "Alanthor: Defense": "Status_FortifiedDefense_01", "Feraldis: Military": "Element_Fire_01", "Runai: Economy": "Item_Pouch_01",
    "Curse nodes": "Status_Cursed_01", "Waves": "Map_Monster_01", "Your reward": "Map_Treasure_01",
    "Units": "Status_Burninating_01", "Buildings": "Status_DefenseBroken_01", "Litharch: Cleansing Ward": "Chr_Attach_Priest_Hat_01", "Litharch: Cleanse Pulse": "Map_Lightning_01",
    "Curse kills": "Map_Skull_01", "Monuments": "Map_Flag_01", "Sanctums": "Stat_Magic_01", "The Temple": "Chr_Attach_Priest_Hat_01", "Sect units": "Status_Attack_01", "Razed nodes": "Status_Burninating_01",
    "Temple of Ridan": "Chr_Attach_Priest_Hat_01", "Chapel": "Item_Book_03", "Powers": "Map_Lightning_01", "Set passive": "Status_Fortified_01",
    "Sect building": "Map_Home_01", "Sect unit": "Status_Attack_01", "Sect hero": "Chr_Attach_King_Crown_01", "The Temple's other offers": "Item_Pouch_01", "Spend carefully": "Status_Quest_01",
    "Capture": "Map_Flag_01", "Hold": "Status_Time_01", "Explore": "Item_Key_01",
    "Stronger": "Stat_Strength_01", "Levels 1-10": "Status_XP_01", "Never reset": "Status_Up_01", "Everyone sees the bar": "Status_Cursed_02",
    "Keep it": "Status_Critical_01", "Drop it": "Status_Down_01", "Stow it": "Stat_Magic_01",
    "Last one standing": "Menu_Trophy_01", "Temple victory": "Chr_Attach_Priest_Hat_01", "Sanctum dominion": "Stat_Magic_01",
}
HEAD["Spend carefully"] = "Map_Quest_01"
def head_icon(h, color=None):
    if h not in HEAD: sys.exit("no icon for heading: " + h)
    return ring(HEAD[h], color)

# ── Building blocks ──────────────────────────────────────────────────────
E = html.escape
RES = dict(s=("#8fc46a", "Supplies"), i=("#b4bcc8", "Iron"), v=("#55d5e0", "Veilstone"), vs=("#e8edf5", "Veilsteel"))

def points(items, cols=1):
    rows = "".join('<div class="pt pn">%s<div><b>%s</b><span>%s</span></div></div>' % (head_icon(h, "var(--accent)"), tr(h), tr(b)) for h, b in items)
    return '<div class="pts c%d">%s</div>' % (cols, rows)

def flow(steps, color="var(--gold)"):
    out = []
    for i, (h, b) in enumerate(steps):
        if i: out.append('<i class="arrow" style="background:%s;-webkit-mask-image:url(\'%s\')"></i>' % (color, sprite(MK + "Arrow_Right_Clean")))
        out.append('<div class="step pn"><div class="th">%s<b style="color:%s">%s</b></div><span>%s</span></div>' % (head_icon(h, color), color, tr(h), tr(b)))
    return '<div class="flow">%s</div>' % "".join(out)

def tiles(items, cols=3):
    cells = []
    for it in items:
        h, b, c = it[:3]
        ic = ring(it[3], c) if len(it) > 3 else head_icon(h, c)
        cells.append('<div class="tile pn"><div class="th">%s<b style="color:%s">%s</b></div><span>%s</span></div>' % (ic, c, tr(h), tr(b)))
    return '<div class="tiles" style="grid-template-columns:repeat(%d,minmax(0,1fr))">%s</div>' % (cols, "".join(cells))

def _mouse(which):
    return '<img class="mouse" src="%s">' % sprite(MK + "MouseColor_%s_Clean" % which)
def _cap(text):
    return '<span class="cap">%s</span>' % text
def _arrow_cap(which):
    return '<span class="cap"><i class="capi" style="-webkit-mask-image:url(\'%s\')"></i></span>' % sprite(MK + "Arrow_%s_Clean" % which)
KEY_ART = {
    "Screen edge": [_arrow_cap("Left"), _arrow_cap("Right")], "Middle drag": [_mouse("Middle")], "Scroll": [_mouse("Middle")], "Minimap": [_mouse("Left")],
    "Left-click": [_mouse("Left")], "Drag a box": [_mouse("Left")], "Right-click": [_mouse("Right")], "Ctrl + number": [_cap("Ctrl"), _cap("1")],
    "Shift + right-click": [_cap("Shift"), _mouse("Right")], "A, then click": [_cap("A"), _mouse("Left")], "Right-drag": [_mouse("Right")],
    "X": [_cap("X")], "G": [_cap("G")], "D": [_cap("D")], "H": [_cap("H")], "R": [_cap("R")], "T": [_cap("T")], "Delete": [_cap("Del")], "F1-F6": [_cap("F1-F6")],
}
def keys(items):
    rows = []
    for k, v in items:
        if k not in KEY_ART: sys.exit("no key art for: " + k)
        label = "" if len(k) == 1 or k == "F1-F6" else "<b>%s</b>" % tr(k)
        rows.append('<div class="key"><div class="kart">%s</div><div class="ktxt">%s<span>%s</span></div></div>' % ("".join(KEY_ART[k]), label, tr(v)))
    return '<div class="keys pn">%s</div>' % "".join(rows)

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
        out.append('<div class="tcol pn"><div class="tt" style="color:%s">%s</div>%s</div>' % (color, E(tr(title) if (LANG == "en" or title in PT) else name(title)), body))
    tallest = max(sum(len(items) + (1 if sub else 0) for sub, items in subs) for _, _, subs in cols)
    return '<div class="tree%s" style="grid-template-columns:repeat(%d,minmax(0,1fr))">%s</div>' % (" roomy" if tallest <= 12 else "", len(cols), "".join(out))

def bar(width, height, percent, color, cls=""):
    """The kit's own bar: Bar_Horizontal_03 over its fill channel, as the hero bar uses it."""
    s = height / 256.0
    return ('<div class="bar %s" style="width:%s;height:%dpx"><div class="chan" style="left:%dpx;right:%dpx;top:%dpx;height:%dpx"><i style="width:%d%%;background:%s"></i></div>'
            '<div class="barf" style="border-width:0 %dpx;border-image-width:0 %dpx"></div></div>') % (
        cls, width if isinstance(width, str) else "%dpx" % width, height, 120 * s, 120 * s, 96 * s, 64 * s, percent, color, 200 * s, 200 * s)

def meter_svg():
    rows = [("Status_Up_01", BLUE, "Your soldiers alone: rises, by the population standing there"),
            ("Icon_Swords_01", RED, "Two armies: the stronger side pushes, by the difference"),
            ("Status_Time_01", "#a9b1bd", "Nobody and no building: it decays")]
    return ('<div class="meter"><div class="mtitle">%s</div>%s<div class="mends"><span>%s</span><span>%s</span></div>%s</div>') % (
        tr("OWNERSHIP METER"), bar("100%", 110, 65, BLUE), tr("0: changes hands"), tr("100: yours"),
        "".join('<div class="mrow">%s<span>%s</span></div>' % (icon(k, c), tr(t)) for k, c, t in rows))

def triangle_svg():
    W_, H_ = 620, 470
    def node(x, y, t, k, c):
        return '<div class="tn" style="left:%dpx;top:%dpx">%s<b>%s</b></div>' % (x - 90, y - 62, '<span class="ring big">%s</span>' % icon(k, c), t)
    def arrow(x1, y1, x2, y2, t, tx, ty):
        a = math.degrees(math.atan2(y2 - y1, x2 - x1))
        head = '<i class="thead" style="left:%dpx;top:%dpx;transform:rotate(%.1fdeg);-webkit-mask-image:url(\'%s\')"></i>' % (x2 - 22, y2 - 22, a, sprite(MK + "Arrow_Right_Clean"))
        return ('<svg class="tline" viewBox="0 0 %d %d"><line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#e0b65a" stroke-width="5"/></svg>' % (W_, H_, x1, y1, x2, y2)) + head + \
            '<span class="tbeat" style="left:%dpx;top:%dpx">%s</span>' % (tx - 60, ty - 18, t)
    return '<div class="tri">' + arrow(222, 145, 142, 290, tr("beats"), 120, 200) + arrow(205, 362, 412, 362, tr("beats"), 310, 395) + arrow(478, 290, 398, 145, tr("beats"), 500, 200) + \
        node(310, 76, tr("Spearmen"), "Status_Defense_01", GREEN) + node(490, 362, tr("Archers"), "Stat_Accuracy_01", CYAN) + node(130, 362, tr("Cavalry"), "Map_Horse_01", RED) + "</div>"

def herobar():
    return '<div class="hb"><div class="dia">7</div>%s%s</div>' % (bar(330, 90, 72, BLUE, "hp"), bar(330, 54, 45, GOLD, "xp"))

# ── The cards ────────────────────────────────────────────────────────────
CH = {1: "Controls", 2: "Economy", 3: "Army", 4: "Culture", 5: "Territory", 6: "The Curse", 7: "Religion", 8: "Heroes and Victory", "T": "Tech Tree"}
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
     tiles([(fmt("%s: the %s", tr(a), tr(b)), fmt("<em>%s</em><br>Building: %s<br>Unit: %s<br>Hero: %s", tr(p), name(c), name(d), name(e)), col, k) for (a, b, c, d, e, p), col, k in zip(ALS, [CYAN, GREEN, BLUE, GOLD], ["Status_Time_01", "Status_Health_02", "Status_Fortified_01", "Item_Ingot_Gold_01"])], 4), CYAN)
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
     "".join('<div class="urow pn">%s<div class="r1">%s</div><div class="r2">%s</div><div class="r3">%s</div></div>' % (ring(r[3], RED), tr(r[0]), tr(r[1]), tr(r[2])) for r in [
        ("King Lexor", "Call to the Banners", "A great army of veterans answers around the king for a time.", "Chr_Attach_King_Crown_01"),
        ("Ledger", "Foreclosure", "Takes half of every enemy's stockpile.", "Item_Book_03"),
        ("Archivist", "Stop the Hours", "Freezes every enemy unit on the map. Enemy heroes lose their abilities for a time.", "Status_Time_01"),
        ("Surgeon", "The Great Rewind", "Heals your whole army, and the units you lost in the last minute come back.", "Status_Health_02"),
        ("Warden", "The Realm Holds", "Enemies on your ground lose most of their health. Your buildings can't be hurt for a time.", "Status_Fortified_01"),
        ("Guildmaster", "Buy Out", "Enemy buildings in a wide circle fall and pay you. Capitals lose half their health.", "Item_Ingot_Gold_01")]), RED)
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
@font-face{font-family:Ortica;src:url("@FONTS/Ortica/Ortica-Bold.otf")}
@font-face{font-family:Museum;src:url("@FONTS/LT Museum/LTMuseum-Bold.ttf")}
@font-face{font-family:Alegreya;src:url("@FONTS/Alegreya Sans/AlegreyaSans-Medium.ttf")}
:root{--gold:#e0b65a;--cream:#efe6d2;--dim:#a9b1bd}
*{box-sizing:border-box;margin:0;padding:0}
i{display:block}
html,body{width:1600px;height:900px;overflow:hidden;background:#090d16}
body{position:relative;color:var(--cream);font:26px/1.26 Alegreya,sans-serif;padding:58px 78px 34px;display:flex;flex-direction:column;z-index:0}
.bgfill{position:absolute;inset:8px;z-index:-3;background:linear-gradient(180deg,#232338,#141a2b 45%,#0d1320);-webkit-mask-box-image:url("@OUTER_MASK") 380 fill / 120px}
.bgpat{position:absolute;left:400px;top:50px;width:800px;height:800px;z-index:-2;opacity:.05;background:var(--accent);-webkit-mask:url("@PATTERN") center/contain no-repeat}
.bgframe{position:absolute;inset:8px;z-index:-1;border:120px solid transparent;border-image:url("@OUTER") 380 / 120px}
.crown{position:absolute;left:50%;top:2px;width:150px;height:72px;margin-left:-75px;background:url("@CROWN") center/contain no-repeat}
.tag{font:400 19px Museum,serif;letter-spacing:4px;color:var(--dim);text-transform:uppercase;text-align:center;margin-top:18px}
h1{font:400 56px/1.06 Ortica,serif;color:var(--accent);margin:4px 0 2px;text-align:center}
.lede{font-size:29px;line-height:1.2;color:var(--cream);text-align:center}
.rule{height:30px;margin:4px auto 8px;width:760px;background:url("@RULE") center/contain no-repeat}
.body{flex:1;display:flex;flex-direction:column;justify-content:center;min-height:0}
.inner{display:flex;flex-direction:column;gap:16px}
.foot{display:flex;justify-content:space-between;font:400 16px Museum,serif;color:#6e7888;letter-spacing:3px;margin-top:8px;padding:0 26px}
.dense .tag{margin-top:12px}.dense h1{font-size:44px}.dense .lede{font-size:24px}.dense .rule{height:20px;margin:2px auto 4px}.dense{padding-top:46px}.dense .crown{top:0;height:54px}

.pn{position:relative;z-index:0}
.pn::before{content:"";position:absolute;inset:0;z-index:-2;background:linear-gradient(180deg,#1f2a3f,#121a28);-webkit-mask-box-image:url("@PANEL_MASK") 120 fill / 30px}
.pn::after{content:"";position:absolute;inset:0;z-index:-1;border:30px solid transparent;border-image:url("@PANEL") 120 / 30px}
.ic{width:100%;height:100%;background-size:contain;background-position:center;background-repeat:no-repeat;-webkit-mask-size:contain;-webkit-mask-position:center;-webkit-mask-repeat:no-repeat}
.ring{flex:none;position:relative;display:block;width:68px;height:68px;padding:15px;background:url("@RING") center/contain no-repeat}
.ring.big{width:124px;height:124px;padding:30px;margin:0 auto}
.th{display:flex;align-items:center;gap:10px;margin-bottom:6px}
.th b{font:400 30px/1.08 Ortica,serif}

.tiles{display:grid;gap:14px}
.tile{padding:18px 24px 20px}
.tile span{font-size:25px;display:block}
.flow{display:flex;align-items:stretch;gap:6px}
.step{flex:1;padding:18px 20px 20px}
.step .th{flex-direction:column;align-items:flex-start;gap:4px}
.step span{font-size:24px;color:#d5d2c9;display:block}
.arrow{flex:none;align-self:center;width:30px;height:30px;-webkit-mask-size:contain;-webkit-mask-position:center;-webkit-mask-repeat:no-repeat}
.pts{display:grid;gap:10px 16px}.pts.c2{grid-template-columns:1fr 1fr}
.pt{display:flex;align-items:center;gap:12px;padding:10px 22px 10px 16px}
.pt b{display:block;font:400 26px/1.1 Ortica,serif;color:var(--cream);margin-bottom:2px}.pt span{font-size:24px;color:#d5d2c9;display:block;line-height:1.2}
.two{display:grid;grid-template-columns:1fr 1fr;gap:20px}.aligned{align-items:center}

.keys{display:flex;flex-direction:column;gap:10px;padding:22px 26px}
.key{display:flex;align-items:center;gap:16px;min-height:62px}
.kart{flex:none;width:150px;display:flex;align-items:center;justify-content:center;gap:6px}
.mouse{height:62px;width:auto;display:block}
.cap{position:relative;z-index:0;display:inline-flex;align-items:center;justify-content:center;min-width:54px;height:54px;padding:0 12px;font:400 21px Museum,serif;color:#1c1a15}
.cap::before{content:"";position:absolute;inset:0;z-index:-1;background:#e6dcc4;-webkit-mask-box-image:url("@KEYCAP") 40 fill / 9px}
.capi{width:24px;height:24px;background:#1c1a15;-webkit-mask-size:contain;-webkit-mask-position:center;-webkit-mask-repeat:no-repeat}
.ktxt b{display:block;font:400 24px/1.1 Ortica,serif;color:var(--gold)}
.ktxt span{font-size:24px;line-height:1.18;display:block}

.note{position:relative;z-index:0;padding:15px 44px;font-size:25px;line-height:1.2;text-align:center;color:#2a2115}
.note::before{content:"";position:absolute;inset:0;z-index:-1;background:#dccba3;-webkit-mask-box-image:url("@PARCHMENT") 60 fill / 22px}
.note b{color:#5c1f10}

.tree{display:grid;gap:8px;align-items:start}
.tcol{padding:13px 15px 16px;overflow:hidden}
.tcol::before,.tcol::after{}
.tt{font:400 23px/1.08 Ortica,serif;margin-bottom:4px}
.tsub{font:400 14px Museum,serif;letter-spacing:2px;text-transform:uppercase;color:var(--dim);margin:7px 0 2px}
.ti{font-size:19px;line-height:1.17;color:#e3dfd3}
.roomy .ti{font-size:25px;line-height:1.26}.roomy .tt{font-size:28px}.roomy .tsub{font-size:16px;margin-top:12px}.roomy .tcol{padding:20px 18px 24px}

.urow{display:flex;align-items:center;gap:14px;padding:6px 26px 6px 16px;margin-bottom:6px}
.urow .r1{flex:none;width:190px;font:400 26px/1.05 Ortica,serif}
.urow .r2{flex:none;width:300px;font:400 26px/1.05 Ortica,serif;color:var(--accent)}
.urow .r3{font-size:23px;line-height:1.16}

.bar{position:relative;margin:0 auto}
.chan{position:absolute;background:#080c13;overflow:hidden;-webkit-mask-box-image:url("@BAR_MASK") 0 50 fill / 0 18px}
.chan i{height:100%}
.barf{position:absolute;inset:0;border-style:solid;border-color:transparent;border-image:url("@BAR") 0 200 fill}
.hb{position:relative;width:440px;height:128px;margin:0 auto}
.dia{position:absolute;left:0;top:6px;width:116px;height:116px;background:url("@DIAMOND") center/contain no-repeat;display:flex;align-items:center;justify-content:center;font:400 42px Ortica,serif;color:#fff;text-shadow:0 2px 3px #000;z-index:2}
.hb .hp{position:absolute;left:100px;top:6px}.hb .xp{position:absolute;left:100px;top:74px}

.meter{width:100%}
.mtitle{font:400 24px Museum,serif;letter-spacing:2px;text-align:center;color:var(--cream)}
.mends{display:flex;justify-content:space-between;font-size:21px;color:var(--dim);margin:-16px 4px 10px;padding:0 26px}
.mrow{display:flex;align-items:center;gap:12px;margin-top:8px;font-size:23px;line-height:1.14}
.mrow .ic{flex:none;width:40px;height:40px}

.tri{position:relative;width:620px;height:470px;margin:0 auto}
.tline{position:absolute;inset:0;width:100%;height:100%}
.thead{position:absolute;width:44px;height:44px;background:#e0b65a;-webkit-mask-size:contain;-webkit-mask-position:center;-webkit-mask-repeat:no-repeat}
.tbeat{position:absolute;width:120px;text-align:center;font-size:20px;color:var(--dim)}
.tn{position:absolute;width:180px;text-align:center}
.tn b{display:block;font:400 25px/1 Ortica,serif;margin-top:-2px;text-shadow:0 2px 3px #000}
"""
for _k, _v in dict(FONTS=FONTS,
                   OUTER=sprite(FM + "FantasyMenus/SPR_FantasyMenus_Frame_Box_Large_02"), OUTER_MASK=sprite(FM + "FantasyMenus/SPR_FantasyMenus_Frame_Box_Large_02_Mask"),
                   PANEL=sprite(FM + "FantasyMenus/SPR_FantasyMenus_Frame_Box_Medium_03"), PANEL_MASK=sprite(FM + "FantasyMenus/SPR_FantasyMenus_Frame_Box_Medium_03_Mask"),
                   PATTERN=sprite(FM + "General/SPR_FantasyMenus_Background_Pattern_03"), CROWN=sprite(FW + "FantasyWarrior/SPR_FantasyWarrior_Frame_Top_01"),
                   RULE=sprite(FW + "FantasyWarrior/SPR_FantasyWarrior_Tracery_Horizontal_01"), RING=sprite(FW + "FantasyWarrior/SPR_FantasyWarrior_Container_02"),
                   KEYCAP=sprite(MK + "Button_Clean"), PARCHMENT=sprite(FM + "General/SPR_FantasyMenus_Box_Small_Parchment_02"),
                   BAR=sprite(FW + "FantasyWarrior/SPR_FantasyWarrior_Bar_Horizontal_03"), BAR_MASK=sprite(FW + "FantasyWarrior/SPR_FantasyWarrior_Bar_Horizontal_03_Mask"),
                   DIAMOND=sprite(FW + "FantasyWarrior/SPR_FantasyWarrior_Frame_Diamond_01")).items():
    CSS = CSS.replace("@" + _k + '"', _v + '"').replace("@" + _k + "/", _v + "/")

CHECK = """<script>addEventListener('load',function(){document.fonts.ready.then(function(){
var b=document.querySelector('.body'),n=document.querySelector('.inner');
function fits(){if(n.getBoundingClientRect().height>b.clientHeight)return false;
var ok=true;document.querySelectorAll('.pn').forEach(function(e){if(e.getBoundingClientRect().right>1530)ok=false});document.querySelectorAll('.th b').forEach(function(e){var p=e.closest('.pn').getBoundingClientRect();if(e.getBoundingClientRect().right>p.right-14)ok=false});document.querySelectorAll('.tri,.hb').forEach(function(e){if(e.getBoundingClientRect().width>e.parentElement.getBoundingClientRect().width+1)ok=false});
document.querySelectorAll('.tcol,.tile,.step,.pt,.urow').forEach(function(e){if(e.scrollWidth>e.clientWidth+1)ok=false});return ok}
var z=1.34;for(;z>1.0;z-=0.02){n.style.zoom=z;if(fits())break}if(z<=1.0)n.style.zoom=1;
var bad=[];if(n.getBoundingClientRect().height>b.clientHeight+1)bad.push('body');document.querySelectorAll('.pn').forEach(function(e){if(e.getBoundingClientRect().right>1530)bad.push('right edge '+e.className)});
document.querySelectorAll('.tcol,.tile,.step,.pt,.urow').forEach(function(e){if(e.scrollHeight>e.clientHeight+1||e.scrollWidth>e.clientWidth+1)bad.push(e.className)});
var d=document.createElement('meta');d.name=bad.length?'overflow':'fit';d.content=bad.join(';')||String(z);document.head.appendChild(d)})})</script>"""

def page(c, index, total):
    label = tr("Tech Tree") if c["ch"] == "T" else fmt("Chapter %s: %s", c["ch"], tr(CH[c["ch"]]))
    dense = ' class="dense"' if 'class="tree"' in c["body"] else ""
    return ('<!doctype html><html><head><meta charset="utf-8"><style>%s</style>%s</head><body%s style="--accent:%s"><div class="bgfill"></div><div class="bgpat"></div><div class="bgframe"></div><div class="crown"></div>'
            '<div class="tag">%s</div><h1>%s</h1><div class="lede">%s</div><div class="rule"></div><div class="body"><div class="inner">%s</div></div><div class="foot"><span>THE WANING BORDER</span><span>%s</span></div></body></html>') % (
        CSS, CHECK, dense, c["accent"], E(label), c["title"], c["lede"], c["body"], c["id"])

def render(c, i, n, tmp):
    h = os.path.join(tmp, c["id"] + ".html")
    open(h, "w", encoding="utf8").write(page(c, i, n))
    folder = OUT if c["id"] in NOT_IN_GAME else GAME
    os.makedirs(folder, exist_ok=True)
    png = os.path.join(folder, "card_%s.png" % c["id"])
    flags = [CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files",
             "--window-size=1600,900", "--virtual-time-budget=2500", "--user-data-dir=" + os.path.join(tmp, "chrome")]
    url = "file:///" + h.replace(os.sep, "/")
    subprocess.run(flags + ["--screenshot=" + png, url], capture_output=True, timeout=120)
    dom = subprocess.run(flags + ["--dump-dom", url], capture_output=True, timeout=120).stdout.decode("utf8", "ignore")
    m = re.search(r'name="overflow" content="([^"]*)"', dom)
    if m: OVERFLOW.append("%s/%s: %s" % (LANG, c["id"], m.group(1)))
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
    """Give a game card a meta whose guid follows from its path."""
    if card_id in NOT_IN_GAME: return
    fn = "card_%s.png" % card_id
    guid = hashlib.md5(("TutorialCards/%s/%s" % (LANG, fn)).encode()).hexdigest()
    open(os.path.join(GAME, fn) + ".meta", "w", encoding="utf8", newline="\n").write(META % guid)

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
    if OVERFLOW:
        sys.exit("Cards overflow:\n  " + "\n  ".join(OVERFLOW))
    from PIL import Image
    paths = [os.path.join(OUT if c["id"] in NOT_IN_GAME else GAME, "card_%s.png" % c["id"]) for c in cards]
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
