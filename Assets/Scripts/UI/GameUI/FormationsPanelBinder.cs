// FormationsPanelBinder.cs
// Code-built formations strip (no authored prefab yet): four buttons for the
// FormationShape set (Box / Line / Wedge / Staggered). Mirrors the X-key
// cycle in RTSInputManager — clicking a shape re-slots the current selection
// immediately, and the highlight follows shape changes from EITHER path.
// Visible only while the local player has movable units selected.

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.UI.GameUI
{
    public class FormationsPanelBinder : MonoBehaviour
    {
        private const float PollInterval = 0.2f;

        private RectTransform _root;
        private readonly List<Image> _buttonBgs = new();
        private float _nextPoll;
        private EntityManager _em;
        private bool _emReady;

        // Sizes are CANVAS units on a 3840x2160 reference, i.e. roughly half
        // these numbers in screen pixels at 1080p. The strip was originally
        // built at 332x46 with 11-13pt text, which rendered as a 166px sliver
        // of ~6px lettering — present, unreadable.
        private const float PanelWidth = 700f;
        private const float PanelHeight = 108f;
        private const float ButtonWidth = 162f;
        private const float ButtonHeight = 58f;
        private const float ButtonGap = 8f;

        private static readonly string[] Labels = { "Box", "Line", "Wedge", "Stagger" };
        private static readonly string[] Tips =
        {
            "<b>Box</b>\nCompact rectangle. The all-round default — good for moving a "
                + "mixed group without exposing a flank.",
            "<b>Line</b>\nWide, shallow rank. Maximises how many units can shoot or "
                + "engage at once; fragile if hit from the side.",
            "<b>Wedge</b>\nArrowhead. Concentrates the leading edge for a charge that "
                + "punches through a line.",
            "<b>Stagger</b>\nOffset rows. Spreads the group out so area damage and "
                + "siege hit fewer units at a time.",
        };

        void Start()
        {
            _root = GameUIKit.Rect(transform, "GameUI_FormationsPanel");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, 24f);
            _root.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            var bg = _root.gameObject.AddComponent<Image>();
            bg.color = GameUIKit.PanelBg;
            GameUIKit.PanelChrome(_root);

            var title = GameUIKit.Text(_root, "Title", Loc.T("FORMATION  (X to cycle)"), 20f,
                GameUIKit.TextDim, TextAlignmentOptions.Center);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -6f);
            titleRect.sizeDelta = new Vector2(0f, 24f);

            float left = (PanelWidth - (4f * ButtonWidth + 3f * ButtonGap)) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                var shape = (FormationShape)i;
                var btnRect = GameUIKit.Rect(_root, $"Btn_{Labels[i]}");
                btnRect.anchorMin = btnRect.anchorMax = new Vector2(0f, 0f);
                btnRect.pivot = new Vector2(0f, 0f);
                btnRect.anchoredPosition = new Vector2(left + i * (ButtonWidth + ButtonGap), 10f);
                btnRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

                var img = btnRect.gameObject.AddComponent<Image>();
                img.color = GameUIKit.ButtonBg;
                _buttonBgs.Add(img);

                var btn = btnRect.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() =>
                    TheWaningBorder.Input.RTSInputManager.RequestFormationShape(shape));
                // Labels[]/Tips[] stay English (GameObject names key off
                // Labels); translation happens here, at render.
                UITooltip.Bind(btnRect.gameObject, Loc.T(Tips[i]));

                var label = GameUIKit.Text(btnRect, "Label", Loc.T(Labels[i]), 24f,
                    GameUIKit.TextMain, TextAlignmentOptions.Center, wrap: false);
                GameUIKit.Stretch(label.rectTransform);
            }

            _root.gameObject.SetActive(false);
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPoll || _root == null) return;
            _nextPoll = Time.unscaledTime + PollInterval;

            if (!_emReady)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return;
                _em = world.EntityManager;
                _emReady = true;
            }

            bool show = false;
            var sel = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (sel != null)
            {
                for (int i = 0; i < sel.Count; i++)
                {
                    if (_em.Exists(sel[i]) && _em.HasComponent<MoveSpeed>(sel[i]))
                    { show = true; break; }
                }
            }
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;

            var current = (int)TheWaningBorder.Input.RTSInputManager.CurrentFormationShape;
            for (int i = 0; i < _buttonBgs.Count; i++)
                _buttonBgs[i].color = i == current ? GameUIKit.BarGold : GameUIKit.ButtonBg;
        }
    }
}
