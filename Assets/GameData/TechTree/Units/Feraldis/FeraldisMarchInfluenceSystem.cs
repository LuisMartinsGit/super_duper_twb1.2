// File: Assets/GameData/TechTree/Units/Feraldis/FeraldisMarchInfluenceSystem.cs
// Feraldis claims ground by WALKING ON IT.
// Canon: docs/Design/Age_1_Feraldis.md — "Marching influence".
//
// Each culture grows its border differently:
//   Alanthor — builds outward from home (forts project influence)
//   Runai    — trade lanes between nodes
//   Feraldis — its ARMY. Every Feraldis military unit and every raider
//              leaks a little influence into the ground beneath it, so the
//              Feraldis border creeps toward wherever its soldiers are —
//              which is, by definition, toward the enemy.
//
// This is the second half of the Feraldis territory model. War Totems are
// the ANCHORS (strong, permanent, planted on blood); marching influence is
// the CONNECTIVE TISSUE that lets an army carve a corridor to a totem site
// in the first place, and that stops a Feraldis player from sitting at 0 %
// influence all match with no curse suppression anywhere (which is exactly
// what five consecutive playtests showed).
//
// Raiders (Plunderers) and conscripted workers DO count — they are soldiers
// on the map. A worker still on build duty does NOT: builders pottering
// around the base would quietly claim home ground for free, which is
// Alanthor's whole identity, not Feraldis's.
//
// PlayerInfluenceMap is managed main-thread state, so this is a SystemBase
// on a slow pulse rather than a per-frame job.

using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Influence;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.World
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisMarchInfluenceSystem : SystemBase
    {
        private float _tick;

        protected override void OnCreate()
        {
            RequireForUpdate<UnitTag>();
        }

        protected override void OnUpdate()
        {
            _tick -= SystemAPI.Time.DeltaTime;
            if (_tick > 0f) return;
            float slice = MarchInfluenceInterval;
            _tick = MarchInfluenceInterval;

            if (!PlayerInfluenceMap.Ready) return;
            var em = EntityManager;

            float amount = MarchInfluencePerSecond * slice;

            // EVERY military unit of a Feraldis faction, not just the ones a
            // Feraldis factory built. The old FeraldisUnitTag filter silently
            // excluded the shared-roster Spearmen and Archers that make up
            // most of a Feraldis army, so most of the army claimed nothing.
            // FeraldisSoldier.Is also does the culture check (a Berserker
            // converted at a shared Fiendstone Keep must not paint the map for
            // an Alanthor owner) and skips builders on build duty.
            foreach (var (xf, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                var f = faction.ValueRO.Value;
                if (!TheWaningBorder.Systems.Border.FeraldisSoldier.Is(em, entity, f)) continue;

                var p = xf.ValueRO.Position;
                PlayerInfluenceMap.Deposit(p.x, p.z, MarchInfluenceRadius, (int)f, amount);
            }
        }
    }
}
