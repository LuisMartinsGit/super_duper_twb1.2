// File: Assets/Scripts/UI/HUD/CrystalDebugPanel.cs
// Debug overlay showing crystal faction economy, income, and per-node production.
// Toggle with F8 key.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    public class CrystalDebugPanel : MonoBehaviour
    {
        private bool _visible;
        private float _refreshTimer;
        private const float RefreshInterval = 0.5f;

        // Cached data
        private int _crystalBank;
        private float _incomePerSec;
        private int _nodeCount;
        private int _unitCount;
        private int _cadaverCount;
        private float _waveTimer;
        private float _waveInterval;
        private int _waveNumber;
        private NodeInfo[] _nodes = System.Array.Empty<NodeInfo>();

        private struct NodeInfo
        {
            public int Index;
            public int Level;
            public string TrainingUnit;
            public float TrainingTimeLeft;
            public float TrainingTimeTotal;
        }

        // Cached EntityQueries — lazily initialized on first RefreshData() call
        private EntityQuery _mainNodeCountQuery;
        private EntityQuery _subNodeQuery;
        private EntityQuery _unitQuery;
        private EntityQuery _cadaverQuery;
        private EntityQuery _waveQuery;
        private EntityQuery _nodeDetailQuery;
        private bool _queriesInit;

        // Styles — debug-specific colors (light-blue label + cyan value) cached locally;
        // panel bg + header pulled from Styles.cs.
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private bool _stylesInit;

        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F8))
                _visible = !_visible;

            if (!_visible) return;

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = RefreshInterval;
                RefreshData();
            }
        }

        private void InitQueries(EntityManager em)
        {
            if (_queriesInit) return;
            _queriesInit = true;

            _mainNodeCountQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CrystalMainNodeTag>());
            _subNodeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CrystalSubNodeTag>());
            _unitQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CrystalUnitTag>());
            _cadaverQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CadaverTag>());
            _waveQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CrystalWaveState>());
            _nodeDetailQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalMainNodeTag>(),
                ComponentType.ReadOnly<CrystalNode>(),
                ComponentType.ReadOnly<CrystalNodeLevel>()
            );
        }

        private void RefreshData()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // Lazily initialize cached queries
            InitQueries(em);

            // Crystal bank
            if (FactionEconomy.TryGetResources(em, Faction.White, out var res))
                _crystalBank = res.Crystal;

            // Count main nodes first (needed for income calc)
            _nodeCount = _mainNodeCountQuery.CalculateEntityCount();

            // Income estimate: flat per-node income
            int resourceNodes = 0;
            using var subTags = _subNodeQuery.ToComponentDataArray<CrystalSubNodeTag>(Allocator.Temp);
            for (int i = 0; i < subTags.Length; i++)
                if (subTags[i].Type == CrystalSubNodeType.Resource) resourceNodes++;
            // Matches CrystalIncomeSystem: 3/sec per main + 2/sec per resource node
            _incomePerSec = _nodeCount * 3f + resourceNodes * 2f;

            // Unit count
            _unitCount = _unitQuery.CalculateEntityCount();

            // Cadaver count
            _cadaverCount = _cadaverQuery.CalculateEntityCount();

            // Wave state
            if (_waveQuery.CalculateEntityCount() > 0)
            {
                using var waveEntities = _waveQuery.ToEntityArray(Allocator.Temp);
                var wave = em.GetComponentData<CrystalWaveState>(waveEntities[0]);
                _waveTimer = wave.WaveTimer;
                _waveInterval = wave.WaveInterval;
                _waveNumber = wave.WaveNumber;
            }

            // Per-node info
            using var nodeEntities = _nodeDetailQuery.ToEntityArray(Allocator.Temp);
            using var nodeData = _nodeDetailQuery.ToComponentDataArray<CrystalNode>(Allocator.Temp);
            using var nodeLevels = _nodeDetailQuery.ToComponentDataArray<CrystalNodeLevel>(Allocator.Temp);

            _nodes = new NodeInfo[nodeEntities.Length];
            for (int i = 0; i < nodeEntities.Length; i++)
            {
                var info = new NodeInfo
                {
                    Index = i + 1,
                    Level = nodeLevels[i].Value,
                    TrainingUnit = "Idle",
                    TrainingTimeLeft = 0f,
                    TrainingTimeTotal = 0f
                };

                if (em.HasComponent<CrystalTrainingState>(nodeEntities[i]))
                {
                    var ts = em.GetComponentData<CrystalTrainingState>(nodeEntities[i]);
                    if (ts.TrainingUnitType != 0)
                    {
                        info.TrainingUnit = ts.TrainingUnitType switch
                        {
                            1 => "Crystalling",
                            2 => "Veilstinger",
                            3 => "Godsplinter",
                            _ => "Unknown"
                        };
                        info.TrainingTimeLeft = ts.TimeRemaining;
                        info.TrainingTimeTotal = ts.TotalTime;
                    }
                }

                _nodes[i] = info;
            }
        }

        void OnGUI()
        {
            if (!_visible) return;
            Styles.Initialize();
            InitStyles();

            float w = 340f;
            float h = 200f + _nodes.Length * 50f;
            h = Mathf.Min(h, Screen.height - 40f);
            Rect panelRect = new Rect(Screen.width - w - 10, 10, w, h);

            GUI.Box(panelRect, GUIContent.none, Styles.PanelBox);
            GUILayout.BeginArea(new Rect(panelRect.x + 8, panelRect.y + 8, w - 16, h - 16));

            GUILayout.Label("CRYSTAL FACTION DEBUG", Styles.Header);
            GUILayout.Space(4);

            // Economy
            DrawRow("Crystal Bank:", $"{_crystalBank}");
            DrawRow("Income:", $"{_incomePerSec:F1}/sec ({_incomePerSec * 60:F0}/min)");
            DrawRow("Units:", $"{_unitCount}");
            DrawRow("Cadavers:", $"{_cadaverCount}");
            GUILayout.Space(6);

            // Waves
            GUILayout.Label("ATTACK WAVES", Styles.Header);
            DrawRow("Next wave in:", $"{Mathf.Max(0, _waveTimer):F0}s");
            DrawRow("Interval:", $"{_waveInterval:F0}s");
            DrawRow("Waves sent:", $"{_waveNumber}");
            GUILayout.Space(6);

            // Per-node
            GUILayout.Label($"NODES ({_nodeCount})", Styles.Header);
            for (int i = 0; i < _nodes.Length; i++)
            {
                var n = _nodes[i];
                GUILayout.Space(2);
                GUILayout.Label($"Node {n.Index} (Lv{n.Level})", _labelStyle);
                if (n.TrainingUnit == "Idle")
                {
                    GUILayout.Label("  Training: Idle", _valueStyle);
                }
                else
                {
                    float pct = n.TrainingTimeTotal > 0 ? (1f - n.TrainingTimeLeft / n.TrainingTimeTotal) * 100f : 100f;
                    GUILayout.Label($"  Training: {n.TrainingUnit} ({pct:F0}%, {n.TrainingTimeLeft:F1}s left)", _valueStyle);
                }
            }

            GUILayout.EndArea();
        }

        private void DrawRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(120));
            GUILayout.Label(value, _valueStyle);
            GUILayout.EndHorizontal();
        }

        private void InitStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;

            // Debug-specific text tints (light-blue label, cyan value) — no Styles match.
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.9f);
            _labelStyle.fontSize = 12;

            _valueStyle = new GUIStyle(GUI.skin.label);
            _valueStyle.normal.textColor = new Color(0.6f, 0.9f, 1f);
            _valueStyle.fontSize = 12;
        }
    }
}
