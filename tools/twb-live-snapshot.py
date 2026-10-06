#!/usr/bin/env python3
"""One RUNNING match's current state -> one small JSON document.

    python tools/twb-live-snapshot.py <match-log-dir> <out.json> [--max-units N]

The Muster Rolls page (tools/twb-report-page.html) has a LIVE MODE: published
as an artifact with the `db` capability, it subscribes to the document
`live/state` and redraws the match in place every time that document is
rewritten. This script builds that document from the logs a match is still
writing; the publisher (an agent session) writes it to `live/state` with one
"set" call every 20-30 s. README-harness.md, "Live mode", has the steps.

WHAT IS READ, and why it is safe while the game is writing:
  MapTrace.txt         append-only. Everything that is not a P line (U units,
                       B buildings, C completions, L levels, D deaths, R/N/TG/TR
                       the map, TO/TM territory) is scanned from the whole file
                       with one C-level regex pass that never builds a line
                       list; the P lines (99 % of the file) are read from the
                       TAIL only -- the last two whole frames.
  Metrics_Faction.csv  the last row per faction (banks, pop, territories).
  Metrics_Score.csv    the last row per faction (the score and its parts).
  Console.log          the match header: Label, AI roster, world extent.
  Summary.txt          present once the match has ended.
A last line with no newline is the game mid-write and is ignored. The newest
frame is used only when the match has ended; while it runs, the frame before
it is used, because the newest may still be half written.

SHAPE (every coordinate is a HALF-METRE integer: x2 = round(x * 2)):
  v, matchKey, map, label, aiRoster, t (game s), seq (t in ds), wrote (epoch
  ms), finished, outcome, world [x0,z0,x1,z1], facs [names],
  factions [{name, units, army, pop, popMax, supplies, iron, veilstone,
             veilsteel, territories, buildings, t}],
  utypes [[name, cls, fl]], units [[facIdx, x2, z2, typeIdx, flags]],
  btypes [names], buildings [[facIdx, typeIdx, level, site, x2, z2, w2, h2,
             yaw (, gx, gz, gw, gh when on the build grid)]],
  terrOwner {idx: owner}, meter [[idx, holder, pct, contested]],
  terr {cell, x0, z0, w, h, rows} (the partition, run-length rows),
  regions [[name, x2, z2]], nodes [[kind, x2, z2]], curse {units, nodes},
  score {final: {faction: {score, economy, strategy, military, kills, deaths,
             razed, kd, territories, fortresses, techs, levels, earned,
             incomePerMin, t}}} from Metrics_Score.csv (null without it),
  cap {units: kept/of} only when units had to be capped.
The db capability takes a document of at most 256 KiB; this stays under
BUDGET (200 KB) by thinning units evenly, and says so in `cap`.
"""
import io, json, os, re, sys, time

BUDGET = 200 * 1000
TAIL = 512 * 1024

STAMP = re.compile(r"^(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})_(.+?)(_host|_client\d+|-\d+)?$")
WORLD = re.compile(r"covering world X\[(-?\d+)\.\.(-?\d+)\] Z\[(-?\d+)\.\.(-?\d+)\]")
RUNINFO = re.compile(r"^(Label|AI)\s+:\s*(.*)$")
# every line that is NOT a position sample, from the whole file in one pass
NONP = re.compile(rb"^(?:[^P\n][^\n]*|P[^ \n][^\n]*)\n", re.M)


def num(s):
    return float(s.replace(",", "."))


def h2(v):
    return int(round(v * 2))


def complete(data):
    """Drop a trailing line the game has not finished writing."""
    k = data.rfind(b"\n")
    return data[:k + 1] if k >= 0 else b""


