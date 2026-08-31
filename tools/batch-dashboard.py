#!/usr/bin/env python3
"""Render the live headless-batch state as a single HTML dashboard.

Reads whatever the running batch has written so far -- sessions still in flight
are included, so the page is useful before the batch ends.

    python tools/batch-dashboard.py <logs-dir> <out.html>
"""
import csv, os, re, sys, glob, time, html
from collections import defaultdict

LOGS   = sys.argv[1]
OUT    = sys.argv[2]
TARGET = 120          # the acceptance floor: every faction must reach this cap
LIMIT  = 1200         # simulated seconds per match

FACTIONS = ["Blue", "Red", "Green", "Yellow"]
HUE = {"Blue": "#5590e0", "Red": "#d1615a", "Green": "#56ae82", "Yellow": "#d0a445"}


def read_csv(path):
    if not os.path.exists(path):
        return []
    try:
        with open(path, newline="", encoding="utf-8", errors="ignore") as f:
            return list(csv.DictReader(f))
    except Exception:
        return []


def num(v, d=0):
    try:
        return int(float(v))
    except Exception:
        return d


sessions = []
for d in sorted(glob.glob(os.path.join(LOGS, "*/"))):
    rows = read_csv(os.path.join(d, "Metrics_Faction.csv"))
    if not rows:
        continue

    # Last sample per faction, and the peak cap each one ever held -- a faction
    # wiped out late still reached its cap, and the run should say so.
    last, peak, series = {}, defaultdict(int), defaultdict(list)
    for r in rows:
        f = r.get("faction")
        if f not in HUE:
            continue
        last[f] = r
        peak[f] = max(peak[f], num(r.get("popMax")))
        series[f].append((num(r.get("t")), num(r.get("popMax"))))

    t_end = max(num(r.get("t")) for r in rows)

    mil = defaultdict(int)
    brows = read_csv(os.path.join(d, "Metrics_Buildings.csv"))
    if brows:
        bt = max(num(r.get("t")) for r in brows)
        for r in brows:
            if num(r.get("t")) != bt:
                continue
            if re.search(r"Barracks|ArcheryRange|RoyalStable|SiegeYard", r.get("buildingId") or ""):
                mil[r.get("faction")] += num(r.get("count"))

    sessions.append({
        "name": os.path.basename(d.rstrip("/\\")),
        "t": t_end,
        "done": t_end >= LIMIT - 30,
        "last": last, "peak": peak, "series": series, "mil": mil,
    })

# -- Aggregates ---------------------------------------------------------
seen = hit = 0
caps, units = [], []
for s in sessions:
    for f in FACTIONS:
        if f not in s["peak"]:
            continue
        seen += 1
        caps.append(s["peak"][f])
        if s["peak"][f] >= TARGET:
            hit += 1
        r = s["last"].get(f)
        if r:
            units.append(num(r.get("units")))

