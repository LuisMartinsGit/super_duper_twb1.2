// UnitStatsPanelBinder.Building.cs
// The building half of the stats panel: which building is being shown,
// its stats, and its resource generation rows.

using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class UnitStatsPanelBinder
    {
        /// <summary>First selected building with Health, or Entity.Null.</summary>
        private static Entity ResolveStatsBuilding(EntityManager em)
        {
            var selection = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (selection == null) return Entity.Null;
            for (int i = 0; i < selection.Count; i++)
            {
                var e = selection[i];
                if (em.Exists(e) && em.HasComponent<BuildingTag>(e)
                    && em.HasComponent<Health>(e))
                    return e;
            }
            return Entity.Null;
        }

        private void RefreshBuilding(EntityManager em, Entity building)
        {
            SetVisible(true);
            bool changed = building != _boundUnit;
            _boundUnit = building;

            var health = em.GetComponentData<Health>(building);
            _hpTarget = health.Max > 0
                ? Mathf.Clamp01((float)health.Value / health.Max) : 0f;
            if (changed && _hpSlider != null) _hpSlider.value = _hpTarget;
            if (_hpLabel != null) _hpLabel.text = health.Value + "/" + health.Max;

            // The cooldown bar doubles as CONSTRUCTION progress while the
            // building is going up; once complete it shows the structure's
            // attack cooldown (towers, keeps) or hides for passive buildings.
            bool showBar = true;
            if (em.HasComponent<UnderConstruction>(building))
            {
                var uc = em.GetComponentData<UnderConstruction>(building);
                _acdTarget = uc.Total > 0f ? Mathf.Clamp01(uc.Progress / uc.Total) : 0f;
                if (changed && _acdSlider != null) _acdSlider.value = _acdTarget;
                if (_acdLabel != null)
                    _acdLabel.text = Mathf.RoundToInt(_acdTarget * 100f) + "%";
            }
            else if (em.HasComponent<BuildingRangedAttack>(building))
            {
                var atk = em.GetComponentData<BuildingRangedAttack>(building);
                float remaining = Mathf.Clamp(atk.Timer, 0f, atk.Cooldown);
                _acdTarget = atk.Cooldown > 0f ? 1f - remaining / atk.Cooldown : 1f;
                bool attackFired = remaining > _lastRemaining + 0.001f;
                _lastRemaining = remaining;
                if ((changed || attackFired) && _acdSlider != null)
                    _acdSlider.value = _acdTarget;
                if (_acdLabel != null)
                    _acdLabel.text = (Mathf.Round(remaining * 10f) / 10f).ToString("0.0") + "s";
            }
            else showBar = false;
            if (_acdSlider != null && _acdSlider.gameObject.activeSelf != showBar)
                _acdSlider.gameObject.SetActive(showBar);
            if (_acdLabel != null && _acdLabel.gameObject.activeSelf != showBar)
                _acdLabel.gameObject.SetActive(showBar);

            // Buildings swap the unit row section for the building set.
            if (_unitsSection != null && _unitsSection.activeSelf)
                _unitsSection.SetActive(false);
            if (_buildingsSection != null && !_buildingsSection.activeSelf)
                _buildingsSection.SetActive(true);

            bool hasAttack = em.HasComponent<BuildingRangedAttack>(building);
            SetRowActive(_bAtkRanged, hasAttack);
            SetRowActive(_bAtkRange, hasAttack);
            if (hasAttack)
            {
                var atk = em.GetComponentData<BuildingRangedAttack>(building);
                SetHeartRow(_bAtkRanged, atk.Damage / DamageFull);
                SetHeartRow(_bAtkRange, atk.Range / RangeFull);
            }

            float sight = em.HasComponent<LineOfSight>(building)
                ? em.GetComponentData<LineOfSight>(building).Radius : 0f;
            SetRowActive(_bSight, sight > 0f);
            SetHeartRow(_bSight, sight / SightFull);

            RefreshResourceGeneration(em, building);

            // Housing: providers show "+N".
            bool hasPop = em.HasComponent<TheWaningBorder.Economy.PopulationProvider>(building);
            if (_housingRow != null && _housingRow.activeSelf != hasPop)
                _housingRow.SetActive(hasPop);
            if (hasPop && _housingAmount != null)
                _housingAmount.text = "+" + em
                    .GetComponentData<TheWaningBorder.Economy.PopulationProvider>(building).Amount;

            var def = em.HasComponent<Defense>(building)
                ? em.GetComponentData<Defense>(building) : default;
            SetHeartRow(_defMelee, def.Melee / ArmorFull);
            SetHeartRow(_defRanged, def.Ranged / ArmorFull);
            SetHeartRow(_defSiege, def.Siege / ArmorFull);
            SetHeartRow(_defMagic, def.Magic / ArmorFull);
        }

        /// <summary>Per-resource generation sliders; the whole pane hides
        /// for buildings that generate nothing.</summary>
        private void RefreshResourceGeneration(EntityManager em, Entity building)
        {
            if (_resourceGenRow == null) return;

            float supplies = em.HasComponent<TheWaningBorder.Economy.SuppliesIncome>(building)
                ? em.GetComponentData<TheWaningBorder.Economy.SuppliesIncome>(building).PerMinute : 0f;
            float iron = em.HasComponent<TheWaningBorder.Economy.IronIncome>(building)
                ? em.GetComponentData<TheWaningBorder.Economy.IronIncome>(building).PerMinute : 0f;
            float veilstone = em.HasComponent<TheWaningBorder.Economy.VeilstoneIncome>(building)
                ? em.GetComponentData<TheWaningBorder.Economy.VeilstoneIncome>(building).PerMinute : 0f;
            float veilsteel = em.HasComponent<TheWaningBorder.Economy.VeilsteelIncome>(building)
                ? em.GetComponentData<TheWaningBorder.Economy.VeilsteelIncome>(building).PerMinute : 0f;

            // A HALL REPORTS ITS TERRITORY (docs/Design/Regions.md §4). It has
            // no income components of its own — territory income is paid by
            // TerritoryIncomeSystem straight into the bank, so read from
            // nothing, the Hall showed an empty pane. What the player needs to
            // decide where to expand is exactly what the ground pays, so the
            // Hall states it: same call the income tick pays out of, so the
            // sliders cannot drift from the bank.
            if (em.HasComponent<HallTag>(building)
                && em.HasComponent<Unity.Transforms.LocalTransform>(building))
            {
                var hp = em.GetComponentData<Unity.Transforms.LocalTransform>(building).Position;
                int territory = TheWaningBorder.World.Regions.RegionMap.RegionAt(hp.x, hp.z);
                if (territory != TheWaningBorder.World.Regions.RegionMap.None)
                {
                    var owner = em.HasComponent<FactionTag>(building)
                        ? em.GetComponentData<FactionTag>(building).Value
                        : GameSettings.LocalPlayerFaction;
                    var ty = TheWaningBorder.Systems.World.TerritoryIncomeSystem
                        .ComputeYield(em, territory, owner);
                    supplies  += ty.Supplies;
                    iron      += ty.Iron;
                    veilstone += ty.Veilstone;
                    veilsteel += ty.Veilsteel;
                }
            }

            bool generates = supplies > 0f || iron > 0f || veilstone > 0f || veilsteel > 0f;
            if (_resourceGenRow.activeSelf != generates) _resourceGenRow.SetActive(generates);
            if (!generates) return;

            if (_suppliesGen != null)  _suppliesGen.value  = Mathf.Clamp01(supplies / IncomeFull);
            if (_ironGen != null)      _ironGen.value      = Mathf.Clamp01(iron / IncomeFull);
            if (_veilstoneGen != null) _veilstoneGen.value = Mathf.Clamp01(veilstone / IncomeFull);
            if (_veilsteelGen != null) _veilsteelGen.value = Mathf.Clamp01(veilsteel / IncomeFull);
        }
    }
}
