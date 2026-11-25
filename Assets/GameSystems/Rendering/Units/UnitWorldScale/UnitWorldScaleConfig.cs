using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Unit World Scale",
                     fileName = "UnitWorldScale")]
    public sealed class UnitWorldScaleConfig : ScriptableObject, IComponentConfig
    {
        public float unitScale;
        public float eliteScale;
    }
}
