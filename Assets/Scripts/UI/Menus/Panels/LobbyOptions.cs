// LobbyOptions.cs
// The match-option vocabulary both lobbies offer: starting resources, and the
// starting age / culture ladder.
// Location: Assets/Scripts/UI/Menus/Panels/LobbyOptions.cs
//
// ONE source, deliberately. These lists used to live as private statics in
// SkirmishPanel, and the multiplayer lobby simply did not offer the options at
// all. Giving multiplayer its own copy would have set up exactly the failure
// SkirmishPanel's own comments warn about in capitals: an age label that
// promises one culture while the ship gate quietly starts another. That bug
// shipped once already, from a hard-coded "(FER)" suffix. The label and the
// behaviour now come from the same call for both screens.

using TheWaningBorder.Core.Config;

namespace TheWaningBorder.UI.Menus.Panels
{
    public static class LobbyOptions
    {
        public static readonly string[] ResourceNames = { "NORMAL", "MAX" };

        /// <summary>AI personalities, index-parallel with LobbyAIStrategy.</summary>
        public static readonly string[] StrategyNames =
            { "RANDOM", "ECONOMIST", "BALANCED", "TECHNOLOGIST", "AGGRESSOR", "TURTLE", "DEFENDER" };

        /// <summary>Culture each age entry starts every faction in, BEFORE the
        /// ship gate. Index-parallel with <see cref="AgeBaseNames"/>.</summary>
        private static readonly byte[] AgeCultures =
        {
            Cultures.None,      // AGE 0 — no promotion, culture chosen in play
            Cultures.Alanthor,
            Cultures.Alanthor,
            Cultures.Alanthor,
            Cultures.Feraldis,  // AGE 4 — top of the ladder, both verb gates open
        };

        private static readonly string[] AgeBaseNames =
            { "AGE 0", "AGE 1", "AGE 2", "AGE 3", "AGE 4" };

        /// <summary>
        /// Age entries as the player sees them. The culture suffix is DERIVED
        /// from the gated culture, never written by hand — see the file header.
        /// Declared after the arrays it reads: static field initialisers run in
        /// declaration order.
        /// </summary>
        public static readonly string[] AgeNames = BuildAgeNames();

        private static string[] BuildAgeNames()
        {
            var names = new string[AgeBaseNames.Length];
            for (int i = 0; i < names.Length; i++)
            {
                byte culture = i < AgeCultures.Length
                    ? CultureConfig.Playable(AgeCultures[i])
                    : Cultures.None;
                string suffix = CultureSuffix(culture);
                names[i] = suffix.Length == 0 ? AgeBaseNames[i] : $"{AgeBaseNames[i]} ({suffix})";
            }
            return names;
        }

        private static string CultureSuffix(byte culture) => culture switch
        {
            Cultures.Alanthor => "AL",
            Cultures.Feraldis => "FER",
            Cultures.Runai    => "RU",
            _                 => string.Empty,
        };

        /// <summary>
        /// Culture for a start age, read straight from AgeCultures and put
        /// through the ship gate — so what the dropdown promises is what the
        /// match actually starts in.
        ///
        /// DERIVED, never stored. An earlier version of the skirmish panel kept
        /// a _startCulture field updated from the dropdown's onValueChanged;
        /// SyncOptions restores the saved age with SetValueWithoutNotify, which
        /// by design does NOT fire that listener, so reopening the lobby on a
        /// saved "AGE 4 (FER)" left the age at 4 and the culture at its Alanthor
        /// default. That shipped exactly once and produced a match of four
        /// Alanthor players from an option labelled (FER).
        /// </summary>
        public static byte CultureForAge(SkirmishStartAge age)
        {
            int i = (int)age;
            byte culture = i >= 0 && i < AgeCultures.Length ? AgeCultures[i] : Cultures.Alanthor;
            return CultureConfig.Playable(culture);
        }
    }
}
