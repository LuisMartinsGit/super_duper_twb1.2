// ShardboundFury.cs
// The Shardbound King: what the Shardroot does to King Lexor while he bears
// it. Canon: docs/Design/Curse_And_Shardroot.md 3.1, "The Shardbound King".
//
//   * ShardboundKing        -- on Lexor while he carries the artifact; the
//                              empowerment marker (cleave, the ability, the
//                              detonation, and the near-invincibility of
//                              ShardboundKing.cs). ShardboundKingSystem adds
//                              and removes it as the artifact comes and goes.
//   * Launched              -- a unit in the air. LaunchSystem flies it on a
//                              parabola the sim owns (so every peer sees the
//                              same arc and the view simply follows the sim
//                              position), then slams it down for LandDamage.
//                              Launched units are excluded from the
//                              integrator, targeting and both attack systems.
//   * Cast / Detonate       -- the two events, both radial: hurl every hostile
//                              unit in range, damage every hostile building.
//
// Every number lives here, next to the card, not in the systems.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    // ShardboundKing (the marker on Lexor while he bears the artifact) lives
    // in ShardboundKing.cs with his invincibility (§ 3.1b).

    /// <summary>A unit hurled into the air. Immobile, cannot attack or be
    /// ordered until it lands; LandDamage applies on landing.</summary>
    public struct Launched : IComponentData
    {
        public float Elapsed;
        public float Duration;
        public float Height;
        public float3 From;      // where it left the ground
        public float3 To;        // where it lands (ground height resolved on landing)
        public int LandDamage;

        /// <summary>Who threw it: kill credit and the friendly-fire test.</summary>
        public Faction Source;
        /// <summary>Armor column the landing is measured against.</summary>
        public DamageType DamageKind;
        /// <summary>1 = the landing is a SPELL hit and goes through
        /// SpellDamage.Apply (Shardbound Fury). 0 = raw and lethal by design
        /// (the death detonation: "killed on landing", friend or foe).</summary>
        public byte SpellRouted;
    }

    public static class ShardboundFury
    {
        public const string AbilityName = "Shardbound Fury";

        // Cleaving blows: every hit also strikes hostiles within CleaveRadius
        // of the target for CleaveFraction of the damage.
        public const float CleaveRadius = 4f;
        public const float CleaveFraction = 0.6f;

        // The ability. Cooldown, radius and slam are the CARD's numbers (the
        // ShardboundFury AbilityDefSO is what runs); these constants only seed
        // the code-side card and must stay equal to the SO. On the spell
        // ladder (docs/Design/Spells.md 8): a hero ultimate, 120 s; the canon
        // Large radius, 25 m; the slam of a level-III Large power, 30 x 2 = 60.
        public const float FuryRadius = 25f;
        public const float FuryCooldown = 120f;
        public const float FuryMinHeight = 4f;        // at the rim
        public const float FuryMaxHeight = 10f;       // at the king's feet
        public const float FuryThrow = 3f;            // metres thrown outward
        public const float FuryFlightSeconds = 1.6f;
        public const int FurySlamDamage = 60;
        /// <summary>Flat damage to each hostile building in the radius: half a
        /// standard (600 HP) building -- a hero ultimate cracks a district, it
        /// does not level it (docs/Design/Spells.md 8.3).</summary>
        public const int FuryBuildingDamage = 300;

        // The death detonation.
        public const float DetonationRadius = 20f;
        public const float DetonationHeight = 12f;
        public const float DetonationThrow = 6f;
        public const float DetonationFlightSeconds = 2.2f;
        public const int DetonationBuildingDamage = 2500;
        /// <summary>Lethal for anything that lands.</summary>
        public const int DetonationLandDamage = 1 << 20;

        static readonly ComponentType[] QT_Victims =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Victims;

        /// <summary>Shardbound Fury: hurl every HOSTILE unit within FuryRadius
        /// of the caster, damage every hostile building in it.</summary>
        /// <summary>The slam damage and its type come off the ability CARD
        /// (Damage / DamageType, i.e. the ShardboundFury AbilityDefSO), so the
        /// SO is what runs; the geometry and the building figure live here.</summary>
        public static void Cast(EntityManager em, Entity caster,
            TheWaningBorder.Abilities.AbilityCard card)
        {
            if (!em.Exists(caster) || !em.HasComponent<LocalTransform>(caster)
                || !em.HasComponent<FactionTag>(caster)) return;
            var origin = em.GetComponentData<LocalTransform>(caster).Position;
            var faction = em.GetComponentData<FactionTag>(caster).Value;
            int slam = card != null ? (int)card.Damage : FurySlamDamage;
            DamageType type = card != null ? card.DamageType : DamageType.Magic;
            float radius = card != null && card.Radius > 0f ? card.Radius : FuryRadius;

            int units = Blast(em, origin, radius, caster, hostileOnly: true, faction,
                FuryMinHeight, FuryMaxHeight, FuryThrow, FuryFlightSeconds,
                slam, FuryBuildingDamage, spellRouted: true, type);

            SimSignals.Ping(origin, SimPingKind.Combat, radius, big: true);
            SimSignals.Notify(string.Format(Loc.T("{0}'s SHARDBOUND FURY hurls {1} into the air!"), faction, units));
            TWBLog.Log($"[Shardbound] {faction} Fury at ({origin.x:F0},{origin.z:F0}): {units} units launched");
        }

        /// <summary>The king died bearing the artifact: everything within
        /// DetonationRadius, friend or foe, is thrown skyward and killed on
        /// landing; buildings take DetonationBuildingDamage.</summary>
        public static void Detonate(EntityManager em, Entity king, float3 origin, Faction faction)
        {
            int units = Blast(em, origin, DetonationRadius, king, hostileOnly: false, faction,
                DetonationHeight, DetonationHeight, DetonationThrow, DetonationFlightSeconds,
                DetonationLandDamage, DetonationBuildingDamage, spellRouted: false, DamageType.True);

            SimSignals.Ping(origin, SimPingKind.Combat, DetonationRadius, big: true);
            SimSignals.Notify(Loc.T("The SHARDBOUND KING has fallen -- the Shardroot detonates!"));
            TWBLog.Log($"[Shardbound] {faction}'s king detonates at ({origin.x:F0},{origin.z:F0}): {units} units launched");
        }

        private static int Blast(EntityManager em, float3 origin, float radius, Entity self,
            bool hostileOnly, Faction faction, float minHeight, float maxHeight, float throwDist,
            float flightSeconds, int landDamage, int buildingDamage,
            bool spellRouted, DamageType dmgType)
        {
            float r2 = radius * radius;
            var q = QC_Victims.Get(em, QT_Victims);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);

            var launch = new NativeList<Entity>(Allocator.Temp);
            var launchData = new NativeList<Launched>(Allocator.Temp);
            var hitBuildings = new NativeList<Entity>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (e == self) continue;
                if (hps[i].Value <= 0) continue;
                if (hostileOnly && !Alliances.AreHostile(faction, facs[i].Value)) continue;
                var p = xfs[i].Position;
                float dx = p.x - origin.x, dz = p.z - origin.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;

                if (em.HasComponent<BuildingTag>(e))
                {
                    if (facs[i].Value != faction) hitBuildings.Add(e);
                    continue;
                }
                if (!em.HasComponent<UnitTag>(e)) continue;
                // An emplaced engine is bolted to its wall deck; launching it
                // would land it on the terrain, off its platform for good.
                if (em.HasComponent<EmplacedEngineTag>(e)) continue;
                if (em.HasComponent<Launched>(e)) continue;
                if (TransientState.Active<DeathAnimationState>(em, e)) continue;
                // The Shardbound King cannot be thrown (§ 3.1b).
                if (em.HasComponent<ShardboundKing>(e)) continue;

                float d = math.sqrt(d2);
                float t = radius > 0f ? math.saturate(d / radius) : 1f;   // 0 at the centre, 1 at the rim
                float height = math.lerp(maxHeight, minHeight, t);
                float2 dir = d > 0.01f ? new float2(dx, dz) / d : new float2(0f, 1f);
                float3 to = new float3(p.x + dir.x * throwDist, p.y, p.z + dir.y * throwDist);

                launch.Add(e);
                launchData.Add(new Launched
                {
                    Elapsed = 0f, Duration = flightSeconds, Height = height,
                    From = p, To = to, LandDamage = landDamage,
                    Source = faction, DamageKind = dmgType,
                    SpellRouted = (byte)(spellRouted ? 1 : 0),
                });
            }

            for (int i = 0; i < launch.Length; i++)
            {
                var e = launch[i];
                em.AddComponentData(e, launchData[i]);
                // Drop whatever it was doing: it is in the air.
                if (em.HasComponent<DesiredDestination>(e))
                {
                    var dd = em.GetComponentData<DesiredDestination>(e);
                    dd.Has = 0;
                    em.SetComponentData(e, dd);
                }
                if (em.HasComponent<Target>(e))
                    em.SetComponentData(e, new Target { Value = Entity.Null });
            }
            for (int i = 0; i < hitBuildings.Length; i++)
            {
                if (spellRouted)
                {
                    // The Fury is a spell: armor, Invulnerable and the ally
                    // test apply (docs/Design/Spells.md).
                    TheWaningBorder.Systems.Combat.SpellDamage.Apply(
                        em, hitBuildings[i], buildingDamage, dmgType, faction);
                    continue;
                }
                // Shield points are hit points (Combat_Pacing.md): spend them first.
                int left = TheWaningBorder.Systems.Combat.ShieldDamage.Absorb(em, hitBuildings[i], buildingDamage);
                var h = em.GetComponentData<Health>(hitBuildings[i]);
                h.Value = math.max(0, h.Value - left);
                em.SetComponentData(hitBuildings[i], h);
            }

            int count = launch.Length;
            launch.Dispose(); launchData.Dispose(); hitBuildings.Dispose();
            return count;
        }
    }
}
