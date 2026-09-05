using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Tunables for the formation layer. The asset is FormationInput.asset,
    /// beside FormationInput.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Formation Input",
                     fileName = "FormationInput")]
    public sealed class FormationInputConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Metres between slots when a selection is sent as a formation.</summary>
        public float formationSpacing;

        /// <summary>Shape the match starts in, before the player cycles it.</summary>
        public FormationShape startingShape;
    }
}
