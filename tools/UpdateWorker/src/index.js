/**
 * The Waning Border - update broker and log sink.
 *
 * This Worker holds the only credential that can read the private release
 * repo. Testers authenticate with a per-tester key stored in KV, so revoking
 * one tester is a single KV delete and affects nobody else. The same key
 * identifies uploaded match logs, so attribution is free.
 *
 * The build itself never passes through here. /download asks GitHub for the
 * asset, catches the 302 to GitHub's short-lived signed CDN URL, and hands
 * that redirect to the launcher - so the zip goes CDN -> tester directly and
 * Worker bandwidth stays at zero regardless of build size.
 */

const GH_API = "https://api.github.com";

/** Refuse anything larger rather than stream a pathological Console.log. */
const MAX_LOG_BYTES = 25 * 1024 * 1024;

/** Per-tester daily upload ceiling, so a retry loop cannot fill the bucket. */
const MAX_UPLOADS_PER_DAY = 50;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // Unauthenticated, so you can tell "Worker is down" from "key is bad".
    if (url.pathname === "/health") return json({ ok: true });

    // Admin routes use a different credential from testers on purpose: a
    // tester key can post logs but must never be able to read anyone else's.
    if (url.pathname === "/logs" && request.method === "GET") return listLogs(request, env, url);
    if (url.pathname.startsWith("/logs/") && request.method === "GET") return fetchLog(request, env, url);

    const key = request.headers.get("x-twb-key") ?? url.searchParams.get("key");
    const tester = key ? await env.KEYS.get(`key:${key}`) : null;
    if (!tester) {
      return json({ error: "Unknown or revoked key. Ask Luis for a new one." }, 401);
    }

    if (url.pathname === "/logs" && request.method === "POST") {
      return uploadLog(request, env, tester);
    }

    let release;
    try {
      release = await latestRelease(env);
    } catch (err) {
      return json({ error: err.message }, err.status ?? 502);
    }

    if (url.pathname === "/version") return versionResponse(release, env);
    if (url.pathname === "/download") return downloadResponse(release, env);
    return json({ error: "Not found" }, 404);
  },
};

/* ------------------------------------------------------------------ logs */

/**
 * Accepts one zipped match-log folder.
 *
 * Metadata rides in a header rather than being read out of the archive: a
 * Worker cannot unzip, and the client already knows every field from
 * Summary.txt.
 *
 * Idempotent by object key. The game uploads at match end and the launcher
 * sweeps leftovers on next start, so the same match legitimately arrives
 * twice; the second one must not duplicate the object or re-post to Discord.
 */
