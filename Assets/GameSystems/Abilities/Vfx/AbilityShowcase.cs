// AbilityShowcase.cs
// One list of EVERY ability in the game, at every level it has, and one way to
// play it — shared by the sandbox's Abilities tab and the unit test scene.
//
// The list is BUILT FROM THE REAL TABLES, never hand-written:
//   * every card in AbilityCatalog — unit/hero abilities, which have no levels
//   * every sect active, SectLeverEffects.ActiveOf(sect, slot, level), over all
//     of SectConfig.AllSectIds x 3 slots x 3 levels
//
// So an ability added to either table appears here without anyone remembering
// to add it, and a spec that fails to resolve is reported rather than quietly
// skipped — half the value of the thing is that it proves every ability
// RESOLVES, not just that its art plays.
//
// LEVELS ARE NUMBERS, NOT ART. There is one spell prefab per SECT, not one per
// level, so I/II/III share a look. What differs is reach, magnitude and
// duration — so a cast is scaled to the SPEC'S radius (visible wherever a level
// widens the power) and the numbers travel with the entry for the caller to
// display. Inventing per-level art here would be showing something the game
// does not have.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;          // SectConfig / SectLeverEffects
using TheWaningBorder.Systems.Sect;     // SectActivePowerHelper

namespace TheWaningBorder.Abilities.Vfx
{
    /// <summary>One castable row: what it is, what it does, and its art.</summary>
    public struct ShowcaseEntry
    {
        /// <summary>"Antiquity II — Scour the Registry", or a card's name.</summary>
        public string Label;
        /// <summary>Kind / reach / magnitude / duration, already formatted.</summary>
        public string Detail;
        /// <summary>Grouping for a UI list: "Abilities" or the sect's name.</summary>
        public string Group;
        /// <summary>The spec's reach — what the cast is scaled to.</summary>
        public float Radius;
        /// <summary>May be null: four catalog abilities have no art at all.</summary>
        public Spell Vfx;

        // ── what a LIVE cast needs ──────────────────────────────────────
        // Exactly one of these is set. A sect power is fired by (sect, slot,
        // level) through the real strike pipeline; a catalog ability is
        // applied to a unit by its card.

        /// <summary>Sect id for a sect power, else null.</summary>
        public string SectId;
        public int Slot;
        public int Level;

        /// <summary>The catalog card for an ability, else null.</summary>
        public AbilityCard Card;
    }

    public static class AbilityShowcase
    {
        private static List<ShowcaseEntry> _entries;
        private static Dictionary<string, Spell> _vfxByName;

        /// <summary>Every ability at every level. Built once, on first ask.</summary>
        public static IReadOnlyList<ShowcaseEntry> Entries
        {
            get
            {
                if (_entries == null) Build();
                return _entries;
            }
        }

        /// <summary>Drop the cached list so a domain reload or a data edit
        /// rebuilds it.</summary>
        public static void Invalidate() { _entries = null; _vfxByName = null; }

        private static Spell Vfx(string key)
            => key != null && _vfxByName.TryGetValue(key, out var s) ? s : null;

        private static void Build()
        {
            _entries = new List<ShowcaseEntry>();
            _vfxByName = new Dictionary<string, Spell>();
            foreach (var s in Resources.LoadAll<Spell>("Spells"))
                if (s != null) _vfxByName[s.gameObject.name] = s;

            // ── Unit / hero abilities ───────────────────────────────────
            for (int i = 0; i < AbilityCatalog.Count; i++)
            {
                var card = AbilityCatalog.Get(i);
                if (card == null) continue;

                // Prefabs are Ability_<PascalCase>: "King's Call" ->
                // Ability_KingsCall.
                string key = "Ability_" + Pascal(card.Name);
                var vfx = Vfx(key);
                if (vfx == null)
                    Debug.LogWarning($"[AbilityShowcase] no spell prefab '{key}' for ability " +
                                     $"'{card.Name}' — it will cast without art.");

                _entries.Add(new ShowcaseEntry
                {
                    Card = card,
                    Label = card.Name,
                    Group = "Abilities",
                    Detail = $"{card.Activation}  {card.Targeting}  radius {card.Radius:0.#}  " +
                             $"cd {card.Cooldown:0.#}s" +
                             (card.UnlocksAtLevel > 1 ? $"  (hero lv {card.UnlocksAtLevel})" : ""),
                    Radius = card.Radius > 0f ? card.Radius : 6f,
                    Vfx = vfx,
                });
            }

            // ── Sect actives, every slot at every level ─────────────────
            var sects = SectConfig.AllSectIds;
            for (int s = 0; s < sects.Length; s++)
            {
                string sectId = sects[s];
                var vfx = Vfx(sectId);            // prefabs are named after the sect id
                if (vfx == null)
                    Debug.LogWarning($"[AbilityShowcase] no spell prefab '{sectId}' — " +
                                     "its powers will cast without art.");

                for (int slot = 1; slot <= 3; slot++)
                {
                    for (int level = 1; level <= 3; level++)
                    {
                        var spec = SectLeverEffects.ActiveOf(sectId, slot, level);
                        if (spec.Kind == SectActivePowerKind.None)
                        {
                            Debug.LogWarning($"[AbilityShowcase] {sectId} slot {slot} level " +
                                             $"{level} resolves to no power — skipped.");
                            continue;
                        }

                        _entries.Add(new ShowcaseEntry
                        {
                            SectId = sectId,
                            Slot = slot,
                            Level = level,
                            Label = $"{Pretty(sectId)} {Roman(level)} — {spec.Name}",
                            Group = Pretty(sectId),
                            Detail = $"{spec.Kind}  slot {slot}  radius {spec.Radius:0.#}  " +
                                     $"magnitude {spec.Magnitude:0.#}  " +
                                     $"duration {(spec.Duration <= 0f ? "—" : spec.Duration + "s")}  " +
                                     $"cd {spec.Cooldown:0.#}s",
                            Radius = spec.Radius > 0f ? spec.Radius : 6f,
                            Vfx = vfx,
                        });
                    }
                }
            }

            Debug.Log($"[AbilityShowcase] {_entries.Count} casts " +
                      $"({AbilityCatalog.Count} ability cards + sect actives at every level), " +
                      $"{_vfxByName.Count} spell prefabs.");
        }

