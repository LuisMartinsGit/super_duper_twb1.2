using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Tuning numbers for <see cref="WallGarrison"/> — walking into a
    /// garrisonable building and being put out of it again
    /// (docs/Design/Age_1_Alanthor.md § Watch Tower, 2026-10-20). The asset is
    /// WallGarrison.asset, beside WallGarrison.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Buildings/WallGarrison",
                     fileName = "WallGarrison")]
    public sealed class WallGarrisonConfig : ScriptableObject, IComponentConfig
    {
        static WallGarrisonConfig _i;
        /// <summary>The one instance, for the static class that reads it.</summary>
        public static WallGarrisonConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<WallGarrisonConfig>());

        /// <summary>Metres beyond the building's footprint edge within which
        /// a unit ordered to garrison it goes inside. Further away it walks
        /// there first.</summary>
        public float entryRange;

        /// <summary>Metres outside the footprint edge the garrison walk aims
        /// at (the footprint itself is impassable).</summary>
        public float approachSlack;

        /// <summary>Metres outside the footprint edge where ejected or
        /// released men are stood.</summary>
        public float ejectGap;

        /// <summary>Metres between two men stood on the same ring.</summary>
        public float ejectSpacing;

        /// <summary>Further rings tried, each <see cref="ejectSpacing"/> out,
        /// when the first has no free ground left.</summary>
        public int ejectRings;

        /// <summary>Percent more damage a troop deals while it stands on a
        /// wall-walk (docs/Design/Combat_Pacing.md § Fighting from a wall-walk).
        /// Replaces the height-advantage damage scaling for that hit.</summary>
        public float wallTopDamagePct;

        /// <summary>Percent of its own armour (for the incoming damage type) a
        /// troop gains on top while it stands on a wall-walk.</summary>
        public float wallTopArmorPct;
    }
}