def read_trace(path, finished):
    with open(path, "rb") as fh:
        data = complete(fh.read())
    units, blds, built, levels, dead = {}, {}, set(), {}, set()
    regions, nodes, own, meter = [], [], {}, {}
    terr = None
    for m in NONP.finditer(data):
        p = m.group(0).decode("utf-8", "ignore").split()
        if not p:
            continue
        k = p[0]
        try:
            if k == "U" and len(p) >= 5:
                units[p[2]] = (p[3], p[4], int(p[5]) if len(p) > 5 else None,
                               int(p[6]) if len(p) > 6 else 0)
            elif k == "B" and len(p) >= 9:
                b = dict(f=p[3], name=p[4], x=num(p[5]), z=num(p[6]), w=num(p[7]), h=num(p[8]),
                         ty=p[4], g=None, yaw=0, site=0)
                if len(p) >= 16:
                    b["ty"] = p[9]
                    if p[10] != "*":
                        b["g"] = [int(p[10]), int(p[11]), int(p[12]), int(p[13])]
                    b["yaw"] = int(p[14])
                    b["site"] = int(p[15])
                blds[p[2]] = b
            elif k == "C":
                built.add(p[2])
            elif k == "L":
                levels[p[2]] = int(p[3])
            elif k == "D":
                dead.add(p[2])
            elif k == "R":
                regions.append([p[2].replace("_", " "), h2(num(p[3])), h2(num(p[4]))])
            elif k == "N":
                nodes.append([p[2], h2(num(p[3])), h2(num(p[4]))])
            elif k == "TG":
                terr = dict(cell=num(p[1]), x0=num(p[2]), z0=num(p[3]), w=int(p[4]), h=int(p[5]),
                            rows=[None] * int(p[5]))
            elif k == "TR" and terr is not None:
                j = int(p[1])
                runs = []
                for tok in p[2:]:
                    a, _, c = tok.partition(":")
                    runs += [int(a), int(c)]
                if 0 <= j < terr["h"]:
                    terr["rows"][j] = runs
            elif k == "TO":
                own[int(p[2])] = p[3]
            elif k == "TM":
                meter[int(p[2])] = [int(p[2]), p[3], int(p[4]), int(p[5])]
        except (ValueError, IndexError):
            continue
    if terr is not None and any(r is None for r in terr["rows"]):
        terr = None                       # a hole in the partition is a lie

    # the last whole frames, from the tail
    size = len(data)
    span = TAIL
    frames = {}
    while True:
        chunk = data[max(0, size - span):]
        if size > span:
            chunk = chunk[chunk.find(b"\n") + 1:]        # first line may be cut
        frames = {}
        order = []
        for line in chunk.split(b"\n"):
            if not line.startswith(b"P "):
                continue
            q = line.split()
            if len(q) < 6:
                continue
            try:
                t = round(num(q[1].decode()), 1)
                rec = (q[2].decode(), num(q[3].decode()), num(q[4].decode()), int(q[5]))
            except ValueError:
                continue
            fr = frames.get(t)
            if fr is None:
                fr = frames[t] = []
                order.append(t)
            fr.append(rec)
        if len(order) >= 3 or span >= size:
            break
        span *= 4
    ts = sorted(frames)
    if not ts:
        return None
    # the newest frame may be half written while the match runs; the oldest
    # one in the chunk may be cut at its start -- use neither unless forced
    if finished or len(ts) == 1:
        t = ts[-1]
    else:
        t = ts[-2]
    return dict(units=units, blds=blds, built=built, levels=levels, dead=dead,
                regions=regions, nodes=nodes, own=own, meter=meter, terr=terr,
                t=t, pos=frames[t])


def read_lines(path):
    try:
        with open(path, "rb") as fh:
            data = fh.read()
    except OSError:
        return []
    ended = data.endswith(b"\n")
    lines = data.decode("utf-8", "ignore").splitlines()
    if lines and not ended:
        lines.pop()
    return lines


def faction_stats(d):
    lines = read_lines(os.path.join(d, "Metrics_Faction.csv"))
    if not lines:
        return {}
    head = lines[0].split(",")
    out = {}
    for ln in lines[1:]:
        c = ln.split(",")
        if len(c) != len(head):
            continue
        r = dict(zip(head, c))
        try:
            t = num(r["t"])
        except (KeyError, ValueError):
            continue
        f = r.get("faction")
        if f and (f not in out or out[f]["t"] <= t):
            row = dict(t=t)
            for k in ("pop", "popMax", "supplies", "iron", "veilstone", "veilsteel",
                      "territories", "units", "buildings"):
                try:
                    row[k] = int(num(r.get(k, "0") or "0"))
                except ValueError:
                    row[k] = 0
            out[f] = row
    return out


