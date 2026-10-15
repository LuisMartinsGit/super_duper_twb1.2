// ScoreBoardHUD.cs
// Every player's Score in the top-right corner (2026-10-11), one line each:
//
//   <player name>: <score>
//
// A human's name is their player name (the local player's profile name, or
// the lobby slot's name in multiplayer); an AI's is "AI <Difficulty>". The
// score is bold, in the player's colour. Highest score first. The number is
// MatchScore's (docs/Design/Score.md) — the same one the stats board and the
// Muster Rolls show; this only reads it. Hidden until the first score sample
// exists. Text only, no panel. Placement, sizes and refresh rate are
// ScoreBoardHUD.asset.

using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Diagnostics;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class ScoreBoardHUD : MonoBehaviour
    {
        static ScoreBoardHUDConfig Cfg => ScoreBoardHUDConfig.I;

        const int Factions = 8;

        RectTransform _root;
        TMP_Text _label;
        float _nextRefresh;
        readonly List<(Faction f, float score)> _rows = new List<(Faction, float)>(Factions);
        readonly StringBuilder _sb = new StringBuilder(512);

        void Awake()
        {
            var cfg = Cfg;
            _root = GameUIKit.Rect(transform, "ScoreBoard");
            _root.anchorMin = _root.anchorMax = Vector2.one;
            _root.pivot = Vector2.one;
            _root.anchoredPosition = cfg.offset;
            _root.sizeDelta = new Vector2(cfg.width, cfg.rowHeight + 2f * cfg.padding);
            // Text only — no panel behind it. A thin dark outline keeps it
            // readable over bright ground.
            _label = GameUIKit.Text(_root, "scores", "", cfg.fontSize, GameUIKit.TextMain,
                TextAlignmentOptions.TopRight, wrap: false);
            GameUIKit.Stretch(_label.rectTransform);
            _label.margin = new Vector4(cfg.padding, cfg.padding, cfg.padding, cfg.padding);
            _label.overflowMode = TextOverflowModes.Overflow;
            _label.outlineWidth = 0.15f;
            _label.outlineColor = new Color32(0, 0, 0, 200);

            _root.gameObject.SetActive(false);
        }

        void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, Cfg.refreshSeconds);
            Refresh();
        }

        void Refresh()
        {
            _rows.Clear();
            for (int f = 0; f < Factions; f++)
                if (MatchScore.TryGet((Faction)f, out var row))
                    _rows.Add(((Faction)f, row.Score));

            bool show = _rows.Count > 0;
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;

            _rows.Sort((a, b) => b.score.CompareTo(a.score));

            _sb.Clear();
            for (int i = 0; i < _rows.Count; i++)
            {
                var (f, score) = _rows[i];
                if (i > 0) _sb.Append('\n');
                _sb.Append(NameOf(f)).Append(": <b><color=#")
                   .Append(ColorUtility.ToHtmlStringRGB(FactionColors.Get(f))).Append('>')
                   .Append(Mathf.RoundToInt(score)).Append("</color></b>");
            }
            string text = _sb.ToString();
            if (_label.text != text) _label.text = text;

            var cfg = Cfg;
            float h = _rows.Count * cfg.rowHeight + 2f * cfg.padding;
            if (!Mathf.Approximately(_root.sizeDelta.y, h))
                _root.sizeDelta = new Vector2(cfg.width, h);
        }

        /// <summary>"AI Hard" for an AI seat; the player's name for a human.</summary>
        static string NameOf(Faction f)
        {
            int i = (int)f;
            var slots = LobbyConfig.Slots;
            bool hasSlot = slots != null && i >= 0 && i < slots.Length;

            if (!GameSettings.IsFactionHumanControlled(f))
            {
                var level = hasSlot ? slots[i].AIDifficulty : LobbyAIDifficulty.Normal;
                return Loc.T("AI") + " " + Loc.T(level.ToString());
            }

            if (f == GameSettings.LocalPlayerFaction && !GameSettings.IsSpectating)
            {
                string me = PlayerProfile.PlayerName;
                if (!string.IsNullOrWhiteSpace(me)) return me;
            }
            if (hasSlot && !string.IsNullOrWhiteSpace(slots[i].PlayerName)) return slots[i].PlayerName;
            return FactionColors.GetColorName(f);
        }
    }
}
