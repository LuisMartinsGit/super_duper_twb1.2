// StatsBoardHUD.cs
// DEBUG BOARD ON DISPLAY 2 — live match diagnostics for development. In the
// editor, set a second Game view to "Display 2"; in a development player build
// a second monitor is activated automatically. Never touches the main display.
//
// NOT SHIPPED IN RELEASES. The whole class compiles only when
//   UNITY_EDITOR || DEVELOPMENT_BUILD || TWB_DEBUG_DISPLAY
// and never when TWB_NO_DEBUG_DISPLAY is defined. Release builds
// (PlayerBuild, BuildOptions.None) are not development builds, so it is
// stripped from them automatically; add TWB_DEBUG_DISPLAY to the scripting
// defines to force it into one, TWB_NO_DEBUG_DISPLAY to remove it even from the
// editor. GameBootstrap adds it under the same condition. (The once-a-minute
// faction snapshot in the match logs is MatchSnapshotLog, which does ship.)
//
// Content:
//   * a header — match clock, FPS (average and worst frame), frame time,
//     entity count, single/multiplayer + lockstep tick, the curse (nodes,
//     territories held, wrath per faction);
//   * one row per faction — culture/era, human/AI, banks with NET income per
//     minute (Trading Outposts included, red when draining), population,
//     territories held and how many are cut off from a Fortress,
//     military / economy units, buildings, plans waiting for a worker,
//     Trading Outposts by trade, Religion Points;
//   * twelve charts (5 s samples, 2 h window): the four banks, the four net
//     incomes per minute, military units, population, territories held (with
//     the curse as a purple series) and Religion Points.
// Presentation only — reads sim state, writes nothing.

#if (UNITY_EDITOR || DEVELOPMENT_BUILD || TWB_DEBUG_DISPLAY) && !TWB_NO_DEBUG_DISPLAY

