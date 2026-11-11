// PresentationSpawnSystem.FarmStoneWall.cs
// The view of one piece of a Farm's level-3 low stone wall
// (docs/Design/Age_1_Alanthor.md § The level-3 wall): one Synty low-wall piece
// (FarmStoneWallSystem.asset's piecePrefabs, picked by where the piece stands,
// so a rebuilt piece looks the same) fitted to the span A-B on the terrain and
// to pieceHeight, a box collider over the strip (ScreenPick finds it, so a
// right-click can order an attack) and the EntityReference.
//
// Visibility: FogVisibilitySyncSystem shows or hides the ROOT by the rule for
// non-building statics (seen or explored ground). The ART child is also hidden
// whenever the piece's Farm is not shown, and the moment the piece is at 0 HP —
// the crop field's rule (FarmCropField draws a field only while its Farm's view
// is up), so a wall never shows round a Farm the player does not see.
// Presentation only.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;
using TheWaningBorder.Rendering;
using TheWaningBorder.World.Terrain;

public partial class PresentationSpawnSystem
{
    private GameObject CreateFarmWallPieceVisual(Vector3 pos, Entity entity)
    {
        var cfg = FarmStoneWallSystemConfig.I;
        var root = new GameObject($"FarmWallPiece_{entity.Index}");
        var piece = _em.GetComponentData<FarmWallPiece>(entity);
        var rootRot = (Quaternion)_em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;
        root.transform.SetPositionAndRotation(pos, rootRot);

        float2 a = piece.A, b = piece.B;
        float span2d = math.distance(a, b);
        GameObject art = null;
        if (cfg != null && cfg.piecePrefabs != null && cfg.piecePrefabs.Length > 0)
        {
            int pick = (int)(math.hash(new int2((int)math.round(a.x * 16f), (int)math.round(a.y * 16f)))
                             % (uint)cfg.piecePrefabs.Length);
            var prefab = cfg.piecePrefabs[pick] ?? FirstNonNull(cfg.piecePrefabs);
            var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            var mr = prefab != null ? prefab.GetComponentInChildren<MeshRenderer>() : null;
            if (mf != null && mf.sharedMesh != null && mr != null)
            {
                art = new GameObject("Art");
                art.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = art.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;

                // The fence's fit (FarmCropField.PieceMatrix): long axis along
                // the span, pitched with the ground, scaled to span and height.
                var a3 = new Vector3(a.x, TerrainUtility.GetHeight(a.x, a.y), a.y);
                var b3 = new Vector3(b.x, TerrainUtility.GetHeight(b.x, b.y), b.y);
                var dir = b3 - a3;
                float span = Mathf.Max(dir.magnitude, 1e-4f);
                var bs = mf.sharedMesh.bounds;
                bool alongX = bs.size.x >= bs.size.z;
                var rot = Quaternion.LookRotation(dir / span, Vector3.up);
                if (alongX) rot *= Quaternion.Euler(0f, -90f, 0f);
                float meshLen = alongX ? bs.size.x : bs.size.z;
                float sh = Mathf.Max(0.1f, cfg.pieceHeight) / Mathf.Max(bs.size.y, 1e-3f);
                float sl = span / Mathf.Max(meshLen, 1e-3f) * 1.03f;   // a touch of overlap at the joints
                var scale = alongX ? new Vector3(sl, sh, sh) : new Vector3(sh, sh, sl);
                var pivot = new Vector3(bs.center.x, bs.min.y + 0.04f / Mathf.Max(sh, 1e-3f), bs.center.z);
                Vector3 worldPos = (a3 + b3) * 0.5f - rot * Vector3.Scale(scale, pivot);
                art.transform.SetParent(root.transform, false);
                art.transform.SetPositionAndRotation(worldPos, rot);
                art.transform.localScale = scale;
            }
        }
        if (art == null)
        {
            // Data bug (no usable piece art): a plain grey block, so the wall
            // that blocks movement is never invisible.
            art = GameObject.CreatePrimitive(PrimitiveType.Cube);
            art.name = "Art (placeholder)";
            Object.Destroy(art.GetComponent<Collider>());
            art.transform.SetParent(root.transform, false);
            float h = cfg != null ? Mathf.Max(0.1f, cfg.pieceHeight) : 0.75f;
            art.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            art.transform.localScale = new Vector3(piece.HalfThickness * 2f, h, span2d);
        }

        // The click target: the strip, a little taller than the wall.
        var col = root.AddComponent<BoxCollider>();
        float hh = Mathf.Max(1f, cfg != null ? cfg.pieceHeight + 0.4f : 1f);
        col.size = new Vector3(Mathf.Max(0.9f, piece.HalfThickness * 2f), hh, Mathf.Max(0.9f, span2d));
        col.center = new Vector3(0f, hh * 0.5f, 0f);
        root.AddComponent<EntityReference>().Entity = entity;
        root.AddComponent<FarmWallPieceView>().Bind(entity, piece.Farm, art);
        return root;
    }

    private static GameObject FirstNonNull(GameObject[] list)
    {
        foreach (var g in list) if (g != null) return g;
        return null;
    }
}

namespace TheWaningBorder.Rendering
{
    /// <summary>Hides a wall piece's art while its Farm is not shown or the
    /// piece is down. See PresentationSpawnSystem.FarmStoneWall.cs.</summary>
    public sealed class FarmWallPieceView : MonoBehaviour
    {
        private Entity _piece, _farm;
        private GameObject _art;
        private float _next;

        public void Bind(Entity piece, Entity farm, GameObject art)
        {
            _piece = piece;
            _farm = farm;
            _art = art;
        }

        private void Update()
        {
            if (_art == null || Time.unscaledTime < _next) return;
            var cfg = FarmStoneWallSystemConfig.I;
            _next = Time.unscaledTime + (cfg != null ? Mathf.Max(0.05f, cfg.viewPollSeconds) : 0.25f);
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            bool show = em.Exists(_piece) && em.HasComponent<Health>(_piece)
                        && em.GetComponentData<Health>(_piece).Value > 0;
            if (show)
            {
                var views = EntityViewManager.Instance;
                show = views != null && views.TryGetView(_farm, out var farmView)
                       && farmView != null && farmView.activeInHierarchy;
            }
            if (_art.activeSelf != show) _art.SetActive(show);
        }
    }
}
