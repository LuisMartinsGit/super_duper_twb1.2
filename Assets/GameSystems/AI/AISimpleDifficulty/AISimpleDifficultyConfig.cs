using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// The difficulty ladder. The asset is AISimpleDifficulty.asset, beside
    /// AISimpleDifficulty.cs.
    ///
    /// It holds nothing but references: the numbers live one per tier in
    /// AISimpleDifficulty/Profiles/. This indirection exists because the
    /// component-config contract is ONE asset per config type, and a designer
    /// wants one asset per DIFFICULTY — so the single config is the list, and
    /// the tiers are separate assets it points at. Same shape as
    /// ComponentConfigCatalog and TechTreeCatalog: an asset in the loadable
    /// place referencing assets that sit beside their code.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AISimpleDifficulty",
                     fileName = "AISimpleDifficulty")]
    public sealed class AISimpleDifficultyConfig : ScriptableObject, IComponentConfig
    {
        static AISimpleDifficultyConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AISimpleDifficultyConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AISimpleDifficultyConfig>());

        /// <summary>Every tier's profile. Order does not matter — the lookup
        /// matches on each profile's own <see cref="AIDifficultyProfileSO.tier"/>.</summary>
        public AIDifficultyProfileSO[] profiles;

        /// <summary>
        /// The profile for a tier, or null when the ladder has no asset for it.
        ///
        /// Null is a DATA bug and the caller says so loudly, exactly as
        /// <see cref="ComponentConfig.Require{T}"/> does — there is no
        /// code-side default tier to fall back on.
        /// </summary>
        public AIDifficultyProfileSO Find(AIDifficulty d)
        {
            if (profiles == null) return null;
            for (int i = 0; i < profiles.Length; i++)
                if (profiles[i] != null && profiles[i].tier == d)
                    return profiles[i];
            return null;
        }
    }
}
