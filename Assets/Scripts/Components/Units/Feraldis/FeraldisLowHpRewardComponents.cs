// FeraldisLowHpRewardComponents.cs
// ECS components lifted out of FeraldisLowHpRewardSystem.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Economy;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Economy
{
        /// <summary>
        /// Per-building snapshot for the Feraldis low-HP-damage reward. Added
        /// lazily on first observation, never removed (cheap stale-but-correct
        /// behavior across destroys; entity destruction takes the snapshot with it).
        /// </summary>
        public struct BuildingHpSnapshot : IComponentData
        {
            public int LastObservedHealth;
        }

}
