// Phase 0 — UI Toolkit pipeline smoke test.
//
// Setup (one-time):
//   1. Menu: Tools > Waning Border > UI > Create Phase 0 PanelSettings
//      → creates Assets/UI/Settings/HudPanelSettings.asset with a theme assigned.
//   2. Add this component to any GameObject in the scene.
//   3. Inspector: assign Assets/UI/Documents/Phase0Demo.uxml to "Demo Uxml"
//      and Assets/UI/Settings/HudPanelSettings.asset to "Panel Settings".
//   4. Hit Play.
//
// Phase 1 replaces this with the real GameplayUIController.

using UnityEngine;
using UnityEngine.UIElements;

namespace TheWaningBorder.UI
{
    [DisallowMultipleComponent]
    public sealed class Phase0DemoMount : MonoBehaviour
    {
        [Tooltip("Assign Assets/UI/Documents/Phase0Demo.uxml.")]
        [SerializeField] private VisualTreeAsset demoUxml;

        [Tooltip("Assign Assets/UI/Settings/HudPanelSettings.asset (created by " +
                 "Tools > Waning Border > UI > Create Phase 0 PanelSettings).")]
        [SerializeField] private PanelSettings panelSettings;

        private UIDocument _document;

        private void OnEnable()
        {
            if (demoUxml == null)
            {
                Debug.LogError("[Phase0DemoMount] demoUxml is not assigned. " +
                               "Assign Assets/UI/Documents/Phase0Demo.uxml in the Inspector.");
                return;
            }
            if (panelSettings == null)
            {
                Debug.LogError("[Phase0DemoMount] panelSettings is not assigned. " +
                               "Run the menu Tools > Waning Border > UI > Create Phase 0 PanelSettings, " +
                               "then assign the resulting asset.");
                return;
            }

            _document = GetComponent<UIDocument>();
            if (_document == null)
                _document = gameObject.AddComponent<UIDocument>();

            _document.panelSettings = panelSettings;
            _document.visualTreeAsset = demoUxml;
            _document.sortingOrder = 0;
        }
    }
}
