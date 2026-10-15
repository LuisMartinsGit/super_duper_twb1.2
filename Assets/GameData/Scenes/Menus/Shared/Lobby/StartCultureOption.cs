// StartCultureOption.cs
// The lobby's START WITH CULTURE option (2026-10-14), shared by the skirmish
// and multiplayer lobbies. It replaced the STARTING AGE dropdown:
//
//   OFF  every faction starts in Age 0 and picks its culture in play.
//   ON   every roster row shows a culture dropdown — RUNAI / FERALDIS /
//        ALANTHOR / OPEN. A faction given a culture starts the match already
//        aged into it (StartAgePromoter, Age 1); OPEN starts in Age 0 as usual.
//
// The choices are filtered through the edition (GameEdition.Allows), so a
// lobby never offers a culture the build cannot play. ONE source for both
// screens, for the reason LobbyOptions gives.

using System.Collections.Generic;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    public static class StartCultureOption
    {
        public const string RowDropdownName = "CultureDropdown";

        /// <summary>The cultures a row may pick, in menu order, OPEN last.
        /// Built per call: the edition can change in the editor.</summary>
        public static List<byte> Choices()
        {
            var list = new List<byte>(4);
            foreach (var c in new[] { Cultures.Runai, Cultures.Feraldis, Cultures.Alanthor })
                if (GameEdition.Allows(c)) list.Add(c);
            list.Add(Cultures.None);
            return list;
        }

        public static string Label(byte culture) => culture switch
        {
            Cultures.Runai    => "RUNAI",
            Cultures.Feraldis => "FERALDIS",
            Cultures.Alanthor => "ALANTHOR",
            _                 => "OPEN",
        };

        /// <summary>A slot's stored culture, put through the edition gate:
        /// a culture this build cannot play reads as OPEN.</summary>
        public static byte Gated(byte culture)
            => culture == Cultures.None || !GameEdition.Allows(culture) ? Cultures.None : culture;

        /// <summary>
        /// Publish the option into GameSettings at match start. ON with at
        /// least one cultured slot promotes those factions (StartAge Age1);
        /// every other case is an ordinary Age 0 start.
        /// </summary>
        public static void Apply(bool on)
        {
            System.Array.Clear(GameSettings.FactionStartCulture, 0, GameSettings.FactionStartCulture.Length);
            bool any = false;
            if (on)
                for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                {
                    var s = LobbyConfig.Slots[i];
                    if (s == null || s.Type == SlotType.Empty) continue;
                    int f = (int)s.Faction;
                    if (f < 0 || f >= GameSettings.FactionStartCulture.Length) continue;
                    byte c = Gated(s.StartCulture);
                    GameSettings.FactionStartCulture[f] = c;
                    any |= c != Cultures.None;
                }
            GameSettings.PerFactionStartCulture = on && any;
            GameSettings.StartAge = on && any ? SkirmishStartAge.Age1 : SkirmishStartAge.Age0;
            GameSettings.StartCulture = Cultures.Alanthor;   // unused while per-faction is on
        }

        /// <summary>
        /// Turn the authored STARTING AGE cell into the START WITH CULTURE
        /// toggle: relabel its caption, hide its dropdown and add an ON/OFF
        /// switch dressed like <paramref name="styleToggle"/>. Idempotent.
        /// </summary>
        public static void BuildToggle(TMP_Dropdown ageDropdown, Button styleToggle, TMP_Text styleState,
                                       out Button toggle, out TMP_Text state)
        {
            toggle = null; state = null;
            if (ageDropdown == null) return;
            var cell = ageDropdown.transform.parent;
            if (cell == null) return;

            var existing = cell.Find("CulturePill");
            if (existing != null)
            {
                toggle = existing.GetComponentInChildren<Button>(true);
                state = existing.Find("State")?.GetComponent<TMP_Text>();
                return;
            }

            ageDropdown.gameObject.SetActive(false);
            var caption = cell.GetComponentInChildren<TMP_Text>(true);
            if (caption != null) caption.text = Loc.T("START WITH CULTURE");

            float stateSize = styleState != null ? styleState.fontSize : 13f;
            var styleLe = styleToggle != null ? styleToggle.GetComponent<LayoutElement>() : null;
            float trackH = styleLe != null && styleLe.minHeight > 0f ? styleLe.minHeight : 26f;
            float trackW = styleLe != null && styleLe.minWidth > 0f ? styleLe.minWidth : 96f;

            var pillGo = new GameObject("CulturePill", typeof(RectTransform));
            pillGo.transform.SetParent(cell, false);
            var h = pillGo.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.spacing = 10f;

            var stateGo = new GameObject("State", typeof(RectTransform));
            stateGo.transform.SetParent(pillGo.transform, false);
            var stateText = stateGo.AddComponent<TextMeshProUGUI>();
            stateText.alignment = TextAlignmentOptions.MidlineLeft;
            stateText.fontSize = stateSize;
            stateText.text = Loc.T("OFF");
            stateText.raycastTarget = false;
            if (styleState != null) { stateText.font = styleState.font; stateText.color = styleState.color; }
            state = stateText;

            var trackGo = new GameObject("Track", typeof(RectTransform));
            trackGo.transform.SetParent(pillGo.transform, false);
            var img = trackGo.AddComponent<Image>();
            img.color = new Color(0.086f, 0.118f, 0.141f);
            toggle = trackGo.AddComponent<Button>();
            toggle.targetGraphic = img;
            var le = trackGo.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = trackH;
            le.minWidth = le.preferredWidth = trackW;

            var sw = styleToggle != null ? styleToggle.GetComponent<MenuToggleSwitch>() : null;
            if (sw != null)
                MenuToggleSwitch.Attach((RectTransform)trackGo.transform,
                    sw.TrackSprite, sw.OutlineSprite, sw.KnobSprite,
                    sw.KnobWidth, sw.KnobInset, sw.KnobPadding);
        }

        /// <summary>
        /// The row's culture dropdown, cloned from <paramref name="styleFrom"/>
        /// (a dropdown already in the row) and placed right after it, so it
        /// shares that column's look and width. Rows are rebuilt from the
        /// template, so the clone is made fresh for each row.
        /// </summary>
        public static TMP_Dropdown RowDropdown(Transform row, TMP_Dropdown styleFrom)
        {
            if (row == null || styleFrom == null) return null;
            var existing = row.Find(RowDropdownName);
            if (existing != null) return existing.GetComponent<TMP_Dropdown>();
            var go = Object.Instantiate(styleFrom.gameObject, styleFrom.transform.parent, false);
            go.name = RowDropdownName;
            go.transform.SetSiblingIndex(styleFrom.transform.GetSiblingIndex() + 1);
            var dd = go.GetComponent<TMP_Dropdown>();
            dd.onValueChanged.RemoveAllListeners();
            return dd;
        }

        /// <summary>Fill a row dropdown with the edition's choices and select
        /// <paramref name="current"/>; <paramref name="onPick"/> receives the culture byte.</summary>
        public static void Fill(TMP_Dropdown dd, byte current, System.Action<byte> onPick)
        {
            if (dd == null) return;
            var choices = Choices();
            dd.onValueChanged.RemoveAllListeners();
            dd.ClearOptions();
            var labels = new List<string>(choices.Count);
            foreach (var c in choices) labels.Add(Loc.T(Label(c)));
            dd.AddOptions(labels);
            int at = choices.IndexOf(Gated(current));
            dd.SetValueWithoutNotify(at < 0 ? choices.Count - 1 : at);
            if (onPick != null) dd.onValueChanged.AddListener(v => onPick(choices[v]));
        }
    }
}
