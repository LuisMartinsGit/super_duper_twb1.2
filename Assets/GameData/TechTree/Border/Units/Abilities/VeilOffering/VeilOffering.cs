// VeilOffering.cs
// THE VEIL OFFERING (Territory_Claims.md §6.9.5, 2026-10-16): every curse
// creature can channel for the card's CastTime, then die, and the ground
// within the card's Radius x its claim size (crystalling 1, veilstinger 2,
// godsplinter 3) of where it died turns cursed at once. Only death or a stun
// breaks the channel (AbilityLifecycleSystem.Unbreakable). The death pays no
// kill to anyone.
//
// The card is data — VeilOffering.asset beside this file, granted through the
// three unit SOs' abilities lists. The curse AI fires it from its merge
// parties (CurseTerritorySystem.Living) through Begin.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Border
{
    public static class VeilOffering
    {
        public const string AbilityName = "Veil Offering";

        /// <summary>Claim size: the creature's weight on the ground.</summary>
        public static int SizeOf(EntityManager em, Entity e)
        {
            if (em.HasComponent<GodsplinterState>(e)) return 3;
            if (em.HasComponent<VeilstingerState>(e)) return 2;
            return 1;
        }

        /// <summary>Is the creature channelling the offering now?</summary>
        public static bool IsChannelling(EntityManager em, Entity e)
        {
            if (!TransientState.Active<AbilityCastState>(em, e)) return false;
            var card = AbilityCatalog.Get(em.GetComponentData<AbilityCastState>(e).AbilityIndex);
            return card != null && card.HasEffect(AbilityEffectKind.CurseVeilOffering);
        }

        /// <summary>
        /// Start the offering: the creature drops its orders, holds where it
        /// stands (Hold stance: it fights only what reaches it) and begins
        /// the channel. False when it cannot (dead, already channelling, no
        /// card).
        /// </summary>
        public static bool Begin(EntityManager em, Entity e)
        {
            if (!em.Exists(e) || !em.HasComponent<UnitAbilities>(e)) return false;
            if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) return false;
            if (TransientState.Active<AbilityCastState>(em, e)) return false;
            int index = AbilityCatalog.IndexOf(AbilityName);
            if (index < 0) return false;
            var ua = em.GetComponentData<UnitAbilities>(e);
            if (ua.S0 != index && ua.S1 != index && ua.S2 != index && ua.S3 != index) return false;

            TransientState.Clear<MoveCommand>(em, e);
            TransientState.Clear<AttackCommand>(em, e);
            if (em.HasComponent<DesiredDestination>(e))
                em.SetComponentData(e, new DesiredDestination { Position = float3.zero, Has = 0 });
            if (em.HasComponent<UnitStance>(e))
                em.SetComponentData(e, new UnitStance { Value = UnitStanceMode.Hold });
            TransientState.Set(em, e, new AbilityActivated { Target = Entity.Null, Slot = -1 });
            return true;
        }

        /// <summary>The channel landed: curse the ground, then die uncredited.</summary>
        public static void Offer(EntityManager em, Entity caster, AbilityCard card)
        {
            if (!em.Exists(caster) || !em.HasComponent<LocalTransform>(caster)) return;
            float3 p = em.GetComponentData<LocalTransform>(caster).Position;
            CurseGroundSystem.CurseDisk(p, card.Radius * SizeOf(em, caster));
            UnityEngine.Debug.Log($"[VeilOffering] offered at ({p.x:F0},{p.z:F0}) — " +
                $"{card.Radius * SizeOf(em, caster):F0} m of ground cursed.");

            // No kill for anyone: no religion points, no bounty (§6.9.5).
            if (em.HasComponent<LastDamagedByFaction>(caster))
                em.SetComponentEnabled<LastDamagedByFaction>(caster, false);
            if (!em.HasComponent<Health>(caster)) { em.DestroyEntity(caster); return; }
            var hp = em.GetComponentData<Health>(caster);
            hp.Value = 0;
            em.SetComponentData(caster, hp);
        }
    }
}
