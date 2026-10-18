// EntityTargeting.cs
// The entity pick for single-target spells — docs/Design/Spells.md §6.
//
// The ground ring (GroundTargeting) is for spells that land on a PATCH of
// map. A spell cast ON one unit or building used to borrow that ring too, and
// then hit whatever stood nearest the click — which read as an area spell and
// could land on the wrong thing. This tool picks the entity itself instead:
//
//   * every frame the cursor ray picks the entity under it (ScreenPick, the
//     same pick a selection click uses — RTSInputManager's hover is frozen
//     while a spell is aimed, so the tool asks for itself);
//   * the hovered entity is outlined on the ground, green when the spell may
//     be cast on it and red when not, with a hint by the cursor;
//   * an enemy the local player cannot SEE is not pickable (fog);
//   * left-click on a legal target confirms; on the ground or a wrong target
//     it explains why and keeps aiming (nothing is spent); right-click / Esc
//     cancels. The confirming press is swallowed (AimingInput) so it neither
//     selects what was clicked nor reaches the order input.
//
// The legality test is the caller's (it knows the spell): the card's
// AbilityTargetRules, or the sect power's CheckTarget — the same functions
// the simulation re-runs before it charges the cooldown.

using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.UI.World
{
    public class EntityTargeting : MonoBehaviour
    {
        private static EntityTargeting _instance;

        /// <summary>True while a single-target spell is being aimed; input
        /// handlers stand down (read through AimingInput.HoldsPointer).</summary>
        public static bool IsActive { get; private set; }

        /// <summary>Legality test: null when the spell may be cast on the
        /// entity, else the (English) reason it may not.</summary>
        public delegate string Refusal(EntityManager em, Entity target);

        private static readonly Color ValidTint = new Color(0.35f, 1f, 0.45f, 0.9f);
        private static readonly Color InvalidTint = new Color(1f, 0.3f, 0.25f, 0.9f);

        private CastTargetKind _kind;
        private Refusal _refusal;
        private Action<Entity> _onConfirm;
        private DecalProjector _outline;
        private bool _armedThisFrame;

        // Hover state, read by OnGUI.
        private Entity _hovered = Entity.Null;
        private string _hoverReason;

        /// <summary>
        /// Enter the entity pick for a spell cast on <paramref name="kind"/>.
        /// <paramref name="refusal"/> judges a hovered entity;
        /// <paramref name="onConfirm"/> receives the clicked legal one.
        /// </summary>
        public static void Begin(CastTargetKind kind, Refusal refusal, Action<Entity> onConfirm)
        {
            GroundTargeting.Cancel();
            EnsureInstance();
            _instance._kind = kind;
            _instance._refusal = refusal;
            _instance._onConfirm = onConfirm;
            _instance._hovered = Entity.Null;
            _instance._hoverReason = null;
            _instance._armedThisFrame = true;
            IsActive = true;
        }

        public static void Cancel()
        {
            if (_instance == null) return;
            _instance._onConfirm = null;
            _instance._refusal = null;
            _instance._hovered = Entity.Null;
            if (_instance._outline != null) _instance._outline.gameObject.SetActive(false);
            IsActive = false;
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("EntityTargeting");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<EntityTargeting>();
        }

        private void Update()
        {
            if (!IsActive) return;

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetMouseButtonDown(1))
            {
                AimingInput.SwallowCurrentPress();
                Cancel();
                return;
            }

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { Cancel(); return; }
            var em = world.EntityManager;

            bool overUi = UnityEngine.EventSystems.EventSystem.current != null
                          && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

            _hovered = overUi ? Entity.Null : PickVisible(em);
            _hoverReason = _hovered != Entity.Null ? _refusal?.Invoke(em, _hovered) : null;
            ShowOutline(em);

            if (UnityEngine.Input.GetMouseButtonDown(0) && !_armedThisFrame && !overUi)
            {
                if (_hovered != Entity.Null && _hoverReason == null)
                {
                    var target = _hovered;
                    var cb = _onConfirm;
                    AimingInput.SwallowCurrentPress();
                    Cancel();
                    cb?.Invoke(target);
                }
                else
                {
                    // The ground, or the wrong thing: say why, keep aiming,
                    // spend nothing.
                    string why = _hoverReason ?? CastTargetRules.Prompt(_kind);
                    TheWaningBorder.UI.Ingame.PlayerNotificationSystem.NotifyError(Loc.T(why));
                }
            }

            _armedThisFrame = false;
        }

        /// <summary>The entity under the cursor, unless it is a hostile one the
        /// local player cannot see right now.</summary>
        private static Entity PickVisible(EntityManager em)
        {
            var e = TheWaningBorder.Input.ScreenPick.EntityUnderMouse(Physics.DefaultRaycastLayers, em);
            if (e == Entity.Null || !em.Exists(e)) return Entity.Null;
            if (!em.HasComponent<LocalTransform>(e) || !em.HasComponent<FactionTag>(e)) return Entity.Null;
            // Only units and buildings are spell targets; a resource node, a
            // plan or a decal reads as the ground.
            if (!em.HasComponent<UnitTag>(e) && !em.HasComponent<BuildingTag>(e)) return Entity.Null;
            var local = GameSettings.GetLocalFaction();
            var owner = em.GetComponentData<FactionTag>(e).Value;
            if (!Alliances.AreAllied(local, owner)
                && !TheWaningBorder.Systems.Visibility.FogOfWarSystem.IsVisibleToFaction(
                       local, em.GetComponentData<LocalTransform>(e).Position))
                return Entity.Null;
            return e;
        }

        /// <summary>The hovered entity's footprint, outlined on the ground: a
        /// rectangle for a building, a ring for a unit.</summary>
        private void ShowOutline(EntityManager em)
        {
            if (_hovered == Entity.Null)
            {
                if (_outline != null) _outline.gameObject.SetActive(false);
                return;
            }
            if (_outline == null) _outline = GroundDecals.Rent(GroundDecals.Ring(), Color.white);

            var ext = TargetGeometry.Extent(em, _hovered);
            var tint = _hoverReason == null ? ValidTint : InvalidTint;
            var at = new Vector3(ext.Center.x, ext.Center.y, ext.Center.z);
            if (ext.IsBox)
            {
                GroundDecals.SetShape(_outline, GroundDecals.RectContour(), tint);
                GroundDecals.Place(_outline, at, ext.HalfW * 2f + 0.8f, ext.HalfH * 2f + 0.8f);
            }
            else
            {
                GroundDecals.SetShape(_outline, GroundDecals.Ring(), tint);
                GroundDecals.Place(_outline, at, math.max(1.4f, ext.Radius * 2f + 0.8f));
            }
            _outline.gameObject.SetActive(true);
        }

        private GUIStyle _hint;

        private void OnGUI()
        {
            if (!IsActive) return;
            if (_hint == null)
            {
                _hint = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            }
            string text = _hovered != Entity.Null && _hoverReason != null
                ? Loc.T(_hoverReason)
                : Loc.T(CastTargetRules.Prompt(_kind));
            Color col = _hovered == Entity.Null ? Color.white
                      : _hoverReason == null ? ValidTint : InvalidTint;

            Vector2 m = UnityEngine.Input.mousePosition;
            var r = new Rect(m.x + 18f, Screen.height - m.y + 6f, 320f, 24f);
            _hint.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _hint);
            _hint.normal.textColor = col;
            GUI.Label(r, text, _hint);
        }
    }
}
