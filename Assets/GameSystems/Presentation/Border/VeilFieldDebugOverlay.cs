// VeilFieldDebugOverlay.cs
// PHASE 1 debug view of the Veil FIELD — the raw cellular-automaton data, with
// no crystals rendered yet. It exists so the sim can be verified on its own:
//   * F9        toggle the overlay,
//   * B         punch a BREAK at the cursor (clears coverage + stamps cooldown),
//   * [ / ]     shrink / grow the break radius.
// The texture shows one pixel per field cell:
//   dim purple   = thin haze (below crust threshold)
//   bright purple= crust
//   cyan-white   = deep veil
//   red          = broken cell on regrow cooldown (watch it refill after ~18 s)
//
// Presentation-only: reads VeilField, and appends to the VeilBreakRequest
// buffer that VeilFieldSystem owns — it never writes the field directly, so the
// field stays the single source of truth. Dev builds only; self-instantiates so
// there's nothing to wire in a scene.

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Core.Config;
using EntityWorld = Unity.Entities.World;
// NOTE: `Input` alone binds to the TheWaningBorder.Input namespace (it wins
// over a using-alias in this scope), so UnityEngine.Input is fully qualified
// at every call site below — same reason BorderDebugPanel does it.

namespace TheWaningBorder.Presentation
{
    public class VeilFieldDebugOverlay : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<VeilFieldDebugOverlay>() != null) return;
            var go = new GameObject("VeilFieldDebugOverlay");
            go.AddComponent<VeilFieldDebugOverlay>();
            DontDestroyOnLoad(go);
        }

        private bool _visible;
        private Texture2D _tex;
        private Color32[] _pixels;
        private int _texW, _texH;
        private float _refresh;
        private const float RefreshInterval = 0.15f;

        private float _breakRadius = VeilCrustConstants.DefaultBreakRadius;
        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        //
        // LAZY, unlike the ~20 other ComponentType[] caches in this codebase,
        // and it has to be. `typeof(VeilField)` converts to ComponentType via
        // TypeManager.GetTypeIndex, which throws NullReferenceException when the
        // TypeManager has no initialised world. As a STATIC FIELD INITIALIZER
        // that ran the instant anything touched this class — and OnGUI touching
        // `_visible` is enough — so with the overlay surviving play mode via
        // DontDestroyOnLoad, every editor GUI event after a domain reload threw
        // a TypeInitializationException.
        //
        // The other caches are safe because their owners are created by
        // GameBootstrap during a match, i.e. only ever with a live world. This
        // one self-instantiates and outlives the world, so it must not touch
        // TypeManager until TryResolve has confirmed one exists.
        private static ComponentType[] _fieldQueryTypes;
        private static ComponentType[] FieldQueryTypes
            => _fieldQueryTypes ??= new ComponentType[] { typeof(VeilField) };
        private TheWaningBorder.Core.CachedEntityQuery _fieldQueryCache;
        private EntityQuery _fieldQuery;

        // Diagnostics shown in the header.
        private int _cursorCoverage = -1;
        private int _cursorCooldown;

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F9)) _visible = !_visible;
            if (!_visible) return;

            if (UnityEngine.Input.GetKeyDown(KeyCode.LeftBracket)) _breakRadius = Mathf.Max(1f, _breakRadius - 2f);
            if (UnityEngine.Input.GetKeyDown(KeyCode.RightBracket)) _breakRadius += 2f;
            if (UnityEngine.Input.GetKeyDown(KeyCode.B)) RequestBreakAtCursor();

            _refresh -= Time.deltaTime;
            if (_refresh <= 0f)
            {
                _refresh = RefreshInterval;
                RebuildTexture();
            }
        }

        private bool TryResolve(out EntityManager em)
        {
            em = default;
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            em = world.EntityManager;
            _fieldQuery = _fieldQueryCache.Get(em, FieldQueryTypes);
            return !_fieldQuery.IsEmptyIgnoreFilter;
        }

        private void RebuildTexture()
        {
            if (!TryResolve(out var em)) { _cursorCoverage = -1; return; }
            var field = _fieldQuery.GetSingleton<VeilField>();
            if (field.Initialised == 0 || !field.Saturation.IsCreated) { _cursorCoverage = -1; return; }

            if (_tex == null || _texW != field.Width || _texH != field.Height)
            {
                _texW = field.Width; _texH = field.Height;
                _tex = new Texture2D(_texW, _texH, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
                _pixels = new Color32[_texW * _texH];
            }

            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = CellColor(field.Saturation[i], field.Cooldown[i]);

            _tex.SetPixels32(_pixels);
            _tex.Apply(false);

            // Sample the cell under the cursor for the header readout.
            if (TryCursorToField(field, out int cx, out int cz))
            {
                int idx = field.Index(cx, cz);
                _cursorCoverage = field.Saturation[idx];
                _cursorCooldown = field.Cooldown[idx];
            }
            else _cursorCoverage = -1;
        }

        // Coverage -> colour, with broken/cooldown cells flagged red so break +
        // regrow are legible at a glance.
        private static Color32 CellColor(byte sat, byte cooldown)
        {
            if (cooldown > 0)
                return new Color32(220, 40, 40, 235); // locked hole (regrowing)

            if (sat < VeilField.PaintThreshold)
                return new Color32(20, 12, 28, 90);    // effectively clean

            if (sat < VeilField.CrustThreshold)
                return new Color32(90, 45, 130, 160);  // thin haze
            if (sat < VeilField.DeepThreshold)
                return new Color32(150, 70, 210, 210); // crust

            return new Color32(180, 210, 255, 235);    // deep veil
        }

        private void RequestBreakAtCursor()
        {
            if (!TryResolve(out var em)) return;
            var entity = _fieldQuery.GetSingletonEntity();
            if (!em.HasBuffer<VeilBreakRequest>(entity)) return;
            if (!TryCursorToWorld(out float wx, out float wz)) return;

            var buf = em.GetBuffer<VeilBreakRequest>(entity);
            buf.Add(new VeilBreakRequest
            {
                Position = new float2(wx, wz),
                Radius = _breakRadius,
            });
            VeilDebris.Burst(new Vector3(wx, 0f, wz), _breakRadius);
        }

        // ── Cursor → world / field helpers ─────────────────────────────

        private static bool TryCursorToWorld(out float wx, out float wz)
        {
            wx = wz = 0f;
            var cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);

            // Prefer a real surface hit; otherwise intersect the y=0 ground plane.
            if (Physics.Raycast(ray, out var hit, 5000f))
            {
                wx = hit.point.x; wz = hit.point.z; return true;
            }
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            var p = ray.origin + ray.direction * t;
            wx = p.x; wz = p.z; return true;
        }

        private bool TryCursorToField(VeilField field, out int cx, out int cz)
        {
            cx = cz = 0;
            if (!TryCursorToWorld(out float wx, out float wz)) return false;
            cx = (int)math.floor((wx - field.Origin.x) / field.CellSize);
            cz = (int)math.floor((wz - field.Origin.y) / field.CellSize);
            return cx >= 0 && cx < field.Width && cz >= 0 && cz < field.Height;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            // The overlay is DontDestroyOnLoad and self-instantiating, so it can
            // outlive the ECS world (quit to menu, stop play mode). Drawing a
            // stale field texture then is misleading at best.
            var liveWorld = EntityWorld.DefaultGameObjectInjectionWorld;
            if (liveWorld == null || !liveWorld.IsCreated) return;

            const float pad = 10f;
            float maxSide = Mathf.Min(Screen.height * 0.5f, 360f);
            float scale = _tex != null ? maxSide / Mathf.Max(_texW, _texH) : 1f;
            float w = _tex != null ? _texW * scale : maxSide;
            float h = _tex != null ? _texH * scale : 40f;

            var box = new Rect(pad, pad, Mathf.Max(w, 320f), h + 58f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 6, box.y + 4, box.width - 12, 18),
                "VEIL FIELD [COVERAGE]  —  F9 hide · B break · [ ] radius");
            string readout = _cursorCoverage >= 0
                ? $"cursor cell: coverage {_cursorCoverage}/255  cooldown {_cursorCooldown}   |   break r={_breakRadius:F0}m"
                : $"cursor off-field   |   break r={_breakRadius:F0}m";
            GUI.Label(new Rect(box.x + 6, box.y + 22, box.width - 12, 18), readout);

            if (_tex != null)
            {
                // Flip vertically so +Z (north) is up, matching the world view.
                var img = new Rect(box.x + 6, box.y + 42, w, h);
                GUI.DrawTextureWithTexCoords(img, _tex, new Rect(0, 1, 1, -1));
            }
        }

        private void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
        }
    }
}
#endif
