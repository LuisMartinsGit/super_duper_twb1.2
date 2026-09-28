using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Abilities
{
    /// <summary>
    /// Numbers for <see cref="AlanthorActiveHelper"/>'s Ranging Shot (the
    /// Siege Yard's aimed-shot active). The asset is AlanthorActiveHelper.asset,
    /// beside the class. On the spell ladder (docs/Design/Spells.md 8-9) it
    /// is a unit tactical active.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Abilities/AlanthorActiveHelper",
                     fileName = "AlanthorActiveHelper")]
    public sealed class AlanthorActiveHelperConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>+% damage on the next shot (100 = double).</summary>
        public float rangingShotPct;
        /// <summary>Seconds the loaded shot stays armed.</summary>
        public float rangingShotWindow;
        /// <summary>Per-faction cooldown, seconds.</summary>
        public float rangingShotCooldown;
    }
}
