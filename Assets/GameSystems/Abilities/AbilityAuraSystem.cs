// AbilityAuraSystem.cs
// Continuous passives for the data-driven ability system:
//   - Passive AURA abilities (King's Call): refresh a SpellBuff on same-faction
//     units in radius; grant ChargeDamageBonus to allied cavalry.
//   - Scout Sight: line-of-sight ramps up while the owner stands still.
//   - Ledger auto-cast: an idle Ledger fires Automate Facility on a nearby
//     eligible economy building (routes through AbilityActivated so the normal
//     cast pipeline runs).
//   - Cavalry charge detection: fast-moving cavalry closing on a target get the
//     Charging marker (read on-hit for the King's Call / King Lexor charge bonus).
//
// Managed SystemBase; self-throttled to ~0.4 s (auras/AI don't need per-frame).
// Buffs use a short refresh window so they fade automatically when a unit leaves
// the aura (SpellBuffSystem expires them).

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy; // SuppliesIncome

namespace TheWaningBorder.Abilities
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AbilityAuraSystem : SystemBase
    {
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and these were never disposed, so this hot path leaked one
        // per invocation. A bloated registry slows every later query AND
        // every structural change. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_UnitTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagLocalTransform;

        static readonly ComponentType[] QT_UnitAbilitiesFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<UnitAbilities>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_UnitAbilitiesFactionTagLocalTransform;

        static readonly ComponentType[] QT_LedgerTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<LedgerTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_LedgerTagFactionTagLocalTransform;

        static readonly ComponentType[] QT_BuildingTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_BuildingTagFactionTagLocalTransform;

        #endregion

        private const float Interval = 0.4f;
        private const float BuffRefresh = Interval + 0.6f; // buff outlives one tick so it only fades when truly out of range
        // (Scout Sight's changing vision — small while moving, ramping up while
        // still, capped until Scouting Celestarii — is REMOVED, 2026-09-29. A
        // scout's line of sight is its authored maximum at all times, set once
        // by Scout.Create; nothing here writes it.)
        private double _last;
        // Anchored to a match clock that restarts at 0 under lockstep; a
        // stale anchor from an earlier match in this process silences the
        // aura tick on this peer alone until the clock catches up.
        private int _epoch = -1;

        protected override void OnUpdate()
        {
            double now = SystemAPI.Time.ElapsedTime;
            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                _last = now;
            }
            if (now - _last < Interval) return;
            float elapsed = (float)(now - _last);
            _last = now;
            var em = EntityManager;

            ApplyPassiveAuras(em);
            TickChargeDetection(em, elapsed);
        }

        // ---- King's Call & any Passive Aura ability ----
        // PERF (2026-10-16): the aura pass snapshotted every unit (three
        // columns) and read three components per ability holder through the
        // EntityManager on every pass, although only a hero or two carries a
        // passive aura. The holders' columns are now read in one copy each,
        // the "is this ability a passive aura" test is memoised per ability
        // index for the pass, and the unit snapshot is taken only once an
        // aura source has been found. Same sources in the same order, same
        // targets: result-identical.
        private AbilityCard[] _auraOf = System.Array.Empty<AbilityCard>();
        private byte[] _auraKnown = System.Array.Empty<byte>();

        private static bool IsPassiveAura(AbilityCard c)
            => c != null && c.Activation == AbilityActivation.Passive && c.Targeting == AbilityTargeting.Aura;

        private AbilityCard AuraCardFor(int idx)
        {
            if (idx < 0 || idx >= _auraKnown.Length)
            {
                var c = AbilityCatalog.Get(idx);
                return IsPassiveAura(c) ? c : null;
            }
            if (_auraKnown[idx] == 0)
            {
                var c = AbilityCatalog.Get(idx);
                _auraOf[idx] = IsPassiveAura(c) ? c : null;
                _auraKnown[idx] = 1;
            }
            return _auraOf[idx];
        }

        private void ApplyPassiveAuras(EntityManager em)
        {
            // The memo lives for one pass (the catalog can be reloaded).
            int n = AbilityCatalog.Count;
            // BOTH lengths: a loaded save restores _auraKnown (a byte[] the
            // snapshot can carry) but not _auraOf (AbilityCard holds a
            // GameObject, so the snapshot skips it) — the pair came back
            // n / 0 and every pass threw IndexOutOfRange, killing passive
            // auras for the rest of a loaded match (2026-10-16).
            if (_auraKnown.Length != n || _auraOf.Length != n)
            { _auraKnown = new byte[n]; _auraOf = new AbilityCard[n]; }
            else System.Array.Clear(_auraKnown, 0, n);

            var auraQ = QC_UnitAbilitiesFactionTagLocalTransform.Get(em, QT_UnitAbilitiesFactionTagLocalTransform);
            using var auras = auraQ.ToEntityArray(Allocator.Temp);
            using var auraSlots = auraQ.ToComponentDataArray<UnitAbilities>(Allocator.Temp);
            using var auraFacs = auraQ.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var auraXfs = auraQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Snapshot all units (potential aura targets) once, on the first
            // source that needs them.
            var unitQ = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            NativeArray<Entity> units = default;
            NativeArray<FactionTag> unitFac = default;
            NativeArray<LocalTransform> unitXf = default;
            bool unitsLoaded = false;

            for (int a = 0; a < auras.Length; a++)
            {
                var slots = auraSlots[a];
                AbilityCard aura = null;
                for (int s = 0; s < UnitAbilities.Count; s++)
                {
                    var c = AuraCardFor(slots.Get(s));
                    // Accounts Settled waits for its level (Heroes.md §3).
                    if (c != null && AbilityQuery.IsUnlocked(em, auras[a], c)) { aura = c; break; }
                }
                if (aura == null) continue;

                if (!unitsLoaded)
                {
                    units = unitQ.ToEntityArray(Allocator.Temp);
                    unitFac = unitQ.ToComponentDataArray<FactionTag>(Allocator.Temp);
                    unitXf = unitQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                    unitsLoaded = true;
                }

                Faction srcFac = auraFacs[a].Value;
                float3 srcPos = auraXfs[a].Position;
                float radSq = aura.Radius * aura.Radius;

                float atkMult = 1f + aura.EffectValue(AbilityEffectKind.AttackPct) / 100f;
                // Percent of each unit's own armor, resolved at the damage site.
                float armorPct = aura.EffectValue(AbilityEffectKind.ArmorPct);
                float armorFlat = aura.EffectValue(AbilityEffectKind.ArmorFlat);
                int chargeBonus = (int)aura.EffectValue(AbilityEffectKind.ChargeBonusFlat);

                for (int i = 0; i < units.Length; i++)
                {
                    // Own faction AND team allies. The per-field math.max
                    // merge below is exactly what stops two allies running
                    // the same aura from stacking: the stronger value wins and
                    // the duration refreshes, rather than each source adding
                    // its own contribution. docs/Design/Teams.md
                    if (!Alliances.AreAllied(srcFac, unitFac[i].Value)) continue;
                    float2 d = new float2(unitXf[i].Position.x - srcPos.x, unitXf[i].Position.z - srcPos.z);
                    if (math.dot(d, d) > radSq) continue;

                    var buff = TransientState.Active<SpellBuff>(em, units[i]) ? em.GetComponentData<SpellBuff>(units[i]) : default;
                    buff.DamageMultiplier = math.max(buff.DamageMultiplier, atkMult);
                    buff.ArmorBonus = math.max(buff.ArmorBonus, armorFlat);
                    buff.ArmorPct = math.max(buff.ArmorPct, armorPct);
                    buff.TimeRemaining = math.max(buff.TimeRemaining, BuffRefresh);
                    TransientState.Set(em, units[i], buff);

                    // Charge bonus to allied cavalry only.
                    if (chargeBonus > 0 && em.HasComponent<ArmorTypeData>(units[i]) &&
                        em.GetComponentData<ArmorTypeData>(units[i]).Value == ArmorType.Cavalry)
                    {
                        TransientState.Set(em, units[i], new ChargeDamageBonus { Bonus = chargeBonus, TimeRemaining = BuffRefresh });
                    }
                }
            }
            if (unitsLoaded) { units.Dispose(); unitFac.Dispose(); unitXf.Dispose(); }
        }

        internal static bool IsEconomyBuilding(EntityManager em, Entity b)
        {
            // Buildings that produce/hold resources are eligible.
            return em.HasComponent<SuppliesIncome>(b) || em.HasComponent<GathererHutTag>(b) || em.HasComponent<HallTag>(b);
        }

        // ---- Cavalry charge: mark cavalry closing fast on a target ----
        private void TickChargeDetection(EntityManager em, float elapsed)
        {
            // Collect first, then apply — structural adds can't happen inside the query.
            var chargers = new NativeList<Entity>(Allocator.Temp);
            // ArmorTypeData joins the query (2026-10-16): a unit without it
            // was skipped by the HasComponent test anyway, and the armour
            // class is now read from the chunk instead of two EntityManager
            // calls per unit. Result-identical.
            foreach (var (xf, tgt, armor, e) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<Target>, RefRO<ArmorTypeData>>().WithEntityAccess())
            {
                if (armor.ValueRO.Value != ArmorType.Cavalry)
                    continue;
                var t = tgt.ValueRO.Value;
                if (t == Entity.Null || !em.Exists(t) || !em.HasComponent<LocalTransform>(t)) continue;

                float dist = math.distance(xf.ValueRO.Position, em.GetComponentData<LocalTransform>(t).Position);
                if (dist > 2.5f) chargers.Add(e); // still closing (outside melee reach)
            }
            for (int i = 0; i < chargers.Length; i++)
                TransientState.Set(em, chargers[i], new Charging { TimeRemaining = 1.5f });
            chargers.Dispose();
        }

        private static void AddOrSet<T>(EntityManager em, Entity e, T value) where T : unmanaged, IComponentData
        {
            if (em.HasComponent<T>(e)) em.SetComponentData(e, value);
            else em.AddComponentData(e, value);
        }
    }
}
