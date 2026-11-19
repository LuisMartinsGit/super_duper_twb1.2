// KeystoneRubble.cs
// What a fallen KEYSTONE leaves behind (decision 69, 2026-11-17;
// docs/Design/Territory_Claims.md § 7). The keystones are the buildings a
// player has one of, ever: the Fortress, the Temple of Ridan, its culture's
// landmark (the Vault of Almierra, the Fiendstone Keep, Thessara's Crossing)
// and each sect building (Reliquary, Mending Hall, Stonehold, Treasury, Muster
// Yard). When one falls, rubble marks the spot; it may be built again only
// there (its old facing kept, whoever holds the ground), for a share of its
// price (FortressFallSystem.asset rubbleRebuildCostShare), from the button over
// the rubble. A player whose keystones are ALL down — fallen or never built —
// is out when EliminationSystem's rebuild grace runs out.
//
// The rubble is not a building: no BuildingTag, no Health, no footprint role.
// It is created by DeathSystem when a finished keystone dies and removed by
// FortressFallSystem once that keystone stands again or its owner is out.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

/// <summary>The rubble of a faction's fallen keystone building.</summary>
public struct KeystoneRubble : IComponentData
{
    public Faction Owner;
    /// <summary>The fallen building's id ("Fortress", "TempleOfRidan", or the
    /// landmark's id).</summary>
    public FixedString64Bytes BuildingId;
    /// <summary>The fallen building's facing, in degrees: the rebuilt one
    /// keeps it.</summary>
    public float YawDegrees;
}

namespace TheWaningBorder.Entities
{
    public static class KeystoneRubbles
    {
        public const string FortressId = "Fortress";
        public const string TempleId = "TempleOfRidan";

        /// <summary>One of the buildings a player has one of, ever: the
        /// Fortress, the Temple, a landmark or a sect building.</summary>
        public static bool IsKeystone(string id)
            => id == FortressId || id == TempleId
               || (id != null && (BuildingFactory.IsChoiceBuilding(id)
                                  || TheWaningBorder.Economy.SectConfig.SectIdForBuilding(id) != null));

        private static bool IsSectBuilding(string id)
            => id != null && TheWaningBorder.Economy.SectConfig.SectIdForBuilding(id) != null;

        /// <summary>The keystone id of a building entity, or null.</summary>
        public static string KeystoneIdOf(EntityManager em, Entity e)
        {
            if (em.HasComponent<HallTag>(e)) return FortressId;
            if (em.HasComponent<TempleOfRidanTag>(e)) return TempleId;
            if (em.HasComponent<ChoiceBuildingTag>(e)
                || em.HasComponent<ReliquaryTag>(e) || em.HasComponent<MendingHallTag>(e)
                || em.HasComponent<StoneholdTag>(e) || em.HasComponent<TreasuryTag>(e)
                || em.HasComponent<MusterYardTag>(e))
            {
                string id = BuildingIds.Of(e, em);
                return IsKeystone(id) ? id : null;
            }
            return null;
        }

        static readonly ComponentType[] QT_Rubble =
            { ComponentType.ReadOnly<KeystoneRubble>(), ComponentType.ReadOnly<LocalTransform>() };
        static CachedEntityQuery QC_Rubble;

