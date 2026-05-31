// bridge.js — JS side of the Unity ↔ HUD message bridge.
//
// C# → JS: HudBridge.cs calls webBrowserClient.ExecuteJs(...) which invokes
//   window.unityHUD.recv('topic', {...payload}) on this page.
//
// JS → C#: components call sendToUnity('menu:open', {...}). We route through
//   the UWB-injected `uwb.ExecuteJsMethod('HudMessage', {Topic, PayloadJson})`
//   bridge. HudBridge.cs registers `HudMessage` as a JS-callable method.
//
// When running outside UWB (browser preview / dev), `uwb` is undefined and
// sends are silently dropped — components still work locally with mock data.

import { useEffect, useState } from 'react';

const listeners = new Map(); // topic -> Set<(payload) => void>
const latest = new Map();    // topic -> last payload (so newly-mounted components see current state immediately)

function emit(topic, payload) {
  latest.set(topic, payload);
  const ls = listeners.get(topic);
  if (ls) for (const cb of ls) { try { cb(payload); } catch (e) { console.error(e); } }
}

function on(topic, cb) {
  let set = listeners.get(topic);
  if (!set) { set = new Set(); listeners.set(topic, set); }
  set.add(cb);
  // Immediately deliver last-known value if we have one
  if (latest.has(topic)) { try { cb(latest.get(topic)); } catch (e) { console.error(e); } }
  return () => set.delete(cb);
}

function send(topic, payload) {
  try {
    if (typeof uwb !== 'undefined' && uwb && typeof uwb.ExecuteJsMethod === 'function') {
      uwb.ExecuteJsMethod('HudMessage', {
        Topic: String(topic),
        PayloadJson: JSON.stringify(payload ?? null),
      });
    } else if (typeof console !== 'undefined') {
      // Browser preview fallback so the message is at least observable.
      console.log('[unityhud-out]', topic, payload);
    }
  } catch (e) {
    try { console.error('[unityhud bridge]', e); } catch {}
  }
}

const api = {
  // Inbound: C# calls window.unityHUD.recv(topic, payload)
  recv(topic, payload) { emit(topic, payload); },
  // Outbound: components call window.unityHUD.send(topic, payload)
  send,
  // Subscribe to topic from any module
  on,
  // Get last-known value synchronously
  peek(topic, fallback) { return latest.has(topic) ? latest.get(topic) : fallback; },
};

if (typeof window !== 'undefined') {
  window.unityHUD = api;
  // Announce so C# can know the bridge is live (used to flush queued pushes).
  //
  // The first send races HudBridge.cs registering the `HudMessage` callback —
  // UWB needs at least one Update tick after the browser reports ready, and
  // our JS bundle has already started running by then. A fire-and-forget send
  // before registration logs a MethodNotFoundException in the Unity console.
  // We defer the first attempt to the next animation frame and retry with
  // backoff until C# actually pushes something back (the cleanest signal we
  // have that the bridge round-trip works).
  let gotInbound = false;
  const origRecv = api.recv;
  api.recv = function (topic, payload) { gotInbound = true; origRecv.call(api, topic, payload); };

  let attempts = 0;
  const maxAttempts = 20;
  function tryReady() {
    if (gotInbound || attempts >= maxAttempts) return;
    attempts++;
    send('hud:ready');
    setTimeout(tryReady, 250);
  }
  // 500ms initial wait — long enough for HudBridge.Update to run several times
  // and call RegisterJsMethod. The retry then catches any environment where
  // registration is unusually slow.
  setTimeout(tryReady, 500);
}

export function useBridge(topic, fallback) {
  const [val, setVal] = useState(() => (latest.has(topic) ? latest.get(topic) : fallback));
  useEffect(() => on(topic, setVal), [topic]);
  return val;
}

export { send as sendToUnity };
