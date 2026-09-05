// SandboxPanel.cs
// The unit/building sandbox: pick an entity, click the ground to place it,
// watch it behave, and tune its SO in the Inspector WITHOUT leaving play mode.
//
// Why this exists
// ---------------
// Before DOTS, stats lived on MonoBehaviours and could be nudged straight in
// the Inspector. Under ECS the numbers are copied into components at spawn
// (see any factory: Health/MoveSpeed/Damage/AttackCooldown/LineOfSight are all
// read from the def once, in Create), so an SO edit reached NEW entities only.
// This panel closes both halves of that gap:
//
//   * placement  -- a palette + ground click, instead of editing ScenarioSetup
//                   and recompiling to try a matchup
//   * live stats -- a change watcher that pushes edited SO fields onto entities
//                   already on the field
//
// The live resync is a CHANGE propagator, not a clamp. It snapshots each type's
// def and pushes only the fields that actually moved since the last snapshot.
// That matters: blindly re-pushing every field each tick would stomp research
// bonuses, rank bumps and ability buffs the moment they applied.
//
// What the resync CANNOT reach is per-type state -- ArcherState.MinRange /
// MaxRange / AimTimeRequired, projectile trajectory and speed, the Defense
// block, building levels -- because those live in type-specific structs rather
// than shared components. "Respawn all" covers them: it rebuilds every recorded
// placement through the factories, so the board comes back fully current.
//
// Presentation-only. It reads and writes sim components through EntityManager
// (allowed: Presentation -> Runtime), but adds no systems and is mounted only
// for ScenarioType.Sandbox, so nothing here can reach a normal match.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Data;
using TheWaningBorder.Entities;
using TheWaningBorder.Influence;
using TheWaningBorder.UI.GameUI;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    public class SandboxPanel : MonoBehaviour
    {
        // ── layout ──────────────────────────────────────────────────────────
        // Both overlays live in the TOP corners, because the in-game HUD owns
        // the bottom band across the full width (bottom-left dock, actions
        // panel, minimap) and top-centre belongs to the match clock. The two
        // reservations are read from their owners rather than copied, so a HUD
        // re-layout moves the sandbox with it instead of silently overlapping —
        // same contract TopChoiceBar has with GameClockHUD.
        private const float PanelW = 300f;
        private const float ResultW = 340f;
        private const float PanelMargin = 8f;
        /// <summary>Extra breathing room past each HUD reservation.</summary>
        private const float ClearanceGap = 10f;

        // ── cadence ─────────────────────────────────────────────────────────
        /// <summary>Seconds between SO-change checks and ledger samples. The
        /// sandbox holds tens-to-hundreds of entities, so a full scan at 10 Hz
        /// is nothing; it is still not something to do every frame.</summary>
        private const float PollInterval = 0.1f;

        /// <summary>
        /// True while the sandbox owns the mouse -- either something is armed
        /// for placement, or the cursor is over one of the panels.
        /// RTSInputManager checks this in its "should I ignore this click"
        /// guard, exactly as it does for BuilderCommandPanel.IsPlacingBuilding.
        ///
        /// The pointer-over-panel half matters as much as the armed half: these
        /// are IMGUI panels, and RTSInputManager's normal "pointer is over UI"
        /// test goes through the EventSystem, which cannot see IMGUI at all. So
        /// without this, every click on a palette button would ALSO land on the
        /// world underneath and clear the selection.
        /// </summary>
        public static bool IsPlacing { get; private set; }

        private enum PaletteMode { Units, Buildings, Terrain }

        /// <summary>
        /// The three paintable map layers. Each is a DIFFERENT live grid with
        /// its own owner, so the brush writes through that owner's API rather
        /// than poking one shared surface:
        ///   Blood     -> BloodMap            (Feraldis blood pool)
        ///   Curse     -> VeilField.Saturation (the veil crust CA)
        ///   Influence -> PlayerInfluenceMap  (per-faction territory channel)
        /// </summary>
        private enum PaintLayer { Blood, Curse, Influence }

        // ── categories ──────────────────────────────────────────────────────
        // One collapsible group per branch of the TechTree, in tree order. The
        // ids ARE the taxonomy at runtime -- CLAUDE.md: "the roster is
        // culture-gated by id prefix at runtime" -- so grouping reads the same
        // prefixes EntityExtractors.GetRequiredCulture* already ships, rather
        // than inventing a second, driftable classification.
        private const int CatAge0 = 0;
        private const int CatAlanthor = 1;
        private const int CatRunai = 2;
        private const int CatFeraldis = 3;
        private const int CatSects = 4;
        private const int CatCurse = 5;
        private const int CatOther = 6;
        private const int CatCount = 7;

        private static readonly string[] CategoryNames =
            { "Age 0", "Alanthor", "Runai", "Feraldis", "Sects", "The Curse", "Other" };

        /// <summary>The curse's own units (Border/Units in the tree).</summary>
        private static readonly HashSet<string> CurseIds = new()
        { "Crystalling", "Godsplinter", "Veilstinger" };

        // ── records ─────────────────────────────────────────────────────────

        /// <summary>One placement the user made; replayed by Respawn all.</summary>
        private struct Placement
        {
            public string Id;
            public bool IsBuilding;
            public Faction Faction;
            public Vector3 Position;
        }

        /// <summary>Per (faction, type) tally for the result readout.</summary>
        private class Tally
        {
            public int Spawned;
            public int Alive;
            public int Deaths;
            public long DamageTaken;
            public long HealingTaken;
            public bool IsBuilding;
        }

        /// <summary>The stat snapshot the change watcher diffs against.</summary>
        private struct StatSnapshot
        {
            public float Hp, Speed, Damage, Cooldown, LineOfSight, Radius;
        }

        private EntityWorld _world;
        private EntityManager _em;
        private bool _ready;

        // palette
        private PaletteMode _mode = PaletteMode.Units;
        private readonly List<string> _allUnitIds = new();
        private readonly List<string> _allBuildingIds = new();
        /// <summary>Category -> ids, per mode. [mode][category]</summary>
        private readonly List<string>[][] _grouped = new List<string>[2][];
        /// <summary>Visible (filter-passing) ids per category, current mode.</summary>
        private readonly List<string>[] _filtered = new List<string>[CatCount];
        private readonly bool[] _expanded = new bool[CatCount];
        private string _filter = "";
        private string _lastFilter = null;
        private PaletteMode _lastMode = PaletteMode.Buildings;   // force first refilter
        private string _armedId;
        private bool _armedIsBuilding;
        private Faction _brushFaction = Faction.Blue;
        private int _brushCount = 1;
        private Vector2 _listScroll;
        private static readonly int[] BrushCounts = { 1, 5, 10, 20 };

        // terrain brush
        private PaintLayer _layer = PaintLayer.Blood;
        private bool _erase;
        private float _brushRadius = 12f;
        private float _brushStrength = 1f;
        private float _nextPaint;
        /// <summary>Seconds between paint stamps while the button is held.</summary>
        private const float PaintInterval = 0.04f;
        private string _paintNote = "";

        // board
        private readonly List<Placement> _placements = new();
        /// <summary>Buildings carry no BuildingTypeId the way units carry
        /// UnitTypeId, so the sandbox remembers what it placed. Nothing else
        /// spawns here, so this is complete by construction.</summary>
        private readonly Dictionary<Entity, string> _buildingIdByEntity = new();

        // live-stat watching
        private readonly Dictionary<string, StatSnapshot> _lastSeenDef = new();

        // ledger
        private readonly Dictionary<Entity, int> _lastHealth = new();
        private readonly Dictionary<string, Tally> _tallies = new();
        private readonly List<string> _tallyOrder = new();
        private float _boardClock;
        private bool _boardRunning;
        private float _resolvedAt = -1f;
        private string _lastResyncNote = "";
        private float _lastResyncNoteAt = -99f;

        private float _nextPoll;
        private bool _showResults = true;
        private Vector2 _resultScroll;

        // GUI styles, built once (OnGUI must not allocate a GUIStyle per frame)
        private GUIStyle _hdr, _row, _rowSel, _grp, _small, _box;
        private bool _stylesReady;

        // ────────────────────────────────────────────────────────────────────

        private void Start()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world == null || !_world.IsCreated)
            {
                Debug.LogError("[SandboxPanel] No ECS world — mounted too early");
                enabled = false;
                return;
            }
            _em = _world.EntityManager;
            _ready = true;

            BuildCatalogLists();
        }

        private void OnDisable()
        {
            IsPlacing = false;

            // Time.timeScale is global and survives a scene load, so a sandbox
            // left paused or at 4x would carry straight into the next match.
            GameSpeedControl.Apply();
        }

        // ── catalog + categories ────────────────────────────────────────────

        /// <summary>
        /// Every unit and building the catalog knows, bucketed by category and
        /// sorted. Ids are the truth here, not display names: the factories are
        /// keyed by id and several entities share a display name across cultures.
        /// </summary>
        private void BuildCatalogLists()
        {
            _allUnitIds.Clear();
            _allBuildingIds.Clear();

            for (int m = 0; m < 2; m++)
            {
                _grouped[m] = new List<string>[CatCount];
                for (int c = 0; c < CatCount; c++) _grouped[m][c] = new List<string>();
            }
            for (int c = 0; c < CatCount; c++) _filtered[c] = new List<string>();

            foreach (var kv in TechCatalog.AllUnits)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                _allUnitIds.Add(kv.Key);
                _grouped[(int)PaletteMode.Units][Categorize(kv.Key, isBuilding: false)].Add(kv.Key);
            }
            foreach (var kv in TechCatalog.AllBuildings)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                _allBuildingIds.Add(kv.Key);
                _grouped[(int)PaletteMode.Buildings][Categorize(kv.Key, isBuilding: true)].Add(kv.Key);
            }

            for (int m = 0; m < 2; m++)
                for (int c = 0; c < CatCount; c++)
                    _grouped[m][c].Sort(System.StringComparer.OrdinalIgnoreCase);

            _lastFilter = null;   // force a refilter
        }

        /// <summary>
        /// Which collapsible group an id belongs to. Mirrors the TechTree branch
        /// layout using the same id conventions the shipping culture gates use
        /// (EntityExtractors.GetRequiredCultureForUnit / GetRequiredCulture),
        /// including their documented prefix-less exceptions.
        /// </summary>
        private static int Categorize(string id, bool isBuilding)
        {
            if (id.StartsWith("Alanthor_")) return CatAlanthor;
            if (id.StartsWith("Runai_")) return CatRunai;
            if (id.StartsWith("Feraldis_")) return CatFeraldis;

            // All sect content is prefixed: units are "Sect_<Unit>"
            // (SectConfig.UnitIdFor), chapels are "Chapel_<SectId>"
            // (SectConfig.ChapelIdFor), and the five sect BUILDINGS are
            // "Sect_Reliquary" / "Sect_Stonehold" / "Sect_Veilworks" /
            // "Sect_MendingHall" / "Sect_MusterYard" -- their folder names drop
            // the prefix, their ids do not.
            if (id.StartsWith("Sect_") || id.StartsWith("Chapel_")) return CatSects;

            if (!isBuilding && CurseIds.Contains(id)) return CatCurse;

            // Prefix-less exceptions, straight from the shipping culture gates.
            if (!isBuilding && (id == "Ledger" || id == "King Lexor")) return CatAlanthor;
            if (isBuilding && id == "ThessarasBazaar") return CatRunai;
            if (isBuilding && id == "Mine") return CatFeraldis;

            // Everything else with no culture marking is pre-culture Age 0.
            return CatAge0;
        }

        private List<string>[] CurrentGroups => _grouped[(int)_mode];

        private void RefreshFilter()
        {
            // Terrain has no roster. It is also mode index 2, and _grouped is
            // sized for the two ENTITY modes only, so falling through here
            // would index past the end the moment the tab is opened.
            if (_mode == PaletteMode.Terrain) return;

            if (_filter == _lastFilter && _mode == _lastMode) return;
            _lastFilter = _filter;
            _lastMode = _mode;

            bool all = string.IsNullOrEmpty(_filter);
            var groups = CurrentGroups;

            for (int c = 0; c < CatCount; c++)
            {
                _filtered[c].Clear();
                var src = groups[c];
                for (int i = 0; i < src.Count; i++)
                    if (all || src[i].IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        _filtered[c].Add(src[i]);

                // A filter is a search: hiding the matches behind a collapsed
                // header would defeat it. Typing expands every group that has a
                // hit; clearing the box collapses back to the tidy default.
                _expanded[c] = !all && _filtered[c].Count > 0;
            }
        }

        // ── frame loop ──────────────────────────────────────────────────────

        private void Update()
        {
            if (!_ready) return;

            HandlePlacementInput();

            if (_boardRunning) _boardClock += Time.unscaledDeltaTime;

            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollInterval;
                SyncChangedStatsToLiveEntities();
                SampleLedger();
            }
        }

        // ── placement ───────────────────────────────────────────────────────

        private void HandlePlacementInput()
        {
            bool overPanel = IsPointerOverPanel();

            if (_mode == PaletteMode.Terrain)
            {
                // The brush owns the mouse whenever the Terrain tab is open, so
                // a drag across the map paints instead of box-selecting.
                IsPlacing = true;
                HandlePaintInput(overPanel);
                return;
            }

            IsPlacing = _armedId != null || overPanel;

            if (_armedId == null) return;

            // Right-click / Escape disarm. Escape is normally the pause menu's,
            // but a live placement mode is exactly the kind of thing that
            // cascade cancels first, so taking it here matches the building
            // placement behaviour.
            if (UnityEngine.Input.GetMouseButtonDown(1) || UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                _armedId = null;
                IsPlacing = overPanel;
                return;
            }

            if (!UnityEngine.Input.GetMouseButtonDown(0)) return;
            if (overPanel) return;

            if (!TryGetGroundPoint(out var point)) return;
            PlaceBrush(_armedId, _armedIsBuilding, _brushFaction, point);

            // Stay armed unless the user holds Ctrl — a sandbox is for placing
            // many things, so "keep the brush" is the useful default and the
            // opt-out is the modifier (the inverse of the build panel's
            // shift-to-repeat, which defaults to placing one).
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
                _armedId = null;
        }

        /// <summary>
        /// Mouse to world. Physics first (terrain collider), falling back to the
        /// y=0 plane so the panel still works on a scenario whose terrain
        /// collider has not finished waking. Final height always comes from
        /// TerrainUtility so the entity lands ON the ground, not on whatever
        /// collider the ray happened to hit.
        /// </summary>
        private bool TryGetGroundPoint(out Vector3 point)
        {
            point = Vector3.zero;
            var cam = Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);

            float x, z;
            if (Physics.Raycast(ray, out var hit, 5000f))
            {
                x = hit.point.x; z = hit.point.z;
            }
            else
            {
                if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
                float t = -ray.origin.y / ray.direction.y;
                if (t <= 0f) return false;
                x = ray.origin.x + ray.direction.x * t;
                z = ray.origin.z + ray.direction.z * t;
            }

            point = new Vector3(x, TerrainUtility.GetHeight(x, z), z);
            return true;
        }

        /// <summary>
        /// Place the whole brush as a centred square block. Buildings always
        /// place ONE regardless of the brush: footprints differ per building and
        /// BuildingFactory snaps every placement to the 2 m build grid, so a
        /// multi-brush would stack overlapping structures on the same cells.
        /// </summary>
        private void PlaceBrush(string id, bool isBuilding, Faction faction, Vector3 centre)
        {
            if (isBuilding)
            {
                Spawn(id, true, faction, centre, record: true);
            }
            else
            {
                var def = TechCatalog.GetUnit(id);
                float spacing = Mathf.Max(1.6f, (def != null ? def.radius : 0.5f) * 3f);

                int n = _brushCount;
                int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
                int rows = Mathf.CeilToInt(n / (float)cols);
                float ox = (cols - 1) * spacing * 0.5f;
                float oz = (rows - 1) * spacing * 0.5f;

                int placed = 0;
                for (int r = 0; r < rows && placed < n; r++)
                {
                    for (int c = 0; c < cols && placed < n; c++, placed++)
                    {
                        float px = centre.x + c * spacing - ox;
                        float pz = centre.z + r * spacing - oz;
                        Spawn(id, false, faction, new Vector3(px, TerrainUtility.GetHeight(px, pz), pz), record: true);
                    }
                }
            }

            if (!_boardRunning) { _boardRunning = true; _boardClock = 0f; }
            _resolvedAt = -1f;
        }

        private void Spawn(string id, bool isBuilding, Faction faction, Vector3 pos, bool record)
        {
            var p = new float3(pos.x, pos.y, pos.z);

            if (isBuilding)
            {
                // BuildingFactory.Create snaps to the 2 m build grid itself and
                // produces a COMPLETED structure (UnderConstruction is added by
                // the player placement path, not the factory), which is what a
                // balance test wants.
                var e = BuildingFactory.Create(_em, id, p, faction);
                if (e != Entity.Null) _buildingIdByEntity[e] = id;
            }
            else
            {
                UnitFactory.Create(_em, id, p, faction);
            }

            if (record)
                _placements.Add(new Placement { Id = id, IsBuilding = isBuilding, Faction = faction, Position = pos });

            TallyFor(faction, id, isBuilding).Spawned++;
        }

        // ── terrain brush ───────────────────────────────────────────────────

        /// <summary>
        /// Paint while the left button is HELD, not on click — terrain is a
        /// drag operation. Stamps are throttled so a slow drag does not deposit
        /// hundreds of times into the same cell.
        /// </summary>
        private void HandlePaintInput(bool overPanel)
        {
            if (overPanel || !UnityEngine.Input.GetMouseButton(0)) return;
            if (Time.unscaledTime < _nextPaint) return;
            _nextPaint = Time.unscaledTime + PaintInterval;

            if (!TryGetGroundPoint(out var p)) return;

            switch (_layer)
            {
                case PaintLayer.Blood: PaintBlood(p); break;
                case PaintLayer.Curse: PaintCurse(p); break;
                case PaintLayer.Influence: PaintInfluence(p); break;
            }
        }

        /// <summary>
        /// BloodMap.AddBlood has a FIXED splat radius (one death = one small
        /// puddle), so a wide brush tiles splats across the disc at that
        /// spacing rather than passing a radius. Erase is BloodMap.Drain, which
        /// does take one.
        /// </summary>
        private void PaintBlood(Vector3 centre)
        {
            if (!BloodMap.Ready) { _paintNote = "Blood map not configured on this map"; return; }

            if (_erase)
            {
                BloodMap.Drain(centre.x, centre.z, _brushRadius);
                _paintNote = $"drained blood r={_brushRadius:0}m";
                return;
            }

            float step = BloodMap.SplatRadius;
            int n = Mathf.Max(0, Mathf.CeilToInt(_brushRadius / step));
            float r2 = _brushRadius * _brushRadius;
            int stamps = 0;

            for (int gz = -n; gz <= n; gz++)
            {
                for (int gx = -n; gx <= n; gx++)
                {
                    float dx = gx * step, dz = gz * step;
                    if (dx * dx + dz * dz > r2) continue;
                    BloodMap.AddBlood(new Vector3(centre.x + dx, centre.y, centre.z + dz), _brushStrength);
                    stamps++;
                }
            }
            _paintNote = $"blood x{stamps} r={_brushRadius:0}m";
        }

        /// <summary>
        /// The curse has TWO layers and the brush has to write both.
        ///
        ///  1. What you SEE is the CurseInfluence terrain layer, driven by
        ///     PlayerInfluenceMap's curse channel (8).
        ///  2. What SIMULATES is VeilField.Saturation, the crust CA's byte grid.
        ///
        /// In a normal match only (2) is authored: VeilFieldSystem walks the
        /// crust each pulse and deposits (1) from it. That never happens here --
        /// the system is gated on RequireForUpdate&lt;BorderNodeState&gt; ("no wells,
        /// no veil") and the sandbox starts with no wells at all. Painting
        /// saturation alone therefore produced literally nothing on screen.
        ///
        /// So the channel is deposited directly, and the saturation write is
        /// best-effort on top: with a well present the CA takes over and keeps
        /// the two in sync, without one you still get the visual to look at.
        /// </summary>
        private void PaintCurse(Vector3 centre)
        {
            // ── 1. the visible layer ────────────────────────────────────────
            if (!PlayerInfluenceMap.Ready) { _paintNote = "Influence map not configured"; return; }

            if (_erase)
                PlayerInfluenceMap.Erase(centre.x, centre.z, _brushRadius,
                                         PlayerInfluenceMap.CurseChannel);
            else
                PlayerInfluenceMap.Deposit(centre.x, centre.z, _brushRadius,
                                           PlayerInfluenceMap.CurseChannel,
                                           PlayerInfluenceMap.MaxValue * _brushStrength);

            // ── 2. the simulated layer (only if this map has a veil field) ──
            int painted = PaintCrustSaturation(centre);

            _paintNote = painted < 0
                ? (_erase ? "cleared curse (visual only - no veil field)"
                          : "curse (visual only - no veil field)")
                : (_erase ? $"cleared curse + {painted} crust cells"
                          : $"curse + {painted} crust cells");
        }

        /// <summary>
        /// Write the crust CA's byte grid, returning the cell count, or -1 when
        /// this map has no veil field.
        ///
        /// Generation is bumped afterwards because VeilNavStampSystem re-mirrors
        /// impassable crust into the nav cost field only when that counter
        /// moves -- skip it and units walk straight through painted crust. Jobs
        /// are completed first: VeilFieldSystem schedules work over this same
        /// array, so a MonoBehaviour write mid-flight is a race.
        /// </summary>
        private int PaintCrustSaturation(Vector3 centre)
        {
            var q = _em.CreateEntityQuery(ComponentType.ReadWrite<VeilField>());
            if (q.CalculateEntityCount() == 0) { q.Dispose(); return -1; }

            _em.CompleteAllTrackedJobs();

            var e = q.GetSingletonEntity();
            var field = _em.GetComponentData<VeilField>(e);
            q.Dispose();

            if (field.Initialised == 0 || !field.Saturation.IsCreated) return -1;
            if (!field.TryWorldToCell(new float3(centre.x, centre.y, centre.z), out int ccx, out int ccz))
                return -1;

            // A byte, not a fraction: CrustThreshold is the visible/impassable
            // line, DeepThreshold reads as established crust.
            byte value = _erase
                ? (byte)0
                : (byte)Mathf.Clamp(Mathf.RoundToInt(255f * _brushStrength),
                                    VeilField.CrustThreshold, 255);

            int cells = Mathf.Max(1, Mathf.CeilToInt(_brushRadius / Mathf.Max(0.01f, field.CellSize)));
            float r2 = _brushRadius * _brushRadius;
            int painted = 0;

            for (int z = ccz - cells; z <= ccz + cells; z++)
            {
                if (z < 0 || z >= field.Height) continue;
                for (int x = ccx - cells; x <= ccx + cells; x++)
                {
                    if (x < 0 || x >= field.Width) continue;
                    float dx = (x - ccx) * field.CellSize;
                    float dz = (z - ccz) * field.CellSize;
                    if (dx * dx + dz * dz > r2) continue;

                    field.Saturation[field.Index(x, z)] = value;
                    painted++;
                }
            }

            field.Generation++;
            _em.SetComponentData(e, field);
            return painted;
        }

        /// <summary>
        /// Territory for the brush faction. Deposit clamps only the upper
        /// bound, so erasing goes through PlayerInfluenceMap.Erase rather than
        /// a negative deposit.
        /// </summary>
        private void PaintInfluence(Vector3 centre)
        {
            if (!PlayerInfluenceMap.Ready) { _paintNote = "Influence map not configured"; return; }

            int channel = (int)_brushFaction;
            if (_erase)
            {
                PlayerInfluenceMap.Erase(centre.x, centre.z, _brushRadius, channel);
                _paintNote = $"cleared {FactionColors.GetColorName(_brushFaction)} r={_brushRadius:0}m";
                return;
            }

            PlayerInfluenceMap.Deposit(centre.x, centre.z, _brushRadius, channel,
                                       PlayerInfluenceMap.MaxValue * _brushStrength);
            _paintNote = $"{FactionColors.GetColorName(_brushFaction)} influence r={_brushRadius:0}m";
        }

        // ── board control ───────────────────────────────────────────────────

        /// <summary>
        /// Destroy every unit and building on the field. Views are not touched:
        /// PresentationSpawnSystem already reaps the GameObject of any entity
        /// that no longer exists, so DestroyEntity is the whole job.
        /// </summary>
        private void DestroyAllEntities()
        {
            DestroyAllMatching(ComponentType.ReadOnly<UnitTypeId>());
            DestroyAllMatching(ComponentType.ReadOnly<BuildingTag>());

            _lastHealth.Clear();
            _buildingIdByEntity.Clear();
        }

        private void DestroyAllMatching(ComponentType t)
        {
            var q = _em.CreateEntityQuery(t);
            var arr = q.ToEntityArray(Allocator.Temp);
            if (arr.Length > 0) _em.DestroyEntity(arr);
            arr.Dispose();
            q.Dispose();
        }

        private void ClearBoard()
        {
            DestroyAllEntities();
            _placements.Clear();
            _tallies.Clear();
            _tallyOrder.Clear();
            _boardRunning = false;
            _boardClock = 0f;
            _resolvedAt = -1f;
        }

        /// <summary>
        /// Rebuild the board from the recorded placements. This is the escape
        /// hatch for every stat the live resync cannot reach — attack range,
        /// aim time, projectile speed, the Defense block, building levels —
        /// because it runs the factories again against the current SOs.
        /// </summary>
        private void RespawnAll()
        {
            DestroyAllEntities();
            _tallies.Clear();
            _tallyOrder.Clear();

            // Iterate a copy: Spawn(record:false) leaves _placements alone, but
            // keeping the read and the write apart is cheap insurance.
            var saved = new List<Placement>(_placements);
            for (int i = 0; i < saved.Count; i++)
                Spawn(saved[i].Id, saved[i].IsBuilding, saved[i].Faction, saved[i].Position, record: false);

            _boardRunning = saved.Count > 0;
            _boardClock = 0f;
            _resolvedAt = -1f;
            _lastSeenDef.Clear();   // fresh entities == fresh baseline
        }

        // ── live stat resync ────────────────────────────────────────────────

        /// <summary>
        /// Read a type's current stats from whichever catalog owns it.
        ///
        /// TechCatalog.TryGetUnit / TryGetBuilding re-apply the SO onto the
        /// cached def on every lookup, so the values here are whatever the
        /// Inspector shows right now — no catalog reload, no domain reload, no
        /// recompile.
        ///
        /// Buildings have no speed/damage/cooldown of their own in the def, so
        /// those stay 0 and simply never register as changed — one diff path
        /// serves both kinds.
        /// </summary>
        private static bool TryReadSnapshot(string id, bool isBuilding, out StatSnapshot snap)
        {
            snap = default;

            if (isBuilding)
            {
                var b = TechCatalog.GetBuilding(id);
                if (b == null) return false;
                snap.Hp = b.hp;
                snap.LineOfSight = b.lineOfSight;
                snap.Radius = b.radius;
                return true;
            }

            var u = TechCatalog.GetUnit(id);
            if (u == null) return false;
            snap.Hp = u.hp;
            snap.Speed = u.speed;
            snap.Damage = u.damage;
            snap.Cooldown = u.attackCooldown;
            snap.LineOfSight = u.lineOfSight;
            snap.Radius = u.radius;
            return true;
        }

        /// <summary>
        /// Diff each type's def against the last snapshot and push only what
        /// moved onto the entities already alive.
        /// </summary>
        private void SyncChangedStatsToLiveEntities()
        {
            // Which types changed since the last poll? (the OLD snapshot is
            // kept as the value — the deltas below are measured against it)
            var changed = new Dictionary<string, StatSnapshot>();

            CollectChanged(_allUnitIds, false, changed);
            CollectChanged(_allBuildingIds, true, changed);

            if (changed.Count == 0) return;

            int touched = 0;
            var names = new List<string>();

            touched += ApplyToQuery(ComponentType.ReadOnly<UnitTypeId>(), false, changed, names);
            touched += ApplyToQuery(ComponentType.ReadOnly<BuildingTag>(), true, changed, names);

            if (touched > 0)
            {
                _lastResyncNote = $"{string.Join(", ", names)} -> {touched}";
                _lastResyncNoteAt = Time.unscaledTime;
            }
        }

        private void CollectChanged(List<string> ids, bool isBuilding, Dictionary<string, StatSnapshot> changed)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (!TryReadSnapshot(id, isBuilding, out var now)) continue;

                if (!_lastSeenDef.TryGetValue(id, out var was))
                {
                    // First sight of this type — take a baseline, push nothing.
                    // Pushing here would overwrite research/rank bonuses that
                    // were legitimately applied after spawn.
                    _lastSeenDef[id] = now;
                    continue;
                }

                if (!Same(was, now))
                {
                    _lastSeenDef[id] = now;
                    changed[id] = was;
                }
            }
        }

        private int ApplyToQuery(ComponentType t, bool isBuilding,
                                 Dictionary<string, StatSnapshot> changed, List<string> names)
        {
            var q = _em.CreateEntityQuery(t);
            var ents = q.ToEntityArray(Allocator.Temp);
            int touched = 0;

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!TryIdOf(e, isBuilding, out string id)) continue;
                if (!changed.TryGetValue(id, out var was)) continue;
                if (!TryReadSnapshot(id, isBuilding, out var now)) continue;

                ApplyChangedStats(e, was, now);
                touched++;
                if (!names.Contains(id)) names.Add(id);
            }

            ents.Dispose();
            q.Dispose();
            return touched;
        }

        /// <summary>
        /// Units stamp their id in UnitTypeId; buildings carry no equivalent, so
        /// the sandbox's own placement record answers for them.
        /// </summary>
        private bool TryIdOf(Entity e, bool isBuilding, out string id)
        {
            if (isBuilding) return _buildingIdByEntity.TryGetValue(e, out id);

            if (_em.HasComponent<UnitTypeId>(e))
            {
                id = _em.GetComponentData<UnitTypeId>(e).Value.ToString();
                return true;
            }
            id = null;
            return false;
        }

        private static bool Same(in StatSnapshot a, in StatSnapshot b) =>
            Mathf.Approximately(a.Hp, b.Hp) &&
            Mathf.Approximately(a.Speed, b.Speed) &&
            Mathf.Approximately(a.Damage, b.Damage) &&
            Mathf.Approximately(a.Cooldown, b.Cooldown) &&
            Mathf.Approximately(a.LineOfSight, b.LineOfSight) &&
            Mathf.Approximately(a.Radius, b.Radius);

        /// <summary>
        /// Push the fields that moved. Each is applied as a DELTA against the
        /// previous def value rather than as an absolute, so an entity carrying
        /// a research or rank bonus keeps it: bump the SO's damage by 5 and a
        /// veteran on +3 goes to base+5+3, not back to base+5.
        ///
        /// Every write is HasComponent-guarded, which is also what lets one path
        /// serve buildings: they simply have no MoveSpeed/Damage/AttackCooldown.
        /// </summary>
        private void ApplyChangedStats(Entity e, in StatSnapshot was, in StatSnapshot now)
        {
            if (!Mathf.Approximately(was.Hp, now.Hp) && _em.HasComponent<Health>(e))
            {
                var h = _em.GetComponentData<Health>(e);
                int delta = (int)now.Hp - (int)was.Hp;
                int newMax = Mathf.Max(1, h.Max + delta);

                // Keep the wound, not the absolute HP: an entity at 50% stays at
                // 50% of the new maximum. Re-reading a fight after an HP tweak
                // is meaningless if half the board silently heals to full.
                float frac = h.Max > 0 ? h.Value / (float)h.Max : 1f;
                h.Max = newMax;
                h.Value = Mathf.Clamp(Mathf.RoundToInt(newMax * frac), 1, newMax);
                _em.SetComponentData(e, h);
            }

            if (!Mathf.Approximately(was.Speed, now.Speed) && _em.HasComponent<MoveSpeed>(e))
            {
                var m = _em.GetComponentData<MoveSpeed>(e);
                m.Value = Mathf.Max(0f, m.Value + (now.Speed - was.Speed));
                _em.SetComponentData(e, m);
            }

            if (!Mathf.Approximately(was.Damage, now.Damage) && _em.HasComponent<Damage>(e))
            {
                var d = _em.GetComponentData<Damage>(e);
                d.Value = Mathf.Max(0, d.Value + ((int)now.Damage - (int)was.Damage));
                _em.SetComponentData(e, d);
            }

            if (!Mathf.Approximately(was.Cooldown, now.Cooldown) && _em.HasComponent<AttackCooldown>(e))
            {
                var c = _em.GetComponentData<AttackCooldown>(e);
                c.Cooldown = Mathf.Max(0.01f, c.Cooldown + (now.Cooldown - was.Cooldown));
                if (c.Timer > c.Cooldown) c.Timer = c.Cooldown;
                _em.SetComponentData(e, c);
            }

            if (!Mathf.Approximately(was.LineOfSight, now.LineOfSight) && _em.HasComponent<LineOfSight>(e))
            {
                var l = _em.GetComponentData<LineOfSight>(e);
                l.Radius = Mathf.Max(0f, l.Radius + (now.LineOfSight - was.LineOfSight));
                _em.SetComponentData(e, l);
            }

            if (!Mathf.Approximately(was.Radius, now.Radius) && _em.HasComponent<Radius>(e))
            {
                var r = _em.GetComponentData<Radius>(e);
                r.Value = Mathf.Max(0.05f, r.Value + (now.Radius - was.Radius));
                _em.SetComponentData(e, r);
            }
        }

        // ── result ledger ───────────────────────────────────────────────────

        private Tally TallyFor(Faction f, string id, bool isBuilding)
        {
            string key = (int)f + "|" + id;
            if (!_tallies.TryGetValue(key, out var t))
            {
                t = new Tally { IsBuilding = isBuilding };
                _tallies[key] = t;
                _tallyOrder.Add(key);
            }
            return t;
        }

        /// <summary>
        /// Damage is measured the same way DamageNumbersUI measures it: by
        /// watching Health deltas. That covers every source — melee, projectiles,
        /// AoE, spells, burn DOT, regen — without hooking a single combat system,
        /// and it cannot desync anything because it only reads.
        ///
        /// It attributes damage TAKEN, which is what a health delta actually
        /// tells you. Damage DEALT per side is derived in the readout as the sum
        /// of what every other side took; per-TYPE dealt would need attacker-side
        /// hooks and is deliberately not claimed here.
        /// </summary>
        private void SampleLedger()
        {
            foreach (var kv in _tallies) kv.Value.Alive = 0;

            var seen = new HashSet<Entity>();
            var factionsAlive = new HashSet<Faction>();

            SampleGroup(ComponentType.ReadOnly<UnitTypeId>(), false, seen, factionsAlive);
            SampleGroup(ComponentType.ReadOnly<BuildingTag>(), true, seen, factionsAlive);

            // Entities that vanished since the last sample died. Dropping their
            // stale HP entry is what keeps the dictionary from growing forever
            // across respawns.
            if (_lastHealth.Count != seen.Count)
            {
                var gone = new List<Entity>();
                foreach (var kv in _lastHealth)
                    if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
                for (int i = 0; i < gone.Count; i++)
                {
                    _lastHealth.Remove(gone[i]);
                    _buildingIdByEntity.Remove(gone[i]);
                }
            }

            foreach (var kv in _tallies)
                kv.Value.Deaths = Mathf.Max(0, kv.Value.Spawned - kv.Value.Alive);

            // One side left standing (and something was actually placed) => the
            // fight resolved; freeze the clock so the readout is a result and
            // not a stopwatch.
            if (_boardRunning && _resolvedAt < 0f && _placements.Count > 0 && factionsAlive.Count <= 1)
            {
                _resolvedAt = _boardClock;
                _boardRunning = false;
            }
        }

        private void SampleGroup(ComponentType t, bool isBuilding,
                                 HashSet<Entity> seen, HashSet<Faction> factionsAlive)
        {
            var q = _em.CreateEntityQuery(t,
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<FactionTag>());
            var ents = q.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!TryIdOf(e, isBuilding, out string id)) continue;

                seen.Add(e);

                var hp = _em.GetComponentData<Health>(e);
                var fac = _em.GetComponentData<FactionTag>(e).Value;
                var tally = TallyFor(fac, id, isBuilding);

                if (hp.Value > 0) { tally.Alive++; factionsAlive.Add(fac); }

                if (_lastHealth.TryGetValue(e, out int prev))
                {
                    int delta = prev - hp.Value;
                    if (delta > 0) tally.DamageTaken += delta;
                    else if (delta < 0) tally.HealingTaken += -delta;
                }
                _lastHealth[e] = hp.Value;
            }

            ents.Dispose();
            q.Dispose();
        }

        // ── GUI ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Top of both overlays: clear of the match clock. The clock pins
        /// top-CENTRE, so strictly it only threatens a centred widget — but
        /// matching its reserve keeps the sandbox aligned with the rest of the
        /// HUD instead of riding higher than everything else.
        /// </summary>
        private static float TopLimit => GameClockHUD.ReservedScreenHeight + ClearanceGap;

        /// <summary>
        /// Bottom of both overlays: clear of the in-game HUD's bottom band —
        /// the bottom-left dock, the actions panel and the minimap all live
        /// there. Read from GameUIManager rather than copied, so a HUD
        /// re-layout carries the sandbox with it.
        /// </summary>
        private static float BottomLimit => GameUIManager.ReservedBottomScreenHeight + ClearanceGap;

        private static float AvailableHeight =>
            Mathf.Max(120f, Screen.height - TopLimit - BottomLimit);

        private Rect PanelRect =>
            new Rect(PanelMargin, TopLimit, PanelW, AvailableHeight);

        /// <summary>
        /// The readout is capped at half the free column so it never grows down
        /// into the HUD; its own scroll view absorbs the overflow.
        /// </summary>
        private Rect ResultRect =>
            new Rect(Screen.width - ResultW - PanelMargin, TopLimit,
                     ResultW, Mathf.Min(360f, AvailableHeight));

        private bool IsPointerOverPanel()
        {
            var m = UnityEngine.Input.mousePosition;
            var p = new Vector2(m.x, Screen.height - m.y);
            if (PanelRect.Contains(p)) return true;
            if (_showResults && ResultRect.Contains(p)) return true;
            return false;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 8, 8) };
            _hdr = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12 };
            _row = new GUIStyle(GUI.skin.label) { fontSize = 11, padding = new RectOffset(4, 4, 1, 1) };
            _rowSel = new GUIStyle(_row) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 1f, 0.6f) } };
            _grp = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(2, 2, 3, 3),
                normal = { textColor = new Color(0.85f, 0.85f, 0.6f) }
            };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = new Color(0.75f, 0.75f, 0.75f) } };
        }

        private void OnGUI()
        {
            if (!_ready) return;
            EnsureStyles();
            RefreshFilter();

            DrawPalette();
            if (_showResults) DrawResults();
        }

        private void DrawPalette()
        {
            GUILayout.BeginArea(PanelRect, _box);

            GUILayout.Label("SANDBOX", _hdr);

            // mode tabs
            GUILayout.BeginHorizontal();
            DrawModeTab(PaletteMode.Units, "Units");
            DrawModeTab(PaletteMode.Buildings, "Buildings");
            DrawModeTab(PaletteMode.Terrain, "Terrain");
            GUILayout.EndHorizontal();

            // The terrain brush shares only the tab strip; it has no roster,
            // no faction brush count and no board controls.
            if (_mode == PaletteMode.Terrain)
            {
                DrawTerrainBrush();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label(_armedId != null
                ? $"Placing {_armedId} — click ground\nRMB/Esc cancel • Ctrl+click = place one"
                : "Pick something below to arm placement", _small);

            GUILayout.Space(4);

            // faction
            GUILayout.BeginHorizontal();
            GUILayout.Label("Faction", _row, GUILayout.Width(50));
            var prev = GUI.color;
            GUI.color = FactionColors.Get(_brushFaction);
            if (GUILayout.Button(FactionColors.GetColorName(_brushFaction), GUILayout.Height(20)))
                _brushFaction = (Faction)(((int)_brushFaction + 1) % 8);
            GUI.color = prev;
            GUILayout.EndHorizontal();

            // Brush count is a unit-only control: buildings always place one
            // (see PlaceBrush), so showing it in Buildings mode would be a lie.
            if (_mode == PaletteMode.Units)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Count", _row, GUILayout.Width(50));
                for (int i = 0; i < BrushCounts.Length; i++)
                {
                    bool on = _brushCount == BrushCounts[i];
                    var c = GUI.color;
                    if (on) GUI.color = new Color(0.55f, 1f, 0.6f);
                    if (GUILayout.Button("x" + BrushCounts[i], GUILayout.Height(20)))
                        _brushCount = BrushCounts[i];
                    GUI.color = c;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Find", _row, GUILayout.Width(50));
            _filter = GUILayout.TextField(_filter ?? "");
            if (GUILayout.Button("x", GUILayout.Width(22))) _filter = "";
            GUILayout.EndHorizontal();

            // grouped, collapsible list
            _listScroll = GUILayout.BeginScrollView(_listScroll);
            for (int c = 0; c < CatCount; c++)
            {
                var items = _filtered[c];
                if (items.Count == 0) continue;   // empty branches never show a header

                string arrow = _expanded[c] ? "▼" : "▶";
                if (GUILayout.Button($"{arrow}  {CategoryNames[c]}  ({items.Count})", _grp))
                    _expanded[c] = !_expanded[c];

                if (!_expanded[c]) continue;

                for (int i = 0; i < items.Count; i++)
                {
                    string id = items[i];
                    bool armed = id == _armedId;
                    if (GUILayout.Button(armed ? "  ▸ " + id : "     " + id, armed ? _rowSel : _row))
                    {
                        if (armed) _armedId = null;
                        else { _armedId = id; _armedIsBuilding = _mode == PaletteMode.Buildings; }
                    }
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);

            // board controls
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Respawn all", GUILayout.Height(24))) RespawnAll();
            if (GUILayout.Button("Clear", GUILayout.Height(24))) ClearBoard();
            GUILayout.EndHorizontal();

            // time control
            GUILayout.Label("Speed", _grp);
            DrawSpeedRows();

            _showResults = GUILayout.Toggle(_showResults, " Show result readout");

            if (Time.unscaledTime - _lastResyncNoteAt < 2.5f)
                GUILayout.Label("SO applied: " + _lastResyncNote, _small);
            else
                GUILayout.Label("Edit any Unit/Building DefSO in the Inspector —\nchanges apply live to what is already placed.", _small);

            GUILayout.EndArea();
        }

        private void DrawTerrainBrush()
        {
            GUILayout.Label("Hold LMB on the map to paint.", _small);
            GUILayout.Space(4);

            GUILayout.Label("Layer", _grp);
            DrawLayerTab(PaintLayer.Blood, "Blood",
                         "Feraldis blood pool - chain-ignites (docs/Design/Fire.md)");
            DrawLayerTab(PaintLayer.Curse, "Curse crust",
                         "VeilField saturation - impassable above the crust threshold");
            DrawLayerTab(PaintLayer.Influence, "Player influence",
                         "Territory for the faction selected below");

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode", _row, GUILayout.Width(56));
            var c = GUI.color;
            if (!_erase) GUI.color = new Color(0.55f, 1f, 0.6f);
            if (GUILayout.Button("Paint", GUILayout.Height(22))) _erase = false;
            GUI.color = c;
            if (_erase) GUI.color = new Color(1f, 0.55f, 0.5f);
            if (GUILayout.Button("Erase", GUILayout.Height(22))) _erase = true;
            GUI.color = c;
            GUILayout.EndHorizontal();

            // The faction picker only means something for the influence layer -
            // blood and curse crust are not owned by anyone.
            if (_layer == PaintLayer.Influence)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Faction", _row, GUILayout.Width(56));
                var pc = GUI.color;
                GUI.color = FactionColors.Get(_brushFaction);
                if (GUILayout.Button(FactionColors.GetColorName(_brushFaction), GUILayout.Height(20)))
                    _brushFaction = (Faction)(((int)_brushFaction + 1) % 8);
                GUI.color = pc;
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Radius {_brushRadius:0}m", _row, GUILayout.Width(92));
            _brushRadius = GUILayout.HorizontalSlider(_brushRadius, 2f, 60f);
            GUILayout.EndHorizontal();

            if (!_erase)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Strength {_brushStrength:0.00}", _row, GUILayout.Width(92));
                _brushStrength = GUILayout.HorizontalSlider(_brushStrength, 0.05f, 1f);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label("Speed", _grp);
            DrawSpeedRows();

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(_paintNote))
                GUILayout.Label(_paintNote, _small);

            // These are LIVE simulations, not a static canvas. Saying so here
            // beats the user concluding the brush is broken when their paint
            // fades or spreads on its own.
            GUILayout.Label("Painted layers keep simulating:", _small);
            GUILayout.Label("  influence decays toward neutral,", _small);
            GUILayout.Label("  blood decays inside influence, and", _small);
            GUILayout.Label("  curse crust grows/recedes by its CA.", _small);
        }

        private void DrawLayerTab(PaintLayer l, string label, string tip)
        {
            bool on = _layer == l;
            var c = GUI.color;
            if (on) GUI.color = new Color(0.55f, 1f, 0.6f);
            if (GUILayout.Button(label, GUILayout.Height(22))) _layer = l;
            GUI.color = c;
            if (on) GUILayout.Label("   " + tip, _small);
        }

        private void DrawModeTab(PaletteMode m, string label)
        {
            bool on = _mode == m;
            var c = GUI.color;
            if (on) GUI.color = new Color(0.55f, 1f, 0.6f);
            if (GUILayout.Button(label, GUILayout.Height(22)) && !on)
            {
                _mode = m;
                _armedId = null;   // an armed unit must not survive into Buildings mode
            }
            GUI.color = c;
        }

        /// <summary>
        /// Time-scale presets, split across two rows: seven buttons do not fit
        /// one row at the panel width. The sub-1x steps are the ones that earn
        /// their place in a balance tool - .5x and .75x are where a fight is
        /// slow enough to read individual trades without the .25x crawl.
        /// </summary>
        private void DrawSpeedRows()
        {
            GUILayout.BeginHorizontal();
            DrawSpeed(0f, "||");
            DrawSpeed(0.25f, ".25x");
            DrawSpeed(0.5f, ".5x");
            DrawSpeed(0.75f, ".75x");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawSpeed(1f, "1x");
            DrawSpeed(2f, "2x");
            DrawSpeed(4f, "4x");
            GUILayout.EndHorizontal();
        }

        private void DrawSpeed(float scale, string label)
        {
            bool on = Mathf.Approximately(Time.timeScale, scale);
            var c = GUI.color;
            if (on) GUI.color = new Color(0.55f, 1f, 0.6f);
            if (GUILayout.Button(label, GUILayout.Height(20))) Time.timeScale = scale;
            GUI.color = c;
        }

        private void DrawResults()
        {
            GUILayout.BeginArea(ResultRect, _box);

            string clock = _resolvedAt >= 0f
                ? $"RESOLVED in {_resolvedAt:0.0}s"
                : $"RUNNING {_boardClock:0.0}s";
            GUILayout.Label("RESULT  —  " + clock, _hdr);

            if (_tallyOrder.Count == 0)
            {
                GUILayout.Label("Nothing placed yet.", _small);
                GUILayout.EndArea();
                return;
            }

            // Per-side totals first: damage DEALT by a side is the damage every
            // other side took, which is the one cross-attribution a health-delta
            // ledger can make honestly.
            var takenByFaction = new Dictionary<Faction, long>();
            var aliveByFaction = new Dictionary<Faction, int>();
            var spawnByFaction = new Dictionary<Faction, int>();
            long grandTaken = 0;

            foreach (var key in _tallyOrder)
            {
                var t = _tallies[key];
                var f = FactionOf(key);
                takenByFaction.TryGetValue(f, out long tk); takenByFaction[f] = tk + t.DamageTaken;
                aliveByFaction.TryGetValue(f, out int al); aliveByFaction[f] = al + t.Alive;
                spawnByFaction.TryGetValue(f, out int sp); spawnByFaction[f] = sp + t.Spawned;
                grandTaken += t.DamageTaken;
            }

            _resultScroll = GUILayout.BeginScrollView(_resultScroll);
            foreach (var kv in spawnByFaction)
            {
                var f = kv.Key;
                var c = GUI.color;
                GUI.color = FactionColors.Get(f);
                long dealt = grandTaken - takenByFaction[f];
                GUILayout.Label(
                    $"{FactionColors.GetColorName(f).ToUpperInvariant()}  " +
                    $"alive {aliveByFaction[f]}/{kv.Value}   dealt {dealt}   taken {takenByFaction[f]}", _row);
                GUI.color = c;

                foreach (var key in _tallyOrder)
                {
                    if (FactionOf(key) != f) continue;
                    var t = _tallies[key];
                    string id = key.Substring(key.IndexOf('|') + 1);
                    string heal = t.HealingTaken > 0 ? $"  healed {t.HealingTaken}" : "";
                    string kind = t.IsBuilding ? "[B] " : "";
                    GUILayout.Label($"    {kind}{id}  {t.Alive}/{t.Spawned}   taken {t.DamageTaken}   lost {t.Deaths}{heal}", _small);
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Label("\"dealt\" is every other side's damage taken —\nper-type attribution needs attacker hooks.", _small);

            GUILayout.EndArea();
        }

        private static Faction FactionOf(string tallyKey)
            => (Faction)int.Parse(tallyKey.Substring(0, tallyKey.IndexOf('|')));
    }
}
