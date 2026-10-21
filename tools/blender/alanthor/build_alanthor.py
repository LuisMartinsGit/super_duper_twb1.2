# build_alanthor.py - every Alanthor building level (and each Alanthor-only
# building's base form) as its own model in the House's architecture
# (docs/Design/Art_Direction.md § 13, 2026-10-20). One .blend + one FBX per
# entry, in the building's own Art/ folder.
#
#   blender -b --factory-startup --python tools/blender/alanthor/build_alanthor.py -- [name ...]
#
# No name builds everything. A level grows a little (bays, roof tiers, towers,
# lanterns) — never a size class: every level of a building stands on the same
# ground. The House line and the Fortress have their own scripts
# (build_house_family.py, build_fortress.py).
import sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from kitlib import *

A = os.path.join(TT, "Civs", "Alanthor", "Buildings")
S = os.path.join(TT, "Sects")
PREVIEW = os.path.join(REPO, "Temp", "alanthor-previews")
PI_ = math.pi


def garrison(lv):
    a = Assembly()
    front = a.terrace(5, 5)
    a.hall(bays=3, loc=(0, 0, 0))
    if lv >= 2: a.upper(0.55, 0.62, loc=(0, 0), base_z=3.6)
    for sx in (+1, -1):
        a.tower(sx * 2.15, front + 1.25, rot=PI_, storeys=3 if lv >= 3 else 2, s0=0.52)
        if lv >= 3: a.lantern(sx * 1.4, front + 0.3, 1.9)
    a.posts([(sx * px, py) for sx in (1, -1) for (px, py) in ((3.7, -1.9), (4.5, -1.9), (3.7, -2.8), (4.5, -2.8))])
    return a, os.path.join(A, "Garrison", "Art")


def guild(lv):
    # The guild hall of the gatherers: a hall on a walled yard, its stores
    # under an open pavilion; L2 a second pavilion, L3 an upper storey.
    a = Assembly()
    a.terrace(0, 0)
    a.hall(bays=1, loc=(-0.6, 0.3, 0), scale=(0.78, 0.78, 0.85))
    a.pavilion(1, loc=(1.6, -0.4, 0), scale=(0.45, 0.45, 0.62))
    if lv >= 2: a.pavilion(1, loc=(1.6, 1.4, 0), scale=(0.45, 0.45, 0.62))
    if lv >= 3: a.upper(0.42, 0.5, loc=(-0.6, 0.3), base_z=3.1)
    return a, os.path.join(A, "Guild", "Art")


def mine(lv):
    # The pithead: a bare stone platform, the shaft under an open pavilion,
    # the winding house behind; L3 a lookout tower.
    a = Assembly()
    a.terrace(0, 0, wall=False)
    a.pavilion(1, loc=(0, -0.6, 0), scale=(0.62, 0.62, 0.72))
    if lv >= 2: a.hall(bays=1, loc=(0, 1.3, 0), scale=(0.62, 0.45, 0.7), lanterns=False)
    if lv >= 3: a.tower(1.9, 1.6, rot=PI_, storeys=2, s0=0.32)
    a.posts([(-2.0, -1.6), (2.0, -1.6)], scale=(0.6, 0.6, 0.45))
    a.lantern(-1.2, -1.2, 1.5)
    return a, os.path.join(A, "Mine", "Art")


def vault(lv):
    # The treasury: a hall on a double dais behind a planter-less wall; L2
    # its upper storey, L3 two lantern towers.
    a = Assembly()
    a.terrace(2, 2, planters=False)
    a.dais(0.5); a.dais(1.0)
    a.hall(bays=1, loc=(0, 0, 1.0))
    if lv >= 2: a.upper(0.55, 0.62, loc=(0, 0), base_z=4.6)
    if lv >= 3:
        for sx in (+1, -1): a.tower(sx * 3.0, -2.6, rot=PI_, storeys=2, s0=0.36)
    for sx in (+1, -1): a.lantern(sx * 1.6, -2.5, 1.6)
    return a, os.path.join(A, "VaultOfAlmierra", "Art")


