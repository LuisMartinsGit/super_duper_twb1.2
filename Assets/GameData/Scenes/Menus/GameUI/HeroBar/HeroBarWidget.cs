// HeroBarWidget.cs
// The small bar every hero wears, friend or foe (docs/Design/Heroes.md §2):
// health, XP toward the next level, and the level. The prefab beside this file is
// authored from the Synty Fantasy Warrior kit; FloatingHealthBars pools and places it.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class HeroBarWidget : MonoBehaviour
    {
        [SerializeField] private RectTransform hpFill;
        [SerializeField] private Image hpFillImage;
        [SerializeField] private RectTransform xpFill;
        [SerializeField] private TMP_Text levelText;

        private int _level = -1;
        private float _hp = -1f, _xp = -1f;

        public void Place(Vector2 screen)
        {
            var r = (RectTransform)transform;
            if (r.anchoredPosition != screen) r.anchoredPosition = screen;
        }

        public void Show(float hp, float xp, int level, Color owner)
        {
            hpFillImage.color = owner;
            if (hp != _hp) { _hp = hp; hpFill.anchorMax = new Vector2(hp, 1f); }
            if (xp != _xp) { _xp = xp; xpFill.anchorMax = new Vector2(xp, 1f); }
            if (level != _level) { _level = level; levelText.text = level.ToString(); }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
