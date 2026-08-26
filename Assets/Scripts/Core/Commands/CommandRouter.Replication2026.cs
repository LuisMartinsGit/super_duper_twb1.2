// CommandRouter.Replication2026.cs
// The six player actions that still wrote ECS directly, brought under lockstep.
//
// Each of these was a UI button reaching into the EntityManager itself, so the
// effect existed on the clicking peer and nowhere else: one player saw a sect
// power land, the other saw their units die to nothing; one saw a wall become a
// tower, the other kept shooting at a wall. Because none of them had a command
// type, there was no replication path to fix — only a bypass to close.
//
// They all follow the shape the earlier conversions established:
//   * the ISSUING peer validates and pays (affordability, cooldown, slot rules)
//   * every peer applies the MUTATION, recomputing anything derivable
//   * the lockstep payload carries only what cannot be recomputed
//
// docs/Multiplayer_LAN_Readiness.md, docs/Multiplayer_Audit.md

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Systems.Sect;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        // ═══════════════════════════════════════════════════════════════
        // SECT ACTIVE POWERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Cast a sect's active power at a ground target. The most
        /// consequential of the unreplicated actions: these do AoE damage and
        /// spawn strikes, so a cast that existed on one peer only made the two
        /// worlds disagree about who was alive.
        /// </summary>
        public static void IssueSectPower(EntityManager em, Faction faction, string sectId,
            int tier, float3 targetPos, CommandSource source = CommandSource.LocalPlayer)
        {
            if (string.IsNullOrEmpty(sectId)) return;

            if (ShouldQueueForLockstep(source))
            {
                // Cooldown is checked on BOTH sides: here so the caster gets
                // immediate feedback, and again inside Fire on every peer so a
                // replayed cast cannot fire a power that is not ready.
                if (!SectActivePowerHelper.CanFire(em, faction, sectId, tier)) return;

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SectPower,
                    EntityNetworkId = (int)faction,   // no entity — the faction IS the caster
                    BuildingId = sectId,
                    TargetEntityId = tier,
                    TargetPosition = targetPos
                });
            }
            else
            {
                SectActivePowerHelper.Fire(em, faction, sectId, tier, targetPos);
            }
        }

        /// <summary>Post-lockstep application. Every peer runs this.</summary>
        public static void SectPowerDirect(EntityManager em, Faction faction, string sectId,
            int tier, float3 targetPos)
            => SectActivePowerHelper.Fire(em, faction, sectId, tier, targetPos);

        // ═══════════════════════════════════════════════════════════════
        // RELIQUARY ABILITIES (Antiquity)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Scry (0) / Lockout (1) target the ground; Vision (2) is self.</summary>
        public static void IssueReliquaryAbility(EntityManager em, Entity reliquary, int ability,
            float3 targetPos, CommandSource source = CommandSource.LocalPlayer)
        {
            if (reliquary == Entity.Null || !em.Exists(reliquary)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, reliquary);
                if (networkId <= 0)
                {
                    ReliquaryAbilityDirect(em, reliquary, ability, targetPos);
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.ReliquaryAbility,
                    EntityNetworkId = networkId,
                    TargetEntityId = ability,
                    TargetPosition = targetPos
                });
            }
            else
            {
                ReliquaryAbilityDirect(em, reliquary, ability, targetPos);
            }
        }

        public static void ReliquaryAbilityDirect(EntityManager em, Entity reliquary,
            int ability, float3 targetPos)
            => TheWaningBorder.Systems.Sect.ReliquaryHelper.Fire(em, reliquary, ability, targetPos);

        // ═══════════════════════════════════════════════════════════════
        // WALL PLACEMENT (hub + extend)
        //
        // Wall hubs/segments used to be created straight from the click
        // handler (and from the AI's wall doctrine), outside the command
        // stream. Beyond the walls existing on one peer only, the off-tick
        // entity creation consumed NetworkId slots on that peer alone and
        // shifted every later id assigned in the same tick — corrupting
        // UNRELATED commands. Both paths now ride these two opcodes; the
        // spend happens in the executor on every peer.
        // docs/Multiplayer_Desync_Sweep_2026-08-16.md
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Wall hub. Builder-driven 5 s construction by default;
        /// <paramref name="autoBuild"/> = the AI/extend flavour that
        /// self-builds in 30 s with no builder.</summary>
        public static void IssuePlaceWallHub(EntityManager em, float3 pos, Faction faction,
            bool autoBuild = false, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;

            if (ShouldQueueForLockstep(source))
            {
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.PlaceWallHub,
                    EntityNetworkId = (int)faction,
                    TargetEntityId = autoBuild ? 1 : 0,
                    TargetPosition = pos
                });
            }
            else
            {
                PlaceWallHubDirect(em, pos, faction, autoBuild);
            }
        }

        /// <summary>Executor — every peer. Validates + spends, then creates
        /// the hub exactly as the old click handler did.</summary>
        public static Entity PlaceWallHubDirect(EntityManager em, float3 pos, Faction faction,
            bool autoBuild = false)
        {
            if (!BuildCosts.TryGet("Alanthor_Wall", out var cost)) cost = default;
            if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost))
                return Entity.Null;

            Entity hub = TheWaningBorder.Entities.AlanthorWall.CreateHub(em, pos, faction);

            float total = autoBuild ? 30f : 5f;
            if (!em.HasComponent<UnderConstruction>(hub))
                em.AddComponentData(hub, new UnderConstruction { Progress = 0f, Total = total });
            if (autoBuild && !em.HasComponent<AutoConstructTag>(hub))
                em.AddComponent<AutoConstructTag>(hub);
            if (em.HasComponent<Health>(hub))
            {
                var hp = em.GetComponentData<Health>(hub);
                em.SetComponentData(hub, new Health { Value = 1, Max = hp.Max });
            }

            // Terrain anchor: seal the hub-to-rock gap with curtain modules.
            TheWaningBorder.Entities.AlanthorWall.SealToTerrain(em, hub, autoConstruct: true);
            return hub;
        }

        /// <summary>
        /// Per-hub "Build Wall": a connecting segment from <paramref name="sourceHub"/>
        /// to either an existing hub (<paramref name="snapHub"/>) or a NEW
        /// self-building hub at <paramref name="pos"/>.
        /// </summary>
        public static void IssueWallExtend(EntityManager em, Entity sourceHub, Entity snapHub,
            float3 pos, Faction faction, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (sourceHub == Entity.Null || !em.Exists(sourceHub)) return;

            if (ShouldQueueForLockstep(source))
            {
                int sourceId = GetNetworkId(em, sourceHub);
                if (sourceId <= 0)
                {
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] WallExtend dropped: source hub has no NetworkId in MP.");
                    return;
                }
                int snapId = snapHub != Entity.Null ? GetNetworkId(em, snapHub) : 0;

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.WallExtend,
                    EntityNetworkId = sourceId,
                    TargetEntityId = snapId > 0 ? snapId : 0,
                    SecondaryTargetId = (int)faction,
                    TargetPosition = pos
                });
            }
            else
            {
                WallExtendDirect(em, sourceHub, snapHub, pos, faction);
            }
        }

        /// <summary>Executor — every peer. Mirrors the old SpawnExtendedWallHub
        /// body: snap-to-hub builds only the segment (free); a new hub pays the
        /// standard hub cost and self-builds, as do the segment instances.</summary>
        public static void WallExtendDirect(EntityManager em, Entity sourceHub, Entity snapHub,
            float3 pos, Faction faction)
        {
            const float BuildSeconds = 30f;
            if (sourceHub == Entity.Null || !em.Exists(sourceHub)) return;

            Entity hub = snapHub;
            if (hub != Entity.Null && em.Exists(hub))
            {
                if (TheWaningBorder.Entities.AlanthorWall.AreHubsConnected(em, sourceHub, hub))
                    return; // identical no-op on every peer
            }
            else
            {
                if (!BuildCosts.TryGet("Alanthor_Wall", out var cost)) cost = default;
                if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost))
                    return;

                hub = TheWaningBorder.Entities.AlanthorWall.CreateHub(em, pos, faction);
                em.AddComponentData(hub,
                    new UnderConstruction { Progress = 0f, Total = BuildSeconds });
                em.AddComponent<AutoConstructTag>(hub);
                if (em.HasComponent<Health>(hub))
                {
                    var hp = em.GetComponentData<Health>(hub);
                    em.SetComponentData(hub, new Health { Value = 1, Max = hp.Max });
                }
                TheWaningBorder.Entities.AlanthorWall.SealToTerrain(em, hub, autoConstruct: true);
            }

            Entity segment = TheWaningBorder.Entities.AlanthorWall.CreateSegment(
                em, sourceHub, hub, faction);

            // Tag every spawned wall instance for auto-construction. Snapshot
            // first — the AddComponentData calls below are structural.
            if (em.HasBuffer<WallInstanceRef>(segment))
            {
                var instances = em.GetBuffer<WallInstanceRef>(segment);
                int count = instances.Length;
                var snapshot = new Unity.Collections.NativeArray<Entity>(
                    count, Unity.Collections.Allocator.Temp);
                for (int i = 0; i < count; i++)
                    snapshot[i] = instances[i].Instance;

                for (int i = 0; i < count; i++)
                {
                    var inst = snapshot[i];
                    if (!em.Exists(inst)) continue;
                    if (!em.HasComponent<UnderConstruction>(inst))
                        em.AddComponentData(inst,
                            new UnderConstruction { Progress = 0f, Total = BuildSeconds });
                    if (!em.HasComponent<AutoConstructTag>(inst))
                        em.AddComponent<AutoConstructTag>(inst);
                    if (em.HasComponent<Health>(inst))
                    {
                        var hp = em.GetComponentData<Health>(inst);
                        em.SetComponentData(inst, new Health { Value = 1, Max = hp.Max });
                    }
                }
                snapshot.Dispose();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // VAULT DEPOSIT / WITHDRAW
        //
        // Moves resources between the faction bank (checksummed) and the
        // vault's VaultStorage — both sides of the move must land on every
        // peer, and interest compounding starts from the stored amount.
        // docs/Multiplayer_Desync_Sweep_2026-08-16.md
        // ═══════════════════════════════════════════════════════════════

        public static void IssueVaultTransfer(EntityManager em, Entity vaultEntity,
            int resourceType, int amount, bool deposit,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (vaultEntity == Entity.Null || !em.Exists(vaultEntity)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, vaultEntity);
                if (networkId <= 0)
                {
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] VaultTransfer dropped: vault has no NetworkId in MP.");
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.VaultTransfer,
                    EntityNetworkId = networkId,
                    TargetEntityId = (resourceType & 0xFF) | (deposit ? 0x100 : 0),
                    SecondaryTargetId = amount,
                });
            }
            else
            {
                VaultTransferDirect(em, vaultEntity, resourceType, amount, deposit);
            }
        }

        /// <summary>Executor — every peer. Faction comes from the vault's own
        /// FactionTag; a short bank rejects the whole deposit identically
        /// everywhere.</summary>
        public static void VaultTransferDirect(EntityManager em, Entity vaultEntity,
            int resourceType, int amount, bool deposit)
        {
            if (vaultEntity == Entity.Null || !em.Exists(vaultEntity)) return;
            if (!em.HasComponent<VaultStorage>(vaultEntity)) return;
            if (!em.HasComponent<FactionTag>(vaultEntity)) return;

            var vault = em.GetComponentData<VaultStorage>(vaultEntity);
            var faction = em.GetComponentData<FactionTag>(vaultEntity).Value;

            if (deposit)
            {
                if (amount <= 0) return;
                if (!TheWaningBorder.Economy.FactionEconomy.Spend(
                        em, faction, VaultTransferCost(resourceType, amount))) return;
                vault.ResourceType = resourceType;
                vault.StoredAmount += amount;
            }
            else
            {
                int withdraw = (int)vault.StoredAmount;
                if (withdraw <= 0) return;
                TheWaningBorder.Economy.FactionEconomy.Add(
                    em, faction, VaultTransferCost(vault.ResourceType, withdraw));
                vault.StoredAmount = 0f;
                vault.ResourceType = 0;
            }
            vault.LockTimer = vault.LockDuration;
            em.SetComponentData(vaultEntity, vault);
        }

        private static Cost VaultTransferCost(int type, int amount) => type switch
        {
            1 => Cost.Of(supplies: amount),
            2 => Cost.Of(iron: amount),
            3 => Cost.Of(veilstone: amount),
            4 => Cost.Of(veilsteel: amount),
            5 => Cost.Of(glow: amount),
            _ => default,
        };

        // ═══════════════════════════════════════════════════════════════
        // BAZAAR PACK / UNPACK
        //
        // The pack tag triggers BazaarPackSystem to DESTROY the building and
        // spawn a wagon — a structural change plus fresh NetworkIds, so a
        // local-only tag forked both the entity set and the id sequence.
        // docs/Multiplayer_Desync_Sweep_2026-08-16.md
        // ═══════════════════════════════════════════════════════════════

        public static void IssueBazaarPack(EntityManager em, Entity bazaar, bool pack,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (bazaar == Entity.Null || !em.Exists(bazaar)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, bazaar);
                if (networkId <= 0)
                {
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] BazaarPack dropped: bazaar has no NetworkId in MP.");
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.BazaarPack,
                    EntityNetworkId = networkId,
                    TargetEntityId = pack ? 1 : 0,
                });
            }
            else
            {
                BazaarPackDirect(em, bazaar, pack);
            }
        }

        /// <summary>Executor — every peer. Idempotent: the tag add is skipped
        /// when already present, so a replay cannot double-trigger.</summary>
        public static void BazaarPackDirect(EntityManager em, Entity bazaar, bool pack)
        {
            if (bazaar == Entity.Null || !em.Exists(bazaar)) return;
            if (pack)
            {
                if (!em.HasComponent<BazaarPackCommand>(bazaar))
                    em.AddComponent<BazaarPackCommand>(bazaar);
            }
            else
            {
                if (!em.HasComponent<BazaarUnpackCommand>(bazaar))
                    em.AddComponent<BazaarUnpackCommand>(bazaar);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SECT GLOW ALLOCATION
        //
        // Allocated glow HALVES the sect's active-power cooldown, so a local
        // allocation makes CanFire disagree between peers — and a replicated
        // SectPower command then fires on one peer and is dropped on the
        // other. docs/Multiplayer_Desync_Sweep_2026-08-16.md
        // ═══════════════════════════════════════════════════════════════

        public static void IssueSectGlowAlloc(EntityManager em, Faction faction, string sectId,
            bool allocate, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (string.IsNullOrEmpty(sectId)) return;

            if (ShouldQueueForLockstep(source))
            {
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SectGlowAlloc,
                    EntityNetworkId = (int)faction,
                    BuildingId = sectId,
                    TargetEntityId = allocate ? 1 : 0,
                });
            }
            else
            {
                SectGlowAllocDirect(em, faction, sectId, allocate);
            }
        }

        /// <summary>Executor — every peer. The helper is a no-op when the
        /// state already matches, so replays cannot double-apply.</summary>
        public static void SectGlowAllocDirect(EntityManager em, Faction faction, string sectId,
            bool allocate)
        {
            if (allocate)
                SectActivePowerHelper.AllocateGlow(em, faction, sectId);
            else
                SectActivePowerHelper.DeallocateGlow(em, faction, sectId);
        }

        // ═══════════════════════════════════════════════════════════════
        // WALL INSTANCE UPGRADE (tower / gate)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Start a wall piece's upgrade. The COST is paid by the issuer; the
        /// timer component is stamped on every peer, so the upgrade completes at
        /// the same tick everywhere.
        /// </summary>
        public static void IssueWallUpgrade(EntityManager em, Entity wall, int upgradeType,
            float duration, CommandSource source = CommandSource.LocalPlayer)
        {
            if (wall == Entity.Null || !em.Exists(wall)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, wall);
                if (networkId <= 0)
                {
                    // DROP, never stamp locally: a local-only upgrade timer
                    // completes on one peer alone, and the old fallback also
                    // skipped the charge. A wall without a NetworkId in MP is
                    // a spawn-path bug — surface it.
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] WallUpgrade dropped: wall has no NetworkId in MP.");
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.WallUpgrade,
                    EntityNetworkId = networkId,
                    TargetEntityId = upgradeType,
                    // Duration rides the spare position field rather than being
                    // recomputed, so a future balance change to one peer's
                    // constants cannot desync an upgrade already in flight.
                    TargetPosition = new float3(duration, 0f, 0f)
                });
            }
            else
            {
                WallUpgradeDirect(em, wall, upgradeType, duration);
            }
        }

        public static void WallUpgradeDirect(EntityManager em, Entity wall, int upgradeType, float duration)
        {
            if (wall == Entity.Null || !em.Exists(wall)) return;
            if (em.HasComponent<WallUpgradeState>(wall)) return;   // already upgrading
            if (duration <= 0f) duration = 10f;
            em.AddComponentData(wall, new WallUpgradeState
            {
                UpgradeType = (byte)upgradeType,
                Duration = duration,
                Remaining = duration,
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // FIENDSTONE KEEP WINGS
        // ═══════════════════════════════════════════════════════════════

        public static void IssueKeepWing(EntityManager em, Entity keep, byte wing, float duration,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (keep == Entity.Null || !em.Exists(keep)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, keep);
                if (networkId <= 0)
                {
                    // DROP — same rationale as the WallUpgrade fallback above.
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] KeepWing dropped: keep has no NetworkId in MP.");
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.KeepWing,
                    EntityNetworkId = networkId,
                    TargetEntityId = wing,
                    TargetPosition = new float3(duration, 0f, 0f)
                });
            }
            else
            {
                KeepWingDirect(em, keep, wing, duration);
            }
        }

        public static void KeepWingDirect(EntityManager em, Entity keep, byte wing, float duration)
        {
            if (keep == Entity.Null || !em.Exists(keep)) return;
            if (!em.HasComponent<KeepWings>(keep)) return;
            if (em.HasComponent<KeepWingConstruction>(keep)) return;   // one at a time
            if (duration <= 0f) duration = 30f;
            em.AddComponentData(keep, new KeepWingConstruction
            {
                Wing = wing, Remaining = duration, Total = duration,
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // UNIT PROMOTION
        // ═══════════════════════════════════════════════════════════════

        public static void IssueUnitPromote(EntityManager em, Entity unit,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, unit);
                if (networkId <= 0)
                {
                    UnitRankCommandHelper.Execute(em, unit);
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.UnitPromote,
                    EntityNetworkId = networkId
                });
            }
            else
            {
                UnitRankCommandHelper.Execute(em, unit);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SHIFT-QUEUED WAYPOINTS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Append one waypoint to a unit's command queue. Ordinary moves were
        /// replicated long ago; the shift-queued variant never was, so a queued
        /// march existed only on the machine that drew it and the other peer's
        /// copy of those units stood still.
        /// </summary>
        public static void IssueQueuedWaypoint(EntityManager em, Entity unit,
            QueuedCommandType type, float3 targetPos, Entity targetEntity,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, unit);
                if (networkId <= 0)
                {
                    QueuedWaypointDirect(em, unit, type, targetPos, targetEntity);
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.QueueWaypoint,
                    EntityNetworkId = networkId,
                    TargetEntityId = (int)type,
                    SecondaryTargetId = GetNetworkId(em, targetEntity),
                    TargetPosition = targetPos
                });
            }
            else
            {
                QueuedWaypointDirect(em, unit, type, targetPos, targetEntity);
            }
        }

        public static void QueuedWaypointDirect(EntityManager em, Entity unit,
            QueuedCommandType type, float3 targetPos, Entity targetEntity)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (!em.HasBuffer<QueuedCommand>(unit)) em.AddBuffer<QueuedCommand>(unit);
            em.GetBuffer<QueuedCommand>(unit).Add(new QueuedCommand
            {
                Type = type,
                TargetPosition = targetPos,
                TargetEntity = targetEntity,
            });

            // The activation must travel WITH the payload: CommandQueueSystem
            // only drains units that carry CommandQueueActive, and the input
            // manager used to add that tag locally — so a remote peer received
            // the waypoints but its copy of the unit never moved (2026-08-16
            // sweep, B3). Adding it here makes the direct path (SP) and the
            // replicated path (every MP peer) behave identically.
            if (!em.HasComponent<CommandQueueActive>(unit))
                em.AddComponent<CommandQueueActive>(unit);
        }
    }
}