def outpost(lv):
    # The trading post on an outcrop: a market pavilion over the stair; L2 a
    # store behind, L3 the store's upper storey.
    a = Assembly()
    a.terrace(0, 0)
    a.pavilion(1, loc=(0, -0.2, 0), scale=(0.7, 0.7, 0.72))
    if lv >= 2: a.hall(bays=1, loc=(0, 1.0, 0), scale=(0.55, 0.4, 0.75), lanterns=False)
    if lv >= 3: a.upper(0.36, 0.45, loc=(0, 1.0), base_z=2.9, door=False)
    a.lantern(1.7, -1.3, 1.5)
    if lv >= 2: a.lantern(-1.7, -1.3, 1.5)
    return a, os.path.join(A, "TradingOutpost", "Art")


def archery(lv):
    # The shooting gallery: a long open pavilion at the back, targets on the
    # yard; L2 a fletcher's hall, L3 a watch tower.
    a = Assembly()
    front = a.terrace(5, 5)
    a.pavilion(3, loc=(0, 0.6, 0), scale=(1, 0.8, 0.85))
    a.posts([(x, front + 2.2) for x in (-3.0, -1.5, 0.0, 1.5, 3.0)], scale=(0.5, 0.5, 0.5))
    if lv >= 2: a.hall(bays=1, loc=(-3.6, -2.4, 0), rot=PI_ / 2, scale=(0.6, 0.6, 0.8))
    if lv >= 3: a.tower(3.9, front + 1.3, rot=PI_, storeys=3, s0=0.45)
    return a, os.path.join(A, "ArcheryRange", "Art")


def stable(lv):
    # The royal stable: a long low stall hall, hitching posts on the yard;
    # L2 the hayloft upper storey, L3 two gate towers.
    a = Assembly()
    front = a.terrace(5, 5)
    a.hall(bays=3, loc=(0, 0, 0), scale=(1, 0.9, 0.8))
    if lv >= 2: a.upper(0.5, 0.55, loc=(0, 0), base_z=3.0, door=False)
    a.posts([(x, front + 2.4) for x in (-2.4, -1.2, 1.2, 2.4)], scale=(0.55, 0.55, 0.4))
    a.bushes([(-4.0, -3.5), (4.0, -3.5)])
    if lv >= 3:
        for sx in (+1, -1): a.tower(sx * 2.0, front + 1.2, rot=PI_, storeys=2, s0=0.42)
    return a, os.path.join(A, "RoyalStable", "Art")


def siegeyard(lv):
    # The engine yard: a wide workshop roof on pillars over a bare yard;
    # L0 (the base form) is the yard and its posts alone.
    a = Assembly()
    front = a.terrace(5, 5, planters=False)
    if lv >= 1: a.pavilion(2, loc=(0, 0.2, 0), scale=(1.25, 1.1, 1.0))
    if lv >= 2: a.hall(bays=1, loc=(-3.4, -3.0, 0), rot=PI_ / 2, scale=(0.6, 0.6, 0.8))
    if lv >= 3: a.tower(3.9, front + 1.3, rot=PI_, storeys=3, s0=0.45)
    a.posts([(x, front + 1.6) for x in (-1.2, 1.2)], scale=(0.7, 0.7, 0.55))
    return a, os.path.join(A, "SiegeYard", "Art")


def watchtower(lv):
    a = Assembly()
    a.terrace(0, 0, wall=False)
    a.tower(0, 0.4, rot=PI_, storeys=lv + 1, s0=0.8, z=0.63)
    a.lantern(1.3, -1.1, 1.6)
    return a, os.path.join(A, "Tower", "Art")


def ballista():
    a = Assembly(); a.terrace(0, 0, planters=False)
    a.lantern(1.9, -1.9, 1.3); return a, os.path.join(A, "BallistaEmplacement", "Art")


def trebuchet():
    a = Assembly(); a.terrace(2, 2, planters=False)
    for sx in (+1, -1): a.lantern(sx * 2.8, -2.8, 1.3)
    return a, os.path.join(A, "TrebuchetEmplacement", "Art")