# Metrics_Score.csv (docs/Design/Score.md): the same columns the match
# extractor keeps, so the page's standings table reads one shape in both
# modes. score = economy + strategy + military; kd = kills / max(1, deaths).
SCORE_COLS = ("score", "economy", "strategy", "military", "kills", "deaths",
              "razed", "territories", "fortresses", "techs", "levels", "earned",
              "incomePerMin")


def score_final(d):
    """The latest score row per faction, {faction: {...}}, or None when the
    match has written no Metrics_Score.csv (yet, or ever)."""
    lines = read_lines(os.path.join(d, "Metrics_Score.csv"))
    if not lines:
        return None
    head = lines[0].split(",")
    out = {}
    for ln in lines[1:]:
        c = ln.split(",")
        if len(c) != len(head):
            continue
        r = dict(zip(head, c))
        try:
            t = num(r["t"])
        except (KeyError, ValueError):
            continue
        f = r.get("faction")
        if f and (f not in out or out[f]["t"] <= t):
            row = dict(t=t)
            for k in SCORE_COLS:
                try:
                    row[k] = int(round(num(r.get(k, "0") or "0")))
                except ValueError:
                    row[k] = 0
            try:
                row["kd"] = round(num(r.get("kd", "0") or "0"), 2)
            except ValueError:
                row["kd"] = 0.0
            out[f] = row
    return out or None


def header(d):
    label, ai, world = "", "", None
    for cn in ("Console.log", "Console-1.log", "Console-2.log", "Console-3.log", "Console-4.log"):
        for line in read_lines(os.path.join(d, cn))[:400]:
            m = RUNINFO.match(line.strip())
            if m:
                if m.group(1) == "Label":
                    label = label or m.group(2).strip()
                else:
                    ai = ai or m.group(2).strip()
            if world is None:
                w = WORLD.search(line)
                if w:
                    world = [int(w.group(1)), int(w.group(3)), int(w.group(2)), int(w.group(4))]
        if ai or world:
            break
    roster = {}
    for part in ai.split(","):
        bits = part.split()
        if len(bits) >= 2:
            roster[bits[0]] = dict(d=bits[1], p=bits[2] if len(bits) > 2 else "")
    return label, roster, world


def summary(d):
    out = {}
    for line in read_lines(os.path.join(d, "Summary.txt")):
        if ":" in line and not line.startswith("==="):
            k, _, v = line.partition(":")
            out[k.strip()] = v.strip()
    return out


