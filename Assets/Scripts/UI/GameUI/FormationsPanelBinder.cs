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

        void Start()
        {
            _root = GameUIKit.Rect(transform, "GameUI_FormationsPanel");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, 12f);
            _root.sizeDelta = new Vector2(332f, 46f);

            var bg = _root.gameObject.AddComponent<Image>();
            bg.color = GameUIKit.PanelBg;
            GameUIKit.PanelChrome(_root);

            GameUIKit.Text(_root, "Title", "FORMATION", 11f, GameUIKit.TextDim,
                TextAlignmentOptions.Center);
            var title = (RectTransform)_root.Find("Title");
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.pivot = new Vector2(0.5f, 1f);
            title.anchoredPosition = new Vector2(0f, -2f);
            title.sizeDelta = new Vector2(0f, 12f);

            string[] labels = { "Box", "Line", "Wedge", "Stag." };
            for (int i = 0; i < 4; i++)
            {
                var shape = (FormationShape)i;
                var btnRect = GameUIKit.Rect(_root, $"Btn_{labels[i]}");
                btnRect.anchorMin = btnRect.anchorMax = new Vector2(0f, 0f);
                btnRect.pivot = new Vector2(0f, 0f);
                btnRect.anchoredPosition = new Vector2(6f + i * 81f, 4f);
                btnRect.sizeDelta = new Vector2(77f, 26f);

                var img = btnRect.gameObject.AddComponent<Image>();
                img.color = GameUIKit.ButtonBg;
                _buttonBgs.Add(img);

                var btn = btnRect.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() =>
                    TheWaningBorder.Input.RTSInputManager.RequestFormationShape(shape));

                var label = GameUIKit.Text(btnRect, "Label", labels[i], 13f,
                    GameUIKit.TextMain, TextAlignmentOptions.Center);
                GameUIKit.Stretch((RectTransform)label.transform);
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
