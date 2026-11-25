// UnitAgeKit.cs
// A unit prefab whose LOOK FOLLOWS WHAT ITS FACTION HAS carries every piece
// it can show and this component.
//
//   Age       the first-age objects are shown until the faction has chosen a
//             culture, the aged objects from then on. A kit may be a whole
//             rig of its own, with its own Animator and controller. The Age 0
//             Spearman: a peasant levy, then an armoured legionary.
//   Gear      (2026-11-13) pieces on the aged kit that follow the faction's
//             progress - developer: "I want the spearmen's gear to change to
//             reflect the type of upgrades they have":
//               level gear    one option per level of a building ladder
//                             (body, helmet, shield: the Garrison's 1-3)
//               weapon tiers  option 0, then one more per researched tech of
//                             a list (Iron / Veilstone / Shard-infused)
//               tech gear     shown once a tech is researched (the cape, for
//                             Charge)
//             Which ladder and which techs is authored on the prefab.
//
// EVERY CHANGE IS A DISSOLVE (developer: "every transition should use the
// dissolve effect, just like in buildings"): the old piece and the new one
// trade places through BuildingDissolveTransition's wave. Only the first look
// a unit is given - at spawn - is set at once.
//
// Presentation only. The spawn system gives it the faction; it re-checks the
// faction twice a second, each unit on its own phase, so an army that gains
// an upgrade changes in a ripple, not in one frame.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Buildings;

namespace TheWaningBorder.Rendering
{
    [DisallowMultipleComponent]
    public sealed class UnitAgeKit : MonoBehaviour
    {
        public GameObject[] firstAge;
        public GameObject[] aged;

        /// <summary>The building ladder whose faction level picks the level gear ("Barracks").</summary>
        public string levelLadderId;
        /// <summary>Level gear, one array per slot; element 0 is level 1. A
        /// level past the end shows the last element.</summary>
        public GameObject[] helmets;
        /// <summary>The aged kit's body per level: skinned meshes on ONE skeleton.</summary>
        public GameObject[] bodies;
        public GameObject[] shields;
        /// <summary>Weapon options: element 0 with none of <see cref="weaponTechs"/>
        /// researched, element n with the n-th.</summary>
        public GameObject[] weapons;
        /// <summary>The same tiers on the FIRST-AGE kit (a tier past the end
        /// shows the last): the Militia's spear changes with Stone Weapons.</summary>
        public GameObject[] firstAgeWeapons;
        public string[] weaponTechs;
        /// <summary>A second ladder worn beside the weapon: the arrows in the
        /// quiver, option n with the n-th of <see cref="quiverTechs"/>.</summary>
        public GameObject[] quivers;
        public string[] quiverTechs;
        /// <summary>Shown once <see cref="techGearTech"/> is researched.</summary>
        public GameObject techGear;
        public string techGearTech;

        const float CheckInterval = 0.5f;
        const float DissolveSeconds = 2f;

        Faction _faction;
        bool _bound;
        int _shown = -1;      // -1 unknown, 0 first age, 1 aged
        int _body = -1, _helmet = -1, _shield = -1, _weapon = -1, _firstWeapon = -1, _quiver = -1, _tech = -1;
        float _nextCheck;

        /// <summary>Called by the spawn system with the unit's faction.</summary>
        public void Bind(Faction faction)
        {
            _faction = faction;
            _bound = true;
            Refresh(instant: true);
            // Each unit on its own phase: an upgrade ripples through an army.
            _nextCheck = Time.unscaledTime + CheckInterval * (0.2f + 0.8f * Mathf.Abs(Mathf.Sin(GetInstanceID() * 12.9898f)));
        }

        void Update()
        {
            if (!_bound || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckInterval;
            Refresh(instant: false);
        }

        void Refresh(bool instant)
        {
            // A unit with no first-age kit (the Swordsman) is always its aged one.
            int want = FactionColors.GetFactionCulture(_faction) != 0
                       || firstAge == null || firstAge.Length == 0 ? 1 : 0;
            int level = 1, weapon = 0, tech = 0, quiver = 0;
            var research = FactionResearchState.Instance;
            if (research != null)
            {
                // Weapon tiers count in either age (Stone Weapons is Age 0).
                if (weaponTechs != null)
                    for (int i = 0; i < weaponTechs.Length; i++)
                        if (research.HasResearched(_faction, weaponTechs[i])) weapon = i + 1;
                if (quiverTechs != null)
                    for (int i = 0; i < quiverTechs.Length; i++)
                        if (research.HasResearched(_faction, quiverTechs[i])) quiver = i + 1;
                if (!string.IsNullOrEmpty(techGearTech) && research.HasResearched(_faction, techGearTech)) tech = 1;
            }
            if (want == 1)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated && !string.IsNullOrEmpty(levelLadderId))
                    level = Mathf.Max(1, FactionBuildingLevels.Get(world.EntityManager, _faction, levelLadderId));
            }
            Apply(want == 1, level, weapon, tech, instant, quiver);
        }