using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.World;
using TheWaningBorder.World.Regions;
using EntityWorld = Unity.Entities.World;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Ingame
{
    public class StatsBoardHUD : MonoBehaviour
    {
        private const int TargetDisplay = 1;      // Unity display index (Display 2)
        private const float SampleInterval = 5f;
        private const float TableInterval = 1f;
        private const int MaxSamples = 1440;      // 2 h at 5 s
        private const int ChartW = 270;
        private const int ChartH = 96;
        private const int ChartCols = 4;
        private const int MaxFactions = 8;

        private enum Chart
        {
            Supplies, Iron, Veilstone, Veilsteel,
            SuppliesPerMin, IronPerMin, VeilstonePerMin, VeilsteelPerMin,
            Military, Population, Territories, Religion,
        }
        private const int ChartCount = 12;

        private static readonly string[] ChartTitles =
        {
            "Supplies (bank)", "Iron (bank)", "Veilstone (bank)", "Veilsteel (bank)",
            "Supplies net /min", "Iron net /min", "Veilstone net /min", "Veilsteel net /min",
            "Military units", "Population", "Territories held  (purple = curse)", "Religion Points",
        };

        private static bool Signed(int c) => c >= (int)Chart.SuppliesPerMin && c <= (int)Chart.VeilsteelPerMin;

        private readonly float[][,] _series = new float[ChartCount][,];
        private readonly float[] _curseTerritories = new float[MaxSamples];
        private readonly bool[] _factionLive = new bool[MaxFactions];
        private int _sampleCount;
        private float _nextSample, _nextTable;

        private Texture2D[] _chartTex;
        private Color32[][] _chartPx;
        private Text _header, _table;

        // Frame timing.
        private float _fpsAccum;
        private int _fpsFrames;
        private float _fps, _worstMs, _worstWindowMs;
        private float _fpsWindowStart, _worstWindowStart;

        private EntityQuery _unitQuery, _buildingQuery, _outpostQuery, _brainQuery, _curseNodeQuery;
        private bool _queriesReady;

        private void Start()
        {
            if (Display.displays.Length > TargetDisplay && !Display.displays[TargetDisplay].active)
                Display.displays[TargetDisplay].Activate();
            for (int c = 0; c < ChartCount; c++)
                _series[c] = new float[MaxFactions, MaxSamples];
            BuildUi();
        }

        // ── UI ──────────────────────────────────────────────────────────

        private void BuildUi()
        {
            var canvasGo = new GameObject("[Debug Board Canvas]");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = TargetDisplay;
            HudCanvas.Configure(canvasGo.AddComponent<CanvasScaler>(), new Vector2(1280f, 720f));

            var bg = new GameObject("Backdrop").AddComponent<Image>();
            bg.transform.SetParent(canvasGo.transform, false);
            bg.color = new Color(0.06f, 0.06f, 0.08f, 1f);
            Stretch(bg.rectTransform);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _header = MakeText(canvasGo.transform, "Header", font, 12, new Vector2(0f, -6f), 34f);
            _header.color = new Color(1f, 0.86f, 0.55f, 1f);
            _table = MakeText(canvasGo.transform, "Table", font, 11, new Vector2(0f, -40f), 150f);

            _chartTex = new Texture2D[ChartCount];
            _chartPx = new Color32[ChartCount][];
            for (int c = 0; c < ChartCount; c++)
            {
                _chartTex[c] = new Texture2D(ChartW, ChartH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                _chartPx[c] = new Color32[ChartW * ChartH];
                int col = c % ChartCols, row = c / ChartCols;
                float x = 0.13f + col * 0.245f;
                float yLabel = 0.665f - row * 0.215f;
                float yChart = 0.58f - row * 0.215f;

                var label = new GameObject($"Label{c}").AddComponent<Text>();
                label.transform.SetParent(canvasGo.transform, false);
                label.font = font; label.fontSize = 11; label.fontStyle = FontStyle.Bold;
                label.text = ChartTitles[c];
                label.color = new Color(0.85f, 0.85f, 0.9f, 1f);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                var lRt = label.rectTransform;
                lRt.anchorMin = lRt.anchorMax = new Vector2(x, yLabel);
                lRt.sizeDelta = new Vector2(ChartW, 16f);

                var img = new GameObject($"Chart{c}").AddComponent<RawImage>();
                img.transform.SetParent(canvasGo.transform, false);
                img.texture = _chartTex[c];
                var iRt = img.rectTransform;
                iRt.anchorMin = iRt.anchorMax = new Vector2(x, yChart);
                iRt.sizeDelta = new Vector2(ChartW, ChartH);
            }
        }

        private static Text MakeText(Transform parent, string name, Font font, int size, Vector2 pos, float height)
        {
            var t = new GameObject(name).AddComponent<Text>();
            t.transform.SetParent(parent, false);
            t.font = font; t.fontSize = size;
            t.alignment = TextAnchor.UpperLeft;
            t.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(-24f, height);
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // ── Loop ────────────────────────────────────────────────────────

        private void Update()
        {
            TrackFrameTime();

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            EnsureQueries(em);

            if (Time.unscaledTime >= _nextSample)
            {
                _nextSample = Time.unscaledTime + SampleInterval;
                Sample(em);
                RedrawCharts();
            }
            if (Time.unscaledTime >= _nextTable)
            {
                _nextTable = Time.unscaledTime + TableInterval;
                RedrawHeader(em);
                RedrawTable(em);
            }
        }

        private void TrackFrameTime()
        {
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            _fpsAccum += dt; _fpsFrames++;
            if (now - _fpsWindowStart >= 1f)
            {
                _fps = _fpsFrames / Mathf.Max(0.0001f, _fpsAccum);
                _fpsAccum = 0f; _fpsFrames = 0; _fpsWindowStart = now;
            }
            _worstWindowMs = Mathf.Max(_worstWindowMs, dt * 1000f);
            if (now - _worstWindowStart >= 5f)
            {
                _worstMs = _worstWindowMs;
                _worstWindowMs = 0f; _worstWindowStart = now;
            }
        }

        private void EnsureQueries(EntityManager em)
        {
            if (_queriesReady) return;
            // Plunderers are free, uncontrollable Raider-Camp bodies — not an
            // army — and would swamp the Feraldis military line.
            _unitQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<UnitTag, FactionTag>().WithNone<PlundererTag>().Build(em);
            _buildingQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BuildingTag, FactionTag>().Build(em);
            _outpostQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TradingOutpostTag, TradingOutpostMode, FactionTag>().Build(em);
            _brainQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TheWaningBorder.AI.AIBrain>().Build(em);
            _curseNodeQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SmallNodeTag, FactionTag>().WithNone<BuildingCollapseState>().Build(em);
            _queriesReady = true;
        }

        // ── Per-faction snapshot (shared by the table and the samples) ──

        private struct Row
        {
            public bool Live, Ai;
            public FactionResources Bank;
            public TerritoryYield Net;
            public int PopCur, PopMax, Military, Economy, Buildings, UnderConstruction, Plans;
            public int Held, Disconnected;
            public int OutBuy, OutForge, OutSell;
            public int Rp, RpHave, RpNeed, Era;
            public byte Culture;
        }

        private readonly Row[] _rows = new Row[MaxFactions];

        private void Collect(EntityManager em)
        {
            for (int f = 0; f < MaxFactions; f++) _rows[f] = default;

            using (var tags = _unitQuery.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var facs = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < tags.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= MaxFactions) continue;
                    var cls = tags[i].Class;
                    if (cls == UnitClass.Melee || cls == UnitClass.Ranged || cls == UnitClass.Siege || cls == UnitClass.Magic)
                        _rows[f].Military++;
                    else if (cls == UnitClass.Economy || cls == UnitClass.Worker)
                        _rows[f].Economy++;
                }

            using (var ents = _buildingQuery.ToEntityArray(Allocator.Temp))
            using (var facs = _buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= MaxFactions) continue;
                    _rows[f].Buildings++;
                    if (em.HasComponent<UnderConstruction>(ents[i])) _rows[f].UnderConstruction++;
                }

            using (var modes = _outpostQuery.ToComponentDataArray<TradingOutpostMode>(Allocator.Temp))
            using (var facs = _outpostQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < modes.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= MaxFactions) continue;
                    switch (modes[i].Recipe)
                    {
                        case TradeRecipe.ForgeVeilsteel: _rows[f].OutForge++; break;
                        case TradeRecipe.SellVeilsteel: _rows[f].OutSell++; break;
                        case TradeRecipe.Hold: break;   // idle, counted in no trade
                        default: _rows[f].OutBuy++; break;
                    }
                }

            using (var brains = _brainQuery.ToComponentDataArray<TheWaningBorder.AI.AIBrain>(Allocator.Temp))
                for (int i = 0; i < brains.Length; i++)
                {
                    int f = (int)brains[i].Owner;
                    if (f >= 0 && f < MaxFactions) _rows[f].Ai = true;
                }

            for (int f = 0; f < MaxFactions; f++)
            {
                var fac = (Faction)f;
                if (!FactionEconomy.TryGetBank(em, fac, out var bank)) continue;
                ref var r = ref _rows[f];
                r.Live = true;
                r.Bank = em.GetComponentData<FactionResources>(bank);
                r.Net = TerritoryIncomeSystem.FactionNetForDisplay(em, fac);
                if (em.HasComponent<FactionPopulation>(bank))
                {
                    var pop = em.GetComponentData<FactionPopulation>(bank);
                    r.PopCur = pop.Current; r.PopMax = pop.Max;
                }
                if (em.HasComponent<FactionEra>(bank)) r.Era = em.GetComponentData<FactionEra>(bank).Value;
                r.Culture = CultureConfig.GetCompletedCulture(em, fac);
                r.Plans = TheWaningBorder.Entities.PlannedBuildings.CountAll(em, fac);
                r.Held = TerritoryClaimSystem.TerritoriesHeldBy(fac);
                if (RegionMap.Ready && TerritoryOwnership.Ready)
                    for (int t = 0; t < RegionMap.Count; t++)
                        if (TerritoryOwnership.OwnerOf(t) == f && !TerritoryClaimSystem.IsConnected(t, fac))
                            r.Disconnected++;
                r.Rp = FactionReligionPointsHelper.GetBalance(em, fac);
                (r.RpHave, r.RpNeed) = FactionReligionPointsHelper.PtsProgress(em, fac);
            }
        }

        // ── Samples & charts ────────────────────────────────────────────

        private void Sample(EntityManager em)
        {
            Collect(em);
            int idx = _sampleCount;
            if (idx >= MaxSamples)
            {
                for (int c = 0; c < ChartCount; c++)
                    for (int f = 0; f < MaxFactions; f++)
                        for (int s = 1; s < MaxSamples; s++)
                            _series[c][f, s - 1] = _series[c][f, s];
                for (int s = 1; s < MaxSamples; s++) _curseTerritories[s - 1] = _curseTerritories[s];
                idx = MaxSamples - 1;
            }
            else _sampleCount++;

            for (int f = 0; f < MaxFactions; f++)
            {
                var r = _rows[f];
                _factionLive[f] = r.Live;
                if (!r.Live) continue;
                _series[(int)Chart.Supplies][f, idx] = r.Bank.Supplies;
                _series[(int)Chart.Iron][f, idx] = r.Bank.Iron;
                _series[(int)Chart.Veilstone][f, idx] = r.Bank.Veilstone;
                _series[(int)Chart.Veilsteel][f, idx] = r.Bank.Veilsteel;
                _series[(int)Chart.SuppliesPerMin][f, idx] = r.Net.Supplies;
                _series[(int)Chart.IronPerMin][f, idx] = r.Net.Iron;
                _series[(int)Chart.VeilstonePerMin][f, idx] = r.Net.Veilstone;
                _series[(int)Chart.VeilsteelPerMin][f, idx] = r.Net.Veilsteel;
                _series[(int)Chart.Military][f, idx] = r.Military;
                _series[(int)Chart.Population][f, idx] = r.PopCur;
                _series[(int)Chart.Territories][f, idx] = r.Held;
                _series[(int)Chart.Religion][f, idx] = r.Rp;
            }
            _curseTerritories[idx] = CurseTerritoryCount();
        }

        private static int CurseTerritoryCount()
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return 0;
            int n = 0;
            for (int t = 0; t < RegionMap.Count; t++)
                if (TerritoryOwnership.OwnerOf(t) == TerritoryOwnership.Curse) n++;
            return n;
        }

        private void RedrawCharts()
        {
            var bgCol = new Color32(16, 16, 20, 255);
            var gridCol = new Color32(36, 36, 44, 255);
            for (int c = 0; c < ChartCount; c++)
            {
                var px = _chartPx[c];
                for (int i = 0; i < px.Length; i++) px[i] = bgCol;
                for (int gy = 1; gy < 4; gy++)
                {
                    int y = gy * ChartH / 4;
                    for (int x = 0; x < ChartW; x++) px[y * ChartW + x] = gridCol;
                }

                bool signed = Signed(c);
                float max = 1f;
                for (int f = 0; f < MaxFactions; f++)
                {
                    if (!_factionLive[f]) continue;
                    for (int s = 0; s < _sampleCount; s++)
                        max = Mathf.Max(max, signed ? Mathf.Abs(_series[c][f, s]) : _series[c][f, s]);
                }
                if (c == (int)Chart.Territories)
                    for (int s = 0; s < _sampleCount; s++) max = Mathf.Max(max, _curseTerritories[s]);
                if (signed)
                    for (int x = 0; x < ChartW; x++) px[(ChartH / 2) * ChartW + x] = new Color32(70, 70, 84, 255);

                for (int f = 0; f < MaxFactions; f++)
                {
                    if (!_factionLive[f]) continue;
                    DrawLine(px, s => _series[c][f, s], max, FactionColors.Get((Faction)f), signed);
                }
                if (c == (int)Chart.Territories)
                    DrawLine(px, s => _curseTerritories[s], max,
                             TheWaningBorder.Influence.PlayerInfluenceMap.CurseColor, false);

                _chartTex[c].SetPixels32(px);
                _chartTex[c].Apply(false, false);
            }
        }

        private void DrawLine(Color32[] px, System.Func<int, float> value, float max, Color32 col, bool signed)
        {
            int prevX = -1, prevY = 0;
            for (int s = 0; s < _sampleCount; s++)
            {
                int x = _sampleCount <= 1 ? 0 : s * (ChartW - 1) / (_sampleCount - 1);
                float v = value(s);
                int y = signed
                    ? Mathf.Clamp((int)(ChartH / 2f + v / max * (ChartH / 2f - 2f)), 0, ChartH - 1)
                    : Mathf.Clamp((int)(v / max * (ChartH - 2)), 0, ChartH - 1);
                if (prevX >= 0) DrawSegment(px, prevX, prevY, x, y, col);
                prevX = x; prevY = y;
            }
        }

        private static void DrawSegment(Color32[] px, int x0, int y0, int x1, int y1, Color32 col)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
            for (int i = 0; i <= steps; i++)
            {
                int x = x0 + (x1 - x0) * i / steps;
                int y = y0 + (y1 - y0) * i / steps;
                if (x < 0 || x >= ChartW || y < 0 || y >= ChartH) continue;
                px[y * ChartW + x] = col;
                if (y + 1 < ChartH) px[(y + 1) * ChartW + x] = col;
            }
        }

        // ── Text ────────────────────────────────────────────────────────

        private void RedrawHeader(EntityManager em)
        {
            var sb = new StringBuilder(256);
            double t = EntityWorld.DefaultGameObjectInjectionWorld.Time.ElapsedTime;
            sb.Append($"DEBUG BOARD   match {((int)t) / 60:00}:{((int)t) % 60:00}   ");
            sb.Append($"{_fps:0} fps ({1000f / Mathf.Max(1f, _fps):0.0} ms), worst frame {_worstMs:0} ms   ");
            sb.Append($"entities {em.UniversalQuery.CalculateEntityCountWithoutFiltering()}   ");
            var ls = TheWaningBorder.Multiplayer.LockstepManager.Instance;
            sb.Append(GameSettings.IsMultiplayer
                ? $"MULTIPLAYER tick {(ls != null ? ls.CurrentTick : 0)}" + (ls != null && ls.DesyncTick > 0 ? $"  <color=#FF5050>DESYNC @ {ls.DesyncTick}</color>" : "")
                : "single player");
            sb.AppendLine();

            int nodes = _curseNodeQuery.CalculateEntityCount();
            sb.Append($"<color=#C9A8FF>CURSE</color>  nodes {nodes}   territories {CurseTerritoryCount()}/{(RegionMap.Ready ? RegionMap.Count : 0)}   wrath ");
            bool any = false;
            for (int f = 0; f < MaxFactions; f++)
            {
                if (!_rows[f].Live) continue;
                int w = TheWaningBorder.Systems.Border.CurseWrath.LevelOf((Faction)f);
                sb.Append($"{ColorTag((Faction)f)}{(Faction)f}</color> {w}  ");
                any = true;
            }
            if (!any) sb.Append("-");
            _header.text = sb.ToString();
        }

        private void RedrawTable(EntityManager em)
        {
            Collect(em);
            var sb = new StringBuilder(1024);
            sb.AppendLine("<b>Faction        Age/Cult  Supplies         Iron             Veilstone        Veilsteel        Pop       Terr(cut)  Mil/Eco  Bld(+site/plan)  Outposts b/f/s  RP (pts)   Score (eco/strat/mil)  K/D</b>");
            for (int f = 0; f < MaxFactions; f++)
            {
                var r = _rows[f];
                if (!r.Live) continue;
                var fac = (Faction)f;
                string who = r.Ai ? "AI" : (fac == GameSettings.LocalPlayerFaction ? "You" : "Human");
                sb.Append($"{ColorTag(fac)}{fac,-7}</color>{who,-6} ");
                sb.Append($"{r.Era - 1}/{CultureLetter(r.Culture),-6} ");
                sb.Append(Res(r.Bank.Supplies, r.Net.Supplies));
                sb.Append(Res(r.Bank.Iron, r.Net.Iron));
                sb.Append(Res(r.Bank.Veilstone, r.Net.Veilstone));
                sb.Append(Res(r.Bank.Veilsteel, r.Net.Veilsteel));
                sb.Append($"{r.PopCur,3}/{r.PopMax,-5} ");
                string cut = r.Disconnected > 0 ? $"<color=#FF5050>({r.Disconnected})</color>" : "   ";
                sb.Append($"{r.Held,-4}{cut}  ");
                sb.Append($"{r.Military,3}/{r.Economy,-4} ");
                sb.Append($"{r.Buildings,3}(+{r.UnderConstruction}/{r.Plans})      ");
                sb.Append($"{r.OutBuy}/{r.OutForge}/{r.OutSell}            ");
                sb.Append($"{r.Rp} ({r.RpHave}/{r.RpNeed})".PadRight(11));
                // The Score (docs/Design/Score.md): MatchScoreSystem's latest sample.
                if (TheWaningBorder.Core.Diagnostics.MatchScore.TryGet(fac, out var score))
                    sb.Append($"{score.Score,6:F0} ({score.Economy:F0}/{score.Strategy:F0}/{score.Military:F0})  {score.Kd:F2}");
                sb.AppendLine();
            }
            _table.text = sb.ToString();
        }

        /// <summary>"1234 +190" — bank and net per minute, red when draining.</summary>
        private static string Res(int bank, float netPerMin)
        {
            int n = Mathf.RoundToInt(netPerMin);
            string net = n < 0 ? $"<color=#FF5050>{n}</color>" : n > 0 ? $"<color=#8CE878>+{n}</color>" : "±0";
            return $"{bank,6} {net,-6}   ";
        }

        private static string CultureLetter(byte c) => c switch
        {
            Cultures.Alanthor => "Alan",
            Cultures.Runai => "Runai",
            Cultures.Feraldis => "Fer",
            _ => "-",
        };

        private static string ColorTag(Faction f)
            => $"<color=#{ColorUtility.ToHtmlStringRGB(FactionColors.Get(f))}>";

        private void OnDestroy()
        {
            if (_chartTex == null) return;
            for (int c = 0; c < ChartCount; c++)
                if (_chartTex[c] != null) Destroy(_chartTex[c]);
        }
    }
}

#endif
