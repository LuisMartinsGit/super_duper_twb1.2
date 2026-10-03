// SanctumSystem.cs
// A SANCTUM territory (docs/Design/Territory_Claims.md §9) carries no resource
// nodes; whoever HOLDS it earns one Religion Point a minute — straight to the
// balance, not through the points ladder.
//
// Lockstep: one SimCadence clock, territories walked in index order, replicated
// ownership only.

using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SanctumSystem : SystemBase
    {
        /// <summary>Seconds per Religion Point a held Sanctum pays.</summary>
        public const float SecondsPerPoint = 60f;

        private SimCadence.Periodic _acc;

        protected override void OnUpdate()
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;
            if (!_acc.Due(SystemAPI.Time.DeltaTime, SecondsPerPoint)) return;

            var em = EntityManager;
            for (int t = 0; t < RegionMap.Count; t++)
            {
                if (!TerritoryResources.IsSanctum(t)) continue;
                int owner = TerritoryOwnership.OwnerOf(t);
                if (owner < 0 || owner >= 8) continue;   // natural ground or the curse
                FactionReligionPointsHelper.Refund(em, (Faction)owner, 1);
                if ((Faction)owner == GameSettings.LocalPlayerFaction)
                    SimSignals.Notify(string.Format(
                        TheWaningBorder.Core.Localization.Loc.T("+1 Religion Point — {0} (Sanctum)"),
                        RegionMap.NameOf(t)));
            }
        }
    }
}
