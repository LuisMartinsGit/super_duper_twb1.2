// ActionsPanelPrefabBinder.NodeButton.cs
// EXTRACTORS ARE BUILT FROM THE NODE (2026-11-13, docs/Design/Regions.md §4):
// with a Worker selected, pointing at a free resource node raises ONE button
// over it — the Gatherer's Hut line on a supply spot, the Mine on an iron
// deposit, the Veilstone Mine (Runai Sanctuary) on a veilstone outcropping.
// Clicking it places that building on the node and sends the selected Workers
// (WorkerCommandPanel.PlaceNow — the same checks and orders a ghost click
// runs). These buildings are not in the build palette. The same button rebuilds
// a fallen keystone — Fortress, Temple, landmark or sect building — over its
// rubble, for a share of its price (decision 69); the Fortress is never in the
// palette, nor any keystone whose rubble lies.
//
// The button is a copy of the panel's own authored action button, so it wears
// the same art, tint, icon, tooltip and afford / locked states (FillSlot). It
// hangs off the HUD canvas rather than the panel, as the first sibling, so
// every HUD panel draws over it.
//
// IT IS THE HEXAGONAL AGE-UP BUTTON (developer, 2026-11-13): a copy of the
// Synty radial button the special-building menu uses, handed in by
// GameUIManager from GameUICatalog.specialBuildingMenu. Only when that prefab
// is missing does it fall back to the panel's own square button inside a
// circular mask with a gold ring.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Data;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ActionsPanelPrefabBinder
    {
        /// <summary>Side of the node button, in canvas units.</summary>
        private const float NodeButtonSize = 152f;
        /// <summary>Ring width as a share of the button's radius.</summary>
        private const float NodeRingWidth = 0.09f;
        private static readonly Color NodeRingColor = new Color(0.86f, 0.70f, 0.32f, 1f);
        /// <summary>Metres above the node's base the button is centred on —
        /// under the node's reserve bar (NodeReserveBars.Lift).</summary>
        private const float NodeButtonLift = 1f;

        private Slot _nodeSlot;
        private RectTransform _nodeRect;
        private RectTransform _nodeCanvasRect;
        private Canvas _nodeRootCanvas;
        private Camera _nodeWorldCam;

        /// <summary>The builder's actions as of the last refresh; null in
        /// every other mode, which is what hides the node button.</summary>
        private List<ActionButton> _builderActions;

        // The node the button stands on, and the building it would raise.
        private bool _nodeHas;
        private string _nodeId;
        private float3 _nodePos;
        /// <summary>Re-evaluate and repaint: set by a target change and by
        /// the panel's own refresh tick (prices, bank, territory move).</summary>
        private bool _nodeDirty;
        private bool _nodeUsable;
        /// <summary>The node just ordered: no button there until the pointer
        /// leaves it (its plan does not make the node "taken").</summary>
        private bool _nodeDone;
        private float3 _nodeDonePos;

        /// <summary>Editor probes only: a ground point treated as the one
        /// under the pointer, so a capture can show the button without a
        /// mouse. Null in play.</summary>
        public static float3? HoverOverride;

        /// <summary>A pristine, inactive copy of an authored action button,
        /// made in Awake: the node button's fallback art.</summary>
        private Transform _nodeFallback;

        /// <summary>Side of the hexagonal button's own rect when the prefab
        /// does not say (Synty Button_FantasyMenus_Radial_Item_04).</summary>
        private const float HexNativeSize = 380f;
        /// <summary>Side of the building picture inside the hexagon, in the
        /// button's own (unscaled) units.</summary>
        private const float HexIconSize = 250f;

        /// <summary>Called from Init: builds the node button from the
        /// hexagonal age-up button, or from the fallback copy.</summary>
        private void BindNodeButton(Transform clone, GameObject hexTemplate)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null || (clone == null && hexTemplate == null))
            {
                if (clone != null) Destroy(clone.gameObject);
                return;
            }
            _nodeRootCanvas = canvas.rootCanvas;
            _nodeCanvasRect = canvas.transform as RectTransform;

            // Root (placed on the node) > Disc (the circular mask) > the
            // button; the ring is the root's second child, outside the mask.
            var rootGo = new GameObject("NodeBuildButton", typeof(RectTransform), typeof(LayoutElement));
            rootGo.GetComponent<LayoutElement>().ignoreLayout = true;
            _nodeRect = (RectTransform)rootGo.transform;
            _nodeRect.SetParent(canvas.transform, false);
            _nodeRect.SetAsFirstSibling();
            _nodeRect.anchorMin = _nodeRect.anchorMax = new Vector2(0.5f, 0.5f);
            _nodeRect.pivot = new Vector2(0.5f, 0.5f);
            _nodeRect.sizeDelta = new Vector2(NodeButtonSize, NodeButtonSize);

            if (hexTemplate != null)
            {
                if (clone != null) Destroy(clone.gameObject);
                var hex = Instantiate(hexTemplate, _nodeRect, false).transform;
                hex.name = "Button";
                hex.gameObject.SetActive(true);
                if (hex is RectTransform hr)
                {
                    float native = Mathf.Max(hr.rect.width, hr.sizeDelta.x);
                    if (native < 1f) native = HexNativeSize;
                    hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.5f);
                    hr.pivot = new Vector2(0.5f, 0.5f);
                    hr.anchoredPosition = Vector2.zero;
                    hr.localRotation = Quaternion.identity;
                    // Scaled as a whole: the Synty art is authored at one size.
                    float s = NodeButtonSize / native;
                    hr.localScale = new Vector3(s, s, 1f);
                }
                // The menu's own name label and its particle dressing are not
                // this button's: the tooltip names it, and it pops on a hover.
                foreach (var label in hex.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    label.gameObject.SetActive(false);
                foreach (var fx in hex.GetComponentsInChildren<ParticleSystem>(true))
                    fx.gameObject.SetActive(false);
                _nodeSlot = BindSlot(hex);
                // The menu's copy of the radial button carries no icon image
                // (its buttons are named, not pictured), so the building's
                // picture gets one: inside the hexagonal mask, under the frame.
                if (_nodeSlot.Icon == null)
                {
                    var frame = GameUIKit.FindDeep(hex, "SPR_Frame");
                    var holder = frame != null ? frame.parent : hex;
                    var iconGo = new GameObject("ICON", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    var irt = (RectTransform)iconGo.transform;
                    irt.SetParent(holder, false);
                    if (frame != null) irt.SetSiblingIndex(frame.GetSiblingIndex());
                    irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                    irt.pivot = new Vector2(0.5f, 0.5f);
                    irt.anchoredPosition = Vector2.zero;
                    irt.sizeDelta = new Vector2(HexIconSize, HexIconSize);
                    var icon = iconGo.GetComponent<Image>();
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    icon.enabled = false;
                    _nodeSlot.Icon = icon;
                }
                ClearSlot(_nodeSlot);
                rootGo.SetActive(false);
                return;
            }

            var disc = NodeImage(_nodeRect, "Disc", DiscSprite(0f), Color.white);
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            clone.SetParent(disc.transform, false);
            clone.name = "Button";
            if (clone is RectTransform cr)
            {
                cr.anchorMin = Vector2.zero;
                cr.anchorMax = Vector2.one;
                cr.pivot = new Vector2(0.5f, 0.5f);
                cr.offsetMin = cr.offsetMax = Vector2.zero;
                cr.localScale = Vector3.one;
            }

            NodeImage(_nodeRect, "Ring", DiscSprite(1f - NodeRingWidth), NodeRingColor);

            _nodeSlot = BindSlot(clone);

            // Everything the button draws must obey the disc. A graphic that
            // opted out of masking is opted back in; one whose material has
            // no stencil test cannot be clipped at all, so it is switched off
            // rather than left drawing a square around the circle.
            foreach (var g in clone.GetComponentsInChildren<MaskableGraphic>(true))
            {
                g.maskable = true;
                var mat = g.material;
                if (mat != null && mat != g.defaultMaterial && !mat.HasProperty("_Stencil"))
                {
                    TWBLog.Log($"[GameUI] Node button: '{g.name}' ({mat.shader.name}) cannot be masked - hidden.");
                    g.enabled = false;
                }
            }
            foreach (var nested in clone.GetComponentsInChildren<Canvas>(true))
                TWBLog.Log($"[GameUI] Node button: nested canvas on '{nested.name}' escapes the round mask.");

            ClearSlot(_nodeSlot);
            rootGo.SetActive(false);
        }

        /// <summary>A stretched, click-through image under <paramref name="parent"/>.</summary>
        private static Image NodeImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static readonly Dictionary<float, Sprite> _discSprites = new Dictionary<float, Sprite>();

        /// <summary>A white disc with a soft edge; <paramref name="inner"/>
        /// above zero hollows it into a ring (inner radius as a share of the
        /// outer). Drawn once per shape.</summary>
        private static Sprite DiscSprite(float inner)
        {
            if (_discSprites.TryGetValue(inner, out var cached) && cached != null) return cached;
            const int Size = 256;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "NodeButtonDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[Size * Size];
            float half = Size * 0.5f, rOut = half - 1f, rIn = inner * rOut;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rOut - d + 0.5f);
                    if (inner > 0f) a *= Mathf.Clamp01(d - rIn + 0.5f);
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            _discSprites[inner] = sprite;
            return sprite;
        }

        private void DestroyNodeButton()
        {
            if (_nodeRect != null) Destroy(_nodeRect.gameObject);
            _nodeSlot = null;
            _nodeRect = null;
        }

        private void HideNodeButton()
        {
            _nodeHas = false;
            if (_nodeSlot != null && _nodeSlot.Root.activeSelf) ClearSlot(_nodeSlot);
            if (_nodeRect != null && _nodeRect.gameObject.activeSelf) _nodeRect.gameObject.SetActive(false);
        }

        private Camera NodeUiCamera =>
            _nodeRootCanvas == null || _nodeRootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : _nodeRootCanvas.worldCamera;

        /// <summary>Every frame: which node is pointed at, and the button on it.</summary>
        private void UpdateNodeButton()
        {
            if (_nodeSlot == null || _nodeRect == null) return;

            var em = EM(out bool ok);
            if (!ok || _builderActions == null || GameSettings.IsSpectating
                || PresentationState.PlacingBuilding
                || TheWaningBorder.UI.World.AimingInput.HoldsPointer
                || SandboxPanel.IsPlacing)
            {
                HideNodeButton();
                _nodeDone = false;
                return;
            }

            Vector2 mouse = UnityEngine.Input.mousePosition;
            var faction = OwnFaction(em);

            // The pointer ON the button keeps the node it stands on: the
            // button covers the node, so the ground under the pointer is no
            // longer the question.
            bool onButton = !HoverOverride.HasValue && _nodeHas && _nodeSlot.Root.activeSelf
                && RectTransformUtility.RectangleContainsScreenPoint(_nodeRect, mouse, NodeUiCamera);
            if (onButton)
            {
                // ...unless the node was taken meanwhile (the rubble: unless
                // the rebuild was ordered or the rubble is gone).
                bool still = TheWaningBorder.Entities.KeystoneRubbles.IsKeystone(_nodeId)
                    ? TheWaningBorder.Entities.KeystoneRubbles.TryFind(em, faction, _nodeId, out _, out _)
                      && !TheWaningBorder.Entities.KeystoneRubbles.Has(em, faction, _nodeId)
                    : TerritoryOwnership.TrySnapToNode(em, _nodeId, _nodePos, out _);
                if (!still) { HideNodeButton(); return; }
            }
            else
            {
                bool overUi = UnityEngine.EventSystems.EventSystem.current != null
                    && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
                // A held button is a drag (selection box, camera): not a hover.
                bool held = UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1)
                    || UnityEngine.Input.GetMouseButton(2);
                Vector3 ground = default;
                if (HoverOverride.HasValue) { overUi = held = false; ground = HoverOverride.Value; }
                if (overUi || held
                    || (!HoverOverride.HasValue && !WorkerCommandPanel.TryMouseGround(out ground))
                    || !ResolveNodeAt(em, faction, (float3)ground, out string id, out float3 pos))
                {
                    HideNodeButton();
                    if (!overUi) _nodeDone = false;
                    return;
                }
                if (_nodeDone)
                {
                    if (math.distancesq(pos.xz, _nodeDonePos.xz) < 0.01f) { HideNodeButton(); return; }
                    _nodeDone = false;
                }
                if (!_nodeHas || id != _nodeId || math.distancesq(pos.xz, _nodePos.xz) > 0.01f)
                    _nodeDirty = true;
                _nodeHas = true;
                _nodeId = id;
                _nodePos = pos;
            }

            if (_nodeDirty && !PaintNodeButton(em, faction)) { HideNodeButton(); return; }
            _nodeDirty = false;
            if (!_nodeUsable) { HideNodeButton(); return; }

            // Lay it on the node.
            var cam = _nodeWorldCam != null ? _nodeWorldCam : (_nodeWorldCam = Camera.main);
            if (cam == null) { HideNodeButton(); return; }
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(_nodePos.x, _nodePos.y + NodeButtonLift, _nodePos.z));
            if (sp.z <= 0f
                || !RectTransformUtility.ScreenPointToWorldPointInRectangle(
                        _nodeCanvasRect, new Vector2(sp.x, sp.y), NodeUiCamera, out Vector3 at))
            { HideNodeButton(); return; }
            _nodeRect.position = at;
        }

        /// <summary>
        /// The free node within reach of this ground point and the building
        /// that stands on its kind: the supply extractor, or whichever ore
        /// extractor this faction raises there. Nearest wins.
        /// </summary>
        private static bool ResolveNodeAt(EntityManager em, Faction faction, float3 ground,
            out string id, out float3 pos)
        {
            id = null;
            pos = default;
            float best = float.MaxValue;
            if (TerritoryOwnership.TrySnapToNode(em, "GatherersHut", ground, out var hut))
            {
                best = math.distancesq(hut.xz, ground.xz);
                id = "GatherersHut";
                pos = hut;
            }
            if (TerritoryOwnership.ResolveExtractorAt(em, faction, ground, out var mineId, out var mine)
                && math.distancesq(mine.xz, ground.xz) < best)
            {
                best = math.distancesq(mine.xz, ground.xz);
                id = mineId;
                pos = mine;
            }
            // KEYSTONE RUBBLE (decision 69): the faction's own fallen
            // Fortress, Temple or landmark is rebuilt from the same button,
            // laid over its rubble — never from the palette.
            TheWaningBorder.Entities.KeystoneRubbles.All(em, faction, _rubbleHits);
            for (int i = 0; i < _rubbleHits.Count; i++)
            {
                var (rid, rubble) = _rubbleHits[i];
                float d2 = math.distancesq(rubble.xz, ground.xz);
                if (d2 > RubbleHoverRadius * RubbleHoverRadius || d2 >= best) continue;
                if (TheWaningBorder.Entities.KeystoneRubbles.Has(em, faction, rid)) continue;
                best = d2;
                id = rid;
                pos = rubble;
            }
            if (id == null) return false;
            pos.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos.x, pos.z);
            return true;
        }

        /// <summary>How near the rubble's centre the pointer raises the
        /// rebuild button, in metres — about the Fortress's footprint.</summary>
        private const float RubbleHoverRadius = 10f;

        private static readonly List<(string id, float3 pos)> _rubbleHits = new List<(string id, float3 pos)>();

        /// <summary>
        /// Fill the button for the current node. False when there is nothing
        /// to offer there: ground the player has never seen, a building this
        /// faction is not offered (culture, tutorial stage), or a node its
        /// own plan already stands on.
        /// </summary>
        private bool PaintNodeButton(EntityManager em, Faction faction)
        {
            _nodeUsable = false;
            if (!TheWaningBorder.Systems.Visibility.FogOfWarSystem.IsRevealedToFaction(faction, _nodePos))
                return false;

            int index = -1;
            for (int i = 0; i < _builderActions.Count && index < 0; i++)
                if (_builderActions[i].Id == _nodeId) index = i;
            ActionButton b;
            if (index >= 0) b = _builderActions[index];
            else if (TheWaningBorder.Entities.KeystoneRubbles.IsKeystone(_nodeId))
            {
                // A landmark is not in the builder's list after the age-up:
                // its rebuild button is made here, at the rebuild price (a
                // share of its own, BuildCosts.For).
                string name = TechCatalog.TryGetBuilding(_nodeId, out var def)
                    && !string.IsNullOrEmpty(def.name) ? Loc.T(def.name) : _nodeId;
                var price = TheWaningBorder.Data.BuildCosts.For(em, faction, _nodeId);
                bool afford = TheWaningBorder.Economy.FactionEconomy.CanAfford(em, faction, price);
                b = new ActionButton
                {
                    Id = _nodeId,
                    Label = name,
                    Tooltip = name + "\n" + Loc.T("Rebuild it on its rubble.")
                        + "\n" + Loc.T("Cost: ") + UIHelpers.FormatCostRich(price,
                            EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction)),
                    Cost = price,
                    Enabled = true,
                    CanAfford = afford,
                };
            }
            else return false;

            var refusal = WorkerCommandPanel.EvaluateNow(_nodeId, _nodePos);
            if (refusal == PlacementRefusal.Overlap) return false;
            if (refusal != PlacementRefusal.None)
            {
                b.Enabled = false;
                b.Tooltip = (b.Tooltip ?? b.Label) + "\n<color=#FF6A5E>"
                    + PlacementRefusalText.Of(refusal, _nodeId) + "</color>";
            }

            string id = _nodeId;
            float3 pos = _nodePos;
            FillSlot(_nodeSlot, b, BuildingCategory(id), null,
                b.Enabled ? (System.Action)(() => NodeButtonClicked(id, pos)) : null, em);
            if (!_nodeRect.gameObject.activeSelf) _nodeRect.gameObject.SetActive(true);
            _nodeUsable = true;
            return true;
        }

        private void NodeButtonClicked(string id, float3 pos)
        {
            if (!WorkerCommandPanel.PlaceNow(id, pos)) { _nodeDirty = true; return; }
            _nodeDone = true;
            _nodeDonePos = pos;
            HideNodeButton();
        }
    }
}
