// WallPieceArtSO.cs
// ONE WALL PIECE AT ONE WALL LEVEL (docs/Design/Art_Direction.md § 13,
// 2026-10-21). Each lives in its own folder beside its .blend and .fbx, and
// nothing else:
//
//   Civs/Alanthor/Buildings/StoneWall/Lvl<N>/<Piece>/   the Stone Wall, levels 1-3
//   Age0/Buildings/Palisade/Lvl0/<Piece>/               the palisade, level 0
//
// A folder exists only where the piece exists at that level (a Ballista
// mount from level 2, a Trebuchet from 3, the hub is a tower at 3).
// WallModuleArt.asset lists every one of these (Waning Border > Art > Bind
// Building Art fills the list and each SO's prefab from its folder's FBX);
// WallModuleArt.TierPrefab picks by part and level.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    [CreateAssetMenu(menuName = "Waning Border/Walls/Wall Piece Art", fileName = "WallPiece")]
    public sealed class WallPieceArtSO : ScriptableObject
    {
        /// <summary>Which wall part this is.</summary>
        public WallModuleArt.WallPart part;
        /// <summary>The wall level it is drawn at: 0 palisade, 1 Stone,
        /// 2 Battlemented, 3 Shielded.</summary>
        public int level;
        /// <summary>The model: the FBX in this SO's own folder.</summary>
        public GameObject prefab;
    }
}