async function uploadLog(request, env, tester) {
  if (!env.LOGS) return json({ error: "Log storage is not configured." }, 503);

  let meta;
  try {
    meta = JSON.parse(atob(request.headers.get("x-twb-meta") ?? ""));
  } catch {
    return json({ error: "Missing or malformed X-TWB-Meta header." }, 400);
  }

  const match = sanitise(meta.match);
  if (!match) return json({ error: "Metadata has no match name." }, 400);

  const declared = Number(request.headers.get("content-length") ?? 0);
  if (declared > MAX_LOG_BYTES) {
    return json({ error: `Log is larger than ${MAX_LOG_BYTES} bytes.` }, 413);
  }

  const objectKey = `logs/${sanitise(tester)}/${match}.zip`;

  // Already have it: report success so the client marks it done and stops
  // retrying, but do not re-announce it.
  if (await env.LOGS.head(objectKey)) {
    return json({ ok: true, duplicate: true, key: objectKey });
  }

  const quotaKey = `uploads:${tester}:${new Date().toISOString().slice(0, 10)}`;
  const used = Number((await env.KEYS.get(quotaKey)) ?? 0);
  if (used >= MAX_UPLOADS_PER_DAY) {
    return json({ error: "Daily upload limit reached." }, 429);
  }

  const body = await request.arrayBuffer();
  if (body.byteLength === 0) return json({ error: "Empty upload." }, 400);
  if (body.byteLength > MAX_LOG_BYTES) {
    return json({ error: `Log is larger than ${MAX_LOG_BYTES} bytes.` }, 413);
  }

  await env.LOGS.put(objectKey, body, {
    httpMetadata: { contentType: "application/zip" },
    customMetadata: {
      tester,
      match,
      map: String(meta.map ?? ""),
      mode: String(meta.mode ?? ""),
      version: String(meta.version ?? ""),
      fingerprint: String(meta.fingerprint ?? ""),
      outcome: String(meta.outcome ?? ""),
      duration: String(meta.duration ?? ""),
      exceptions: String(meta.exceptions ?? 0),
      errors: String(meta.errors ?? 0),
      warnings: String(meta.warnings ?? 0),
      uploadedAt: new Date().toISOString(),
    },
  });

  // 36h TTL: comfortably outlives the calendar day the key is named for,
  // without accumulating dead counters.
  await env.KEYS.put(quotaKey, String(used + 1), { expirationTtl: 60 * 60 * 36 });

  // Never let a Discord outage fail an upload that already landed.
  try {
    await announce(env, tester, meta, body.byteLength);
  } catch (err) {
    console.log("Discord announce failed:", err.message);
  }

  return json({ ok: true, key: objectKey, bytes: body.byteLength });
}

async function announce(env, tester, meta, bytes) {
  if (!env.DISCORD_LOG_WEBHOOK) return;

  const exceptions = Number(meta.exceptions ?? 0);
  const errors = Number(meta.errors ?? 0);

  // Colour carries the triage signal, so a bad match is visible without
  // reading the line.
  const colour = exceptions > 0 ? 0xd6766a : errors > 0 ? 0xc4a25c : 0x5c9c6b;

  const fields = [
    { name: "Build", value: `${meta.version ?? "?"} \`${meta.fingerprint ?? "?"}\``, inline: true },
    { name: "Map", value: String(meta.map || "unknown"), inline: true },
    { name: "Mode", value: String(meta.mode || "unknown"), inline: true },
    { name: "Outcome", value: String(meta.outcome || "unfinished"), inline: true },
    { name: "Duration", value: String(meta.duration || "?"), inline: true },
    { name: "Size", value: `${(bytes / 1024).toFixed(0)} KB`, inline: true },
    {
      name: "Counts",
      value: `exceptions **${exceptions}** · errors **${errors}** · warnings ${meta.warnings ?? 0}`,
    },
  ];

  await fetch(env.DISCORD_LOG_WEBHOOK, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      embeds: [{ title: `${tester} - ${meta.match}`, color: colour, fields }],
    }),
  });
}

async function listLogs(request, env, url) {
  if (!isAdmin(request, env)) return json({ error: "Admin key required." }, 401);
  if (!env.LOGS) return json({ error: "Log storage is not configured." }, 503);

  const prefix = url.searchParams.get("tester")
    ? `logs/${sanitise(url.searchParams.get("tester"))}/`
    : "logs/";

  const listed = await env.LOGS.list({ prefix, limit: 200, include: ["customMetadata"] });

  return json({
    count: listed.objects.length,
    truncated: listed.truncated,
    objects: listed.objects.map((o) => ({
      key: o.key,
      size: o.size,
      uploaded: o.uploaded,
      ...o.customMetadata,
    })),
  });
}

async function fetchLog(request, env, url) {
  if (!isAdmin(request, env)) return json({ error: "Admin key required." }, 401);
  if (!env.LOGS) return json({ error: "Log storage is not configured." }, 503);

  const object = await env.LOGS.get(decodeURIComponent(url.pathname.slice(1)));
  if (!object) return json({ error: "Not found" }, 404);

  return new Response(object.body, {
    headers: { "content-type": "application/zip" },
  });
}

