// WallGarrison.cs
// Putting men inside a reinforced curtain module and taking them out again
// (docs/Design/Age_1_Alanthor.md § Garrison slots). Level 3 only: two slots
// per module, foot units only.
//
// A garrisoned unit is ABSORBED — disabled and its view dropped — rather
// than parked on the crown. A unit standing on a 2.6 m curtain is at the
// mercy of steering, avoidance and every system that writes a unit's Y; an
// absorbed one cannot drift, cannot be pushed off and cannot fight the nav
// layers. What the player sees is the module's own visual: a manned shield
// position per occupant. It still costs population (PopulationSyncSystem
// counts disabled units for exactly this reason) and its damage is added to
// the module's own fire.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    public static class WallGarrison
    {
        /// <summary>Reach of a manned wall, metres.</summary>
        public const float FireRange = 18f;
        /// <summary>Seconds between volleys from a manned wall.</summary>
        public const float FireCooldown = 2.0f;
        /// <summary>Line of sight a manned module sees at.</summary>
        public const float MannedLineOfSight = 14f;

        /// <summary>True when the module has a slot nobody is in.</summary>
        public static bool HasFreeSlot(EntityManager em, Entity module)
        {
            if (module == Entity.Null || !em.Exists(module)) return false;
            if (!em.HasBuffer<WallGarrisonSlot>(module)) return false;
            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Occupant == Entity.Null || !em.Exists(slots[i].Occupant)) return true;
            return false;
        }

        /// <summary>How many of the module's slots are filled.</summary>
        public static int OccupantCount(EntityManager em, Entity module)
        {
            if (module == Entity.Null || !em.Exists(module)) return 0;
            if (!em.HasBuffer<WallGarrisonSlot>(module)) return 0;
            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Occupant != Entity.Null && em.Exists(slots[i].Occupant)) n++;
            return n;
        }

        /// <summary>Total slots the module has (0 below level 3).</summary>
        public static int SlotCount(EntityManager em, Entity module)
            => module != Entity.Null && em.Exists(module) && em.HasBuffer<WallGarrisonSlot>(module)
               ? em.GetBuffer<WallGarrisonSlot>(module).Length : 0;

        /// <summary>
        /// Foot units only — infantry and archers. The same roster the
        /// wall-top order has always used: no cavalry, no siege, no workers,
        /// no scouts.
        /// </summary>
        public static bool IsFootUnit(EntityManager em, Entity unit)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return false;
            if (!em.HasComponent<UnitTag>(unit) || em.HasComponent<BuildingTag>(unit)) return false;
            if (em.HasComponent<CanBuild>(unit) || em.HasComponent<MinerTag>(unit)) return false;
            if (em.HasComponent<CavalryTag>(unit)) return false;
            if (em.HasComponent<EmplacedEngineTag>(unit)) return false;
            var cls = em.GetComponentData<UnitTag>(unit).Class;
            return cls != UnitClass.Siege && cls != UnitClass.Scout;
        }

        /// <summary>Every gate this order has to pass before a unit walks in.</summary>
        public static bool CanGarrison(EntityManager em, Entity unit, Entity module)
        {
            if (!IsFootUnit(em, unit)) return false;
            if (em.HasComponent<WallGarrisonedIn>(unit)) return false;
            if (!HasFreeSlot(em, module)) return false;
            if (em.HasComponent<UnderConstruction>(module)) return false;
            if (!em.HasComponent<FactionTag>(unit) || !em.HasComponent<FactionTag>(module)) return false;
            return em.GetComponentData<FactionTag>(unit).Value
                == em.GetComponentData<FactionTag>(module).Value;
        }

        /// <summary>
        /// Put <paramref name="unit"/> in the module's first free slot.
        /// Structural (the unit is disabled): call outside query iteration.
        /// </summary>
        public static void Enter(EntityManager em, Entity unit, Entity module)
        {
            if (!CanGarrison(em, unit, module)) return;

            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            int free = -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Occupant == Entity.Null || !em.Exists(slots[i].Occupant)) { free = i; break; }
            if (free < 0) return;
            slots[free] = new WallGarrisonSlot { Occupant = unit };

            em.AddComponentData(unit, new WallGarrisonedIn { Module = module });
            // Drop the view first: once the entity is disabled the spawn
            // query no longer sees it, so nothing would ever come back for it.
            var spawn = PresentationSpawnSystem.Instance;
            if (spawn != null) spawn.ForceRespawn(unit);
            em.AddComponent<Disabled>(unit);

            RefreshFire(em, module);
            Reclad(em, module);
        }

        /// <summary>The module draws one manned position per occupant, so a
        /// garrison change means its view is rebuilt.</summary>
        static void Reclad(EntityManager em, Entity module)
        {
            var spawn = PresentationSpawnSystem.Instance;
            if (spawn != null && em.Exists(module)) spawn.ForceRespawn(module);
        }

        /// <summary>
        /// Take everyone out of <paramref name="module"/> and stand them on
        /// the ground beside it. Structural: call outside query iteration.
        /// </summary>
        public static void EmptyModule(EntityManager em, Entity module)
        {
            if (module == Entity.Null || !em.Exists(module)) return;
            if (!em.HasBuffer<WallGarrisonSlot>(module)) return;

            var occupants = new List<Entity>();
            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Occupant != Entity.Null && em.Exists(slots[i].Occupant))
                    occupants.Add(slots[i].Occupant);
                slots[i] = new WallGarrisonSlot { Occupant = Entity.Null };
            }
            if (occupants.Count == 0) { RefreshFire(em, module); Reclad(em, module); return; }

            float3 at = em.HasComponent<LocalTransform>(module)
                ? em.GetComponentData<LocalTransform>(module).Position : float3.zero;
            quaternion rot = em.HasComponent<LocalTransform>(module)
                ? em.GetComponentData<LocalTransform>(module).Rotation : quaternion.identity;
            // -X is the INNER (friendly) face of a wall module: the men step
            // back down on their own side, never into the enemy's.
            float3 inward = math.mul(rot, new float3(-1f, 0f, 0f));

            for (int i = 0; i < occupants.Count; i++)
            {
                var u = occupants[i];
                if (!em.Exists(u)) continue;
                float3 spot = at + inward * 2.2f + math.mul(rot, new float3(0f, 0f, (i - 0.5f) * 1.4f));
                spot.y = TerrainUtility.GetHeight(spot.x, spot.z);

                if (em.HasComponent<Disabled>(u)) em.RemoveComponent<Disabled>(u);
                if (em.HasComponent<WallGarrisonedIn>(u)) em.RemoveComponent<WallGarrisonedIn>(u);
                if (em.HasComponent<LocalTransform>(u))
                {
                    var t = em.GetComponentData<LocalTransform>(u);
                    em.SetComponentData(u, LocalTransform.FromPositionRotationScale(spot, t.Rotation, t.Scale));
                }
            }

            RefreshFire(em, module);
            Reclad(em, module);
        }

        /// <summary>
        /// Rebuild the module's fire from the men actually inside it, and
        /// drop dead occupants out of their slots. A module with nobody in it
        /// loses the attack entirely — the wall does not shoot on its own.
        /// </summary>
        public static void RefreshFire(EntityManager em, Entity module)
        {
            if (module == Entity.Null || !em.Exists(module)) return;
            if (!em.HasBuffer<WallGarrisonSlot>(module)) return;

            var slots = em.GetBuffer<WallGarrisonSlot>(module);
            int manned = 0, damage = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var occ = slots[i].Occupant;
                if (occ == Entity.Null) continue;
                if (!em.Exists(occ)) { slots[i] = new WallGarrisonSlot { Occupant = Entity.Null }; continue; }
                manned++;
                damage += em.HasComponent<Damage>(occ) ? em.GetComponentData<Damage>(occ).Value : 8;
            }

            if (manned == 0)
            {
                if (em.HasComponent<BuildingRangedAttack>(module))
                    em.RemoveComponent<BuildingRangedAttack>(module);
                return;
            }

            float timer = em.HasComponent<BuildingRangedAttack>(module)
                ? em.GetComponentData<BuildingRangedAttack>(module).Timer : 0f;
            em.AddComponentData(module, new BuildingRangedAttack
            {
                Range = FireRange,
                Damage = damage,
                Cooldown = FireCooldown,
                Timer = timer,
                MaxTargets = manned,
            });
            if (!em.HasComponent<DamageTypeData>(module))
                em.AddComponentData(module, new DamageTypeData { Value = DamageType.Ranged });
            if (em.HasComponent<LineOfSight>(module))
            {
                var los = em.GetComponentData<LineOfSight>(module);
                if (los.Radius < MannedLineOfSight)
                    em.SetComponentData(module, new LineOfSight { Radius = MannedLineOfSight });
            }
        }
    }
}
