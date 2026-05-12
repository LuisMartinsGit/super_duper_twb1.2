// File: Assets/Scripts/UI/HUD/VictoryProgressHUD.cs
// Per-culture node-victory tracker (spec §11 item 7). Shows at the top
// center of the screen:
//   - Total main nodes in the match.
//   - For each culture: claimed-count + hold-timer countdown when at full.
//
// Reads CrystalMainNodeTag entities (state) + NodeVictoryState singleton
// (hold timers). Lives on RuntimeManagers.

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.UI.HUD
{
    public class VictoryProgressHUD : MonoBehaviour
    {
        private const float PanelWidth = 460f;
        private const float PanelHeight = 96f;
        private const float TopMargin = 8f;

        private Unity.Entities.World _world;
        private EntityManager _em;
        private GUIStyle _boxStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;
        private GUIStyle _holdStyle;
        private bool _stylesInit;

        void OnGUI()
        {
            if (GameSettings.IsObserver && !true /* render for observers too */) return;
            EnsureWorld();
            if (_em == default(EntityManager)) return;
            if (!_stylesInit) InitStyles();

            int totalNodes = CountTotalNodes();
            if (totalNodes <= 0) return;  // no nodes yet — bootstrap incomplete

            int cleansedByAlanthor = 0;
            int convertedByRunai = 0;
            int destroyed = 0;
            CountClaimedStates(out cleansedByAlanthor, out convertedByRunai, out destroyed);

            float alanthorHold = 0f;
            float runaiHold = 0f;
            byte lastDestroyerCulture = Cultures.None;
            ReadVictoryState(out alanthorHold, out runaiHold, out lastDestroyerCulture);

            float x = (Screen.width - PanelWidth) * 0.5f;
            float y = TopMargin;
            GUI.Box(new Rect(x, y, PanelWidth, PanelHeight), GUIContent.none, _boxStyle);

            GUI.Label(new Rect(x + 12, y + 6, PanelWidth - 24, 22),
                $"Node Victory — {totalNodes} node{(totalNodes == 1 ? "" : "s")} on the map", _titleStyle);

            DrawRow(x + 12, y + 32,
                "Alanthor (Cleanse all)", cleansedByAlanthor, totalNodes,
                alanthorHold, NodeVictoryHoldTime);
            DrawRow(x + 12, y + 52,
                "Runai (Convert all)", convertedByRunai, totalNodes,
                runaiHold, NodeVictoryHoldTime);
            DrawRow(x + 12, y + 72,
                "Feraldis (Destroy all)", destroyed, totalNodes,
                0f, 0f,
                feraldis: true,
                lastDestroyerCulture: lastDestroyerCulture);
        }

        private void DrawRow(float x, float y, string label, int count, int total,
            float holdTimer, float holdTotal,
            bool feraldis = false, byte lastDestroyerCulture = 0)
        {
            string statusFragment;
            GUIStyle style = _lineStyle;
            if (feraldis)
            {
                bool feraldisPoised = count == total && lastDestroyerCulture == Cultures.Feraldis;
                statusFragment = feraldisPoised
                    ? "<color=#FF7C3C>!! NODE VICTORY !!</color>"
                    : (count == total
                        ? "<color=#FFD37C>all destroyed (no Feraldis kill credit)</color>"
                        : "");
            }
            else if (count == total)
            {
                float remaining = math.max(0f, holdTotal - holdTimer);
                statusFragment = remaining <= 0f
                    ? "<color=#FF7C3C>!! NODE VICTORY !!</color>"
                    : $"<color=#FFD37C>HOLDING — victory in {remaining:F1}s</color>";
                style = _holdStyle;
            }
            else
            {
                statusFragment = "";
            }

            string text = string.IsNullOrEmpty(statusFragment)
                ? $"{label}: {count}/{total}"
                : $"{label}: {count}/{total}   {statusFragment}";
            GUI.Label(new Rect(x, y, PanelWidth - 24, 18), text, style);
        }

        private int CountTotalNodes()
        {
            var q = _em.CreateEntityQuery(ComponentType.ReadOnly<CrystalMainNodeTag>());
            return q.CalculateEntityCount();
        }

        private void CountClaimedStates(out int cleansedByAlanthor, out int convertedByRunai, out int destroyed)
        {
            cleansedByAlanthor = convertedByRunai = destroyed = 0;
            var q = _em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalMainNodeTag>(),
                ComponentType.ReadOnly<CrystalNodeState>());
            using var arr = q.ToComponentDataArray<CrystalNodeState>(Allocator.Temp);
            for (int i = 0; i < arr.Length; i++)
            {
                switch (arr[i].State)
                {
                    case NodeState.Cleansed:
                        if (arr[i].OwnerCulture == Cultures.Alanthor) cleansedByAlanthor++;
                        break;
                    case NodeState.Converted:
                        if (arr[i].OwnerCulture == Cultures.Runai) convertedByRunai++;
                        break;
                    case NodeState.Destroyed:
                        destroyed++;
                        break;
                }
            }
        }

        private void ReadVictoryState(out float alanthor, out float runai, out byte lastDestroyerCulture)
        {
            alanthor = 0f; runai = 0f; lastDestroyerCulture = Cultures.None;
            var q = _em.CreateEntityQuery(ComponentType.ReadOnly<NodeVictoryState>());
            using var arr = q.ToComponentDataArray<NodeVictoryState>(Allocator.Temp);
            if (arr.Length == 0) return;
            alanthor = arr[0].AlanthorHoldTimer;
            runai = arr[0].RunaiHoldTimer;
            lastDestroyerCulture = arr[0].LastDestroyerCulture;
        }

        private void EnsureWorld()
        {
            if (_world != null && _world.IsCreated) return;
            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated) _em = _world.EntityManager;
        }

        private void InitStyles()
        {
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeColorTexture(new Color(0.06f, 0.07f, 0.11f, 0.90f));
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, richText = true,
            };
            _titleStyle.normal.textColor = new Color(0.95f, 0.90f, 0.70f);
            _lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
            _lineStyle.normal.textColor = new Color(0.86f, 0.86f, 0.86f);
            _holdStyle = new GUIStyle(_lineStyle) { fontStyle = FontStyle.Bold };
            _stylesInit = true;
        }

        private static Texture2D MakeColorTexture(Color c)
        {
            var t = new Texture2D(2, 2);
            var px = new Color[4]; for (int i = 0; i < 4; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // math shim — System.Math doesn't have a float `max` overload everywhere.
        private static class math
        {
            public static float max(float a, float b) => a > b ? a : b;
        }
    }
}
