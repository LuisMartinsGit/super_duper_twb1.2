// InputModes.cs
// The armed aiming modes (attack-move / patrol) — the state the hotkey layer
// writes, the click layer consumes, and the banner reports.
// Part of: Input/ — split out of RTSInputManager.

using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Attack-move and patrol are ARMED by a key and SPENT by the next
    /// right-click, so the two halves of the input layer have to share them.
    /// They live here rather than on either side: putting them on the hotkey
    /// class would make the click layer reach into the keyboard, and vice
    /// versa.
    ///
    /// Mutually exclusive by construction — arming one disarms the other,
    /// which is why these are methods and not two public bools.
    ///
    /// The on-screen banner is here too. It is not a separate feature, it is
    /// this state made visible, and keeping it together is what lets the
    /// labels and the plate live in InputModes.asset instead of being spelled
    /// out inside somebody else's OnGUI.
    /// </summary>
    public sealed class InputModes
    {
        private InputModesConfig _cfg;
        private InputModesConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<InputModesConfig>());

        /// <summary>A + right-click: move, engaging anything on the way.</summary>
        public bool AttackMove { get; private set; }

        /// <summary>P + right-click: patrol between here and there.</summary>
        public bool Patrol { get; private set; }

        /// <summary>True while either mode is waiting for its click.</summary>
        public bool AnyArmed => AttackMove || Patrol;

        public void ArmAttackMove() { AttackMove = true; Patrol = false; }

        public void ArmPatrol() { Patrol = true; AttackMove = false; }

        /// <summary>
        /// Drop both modes. Returns true when something was actually armed —
        /// the Esc cascade uses that to decide whether Esc was consumed here
        /// or should fall through to clearing the selection.
        /// </summary>
        public bool Disarm()
        {
            if (!AnyArmed) return false;
            AttackMove = false;
            Patrol = false;
            return true;
        }

        /// <summary>
        /// Centred banner naming the armed mode. Called from the pump's OnGUI;
        /// draws nothing while both modes are idle.
        /// </summary>
        public void DrawBanner()
        {
            if (!AnyArmed) return;

            var cfg = Cfg;
            if (cfg == null) return;

            string label = AttackMove ? cfg.attackMoveBanner : cfg.patrolBanner;
            var rect = new Rect((Screen.width - cfg.bannerWidth) * 0.5f,
                                cfg.bannerTopMargin, cfg.bannerWidth, cfg.bannerHeight);

            GUI.color = cfg.bannerBackground;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = cfg.bannerTextColor;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = cfg.bannerFontSize
            };
            style.normal.textColor = cfg.bannerTextColor;

            GUI.Label(rect, label, style);
            GUI.color = Color.white;
        }
    }
}
