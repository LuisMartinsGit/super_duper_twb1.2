using System;
using UnityEditor;
using TheWaningBorder.EditorTools.Inspector;

namespace TheWaningBorder.Scenarios.EditorTools
{
    /// <summary>
    /// Inspector chrome for PlasterDamageSubstance.Channel, which is edited as
    /// an array element.
    ///
    /// It sits in its own editor assembly rather than beside the other nested
    /// drawers because PlasterDamageSubstance lives in TWB.PlasterScenario,
    /// which pulls in Adobe.Substance. TheWaningBorder.Editor carries the
    /// release pipeline — if it stops compiling, builds stop — so it does not
    /// take a dependency on a scenario sandbox or a third-party package.
    /// </summary>
    [CustomPropertyDrawer(typeof(PlasterDamageSubstance.Channel))]
    public sealed class PlasterChannelDrawer : NestedMetadataDrawer
    {
        protected override Type Target => typeof(PlasterDamageSubstance.Channel);
    }
}
