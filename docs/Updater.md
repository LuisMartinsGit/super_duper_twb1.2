# The updater

**Status:** canonical for how alpha builds reach testers.

Testers run `TWBLauncher.exe`. It checks a version endpoint, downloads and
installs a new build if there is one, and starts the game. The build lives as a
release asset on a **private** GitHub repo, so nothing is publicly downloadable
and access is revocable per tester.

---

## 1. Why it is shaped this way

Two constraints drive the whole design.

**The game cannot update itself.** Windows locks a running executable and its
loaded DLLs, so a self-updating game can never overwrite its own files. Hence a
separate launcher that lives *outside* the folder it replaces.

**The GitHub token can never ship.** A credential inside an exe is recoverable
in seconds, and a token that reads the private releases repo is one
misconfiguration away from reading the source repo. So the launcher never holds
it. A Cloudflare Worker does, and hands out short-lived signed URLs instead:

```
Launcher --X-TWB-Key--> Worker --PAT--> GitHub API (private repo)
                          |
                          +-- 302 --> short-lived signed CDN URL
Launcher ---------------------------------> downloads direct from GitHub CDN
```

The zip never passes through the Worker, so build size is irrelevant to
hosting cost and the free tier is never in play.

Per-tester keys are not DRM. A tester can extract their own key and pass it on.
What they buy is **attribution** (you know whose key leaked) and **revocation**
(one KV delete, nobody else affected).

---

## 2. One-time setup

### The live instance

Deployed 2026-08-18. Recorded here so nobody has to go digging in the
Cloudflare dashboard to answer "where does this actually run".

| | |
|---|---|
| Endpoint | `https://twb-updates.luis-resmart.workers.dev` |
| Worker | `twb-updates` |
| KV namespace | `twb-updates-KEYS`, id `fa8c1b5913494faba25c936fc2156359` |
| Account | `luis.resmart@gmail.com` |
| Releases repo | `Ahridan/TWB-Releases` (private) |
| Verified | `/health` 200, auth gate 401, `/version` reaches the repo |

The rest of this section is what was done to get there, kept for when it needs
rebuilding.

### The releases repo

Create a **public-in-name-only** second repo, `TWB-Releases` — private, empty,
no code. It exists purely to hold release assets. Release assets do not count
against repository size, and unlike Git LFS they are not metered against a
bandwidth quota. The hard limit that matters is **2 GiB per individual file**;
[release.ps1](../tools/release.ps1) refuses a build over it rather than failing
four minutes into an upload.

### The Worker credential

Create a **fine-grained** PAT — not a classic one:

- Repository access: **Only select repositories** -> `TWB-Releases`
- Permissions: **Contents: Read-only**

That token can read release assets in one empty repo and nothing else. Note its
expiry date somewhere you will see it: when it lapses, every launcher fails at
once.

The Worker reports upstream GitHub failures distinctly rather than collapsing
them into one message, because in practice they are hard to tell apart:

| Worker says | Means |
|---|---|
| `credential is invalid (GitHub 401)` | The token value is wrong, truncated or revoked |
| `credential lacks access (GitHub 403)` | Token is real but not permitted |
| `Cannot reach <repo> (GitHub 404)` | Wrong repo name, or not selected on the token |
| `No published release yet.` | Everything is wired correctly, nothing shipped |

The 404 case needs the extra probe the Worker does: GitHub deliberately hides
repos a token cannot see behind the same 404 as a repo with no releases, so
"not shipped yet" and "misconfigured" are otherwise indistinguishable.

### Deploying the Worker

```bash
cd tools/UpdateWorker
npm install
npx wrangler kv namespace create KEYS     # paste the id into wrangler.toml
npx wrangler secret put GITHUB_TOKEN      # the fine-grained read-only PAT
npx wrangler deploy
```

Deploy prints the URL. Put it in
[LauncherSettings.cs](../tools/Launcher/LauncherSettings.cs) as
`DefaultApiBase`. The free `workers.dev` subdomain is fine; no custom domain
needed.

Check it is alive — `/health` needs no key, which is what lets you tell "server
down" apart from "key rejected":

```bash
curl https://twb-updates.luis-resmart.workers.dev/health
```

### Provisioning testers

One key each, so they can be revoked individually. In PowerShell, from
`tools/UpdateWorker`:

```powershell
# issue - prints the key to send
$key = [guid]::NewGuid().ToString()
npx wrangler kv key put --binding=KEYS "key:$key" "alice"
$key

# list
npx wrangler kv key list --binding=KEYS

# revoke
echo y | npx wrangler kv key delete --binding=KEYS "key:<the-uuid>"
```