pct = (100.0 * hit / seen) if seen else 0.0
med = lambda xs: sorted(xs)[len(xs) // 2] if xs else 0
mil_total = sum(sum(s["mil"].values()) for s in sessions)
mil_none = sum(1 for s in sessions for f in FACTIONS
               if f in s["peak"] and s["mil"].get(f, 0) == 0)

# -- Refusal causes, so a stall has a named reason on the page -----------
causes = defaultdict(int)
for d in glob.glob(os.path.join(LOGS, "*/")):
    for lg in glob.glob(os.path.join(d, "AI_*.log")):
        try:
            txt = open(lg, encoding="utf-8", errors="ignore").read()
        except Exception:
            continue
        for m in re.finditer(r"refused: ([^|]+)", txt):
            for item in m.group(1).split(", "):
                c = re.search(r"\[([^\]]+)\]", item)
                if c:
                    causes[re.sub(r"\d+", "N", c.group(1)).strip()] += 1
top_causes = sorted(causes.items(), key=lambda kv: -kv[1])[:6]
cause_max = max([c for _, c in top_causes], default=1)

done_n = sum(1 for s in sessions if s["done"])


def spark(series, w=132, h=30):
    """Cap-over-time trace, with the TARGET rule drawn behind it."""
    if not series:
        return ""
    xs = [p[0] for p in series]
    ys = [p[1] for p in series]
    x0, x1 = min(xs), max(xs)
    top = max(max(ys), TARGET) * 1.08 or 1
    sx = lambda x: (x - x0) / ((x1 - x0) or 1) * w
    sy = lambda y: h - (y / top) * h
    pts = " ".join("%.1f,%.1f" % (sx(x), sy(y)) for x, y in zip(xs, ys))
    ty = sy(TARGET)
    return ('<svg class="spark" viewBox="0 0 %d %d" preserveAspectRatio="none" aria-hidden="true">'
            '<line x1="0" y1="%.1f" x2="%d" y2="%.1f" class="sparkrule"/>'
            '<polyline points="%s" fill="none" stroke="currentColor" '
            'stroke-width="1.5" stroke-linejoin="round"/></svg>' % (w, h, ty, w, ty, pts))


cards = []
for s in sessions:
    rows_html = []
    for f in FACTIONS:
        if f not in s["peak"]:
            continue
        cap = s["peak"][f]
        r = s["last"].get(f, {})
        pop, un = num(r.get("pop")), num(r.get("units"))
        met = cap >= TARGET
        scale = float(max(200, cap))
        rows_html.append(
            '<div class="frow%s" style="--hue:%s">'
            '<span class="fname">%s</span>'
            '<span class="track" role="img" aria-label="peak cap %d of target %d">'
            '<span class="fill" style="width:%.1f%%"></span>'
            '<span class="rule" style="left:%.1f%%"></span></span>'
            '<span class="cap">%d</span>'
            '<span class="dim">%d pop</span>'
            '<span class="dim">%d units</span>'
            '<span class="dim">%d mil</span>'
            '<span class="trace">%s</span></div>'
            % (" is-met" if met else "", HUE[f], f, cap, TARGET,
               min(100.0, cap / scale * 100), TARGET / scale * 100,
               cap, pop, un, s["mil"].get(f, 0), spark(s["series"][f])))

    state = "complete" if s["done"] else "running"
    cards.append(
        '<article class="card"><header class="cardhead">'
        '<h3>%s</h3><span class="state s-%s">%s</span>'
        '<span class="clock">%.0f min</span></header>%s</article>'
        % (html.escape(s["name"].split("_")[-1]), state, state,
           s["t"] / 60.0, "".join(rows_html)))

cause_html = "".join(
    '<li><span class="ck">%s</span><span class="cbar"><span style="width:%.0f%%"></span></span>'
    '<span class="cn">%d</span></li>' % (html.escape(k), c / cause_max * 100, c)
    for k, c in top_causes) or '<li class="empty">No refusals recorded yet.</li>'

doc = """<title>Waning Border Batch Watch</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500;600&family=IBM+Plex+Sans:wght@400;500;600&family=Spectral:wght@500;600&display=swap">
<style>
:root {
  --ground:#f8f6fb; --surface:#ffffff; --sunk:#f1eef6;
  --line:#e3dced; --ink:#191524; --muted:#6c6480;
  --accent:#7a52c4; --accent-soft:#efe7fb;
  --display:"Spectral",Georgia,serif;
  --ui:"IBM Plex Sans",system-ui,sans-serif;
  --data:"IBM Plex Mono",ui-monospace,monospace;
}
@media (prefers-color-scheme:dark) {
  :root:not([data-theme="light"]) {
    --ground:#131020; --surface:#1c1829; --sunk:#171327;
    --line:#2d2640; --ink:#ece8f6; --muted:#918aa8;
    --accent:#a98ae8; --accent-soft:#2a2140;
  }
}
:root[data-theme="dark"] {
  --ground:#131020; --surface:#1c1829; --sunk:#171327;
  --line:#2d2640; --ink:#ece8f6; --muted:#918aa8;
  --accent:#a98ae8; --accent-soft:#2a2140;
}
*{box-sizing:border-box}
body{margin:0;background:var(--ground);color:var(--ink);font-family:var(--ui);
     font-size:14px;line-height:1.5;-webkit-font-smoothing:antialiased}
.wrap{max-width:1180px;margin:0 auto;padding:32px 24px 64px}

.top{display:flex;flex-wrap:wrap;align-items:baseline;gap:12px 20px;
      padding-bottom:18px;border-bottom:1px solid var(--line)}
h1{font-family:var(--display);font-weight:600;font-size:27px;margin:0;
    letter-spacing:-.01em;text-wrap:balance}
.sub{color:var(--muted);font-size:13px}
.live{margin-left:auto;font-family:var(--data);font-size:11px;letter-spacing:.09em;
       text-transform:uppercase;color:var(--muted);display:flex;align-items:center;gap:7px}
.dot{width:7px;height:7px;border-radius:50%;background:var(--accent)}
@media (prefers-reduced-motion:no-preference){
  .dot{animation:pulse 2.4s ease-in-out infinite}
  @keyframes pulse{0%,100%{opacity:1}50%{opacity:.25}}
}

.stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(158px,1fr));
        gap:1px;background:var(--line);border:1px solid var(--line);
        border-radius:3px;overflow:hidden;margin:22px 0 30px}
.stat{background:var(--surface);padding:16px 18px}
.stat .k{font-family:var(--data);font-size:10.5px;letter-spacing:.1em;
          text-transform:uppercase;color:var(--muted)}
.stat .v{font-family:var(--data);font-size:29px;font-weight:500;
          font-variant-numeric:tabular-nums;margin-top:5px;letter-spacing:-.02em}
.stat .v small{font-size:14px;color:var(--muted);font-weight:400}
.stat.lead{background:var(--accent-soft)}
.stat.lead .v{color:var(--accent)}

h2{font-family:var(--display);font-weight:600;font-size:16px;margin:0 0 12px;
    padding-top:8px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(452px,1fr));gap:14px}
.card{background:var(--surface);border:1px solid var(--line);border-radius:3px;
       padding:14px 16px 12px}
.cardhead{display:flex;align-items:center;gap:10px;margin-bottom:11px}
.cardhead h3{font-family:var(--data);font-size:12px;font-weight:500;margin:0;
              letter-spacing:.03em;color:var(--muted)}
.state{font-family:var(--data);font-size:9.5px;letter-spacing:.1em;
        text-transform:uppercase;padding:2px 7px;border-radius:2px;
        border:1px solid var(--line);color:var(--muted)}
.s-complete{border-color:var(--accent);color:var(--accent)}
.clock{margin-left:auto;font-family:var(--data);font-size:11px;color:var(--muted);
        font-variant-numeric:tabular-nums}

.frow{display:grid;
       grid-template-columns:52px minmax(70px,1fr) 38px 58px 62px 44px 132px;
       align-items:center;gap:9px;padding:4px 0}
.fname{font-size:12px;font-weight:500;color:var(--hue)}
.track{position:relative;height:7px;background:var(--sunk);border-radius:1px;
        display:block}
.fill{position:absolute;inset:0 auto 0 0;background:var(--hue);opacity:.34;
       border-radius:1px}
.is-met .fill{opacity:.85}
.rule{position:absolute;top:-3px;bottom:-3px;width:1px;background:var(--ink);
       opacity:.45}
.cap{font-family:var(--data);font-size:13px;font-weight:600;color:var(--ink);
      font-variant-numeric:tabular-nums;text-align:right}
.is-met .cap{color:var(--hue)}
.dim{font-family:var(--data);font-size:11px;color:var(--muted);
      font-variant-numeric:tabular-nums;text-align:right}
.trace{color:var(--hue);opacity:.75;display:block}
.spark{width:132px;height:30px;display:block}
.sparkrule{stroke:var(--ink);stroke-width:1;opacity:.2;stroke-dasharray:2 3}

.panels{display:grid;grid-template-columns:repeat(auto-fit,minmax(330px,1fr));
         gap:14px;margin-top:30px}
.panel{background:var(--surface);border:1px solid var(--line);border-radius:3px;
        padding:15px 17px}
.panel ul{list-style:none;margin:0;padding:0;display:grid;gap:8px}
.panel li{display:grid;grid-template-columns:1fr 96px 34px;align-items:center;gap:10px}
.ck{font-family:var(--data);font-size:11.5px}
.cbar{height:5px;background:var(--sunk);border-radius:1px;display:block}
.cbar span{display:block;height:100%;background:var(--accent);opacity:.6;border-radius:1px}
.cn{font-family:var(--data);font-size:11.5px;color:var(--muted);text-align:right;
     font-variant-numeric:tabular-nums}
.empty{display:block;color:var(--muted);font-size:12.5px}
.note{color:var(--muted);font-size:12.5px;margin:9px 0 0;max-width:62ch}
footer{margin-top:34px;padding-top:15px;border-top:1px solid var(--line);
        color:var(--muted);font-size:11.5px;font-family:var(--data)}
@media (max-width:600px){
  .frow{grid-template-columns:48px 1fr 36px 52px}
  .frow .dim:nth-of-type(2),.frow .dim:nth-of-type(3),.trace{display:none}
}
</style>

<div class="wrap">
  <header class="top">
    <h1>Batch Watch</h1>
    <span class="sub">4-AI skirmish &middot; __MINS__-minute matches &middot; 3&times; speed</span>
    <span class="live"><span class="dot"></span>updated __STAMP__</span>
  </header>

  <section class="stats">
    <div class="stat lead">
      <div class="k">reached cap __TARGET__</div>
      <div class="v">__PCT__<small>%</small></div>
    </div>
    <div class="stat"><div class="k">factions</div>
      <div class="v">__HIT__<small> / __SEEN__</small></div></div>
    <div class="stat"><div class="k">matches</div>
      <div class="v">__DONE__<small> / __NSESS__</small></div></div>
    <div class="stat"><div class="k">median peak cap</div>
      <div class="v">__MEDCAP__</div></div>
    <div class="stat"><div class="k">median final army</div>
      <div class="v">__MEDUNITS__</div></div>
    <div class="stat"><div class="k">military buildings</div>
      <div class="v">__MILTOTAL__<small> &middot; __MILNONE__ with none</small></div></div>
  </section>

  <h2>Per match</h2>
  <p class="note">Bar is the highest population cap the faction ever held; the
  vertical rule marks __TARGET__. A faction wiped out late still counts as
  having reached its cap. The trace shows cap across the match, dashed line at
  __TARGET__.</p>
  <div class="grid">__CARDS__</div>

  <div class="panels">
    <section class="panel">
      <h2>Why the AI refused to build</h2>
      <ul>__CAUSES__</ul>
    </section>
    <section class="panel">
      <h2>Reading this</h2>
      <p class="note">Every faction crossing __TARGET__ is the acceptance bar. A
      run where one faction lags is usually an early elimination, which the
      trace shows as a cap that climbs and then falls away. A run where
      <em>all</em> of them lag points back at the economy.</p>
      <p class="note">Military buildings counts Barracks, Archery Ranges, Royal
      Stables and Siege Yards. A faction finishing with none is a production
      failure, not a balance one.</p>
    </section>
  </div>

  <footer>__LOGS__ &middot; regenerated every minute while the batch runs</footer>
</div>
"""

for k, v in [
    ("__MINS__", str(LIMIT // 60)), ("__STAMP__", time.strftime("%H:%M:%S")),
    ("__TARGET__", str(TARGET)), ("__PCT__", "%.0f" % pct),
    ("__HIT__", str(hit)), ("__SEEN__", str(seen)),
    ("__DONE__", str(done_n)), ("__NSESS__", str(len(sessions))),
    ("__MEDCAP__", str(med(caps))), ("__MEDUNITS__", str(med(units))),
    ("__MILTOTAL__", str(mil_total)), ("__MILNONE__", str(mil_none)),
    ("__CARDS__", "".join(cards)), ("__CAUSES__", cause_html),
    ("__LOGS__", html.escape(LOGS)),
]:
    doc = doc.replace(k, v)

with open(OUT, "w", encoding="utf-8") as f:
    f.write(doc)
print("%d sessions | %d/%d at cap %d (%.0f%%) | %d complete"
      % (len(sessions), hit, seen, TARGET, pct, done_n))
