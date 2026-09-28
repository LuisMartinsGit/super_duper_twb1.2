// EmplacementComponents.cs
// The emplacement pair (docs/Design/Age_1_Alanthor.md § Ballista and
// Trebuchet emplacements): a PLATFORM — a masonry curtain module the player
// mounts an engine on — and an ENGINE standing on it. They are two entities
// on purpose — the platform holds the ground, takes the repairs and owns the
// footprint; the engine shoots and can be killed off it without the position
// being lost. The engine never moves: it carries no MoveSpeed and no
// DesiredDestination, and it holds position for ever.
//
// A destroyed engine is NOT rebuilt for free (2026-09-25). The platform stays,
// empty, and its action panel sells a Replace Equipment order at the engine
// SO's cost and trainingTime; EmplacementCrewSystem raises the engine when
// that restore timer runs out.
//
// Set-level file: both emplacements share it, so it sits one level above
// their folders (CLAUDE.md § Set-level code sits one level above).

using Unity.Collections;
using Unity.Entities;

/// <summary>Marks the PLATFORM half of an emplacement.</summary>
public struct EmplacementTag : IComponentData { }

/// <summary>
/// The platform's crew state. <see cref="Engine"/> is the live engine
/// entity, or Entity.Null while there is none. A platform with no engine is
/// EMPTY until its owner pays for a Replace Equipment order, which starts
/// <see cref="Restore"/>.
/// </summary>
public struct EmplacementCrew : IComponentData
{
    public Entity Engine;
    /// <summary>Seconds left on a paid Replace Equipment restore. 0 = none running.</summary>
    public float Restore;
    /// <summary>The running restore's full length, for the UI's countdown.</summary>
    public float RestoreTime;
    /// <summary>Which engine this platform mounts (an "Alanthor_Emplaced*" unit id).</summary>
    public FixedString32Bytes EngineId;
    /// <summary>How high above the platform's origin the engine stands.</summary>
    public float MountHeight;
    /// <summary>1 once the platform has raised its first engine. The first
    /// engine comes with the mount; every later one is paid for.</summary>
    public byte Raised;
}

/// <summary>Marks the ENGINE half — an immobile siege piece bolted to a
/// platform. Used by the order paths to refuse a move, and by the
/// platform's death to take its engine with it.</summary>
public struct EmplacedEngineTag : IComponentData { }

/// <summary>Back-pointer from the engine to the platform it stands on.</summary>
public struct EmplacedOn : IComponentData
{
    public Entity Emplacement;
}

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// The Replace Equipment rule, in one place for the executor, the crew
    /// system and the action panel. The price and the restore time are the
    /// ENGINE's own SO numbers (<c>cost</c> / <c>trainingTime</c>) — nothing
    /// here holds a number.
    /// </summary>
    public static class EmplacementEquipment
    {
        /// <summary>True when the platform has lost its engine: it raised
        /// one, and that one is gone.</summary>
        public static bool IsEmpty(EntityManager em, Entity platform)
        {
            if (platform == Entity.Null || !em.Exists(platform)) return false;
            if (!em.HasComponent<EmplacementTag>(platform) || !em.HasComponent<EmplacementCrew>(platform))
                return false;
            var c = em.GetComponentData<EmplacementCrew>(platform);
            if (c.Raised == 0) return false;   // the mount's own engine is still coming
            return c.Engine == Entity.Null || !em.Exists(c.Engine);
        }

        /// <summary>True while a paid restore is counting down.</summary>
        public static bool IsRestoring(EntityManager em, Entity platform)
            => platform != Entity.Null && em.Exists(platform)
               && em.HasComponent<EmplacementCrew>(platform)
               && em.GetComponentData<EmplacementCrew>(platform).Restore > 0f;

        /// <summary>Everything the executor re-checks before it spends:
        /// an owned, finished platform whose engine is gone and not already
        /// being restored.</summary>
        public static bool CanReplace(EntityManager em, Entity platform)
            => IsEmpty(em, platform)
               && !IsRestoring(em, platform)
               && !em.HasComponent<UnderConstruction>(platform)
               && em.HasComponent<FactionTag>(platform);

        /// <summary>The engine id this platform mounts, or null.</summary>
        public static string EngineIdOf(EntityManager em, Entity platform)
            => em.HasComponent<EmplacementCrew>(platform)
                ? em.GetComponentData<EmplacementCrew>(platform).EngineId.ToString()
                : null;

        /// <summary>The Replace Equipment price: the engine SO's cost.</summary>
        public static TheWaningBorder.Core.Cost CostOf(string engineId)
        {
            var def = TechCatalog.Unit(engineId);
            if (def.cost == null) return default;
            return TheWaningBorder.Core.Cost.Of(
                supplies: def.cost.Supplies,
                iron: def.cost.Iron,
                veilstone: def.cost.Veilstone,
                veilsteel: def.cost.Veilsteel);
        }

        /// <summary>The Replace Equipment timer: the engine SO's trainingTime.</summary>
        public static float SecondsOf(string engineId) => TechCatalog.Unit(engineId).trainingTime;
    }
}