        /// <summary>The faction's rubble of <paramref name="id"/>, if that
        /// keystone has fallen.</summary>
        public static bool TryFind(EntityManager em, Faction faction, string id,
            out float3 position, out float yawDegrees)
        {
            position = default; yawDegrees = 0f;
            var q = QC_Rubble.Get(em, QT_Rubble);
            if (q.IsEmptyIgnoreFilter) return false;
            using var rs = q.ToComponentDataArray<KeystoneRubble>(Allocator.Temp);
            using var xs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i].Owner != faction || rs[i].BuildingId.ToString() != id) continue;
                position = xs[i].Position;
                yawDegrees = rs[i].YawDegrees;
                return true;
            }
            return false;
        }

        /// <summary>Every rubble heap of the faction, Fortress first, then
        /// in entity order: (id, position).</summary>
        public static void All(EntityManager em, Faction faction,
            System.Collections.Generic.List<(string id, float3 pos)> into)
        {
            into.Clear();
            var q = QC_Rubble.Get(em, QT_Rubble);
            if (q.IsEmptyIgnoreFilter) return;
            using var rs = q.ToComponentDataArray<KeystoneRubble>(Allocator.Temp);
            using var xs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i].Owner != faction) continue;
                string id = rs[i].BuildingId.ToString();
                if (id == FortressId) into.Insert(0, (id, xs[i].Position));
                else into.Add((id, xs[i].Position));
            }
        }

        static readonly ComponentType[] QT_Halls =
            { ComponentType.ReadOnly<HallTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<Health>() };
        static CachedEntityQuery QC_Halls;
        static readonly ComponentType[] QT_Temples =
            { ComponentType.ReadOnly<TempleOfRidanTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<Health>() };
        static CachedEntityQuery QC_Temples;
        static readonly ComponentType[] QT_Landmarks =
            { ComponentType.ReadOnly<ChoiceBuildingTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<Health>() };
        static CachedEntityQuery QC_Landmarks;

        static readonly ComponentType[] QT_Buildings =
            { ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<Health>() };
        static CachedEntityQuery QC_Buildings;

        /// <summary>Any finished keystone of the faction stands (the fall
        /// clock is stopped).</summary>
        public static bool AnyFinished(EntityManager em, Faction faction)
        {
            var bq = QC_Buildings.Get(em, QT_Buildings);
            using var be = bq.ToEntityArray(Allocator.Temp);
            using var bf = bq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bh = bq.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < be.Length; i++)
            {
                if (bf[i].Value != faction || (bh[i].Value <= 0 && bh[i].Max > 0)) continue;
                if (em.HasComponent<UnderConstruction>(be[i])) continue;
                if (KeystoneIdOf(em, be[i]) != null) return true;
            }
            return false;
        }

        private static EntityQuery QueryFor(EntityManager em, string id)
            => id == FortressId ? QC_Halls.Get(em, QT_Halls)
             : id == TempleId ? QC_Temples.Get(em, QT_Temples)
             : QC_Landmarks.Get(em, QT_Landmarks);

        /// <summary>The faction has this keystone: standing, a site or a plan.
        /// A fallen one still collapsing (Health 0) does not count.
        /// <paramref name="finishedOnly"/> counts only a finished one.</summary>
        public static bool Has(EntityManager em, Faction faction, string id, bool finishedOnly = false)
        {
            if (!finishedOnly && PlannedBuildings.CountOf(em, faction, id) > 0) return true;
            if (IsSectBuilding(id))
            {
                // A sect building has a tag of its own; matched by id.
                var bq = QC_Buildings.Get(em, QT_Buildings);
                using var be = bq.ToEntityArray(Allocator.Temp);
                using var bf = bq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var bh = bq.ToComponentDataArray<Health>(Allocator.Temp);
                for (int i = 0; i < be.Length; i++)
                {
                    if (bf[i].Value != faction || (bh[i].Value <= 0 && bh[i].Max > 0)) continue;
                    if (finishedOnly && em.HasComponent<UnderConstruction>(be[i])) continue;
                    if (KeystoneIdOf(em, be[i]) == id) return true;
                }
                return false;
            }
            var q = QueryFor(em, id);
            using var es = q.ToEntityArray(Allocator.Temp);
            using var fs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hs = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < fs.Length; i++)
            {
                if (fs[i].Value != faction || (hs[i].Value <= 0 && hs[i].Max > 0)) continue;
                if (finishedOnly && em.HasComponent<UnderConstruction>(es[i])) continue;
                return true;
            }
            return false;
        }

        /// <summary>The Fortress shorthand the AI and the Fortress gates use.</summary>
        public static bool HasFortress(EntityManager em, Faction faction) => Has(em, faction, FortressId);

        /// <summary>
        /// The keystone gate on a placement (decision 69): a second one is
        /// refused; with rubble, the position and facing become the rubble's.
        /// Any other id passes untouched. Returns false to refuse.
        /// <paramref name="atRubble"/> is true when the placement is the
        /// rebuild (the territory and landmark gates do not apply to it).
        /// </summary>
        public static bool Gate(EntityManager em, Faction faction, string id, ref float3 position,
            ref float yawDegrees, out bool atRubble)
        {
            atRubble = false;
            if (!IsKeystone(id)) return true;
            if (Has(em, faction, id)) return false;
            if (TryFind(em, faction, id, out var p, out float yaw))
            {
                position = p;
                yawDegrees = yaw;
                atRubble = true;
            }
            return true;
        }

        /// <summary>A placement of <paramref name="id"/> at
        /// <paramref name="position"/> is its owner's rebuild on the rubble.</summary>
        public static bool IsRebuild(EntityManager em, Faction faction, string id, float3 position)
            => IsKeystone(id) && TryFind(em, faction, id, out var rp, out _)
               && math.distancesq(rp.xz, position.xz) < 4f;

        /// <summary>Called by DeathSystem for a finished building that died:
        /// a keystone lays its rubble where it stood.</summary>
        public static void Spawn(EntityManager em, EntityCommandBuffer ecb, Entity building)
        {
            if (!em.HasComponent<FactionTag>(building) || !em.HasComponent<LocalTransform>(building)) return;
            string id = KeystoneIdOf(em, building);
            if (id == null) return;
            var xf = em.GetComponentData<LocalTransform>(building);
            var fwd = math.mul(xf.Rotation, new float3(0f, 0f, 1f));
            float yaw = math.degrees(math.atan2(fwd.x, fwd.z));
            if (yaw < 0f) yaw += 360f;
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, LocalTransform.FromPositionRotation(xf.Position, xf.Rotation));
            ecb.AddComponent(e, new KeystoneRubble
            {
                Owner = em.GetComponentData<FactionTag>(building).Value,
                BuildingId = new FixedString64Bytes(id),
                YawDegrees = yaw,
            });
        }
    }
}
