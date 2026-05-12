// File: Assets/Scripts/UI/HUD/GodPowerHUD.cs
// IMGUI HUD for the faction god power (spec §6.2 + refinement #6).
// Reads the local player's GodPowerState + Temple GlowStored each frame
// and renders a small panel in the bottom-right corner:
//   - Status line: "Ready" / cooldown countdown
//   - Stored Glow indicator (drives the CDR formula)
//   - "Cast (G)" button — pressing G or clicking the button enters
//     targeting mode; the next left-click on the world drops the cast at
//     that point and exits targeting mode. Right-click / Esc cancels.
//
// The actual cast flows through CommandRouter.IssueGodPower; this panel
// only collects the target position. Faction-bias resolution (Alanthor
// Sanctify vs Feraldis Pyre vs Runai Veil Ward) is GodPowerSystem's job.

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands;

namespace TheWaningBorder.UI.HUD
{
    public class GodPowerHUD : MonoBehaviour
    {
        /// <summary>True when the player is aiming their god power. RTSInputManager checks this to suppress normal click handling.</summary>
        public static bool TargetingMode { get; private set; }

        private const KeyCode CastHotkey = KeyCode.G;
        private const float PanelWidth = 220f;
        private const float PanelHeight = 90f;
        private const float Padding = 12f;

        private Unity.Entities.World _world;
        private EntityManager _em;
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _aimingStyle;
        private bool _stylesInit;

        void Update()
        {
            if (TargetingMode)
            {
                // Esc / right-click cancel.
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetMouseButtonDown(1))
                {
                    TargetingMode = false;
                    return;
                }

                // Left-click resolves the cast against world position.
                if (UnityEngine.Input.GetMouseButtonDown(0))
                {
                    if (TryGetGroundClick(out float3 pos))
                    {
                        EnsureWorld();
                        if (_em != default(EntityManager))
                        {
                            CommandRouter.IssueGodPower(_em, GameSettings.LocalPlayerFaction, pos,
                                CommandSource.LocalPlayer);
                        }
                    }
                    TargetingMode = false;
                }
                return;
            }

            // G hotkey enters targeting mode (if cooldown ready).
            if (UnityEngine.Input.GetKeyDown(CastHotkey))
            {
                EnsureWorld();
                if (IsReady()) TargetingMode = true;
            }
        }

        void OnGUI()
        {
            if (GameSettings.IsObserver) return;
            EnsureWorld();
            if (_em == default(EntityManager)) return;
            if (!_stylesInit) InitStyles();

            float remaining;
            int storedGlow;
            int castCount;
            if (!ReadState(out remaining, out storedGlow, out castCount)) return;

            float x = Screen.width - PanelWidth - Padding;
            float y = Screen.height - PanelHeight - Padding;
            GUI.Box(new Rect(x, y, PanelWidth, PanelHeight), GUIContent.none, _boxStyle);

            float ix = x + 10;
            float iy = y + 8;

            string statusLine = TargetingMode
                ? "<color=#FFD37C>AIM — click ground</color>"
                : (remaining <= 0f ? "<color=#9CFF9C>READY</color>" : $"Cooldown: {remaining:F1}s");
            GUI.Label(new Rect(ix, iy, PanelWidth - 20, 22),
                $"God Power  ({statusLine})", _labelStyle);

            GUI.Label(new Rect(ix, iy + 22, PanelWidth - 20, 20),
                $"Stored Glow: {storedGlow}    Casts: {castCount}", _labelStyle);

            var btnRect = new Rect(ix, iy + 50, PanelWidth - 20, 22);
            bool ready = remaining <= 0f;
            GUI.enabled = ready && !TargetingMode;
            if (GUI.Button(btnRect, ready ? "Cast (G)" : "On Cooldown",
                TargetingMode ? _aimingStyle : _btnStyle))
            {
                TargetingMode = true;
            }
            GUI.enabled = true;
        }

        private bool ReadState(out float remaining, out int storedGlow, out int castCount)
        {
            remaining = 0f; storedGlow = 0; castCount = 0;
            var bankQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<GodPowerState>());
            using var ents = bankQuery.ToEntityArray(Allocator.Temp);
            using var tags = bankQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var gps = bankQuery.ToComponentDataArray<GodPowerState>(Allocator.Temp);
            bool found = false;
            for (int i = 0; i < ents.Length; i++)
            {
                if (tags[i].Value != GameSettings.LocalPlayerFaction) continue;
                remaining = gps[i].CooldownRemaining;
                castCount = gps[i].CastCount;
                found = true;
                break;
            }
            if (!found) return false;

            var templeQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<GlowStored>());
            using var tEnts = templeQuery.ToEntityArray(Allocator.Temp);
            using var tFacs = templeQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var tStored = templeQuery.ToComponentDataArray<GlowStored>(Allocator.Temp);
            for (int i = 0; i < tEnts.Length; i++)
            {
                if (tFacs[i].Value != GameSettings.LocalPlayerFaction) continue;
                storedGlow += tStored[i].Amount;
            }
            return true;
        }

        private bool IsReady()
        {
            if (_em == default(EntityManager)) return false;
            var bankQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<GodPowerState>());
            using var tags = bankQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var gps = bankQuery.ToComponentDataArray<GodPowerState>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value != GameSettings.LocalPlayerFaction) continue;
                return gps[i].CooldownRemaining <= 0f;
            }
            return false;
        }

        private void EnsureWorld()
        {
            if (_world != null && _world.IsCreated) return;
            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated) _em = _world.EntityManager;
        }

        private bool TryGetGroundClick(out float3 pos)
        {
            pos = float3.zero;
            var cam = Camera.main;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            // Try a horizontal plane at y=0; good enough for the procedural terrain.
            var plane = new UnityEngine.Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float dist))
            {
                var p = ray.GetPoint(dist);
                pos = new float3(p.x, p.y, p.z);
                return true;
            }
            return false;
        }

        private void InitStyles()
        {
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeColorTexture(new Color(0.08f, 0.10f, 0.14f, 0.92f));
            _labelStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };
            _labelStyle.normal.textColor = new Color(0.92f, 0.86f, 0.62f);
            _btnStyle = new GUIStyle(GUI.skin.button);
            _aimingStyle = new GUIStyle(GUI.skin.button);
            _aimingStyle.normal.textColor = new Color(1.0f, 0.85f, 0.30f);
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
    }
}
