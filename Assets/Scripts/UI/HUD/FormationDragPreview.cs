// FormationDragPreview.cs
// While the user holds right-mouse-button and drags, shows a live floor-decal
// preview of where each battalion will be placed and which way it will face.
// One icon decal is rendered per soldier in each battalion (incomplete
// battalions render only the icons for surviving members). Drag distance
// controls thinness; drag direction controls facing. On release, applies the
// previewed formation to all selected battalions/units.
//
// Cooperates with RTSInputManager via FormationDragPreview.SuppressNextRightClick.
// If the user clicks without dragging, the drag system never engages and the
// existing instant move logic in RTSInputManager fires on mouse-up as usual.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    // Negative execution order — must run BEFORE RTSInputManager (default 0)
    // so on a mouse-up frame we can apply the preview and set the suppress flag
    // before RTSInputManager.HandleRightClick fires the regular move command.
    [DefaultExecutionOrder(-100)]
    public class FormationDragPreview : MonoBehaviour
    {
        // RTSInputManager checks this. When true, it skips its mouse-up handling
        // (because the drag preview is applying its own move command).
        public static bool SuppressNextRightClick;

        // ── Tuning ──
        [Header("Drag Activation")]
        [Tooltip("Hold time before the preview activates (lets quick clicks pass through)")]
        public float holdTimeThreshold = 0.18f;
        [Tooltip("Mouse pixel delta that activates the preview before the hold time")]
        public float dragPixelThreshold = 4f;

        [Header("Mouse Sensitivity")]
        [Tooltip("Pixel distance at which formation reaches its thinnest (1 row)")]
        public float pixelsForMaxThinness = 200f;

        [Header("Visual")]
        [Tooltip("Color tint of the per-soldier circle icons")]
        public Color ghostColor = new(0.2f, 1f, 0.3f, 0.85f);
        [Tooltip("Y offset above terrain for the icon quads")]
        public float ghostYOffset = 0.05f;
        [Tooltip("Diameter (world units) of each soldier circle icon")]
        public float iconSize = 0.9f;

        // ── State ──
        private EntityWorld _world;
        private EntityManager _em;

        private bool _rmbDown;
        private bool _previewActive;
        private float _rmbDownTime;
        private Vector2 _rmbDownScreen;
        private float3 _clickWorld;
        private List<Entity> _previewLeaders = new();

        // 0 = square-ish base (cols = ceil(sqrt(N))), 1 = single-line (rows=1, cols=N).
        // Driven by drag distance. Replaces the old _previewRows count.
        private float _previewThinness;
        // True world-space facing direction the user is currently aiming the
        // formation at, derived from the radial drag vector. Replaces the
        // old "yaw offset on top of move-dir" model.
        private float3 _previewFacingDir;
        private bool _hasFacingFromDrag;

        // Icon gameobjects — pooled
        private readonly List<GameObject> _activeGhosts = new();
        private readonly List<GameObject> _pool = new();
        private Material _iconMat;
        private Texture2D _iconTex;

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated) _em = _world.EntityManager;
            try
            {
                _iconTex = CreateCircleIconTexture(64);
                _iconMat = CreateIconMaterial(_iconTex);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[FormationDragPreview] Awake material creation failed: {ex.Message}");
                _iconMat = null;
            }
        }

        void OnDestroy()
        {
            if (_iconMat != null) Destroy(_iconMat);
            if (_iconTex != null) Destroy(_iconTex);
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
                _em = _world.EntityManager;
            }

            if (GameSettings.IsObserver) return;

            // ── Down: capture initial state but don't act yet ──
            // Skip entirely if Shift is held — the user is queuing waypoints
            // (RTSInputManager.QueueWaypointForSelection handles those) and
            // the drag-preview must not steal the click.
            bool shiftHeld = UnityEngine.Input.GetKey(KeyCode.LeftShift)
                          || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (UnityEngine.Input.GetMouseButtonDown(1))
            {
                if (!shiftHeld)
                {
                    Debug.Log("[FormationDragPreview] right-mouse-DOWN detected");
                    BeginRightMouseTracking();
                }
            }

            // ── Hold: detect drag, render preview ──
            if (_rmbDown && !shiftHeld && UnityEngine.Input.GetMouseButton(1))
            {
                UpdateDuringHold();
            }

            // ── Up: apply preview if active, else let RTSInputManager handle as click ──
            if (UnityEngine.Input.GetMouseButtonUp(1))
            {
                if (_previewActive)
                {
                    ApplyPreview();
                    // Tell RTSInputManager to skip its mouse-up handler this frame.
                    // We run BEFORE it, so the flag is already set when it runs.
                    SuppressNextRightClick = true;
                    Debug.Log($"[FormationDragPreview] applied preview to {_previewLeaders.Count} leaders, thinness={_previewThinness:0.00}");
                }
                EndRightMouseTracking();
            }
        }

        private void BeginRightMouseTracking()
        {
            // Only track if there's a usable selection of owned units
            var sel = SelectionSystem.CurrentSelection;
            if (sel == null || sel.Count == 0)
            {
                Debug.Log("[FormationDragPreview] no selection — preview disabled");
                return;
            }
            if (!TryGetClickWorld(out var click))
            {
                Debug.Log("[FormationDragPreview] click raycast failed — preview disabled");
                return;
            }

            _previewLeaders.Clear();
            var added = new HashSet<Entity>();
            foreach (var e in sel)
            {
                if (e == Entity.Null || !_em.Exists(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;

                Entity leader = e;
                if (_em.HasComponent<BattalionMemberData>(e))
                {
                    var md = _em.GetComponentData<BattalionMemberData>(e);
                    if (md.Leader == Entity.Null || !_em.Exists(md.Leader)) continue;
                    leader = md.Leader;
                }
                if (!added.Add(leader)) continue;
                _previewLeaders.Add(leader);
            }

            if (_previewLeaders.Count == 0)
            {
                Debug.Log("[FormationDragPreview] no movable leaders in selection — preview disabled");
                return;
            }

            _rmbDown = true;
            _rmbDownTime = Time.time;
            _rmbDownScreen = UnityEngine.Input.mousePosition;
            _clickWorld = click;
            _previewThinness = 0f;
            _previewFacingDir = float3.zero;
            _hasFacingFromDrag = false;
            _previewActive = false;
        }

        private void UpdateDuringHold()
        {
            float held = Time.time - _rmbDownTime;
            Vector2 delta = (Vector2)UnityEngine.Input.mousePosition - _rmbDownScreen;

            if (!_previewActive)
            {
                if (held >= holdTimeThreshold || delta.magnitude >= dragPixelThreshold)
                {
                    _previewActive = true;
                    SuppressNextRightClick = true; // tell RTSInputManager to skip mouse-up
                    Debug.Log($"[FormationDragPreview] preview activated — leaders: {_previewLeaders.Count}");
                }
                else
                {
                    return;
                }
            }

            // ── Radial drag mapping ──
            //   distance from click  →  thinness (0=square base, 1=single-row line)
            //   angle from click     →  facing direction (rotated around world up)
            float dragDist = delta.magnitude;
            _previewThinness = Mathf.Clamp01(dragDist / pixelsForMaxThinness);

            // Convert screen drag direction to world facing.
            // Screen +Y == "away from camera" (world camera-forward),
            // Screen +X == camera-right. Using camera basis projected to ground.
            if (dragDist >= dragPixelThreshold)
            {
                var cam = Camera.main;
                Vector3 camFwd = cam
                    ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized
                    : Vector3.forward;
                Vector3 camRight = Vector3.Cross(Vector3.up, camFwd).normalized;
                Vector3 worldFacing = (camRight * delta.x + camFwd * delta.y).normalized;
                _previewFacingDir = new float3(worldFacing.x, 0f, worldFacing.z);
                _hasFacingFromDrag = true;
            }
            else
            {
                _hasFacingFromDrag = false;
            }

            RebuildGhosts();
        }

        // ── Slot computation: returns where each leader will be placed and
        //    its facing rotation. Mirrors RTSInputManager.IssueFormationMove —
        //    square-ish base grid (cols = ceil(sqrt(N))), with drag-thinness
        //    interpolating toward a single-row line, and per-slot tactical
        //    role tagging (Front/Wing/Back) so role-based assignment works.
        //    Slots are indexed sequentially front-to-back, left-to-right.
        private void ComputeSlots(
            out float3[] slots,
            out quaternion sharedFacing,
            out int[] slotRole,
            out int[] slotRow,
            out int[] slotCol,
            out int[] slotsInRow)
        {
            int n = _previewLeaders.Count;
            slots = new float3[n];
            slotRole = new int[n];
            slotRow = new int[n];
            slotCol = new int[n];
            slotsInRow = new int[n];

            // Facing direction comes from the radial drag if the user has dragged
            // far enough; otherwise fall back to the centroid → click vector
            // (so a quick click still produces a sensibly-oriented formation).
            float3 moveDir;
            if (_hasFacingFromDrag && math.lengthsq(_previewFacingDir) > 0.001f)
            {
                moveDir = math.normalize(_previewFacingDir);
            }
            else
            {
                float3 centroid = float3.zero;
                for (int i = 0; i < n; i++)
                {
                    if (_em.HasComponent<LocalTransform>(_previewLeaders[i]))
                        centroid += _em.GetComponentData<LocalTransform>(_previewLeaders[i]).Position;
                }
                centroid /= math.max(1, n);

                moveDir = _clickWorld - centroid;
                moveDir.y = 0f;
                if (math.lengthsq(moveDir) < 0.01f)
                {
                    var cam = Camera.main;
                    Vector3 cf = cam ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
                    moveDir = new float3(cf.x, 0f, cf.z);
                }
                moveDir = math.normalize(moveDir);
            }

            float3 rightDir = math.cross(new float3(0f, 1f, 0f), moveDir);
            sharedFacing = quaternion.LookRotationSafe(moveDir, new float3(0f, 1f, 0f));

            if (n == 0) return;

            // Single shared cell size so rows align cleanly side-by-side.
            float maxBattalionWidth = 2f;
            float maxBattalionDepth = 2f;
            for (int i = 0; i < n; i++)
            {
                float w = 2f, d = 2f;
                if (_em.HasComponent<BattalionLeader>(_previewLeaders[i]))
                {
                    var bl = _em.GetComponentData<BattalionLeader>(_previewLeaders[i]);
                    w = bl.Columns * bl.Spacing + 1.5f;
                    d = bl.Rows * bl.Spacing + 1.5f;
                }
                if (w > maxBattalionWidth) maxBattalionWidth = w;
                if (d > maxBattalionDepth) maxBattalionDepth = d;
            }

            // thinness 0 → square base; thinness 1 → single-row line.
            int baseCols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(n)));
            int cols = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Lerp(baseCols, n, _previewThinness)),
                1, n);
            int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)cols));

            int[] rowCount = new int[rows];
            int rem = n;
            for (int r = 0; r < rows; r++)
            {
                rowCount[r] = Mathf.Min(cols, rem);
                rem -= rowCount[r];
            }

            int idx = 0;
            for (int r = 0; r < rows; r++)
            {
                int rc = rowCount[r];
                float rowWidth = rc * maxBattalionWidth;
                float startOffset = -rowWidth * 0.5f + maxBattalionWidth * 0.5f;
                for (int c = 0; c < rc; c++)
                {
                    float lateralOffset = startOffset + c * maxBattalionWidth;
                    slots[idx] = _clickWorld
                               + rightDir * lateralOffset
                               - moveDir * (r * maxBattalionDepth);
                    slotRow[idx] = r;
                    slotCol[idx] = c;
                    slotsInRow[idx] = rc;
                    idx++;
                }
            }

            // ── Tactical role per slot ──
            //   Back   = anything not in the front row
            //   Wing   = leftmost / rightmost slot of the front row (if >1 wide)
            //   Front  = front-row interior
            for (int s = 0; s < n; s++)
            {
                if (slotRow[s] > 0) slotRole[s] = 2;
                else if (slotsInRow[s] > 1
                         && (slotCol[s] == 0 || slotCol[s] == slotsInRow[s] - 1))
                    slotRole[s] = 1;
                else slotRole[s] = 0;
            }
        }

        private void ApplyPreview()
        {
            ComputeSlots(out var slots, out var sharedFacing,
                         out var slotRole, out _, out _, out _);
            int n = _previewLeaders.Count;
            if (n == 0) return;

            var leaderPos = new float3[n];
            for (int i = 0; i < n; i++)
                leaderPos[i] = _em.HasComponent<LocalTransform>(_previewLeaders[i])
                    ? _em.GetComponentData<LocalTransform>(_previewLeaders[i]).Position
                    : float3.zero;

            // Role-aware greedy assignment: role mismatch dominates distance,
            // so cavalry land on wings and ranged/siege/support/magic land in
            // the back row regardless of who started closer.
            const float ROLE_PENALTY = 1_000_000f;
            int[] leaderRole = new int[n];
            for (int l = 0; l < n; l++) leaderRole[l] = ClassifyLeaderRole(_previewLeaders[l]);

            int[] leaderToSlot = new int[n];
            bool[] slotUsed = new bool[n];
            for (int i = 0; i < n; i++) leaderToSlot[i] = -1;

            var pairs = new List<(int leader, int slot, float cost)>(n * n);
            for (int l = 0; l < n; l++)
            for (int s = 0; s < n; s++)
            {
                float3 d = slots[s] - leaderPos[l];
                d.y = 0f;
                float cost = math.lengthsq(d);
                if (slotRole[s] != leaderRole[l]) cost += ROLE_PENALTY;
                pairs.Add((l, s, cost));
            }
            pairs.Sort((a, b) => a.cost.CompareTo(b.cost));

            int assigned = 0;
            for (int p = 0; p < pairs.Count && assigned < n; p++)
            {
                var pp = pairs[p];
                if (leaderToSlot[pp.leader] != -1) continue;
                if (slotUsed[pp.slot]) continue;
                leaderToSlot[pp.leader] = pp.slot;
                slotUsed[pp.slot] = true;
                assigned++;
            }

            // Slowest-speed group move (BFME2-style synchronized arrival)
            float slowestSpeed = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_em.HasComponent<MoveSpeed>(_previewLeaders[i]))
                {
                    float s = _em.GetComponentData<MoveSpeed>(_previewLeaders[i]).Value;
                    if (s > 0f && s < slowestSpeed) slowestSpeed = s;
                }
            }
            if (slowestSpeed <= 0f || slowestSpeed == float.MaxValue) slowestSpeed = 3.5f;

            for (int i = 0; i < n; i++)
            {
                var unit = _previewLeaders[i];
                if (!_em.Exists(unit)) continue;

                int sIdx = leaderToSlot[i];
                if (sIdx < 0) sIdx = i;

                CommandRouter.IssueMove(_em, unit, slots[sIdx], CommandSource.LocalPlayer);

                if (_em.HasComponent<BattalionLeader>(unit))
                {
                    var bl = _em.GetComponentData<BattalionLeader>(unit);
                    bl.DestinationRot = sharedFacing;
                    bl.HasDestinationRot = 1;
                    bl.NeedsReassignment = 1;
                    _em.SetComponentData(unit, bl);
                }

                if (_em.HasComponent<FormationSpeedOverride>(unit))
                    _em.SetComponentData(unit, new FormationSpeedOverride { Value = slowestSpeed });
                else
                    _em.AddComponentData(unit, new FormationSpeedOverride { Value = slowestSpeed });
            }
        }

        private void EndRightMouseTracking()
        {
            _rmbDown = false;
            _previewActive = false;
            ClearGhosts();
        }

        // ── Ghost rendering ──

        // For each battalion: render one ground-icon per surviving member,
        // placed at the member's projected slot (Column/Row offset rotated by
        // the formation's facing). Dead/missing members render no icon — so
        // an incomplete battalion shows a partial pattern, not phantom slots.
        private void RebuildGhosts()
        {
            ClearGhosts();

            ComputeSlots(out var slots, out var facing,
                         out _, out _, out _, out _);
            int n = _previewLeaders.Count;
            for (int i = 0; i < n; i++)
            {
                var leader = _previewLeaders[i];
                if (!_em.Exists(leader)) continue;

                if (_em.HasComponent<BattalionLeader>(leader)
                    && _em.HasBuffer<BattalionMember>(leader))
                {
                    var bl = _em.GetComponentData<BattalionLeader>(leader);
                    var members = _em.GetBuffer<BattalionMember>(leader);
                    for (int k = 0; k < members.Length; k++)
                    {
                        var m = members[k].Value;
                        if (m == Entity.Null || !_em.Exists(m)) continue;
                        if (_em.HasComponent<Health>(m)
                            && _em.GetComponentData<Health>(m).Value <= 0) continue;
                        if (!_em.HasComponent<BattalionMemberData>(m)) continue;

                        var md = _em.GetComponentData<BattalionMemberData>(m);
                        float3 localOffset = BattalionFormation.ComputeSlotOffset(
                            md.Column, md.Row, bl.Columns, bl.Rows, bl.Spacing);
                        float3 worldOffset = math.rotate(facing, localOffset);
                        float3 soldierPos = slots[i] + worldOffset;
                        SpawnSoldierIcon(soldierPos);
                    }
                }
                else
                {
                    // Single non-battalion unit: one icon at its slot.
                    SpawnSoldierIcon(slots[i]);
                }
            }
        }

        // Spawns (or reuses from pool) a flat horizontal quad lying on the
        // terrain at the soldier slot position. Quad's normal points up, so
        // the circle icon is visible from the top-down RTS camera.
        private void SpawnSoldierIcon(float3 worldPos)
        {
            if (_iconMat == null) return;

            GameObject go;
            if (_pool.Count > 0)
            {
                go = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                go.SetActive(true);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(go.GetComponent<Collider>());
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterial = _iconMat;
                go.transform.SetParent(transform, false);
            }

            float y = TerrainUtility.GetHeight(worldPos.x, worldPos.z) + ghostYOffset;
            go.transform.position = new Vector3(worldPos.x, y, worldPos.z);
            // Quad's default normal is +Z. Rotate +90° around X so the normal
            // points world +Y (up) — laying the icon flat on the ground.
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(iconSize, iconSize, 1f);

            _activeGhosts.Add(go);
        }

        private void ClearGhosts()
        {
            for (int i = 0; i < _activeGhosts.Count; i++)
            {
                var g = _activeGhosts[i];
                if (g == null) continue;
                g.SetActive(false);
                _pool.Add(g);
            }
            _activeGhosts.Clear();
        }


        // ── Helpers ──

        private bool TryGetClickWorld(out float3 world)
        {
            world = float3.zero;
            var cam = Camera.main;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            // Hit the terrain plane at y=0 as a fallback if no terrain ray
            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                world = new float3(hit.point.x, hit.point.y, hit.point.z);
                return true;
            }
            // y=0 plane fallback
            if (Mathf.Abs(ray.direction.y) > 0.0001f)
            {
                float t = -ray.origin.y / ray.direction.y;
                if (t > 0)
                {
                    var p = ray.origin + ray.direction * t;
                    world = new float3(p.x, p.y, p.z);
                    return true;
                }
            }
            return false;
        }

        private bool IsOwnedByLocalPlayer(Entity e)
        {
            if (!_em.HasComponent<FactionTag>(e)) return false;
            return _em.GetComponentData<FactionTag>(e).Value == GameSettings.LocalPlayerFaction;
        }

        // Tactical role: 0 = Front (melee), 1 = Wing (cavalry), 2 = Back
        // (ranged / siege / support / magic). Battalion leaders are classified
        // by their first alive member. Mirrors RTSInputManager.ClassifyLeaderRole.
        private int ClassifyLeaderRole(Entity unit)
        {
            int FromEntity(Entity e)
            {
                if (_em.HasComponent<CavalryTag>(e)) return 1;
                if (_em.HasComponent<UnitTag>(e))
                {
                    var c = _em.GetComponentData<UnitTag>(e).Class;
                    if (c == UnitClass.Ranged || c == UnitClass.Siege
                        || c == UnitClass.Support || c == UnitClass.Magic)
                        return 2;
                    if (c == UnitClass.Melee) return 0;
                }
                return 0;
            }

            if (_em.HasComponent<BattalionLeader>(unit) && _em.HasBuffer<BattalionMember>(unit))
            {
                var members = _em.GetBuffer<BattalionMember>(unit);
                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i].Value;
                    if (m == Entity.Null || !_em.Exists(m)) continue;
                    if (_em.HasComponent<Health>(m)
                        && _em.GetComponentData<Health>(m).Value <= 0) continue;
                    return FromEntity(m);
                }
            }
            return FromEntity(unit);
        }

        // Procedurally-generated soft-circle alpha texture — one shared
        // instance for every soldier icon. White RGB so the material's
        // _BaseColor tint controls the final colour.
        private Texture2D CreateCircleIconTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float maxR = center;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    float a;
                    if (dist > 1f) a = 0f;
                    else if (dist < 0.55f) a = 1f;          // solid core
                    else if (dist < 0.78f) a = 0.85f;       // ring
                    else a = Mathf.SmoothStep(0.6f, 0f, (dist - 0.78f) / 0.22f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        // Transparent unlit material for the per-soldier circle icons.
        // Configures URP/Unlit for alpha blending; falls back to
        // Sprites/Default or Unlit/Transparent if URP isn't present.
        private Material CreateIconMaterial(Texture2D iconTex)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader) { hideFlags = HideFlags.DontSave };

            mat.mainTexture = iconTex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", iconTex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", iconTex);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ghostColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", ghostColor);
            mat.color = ghostColor;

            // URP/Unlit transparent surface (no-op on other shaders).
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            return mat;
        }
    }
}
