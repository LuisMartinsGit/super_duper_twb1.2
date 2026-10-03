// WallDeckArmorSystem.cs
// A Shielded wall hardens the men standing on it
// (docs/Design/Age_1_Alanthor.md § The stone wall): a foot unit on the
// wall-walk of its own — or an ally's — Shielded wall takes
// WallTiers.DeckArmorBonus extra melee and ranged armour. Gained on the deck,
// lost the moment the unit steps off it (or the wall under it is not
// Shielded).
//
// The bonus is written INTO the unit's Defense, so every damage path reads it
// with no change of its own; WallDeckArmor remembers how much was added so it
// comes off exactly, whatever else (ranks, equipment, techs) moved Defense in
// between — those systems add their own deltas, so the two never fight.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;

/// <summary>Armour a unit is currently drawing from the Shielded wall it
/// stands on — removed, with the armour, when it leaves.</summary>
public struct WallDeckArmor : IComponentData
{
    public int Applied;
}

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LayeredMoveSystem))]
    public partial class WallDeckArmorSystem : SystemBase
    {
        /// <summary>How far from a wall piece's centre a deck unit counts as
        /// standing on it: a module is 3 m long and its walk ~3.2 m wide.</summary>
        private const float PieceReach = 3f;

        private EntityQuery _onDeckOrArmored;
        private EntityQuery _wallPieces;
        private readonly List<float3> _piecePos = new List<float3>();
        private readonly List<Faction> _pieceFac = new List<Faction>();

        protected override void OnCreate()
        {
            _onDeckOrArmored = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<NavLayerIndex>() },
            });
            _wallPieces = GetEntityQuery(
                ComponentType.ReadOnly<WallTag>(),
                ComponentType.ReadOnly<WallTier>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.Exclude<WallSegmentTag>(),
                ComponentType.Exclude<PalisadeTag>());
            RequireForUpdate(_onDeckOrArmored);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // Shielded pieces only, gathered once per tick — and only when
            // somebody is up on a deck or still carries the bonus.
            _piecePos.Clear();
            _pieceFac.Clear();
            bool anyoneUp = false;
            foreach (var nli in SystemAPI.Query<RefRO<NavLayerIndex>>().WithAll<UnitTag>())
                if (nli.ValueRO.Layer == NavLayerIndex.LayerRampart) { anyoneUp = true; break; }
            if (!anyoneUp && SystemAPI.QueryBuilder().WithAll<WallDeckArmor>().Build().IsEmptyIgnoreFilter)
                return;

            using (var tiers = _wallPieces.ToComponentDataArray<WallTier>(Allocator.Temp))
            using (var xfs = _wallPieces.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var facs = _wallPieces.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < tiers.Length; i++)
                {
                    if (tiers[i].Level < WallTiers.Shielded) continue;
                    _piecePos.Add(xfs[i].Position);
                    _pieceFac.Add(facs[i].Value);
                }
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (nli, xf, fac, def, entity) in SystemAPI
                         .Query<RefRO<NavLayerIndex>, RefRO<LocalTransform>, RefRO<FactionTag>, RefRW<Defense>>()
                         .WithAll<UnitTag>()
                         .WithEntityAccess())
            {
                int want = 0;
                if (nli.ValueRO.Layer == NavLayerIndex.LayerRampart
                    && OnShieldedWall(xf.ValueRO.Position, fac.ValueRO.Value))
                    want = WallTiers.DeckArmorBonus;

                int had = em.HasComponent<WallDeckArmor>(entity)
                    ? em.GetComponentData<WallDeckArmor>(entity).Applied : 0;
                if (want == had) continue;

                int delta = want - had;
                var d = def.ValueRO;
                d.Melee += delta;
                d.Ranged += delta;
                def.ValueRW = d;

                if (want == 0) ecb.RemoveComponent<WallDeckArmor>(entity);
                else if (had == 0) ecb.AddComponent(entity, new WallDeckArmor { Applied = want });
                else ecb.SetComponent(entity, new WallDeckArmor { Applied = want });
            }
            ecb.Playback(em);
            ecb.Dispose();
        }

        /// <summary>True when a Shielded piece of the unit's own side stands
        /// under it.</summary>
        private bool OnShieldedWall(float3 p, Faction unitFaction)
        {
            for (int i = 0; i < _piecePos.Count; i++)
            {
                if (math.distancesq(p.xz, _piecePos[i].xz) > PieceReach * PieceReach) continue;
                if (_pieceFac[i] == unitFaction || Alliances.AreAllied(_pieceFac[i], unitFaction))
                    return true;
            }
            return false;
        }
    }
}