def reliquary():
    a = Assembly(); a.terrace(2, 2); a.dais(0.6)
    a.hall(1, loc=(0, 0, 0.6)); z = a.upper(0.6, 0.6, base_z=4.2)
    a.upper(0.4, 0.5, base_z=z - 0.9, door=False)
    return a, os.path.join(S, "Antiquity", "Buildings", "Reliquary", "Art")


def mendinghall():
    a = Assembly(); front = a.terrace(2, 2)
    a.pavilion(2, loc=(0, 0.3, 0), scale=(0.9, 0.9, 0.85))
    a.bushes([(x, y) for x in (-2.6, -1.3, 1.3, 2.6) for y in (front + 1.0,)])
    a.lantern(0, -0.4, 1.9)
    return a, os.path.join(S, "Renewal", "Buildings", "MendingHall", "Art")


def stonehold():
    a = Assembly(); front = a.terrace(2, 2, planters=False)
    a.hall(1, loc=(0, 0, 0), scale=(1.1, 1.0, 0.8), lanterns=False)
    for sx in (+1, -1):
        for sy in (front + 1.0, 2.0): a.tower(sx * 3.1, sy, rot=PI_, storeys=1, s0=0.4)
    return a, os.path.join(S, "Fortitude", "Buildings", "Stonehold", "Art")


def treasury():
    a = Assembly(); a.terrace(2, 2, planters=False); a.dais(0.5)
    a.hall(2, loc=(0, 0, 0.5), scale=(0.8, 0.9, 0.9))
    for x in (-2.2, -0.7, 0.7, 2.2): a.lantern(x, -2.4, 1.4)
    return a, os.path.join(S, "Prosperity", "Buildings", "Treasury", "Art")


def musteryard():
    a = Assembly(); front = a.terrace(2, 2, planters=False)
    for sx in (+1, -1): a.tower(sx * 1.8, front + 1.2, rot=PI_, storeys=2, s0=0.42)
    a.posts([(x, y) for x in (-2.4, -1.2, 0.0, 1.2, 2.4) for y in (-0.6, 0.8)], scale=(0.55, 0.55, 0.45))
    return a, os.path.join(S, "War", "Buildings", "MusterYard", "Art")


RA = os.path.join(S, "Renewal", "Abilities", "RaiseAnew", "Art")


def renewal_tower():
    a = Assembly(); a.terrace(0, 0, wall=False); a.tower(0, 0.4, rot=PI_, storeys=2, s0=0.7)
    return a, RA


def renewal_fortification():
    a = Assembly(); front = a.terrace(2, 2, planters=False)
    for sx in (+1, -1): a.tower(sx * 2.4, front + 1.2, rot=PI_, storeys=2, s0=0.45)
    return a, RA


def renewal_fortress():
    a = Assembly(); front = a.terrace(4, 4, planters=False)
    a.hall(1, loc=(0, 0.4, 0))
    for sx in (+1, -1):
        a.tower(sx * 3.6, front + 1.2, rot=PI_, storeys=2, s0=0.45)
        a.tower(sx * 3.8, 1.9, rot=0.0, storeys=2, s0=0.45)
    return a, RA


def field_hospital():
    # The Litharch's healing tent: an open pavilion over a bare platform,
    # bushes and lanterns round it.
    a = Assembly(); a.terrace(0, 0, wall=False)
    a.pavilion(1, loc=(0, 0.1, 0), scale=(0.75, 0.75, 0.7))
    a.bushes([(-2.0, -1.4), (2.0, -1.4), (-2.0, 1.9), (2.0, 1.9)], s=0.8)
    a.lantern(1.3, -0.9, 1.5); a.lantern(-1.3, -0.9, 1.5)
    return a, os.path.join(S, "Renewal", "Abilities", "DeployFieldHospital", "Art")


CH = os.path.join(S, "Shared", "Chapel", "Art")


