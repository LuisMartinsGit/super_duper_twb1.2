/**
 * The Waning Border - update broker.
 *
 * This Worker holds the only credential that can read the private release
 * repo. Testers authenticate with a per-tester key stored in KV, so revoking
 * one tester is a single KV delete and affects nobody else.
 *
 * The build itself never passes through here. /download asks GitHub for the
 * asset, catches the 302 to GitHub's short-lived signed CDN URL, and hands
 * that redirect to the launcher - so the zip goes CDN -> tester directly and
 * Worker bandwidth stays at zero regardless of build size.
 */

const GH_API = "https://api.github.com";

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // Unauthenticated, so you can tell "Worker is down" from "key is bad".
    if (url.pathname === "/health") return json({ ok: true });

    const key = request.headers.get("x-twb-key") ?? url.searchParams.get("key");
    const tester = key ? await env.KEYS.get(`key:${key}`) : null;
    if (!tester) {
      return json({ error: "Unknown or revoked key. Ask Luis for a new one." }, 401);
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
