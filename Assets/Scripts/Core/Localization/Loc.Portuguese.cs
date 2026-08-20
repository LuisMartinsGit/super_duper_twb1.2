// Loc.Portuguese.cs
// Builds the Portuguese (European) translation table from per-domain
// partials. Keys are the ENGLISH source strings exactly as they appear at
// the call sites — see Loc.cs for why.
//
// Each domain lives in its own Loc.Pt.<Domain>.cs partial with one
// Add<Domain>(t) method, so translation passes over different UI areas
// never edit the same file. Inside those methods use the INDEXER
// (t["..."] = "...") and never .Add(): the same English string legitimately
// appears in several domains ("Cancel", "Back") and .Add would throw on
// the second one.
// Location: Assets/Scripts/Core/Localization/Loc.Portuguese.cs

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static readonly Dictionary<string, string> Pt = BuildPortuguese();

        private static Dictionary<string, string> BuildPortuguese()
        {
            var t = new Dictionary<string, string>(1400);
            AddOptions(t);
            AddMenus(t);
            AddGameUI(t);
            AddExtractors(t);
            AddNotifications(t);
            AddSects(t);
            AddTutorial(t);
            AddData(t);
            return t;
        }
    }
}
