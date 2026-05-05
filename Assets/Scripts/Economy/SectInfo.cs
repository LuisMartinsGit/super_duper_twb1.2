// SectInfo.cs
// Display-time descriptions for the 12 sects. Used by the Religion HUD
// to show a one-line passive summary alongside each sect button.
//
// Phase-5 polish may move this into SectDefinition ScriptableObjects so
// it can be localized + edited without recompiling.
//
// Location: Assets/Scripts/Economy/SectInfo.cs

namespace TheWaningBorder.Economy
{
    public static class SectInfo
    {
        public static string PassiveDescription(string sectId) => sectId switch
        {
            SectConfig.Antiquity   => "Tally of the Lost — +dmg per kill of each unit-type.",
            SectConfig.Renewal     => "Hands That Mend — buildings auto-repair when out of combat.",
            SectConfig.Fortitude   => "Veiled Stone — walls and towers gain bonus HP.",
            SectConfig.Reclamation => "Curse-Hardened — units take less damage from Crystal-Curse.",
            SectConfig.Silence     => "Steadfast Vigil — units gain armor while holding position.",
            SectConfig.Justice     => "Marked for Sentence — units that kill yours take bonus damage.",
            SectConfig.Veneration  => "Fervor — kills grant a stacking damage / attack-rate buff.",
            SectConfig.Witness     => "All-Seeing — Scout units gain extended vision.",
            SectConfig.War         => "Forged in Battle — military units cost less and train faster.",
            SectConfig.Ash         => "Pyre's Promise — units leave a burning patch on death.",
            SectConfig.Ruin        => "Profane Hands — bonus damage vs buildings + cost refund on destruction.",
            SectConfig.Wrath       => "Spite of the Forsaken — wounded units deal more damage.",
            _                      => "—",
        };
    }
}