        /// <summary>Switch the kit at once. Public so an editor preview can drive it.</summary>
        public void Show(bool agedKit) => Apply(agedKit, 1, 0, 0, instant: true);

        /// <summary>Set the whole look: the kit, the gear of a ladder level
        /// (1-based), a weapon tier (0 = base) and the tech gear (0 / 1).
        /// <paramref name="instant"/> skips the dissolve.</summary>
        public void Apply(bool agedKit, int level, int weapon, int tech, bool instant, int quiver = 0)
        {
            int kit = agedKit ? 1 : 0;
            bool kitChanged = kit != _shown;
            if (kitChanged)
            {
                var from = _shown < 0 ? null : First(_shown == 1 ? aged : firstAge);
                var to = First(agedKit ? aged : firstAge);
                bool wave = !instant && from != null && to != null && from != to;
                SetAll(agedKit ? aged : firstAge, true);
                if (!wave) SetAll(agedKit ? firstAge : aged, false);
                _shown = kit;
                // The gear rides on the kit it belongs to: set it before the
                // kit appears, so the wave reveals the right pieces.
                SetGear(level, weapon, tech, quiver, instant: true);
                if (wave)
                {
                    // The animation driver takes the first Animator it finds:
                    // put the kit being revealed first.
                    to.transform.SetAsFirstSibling();
                    BuildingDissolveTransition.Begin(from, to, DissolveSeconds, EdgeColor(), destroyOldOnComplete: false);
                }
                var sync = GetComponent<UnitAnimationSync>();
                if (sync != null) sync.ControllerChanged();
                return;
            }
            SetGear(level, weapon, tech, quiver, instant);
        }

        void SetGear(int level, int weapon, int tech, int quiver, bool instant)
        {
            Pick(bodies, level - 1, ref _body, instant);
            Pick(helmets, level - 1, ref _helmet, instant);
            Pick(shields, level - 1, ref _shield, instant);
            Pick(weapons, weapon, ref _weapon, instant);
            Pick(quivers, quiver, ref _quiver, instant);
            Pick(firstAgeWeapons, weapon, ref _firstWeapon, instant);
            if (techGear != null && tech != _tech)
            {
                bool on = tech == 1;
                if (instant || !techGear.transform.parent.gameObject.activeInHierarchy) techGear.SetActive(on);
                else if (on) { techGear.SetActive(true); BuildingDissolveTransition.Begin(null, techGear, DissolveSeconds, EdgeColor(), false); }
                else techGear.SetActive(false);
                _tech = tech;
            }
        }

        /// <summary>Show option <paramref name="want"/> of a slot (clamped to
        /// what the slot has), hiding the others; a change dissolves.</summary>
        void Pick(GameObject[] options, int want, ref int shown, bool instant)
        {
            if (options == null || options.Length == 0) return;
            want = Mathf.Clamp(want, 0, options.Length - 1);
            if (want == shown) return;
            var to = options[want];
            var from = shown >= 0 && shown < options.Length ? options[shown] : null;
            bool wave = !instant && from != null && to != null && from != to
                        && from.activeInHierarchy;
            for (int i = 0; i < options.Length; i++)
                if (options[i] != null && options[i] != to && !(wave && options[i] == from))
                    options[i].SetActive(false);
            if (to != null) to.SetActive(true);
            if (wave) BuildingDissolveTransition.Begin(from, to, DissolveSeconds, EdgeColor(), destroyOldOnComplete: false);
            shown = want;
        }

        Color EdgeColor() => FactionColors.Get(_faction);

        static GameObject First(GameObject[] set) => set != null && set.Length > 0 ? set[0] : null;

        static void SetAll(GameObject[] set, bool on)
        {
            if (set == null) return;
            for (int i = 0; i < set.Length; i++)
                if (set[i] != null) set[i].SetActive(on);
        }
    }
}
