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
            TickLedgerAutoCast(em);
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
                for (int s = 0; s < 4; s++)
                {
                    var c = AuraCardFor(slots.Get(s));
                    if (c != null) { aura = c; break; }
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

        // ---- Ledger: fire Automate Facility on a nearby eligible eco building ----
        private void TickLedgerAutoCast(EntityManager em)
        {
            var ledgerQ = QC_LedgerTagFactionTagLocalTransform.Get(em, QT_LedgerTagFactionTagLocalTransform);
            using var ledgers = ledgerQ.ToEntityArray(Allocator.Temp);
            if (ledgers.Length == 0) return;

            int automateIdx = AbilityCatalog.IndexOf("Automate Facility");
            var card = AbilityCatalog.Get(automateIdx);
            if (card == null) return;

            var bldgQ = QC_BuildingTagFactionTagLocalTransform.Get(em, QT_BuildingTagFactionTagLocalTransform);
            using var bldgs = bldgQ.ToEntityArray(Allocator.Temp);
            using var bldgFac = bldgQ.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bldgXf = bldgQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float castRange = card.Range + 2f;               // must be ~adjacent to automate
            float castRangeSq = castRange * castRange;

            // Eligibility is a property of the BUILDING, and nothing in the
            // ledger loop changes it (the loop writes only the Ledgers), so it
            // is tested once per building per pass instead of once per
            // building per Ledger (five EntityManager calls each).
            // Result-identical. Filled on first use.
            byte[] eligible = null;

            foreach (var led in ledgers)
            {
                bool casting = TransientState.Active<AbilityCastState>(em, led) || TransientState.Active<AbilityActivated>(em, led);
                var cds = em.HasComponent<AbilityCooldowns>(led) ? em.GetComponentData<AbilityCooldowns>(led) : default;

                Faction fac = em.GetComponentData<FactionTag>(led).Value;
                float3 pos = em.GetComponentData<LocalTransform>(led).Position;

                // A PLAYER'S MOVE ORDER OUTRANKS THE ROAMING. This loop used to
                // re-issue its own move (or StopLedger) on every throttle tick,
                // so a Ledger the player sent somewhere was dragged straight
                // back to the nearest building, or stopped dead. A live move
                // whose destination is not the one the roaming last wrote is
                // the player's: leave the Ledger entirely alone until it
                // arrives. (UserMoveOrder alone cannot tell them apart — the
                // roaming's own IssueMove sets it too.)
                if (IsPlayerMoving(em, led)) continue;

                // Nearest eligible economy building at ANY distance (not just in range).
                Entity best = Entity.Null; float bestSq = float.MaxValue; float3 bestPos = default;
                if (eligible == null)
                {
                    eligible = new byte[bldgs.Length];
                    for (int i = 0; i < bldgs.Length; i++)
                        eligible[i] = (byte)(!TransientState.Active<UnderAutomation>(em, bldgs[i])
                                             && !TransientState.Active<AutoYieldBoost>(em, bldgs[i])
                                             && IsEconomyBuilding(em, bldgs[i]) ? 1 : 0);
                }
                for (int i = 0; i < bldgs.Length; i++)
                {
                    if (bldgFac[i].Value != fac) continue;
                    if (eligible[i] == 0) continue;
                    float2 d = new float2(bldgXf[i].Position.x - pos.x, bldgXf[i].Position.z - pos.z);
                    float sq = math.dot(d, d);
                    if (sq < bestSq) { bestSq = sq; best = bldgs[i]; bestPos = bldgXf[i].Position; }
                }

                if (best == Entity.Null)
                {
                    // Nothing left to automate — stop roaming.
                    StopLedger(em, led, pos);
                    continue;
                }

                if (bestSq <= castRangeSq)
                {
                    // In range: hold position and fire (unless already casting / cooling).
                    StopLedger(em, led, pos);
                    if (!casting && cds.C0 <= 0f)
                        // Slot -1 = "first ready active", the behaviour this
                        // had before slots became selectable. The Ledger has
                        // exactly one active, so naming it would be noise.
                        TransientState.Set(em, led, new AbilityActivated { Target = best, Slot = -1 });
                }
                else if (!casting)
                {
                    // Walk toward the target building via the AI move path. Only
                    // (re)issue when the goal actually changed, so we don't spam
                    // nav-path requests every throttle tick.
                    // Compared against the goal the roaming RECORDED: the
                    // move snaps the building centre onto walkable ground, so
                    // the destination never equals bestPos and the old test
                    // re-issued the move every throttle tick.
                    bool needMove = true;
                    if (em.HasComponent<DesiredDestination>(led) && em.HasComponent<LedgerAutoGoal>(led))
                    {
                        var dd = em.GetComponentData<DesiredDestination>(led);
                        var goal = em.GetComponentData<LedgerAutoGoal>(led);
                        if (dd.Has == 1 && math.distancesq(goal.Target, bestPos) < 1f
                            && math.distancesq(dd.Position, goal.Position) < 1f) needMove = false;
                    }
                    if (needMove)
                    {
                        // CommandSource.System, NOT AI: this system runs on
                        // EVERY peer (no host gate) and the move is a
                        // deterministic consequence of the tick. AI-source
                        // would queue it on the host (+2 ticks) but execute it
                        // immediately on the client — a tick-offset position
                        // divergence under lockstep.
                        TheWaningBorder.Core.Commands.CommandRouter.IssueMove(
                            em, led, bestPos, TheWaningBorder.Core.Commands.CommandSource.System);
                        float3 written = em.HasComponent<DesiredDestination>(led)
                            ? em.GetComponentData<DesiredDestination>(led).Position : bestPos;
                        AddOrSet(em, led, new LedgerAutoGoal { Position = written, Target = bestPos });
                    }
                }
            }
        }

        /// <summary>True while the Ledger is executing a move the PLAYER gave
        /// it — a live destination the roaming did not write.</summary>
        private static bool IsPlayerMoving(EntityManager em, Entity led)
        {
            if (!TransientState.Active<UserMoveOrder>(em, led)) return false;
            if (!em.HasComponent<DesiredDestination>(led)) return false;
            var dd = em.GetComponentData<DesiredDestination>(led);
            if (dd.Has == 0) return false;
            if (!em.HasComponent<LedgerAutoGoal>(led)) return true;
            return math.distancesq(dd.Position, em.GetComponentData<LedgerAutoGoal>(led).Position) >= 1f;
        }

        // Clear a Ledger's movement goal so it stops (used when it arrives at a
        // building or has nothing to automate). Leaves any in-flight cast alone,
        // and never touches a destination the PLAYER set — the caller has
        // already skipped a player-moving Ledger, and this re-checks so no
        // future caller can clear one by accident.
        private static void StopLedger(EntityManager em, Entity led, float3 pos)
        {
            if (IsPlayerMoving(em, led)) return;
            if (em.HasComponent<DesiredDestination>(led))
            {
                var dd = em.GetComponentData<DesiredDestination>(led);
                if (dd.Has != 0)
                    em.SetComponentData(led, new DesiredDestination { Position = pos, Has = 0 });
            }
        }

        private static bool IsEconomyBuilding(EntityManager em, Entity b)
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
