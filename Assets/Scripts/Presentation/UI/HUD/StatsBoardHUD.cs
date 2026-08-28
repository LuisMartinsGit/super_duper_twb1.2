// StatsBoardHUD.cs
// Always-on match statistics board (user request 2026-08-04): AoE-postgame
// style charts, but LIVE, rendered to DISPLAY 2 — in the editor, set a
// second Game view to "Display 2"; in a player build a second monitor is
// activated automatically. Never touches the main display's UI.
//
// Content, per faction with a resource bank (banner-colored series):
//   * six time-series charts — Supplies, Iron, Veilstone, Veilsteel,
//     Military count, Influence area % (the influence chart also carries a
//     purple CURSE series — the map-domination race at a glance)
//   * a live table of current bank values + worker/military counts
// Sampled every 5 s into a ring buffer (2 h window), charts redrawn on
// sample — presentation-only, reads sim state, writes nothing.

using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Economy;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    public class StatsBoardHUD : MonoBehaviour
    {
        private const int TargetDisplay = 1;      // Unity display index (Display 2)
        private const float SampleInterval = 5f;
        private const int MaxSamples = 1440;      // 2 h at 5 s
        // 3 x 3 grid (was 2 x 3 for six charts). Narrower so three fit
        // across the 1280 reference width.
        private const int ChartW = 300;
        private const int ChartH = 120;
        private const int ChartCols = 3;
        private const int MaxFactions = 8;
        private const int ChartCount = 9;
        private const int ChartMilitary = 4;      // series index for the military chart
        private const int ChartInfluence = 5;     // series index for the influence-% chart
        private const int ChartIncome = 6;        // total resource income per minute
        private const int ChartEconomy = 7;       // economy units (workers/miners)
        private const int ChartTotalUnits = 8;    // every living unit, army + economy

        private static readonly string[] ChartTitles =
            { "Supplies", "Iron", "Veilstone", "Veilsteel", "Military", "Influence %",
              "Income /min", "Economy units", "Total units" };

        /// <summary>Bank totals at the previous sample, per faction — the
        /// income chart is the delta between samples, scaled to a per-minute
        /// rate. Resources are weighted the same way FactionResources.TotalValue
        /// weights them (iron x2, veilstone x3, veilsteel x5) so one line means
        /// "economic output", not "supplies happened to tick".</summary>
        private readonly float[] _prevWealth = new float[MaxFactions];
        private bool _haveWealthBaseline;

        private readonly float[][,] _series = new float[ChartCount][,]; // [chart][faction, sample]
        private readonly float[] _curseInf = new float[MaxSamples];     // curse territory %, purple series
        private readonly bool[] _factionLive = new bool[MaxFactions];
        private int _sampleCount;
        private float _nextSample;

        private Texture2D[] _chartTex;
        private Text _table;
        private EntityQuery _unitQuery;
        private bool _queriesReady;

        private void Start()
        {
            // Activate the physical second display in player builds; in the
            // editor the Game view's "Display 2" dropdown shows this canvas.
            if (Display.displays.Length > TargetDisplay && !Display.displays[TargetDisplay].active)
                Display.displays[TargetDisplay].Activate();

            for (int c = 0; c < ChartCount; c++)
                _series[c] = new float[MaxFactions, MaxSamples];

            BuildUi();
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("[Stats Board Canvas]");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = TargetDisplay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            // Dark backdrop.
            var bg = new GameObject("Backdrop").AddComponent<Image>();
            bg.transform.SetParent(canvasGo.transform, false);
            bg.color = new Color(0.07f, 0.07f, 0.09f, 1f);
            var bgRt = bg.rectTransform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Live table across the top.
            var tableGo = new GameObject("Table");
            tableGo.transform.SetParent(canvasGo.transform, false);
            _table = tableGo.AddComponent<Text>();
            _table.font = font;
            _table.fontSize = 13;
            _table.alignment = TextAnchor.UpperLeft;
            _table.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            var tRt = _table.rectTransform;
            tRt.anchorMin = new Vector2(0f, 1f); tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = new Vector2(0f, -8f);
            tRt.sizeDelta = new Vector2(-24f, 150f);

            // 3x3 chart grid below the table.
            _chartTex = new Texture2D[ChartCount];
            for (int c = 0; c < ChartCount; c++)
            {
                _chartTex[c] = new Texture2D(ChartW, ChartH, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };

                int col = c % ChartCols, row = c / ChartCols;
                float x = 0.18f + col * 0.32f;
                float yLabel = 0.72f - row * 0.245f;
                float yChart = 0.62f - row * 0.245f;

                var label = new GameObject($"Label{c}").AddComponent<Text>();
                label.transform.SetParent(canvasGo.transform, false);
                label.font = font; label.fontSize = 13; label.fontStyle = FontStyle.Bold;
                label.text = ChartTitles[c];
                label.color = new Color(0.85f, 0.85f, 0.9f, 1f);
                var lRt = label.rectTransform;
                lRt.anchorMin = lRt.anchorMax = new Vector2(x, yLabel);
                lRt.sizeDelta = new Vector2(200f, 20f);

                var img = new GameObject($"Chart{c}").AddComponent<RawImage>();
                img.transform.SetParent(canvasGo.transform, false);
                img.texture = _chartTex[c];
                var iRt = img.rectTransform;
                iRt.anchorMin = iRt.anchorMax = new Vector2(x, yChart);
                iRt.sizeDelta = new Vector2(ChartW, ChartH);
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + SampleInterval;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            if (!_queriesReady)
            {
                // Plunderers are excluded from the military chart: they are
                // free, uncontrollable 45 HP tax collectors that stream out
                // of Raider Camps continuously, so counting them made a
                // Feraldis "military" line that said nothing about the army
                // the player could actually fight with.
                _unitQuery = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<UnitTag, FactionTag>()
                    .WithNone<PlundererTag>()
                    .Build(em);
                _queriesReady = true;
            }

            Sample(em);
            RedrawCharts();
            RedrawTable(em);

            // Once a minute, drop a bank snapshot into each faction's log —
            // the human's into Player_*.log, AIs into AI_*.log — so a human
            // match and an AI match compare line-for-line.
            if (_sampleCount % 12 == 0)
                WriteSnapshots(em);
        }

        private void WriteSnapshots(EntityManager em)
        {
            var localFaction = GameSettings.LocalPlayerFaction;
            int idx = Mathf.Max(0, _sampleCount - 1);
            for (int f = 0; f < MaxFactions; f++)
            {
                if (!_factionLive[f]) continue;
                if (!FactionEconomy.TryGetBank(em, (Faction)f, out var bank)) continue;
                var res = em.GetComponentData<FactionResources>(bank);
                string msg = $"supplies {res.Supplies} iron {res.Iron} veilstone {res.Veilstone} " +
                             $"veilsteel {res.Veilsteel} military {(int)_series[ChartMilitary][f, idx]} " +
                             $"influence {_series[ChartInfluence][f, idx]:0.0}% curse {_curseInf[idx]:0.0}%";
                if ((Faction)f == localFaction && !GameSettings.IsObserver)
                    TheWaningBorder.AI.AILogger.LogPlayer((Faction)f, "SNAPSHOT", msg);
                else
                    TheWaningBorder.AI.AILogger.Log((Faction)f, "SNAPSHOT", msg);
            }
        }

        private void Sample(EntityManager em)
        {
            // Ring behavior: past capacity, shift left (rare — every 2 h).
            int idx = _sampleCount;
            if (idx >= MaxSamples)
            {
                for (int c = 0; c < ChartCount; c++)
                    for (int f = 0; f < MaxFactions; f++)
                        for (int s = 1; s < MaxSamples; s++)
                            _series[c][f, s - 1] = _series[c][f, s];
                for (int s = 1; s < MaxSamples; s++)
                    _curseInf[s - 1] = _curseInf[s];
                idx = MaxSamples - 1;
            }
            else
                _sampleCount++;

            // Unit tallies per faction: military, economy, and everything.
            // NOTE the query already excludes Plunderers (see _unitQuery) —
            // free Raider-Camp bodies are not an army and would swamp both
            // the military and total lines for Feraldis.
            var mil = new int[MaxFactions];
            var eco = new int[MaxFactions];
            var tot = new int[MaxFactions];
            using (var tags = _unitQuery.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var facs = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= MaxFactions) continue;
                    tot[f]++;
                    var cls = tags[i].Class;
                    if (cls == UnitClass.Melee || cls == UnitClass.Ranged
                        || cls == UnitClass.Siege || cls == UnitClass.Magic)
                        mil[f]++;
                    else if (cls == UnitClass.Economy || cls == UnitClass.Miner)
                        eco[f]++;
                }
            }

            // TERRITORY %: the share of the map's territories each faction
            // HOLDS. Counted in territories, not influence cells.
            //
            // It used to be the share of influence cells over 0.5, which is a
            // different quantity entirely now that ground is claimed a
            // territory at a time (docs/Design/Regions.md §2): influence is an
            // Age 1 thing that nobody has in the opening, so the chart read
            // flat zero for every player through the whole early game while
            // they were visibly holding ground — and once it did move, it
            // measured a bubble around buildings rather than anything owned.
            //
            // Weighted by each territory's CLAIMABLE area, not by a flat count,
            // so holding one big territory outscores three slivers — otherwise
            // the chart says a player is winning the map for taking the three
            // smallest corners of it.
            var infCells = new int[MaxFactions];
            int curseCells = 0;
            if (TheWaningBorder.World.Regions.RegionMap.Ready
                && TheWaningBorder.World.Regions.TerritoryOwnership.Ready)
            {
                const int res = TheWaningBorder.Influence.PlayerInfluenceMap.Resolution;
                Vector2 wMin = TheWaningBorder.Influence.PlayerInfluenceMap.WorldMin;
                Vector2 wSize = TheWaningBorder.Influence.PlayerInfluenceMap.WorldSize;
                int claimable = 0;
                for (int y = 0; y < res; y++)
                {
                    float wz = wMin.y + (y + 0.5f) / res * wSize.y;
                    for (int x = 0; x < res; x++)
                    {
                        float wx = wMin.x + (x + 0.5f) / res * wSize.x;
                        int t = TheWaningBorder.World.Regions.RegionMap.RegionAt(wx, wz);
                        if (t == TheWaningBorder.World.Regions.RegionMap.None) continue;
                        claimable++;
                        int owner = TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(t);
                        if (owner >= 0 && owner < MaxFactions) infCells[owner]++;
                    }
                }
                if (claimable > 0)
                {
                    // The curse holds no territories yet (Regions.md §3 is
                    // unimplemented), so its share still comes from its field —
                    // the one channel for which influence IS the statement.
                    if (TheWaningBorder.Influence.PlayerInfluenceMap.Ready)
                    {
                        for (int y = 0; y < res; y++)
                            for (int x = 0; x < res; x++)
                                if (TheWaningBorder.Influence.PlayerInfluenceMap.CellValue(
                                        x, y, TheWaningBorder.Influence.PlayerInfluenceMap.CurseChannel) >= 0.5f)
                                    curseCells++;
                        _curseInf[idx] = curseCells / (float)(res * res) * 100f;
                    }
                    for (int f = 0; f < MaxFactions; f++)
                        infCells[f] = Mathf.RoundToInt(infCells[f] / (float)claimable * 10000f); // % x100
                }
            }

            for (int f = 0; f < MaxFactions; f++)
            {
                if (!FactionEconomy.TryGetBank(em, (Faction)f, out var bank))
                { _factionLive[f] = false; continue; }
                _factionLive[f] = true;
                var res = em.GetComponentData<FactionResources>(bank);
                _series[0][f, idx] = res.Supplies;
                _series[1][f, idx] = res.Iron;
                _series[2][f, idx] = res.Veilstone;
                _series[3][f, idx] = res.Veilsteel;
                _series[ChartMilitary][f, idx] = mil[f];
                _series[ChartInfluence][f, idx] = infCells[f] / 100f;
                _series[ChartEconomy][f, idx] = eco[f];
                _series[ChartTotalUnits][f, idx] = tot[f];

                // Income per minute = weighted bank delta since the last
                // sample. First sample has no baseline, so it reads 0 rather
                // than reporting the entire starting bank as one minute of
                // income. Spending shows as a dip, which is intentional: this
                // is NET economic flow, the number that actually says whether
                // a faction is converting resources into anything.
                float wealth = res.Supplies + res.Iron * 2f
                             + res.Veilstone * 3f + res.Veilsteel * 5f;
                if (_haveWealthBaseline && SampleInterval > 0f)
                    _series[ChartIncome][f, idx] =
                        (wealth - _prevWealth[f]) * (60f / SampleInterval);
                _prevWealth[f] = wealth;
            }
            _haveWealthBaseline = true;
        }

        private void RedrawCharts()
        {
            var bgCol = new Color32(16, 16, 20, 255);
            var gridCol = new Color32(38, 38, 46, 255);

            for (int c = 0; c < ChartCount; c++)
            {
                var px = _chartTex[c].GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i] = bgCol;
                for (int gy = 1; gy < 4; gy++) // horizontal quarter grid
                {
                    int y = gy * ChartH / 4;
                    for (int x = 0; x < ChartW; x++) px[y * ChartW + x] = gridCol;
                }

                float max = 1f;
                for (int f = 0; f < MaxFactions; f++)
                {
                    if (!_factionLive[f]) continue;
                    for (int s = 0; s < _sampleCount; s++)
                        if (_series[c][f, s] > max) max = _series[c][f, s];
                }
                if (c == ChartInfluence)
                    for (int s = 0; s < _sampleCount; s++)
                        if (_curseInf[s] > max) max = _curseInf[s];

                // The income chart is the only SIGNED series — spending shows
                // as a dip below zero, and that is the interesting half (a
                // faction banking 20k while spending nothing is the exact
                // failure mode these charts exist to expose). Give it a
                // symmetric scale around a mid-height zero line.
                bool signed = c == ChartIncome;
                if (signed)
                {
                    float mag = 1f;
                    for (int f = 0; f < MaxFactions; f++)
                    {
                        if (!_factionLive[f]) continue;
                        for (int s = 0; s < _sampleCount; s++)
                        {
                            float v = _series[c][f, s];
                            if (v < 0f) v = -v;
                            if (v > mag) mag = v;
                        }
                    }
                    max = mag;
                    int zeroY = ChartH / 2;
                    for (int x = 0; x < ChartW; x++)
                        px[zeroY * ChartW + x] = new Color32(70, 70, 84, 255);
                }

                for (int f = 0; f < MaxFactions; f++)
                {
                    if (!_factionLive[f]) continue;
                    Color32 col = FactionColors.Get((Faction)f);
                    DrawSeriesLine(px, f, c, max, col, signed);
                }

                // The influence chart carries the CURSE as its own purple
                // series — the three-way map race in one picture.
                if (c == ChartInfluence)
                {
                    Color32 purple = TheWaningBorder.Influence.PlayerInfluenceMap.CurseColor;
                    int prevX = -1, prevY = 0;
                    for (int s = 0; s < _sampleCount; s++)
                    {
                        int x = _sampleCount <= 1 ? 0 : s * (ChartW - 1) / (_sampleCount - 1);
                        int y = Mathf.Clamp((int)(_curseInf[s] / max * (ChartH - 2)), 0, ChartH - 1);
                        if (prevX >= 0) DrawSegment(px, prevX, prevY, x, y, purple);
                        prevX = x; prevY = y;
                    }
                }

                _chartTex[c].SetPixels32(px);
                _chartTex[c].Apply(false, false);
            }
        }

        private void DrawSeriesLine(Color32[] px, int f, int c, float max, Color32 col,
            bool signed = false)
        {
            int prevX = -1, prevY = 0;
            for (int s = 0; s < _sampleCount; s++)
            {
                int x = _sampleCount <= 1 ? 0 : s * (ChartW - 1) / (_sampleCount - 1);
                // Signed series plot around a mid-height zero line; unsigned
                // ones keep the original bottom-anchored scale.
                int y = signed
                    ? Mathf.Clamp((int)(ChartH / 2f + _series[c][f, s] / max * (ChartH / 2f - 2f)),
                                  0, ChartH - 1)
                    : Mathf.Clamp((int)(_series[c][f, s] / max * (ChartH - 2)), 0, ChartH - 1);
                if (prevX >= 0) DrawSegment(px, prevX, prevY, x, y, col);
                prevX = x; prevY = y;
            }
        }

        private static void DrawSegment(Color32[] px, int x0, int y0, int x1, int y1, Color32 col)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            if (steps == 0) steps = 1;
            for (int i = 0; i <= steps; i++)
            {
                int x = x0 + (x1 - x0) * i / steps;
                int y = y0 + (y1 - y0) * i / steps;
                if (x < 0 || x >= ChartW || y < 0 || y >= ChartH) continue;
                px[y * ChartW + x] = col;
                if (y + 1 < ChartH) px[(y + 1) * ChartW + x] = col; // 2px line
            }
        }

        private void RedrawTable(EntityManager em)
        {
            var sb = new StringBuilder(512);
            double t = EntityWorld.DefaultGameObjectInjectionWorld.Time.ElapsedTime;
            sb.AppendLine($"MATCH {((int)t) / 60:00}:{((int)t) % 60:00}    (5 s samples, {_sampleCount} points)");
            sb.AppendLine("Faction     Supplies     Iron    Veilstone  Veilsteel   Military  Influence");

            int idx = Mathf.Max(0, _sampleCount - 1);
            for (int f = 0; f < MaxFactions; f++)
            {
                if (!_factionLive[f]) continue;
                if (!FactionEconomy.TryGetBank(em, (Faction)f, out var bank)) continue;
                var res = em.GetComponentData<FactionResources>(bank);
                sb.AppendLine($"{(Faction)f,-10} {res.Supplies,9} {res.Iron,8} {res.Veilstone,10} " +
                              $"{res.Veilsteel,10} {(int)_series[ChartMilitary][f, idx],9} " +
                              $"{_series[ChartInfluence][f, idx],8:0.0}%");
            }
            sb.AppendLine($"{"Curse",-10} {"",9} {"",8} {"",10} {"",10} {"",9} {_curseInf[idx],8:0.0}%");
            _table.text = sb.ToString();
        }

        private void OnDestroy()
        {
            if (_chartTex == null) return;
            for (int c = 0; c < ChartCount; c++)
                if (_chartTex[c] != null) Destroy(_chartTex[c]);
        }
    }
}
