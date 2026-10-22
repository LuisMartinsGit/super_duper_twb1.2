using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Buildings
{
    /// <summary>
    /// How many Field Hospitals a team may keep standing and how far apart
    /// (docs/Design/Sects.md, Renewal, 2026-10-22). The asset is
    /// FieldHospitalSystem.asset, beside FieldHospitalSystem.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Sects/FieldHospitalSystem",
                     fileName = "FieldHospitalSystem")]
    public sealed class FieldHospitalSystemConfig : ScriptableObject, IComponentConfig
    {
        static FieldHospitalSystemConfig _i;
        public static FieldHospitalSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<FieldHospitalSystemConfig>());

        /// <summary>Most hospitals a team (a faction and its allies) may have
        /// standing at once; a cast past the cap is refused.</summary>
        public int capPerTeam;
        /// <summary>Least distance (m) between two of the team's hospitals;
        /// a cast closer than this to one is refused.</summary>
        public float minSpacingMetres;
    }
}