Wrangler 3 writes to the real namespace by default; `--local` is the opt-out.
Do **not** add `--remote` — that flag only exists in Wrangler 4 and errors out
here. Delete prompts for confirmation, hence the piped `y`.

The value (`alice`) is a label for your own benefit only. Send each tester
their uuid; they paste it once on first run and it is stored in `%APPDATA%`.

---

## 3. Shipping a build

1. Bump **bundleVersion** in Unity Player Settings.
2. Build the player.
3. Publish:

```powershell
.\tools\release.ps1 -BuildPath 'D:\Builds\TWB' -Notes 'Multiplayer desync fixes'
```

`release.ps1` zips the build, hashes it, writes `manifest.json`, creates the
release, and uploads both assets. It needs `GH_RELEASE_TOKEN` in `.env` — a
**separate, read-write** token from the one the Worker holds.

There is no `-Version` argument in normal use: the script reads `bundleVersion`
straight from `ProjectSettings.asset`. That is the single source of truth
already used by the lobby handshake in
[MatchSettingsSync.cs:99](../Assets/Scripts/Core/Multiplayer/MatchSettingsSync.cs#L99)
and stamped into every match log by
[MatchLogSession.cs:77](../Assets/Scripts/Core/Diagnostics/MatchLogSession.cs#L77),
so a manifest can never claim a version the build does not report. Passing
`-Version` anyway is allowed but refuses to run if the two disagree.

Forgetting step 1 is therefore not silent: the release fails with "a release
for v0.0.8 already exists" rather than shipping a mislabelled build.

**Releasing does not update the launcher.** The launcher cannot replace itself
any more than the game can, so a change to `TWBLauncher.exe` has to be handed
to testers by hand. Keep launcher changes rare and batch them.

---

## 4. What a tester gets

A folder containing exactly one file, `TWBLauncher.exe`. Everything else the
launcher creates on first run:

```
The Waning Border\
  TWBLauncher.exe     the launcher, never touched by an update
  version.txt         the installed version
  game\               everything an update replaces
    The Waning Border.exe
    logs\             match logs, carried across updates
  game.old\           the previous build, kept for rollback
```

`game.old` is deliberate. A bad patch is recoverable by deleting `game` and
renaming `game.old` back — no re-download. It is cleared at the start of the
*next* update, so peak disk is two copies of the build, not three.

The `logs` folder is moved forward across the swap. Losing it would defeat the
point of the alpha build.

---

## 5. Behaviour worth knowing

| Situation | What happens |
|---|---|
| Up to date | Launches immediately; the window is on screen for about a second |
| Update available | Downloads, verifies the sha256, installs, launches |
| Checksum mismatch | Deletes the download and reports corruption — nothing is installed |
| Server unreachable | **Fails open**: starts the installed build after a 2.5 s notice |
| Key rejected | Re-prompts once with the reason, then quits if cancelled |
| Update fails mid-way | Old build is restored, and Play stays available |
| Nothing installed yet | Hard error — there is nothing to fall back to |

Failing open is the important one. An outage on your side must never stop a
tester from playing the build they already have. Only a rejected key hard-stops,
because that one is actionable.

---

## 6. Code map

| Path | Role |
|---|---|
| [tools/UpdateWorker/src/index.js](../tools/UpdateWorker/src/index.js) | The broker: key check, GitHub lookup, signed redirect |
| [tools/Launcher/UpdateClient.cs](../tools/Launcher/UpdateClient.cs) | HTTP, streaming download, SHA-256 verification |
| [tools/Launcher/Installer.cs](../tools/Launcher/Installer.cs) | Extraction, zip-slip guard, the folder swap |
| [tools/Launcher/MainForm.cs](../tools/Launcher/MainForm.cs) | The window and the check/download/install/launch flow |
| [tools/Launcher/AppPaths.cs](../tools/Launcher/AppPaths.cs) | The on-disk layout |
| [tools/release.ps1](../tools/release.ps1) | Package and publish a release |

Two details in the code are easy to "clean up" and must not be:

- The launcher sends its key as **`X-TWB-Key`, not `Authorization`**.
  `HttpClient` forwards `Authorization` across redirects, and `/download`
  redirects to a pre-signed CDN URL that rejects a second credential.
- The Worker fetches the asset with **`redirect: "manual"`** for the same
  reason from the other side — it must catch the 302 rather than follow it
  with the PAT attached.
