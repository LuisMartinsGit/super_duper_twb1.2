#!/usr/bin/env python3
"""
Diff two peers' Desync_*_trace.log files the way a person has to, so a person
no longer has to.

    python tools/mp-trace-diff.py <host_trace.log> <client_trace.log> [types_a types_b]

WHY THIS EXISTS (2026-09-13). The per-entity trace is a ring of the last 120
ticks, and each peer flushes its own ring when IT detects the fork -- so the
two files start a few ticks apart and a plain `diff` reports the whole first
block as missing. Three forks in one night were each diagnosed by hand with
the same three steps: find the shared tick range, walk it oldest-first for the
first tick whose entity lines differ, then print the differing entity on both
sides. The archetype count (`arch=`) and, when the *_types.log rosters are
given, the exact component name come out of the same pass.

Exit 0 = no divergence found in the shared range (suspicious: the fork was
older than the ring), 42 = divergence found and named.
"""
import re
import sys

TICK = re.compile(r"tick=(\d+) ")
ENT = re.compile(r"^e\s+tick=(\d+) id=(\S+) (.*)$")
FIELD = re.compile(r"(\w+)=(\S+)")


def load(path):
    """tick -> {id -> line}"""
    ticks = {}
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = ENT.match(line)
            if not m:
                continue
            ticks.setdefault(int(m.group(1)), {})[m.group(2)] = m.group(3).rstrip()
    return ticks


def fields(line):
    return dict(FIELD.findall(line))


def load_types(path):
    out = {}
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if line.startswith("t "):
                parts = line.split()
                out[parts[1]] = set(parts[2:])
    return out


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    a, b = load(sys.argv[1]), load(sys.argv[2])
    shared = sorted(set(a) & set(b))
    if not shared:
        print("no shared ticks: A %s..%s, B %s..%s"
              % (min(a), max(a), min(b), max(b)))
        return 2
    print("shared range: %d..%d (%d ticks)" % (shared[0], shared[-1], len(shared)))

    for t in shared:
        ea, eb = a[t], b[t]
        ids = sorted(set(ea) | set(eb))
        for i in ids:
            la, lb = ea.get(i), eb.get(i)
            if la == lb:
                continue
            print()
            print("FIRST DIVERGENCE at tick %d, entity %s" % (t, i))
            if la is None or lb is None:
                print("  present on only one peer: %s" % ("A" if la else "B"))
                return 42
            fa, fb = fields(la), fields(lb)
            for k in fa:
                if fa.get(k) != fb.get(k):
                    print("  %-6s A=%s  B=%s" % (k, fa.get(k), fb.get(k)))
            # one tick earlier, for the reader: was it already different?
            prev = [x for x in shared if x < t]
            if prev:
                pt = prev[-1]
                same = a[pt].get(i) == b[pt].get(i)
                print("  tick %d (one earlier): %s" % (pt, "IDENTICAL" if same else "already different"))
            if len(sys.argv) >= 5:
                ta, tb = load_types(sys.argv[3]), load_types(sys.argv[4])
                sa, sb = ta.get(i, set()), tb.get(i, set())
                if sa or sb:
                    only_a, only_b = sorted(sa - sb), sorted(sb - sa)
                    print("  components only on A: %s" % (", ".join(only_a) or "-"))
                    print("  components only on B: %s" % (", ".join(only_b) or "-"))
                    if only_a or only_b:
                        print("  ^^^ that is the component that forked the sim")
            return 42
    print("no per-entity divergence inside the shared range; the fork is older than the ring")
    return 0


if __name__ == "__main__":
    sys.exit(main())
