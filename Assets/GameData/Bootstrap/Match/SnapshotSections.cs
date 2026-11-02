// SnapshotSections.cs
// Saved-game state owned by the Bootstrap assembly, which the Runtime-side
// SnapshotState cannot reach: the start positions every map-derived rebuild
// (start clearing, reachability, territory types) is computed from.
// docs/Design/Replays_And_Saves.md §3

using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheWaningBorder.Bootstrap
{
    public static class SnapshotSections
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            TheWaningBorder.Core.Save.SnapshotState.RegisterSection("spawn", SaveSpawns, LoadSpawns);
        }

        private static byte[] SaveSpawns()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                var all = PlayerSpawnSystem.SpawnPositions;
                w.Write(all.Count);
                foreach (var kv in all)
                {
                    w.Write((int)kv.Key);
                    w.Write(kv.Value.x); w.Write(kv.Value.y); w.Write(kv.Value.z);
                }
            }
            return ms.ToArray();
        }

        private static void LoadSpawns(byte[] data)
        {
            var list = new List<KeyValuePair<Faction, Vector3>>();
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var f = (Faction)r.ReadInt32();
                    list.Add(new KeyValuePair<Faction, Vector3>(f,
                        new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle())));
                }
            }
            PlayerSpawnSystem.RestoreSpawnPositions(list);
        }
    }
}
