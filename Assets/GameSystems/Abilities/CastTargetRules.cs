// CastTargetRules.cs
// The one test of "may this single-target spell be cast on that entity" —
// docs/Design/Spells.md §6.
//
// Asked by the client while the player aims (EntityTargeting, to colour the
// outline and explain a refusal) and again by the simulation on every peer
// before a cooldown is charged (AbilityLifecycleSystem for cards,
// SectActivePowerHelper.Fire for sect powers). The same function answers
// both, so a pick the cursor shows green is never dropped in the sim — and a
// pick that became illegal on the way (the target died, changed hands) is
// dropped on every peer alike.
//
// Pure function of ECS state: no client-only input (fog is the CLIENT's extra
// test, in EntityTargeting, because it is the local player's view; the
// SIMULATION's sight rule, Spells.md §6.1, is FactionSight — the
// AbilityTargetRules sight helpers below).

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Abilities
{
    /// <summary>Why a target was refused. None = legal.</summary>
    public enum CastTargetRefusal : byte
    {
        None = 0,
        NoTarget,        // the ground, or nothing
        Gone,            // the entity no longer exists
        Dead,
        NotAUnit,
        NotABuilding,
        NotEnemy,
        NotOwn,
        NotFriendly,
        Hidden,          // veiled (Stoneveil) — untargetable by its enemies
        Wall,
        Curse,           // a curse structure, where the power spares them
        Capital,
        Unfinished,
        AlreadyAffected,
        NoProduction,
        OutOfRange,
        NotInSight,      // outside the caster's (and allies') line of sight — Spells.md §6.1
    }

    public static class CastTargetRules
    {
        public static bool WantsUnit(CastTargetKind k) => k switch
        {
            CastTargetKind.EnemyUnit           => true,
            CastTargetKind.OwnUnit             => true,
            CastTargetKind.FriendlyUnit        => true,
            CastTargetKind.AnyUnit             => true,
            CastTargetKind.EnemyUnitOrBuilding => true,
            _                                  => false,
        };

        public static bool WantsBuilding(CastTargetKind k) => k switch
        {
            CastTargetKind.EnemyBuilding       => true,
            CastTargetKind.OwnBuilding         => true,
            CastTargetKind.FriendlyBuilding    => true,
            CastTargetKind.AnyBuilding         => true,
            CastTargetKind.EnemyUnitOrBuilding => true,
            _                                  => false,
        };

        private enum Side : byte { Any, Enemy, Own, Friendly }

        private static Side SideOf(CastTargetKind k) => k switch
        {
            CastTargetKind.EnemyUnit           => Side.Enemy,
            CastTargetKind.EnemyBuilding       => Side.Enemy,
            CastTargetKind.EnemyUnitOrBuilding => Side.Enemy,
            CastTargetKind.OwnUnit             => Side.Own,
            CastTargetKind.OwnBuilding         => Side.Own,
            CastTargetKind.FriendlyUnit        => Side.Friendly,
            CastTargetKind.FriendlyBuilding    => Side.Friendly,
            _                                  => Side.Any,
        };

        public static bool IsWall(EntityManager em, Entity e)
            => em.HasComponent<WallTag>(e) || em.HasComponent<PalisadeTag>(e);

        public static bool IsCapital(EntityManager em, Entity e)
            => em.HasComponent<HallTag>(e) || em.HasComponent<FortressTag>(e);

        /// <summary>
        /// The generic test: the entity exists, is alive, is a unit / building
        /// as <paramref name="kind"/> asks, and stands on the side it asks
        /// (enemy = Alliances.AreHostile, own = the caster's faction, friendly
        /// = Alliances.AreAllied). A spell's own extra rules (no walls, not
        /// the capital, ...) are layered on top by its pipeline.
        /// </summary>
        public static bool Check(EntityManager em, CastTargetKind kind, Faction caster,
            Entity target, out CastTargetRefusal why)
        {
            why = CastTargetRefusal.None;
            if (kind == CastTargetKind.None) return true;
            if (target == Entity.Null) { why = CastTargetRefusal.NoTarget; return false; }
            if (!em.Exists(target)) { why = CastTargetRefusal.Gone; return false; }
            if (!em.HasComponent<FactionTag>(target) || !em.HasComponent<LocalTransform>(target))
            { why = CastTargetRefusal.NoTarget; return false; }

            bool isUnit = em.HasComponent<UnitTag>(target);
            bool isBuilding = em.HasComponent<BuildingTag>(target);
            if (!isUnit && !isBuilding) { why = CastTargetRefusal.NoTarget; return false; }
            if (isUnit && !WantsUnit(kind)) { why = CastTargetRefusal.NotABuilding; return false; }
            if (isBuilding && !WantsBuilding(kind)) { why = CastTargetRefusal.NotAUnit; return false; }

            if ((em.HasComponent<Health>(target) && em.GetComponentData<Health>(target).Value <= 0)
                || TransientState.Active<DeathAnimationState>(em, target))
            { why = CastTargetRefusal.Dead; return false; }

            var owner = em.GetComponentData<FactionTag>(target).Value;
            switch (SideOf(kind))
            {
                case Side.Enemy:
                    if (!Alliances.AreHostile(caster, owner)) { why = CastTargetRefusal.NotEnemy; return false; }
                    // Stoneveil: untargetable by its enemies.
                    if (TransientState.Active<SectVeiled>(em, target)) { why = CastTargetRefusal.Hidden; return false; }
                    break;
                case Side.Own:
                    if (owner != caster) { why = CastTargetRefusal.NotOwn; return false; }
                    break;
                case Side.Friendly:
                    if (!Alliances.AreAllied(caster, owner) || owner == Faction.Border)
                    { why = CastTargetRefusal.NotFriendly; return false; }
                    break;
            }
            return true;
        }

        /// <summary>The aiming hint: what the player should click.</summary>
        public static string Prompt(CastTargetKind kind) => kind switch
        {
            CastTargetKind.EnemyUnit           => "Pick an enemy unit",
            CastTargetKind.OwnUnit             => "Pick one of your units",
            CastTargetKind.FriendlyUnit        => "Pick a friendly unit",
            CastTargetKind.AnyUnit             => "Pick a unit",
            CastTargetKind.EnemyBuilding       => "Pick an enemy building",
            CastTargetKind.OwnBuilding         => "Pick one of your buildings",
            CastTargetKind.FriendlyBuilding    => "Pick a friendly building",
            CastTargetKind.AnyBuilding         => "Pick a building",
            CastTargetKind.EnemyUnitOrBuilding => "Pick an enemy unit or building",
            _                                  => "Pick a target",
        };

        /// <summary>
        /// The refusal message (English source string; the UI localises it).
        /// A click on the wrong kind or side repeats the prompt, which is the
        /// most useful thing to say about it.
        /// </summary>
        public static string Describe(CastTargetRefusal why, CastTargetKind kind) => why switch
        {
            CastTargetRefusal.Dead            => "That target is dead",
            CastTargetRefusal.Gone            => "That target is gone",
            CastTargetRefusal.Hidden          => "That target cannot be seen",
            CastTargetRefusal.Wall            => "Walls cannot be targeted",
            CastTargetRefusal.Curse           => "Curse structures cannot be targeted",
            CastTargetRefusal.Capital         => "Not on your capital",
            CastTargetRefusal.Unfinished      => "That building is not finished",
            CastTargetRefusal.AlreadyAffected => "That target is already affected",
            CastTargetRefusal.NoProduction    => "That building trains nothing",
            CastTargetRefusal.OutOfRange      => "Out of range",
            CastTargetRefusal.NotInSight      => "You cannot see there",
            _                                 => Prompt(kind),
        };
    }

    /// <summary>
    /// The ability-card layer on top of <see cref="CastTargetRules"/>: the
    /// card's range, and the few cards with a rule of their own.
    /// </summary>
    public static class AbilityTargetRules
    {
        public static bool Check(EntityManager em, AbilityCard card, Entity caster, Entity target,
            out CastTargetRefusal why)
        {
            why = CastTargetRefusal.None;
            if (card == null || card.TargetKind == CastTargetKind.None) return true;
            if (caster == Entity.Null || !em.Exists(caster) || !em.HasComponent<FactionTag>(caster))
            { why = CastTargetRefusal.NoTarget; return false; }

            var faction = em.GetComponentData<FactionTag>(caster).Value;
            if (!CastTargetRules.Check(em, card.TargetKind, faction, target, out why)) return false;

            // Liquidate: the capital cannot be liquidated (Sects.md §4).
            if (card.HasEffect(AbilityEffectKind.SectLiquidate) && CastTargetRules.IsCapital(em, target))
            { why = CastTargetRefusal.Capital; return false; }

            // Range 0 = unlimited. Measured to the target's footprint edge.
            if (card.Range > 0f && em.HasComponent<LocalTransform>(caster))
            {
                var from = em.GetComponentData<LocalTransform>(caster).Position;
                if (TargetGeometry.SurfaceDistXZ(em, from, target) > card.Range)
                { why = CastTargetRefusal.OutOfRange; return false; }
            }
            return true;
        }

        // ── Line of sight (docs/Design/Spells.md §6.1, 2026-10-19) ──────────
        // The sect powers' rule, extended to ability cards: a ground-aimed
        // card needs its point in the caster faction's (or an ally's) sight,
        // an entity-targeted card needs an ENEMY target in sight (own / allied
        // targets always are). The simulation's test (FactionSight), so the
        // aiming UI, the AI and AbilityLifecycleSystem agree on every peer.

        /// <summary>A reveal looks INTO the fog; gating it on sight would make
        /// it pointless (Use Celestar, Scout Sight, any RevealFog card).</summary>
        public static bool IsReveal(AbilityCard card)
            => card != null && (card.HasEffect(AbilityEffectKind.RevealFog)
                                || card.HasEffect(AbilityEffectKind.LosRampWhileStill));

        /// <summary>
        /// Does a cast of <paramref name="card"/> need sight at all? Only an
        /// ACTIVE card with an aim: a ground point (Area + AimedAtPoint) or an
        /// entity (TargetKind). Passives, auras, self-casts, caster-centred
        /// areas and reveals are exempt.
        /// </summary>
        public static bool NeedsSight(AbilityCard card)
            => card != null
               && card.Activation == AbilityActivation.Active
               && !IsReveal(card)
               && (card.IsEntityTargeted
                   || (card.Targeting == AbilityTargeting.Area && card.AimedAtPoint));

        /// <summary>The caster's faction, or false when there is none. The
        /// curse (Faction.Border) has no fog and is never sight-gated.</summary>
        private static bool SightGated(EntityManager em, Entity caster, out Faction faction)
        {
            faction = default;
            if (caster == Entity.Null || !em.Exists(caster) || !em.HasComponent<FactionTag>(caster)) return false;
            faction = em.GetComponentData<FactionTag>(caster).Value;
            return faction != Faction.Border;
        }

        /// <summary>
        /// A ground-aimed card aimed at <paramref name="point"/>: true when the
        /// card needs no sight, or the caster's faction (or an ally) sees the
        /// point right now. <paramref name="fresh"/> as FactionSight.CanSee
        /// (the authoritative sim caller passes it).
        /// </summary>
        public static bool AimInSight(EntityManager em, AbilityCard card, Entity caster, float3 point,
            bool fresh = false)
        {
            if (!NeedsSight(card) || card.IsEntityTargeted) return true;
            if (!SightGated(em, caster, out var faction)) return true;
            return TheWaningBorder.World.FogOfWar.FactionSight.CanSee(em, faction, point, fresh);
        }

        /// <summary>
        /// <see cref="Check"/> plus the sight rule — what a CAST must pass (the
        /// aiming UI, the AI's pick). An unseen enemy is refused with
        /// <see cref="CastTargetRefusal.NotInSight"/>.
        /// </summary>
        public static bool CheckInSight(EntityManager em, AbilityCard card, Entity caster, Entity target,
            out CastTargetRefusal why, bool fresh = false)
        {
            if (!Check(em, card, caster, target, out why)) return false;
            if (card == null || !card.IsEntityTargeted || !NeedsSight(card)) return true;
            if (!SightGated(em, caster, out var faction)) return true;
            if (!TheWaningBorder.Systems.Sect.SectActivePowerHelper.TargetInSight(em, faction, target, fresh))
            { why = CastTargetRefusal.NotInSight; return false; }
            return true;
        }

        /// <summary>
        /// The simulation's sight test when a cast STARTS (AbilityLifecycleSystem,
        /// before the cooldown is charged): the entity target, or the aim point
        /// stamped on the caster (AbilityAimPoint). A card with no stamped
        /// point lands on the caster, which is in sight by definition. The
        /// channel-completion re-check does not ask this — a target that walks
        /// into the fog during the wind-up is still struck, as for sect powers.
        /// </summary>
        public static bool CastInSight(EntityManager em, AbilityCard card, Entity caster, Entity target,
            bool fresh = false)
        {
            if (!NeedsSight(card)) return true;
            if (!SightGated(em, caster, out var faction)) return true;
            if (card.IsEntityTargeted)
                return TheWaningBorder.Systems.Sect.SectActivePowerHelper.TargetInSight(em, faction, target, fresh);
            if (!em.HasComponent<AbilityAimPoint>(caster)) return true;
            return TheWaningBorder.World.FogOfWar.FactionSight.CanSee(
                em, faction, em.GetComponentData<AbilityAimPoint>(caster).Position, fresh);
        }
    }
}
