// MatchSnapshotLog.cs
// Once a minute, drop a bank snapshot into each faction's log — the human's
// into Player_*.log, AIs into AI_*.log — so a human match and an AI match
// compare line-for-line, and alpha testers' match logs carry the economy curve
// (docs/Design/Alpha_Build.md).
//
// SHIPS IN RELEASES. It used to live inside the Display 2 board, which does not
// (StatsBoardHUD is compiled out of release builds); split out so stripping the
// debug board does not strip the match logs. Presentation only — reads sim
// state, writes log lines.

using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.Ingame
{
    public class MatchSnapshotLog : MonoBehaviour
    {
        private const float Interval = 60f;
        private const int MaxFactions = 8;

        private float _next = Interval;
        private EntityQuery _unitQuery;
        private bool _ready;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!_ready)
            {
                _unitQuery = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<UnitTag, FactionTag>().WithNone<PlundererTag>().Build(em);
                _ready = true;
            }

            var mil = new int[MaxFactions];
            using (var tags = _unitQuery.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var facs = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < tags.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= MaxFactions) continue;
                    var cls = tags[i].Class;
                    if (cls == UnitClass.Melee || cls == UnitClass.Ranged || cls == UnitClass.Siege || cls == UnitClass.Magic)
                        mil[f]++;
                }

            var local = GameSettings.LocalPlayerFaction;
            for (int f = 0; f < MaxFactions; f++)
            {
                if (!FactionEconomy.TryGetBank(em, (Faction)f, out var bank)) continue;
                var res = em.GetComponentData<FactionResources>(bank);
                int held = 0;
                if (RegionMap.Ready && TerritoryOwnership.Ready)
                    for (int t = 0; t < RegionMap.Count; t++)
                        if (TerritoryOwnership.OwnerOf(t) == f) held++;
                string msg = $"supplies {res.Supplies} iron {res.Iron} veilstone {res.Veilstone} " +
                             $"veilsteel {res.Veilsteel} military {mil[f]} territories {held}";
                if ((Faction)f == local && !GameSettings.IsObserver)
                    TheWaningBorder.AI.AILogger.LogPlayer((Faction)f, "SNAPSHOT", msg);
                else
                    TheWaningBorder.AI.AILogger.Log((Faction)f, "SNAPSHOT", msg);
            }
        }
    }
}
