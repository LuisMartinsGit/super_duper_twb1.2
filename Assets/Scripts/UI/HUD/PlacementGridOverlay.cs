// PlacementGridOverlay.cs
// Shows a grid overlay and building footprint when placing buildings.
// Uses URP DecalProjector with tiling grid texture.
// Active only during BuildCommandPanel.IsPlacingBuilding.
// Location: Assets/Scripts/UI/HUD/PlacementGridOverlay.cs

using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.UI.Panels;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Shows a grid overlay around the cursor and a building footprint indicator
    /// when placing buildings. Uses URP DecalProjector for terrain projection.
    /// </summary>
    [DefaultExecutionOrder(920)]
    public class PlacementGridOverlay : MonoBehaviour
    {
        [Header("Grid Display")]
        [SerializeField] private Color gridColor = new Color(1f, 1f, 1f, 0.25f);
        [SerializeField] private Color validFootprintColor = new Color(0.3f, 1f, 0.3f, 0.35f);
        [SerializeField] private Color invalidFootprintColor = new Color(1f, 0.3f, 0.3f, 0.35f);
        [SerializeField] private float gridWorldSize = 16f;
        [SerializeField] private float projectionDepth = 10f;

        private GameObject _gridDecalObj;
        private DecalProjector _gridDecal;
        private Material _gridMat;
        private Texture2D _gridTex;

        private GameObject _footprintDecalObj;
        private DecalProjector _footprintDecal;
        private Material _footprintMat;
        private Texture2D _footprintTexValid;
        private Texture2D _footprintTexInvalid;

        void Start()
        {
            _gridTex = DecalHelper.CreateGridTexture();
            SetupGridDecal();
            SetupFootprintDecal();
        }

        void Update()
        {
            bool placing = BuilderCommandPanel.IsPlacingBuilding;
            _gridDecalObj.SetActive(placing);
            _footprintDecalObj.SetActive(placing);

            if (!placing) return;

            // Find the placement preview to get cursor position
            var preview = GameObject.Find("PlacementPreview");
            if (preview == null) return;

            var pos = preview.transform.position;
            float terrainY = TerrainUtility.GetHeight(pos.x, pos.z);

            // Position grid overlay centered on cursor, above terrain
            _gridDecalObj.transform.position = new Vector3(pos.x, terrainY + projectionDepth / 2f, pos.z);
            _gridDecal.size = new Vector3(gridWorldSize, projectionDepth, gridWorldSize);

            // Tiling: one texture repeat = one cell (1m)
            if (_gridMat != null)
            {
                _gridMat.SetTextureScale("Base_Map", new Vector2(gridWorldSize, gridWorldSize));
                _gridMat.SetTextureOffset("Base_Map", Vector2.zero);
            }

            // Position building footprint overlay
            UpdateFootprintDecal(pos, terrainY);
        }

        private void UpdateFootprintDecal(Vector3 pos, float terrainY)
        {
            // Get building size from current build context
            string buildId = BuilderCommandPanel.CurrentBuildId;
            if (string.IsNullOrEmpty(buildId)) return;

            var size = BuildingSizeConfig.GetSize(buildId);
            _footprintDecalObj.transform.position = new Vector3(pos.x, terrainY + projectionDepth / 2f, pos.z);
            _footprintDecal.size = new Vector3(size.x, projectionDepth, size.y);

            // Swap texture based on placement validity
            bool valid = BuilderCommandPanel.PlacementIsValid;
            _footprintDecal.material.SetTexture("Base_Map",
                valid ? _footprintTexValid : _footprintTexInvalid);
        }

        private void SetupGridDecal()
        {
            _gridDecalObj = new GameObject("PlacementGridDecal");
            _gridDecalObj.transform.SetParent(transform);
            _gridDecalObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            _gridDecal = _gridDecalObj.AddComponent<DecalProjector>();

            var shader = Shader.Find("Shader Graphs/Decal");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Decal");
            if (shader == null)
            {
                Debug.LogWarning("[PlacementGridOverlay] Decal shader not found");
                return;
            }

            _gridMat = new Material(shader);
            _gridMat.SetTexture("Base_Map", _gridTex);
            if (_gridMat.HasProperty("_BaseColor"))
                _gridMat.SetColor("_BaseColor", gridColor);
            _gridDecal.material = _gridMat;
            _gridDecal.drawDistance = 200f;

            _gridDecalObj.SetActive(false);
        }

        private void SetupFootprintDecal()
        {
            _footprintDecalObj = new GameObject("PlacementFootprintDecal");
            _footprintDecalObj.transform.SetParent(transform);
            _footprintDecalObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            _footprintDecal = _footprintDecalObj.AddComponent<DecalProjector>();

            var shader = Shader.Find("Shader Graphs/Decal");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Decal");
            if (shader == null) return;

            _footprintMat = new Material(shader);

            _footprintTexValid = MakeSolidTexture(validFootprintColor);
            _footprintTexInvalid = MakeSolidTexture(invalidFootprintColor);

            _footprintMat.SetTexture("Base_Map", _footprintTexValid);
            _footprintDecal.material = _footprintMat;
            _footprintDecal.drawDistance = 200f;

            _footprintDecalObj.SetActive(false);
        }

        private Texture2D MakeSolidTexture(Color color)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            var c32 = (Color32)color;
            for (int i = 0; i < 16; i++) pixels[i] = c32;
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        void OnDestroy()
        {
            if (_gridDecalObj) Destroy(_gridDecalObj);
            if (_footprintDecalObj) Destroy(_footprintDecalObj);
            if (_gridMat) Destroy(_gridMat);
            if (_footprintMat) Destroy(_footprintMat);
            if (_gridTex) Destroy(_gridTex);
            if (_footprintTexValid) Destroy(_footprintTexValid);
            if (_footprintTexInvalid) Destroy(_footprintTexInvalid);
        }
    }
}