function isAdmin(request, env) {
  const given = request.headers.get("x-twb-admin");
  return Boolean(env.ADMIN_KEY) && given === env.ADMIN_KEY;
}

/** Keeps a hostile or accidental name from escaping its prefix in R2. */
function sanitise(value) {
  return String(value ?? "")
    .replace(/[^A-Za-z0-9._-]/g, "_")
    .replace(/^\.+/, "")
    .slice(0, 120);
}

/* --------------------------------------------------------------- updates */

function ghHeaders(env) {
  return {
    "Authorization": `Bearer ${env.GITHUB_TOKEN}`,
    "Accept": "application/vnd.github+json",
    "User-Agent": "twb-update-broker",
    "X-GitHub-Api-Version": "2022-11-28",
  };
}

async function latestRelease(env) {
  const res = await fetch(`${GH_API}/repos/${env.RELEASE_REPO}/releases/latest`, {
    headers: ghHeaders(env),
  });

  if (res.ok) return res.json();

  // These are split apart because a silently expired fine-grained PAT is
  // otherwise indistinguishable from a network fault, and it WILL expire.
  // The upstream status is quoted verbatim: it leaks nothing, and without it
  // every failure here looks identical from the tester side.
  if (res.status === 401) throw fail("Update server credential is invalid (GitHub 401).", 503);
  if (res.status === 403) throw fail("Update server credential lacks access (GitHub 403).", 503);

  // 404 is ambiguous on purpose at GitHub: a repo the token cannot see is
  // hidden behind the same status as a repo with no releases. Probing the
  // repo itself is the only way to tell "not shipped yet" from "misconfigured".
  if (res.status === 404) {
    const probe = await fetch(`${GH_API}/repos/${env.RELEASE_REPO}`, { headers: ghHeaders(env) });

    if (probe.ok) throw fail("No published release yet.", 503);

    throw fail(
      `Cannot reach ${env.RELEASE_REPO} (GitHub ${probe.status}). ` +
      "Check the repo name and that the token grants it Contents: Read.", 503);
  }

  throw fail(`GitHub returned ${res.status}.`, 502);
}

function assets(release) {
  const zip = release.assets?.find((a) => a.name.endsWith(".zip"));
  const manifest = release.assets?.find((a) => a.name === "manifest.json");
  if (!zip || !manifest) {
    throw fail("Latest release is missing its build or manifest.", 503);
  }
  return { zip, manifest };
}

async function versionResponse(release, env) {
  let manifest;
  try {
    const { zip, manifest: asset } = assets(release);
    const res = await fetch(await signedUrl(asset, env));
    if (!res.ok) throw fail("Could not read the release manifest.", 502);
    manifest = await res.json();
    manifest.sizeBytes ??= zip.size;
  } catch (err) {
    return json({ error: err.message }, err.status ?? 502);
  }

  return json({
    version: manifest.version ?? release.tag_name.replace(/^v/, ""),
    sha256: manifest.sha256,
    sizeBytes: manifest.sizeBytes,
    notes: manifest.notes ?? release.body ?? "",
  });
}

async function downloadResponse(release, env) {
  try {
    const { zip } = assets(release);
    return Response.redirect(await signedUrl(zip, env), 302);
  } catch (err) {
    return json({ error: err.message }, err.status ?? 502);
  }
}

/**
 * The asset API answers with a 302 to a pre-signed CDN URL. redirect:"manual"
 * is load-bearing: that signed URL carries its own auth in the query string,
 * and forwarding our Authorization header to it makes the CDN reject the
 * request outright.
 */
async function signedUrl(asset, env) {
  const res = await fetch(asset.url, {
    headers: { ...ghHeaders(env), Accept: "application/octet-stream" },
    redirect: "manual",
  });
  const location = res.headers.get("location");
  if (!location) throw fail(`No download URL from GitHub (${res.status}).`, 502);
  return location;
}

function fail(message, status) {
  const err = new Error(message);
  err.status = status;
  return err;
}

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8" },
  });
}
