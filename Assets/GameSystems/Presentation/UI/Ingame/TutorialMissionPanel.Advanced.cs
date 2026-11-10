// TutorialMissionPanel.Advanced.cs
// The Alanthor mission's objectives and coach lines. Same panel, same tone as the
// Basic mission. docs/Design/Tutorial_Missions.md section 4

using UnityEngine;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Tutorial;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class TutorialMissionPanel
    {
        private void AddAdvancedRows(RectTransform root)
        {
            var cfg = TutorialAdvancedSystemConfig.I;
            bool Open(AdvancedStage s) => TutorialMissionState.AdvancedStage >= s;
            int Count(bool done) => done ? 1 : 0;

            AddRow(root, () => true,
                () => string.Format(Loc.T("1 - Build the Vault of Almiérra ({0}/{1})"),
                    Count(Open(AdvancedStage.Economy)), 1),
                () => Open(AdvancedStage.Economy));
            AddRow(root, () => Open(AdvancedStage.Economy),
                () => string.Format(Loc.T("2a - Level up your Guilds ({0}/{1})"),
                    Count(TutorialMissionState.SurveyDone), 1),
                () => TutorialMissionState.SurveyDone);
            AddRow(root, () => Open(AdvancedStage.Economy),
                () => string.Format(Loc.T("2b - Research Trade Charters at the Fortress ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.Charters, 1), 1),
                () => TutorialMissionState.Charters >= 1);
            AddRow(root, () => Open(AdvancedStage.Military),
                () => string.Format(Loc.T("3 - Build an Archery Range ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.Ranges, 1), 1),
                () => TutorialMissionState.Ranges >= 1);
            AddRow(root, () => Open(AdvancedStage.Defense),
                () => string.Format(Loc.T("4a - Build a Watch Tower ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.Towers, 1), 1),
                () => TutorialMissionState.Towers >= 1);
            AddRow(root, () => Open(AdvancedStage.Defense),
                () => string.Format(Loc.T("4b - Claim a territory ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.Claimed, 1), 1),
                () => TutorialMissionState.Claimed >= 1);
            AddRow(root, () => Open(AdvancedStage.Points),
                () => string.Format(Loc.T("5 - Earn Religion Points ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.ReligionPoints, cfg.religionPointsNeeded), cfg.religionPointsNeeded),
                () => Open(AdvancedStage.Religion));
            AddRow(root, () => Open(AdvancedStage.Religion),
                () => string.Format(Loc.T("6a - Build the Temple ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.Temples, 1), 1),
                () => TutorialMissionState.Temples >= 1);
            AddRow(root, () => Open(AdvancedStage.Religion),
                () => string.Format(Loc.T("6b - Adopt a sect ({0}/{1})"),
                    Count(TutorialMissionState.SectAdopted), 1),
                () => TutorialMissionState.SectAdopted);
            AddRow(root, () => Open(AdvancedStage.Sanctum),
                () => string.Format(Loc.T("7 - Destroy the curse node in the Sanctum ({0}/{1})"),
                    Count(TutorialMissionState.NodeDead), 1),
                () => TutorialMissionState.NodeDead);
            AddRow(root, () => Open(AdvancedStage.Ruins),
                () => string.Format(Loc.T("8a - Claim the monument ({0}/{1})"),
                    Count(TutorialMissionState.MonumentHeld), 1),
                () => TutorialMissionState.MonumentHeld);
            AddRow(root, () => Open(AdvancedStage.Ruins),
                () => string.Format(Loc.T("8b - Explore its ruins ({0}/{1})"),
                    Count(TutorialMissionState.MonumentExplored), 1),
                () => TutorialMissionState.MonumentExplored);
            AddRow(root, () => Open(AdvancedStage.Victory),
                () => Loc.T("9 - Win: destroy the rival, enshrine the Shardroot, or hold every Sanctum"),
                () => false);
        }

        private static void WriteAdvancedCoach(out string title, out string body)
        {
            var cfg = TutorialAdvancedSystemConfig.I;
            switch (TutorialMissionState.AdvancedStage)
            {
                case AdvancedStage.Vault:
                    title = Loc.T("Age up");
                    body = Loc.T("The camp has grown into a town, and its Litharch is now its king. Use the special "
                        + "building menu at the top of the screen to place the <b>Vault of Almiérra</b>. When it "
                        + "stands, your people become Alanthor.");
                    break;
                case AdvancedStage.Economy:
                    title = Loc.T("The Alanthor economy");
                    body = Loc.T("Your Gatherer's Huts are now <b>Guilds</b>: select one and level it up — every Guild "
                        + "levels with it and pays more. Every node runs dry in the end. Research <b>Trade Charters</b> at the Fortress: "
                        + "a <b>Trading Outpost</b> beside a node keeps it at its last unit, and pays you forever. "
                        + "The Litharch is now <b>King Lexor</b>, your hero.");
                    break;
                case AdvancedStage.Military:
                    title = Loc.T("The Alanthor army");
                    body = Loc.T("The Barracks is now the <b>Garrison</b>. You can also build the <b>Archery Range</b>, "
                        + "the <b>Royal Stable</b> and the <b>Siege Yard</b>. Levelling up one building levels every "
                        + "building of that type.");
                    break;
                case AdvancedStage.Defense:
                    title = Loc.T("Defend and expand");
                    body = Loc.T("The waves keep coming. <b>Stone Walls</b> close the way and <b>Watch Towers</b> shoot "
                        + "whatever comes near. To take new ground, stand soldiers in a territory that borders yours "
                        + "until its meter fills, then build there to hold it.");
                    break;
                case AdvancedStage.Rival:
                    title = Loc.T("A rival kingdom");
                    body = Loc.T("Another kingdom holds the east. It will not march on you. When two hostile armies share "
                        + "a territory, the stronger one pushes the meter. For now it stays out of reach.");
                    break;
                case AdvancedStage.Points:
                    title = Loc.T("Religion Points");
                    body = Loc.T("Every curse creature you kill earns religion points, and they turn into Religion Points. "
                        + "They are the key to victory. Keep your army at the walls and let the waves come.");
                    break;
                case AdvancedStage.Religion:
                    title = Loc.T("Religion");
                    body = Loc.T("Build the <b>Temple</b>, then press <b>R</b> to open the religion menu and adopt a sect of "
                        + "the House of Vaelith. A sect gives you powers and its own unit. Press <b>T</b> for the full tree.");
                    break;
                case AdvancedStage.Sanctum:
                    title = Loc.T("The cursed Sanctum");
                    body = Loc.T("The curse node stands in a Sanctum, marked on the minimap. Select your army, press "
                        + "<b>A</b> and right-click the node. A Sanctum you hold pays Religion Points and shortens your "
                        + "powers' cooldowns.");
                    break;
                case AdvancedStage.Ruins:
                    title = Loc.T("The monument");
                    body = Loc.T("A monument stands in this territory. Keep any unit inside its circle to claim it, then "
                        + "select it and pay to <b>Explore</b> its ruins. They grant a buff, and these ones show where "
                        + "the Shardroot lies.");
                    break;
                default:
                    title = Loc.T("Three ways to win");
                    body = string.Format(Loc.T("The rival is now your enemy. <b>Destroy</b> it. Or send a hero to take the "
                        + "Shardroot, bring it to your Temple and <b>enshrine</b> it for {0}. Or hold <b>every Sanctum</b> "
                        + "for {0}. Choose your road."), FormatMinutes(cfg.winCountdownSeconds));
                    break;
            }
        }

        private static string FormatMinutes(float seconds)
            => string.Format(Loc.T("{0} minutes"), Mathf.RoundToInt(seconds / 60f));
    }
}
