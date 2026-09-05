// WarTotemAuraComponents.cs
// ECS components lifted out of WarTotemAuraSystem.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Influence;
using TheWaningBorder.Core.Localization;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.World
{
        /// <summary>
        /// Seconds a totem has been standing on dry ground. Present only while it
        /// is starving; removed the moment it finds blood again.
        /// </summary>
        public struct TotemStarving : IComponentData
        {
            public float DrySeconds;
        }

}
