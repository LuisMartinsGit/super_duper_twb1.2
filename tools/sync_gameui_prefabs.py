#!/usr/bin/env python3
"""Sync GameUI staging-scene layout into the panel prefabs.

The in-game UI is built by GameUIManager from the prefabs under
Assets/GameData/Scenes/Menus/GameUI — but panels are positioned in the
GameUI.unity staging scene, where edits are stored as PREFAB-INSTANCE
OVERRIDES that the runtime never sees. Run this after moving/resizing
panels in the scene:

  python tools/sync_gameui_prefabs.py

Every rect-transform override on a scene instance of a GameUI prefab is
written back into the prefab file (the layout half of Unity's
"Apply All"). The script also prints the layout-container rects that
GameUIManager mirrors at runtime (BottonLeft, TopCenter, ...) — if one
changed, update the matching Dock constants in
Assets/Scripts/UI/GameUI/GameUIManager.cs.

Run with the Unity editor closed, or let it reload assets afterwards.
"""

import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAMEUI = os.path.join(ROOT, "Assets", "GameData", "Scenes", "Menus", "GameUI")
SCENE = os.path.join(GAMEUI, "GameUI.unity")

# Layout containers whose rects GameUIManager mirrors in code.
MIRRORED_CONTAINERS = ("BottonLeft", "TopCenter", "TopLeft", "TopRight",
                       "BottomRight", "CenterRight", "Center")

ALLOWED = re.compile(
    r"^m_(AnchorMin|AnchorMax|AnchoredPosition|SizeDelta|Pivot"
    r"|LocalScale|LocalPosition|LocalRotation)\.([xyzw])$")


def prefab_guids():
    """guid -> prefab path for every prefab under the GameUI folder."""
    out = {}
    for path in glob.glob(os.path.join(GAMEUI, "**", "*.prefab"), recursive=True):
        with open(path + ".meta", encoding="utf-8") as f:
            m = re.search(r"guid: ([0-9a-f]+)", f.read())
        if m:
            out[m.group(1)] = path
    return out


def main():
    guids = prefab_guids()
    with open(SCENE, encoding="utf-8") as f:
        scene = f.read()
    docs = re.split(r"--- !u!(\d+) &(\d+)\n", scene)

    # Collect rect-transform overrides per prefab: {guid: {fileID: {prop: val}}}
    mods = {}
    i = 1
    while i < len(docs) - 2:
        cls, body = docs[i], docs[i + 2]
        if cls == "1001":
            src = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]+)", body)
            g = src.group(1) if src else "?"
            if g in guids:
                for m in re.finditer(
                        r"- target: \{fileID: (\d+), guid: [0-9a-f]+, type: 3\}"
                        r"\n\s+propertyPath: ([\w.\[\]]+)\n\s+value: ([^\n]*)", body):
                    tgt, pp, val = int(m.group(1)), m.group(2), m.group(3).strip()
                    if ALLOWED.match(pp) and val != "":
                        mods.setdefault(g, {}).setdefault(tgt, {})[pp] = val
        i += 3

    for g, per_target in mods.items():
        path = guids[g]
        with open(path, encoding="utf-8") as f:
            text = f.read()
        parts = re.split(r"(--- !u!\d+ &\d+\n)", text)
        patched = missing = 0
        for tgt, props in per_target.items():
            found = False
            for j in range(1, len(parts), 2):
                hm = re.match(r"--- !u!(\d+) &(\d+)\n", parts[j])
                if not hm or int(hm.group(2)) != tgt or hm.group(1) not in ("4", "224"):
                    continue
                body = parts[j + 1]
                for pp, val in props.items():
                    field, comp = pp.rsplit(".", 1)

                    def rep(mo, comp=comp, val=val):
                        inner, _ = re.subn(comp + r": [\-\d.e+]+",
                                           f"{comp}: {val}", mo.group(2), count=1)
                        return mo.group(1) + inner + mo.group(3)

                    body, n = re.subn(r"(  " + field + r": \{)([^}]*)(\})",
                                      rep, body, count=1)
                    patched += n
                parts[j + 1] = body
                found = True
                break
            if not found:
                missing += 1
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("".join(parts))
        print(f"{os.path.relpath(path, ROOT)}: applied {patched} overrides"
              + (f", {missing} targets NOT found" if missing else ""))

    # Print the container rects GameUIManager mirrors.
    print("\nLayout containers (mirror these in GameUIManager if changed):")
    objs, trans = {}, []
    i = 1
    while i < len(docs) - 2:
        cls, body = docs[i], docs[i + 2]
        if cls == "1":
            m = re.search(r"m_Name: (.*)", body)
            objs[int(docs[i + 1])] = m.group(1).strip() if m else ""
        elif cls in ("4", "224"):
            trans.append(body)
        i += 3
    for body in trans:
        go = re.search(r"m_GameObject: \{fileID: (\d+)\}", body)
        name = objs.get(int(go.group(1)) if go else 0, "")
        if name in MIRRORED_CONTAINERS:
            vals = []
            for k in ("m_AnchorMin", "m_AnchorMax", "m_AnchoredPosition",
                      "m_SizeDelta", "m_LocalScale"):
                m = re.search(k + r": \{[^}]*\}", body)
                if m:
                    vals.append(m.group(0))
            print(f"  {name}: " + "  ".join(vals))


if __name__ == "__main__":
    sys.exit(main())
