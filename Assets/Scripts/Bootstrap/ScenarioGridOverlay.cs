// ScenarioGridOverlay.cs
// Bright-green 2 m build-grid overlay for showcase scenarios.
// Location: Assets/Scripts/Bootstrap/ScenarioGridOverlay.cs

using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Draws the 2 m build grid as one line-topology mesh across a fixed
    /// world rect — the review surface for the building showcase, so every
    /// placed building's footprint can be read against real build cells.
    /// Same green as the Gatherer's Hut gather-area circle
    /// (GathererHutAreaDisplay.circleColor), per the review spec.
    /// </summary>
    public sealed class ScenarioGridOverlay : MonoBehaviour
    {
        private static readonly Color GridGreen = new Color(0.2f, 0.8f, 0.3f, 0.6f);

        /// <summary>Lift above the ground so the lines never z-fight the terrain.</summary>
        private const float YLift = 0.06f;

        public static void Create(float minX, float minZ, float maxX, float maxZ)
        {
            var go = new GameObject("ScenarioGridOverlay");
            go.AddComponent<ScenarioGridOverlay>().Build(minX, minZ, maxX, maxZ);
        }

        private void Build(float minX, float minZ, float maxX, float maxZ)
        {
            // Snap the rect outward onto grid lines so drawn lines ARE cell
            // boundaries, not arbitrary parallels.
            float cell = BuildGrid.CellSize;
            minX = Mathf.Floor(minX / cell) * cell;
            minZ = Mathf.Floor(minZ / cell) * cell;
            maxX = Mathf.Ceil(maxX / cell) * cell;
            maxZ = Mathf.Ceil(maxZ / cell) * cell;

            int xLines = Mathf.RoundToInt((maxX - minX) / cell) + 1;
            int zLines = Mathf.RoundToInt((maxZ - minZ) / cell) + 1;

            var verts = new Vector3[(xLines + zLines) * 2];
            var idx = new int[verts.Length];
            int v = 0;
            for (int i = 0; i < xLines; i++)
            {
                float x = minX + i * cell;
                verts[v] = new Vector3(x, HeightAt(x, minZ), minZ); idx[v] = v; v++;
                verts[v] = new Vector3(x, HeightAt(x, maxZ), maxZ); idx[v] = v; v++;
            }
            for (int i = 0; i < zLines; i++)
            {
                float z = minZ + i * cell;
                verts[v] = new Vector3(minX, HeightAt(minX, z), z); idx[v] = v; v++;
                verts[v] = new Vector3(maxX, HeightAt(maxX, z), z); idx[v] = v; v++;
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.SetIndices(idx, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();

            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // Same URP-unlit transparent setup as GathererHutAreaDisplay — the
            // scenario is editor-only, and this shader is referenced by live
            // materials, so it survives build stripping anyway.
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", GridGreen);
            mat.color = GridGreen;
            mat.renderQueue = 3000;
            mr.sharedMaterial = mat;
        }

        private static float HeightAt(float x, float z)
            => TerrainUtility.GetHeight(x, z) + YLift;
    }
}
