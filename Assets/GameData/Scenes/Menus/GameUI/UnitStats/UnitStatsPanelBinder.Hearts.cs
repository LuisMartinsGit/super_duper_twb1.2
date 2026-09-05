// UnitStatsPanelBinder.Hearts.cs
// The heart rows - binding them from the prefab and filling them.

using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class UnitStatsPanelBinder
    {
        /// <summary>Row node by name under <paramref name="root"/> (first
        /// match among the given aliases), then the SPR_Heart fill image of
        /// each heart Toggle under it, in sibling (display) order. The
        /// Toggles themselves are disabled — the row is display-only and the
        /// images are driven directly.</summary>
        private static HeartRow BindHeartRow(Transform root, params string[] rowNames)
        {
            Transform row = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (var rowName in rowNames)
                {
                    if (string.Equals(t.name, rowName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        row = t;
                        break;
                    }
                }
                if (row != null) break;
            }
            if (row == null) return null;

            var toggles = row.GetComponentsInChildren<Toggle>(true);
            if (toggles.Length == 0) return null;

            var hearts = new Image[toggles.Length];
            for (int i = 0; i < toggles.Length; i++)
            {
                toggles[i].enabled = false;
                foreach (var img in toggles[i].GetComponentsInChildren<Image>(true))
                {
                    if (img.name == "SPR_Heart") { hearts[i] = img; break; }
                }
            }
            return new HeartRow { Root = row.gameObject, Hearts = hearts };
        }

        private void RefreshHeartRows(EntityManager em, Entity unit)
        {
            bool hasAttack = em.HasComponent<Damage>(unit);
            float damage = hasAttack ? em.GetComponentData<Damage>(unit).Value : 0f;
            // DamageTypeData defaults to Melee when absent (combat rule).
            var dmgType = em.HasComponent<DamageTypeData>(unit)
                ? em.GetComponentData<DamageTypeData>(unit).Value
                : DamageType.Melee;

            // Only the attack row matching the unit's damage type shows.
            // Types without an authored row fall back to the ranged row
            // (True always; Siege/Magic until their rows exist).
            HeartRow attackRow;
            switch (dmgType)
            {
                case DamageType.Melee: attackRow = _atkMelee; break;
                case DamageType.Siege: attackRow = _atkSiege ?? _atkRanged; break;
                case DamageType.Magic: attackRow = _atkMagic ?? _atkRanged; break;
                default:               attackRow = _atkRanged; break;
            }
            SetRowActive(_atkMelee, hasAttack && attackRow == _atkMelee);
            SetRowActive(_atkRanged, hasAttack && attackRow == _atkRanged);
            SetRowActive(_atkSiege, hasAttack && attackRow == _atkSiege);
            SetRowActive(_atkMagic, hasAttack && attackRow == _atkMagic);
            if (hasAttack) SetHeartRow(attackRow, damage / DamageFull);

            // Melee units have no meaningful attack range — hide the row.
            bool showRange = hasAttack && dmgType != DamageType.Melee;
            SetRowActive(_atkRange, showRange);
            if (showRange)
            {
                float range = em.HasComponent<ArcherState>(unit)
                    ? em.GetComponentData<ArcherState>(unit).MaxRange : 0f;
                SetHeartRow(_atkRange, range / RangeFull);
            }

            float speed = em.HasComponent<MoveSpeed>(unit)
                ? em.GetComponentData<MoveSpeed>(unit).Value : 0f;
            SetHeartRow(_speedRow, speed / SpeedFull);

            float sight = em.HasComponent<LineOfSight>(unit)
                ? em.GetComponentData<LineOfSight>(unit).Radius : 0f;
            SetHeartRow(_sightRow, sight / SightFull);

            var def = em.HasComponent<Defense>(unit)
                ? em.GetComponentData<Defense>(unit) : default;
            SetHeartRow(_defMelee, def.Melee / ArmorFull);
            SetHeartRow(_defRanged, def.Ranged / ArmorFull);
            SetHeartRow(_defSiege, def.Siege / ArmorFull);
            SetHeartRow(_defMagic, def.Magic / ArmorFull);

            // Units swap the building row section for the unit set.
            if (_unitsSection != null && !_unitsSection.activeSelf)
                _unitsSection.SetActive(true);
            if (_buildingsSection != null && _buildingsSection.activeSelf)
                _buildingsSection.SetActive(false);

            // Housing: units show the slots they occupy.
            bool hasPop = em.HasComponent<TheWaningBorder.Economy.PopulationCost>(unit);
            if (_housingRow != null && _housingRow.activeSelf != hasPop)
                _housingRow.SetActive(hasPop);
            if (hasPop && _housingAmount != null)
                _housingAmount.text = em
                    .GetComponentData<TheWaningBorder.Economy.PopulationCost>(unit)
                    .Amount.ToString();
        }

        private static void SetRowActive(HeartRow row, bool active)
        {
            if (row?.Root != null && row.Root.activeSelf != active)
                row.Root.SetActive(active);
        }

        /// <summary>Fill a row from a 0-1 fraction: rounded to the 0-15
        /// point scale, three points per heart, partial hearts via
        /// fillAmount. Empty hearts keep only their background sprite.</summary>
        private static void SetHeartRow(HeartRow row, float fraction)
        {
            if (row == null) return;
            int points = Mathf.Clamp(Mathf.RoundToInt(fraction * PointsMax), 0, PointsMax);
            for (int i = 0; i < row.Hearts.Length; i++)
            {
                var heart = row.Hearts[i];
                if (heart == null) continue;
                int heartPoints = Mathf.Clamp(points - i * PointsPerHeart, 0, PointsPerHeart);
                bool on = heartPoints > 0;
                if (heart.enabled != on) heart.enabled = on;
                if (on) heart.fillAmount = heartPoints / (float)PointsPerHeart;
            }
        }
    }
}