def build(d, max_units=None):
    key = os.path.basename(os.path.normpath(d))
    sm = STAMP.match(key)
    mapname = sm.group(2) if sm else key
    summ = summary(d)
    finished = bool(summ)
    tr = read_trace(os.path.join(d, "MapTrace.txt"), finished)
    if tr is None:
        raise SystemExit("no position frames in %s/MapTrace.txt yet" % d)
    label, roster, world = header(d)
    stats = faction_stats(d)
    score = score_final(d)

    facs, fidx = [], {}

    def fi(name):
        if name not in fidx:
            fidx[name] = len(facs)
            facs.append(name)
        return fidx[name]

    for f in sorted(stats):
        fi(f)

    utypes, uidx = [], {}
    units, army = [], {}
    curse_units = 0
    for uid, x, z, fl in tr["pos"]:
        u = tr["units"].get(uid)
        f, name, cls, ufl = u if u else ("?", "?", None, 0)
        key_t = (name, cls, ufl)
        ti = uidx.get(key_t)
        if ti is None:
            ti = uidx[key_t] = len(utypes)
            utypes.append([name, cls, ufl])
        units.append([fi(f), h2(x), h2(z), ti, fl & 7])
        if f == "Border":
            curse_units += 1
        elif cls not in (5, 6):
            army[f] = army.get(f, 0) + 1

    btypes, bidx = [], {}
    blds = []
    curse_nodes = 0
    for bid, b in tr["blds"].items():
        if bid in tr["dead"]:
            continue
        ti = bidx.get(b["ty"])
        if ti is None:
            ti = bidx[b["ty"]] = len(btypes)
            btypes.append(b["ty"])
        site = 1 if (b["site"] and bid not in tr["built"]) else 0
        row = [fi(b["f"]), ti, tr["levels"].get(bid, 0), site,
               h2(b["x"]), h2(b["z"]), h2(b["w"]), h2(b["h"]), b["yaw"]]
        if b["g"]:
            row += b["g"]
        blds.append(row)
        if b["f"] == "Border":
            curse_nodes += 1

    terr_count = {}
    for o in tr["own"].values():
        if o != "-":
            terr_count[o] = terr_count.get(o, 0) + 1
    ucount = {}
    for u in units:
        ucount[facs[u[0]]] = ucount.get(facs[u[0]], 0) + 1
    bcount = {}
    for b in blds:
        bcount[facs[b[0]]] = bcount.get(facs[b[0]], 0) + 1

    factions = []
    for f in facs:
        s = stats.get(f, {})
        factions.append(dict(
            name=f, units=ucount.get(f, 0), army=army.get(f, 0),
            pop=s.get("pop"), popMax=s.get("popMax"), supplies=s.get("supplies"),
            iron=s.get("iron"), veilstone=s.get("veilstone"), veilsteel=s.get("veilsteel"),
            territories=s.get("territories", terr_count.get(f, 0)),
            buildings=bcount.get(f, 0), t=s.get("t")))

    doc = dict(
        v=1, matchKey=key, map=mapname, label=label, aiRoster=roster,
        t=tr["t"], seq=int(round(tr["t"] * 10)), wrote=int(time.time() * 1000),
        finished=finished, outcome=summ.get("Outcome", ""),
        world=world, facs=facs, factions=factions,
        utypes=utypes, units=units, btypes=btypes, buildings=blds,
        terrOwner={str(k): v for k, v in sorted(tr["own"].items()) if v != "-"},
        # only meters the page labels: someone filling, draining, or frozen
        meter=[m for i, m in sorted(tr["meter"].items())
               if (m[1] != "-" or m[2] > 0)
               and not (m[2] >= 100 and m[1] == tr["own"].get(i) and not m[3])],
        terr=tr["terr"], regions=tr["regions"], nodes=tr["nodes"],
        curse=dict(units=curse_units, nodes=curse_nodes),
        # the latest score row per faction, in the match extractor's
        # `score.final` shape; None before the game writes Metrics_Score.csv
        score=dict(final=score) if score else None)

    def size():
        return len(json.dumps(doc, separators=(",", ":")).encode("utf-8"))

    total = len(units)
    keep = total if max_units is None else min(total, max_units)
    while True:
        if keep < total:
            step = total / float(keep)
            doc["units"] = [units[int(i * step)] for i in range(keep)]
            doc["cap"] = dict(units="%d/%d" % (keep, total))
        if size() <= BUDGET or keep <= 50:
            break
        if doc.get("terr") is not None and keep < total:
            doc["terr"] = None            # the page keeps the partition it already has
            continue
        keep = int(keep * 0.75)
    return doc


def main():
    args = list(sys.argv[1:])
    max_units = None
    if "--max-units" in args:
        i = args.index("--max-units")
        max_units = int(args[i + 1])
        del args[i:i + 2]
    if len(args) != 2:
        raise SystemExit(__doc__.split("\n\n")[1])
    d, out = args
    t0 = time.time()
    doc = build(d, max_units)
    text = json.dumps(doc, separators=(",", ":"))
    with io.open(out, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    n = len(text.encode("utf-8"))
    print("%s  t=%s  %d units, %d buildings, %d factions  %.1f KB%s  (%.2f s)%s"
          % (doc["matchKey"], doc["t"], len(doc["units"]), len(doc["buildings"]),
             len(doc["facs"]), n / 1000.0,
             "  cap " + doc["cap"]["units"] if doc.get("cap") else "",
             time.time() - t0, "  FINISHED" if doc["finished"] else ""))
    if n > 256 * 1024:
        raise SystemExit("over the 256 KiB a db document holds")


if __name__ == "__main__":
    main()
