#!/usr/bin/env python3
"""Record what every past release shipped, for the launcher's old-install cleanup.

    python tools/gen-shipped-index.py

Reads manifest.json from each release of Ahridan/TWB-Releases (token: GH_RELEASE_TOKEN
in .env) and writes tools/Launcher/ShippedFiles.txt.gz, one "sha256 size path" per line,
which the launcher embeds. ShippedFileCleanup deletes only files found in that record,
so run this before tools/Launcher/publish.ps1 when releasing.
"""
import gzip
import json
import os
import re
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = "Ahridan/TWB-Releases"


def token():
    if os.environ.get("GH_RELEASE_TOKEN"):
        return os.environ["GH_RELEASE_TOKEN"]
    for line in open(os.path.join(ROOT, ".env"), encoding="utf-8"):
        m = re.match(r"\s*GH_RELEASE_TOKEN=(.*)", line)
        if m:
            return m.group(1).strip().strip('"')
    raise SystemExit("No GH_RELEASE_TOKEN")


def get(url, accept="application/vnd.github+json"):
    req = urllib.request.Request(url, headers={
        "Authorization": "Bearer " + token(), "Accept": accept, "User-Agent": "twb-shipped-index"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


records, with_files, without = set(), 0, 0
page = 1
while True:
    releases = json.loads(get(f"https://api.github.com/repos/{REPO}/releases?per_page=100&page={page}"))
    if not releases:
        break
    for rel in releases:
        asset = next((a for a in rel.get("assets", []) if a["name"] == "manifest.json"), None)
        if asset is None:
            without += 1
            continue
        manifest = json.loads(get(asset["url"], "application/octet-stream").decode("utf-8-sig"))
        files = manifest.get("files") or []
        if not files:
            without += 1
            continue
        with_files += 1
        for f in files:
            records.add((f["sha256"].lower(), int(f["size"]), f["path"].replace("\\", "/")))
    page += 1

if not records:
    raise SystemExit("No manifest listed any file: refusing to write an empty record")

out = os.path.join(ROOT, "tools", "Launcher", "ShippedFiles.txt.gz")
text = "".join(f"{s} {n} {p}\n" for s, n, p in sorted(records))
with open(out, "wb") as raw, gzip.GzipFile(fileobj=raw, mode="wb", mtime=0) as gz:
    gz.write(text.encode("utf-8"))
print(f"{with_files} releases with file lists, {without} without; {len(records)} records, "
      f"{os.path.getsize(out) // 1024} KB -> {out}")