        /// <summary>
        /// Play one entry's art at a point, scaled to the SPEC's radius rather
        /// than the prefab's — SpellVfxPlayer.Cast has no radius override, so
        /// this mirrors it through the slot spawners, which do.
        /// </summary>
        public static void Cast(in ShowcaseEntry e, Vector3 pos)
        {
            var s = e.Vfx;
            if (s == null) return;
            float life = s.DisplayLife;

            if (s.circlePrefab != null)
            {
                var go = SpellVfxPlayer.SpawnCircleSlot(s, pos);
                SpellVfxPlayer.ApplySpeed(go, s.circleSpeed);
                Object.Destroy(go, life);
            }
            if (s.castPrefab != null)
            {
                var go = SpellVfxPlayer.SpawnCastSlot(
                    s.castPrefab, e.Radius, s.castTint, s.castColor, pos);
                SpellVfxPlayer.ApplySpeed(go, s.castSpeed);
                Object.Destroy(go, life);
            }
            if (s.powerUpPrefab != null)
            {
                var go = SpellVfxPlayer.SpawnCastSlot(
                    s.powerUpPrefab, e.Radius, s.powerUpTint, s.powerUpColor, pos);
                SpellVfxPlayer.ApplySpeed(go, s.powerUpSpeed);
                Object.Destroy(go, Mathf.Max(1.5f, s.castTime));
            }
        }

        // ── live casting ────────────────────────────────────────────────

        // Never CreateEntityQuery on a repeating path — Core/CachedEntityQuery.cs.
        private static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Units;

        /// <summary>
        /// Cast for real: play the art AND land the effect, as
        /// <paramref name="faction"/>.
        ///
        /// The two kinds of ability resolve through completely different
        /// machinery, which is why this exists rather than one call:
        ///
        ///   SECT POWERS are area effects. They go through the real strike
        ///   pipeline (telegraph, windup, DispatchEffect), which is already
        ///   what decides friend from foe — a damage power hits units hostile
        ///   to the caster, a heal or buff power finds its allies. Getting
        ///   "buffs for friendlies, damage for enemies" is therefore not a
        ///   thing this code does; it is a thing it stops getting in the way of.
        ///
        ///   CATALOG ABILITIES buff the CASTER (AbilityEffectExecutor writes
        ///   SpellBuff/SpellDebuff onto it), and an aura card then reaches
        ///   nearby units through AbilityAuraSystem. So a card needs a real
        ///   unit to cast from, and the nearest one of the chosen colour is
        ///   the least surprising choice.
        /// </summary>
        public static bool CastLive(EntityManager em, Faction faction, in ShowcaseEntry e,
                                    Vector3 pos, out string note)
        {
            Cast(e, pos);   // the art plays either way

            if (e.SectId != null)
            {
                bool ok = SectActivePowerHelper.FireUnchecked(
                    em, faction, e.SectId, e.Slot, e.Level, pos);
                note = ok
                    ? $"{faction} — {e.Label}"
                    : $"{e.Label} did not resolve to a power";
                return ok;
            }

            if (e.Card != null)
            {
                var caster = NearestUnit(em, faction, pos, out float dist);
                if (caster == Entity.Null)
                {
                    note = $"no {faction} unit to cast from — place one on the Units tab first";
                    return false;
                }
                AbilityEffectExecutor.Apply(em, caster, e.Card, Entity.Null);
                note = $"{e.Card.Name} on the nearest {faction} unit ({dist:0.#} m)";
                return true;
            }

            note = "nothing to cast";
            return false;
        }

        /// <summary>Nearest living unit of a faction to a point, or Null.</summary>
        private static Entity NearestUnit(EntityManager em, Faction faction, Vector3 pos,
                                          out float distance)
        {
            distance = 0f;
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

            float best = float.MaxValue;
            Entity found = Entity.Null;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var p = xfs[i].Position;
                float dx = p.x - pos.x, dz = p.z - pos.z;
                float d = dx * dx + dz * dz;
                if (d < best) { best = d; found = ents[i]; }
            }
            if (found != Entity.Null) distance = Mathf.Sqrt(best);
            return found;
        }

        // ── naming ──────────────────────────────────────────────────────

        private static string Pascal(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>"Sect_Antiquity" -> "Antiquity".</summary>
        private static string Pretty(string sectId)
        {
            int i = sectId.IndexOf('_');
            return i >= 0 && i + 1 < sectId.Length ? sectId.Substring(i + 1) : sectId;
        }

        private static string Roman(int level) => level switch
        {
            2 => "II",
            3 => "III",
            _ => "I",
        };
    }
}
