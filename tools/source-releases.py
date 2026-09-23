#!/usr/bin/env python3
"""
Create a GitHub Release on the SOURCE repo for every version tag that has none.

    python tools/source-releases.py            # dry run: say what would be made
    python tools/source-releases.py --apply    # create them

WHY (2026-09-23). Builds are published as releases on Ahridan/TWB-Releases,
which is what the launcher reads; the source repo had no tags and no releases,
so nothing tied "0.0.23 4184a27d" in a tester's log to a commit. The sixteen
version commits were tagged v0.0.9..v0.0.24 that day, and tools/release.ps1
now tags every new build. This script gives each source tag a release whose
body is that version's CHANGELOG.md section, so the version history reads on
GitHub the way it reads in the file. No binaries: those stay on TWB-Releases.

Needs a token with contents:write on the source repo. It reads GH_TOKEN from
.env (the same token the CLAUDE.md agent pipeline uses) or the environment.
The tags must already be on origin: a release for a tag GitHub does not have
would create a lightweight tag at the default branch, which is wrong, so this
refuses to create one for a tag it cannot see remotely.
"""
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = "LuisMartinsGit/super_duper_twb1.2"
BUILD_REPO = "Ahridan/TWB-Releases"
API = "https://api.github.com"


def token():
    t = os.environ.get("GH_TOKEN")
    if t:
        return t
    env = os.path.join(ROOT, ".env")
    if os.path.exists(env):
        for line in open(env, encoding="utf-8"):
            if line.startswith("GH_TOKEN="):
                return line.split("=", 1)[1].strip()
    sys.exit("No GH_TOKEN in the environment or .env.")


def gh(method, path, tok, body=None):
    req = urllib.request.Request(
        API + path,
        method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers={
            "Authorization": f"Bearer {tok}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "Content-Type": "application/json",
            "User-Agent": "twb-source-releases",
        },
    )
    try:
        with urllib.request.urlopen(req) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        sys.exit(f"{method} {path} -> {e.code}: {e.read().decode(errors='replace')[:300]}")


def changelog_sections():
    """version -> (date, body) from CHANGELOG.md's '## [x.y.z] — date' headings."""
    text = open(os.path.join(ROOT, "CHANGELOG.md"), encoding="utf-8-sig").read()
    heads = list(re.finditer(r"^## \[([^\]]+)\](?: — (\S+))?\s*$", text, re.M))
    out = {}
    for i, h in enumerate(heads):
        end = heads[i + 1].start() if i + 1 < len(heads) else len(text)
        body = text[h.end():end].strip()
        body = re.sub(r"\n---\s*$", "", body).strip()
        out[h.group(1)] = (h.group(2), body)
    return out


def local_tags():
    out = subprocess.run(["git", "-C", ROOT, "tag", "-l", "v*"], capture_output=True, text=True).stdout.split()
    return sorted(out, key=lambda t: [int(p) for p in t[1:].split(".")])


def main():
    apply = "--apply" in sys.argv
    tok = token()
    sections = changelog_sections()
    remote_tags = {t["ref"].split("/")[-1] for t in gh("GET", f"/repos/{REPO}/git/matching-refs/tags/v", tok)}
    existing = {r["tag_name"] for r in gh("GET", f"/repos/{REPO}/releases?per_page=100", tok)}

    for tag in local_tags():
        ver = tag[1:]
        if tag in existing:
            print(f"  {tag}: release exists, skipped")
            continue
        if tag not in remote_tags:
            print(f"  {tag}: NOT on origin, skipped - push the tag first (git push origin {tag})")
            continue
        date, body = sections.get(ver, (None, None))
        if body is None:
            sha = subprocess.run(["git", "-C", ROOT, "log", "-1", "--format=%s%n%n%b", tag], capture_output=True, text=True).stdout.strip()
            body = f"No CHANGELOG section for {ver}; the version commit says:\n\n{sha}"
        body += f"\n\n---\nBuild: https://github.com/{BUILD_REPO}/releases/tag/{tag}"
        name = f"The Waning Border {ver}" + (f" ({date})" if date else "")
        if not apply:
            print(f"  {tag}: would create '{name}' ({len(body)} chars of notes)")
            continue
        gh("POST", f"/repos/{REPO}/releases", tok, {
            "tag_name": tag, "name": name, "body": body,
            "draft": False, "prerelease": ver.startswith("0."),
        })
        print(f"  {tag}: created '{name}'")

    if not apply:
        print("\nDry run. Re-run with --apply to create them.")


if __name__ == "__main__":
    main()