def chapel(kind):
    # A sect chapel: a small shrine on a bare platform, one identity per sect.
    a = Assembly(); a.terrace(0, 0, wall=False)
    if kind == "Antiquity":      # the archive shrine: a hall with a tall upper tier
        a.hall(1, loc=(0, 0.2, 0), scale=(0.62, 0.62, 0.7), lanterns=False)
        a.upper(0.42, 0.55, loc=(0, 0.2), base_z=2.75, door=False)
    elif kind == "Renewal":      # the healing garden: an open pavilion among bushes
        a.pavilion(1, loc=(0, 0.2, 0), scale=(0.6, 0.6, 0.65))
        a.bushes([(-1.9, -1.3), (1.9, -1.3)], s=0.75)
    elif kind == "Fortitude":    # the bulwark: a squat two-storey tower
        a.tower(0, 0.4, rot=math.pi, storeys=2, s0=0.62)
    else:                        # Prosperity, the counting house: a lantern-hung hall
        a.hall(1, loc=(0, 0.2, 0), scale=(0.66, 0.6, 0.72))
        a.lantern(-1.3, -1.0, 1.4); a.lantern(1.3, -1.0, 1.4)
    return a, CH


def temple():
    # The Temple of Ridan, Alanthor's look (it has no levels — the base SO's
    # culturePrefabs): a hall on a dais behind a pillared portico, two rear
    # towers, lanterns at the stair.
    a = Assembly(); front = a.terrace(2, 2)
    a.dais(0.6)
    a.hall(1, loc=(0, 0.3, 0.6), scale=(0.95, 0.9, 1.0))
    a.upper(0.5, 0.58, loc=(0, 0.3), base_z=4.2, door=False)
    a.pavilion(1, loc=(0, -1.7, 0), scale=(0.7, 0.4, 0.8))
    for sx in (+1, -1):
        a.tower(sx * 3.0, 2.0, rot=0.0, storeys=2, s0=0.36)
        a.lantern(sx * 1.6, front + 1.2, 1.6)
    return a, os.path.join(TT, "Age0", "Buildings", "TempleOfRidan", "Art")


MODELS = {}
for lv in (2, 3): MODELS["alanthor_Garrison_lv%d" % lv] = (lambda l=lv: garrison(l))
for lv in (1, 2, 3):
    MODELS["alanthor_Guild_lv%d" % lv] = (lambda l=lv: guild(l))
    MODELS["alanthor_Mine_lv%d" % lv] = (lambda l=lv: mine(l))
    MODELS["alanthor_Vault_lv%d" % lv] = (lambda l=lv: vault(l))
    MODELS["alanthor_TradingOutpost_lv%d" % lv] = (lambda l=lv: outpost(l))
    MODELS["alanthor_ArcheryRange_lv%d" % lv] = (lambda l=lv: archery(l))
    MODELS["alanthor_RoyalStable_lv%d" % lv] = (lambda l=lv: stable(l))
    MODELS["alanthor_SiegeYard_lv%d" % lv] = (lambda l=lv: siegeyard(l))
    MODELS["alanthor_WatchTower_lv%d" % lv] = (lambda l=lv: watchtower(l))
MODELS["Alanthor_SiegeYard_Age_0"] = lambda: siegeyard(0)
MODELS["Alanthor_BallistaEmplacement_Age_0"] = ballista
MODELS["Alanthor_TrebuchetEmplacement_Age_0"] = trebuchet
MODELS["Sect_Reliquary_Age_0"] = reliquary
MODELS["Sect_MendingHall_Age_0"] = mendinghall
MODELS["Sect_Stonehold_Age_0"] = stonehold
MODELS["Sect_Treasury_Age_0"] = treasury
MODELS["Sect_MusterYard_Age_0"] = musteryard
MODELS["Renewal_Tower_Age_0"] = renewal_tower
MODELS["Renewal_Fortification_Age_0"] = renewal_fortification
MODELS["Renewal_Fortress_Age_0"] = renewal_fortress
MODELS["Renewal_FieldHospital_Age_0"] = field_hospital
MODELS["alanthor_TempleOfRidan_lv1"] = temple
for k in ("Antiquity", "Renewal", "Fortitude", "Prosperity"):
    MODELS["Chapel_Sect_%s_Age_0" % k] = (lambda kk=k: chapel(kk))

if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name in (args or list(MODELS)):
        a, out = MODELS[name]()
        a.finish(name, out, PREVIEW)
    print("DONE")
