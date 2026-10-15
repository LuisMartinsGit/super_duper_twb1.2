// CartographySystem.cs
// Antiquity's research, Cartography (docs/Design/Sects.md §4, 2026-10-15):
// "Capturing a territory explores all of it."
//
// Once a second, for every faction that has researched it, any territory it
// holds now and did not hold at the last look is explored end to end: short
// reveals are laid on a grid over the territory's ground, which the fog marks
// permanently explored like any other sight. Territory ownership and the
// research flag are replicated sim state, so every peer explores the same
// ground on the same tick.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CartographySystem : SystemBase
    {
        private const float CheckSeconds = 1f;
        private const float GridStep = 18f;
        private const float RevealRadius = 14f;
        private const float RevealSeconds = 1.5f;
        private const float MaxReach = 160f;

        private float _timer;
        private int _epoch = -1;
        private int _version = int.MinValue;
        private bool[,] _held;   // [faction, territory] at the last look

        protected override void OnUpdate()
        {
            if (!RegionMap.Ready) return;
            if (_epoch != SimCadence.Epoch || _version != RegionMap.Version || _held == null)
            {
                _epoch = SimCadence.Epoch;
                _version = RegionMap.Version;
                _held = new bool[8, RegionMap.Count];
                Snapshot(false);
                return;
            }
            _timer += SystemAPI.Time.DeltaTime;
            if (_timer < CheckSeconds) return;
            _timer = 0f;
            Snapshot(true);
        }

        private void Snapshot(bool reveal)
        {
            var em = EntityManager;
            int count = RegionMap.Count;
            for (int f = 0; f < 8; f++)
            {
                var faction = (Faction)f;
                bool charts = reveal && SectResearchEffects.ExploresCapturedTerritory(faction);
                for (int t = 0; t < count; t++)
                {
                    bool now = TerritoryOwnership.IsOwnedBy(t, faction);
                    if (now && !_held[f, t] && charts) Explore(em, faction, t);
                    _held[f, t] = now;
                }
            }
        }

        private static void Explore(EntityManager em, Faction faction, int territory)
        {
            var seed = RegionMap.SeedOf(territory);
            int steps = (int)(MaxReach / GridStep);
            for (int ix = -steps; ix <= steps; ix++)
            for (int iz = -steps; iz <= steps; iz++)
            {
                float x = seed.x + ix * GridStep, z = seed.y + iz * GridStep;
                if (RegionMap.RegionAt(x, z) != territory) continue;
                SectActivePowerHelper.SpawnReveal(em, faction, new float3(x, 0f, z), RevealRadius, RevealSeconds);
            }
        }
    }
}
