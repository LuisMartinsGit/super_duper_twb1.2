// VfxUvScroll.cs
// Scrolls a renderer material's texture over time — the URP-correct stand-in
// for Lana Studio's UVscroll (which writes _MainTex; URP particle and lit
// shaders sample _BaseMap). Field names match UVscroll so effects imported by
// PackVfxImport keep their authored speeds. Per-instance authored data, set on
// the effect prefab, so it carries no config asset.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public class VfxUvScroll : MonoBehaviour
    {
        public int materialId;
        public float scrollSpeedX;
        public float scrollSpeedY;

        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private Vector4 _st;

        void Start()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer == null) { enabled = false; return; }
            _block = new MaterialPropertyBlock();
            var mats = _renderer.sharedMaterials;
            var mat = materialId < mats.Length ? mats[materialId] : null;
            _st = mat != null && mat.HasProperty(BaseMapST) ? mat.GetVector(BaseMapST) : new Vector4(1, 1, 0, 0);
        }

        void Update()
        {
            _renderer.GetPropertyBlock(_block, materialId);
            _block.SetVector(BaseMapST, new Vector4(_st.x, _st.y,
                _st.z + Time.time * scrollSpeedX, _st.w + Time.time * scrollSpeedY));
            _renderer.SetPropertyBlock(_block, materialId);
        }
    }
}
