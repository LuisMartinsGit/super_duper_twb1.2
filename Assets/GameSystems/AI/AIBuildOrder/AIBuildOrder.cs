// AIBuildOrder.cs
// The personality's PRIOR culture lean — the one thing left of the scripted
// per-personality Age 0 build orders.
//
// The six step arrays (EcoBoom / Balanced / TechBoom / Rush / Turtle /
// Defensive), BuildOrderStep, BuildStepKind, For() and CultureFor() were
// deleted on 2026-10-05: SimpleAISystem's step pointer was never advanced,
// so no step of any order was ever issued and the one reader (the
// advancement-gate test) was always false. Every opening the AI actually
// plays is the maintenance loop driven by its personality row in
// Resources/AISettings.asset (AISettingsSO.PersonalityBlock) and its
// strategic plan (AIPlan).

namespace TheWaningBorder.AI
{
    public static class AIBuildOrder
    {
        /// <summary>
        /// The personality's PRIOR culture preference — what it leans toward
        /// before it has looked at the map. Used as the base score by
        /// <see cref="AICultureChoice"/>, which then bends it with scouted
        /// intel.
        ///
        /// Runai is deliberately absent: it is still an incomplete culture
        /// (CultureConfig.IsComingSoon locks it for the player too), so the
        /// AI must never pick it. Restore it here when Runai ships.
        ///
        /// Returns a signed lean: negative = Alanthor, positive = Feraldis.
        /// </summary>
        public static float CultureLeanFor(AIPersonality personality) => personality switch
        {
            // Aggression wants the raiding culture.
            AIPersonality.Rush       => +2.0f,
            AIPersonality.Balanced   => +1.5f,
            // Balanced/eco lean slightly to the fortified culture.
            AIPersonality.Economic   => -0.5f,
            AIPersonality.TechBoom   => -0.5f,
            // Defensive play wants walls and towers.
            AIPersonality.Defensive  => -2.0f,
            AIPersonality.Turtle     => -2.5f,
            _                        => 0f,
        };
    }
}
