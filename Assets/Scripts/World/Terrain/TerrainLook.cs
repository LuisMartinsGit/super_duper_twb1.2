// TerrainLook.cs
// Per-map switches for the terrain shader's anti-tiling and broad colour
// drift (TWBTerrainLitPasses.hlsl, "TWB: anti-tiling"). Sits on a map's
// Terrain object; the values are that map's authored look. A map without
// this component renders its ground exactly as before: the globals are
// zeroed when the component goes away.

using UnityEngine;

namespace TheWaningBorder.World.Terrain
{
    [ExecuteAlways]
    public sealed class TerrainLook : MonoBehaviour
    {
        public float antiTile;
        public float antiTileCells;
        public float macroVariation;
        public Vector2 macroCells;
        public bool cliffProjection;

        static readonly int AntiTileId = Shader.PropertyToID("_TWBAntiTile");
        static readonly int AntiTileCellsId = Shader.PropertyToID("_TWBAntiTileCells");
        static readonly int MacroVariationId = Shader.PropertyToID("_TWBMacroVariation");
        static readonly int MacroCellsId = Shader.PropertyToID("_TWBMacroCells");
        static readonly int CliffProjectionId = Shader.PropertyToID("_TWBCliffProjection");
        static readonly int TerrainSizeId = Shader.PropertyToID("_TWBTerrainSize");

        void OnEnable() => Apply();
        void OnValidate() { if (isActiveAndEnabled) Apply(); }

        void OnDisable()
        {
            Shader.SetGlobalFloat(AntiTileId, 0f);
            Shader.SetGlobalFloat(MacroVariationId, 0f);
            Shader.SetGlobalFloat(CliffProjectionId, 0f);
        }

        public void Apply()
        {
            Shader.SetGlobalFloat(AntiTileId, antiTile);
            Shader.SetGlobalFloat(AntiTileCellsId, antiTileCells);
            Shader.SetGlobalFloat(MacroVariationId, macroVariation);
            Shader.SetGlobalVector(MacroCellsId, new Vector4(macroCells.x, macroCells.y, 0f, 0f));
            var terrain = GetComponent<UnityEngine.Terrain>();
            bool cliffs = cliffProjection && terrain != null && terrain.terrainData != null;
            Shader.SetGlobalFloat(CliffProjectionId, cliffs ? 1f : 0f);
            if (cliffs)
            {
                var size = terrain.terrainData.size;
                Shader.SetGlobalVector(TerrainSizeId, new Vector4(size.x, size.z, 0f, 0f));
            }
        }
    }
}
