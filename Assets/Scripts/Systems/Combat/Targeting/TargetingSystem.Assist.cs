// TargetingSystem.Assist.cs
// STANDING BY A STRUCK NEIGHBOUR (docs/Design/Stances.md §1c, 2026-10-25):
// an idle Aggressive / Defensive unit within StanceSettings.AssistRadius of a
// friend of its own faction that is being attacked goes for that friend's
// attacker, leashed like any auto engagement, and walks home afterwards.
// Partial of TargetingSystem.cs.
//
// Pull model, read-only, run from AcquirePlanJob on the unit's own stagger
// tick: the unit looks through ITS OWN faction's buckets of the targeting
// grid (the same grid the enemy scan uses — it holds every faction) for the
// nearest friend hit within AssistWindow. No unit writes another's state, so
// the answer does not depend on the order units are visited in; ties between
// equally near friends resolve by the grid's peer-stable visit order.

using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;

namespace TheWaningBorder.Systems.Combat
{
    public partial struct TargetingSystem : ISystem
    {
        /// <summary>
        /// The attacker of the nearest struck friend within the assist
        /// radius, or Entity.Null. <paramref name="anchor"/> is the guard point
        /// the engagement will be leashed from (Stances.md §3).
        /// </summary>
        private static Entity FindAssistTarget(Entity self, float3 myPos, Faction myFaction, float3 anchor,
            in AcquireRules rules, in UnitLookups lk, in EnemyScan scan, in StanceSettings s, float clock)
        {
            float r = s.AssistRadius;
            if (!(r > 0f)) return Entity.Null;
            int myF = (int)myFaction;
            if (myF < 0 || myF >= FactionSlots) return Entity.Null;
            if (scan.FactionPresent[myF] == 0) return Entity.Null;

            float r2 = r * r;
            int cr = (int)math.ceil(r / TargetingCellSize);
            int cx = (int)math.floor(myPos.x / TargetingCellSize);
            int cz = (int)math.floor(myPos.z / TargetingCellSize);

            Entity best = Entity.Null;
            float bestD2 = float.MaxValue;

            for (int dx = -cr; dx <= cr; dx++)
            {
                for (int dz = -cr; dz <= cr; dz++)
                {
                    if (!scan.Grid.Range(new int3(cx + dx, cz + dz, myF), out int k0, out int k1)) continue;
                    for (int k = k0; k < k1; k++)
                    {
                        var ec = scan.Grid.Cands[k];
                        if (ec.E == self) continue;
                        if ((ec.Flags & FlagBuilding) != 0) continue;   // a struck UNIT only

                        float px = ec.Pos.x - myPos.x, pz = ec.Pos.z - myPos.z;
                        float d2 = px * px + pz * pz;
                        if (d2 > r2 || !(d2 < bestD2)) continue;

                        var friend = ec.E;
                        if (!lk.Engagement.TryGetComponent(friend, out var feng)
                            || !lk.Engagement.IsComponentEnabled(friend)) continue;
                        if (clock - feng.HitAt > s.AssistWindow) continue;
                        if (!UnitLookups.On(in lk.LastAttacker, friend)) continue;
                        var atk = lk.LastAttacker[friend].Value;
                        if (!AssistTargetOk(self, myPos, myF, anchor, atk, in rules, in lk, in scan, in s))
                            continue;

                        best = atk;
                        bestD2 = d2;
                    }
                }
            }
            return best;
        }

        /// <summary>Could this unit pick <paramref name="atk"/> on its own —
        /// the enemy scan's filters, applied to one entity.</summary>
        private static bool AssistTargetOk(Entity self, float3 myPos, int myF, float3 anchor, Entity atk,
            in AcquireRules rules, in UnitLookups lk, in EnemyScan scan, in StanceSettings s)
        {
            if (atk == Entity.Null || atk == self || !lk.Storage.Exists(atk)) return false;
            if (!lk.Health.TryGetComponent(atk, out var hp) || hp.Value <= 0) return false;
            if (!lk.Faction.TryGetComponent(atk, out var af)) return false;
            if (!lk.Transform.TryGetComponent(atk, out var axf)) return false;

            int col = UnitLookups.On(in lk.Disordered, atk) ? RogueSlot : (int)af.Value;
            if (col < 0 || col >= GridSlots) return false;
            if (scan.Hostile[myF * GridSlots + col] == 0) return false;

            if (UnitLookups.On(in lk.Veiled, atk)) return false;
            if (lk.NodeUntargetable.HasComponent(atk) || lk.NodeNoAutoAcquire.HasComponent(atk)) return false;

            bool isBuilding = lk.Building.HasComponent(atk);
            if (rules.BuildingsOnly != 0 && !isBuilding) return false;
            if (rules.NonSiege != 0 && lk.Wall.HasComponent(atk)) return false;
            if (lk.Flying.HasComponent(atk) && !lk.CanHitFlying(self)) return false;

            var atkPos = axf.Position;
            if (rules.IsMelee != 0 && MeleeHeightBlocks(myPos.y, atkPos.y, isBuilding, false)) return false;
            if (DistXZ(myPos, atkPos) > 3f && scan.IsHidden(atk)) return false;

            // Beyond the leash the chase would break on the spot.
            return DistXZ(anchor, atkPos) <= s.AggressiveLeash;
        }
    }
}
