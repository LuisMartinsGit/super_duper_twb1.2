// AIAlanthorEndgameSystem.Sects.cs
// Sect adoption, active-power firing and its target pickers, sect-unit training.
// Partial of AIAlanthorEndgameSystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_IronDepositStateLocalTransform =
        {
            ComponentType.ReadOnly<IronDepositState>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_IronDepositStateLocalTransform;

        static readonly ComponentType[] QT_BuildingTagLocalTransformFactionTagHealth =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_BuildingTagLocalTransformFactionTagHealth;

        static readonly ComponentType[] QT_AIBrainAISharedKnowledge =
        {
            ComponentType.ReadOnly<AIBrain>(),
            ComponentType.ReadOnly<AISharedKnowledge>(),
        };
        static CachedEntityQuery QC_AIBrainAISharedKnowledge;

        static readonly ComponentType[] QT_ChapelTagFactionTag =
        {
            ComponentType.ReadOnly<ChapelTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_ChapelTagFactionTag;

        static readonly ComponentType[] QT_UnitTypeIdFactionTagHealth =
        {
            ComponentType.ReadOnly<UnitTypeId>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_UnitTypeIdFactionTagHealth;

        static readonly ComponentType[] QT_FactionTagProductionQueueItem =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<ProductionQueueItem>(),
        };
        static CachedEntityQuery QC_FactionTagProductionQueueItem;

        #endregion

        // (The flat late-game tower cap + 5-minute gate are gone — towers are
        // governed by the doctrine below: per-difficulty budget, chokepoint /
        // threat-facing placement, anti-clump spacing, active from era 2.)

        // Sect adoption priority (best-first for a defensive economic
        // culture). ALL 12 sects are adoptable since 2026-08-11 — the Temple
        // caps at 6 chapels and RP is finite, so this order IS the strategy:
        // the home cluster's defense/eco kits lead, high-value cross-cluster
        // powers follow, pure aggression flavor sits at the tail.
        private static readonly string[] AlanthorSectPriority =
        {
            SectConfig.Renewal,      // heal circle + hp lever — defense core
            SectConfig.Fortitude,    // armor circle + melee armor — the wall behind the wall
            SectConfig.Justice,      // reveal + global damage lever
            SectConfig.Antiquity,    // Lorekeeper + Reliquary intel hub
            SectConfig.Reclamation,  // worker armor + heal — the economy insurance
            SectConfig.Veneration,   // damage circle on the garrison
            SectConfig.War,          // speed surge + Warbreaker shock elite
            SectConfig.Witness,      // wide reveal — scout redundancy
            SectConfig.Silence,      // ranged damage lever
            SectConfig.Ash,          // burning ground
            SectConfig.Ruin,         // smite + siege lever
            SectConfig.Wrath,        // pyre — pure aggression flavor
        };

        // All 12 sects — for the Active-Power firing pass (sect adoption is
        // not strictly Alanthor-cluster: a faction may adopt a non-cluster
        // sect too, and once adopted its Active Power should still fire).
        private static readonly string[] AllSects =
        {
            SectConfig.Antiquity, SectConfig.Renewal,    SectConfig.Fortitude,
            SectConfig.Reclamation, SectConfig.Silence,  SectConfig.Justice,
            SectConfig.Veneration, SectConfig.Witness,   SectConfig.War,
            SectConfig.Ash,        SectConfig.Ruin,      SectConfig.Wrath,
        };
        // ──────────────────────────────────────────────────────────────────
        // 2. SECT ADOPTION
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Adopt the next Alanthor-priority sect. Mechanics are
        /// shared with Feraldis (AIEndgameCommon); the priority order is the
        /// only culture input.</summary>
        private static void TryAdoptNextSect(Faction faction, EntityManager em)
            => AIEndgameCommon.TryAdoptNextSect(em, faction, AlanthorSectPriority);

        // ──────────────────────────────────────────────────────────────────
        // 3. SECT ACTIVE-POWER FIRING
        // ──────────────────────────────────────────────────────────────────

        // Fire every adopted sect's Active Power that has a level-1+ lever
        // and an off-cooldown timer. Targeting depends on the power's
        // intent: offensive at enemy clusters near our base, support on
        // our own units (preferring those in combat), reveal at the
        // last-known enemy position.
        /// <summary>
        /// Fire every ready active on every adopted sect. A sect has THREE
        /// actives, not one (docs/Design/Sects.md section 1), each on its own
        /// cooldown — so this walks all three slots. The power's LEVEL comes
        /// from adoption timing and is resolved inside Fire; the AI only picks
        /// the slot and the aim point.
        /// </summary>
        private static void TryFireSectPowers(Faction faction, EntityManager em, float3 hallPos)
        {
            // WAIT FOR VALUE (2026-10-04, Game_AI.md § 6e). Every active used
            // to fire the moment it came off cooldown at whatever the picker
            // found — a smite on a lone building near the keep, a heal on
            // three idle full-HP spearmen, Bulwark on a base nobody was
            // attacking. Powers now aim only at FIGHTS: the ones the army
            // tactics reported (AITactics fight sites) plus the base when it
            // is under attack, and each kind has a value bar from the tier's
            // AITacticsSkill. A power that clears no bar is HELD, and says why.
            AITactics.TryGetSkill(em, faction, out var skill);
            CollectPowerSites(em, faction, hallPos, out bool baseThreat);

            for (int i = 0; i < AllSects.Length; i++)
            {
                string sectId = AllSects[i];
                byte level = SectQuery.PowerLevelOf(em, faction, sectId);
                if (level < 1) continue;   // not adopted

                for (int slot = 1; slot <= SectLeverEffects.ActiveSlots; slot++)
                {
                    if (!SectActivePowerHelper.CanFire(em, faction, sectId, slot)) continue;

                    var spec = SectLeverEffects.ActiveOf(sectId, slot, level);
                    if (spec.Kind == SectActivePowerKind.None) continue;
                    if (!TryPickTargetFor(em, faction, hallPos, spec, in skill, baseThreat,
                            out float3 target, out string why, out int caught))
                    {
                        if (why != null
                            && AITactics.LogDue(em, faction, "sect:" + sectId + slot, AITactics.Cfg.abilityHeldLogSeconds))
                            AILogger.Log(faction, "ABILITY",
                                $"{sectId.Substring(5)} slot {slot} ({spec.Kind}) held ({why})");
                        continue;
                    }

                    // THROUGH THE ROUTER, NEVER Fire() DIRECT (2026-09-04, MP
                    // harness catch #8 — the same class as catch #2). This
                    // system is host-gated, so a direct Fire cast the power on
                    // the host alone: Renewal's Raise Anew conjured a Watch
                    // Tower at the hall that no client had, forking the nav
                    // cost field and then the sim (tick 9389/9540).
                    // CommandSource.AI queues on the host and replicates, so
                    // every peer Fires on the same tick. CanFire above already
                    // vetted the cast; the router re-checks it on every peer.
                    TheWaningBorder.Core.Commands.CommandRouter.IssueSectPower(
                        em, faction, sectId, slot, target,
                        TheWaningBorder.Core.Commands.CommandSource.AI);
                    AILogger.Log(faction, "STRATEGY",
                        $"Alanthor: fired {sectId.Substring(5)} slot {slot} at Lv {level}");
                    AILogger.Log(faction, "ABILITY",
                        $"{sectId.Substring(5)} slot {slot} ({spec.Kind}) cast ({caught} enemies) at ({target.x:F0},{target.z:F0})");
                }
            }
        }

        // ── Fight sites for the power pickers (host scratch) ─────────────
        private static readonly System.Collections.Generic.List<AIFightSite> _powerSites =
            new System.Collections.Generic.List<AIFightSite>(8);

        /// <summary>
        /// The places a power may land: every fight the army tactics reported
        /// within fightSiteSeconds, plus the capital when hostile PLAYER
        /// units stand within baseThreatRadius of it.
        /// </summary>
        private static void CollectPowerSites(EntityManager em, Faction faction, float3 hallPos,
            out bool baseThreat)
        {
            AITactics.FightSites(em, faction, _powerSites);
            float r = AITactics.Cfg.baseThreatRadius;
            int enemies = CountPlayerEnemiesNear(em, faction, hallPos, r);
            baseThreat = enemies > 0;
            if (!baseThreat) return;
            var a = AIEngagement.Assess(em, faction, hallPos, r);
            _powerSites.Add(new AIFightSite { Pos = hallPos, Mine = a.MyPower, Enemy = a.EnemyPower });
        }

        private static int CountPlayerEnemiesNear(EntityManager em, Faction faction, float3 pos, float r)
        {
            var query = QC_UnitTagLocalTransformFactionTagHealth.Get(em, QT_UnitTagLocalTransformFactionTagHealth);
            using var ents = query.ToEntityArray(Allocator.Temp);
            float r2 = r * r; int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var fac = em.GetComponentData<FactionTag>(e).Value;
                if (fac == Faction.Border || !Alliances.AreHostile(faction, fac)) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - pos.x, dz = p.z - pos.z;
                if (dx * dx + dz * dz <= r2) n++;
            }
            return n;
        }

        /// <summary>
        /// Where to aim one active, and whether it is worth it NOW. Returns
        /// false with <paramref name="why"/> set when the power should be held
        /// (null why = nothing to say, e.g. an economy power with no node).
        /// </summary>
        private static bool TryPickTargetFor(EntityManager em, Faction faction, float3 hallPos,
            SectActivePowerSpec spec, in AITacticsSkill skill, bool baseThreat,
            out float3 target, out string why, out int caught)
        {
            why = null;
            caught = 0;
            int aoeMin = math.max(1, skill.aoeMinEnemies);
            switch (spec.Kind)
            {
                // Aim at the enemy army — at a fight, never "near the base
                // at whatever is there".
                case SectActivePowerKind.SmiteCircle:
                case SectActivePowerKind.BurningCircle:
                case SectActivePowerKind.SpawnPyre:
                // Recall the Codex (Antiquity): freezing enemy cooldowns
                // is an offensive cast — same cluster targeting.
                case SectActivePowerKind.FreezeCooldowns:
                case SectActivePowerKind.HostileConversion:
                // Writ of Attainder bills the units that have been doing the
                // killing, so the enemy cluster is exactly where it pays. Spy
                // Network wants a body in that same cluster — a spy planted in
                // a marching army is the one that cascades. Blinding Glare is
                // an ordinary enemy-area debuff.
                case SectActivePowerKind.AttainderStrike:
                case SectActivePowerKind.SpyNetwork:
                case SectActivePowerKind.Blind:
                    if (_powerSites.Count == 0) { target = default; why = "no fight in progress"; return false; }
                    if (TryPickEnemyClusterAtSites(em, faction, spec.Radius, aoeMin, out target, out caught))
                        return true;
                    why = $"densest cluster {caught}/{aoeMin} enemies";
                    return false;

                // Nowhere to Hide is map-wide: it hits whatever the AI can
                // already see — worth it only while a fight is on.
                case SectActivePowerKind.RevealedStrike:
                    for (int s = 0; s < _powerSites.Count; s++)
                        if (_powerSites[s].Enemy > 0)
                        {
                            target = _powerSites[s].Pos; caught = _powerSites.Count;
                            return true;
                        }
                    target = hallPos; why = "no fight in progress";
                    return false;

                // Aim at an enemy BUILDING beside a fight.
                case SectActivePowerKind.BuildingShutdown:
                    for (int s = 0; s < _powerSites.Count; s++)
                        if (TryPickEnemyBuildingNearBase(em, faction, _powerSites[s].Pos, out target)) return true;
                    target = default; why = "no enemy building at a fight";
                    return false;

                // Heals: only where enough of the allied pool is missing.
                case SectActivePowerKind.HealCircle:
                case SectActivePowerKind.HealCirclePercent:
                    return TryPickFriendlyAtSites(em, faction, spec.Radius, FriendlyNeed.Heal, in skill,
                        out target, out why, out caught);

                // Buffs: only on a fighting cluster of allies.
                case SectActivePowerKind.ArmorCircle:
                case SectActivePowerKind.DamageCircle:
                case SectActivePowerKind.SpeedCircle:
                case SectActivePowerKind.Veil:
                case SectActivePowerKind.CurseWard:
                    return TryPickFriendlyAtSites(em, faction, spec.Radius, FriendlyNeed.Buff, in skill,
                        out target, out why, out caught);

                // Ultimates: only when the fight is pivotal.
                case SectActivePowerKind.DeathWard:
                case SectActivePowerKind.Invulnerable:
                    return TryPickFriendlyAtSites(em, faction, spec.Radius, FriendlyNeed.Pivotal, in skill,
                        out target, out why, out caught);

                // Aim into the fog.
                case SectActivePowerKind.RevealCircle:
                    return TryPickRevealTarget(em, faction, hallPos, out target);

                // Aim at your own ground. Bulwark wants the base, not the field
                // army: it buffs buildings — and only while the base is
                // actually under attack. Cleanse is base-defensive too.
                case SectActivePowerKind.BuildingHpBuff:
                case SectActivePowerKind.InfluenceBurst:
                    target = hallPos;
                    if (baseThreat)
                    {
                        caught = CountPlayerEnemiesNear(em, faction, hallPos, AITactics.Cfg.baseThreatRadius);
                        return true;
                    }
                    why = "base not under attack";
                    return false;

                // A tower is a PICKET, so it goes out on the threatened side
                // rather than on the keep — which is where "aim at your own
                // ground" was putting it, every single cast, stacking tower on
                // tower inside the base where nothing could shoot at anything.
                case SectActivePowerKind.RaiseTower:
                    return TryPickTowerSite(em, faction, hallPos, out target);

                // Aim at a resource node.
                case SectActivePowerKind.NodeOverYield:
                    return TryPickResourceNode(em, hallPos, out target);

                default:
                    target = default;
                    return false;
            }
        }

        private enum FriendlyNeed : byte { Heal, Buff, Pivotal }

        /// <summary>
        /// Densest enemy PLAYER cluster standing at one of the fight sites.
        /// <paramref name="caught"/> is the best count found (also on failure,
        /// for the held log).
        /// </summary>
        private static bool TryPickEnemyClusterAtSites(EntityManager em, Faction faction,
            float castRadius, int need, out float3 target, out int caught)
        {
            target = default; caught = 0;
            var query = QC_UnitTagLocalTransformFactionTagHealth.Get(em, QT_UnitTagLocalTransformFactionTagHealth);
            using var ents = query.ToEntityArray(Allocator.Temp);
            float siteR = AITactics.Cfg.powerRadius;
            float siteR2 = siteR * siteR;

            var pts = new NativeList<float3>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var fac = em.GetComponentData<FactionTag>(e).Value;
                // PLAYER enemies only: curse critters respawn from the node,
                // so a smite on them buys nothing (2026-08-11).
                if (fac == Faction.Border || !Alliances.AreHostile(faction, fac)) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                bool atSite = false;
                for (int s = 0; s < _powerSites.Count && !atSite; s++)
                {
                    float dx = p.x - _powerSites[s].Pos.x, dz = p.z - _powerSites[s].Pos.z;
                    atSite = dx * dx + dz * dz <= siteR2;
                }
                if (atSite) pts.Add(p);
            }

            float c2 = castRadius * castRadius;
            int bestIdx = -1;
            for (int i = 0; i < pts.Length; i++)
            {
                int count = 0;
                for (int j = 0; j < pts.Length; j++)
                {
                    float dx = pts[j].x - pts[i].x, dz = pts[j].z - pts[i].z;
                    if (dx * dx + dz * dz <= c2) count++;
                }
                if (count > caught) { caught = count; bestIdx = i; }
            }
            bool ok = bestIdx >= 0 && caught >= need;
            if (ok) target = pts[bestIdx];
            pts.Dispose();
            return ok;
        }

        /// <summary>
        /// Own units standing at a fight site, clustered in the power's
        /// radius, judged by what the power is FOR: a heal needs the missing
        /// share of the cluster's HP pool above healMinMissingFraction; a buff
        /// needs buffMinAllies in the circle; an ultimate needs the site's odds
        /// at or past pivotalRatio as well.
        /// </summary>
        private static bool TryPickFriendlyAtSites(EntityManager em, Faction faction, float castRadius,
            FriendlyNeed need, in AITacticsSkill skill, out float3 target, out string why, out int caught)
        {
            target = default; why = null; caught = 0;
            if (_powerSites.Count == 0) { why = "no fight in progress"; return false; }

            int pivotalSite = -1;
            if (need == FriendlyNeed.Pivotal)
            {
                float worst = 0f;
                for (int s = 0; s < _powerSites.Count; s++)
                {
                    float r = _powerSites[s].Ratio;
                    if (r >= skill.pivotalRatio && r > worst) { worst = r; pivotalSite = s; }
                }
                if (pivotalSite < 0) { why = "no fight is pivotal"; return false; }
            }

            var query = QC_UnitTagLocalTransformFactionTagHealth.Get(em, QT_UnitTagLocalTransformFactionTagHealth);
            using var ents = query.ToEntityArray(Allocator.Temp);
            float siteR = AITactics.Cfg.powerRadius;
            float siteR2 = siteR * siteR;
            var pos = new NativeList<float3>(Allocator.Temp);
            var cur = new NativeList<int>(Allocator.Temp);
            var max = new NativeList<int>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                bool atSite = false;
                for (int s = 0; s < _powerSites.Count && !atSite; s++)
                {
                    if (pivotalSite >= 0 && s != pivotalSite) continue;
                    float dx = p.x - _powerSites[s].Pos.x, dz = p.z - _powerSites[s].Pos.z;
                    atSite = dx * dx + dz * dz <= siteR2;
                }
                if (!atSite) continue;
                pos.Add(p); cur.Add(hp.Value); max.Add(hp.Max);
            }

            float c2 = castRadius * castRadius;
            int bestIdx = -1; float bestScore = 0f; int bestN = 0; float bestMissing = 0f;
            for (int i = 0; i < pos.Length; i++)
            {
                int n = 0, missing = 0, pool = 0;
                for (int j = 0; j < pos.Length; j++)
                {
                    float dx = pos[j].x - pos[i].x, dz = pos[j].z - pos[i].z;
                    if (dx * dx + dz * dz > c2) continue;
                    n++; pool += max[j]; missing += math.max(0, max[j] - cur[j]);
                }
                float frac = pool > 0 ? missing / (float)pool : 0f;
                float score = need == FriendlyNeed.Heal ? missing : n;
                if (score > bestScore) { bestScore = score; bestIdx = i; bestN = n; bestMissing = frac; }
            }
            float3 best = bestIdx >= 0 ? pos[bestIdx] : default;
            pos.Dispose(); cur.Dispose(); max.Dispose();
            caught = bestN;

            int minAllies = math.max(1, AITactics.Cfg.buffMinAllies);
            if (bestIdx < 0 || bestN < minAllies)
            {
                why = $"only {bestN}/{minAllies} allies in the circle";
                return false;
            }
            if (need == FriendlyNeed.Heal && bestMissing < skill.healMinMissingFraction)
            {
                why = $"{bestMissing * 100f:F0}% hp missing (needs {skill.healMinMissingFraction * 100f:F0}%)";
                return false;
            }
            target = best;
            return true;
        }

        /// <summary>Nearest live resource node to the Hall, for Harvest the
        /// Veil. Nearest rather than richest on purpose: a node beside the base
        /// is one the AI still holds when the 30 s payout finishes.</summary>
        private static bool TryPickResourceNode(EntityManager em, float3 hallPos, out float3 target)
        {
            target = default;
            var q = QC_IronDepositStateLocalTransform.Get(em, QT_IronDepositStateLocalTransform);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            const float MaxRange = 90f;
            float best = MaxRange * MaxRange;
            bool found = false;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<IronDepositState>(ents[i]).Depleted != 0) continue;
                float dx = xfs[i].Position.x - hallPos.x;
                float dz = xfs[i].Position.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 >= best) continue;
                best = d2;
                target = xfs[i].Position;
                found = true;
            }
            return found;
        }

        // Densest enemy cluster within ~80 m of the Hall. Returns the
        // grid cell with the most enemy units (4 m bucket size). Avoids
        // wasting a 60-100 cooldown power on a single straggler.
        private static bool TryPickEnemyClusterNearBase(
            EntityManager em, Faction faction, float3 hallPos, float castRadius,
            out float3 target)
        {
            target = default;
            var query = QC_UnitTagLocalTransformFactionTagHealth.Get(em, QT_UnitTagLocalTransformFactionTagHealth);
            using var ents = query.ToEntityArray(Allocator.Temp);

            const float scanRadius = 80f;
            float scanRadiusSq = scanRadius * scanRadius;

            // Snapshot enemy positions within scan radius. PLAYER enemies
            // only (2026-08-11): offensive powers were burning their 60-150 s
            // cooldowns on Border creature clusters at the wells — curse
            // critters respawn from the node, so the smite bought nothing.
            var enemyPositions = new NativeList<float3>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var fac = em.GetComponentData<FactionTag>(e).Value;
                if (fac == faction || fac == Faction.Border) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                if (dx * dx + dz * dz > scanRadiusSq) continue;
                enemyPositions.Add(p);
            }

            if (enemyPositions.Length == 0)
            {
                enemyPositions.Dispose();
                return TryPickEnemyBuildingNearBase(em, faction, hallPos, out target);
            }

            // Pick the densest cluster: for each candidate enemy, count
            // how many other enemies are within castRadius; pick the
            // enemy with the highest count. Ties broken by the first one
            // encountered. O(N²) but N is bounded by units within 80 m.
            float castRadiusSq = castRadius * castRadius;
            int bestCount = 0;
            int bestIdx = -1;
            for (int i = 0; i < enemyPositions.Length; i++)
            {
                int count = 0;
                for (int j = 0; j < enemyPositions.Length; j++)
                {
                    float dx = enemyPositions[j].x - enemyPositions[i].x;
                    float dz = enemyPositions[j].z - enemyPositions[i].z;
                    if (dx * dx + dz * dz <= castRadiusSq) count++;
                }
                if (count > bestCount) { bestCount = count; bestIdx = i; }
            }

            // Need at least 3 units in the cluster to justify a 60-150s cd
            // power — with no such cluster, fall back to the nearest enemy
            // PLAYER building (smite damages structures since 2026-08-11).
            if (bestCount < 3)
            {
                enemyPositions.Dispose();
                return TryPickEnemyBuildingNearBase(em, faction, hallPos, out target);
            }
            target = enemyPositions[bestIdx];
            enemyPositions.Dispose();
            return true;
        }

        /// <summary>Nearest enemy PLAYER building within the base scan
        /// radius — the offensive-power fallback target. Walls are skipped
        /// (siege-only per Combat_Pacing.md, smite cannot hurt them) and so
        /// is anything Border-owned (wells are verb objectives).</summary>
        private static bool TryPickEnemyBuildingNearBase(
            EntityManager em, Faction faction, float3 hallPos, out float3 target)
        {
            target = default;
            var q = QC_BuildingTagLocalTransformFactionTagHealth.Get(em, QT_BuildingTagLocalTransformFactionTagHealth);
            using var ents = q.ToEntityArray(Allocator.Temp);

            const float scanRadius = 80f;
            float bestD2 = scanRadius * scanRadius;
            bool found = false;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var fac = em.GetComponentData<FactionTag>(e).Value;
                if (fac == faction || fac == Faction.Border) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                if (em.HasComponent<WallTag>(e)) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; target = p; found = true; }
            }
            return found;
        }

        /// <summary>
        /// Where to conjure a Watch Tower: on the line from the Hall toward
        /// whatever is threatening it, one picket-distance out.
        ///
        /// A tower dropped on the keep covers ground the keep already covers,
        /// and the AI cast it there every time the power came off cooldown.
        /// Facing the threat gives it something to shoot and puts it where the
        /// attacker arrives first.
        ///
        /// Threat order: a live enemy ARMY near the base, else the nearest
        /// enemy building (the direction an attack will come from). With
        /// neither in ~80 m there is nothing to picket against, so the cast is
        /// refused and the charge is kept — the tower is permanent at level
        /// III, so spending it on empty ground is worse than waiting.
        /// </summary>
        private static bool TryPickTowerSite(
            EntityManager em, Faction faction, float3 hallPos, out float3 target)
        {
            target = default;

            // castRadius 0: this is a direction probe, not an area cast.
            if (!TryPickEnemyClusterNearBase(em, faction, hallPos, 0f, out var threat)
                && !TryPickEnemyBuildingNearBase(em, faction, hallPos, out threat))
                return false;

            float dx = threat.x - hallPos.x;
            float dz = threat.z - hallPos.z;
            float dist = math.sqrt(dx * dx + dz * dz);
            if (dist < 0.01f) return false;   // threat is standing on the Hall

            // Far enough out to be a forward picket and to cover the approach,
            // close enough that the base's other defences still support it.
            const float PicketDistance = 24f;
            float reach = math.min(PicketDistance, dist * 0.6f);

            target = new float3(
                hallPos.x + dx / dist * reach,
                hallPos.y,
                hallPos.z + dz / dist * reach);
            return true;
        }

        // Reveal goes to the AISharedKnowledge.EnemyLastKnownPosition if
        // recent; otherwise we skip rather than blow the cooldown blind.
        private static bool TryPickRevealTarget(EntityManager em, Faction faction,
            float3 hallPos, out float3 target)
        {
            target = default;
            var query = QC_AIBrainAISharedKnowledge.Get(em, QT_AIBrainAISharedKnowledge);
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<AIBrain>(ents[i]).Owner != faction) continue;
                var sk = em.GetComponentData<AISharedKnowledge>(ents[i]);
                // Only fire reveal if we've actually seen something — saves the
                // cooldown vs spraying it on the Hall position.
                if (sk.EnemyLastSeenTime <= 0) return false;
                target = sk.EnemyLastKnownPosition;
                return true;
            }
            return false;
        }

        /// <summary>Train each adopted sect's unique unit at its chapel —
        /// chapels carry a train queue from birth and are the ONLY trainer
        /// for these (SectConfig.UnitIdFor). One queue attempt per think
        /// tick across all chapels.</summary>
        private static void TryTrainSectUnits(Faction faction, EntityManager em)
        {
            if (!TechCatalog.IsReady) return;
            var q = QC_ChapelTagFactionTag.Get(em, QT_ChapelTagFactionTag);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                Entity chapel = ents[i];
                if (em.HasComponent<UnderConstruction>(chapel)) continue;
                if (!em.HasBuffer<ProductionQueueItem>(chapel)) continue;
                if (CommandRouter.GetTrainQueueLength(em, chapel) >= Cfg.maxTrainQueue) continue;

                string sectId = em.GetComponentData<ChapelTag>(chapel).SectId.ToString();
                string unitId = SectConfig.UnitIdFor(sectId);
                if (unitId == null) continue;
                if (CountAliveAndQueued(em, faction, unitId) >= Cfg.sectUnitCap) continue;
                if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) continue;

                // Affordability CHECK only — TrainCommandDirect spends on
                // every peer (docs/Multiplayer_LAN_Readiness.md).
                var cost = AICommon.ToCost(def.cost);
                if (!FactionEconomy.CanAfford(em, faction, cost)) continue;
                if (AIPivotalReserve.ShouldHold(em, faction, cost)) continue;
                CommandRouter.IssueTrain(em, chapel, unitId, CommandSource.AI);
                AILogger.Log(faction, "MILITARY",
                    $"Alanthor: queued {unitId} at the {sectId.Substring(5)} chapel");
                return; // one per tick
            }
        }

        /// <summary>Living units of the exact type plus copies waiting in any
        /// of this faction's train queues — the sect-unit cap check.</summary>
        private static int CountAliveAndQueued(EntityManager em, Faction faction, string unitId)
        {
            int n = 0;
            var key = new FixedString64Bytes(unitId);   // no ToString per unit
            var uq = QC_UnitTypeIdFactionTagHealth.Get(em, QT_UnitTypeIdFactionTagHealth);
            using (var uEnts = uq.ToEntityArray(Allocator.Temp))
            using (var uFacs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < uEnts.Length; i++)
                {
                    if (uFacs[i].Value != faction) continue;
                    if (em.GetComponentData<Health>(uEnts[i]).Value <= 0) continue;
                    if (em.GetComponentData<UnitTypeId>(uEnts[i]).Value == key) n++;
                }
            }
            var tq = QC_FactionTagProductionQueueItem.Get(em, QT_FactionTagProductionQueueItem);
            using (var tEnts = tq.ToEntityArray(Allocator.Temp))
            using (var tFacs = tq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < tEnts.Length; i++)
                {
                    if (tFacs[i].Value != faction) continue;
                    var buf = em.GetBuffer<ProductionQueueItem>(tEnts[i]);
                    for (int j = 0; j < buf.Length; j++)
                        if (buf[j].Kind == ProductionKind.Train
                            && buf[j].Id == key) n++;
                }
            }
            return n;
        }
    
        // ------------------------------------------------------------------
        // SECT BUILDINGS -- build them, research at them, train at them
        // ------------------------------------------------------------------


        /// <summary>Sect building id for an adopted sect, or null when that
        /// sect has not been cut over to the canon building yet.</summary>
        private static string SectBuildingIdFor(string sectId) => sectId switch
        {
            SectConfig.Antiquity   => "Sect_Reliquary",
            SectConfig.Renewal     => "Sect_MendingHall",
            SectConfig.Fortitude   => "Sect_Stonehold",
            SectConfig.Reclamation => "Sect_Veilworks",
            SectConfig.War         => "Sect_MusterYard",
            _                      => null,
        };

        private static int CountSectBuildings(EntityManager em, Faction faction, string buildingId)
            => buildingId switch
            {
                "Sect_Reliquary"   => CountFactionBuildingsByTag<ReliquaryTag>(em, faction),
                "Sect_MendingHall" => CountFactionBuildingsByTag<MendingHallTag>(em, faction),
                "Sect_Stonehold"   => CountFactionBuildingsByTag<StoneholdTag>(em, faction),
                "Sect_Veilworks"   => CountFactionBuildingsByTag<VeilworksTag>(em, faction),
                "Sect_MusterYard"  => CountFactionBuildingsByTag<MusterYardTag>(em, faction),
                _                  => 0,
            };

        private static bool AnySectBuildingUnderConstruction(EntityManager em, Faction faction, string buildingId)
            => buildingId switch
            {
                "Sect_Reliquary"   => AnyFactionBuildingUnderConstruction<ReliquaryTag>(em, faction),
                "Sect_MendingHall" => AnyFactionBuildingUnderConstruction<MendingHallTag>(em, faction),
                "Sect_Stonehold"   => AnyFactionBuildingUnderConstruction<StoneholdTag>(em, faction),
                "Sect_Veilworks"   => AnyFactionBuildingUnderConstruction<VeilworksTag>(em, faction),
                "Sect_MusterYard"  => AnyFactionBuildingUnderConstruction<MusterYardTag>(em, faction),
                _                  => false,
            };

        /// <summary>
        /// Raise the building of an adopted sect, one at a time, in the same
        /// priority order the AI adopts them. Returns true when a foundation
        /// went down this tick so the caller can skip its other build attempts.
        /// </summary>
        private static bool TryBuildSectBuildings(Faction faction, EntityManager em, float3 hallPos)
        {
            for (int i = 0; i < AlanthorSectPriority.Length; i++)
            {
                string sectId = AlanthorSectPriority[i];
                string buildingId = SectBuildingIdFor(sectId);
                if (buildingId == null) continue;
                if (!SectQuery.IsAdopted(em, faction, sectId)) continue;
                if (CountSectBuildings(em, faction, buildingId) >= Cfg.sectBuildingTarget) continue;
                if (AnySectBuildingUnderConstruction(em, faction, buildingId)) continue;

                if (TryBuildOnce(faction, em, hallPos, buildingId, 14f, 26f, holdable: true))
                {
                    AILogger.Log(faction, "STRATEGY",
                        $"Alanthor: raising {buildingId} for the {sectId.Substring(5)} sect");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Buy the sect research at its own building. One per tick. The
        /// research host and the affordability check both live in the shared
        /// ladder (SimpleAISystem.TryResearchTech); this only decides WHICH
        /// tech is worth asking for.
        /// </summary>
        private static void TryResearchSectTech(Faction faction, EntityManager em)
        {
            if (!TechCatalog.IsReady) return;

            for (int i = 0; i < AlanthorSectPriority.Length; i++)
            {
                string sectId = AlanthorSectPriority[i];
                string buildingId = SectBuildingIdFor(sectId);
                if (buildingId == null) continue;
                if (!SectQuery.IsAdopted(em, faction, sectId)) continue;

                Entity host = FindCompletedBuilding(em, faction, buildingId);
                if (host == Entity.Null) continue;

                if (!TechCatalog.TryGetBuilding(buildingId, out var bdef) || bdef?.research == null) continue;
                for (int t = 0; t < bdef.research.Length; t++)
                {
                    string techId = bdef.research[t];
                    var research = FactionResearchState.Instance;
                    if (research != null && research.HasResearched(faction, techId)) continue;
                    if (!TechCatalog.TryGetTechnology(techId, out var tdef) || tdef == null) continue;

                    // Affordability CHECK only — ResearchCommandDirect spends
                    // on every peer (docs/Multiplayer_LAN_Readiness.md).
                    var cost = AICommon.ToCost(tdef.cost);
                    if (!FactionEconomy.CanAfford(em, faction, cost)) continue;
                    if (AIPivotalReserve.ShouldHold(em, faction, cost)) continue;

                    CommandRouter.IssueResearch(em, host, techId, CommandSource.AI);
                    AILogger.Log(faction, "STRATEGY",
                        $"Alanthor: researching {techId} at the {sectId.Substring(5)} building");
                    return; // one per tick
                }
            }
        }

        /// <summary>First completed building of this id owned by the faction,
        /// or Entity.Null. Skips foundations -- a building under construction
        /// cannot host research or training.</summary>
        private static Entity FindCompletedBuilding(EntityManager em, Faction faction, string buildingId)
        {
            switch (buildingId)
            {
                case "Sect_Reliquary":   return FirstCompleted<ReliquaryTag>(em, faction);
                case "Sect_MendingHall": return FirstCompleted<MendingHallTag>(em, faction);
                case "Sect_Stonehold":   return FirstCompleted<StoneholdTag>(em, faction);
                case "Sect_Veilworks":   return FirstCompleted<VeilworksTag>(em, faction);
                case "Sect_MusterYard":  return FirstCompleted<MusterYardTag>(em, faction);
                default: return Entity.Null;
            }
        }

        private static Entity FirstCompleted<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = AIQueryCache.TagFaction<T>(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                return ents[i];
            }
            return Entity.Null;
        }

        /// <summary>
        /// Train each adopted sect's unit at that sect's own BUILDING. Canon
        /// moved the sect unit off the chapel and onto the sect building
        /// (docs/Design/Sects.md section 1); TryTrainSectUnits still covers the
        /// chapel path for the eight sects that have no building yet.
        /// </summary>
        private static void TryTrainSectUnitsAtSectBuildings(Faction faction, EntityManager em)
        {
            if (!TechCatalog.IsReady) return;

            for (int i = 0; i < AlanthorSectPriority.Length; i++)
            {
                string sectId = AlanthorSectPriority[i];
                string buildingId = SectBuildingIdFor(sectId);
                if (buildingId == null) continue;
                if (!SectQuery.IsAdopted(em, faction, sectId)) continue;

                Entity host = FindCompletedBuilding(em, faction, buildingId);
                if (host == Entity.Null) continue;
                if (!em.HasBuffer<ProductionQueueItem>(host)) continue;
                if (CommandRouter.GetTrainQueueLength(em, host) >= Cfg.maxTrainQueue) continue;

                string unitId = SectConfig.UnitIdFor(sectId);
                if (unitId == null) continue;
                if (CountAliveAndQueued(em, faction, unitId) >= Cfg.sectUnitCap) continue;
                if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) continue;

                // Affordability CHECK only — TrainCommandDirect spends on
                // every peer (docs/Multiplayer_LAN_Readiness.md).
                var cost = AICommon.ToCost(def.cost);
                if (!FactionEconomy.CanAfford(em, faction, cost)) continue;
                if (AIPivotalReserve.ShouldHold(em, faction, cost)) continue;

                CommandRouter.IssueTrain(em, host, unitId, CommandSource.AI);
                AILogger.Log(faction, "MILITARY",
                    $"Alanthor: queued {unitId} at the {sectId.Substring(5)} building");
                return; // one per tick
            }
        }
}
}
