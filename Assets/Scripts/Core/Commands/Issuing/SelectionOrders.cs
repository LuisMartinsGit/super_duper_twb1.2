// SelectionOrders.cs
// The order-issuing layer: what a click MEANS for the current selection.
//
// Lifted out of RTSInputManager (Presentation) because none of it is input.
// It reads no mouse and no screen: the input layer decides THAT an order was
// given and what it was aimed at, then hands the target here. What a unit is
// able to do, who owns it, and which command that becomes is simulation
// knowledge, and it routes through CommandRouter, which lives here too.
//
// Runtime must not know about SelectionSystem, so the selection arrives as a
// delegate supplied by the input layer.

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Core.Commands.Issuing
{
    /// <summary>
    /// Turns "the player aimed this at that" into commands on the selection.
    /// One instance is owned by the input layer.
    /// </summary>
    public sealed class SelectionOrders
    {
        // Cached: this runs on every Shift release, and CreateEntityQuery
        // matches every archetype in the world. See Core/CachedEntityQuery.cs.
        static readonly ComponentType[] FrozenTypes = { typeof(CommandQueueFrozen) };
        TheWaningBorder.Core.CachedEntityQuery _frozenQuery;

        private readonly EntityManager _em;
        private readonly Func<List<Entity>> _selection;

        /// <summary>Metres between slots in a formation move.</summary>
        public float FormationSpacing;

        /// <summary>Shape a formation move arranges itself into.</summary>
        public FormationShape Shape;

        public SelectionOrders(EntityManager em, Func<List<Entity>> selection)
        {
            _em = em;
            _selection = selection;
        }

        private List<Entity> CurrentSelection => _selection();

        /// <summary>
        /// Send every OWNED unit in a selection to a point. Static because the
        /// minimap issues this without owning a SelectionOrders instance — it
        /// hands over the world and the selection, the same contract the input
        /// layer uses. What counts as "a unit I may order" is decided here,
        /// not in the panel that was clicked.
        /// </summary>
        /// The move is a FORMATION move in the player's current shape, the same
        /// order a right-click on the ground gives (2026-10-16: it used to be
        /// one loose IssueMove per unit, so a minimap order scattered the
        /// group into a blob).
        public static void IssueMoveToOwnedUnits(EntityManager em, List<Entity> selection,
                                                 float3 destination, Faction faction,
                                                 FormationShape shape)
        {
            if (selection == null) return;

            var units = new List<Entity>(selection.Count);
            foreach (var entity in selection)
            {
                if (!em.Exists(entity)) continue;
                if (!em.HasComponent<UnitTag>(entity)) continue;
                if (!em.HasComponent<FactionTag>(entity)) continue;
                if (em.GetComponentData<FactionTag>(entity).Value != faction) continue;
                units.Add(entity);
            }
            if (units.Count == 0) return;

            CommandRouter.IssueFormationMove(em, units, destination, shape, CommandSource.LocalPlayer);
        }

        public bool CanGarrisonWall(Entity e)
        {
            if (!_em.Exists(e) || !IsOwnedByLocalPlayer(e)) return false;
            if (_em.HasComponent<BuildingTag>(e) || !_em.HasComponent<UnitTag>(e)) return false;
            if (_em.HasComponent<CanBuild>(e) || _em.HasComponent<WorkerTag>(e)) return false;
            if (_em.HasComponent<CavalryTag>(e)) return false;
            // Heroes stay on the ground (docs/Design/Age_1_Alanthor.md § The
            // stone wall).
            if (_em.HasComponent<HeroLevel>(e)) return false;
            var cls = _em.GetComponentData<UnitTag>(e).Class;
            if (cls == UnitClass.Siege || cls == UnitClass.Scout) return false;
            return true;
        }

        // The wall-walk slot layout and the layered-move issue live in
        // WallTopOrders (2026-10-20) so the AI's wall manning shares them.

        // Order selected FOOT units onto the wall top, spread along the wall
        // around the clicked point. Returns how many units took the order, so
        // the input layer can fall through to its normal handling when nobody
        // in the selection may stand on a wall.
        public int IssueWallTopMove(float3 wallTopPoint)
        {
            // Overpass-bridge decks are roads, not fortifications: ANY
            // movable unit (cavalry, siege, workers included) may cross.
            // Wall ramparts keep the AoE4 foot-unit garrison rule.
            bool isOverpassDeck = TheWaningBorder.World.Terrain.BridgeSurface
                .TryGetDeckHeight(wallTopPoint.x, wallTopPoint.z, out _);

            var units = new List<Entity>();
            foreach (var e in CurrentSelection)
            {
                if (isOverpassDeck)
                {
                    if (_em.Exists(e) && IsOwnedByLocalPlayer(e)
                        && !_em.HasComponent<BuildingTag>(e)
                        && _em.HasComponent<UnitTag>(e))
                        units.Add(e);
                }
                else if (CanGarrisonWall(e)) units.Add(e);
            }
            return WallTopOrders.IssueWallTopMove(_em, units, wallTopPoint,
                FormationSpacing, CommandSource.LocalPlayer);
        }

        /// <summary>
        /// A right-click on a friendly or allied STONE wall piece — hub,
        /// curtain (the segment that owns the curtain mesh, which the old
        /// handler skipped as "data-only"), wall tower or gate — orders the
        /// selection's foot units onto its wall-walk at the deck cell nearest
        /// the click (docs/Design/Age_1_Alanthor.md § The stone wall,
        /// 2026-10-20). A click on the wall's FACE missed the deck plane and
        /// used to fall through to a plain move to the foot of the wall.
        /// Returns how many units took the order (0: not a walkable wall, or
        /// nobody here may stand on one — the caller falls through).
        /// </summary>
        public int TryWallTopOrder(Entity wallPiece, float3 clickWorld)
        {
            if (wallPiece == Entity.Null || !_em.Exists(wallPiece)) return 0;
            if (!_em.HasComponent<WallTag>(wallPiece)) return 0;
            if (_em.HasComponent<PalisadeTag>(wallPiece)) return 0;          // a fence has no walk
            if (_em.HasComponent<UnderConstruction>(wallPiece)) return 0;
            if (!_em.HasComponent<FactionTag>(wallPiece)) return 0;
            if (Alliances.AreHostile(GameSettings.LocalPlayerFaction,
                    _em.GetComponentData<FactionTag>(wallPiece).Value)) return 0;

            if (!TryNearestDeckPoint(clickWorld, WallTopSearchRadius, out float3 deck))
            {
                if (!_em.HasComponent<LocalTransform>(wallPiece)) return 0;
                var at = _em.GetComponentData<LocalTransform>(wallPiece).Position;
                if (!TryNearestDeckPoint(at, WallTopSearchRadius, out deck)) return 0;
            }
            return IssueWallTopMove(deck);
        }

        /// <summary>How far from the click a wall-top order looks for a deck
        /// cell, metres: past half the wall's depth plus a click on its foot.</summary>
        private const float WallTopSearchRadius = 4f;

        private static bool TryNearestDeckPoint(float3 near, float radius, out float3 point)
            => WallTopOrders.TryNearestDeckPoint(near, radius, out point);

        // Every wall instance, for resolving which one a deck cell belongs to.
        // Cached: CreateEntityQuery registers permanently with the world.
        static readonly ComponentType[] WallTypes =
        {
            ComponentType.ReadOnly<WallTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery _wallQuery;

        /// <summary>
        /// True when a walkable deck point may take a wall-top order from the
        /// local player: an overpass bridge (a road, nobody's), or the
        /// rampart of a wall owned by the local player or an ally. The deck
        /// layer is stamped walkable for EVERY faction's walls, so without
        /// this an enemy rampart would read as a place to walk to.
        /// </summary>
        public bool IsFriendlyRampartDeck(float3 deckPoint)
        {
            if (TheWaningBorder.World.Terrain.BridgeSurface
                    .TryGetDeckHeight(deckPoint.x, deckPoint.z, out _))
                return true;

            var cell = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(deckPoint);
            if (cell.x == int.MinValue) return false;

            // Same square StampWallLayersJob writes around each wall's cell.
            int half = TheWaningBorder.Systems.Navigation.StampWallLayersJob.FootprintCells / 2;
            var local = GameSettings.LocalPlayerFaction;

            var query = _wallQuery.Get(_em, WallTypes);
            using var walls = query.ToEntityArray(Allocator.Temp);
            using var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < walls.Length; i++)
            {
                var wc = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(xfs[i].Position);
                if (math.abs(wc.x - cell.x) > half || math.abs(wc.y - cell.y) > half) continue;
                if (!Alliances.AreHostile(local, factions[i].Value)) return true;
            }
            return false;
        }

        /// <summary>
        /// Put the selection's foot units into a reinforced wall
        /// (docs/Design/Age_1_Alanthor.md § Garrison slots). Each unit takes
        /// the nearest module with a free slot, starting from the one the
        /// player clicked, so a right-click on a manned module spills into
        /// its neighbours instead of failing. Returns false when the target
        /// is not a garrisonable wall or nobody in the selection can take a
        /// slot — the caller then falls through to its usual handling.
        /// </summary>
        /// <summary>
        /// FERALDIS right-click orders on your own building or War Rig
        /// (docs/Design/Age_1_Feraldis.md §3-5, §7.5):
        ///   slaves  → an extractor or the Great Forge: staff it (a damaged or
        ///             unfinished one is repaired / built as usual instead)
        ///   soldiers → the Great Forge: Melt Down
        ///   any unit → the Blood Altar: sacrifice
        ///   infantry → a War Rig: board it
        /// Returns false when nothing in the selection takes a Feraldis order,
        /// so the caller falls through to its usual handling.
        /// </summary>
        public bool TryFeraldisOrder(Entity target)
        {
            if (!_em.Exists(target) || !_em.HasComponent<FactionTag>(target)) return false;
            if (_em.GetComponentData<FactionTag>(target).Value != GameSettings.LocalPlayerFaction) return false;
            // A Runai War Wagon takes a squad on board like the Feraldis War Rig.
            bool wagon = _em.HasComponent<WarWagonTag>(target);
            if (!wagon && !TheWaningBorder.Feraldis.FeraldisEconomy.IsFeraldis(_em, GameSettings.LocalPlayerFaction)) return false;
            if (_em.HasComponent<UnderConstruction>(target)) return false;

            bool damaged = _em.HasComponent<Health>(target)
                && _em.GetComponentData<Health>(target).Value < _em.GetComponentData<Health>(target).Max;
            bool forge = _em.HasComponent<GreatForgeTag>(target);
            bool altar = _em.HasComponent<TempleOfRidanTag>(target);
            bool rig = _em.HasComponent<WarRigTag>(target);
            bool staffable = TheWaningBorder.Feraldis.FeraldisEconomy.CrewSlots(_em, target) > 0;

            bool any = false;
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e) || !IsOwnedByLocalPlayer(e)) continue;
                FeraldisOp op = FeraldisOp.None;
                if (_em.HasComponent<SlaveTag>(e))
                {
                    if (staffable && !damaged) op = FeraldisOp.Staff;
                    else if (altar) op = FeraldisOp.Sacrifice;
                }
                else if (forge && TheWaningBorder.Feraldis.FeraldisOrders.CanMelt(_em, e)) op = FeraldisOp.MeltDown;
                else if (altar && TheWaningBorder.Feraldis.FeraldisOrders.CanSacrifice(_em, e)) op = FeraldisOp.Sacrifice;
                else if (rig && TheWaningBorder.Feraldis.WarRig.CanLoad(_em, e, target)) op = FeraldisOp.RigLoad;
                if (op == FeraldisOp.None) continue;
                CommandRouter.IssueFeraldisOrder(_em, e, target, op, 0, default, CommandSource.LocalPlayer);
                any = true;
            }
            return any;
        }

        /// <summary>
        /// ENSHRINE (Curse_And_Shardroot.md § 3.1d, 2026-10-20): a right-click
        /// on a Temple of Ridan with the Shardroot's bearer in the selection
        /// sends the bearer to enshrine it there. Returns false when nobody in
        /// the selection carries the artifact or the target is no Temple, so
        /// the caller falls through to its usual handling. A refused order
        /// (not your Temple, unfinished, bound hero, cannot pay) says why and
        /// returns true — the click meant "enshrine", nothing else happens.
        /// Only the bearer takes the order; the rest of the selection walks
        /// alongside it.
        /// </summary>
        public bool TryEnshrineOrder(Entity target)
        {
            if (!ShardrootEnshrine.IsTemple(_em, target)) return false;
            Entity bearer = SelectedBearer();
            if (bearer == Entity.Null) return false;

            var refusal = ShardrootEnshrine.Check(_em, bearer, target, checkPrice: true);
            if (refusal != ShardrootEnshrine.Refusal.None)
            {
                SimSignals.NotifyError(ShardrootEnshrine.RefusalText(refusal));
                return true;
            }
            CommandRouter.IssueEnshrineShardroot(_em, bearer, target, CommandSource.LocalPlayer);

            // The escort follows to the Temple as a formation; the bearer is
            // left out so its own walk to the wall is not overwritten.
            var escort = CollectOwnedMovableSelection();
            escort.Remove(bearer);
            if (escort.Count > 0 && _em.HasComponent<LocalTransform>(target))
                CommandRouter.IssueFormationMove(_em, escort,
                    _em.GetComponentData<LocalTransform>(target).Position, Shape, CommandSource.LocalPlayer);
            return true;
        }

        /// <summary>
        /// The cursor hint over <paramref name="target"/> when the selection
        /// holds the Shardroot's bearer: "Enshrine the Shardroot", or why it
        /// cannot be (<paramref name="ok"/> false). Null when the hint does not
        /// apply (no bearer selected, or not a Temple).
        /// </summary>
        public string EnshrineHoverHint(Entity target, out bool ok)
        {
            ok = false;
            if (!ShardrootEnshrine.IsTemple(_em, target)) return null;
            Entity bearer = SelectedBearer();
            if (bearer == Entity.Null) return null;
            // A hostile Temple is an attack target, not an enshrine refusal.
            if (_em.HasComponent<FactionTag>(target)
                && Alliances.AreHostile(GameSettings.LocalPlayerFaction, _em.GetComponentData<FactionTag>(target).Value))
                return null;
            var refusal = ShardrootEnshrine.Check(_em, bearer, target, checkPrice: true);
            ok = refusal == ShardrootEnshrine.Refusal.None;
            return ok ? TheWaningBorder.Core.Localization.Loc.T("Enshrine the Shardroot") : ShardrootEnshrine.RefusalText(refusal);
        }

        /// <summary>The owned unit in the selection carrying the Shardroot,
        /// or Entity.Null (there is only ever one).</summary>
        public Entity SelectedBearer()
        {
            var sel = CurrentSelection;
            if (sel == null) return Entity.Null;
            foreach (var e in sel)
                if (IsOwnedByLocalPlayer(e) && ShardrootEnshrine.IsBearer(_em, e)) return e;
            return Entity.Null;
        }

        /// <summary>
        /// The actions-panel button: send the selected bearer to the nearest
        /// finished Temple of Ridan of its own. Returns the refusal text, or
        /// null when the order went out.
        /// </summary>
        public static string EnshrineAtNearestTemple(EntityManager em, Entity bearer)
        {
            if (!ShardrootEnshrine.IsBearer(em, bearer)) return ShardrootEnshrine.RefusalText(ShardrootEnshrine.Refusal.NoBearer);
            var f = em.GetComponentData<FactionTag>(bearer).Value;
            float3 from = em.GetComponentData<LocalTransform>(bearer).Position;
            var q = _templeQuery.Get(em, TempleTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != f) continue;
                if (ShardrootEnshrine.Check(em, bearer, ents[i], checkPrice: false) != ShardrootEnshrine.Refusal.None) continue;
                float d = math.distancesq(xfs[i].Position.xz, from.xz);
                if (d < bestD) { bestD = d; best = ents[i]; }
            }
            if (best == Entity.Null)
                return em.HasComponent<ShardboundHeroTag>(bearer)
                    ? ShardrootEnshrine.RefusalText(ShardrootEnshrine.Refusal.Bound)
                    : TheWaningBorder.Core.Localization.Loc.T("You have no finished Temple of Ridan to enshrine the Shardroot in");
            var refusal = ShardrootEnshrine.Check(em, bearer, best, checkPrice: true);
            if (refusal != ShardrootEnshrine.Refusal.None) return ShardrootEnshrine.RefusalText(refusal);
            CommandRouter.IssueEnshrineShardroot(em, bearer, best, CommandSource.LocalPlayer);
            return null;
        }

        static readonly ComponentType[] TempleTypes =
        {
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery _templeQuery;

        public bool TryGarrisonWall(Entity target)
        {
            if (!_em.Exists(target)) return false;
            if (!_em.HasBuffer<WallGarrisonSlot>(target)) return false;
            if (_em.HasComponent<UnderConstruction>(target)) return false;
            if (!_em.HasComponent<LocalTransform>(target)) return false;

            var faction = GameSettings.LocalPlayerFaction;
            if (!_em.HasComponent<FactionTag>(target)
                || _em.GetComponentData<FactionTag>(target).Value != faction) return false;

            float3 at = _em.GetComponentData<LocalTransform>(target).Position;
            bool any = false;
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e) || !IsOwnedByLocalPlayer(e)) continue;
                if (!TheWaningBorder.Entities.WallGarrison.IsFootUnit(_em, e)) continue;

                // The clicked module first; then the nearest one with room.
                Entity module = TheWaningBorder.Entities.WallGarrison.HasFreeSlot(_em, target)
                    ? target
                    : CommandRouter.FindGarrisonModuleNear(_em, at, faction, WallGarrisonSpill);
                if (module == Entity.Null) break;

                CommandRouter.IssueGarrisonWall(_em, e, module, CommandSource.LocalPlayer);
                any = true;
            }
            return any;
        }

        /// <summary>How far a garrison order spills along the wall when the
        /// clicked module is full, metres.</summary>
        private const float WallGarrisonSpill = 24f;

        public void IssueStopToSelection()
        {
            var selection = CurrentSelection;
            if (selection == null || selection.Count == 0) return;
            var issued = new HashSet<Entity>();

            foreach (var e in selection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e))
                {
                    // Stop on a building ends its directed fire
                    // (docs/Design/Combat_Pacing.md § Directed building fire).
                    if (_em.HasComponent<BuildingForcedTarget>(e) && issued.Add(e))
                        CommandRouter.IssueBuildingAttack(_em, e, Entity.Null, CommandSource.LocalPlayer);
                    continue;
                }

                Entity unit = e;
                if (!issued.Add(unit)) continue;

                CommandRouter.IssueStop(_em, unit, CommandSource.LocalPlayer);
            }
        }

        /// <summary>Hold Position — the Hold STANCE (docs/Design/Stances.md).</summary>
        public void IssueHoldPositionToSelection() => IssueStanceToSelection(UnitStanceMode.Hold);

        /// <summary>Put every owned unit of the selection in a stance.</summary>
        public void IssueStanceToSelection(UnitStanceMode stance)
            => IssueStanceToOwnedUnits(_em, CurrentSelection, stance, GameSettings.LocalPlayerFaction);

        /// <summary>
        /// Put every owned UNIT of a selection in a stance. Static for the
        /// actions panel, which holds no SelectionOrders instance — the same
        /// contract as <see cref="IssueMoveToOwnedUnits"/>. Emplaced engines are
        /// skipped: they always hold.
        /// </summary>
        public static void IssueStanceToOwnedUnits(EntityManager em, List<Entity> selection,
            UnitStanceMode stance, Faction faction)
        {
            if (selection == null || selection.Count == 0) return;
            var issued = new HashSet<Entity>();

            foreach (var e in selection)
            {
                if (!em.Exists(e)) continue;
                if (!em.HasComponent<UnitTag>(e) || em.HasComponent<BuildingTag>(e)) continue;
                if (!em.HasComponent<FactionTag>(e)
                    || em.GetComponentData<FactionTag>(e).Value != faction) continue;
                if (em.HasComponent<EmplacedEngineTag>(e)) continue;
                if (!issued.Add(e)) continue;

                CommandRouter.IssueStance(em, e, stance, CommandSource.LocalPlayer);
            }
        }

        public void SetRallyPoints(float3 position, Entity targetEntity)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<BuildingTag>(e)) continue;
                if (!CanHoldRally(_em, e)) continue;

                CommandRouter.SetRallyPoint(_em, e, position, targetEntity, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// A rally point only means something on a building that sends units
        /// out: one that TRAINS them (its SO's trains list; a Chapel's sect
        /// unit; a Fiendstone Keep's wings) or GARRISONS them (a wall). A
        /// house, mine or outpost has no rally (2026-10-16) — right-clicking
        /// with one selected used to create a rally on it, because the sim
        /// adds the component on demand.
        /// </summary>
        public static bool CanHoldRally(EntityManager em, Entity building)
        {
            if (em.HasComponent<RallyPoint>(building)) return true;   // authored at spawn
            if (em.HasComponent<ChapelTag>(building)) return true;
            if (em.HasComponent<KeepWings>(building)) return true;
            if (em.HasBuffer<WallGarrisonSlot>(building)) return true;

            string id = BuildingIds.Of(building, em);
            return !string.IsNullOrEmpty(id)
                && TechCatalog.TryGetBuilding(id, out var def)
                && def?.trains != null && def.trains.Length > 0;
        }

        public void IssueAttackCommands(Entity target)
        {
            // The Wall Rule (docs/Design/Combat_Pacing.md): against a wall
            // piece only the SIEGE units are ordered. The rest would have the
            // order dropped by the combat system anyway; leaving them alone
            // keeps whatever they were doing.
            bool wall = IsWallPiece(target);

            var issued = new HashSet<Entity>();
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;

                Entity unit = e;
                if (_em.HasComponent<BuildingTag>(e))
                {
                    // An emplacement PLATFORM is selected, but its ENGINE is
                    // the thing that shoots: forward the order to it.
                    unit = EmplacedEngineOf(e);
                    if (unit == Entity.Null) continue;
                }

                if (wall && !TheWaningBorder.Systems.Combat.CombatDamageHelper.DealsSiege(_em, unit)) continue;
                if (!issued.Add(unit)) continue; // Deduplicate leader commands

                CommandRouter.IssueAttack(_em, unit, target, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// Direct the fire of every owned shooting building in the selection
        /// at <paramref name="target"/> (docs/Design/Combat_Pacing.md §
        /// Directed building fire). Buildings the Wall Rule forbids are
        /// skipped. Returns how many buildings took the order.
        /// </summary>
        public int IssueBuildingAttackCommands(Entity target)
        {
            int n = 0;
            var faction = GameSettings.LocalPlayerFaction;
            foreach (var e in CurrentSelection)
            {
                if (!CommandRouter.CanDirectFire(_em, e, faction)) continue;
                if (!TheWaningBorder.Systems.Combat.BuildingCombatSystem.IsLegalForcedTarget(_em, e, target))
                    continue;
                CommandRouter.IssueBuildingAttack(_em, e, target, CommandSource.LocalPlayer);
                n++;
            }
            return n;
        }

        /// <summary>True when <paramref name="target"/> is a wall piece —
        /// hub, curtain, wall tower or gate — which only siege may damage.</summary>
        public bool IsWallPiece(Entity target)
            => target != Entity.Null && _em.Exists(target) && _em.HasComponent<WallTag>(target);

        /// <summary>The live engine standing on an owned emplacement
        /// platform, or Entity.Null.</summary>
        private Entity EmplacedEngineOf(Entity platform)
        {
            if (!_em.HasComponent<EmplacementTag>(platform) || !_em.HasComponent<EmplacementCrew>(platform))
                return Entity.Null;
            var engine = _em.GetComponentData<EmplacementCrew>(platform).Engine;
            return engine != Entity.Null && _em.Exists(engine) && IsOwnedByLocalPlayer(engine)
                ? engine : Entity.Null;
        }

        public void IssueHealCommands(Entity target)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!CanHeal(e)) continue;

                CommandRouter.IssueHeal(_em, e, target, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// True when the right-click target is a veilstone main node currently
        /// in the Active state — the only state that accepts Purification.
        /// </summary>
        public bool IsActiveBorderMainNode(Entity target)
        {
            if (target == Entity.Null || !_em.Exists(target)) return false;
            if (!_em.HasComponent<BorderMainNodeTag>(target)) return false;
            if (!_em.HasComponent<BorderNodeState>(target)) return false;
            return _em.GetComponentData<BorderNodeState>(target).State == NodeState.Active;
        }

        /// <summary>
        /// True when the right-click target is a well a Holy Scholar may
        /// PURIFY: Active, or lying in rubble (Destroyed) — consecrating a
        /// broken well before it rebuilds is legal (Curse_And_Shardroot.md,
        /// "Rubble (Destroyed) wells are purifiable"). Mirrors what
        /// PurificationRitualSystem and the AI accept; only a well another
        /// culture already claimed (Cleansed / Converted) is refused.
        /// </summary>
        public bool IsPurifiableBorderMainNode(Entity target)
        {
            if (target == Entity.Null || !_em.Exists(target)) return false;
            if (!_em.HasComponent<BorderMainNodeTag>(target)) return false;
            if (!_em.HasComponent<BorderNodeState>(target)) return false;
            var st = _em.GetComponentData<BorderNodeState>(target).State;
            return st == NodeState.Active || st == NodeState.Destroyed;
        }

        /// <summary>
        /// Issue IssuePurify on every scholar in the current selection
        /// targeting the same node. Non-scholars in the selection ignore the
        /// click (they don't fall back to Attack from here — the right-click
        /// handler treated the click as a Purify intent).
        /// </summary>
        public void IssuePurifyCommands(Entity node)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<ScholarTag>(e)) continue;

                CommandRouter.IssuePurify(_em, e, node, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// Issue IssueCorrupt on every Corruptor in the current selection.
        /// Same one-target semantics as IssuePurifyCommands.
        /// </summary>
        public void IssueCorruptCommands(Entity node)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<CorruptorTag>(e)) continue;

                CommandRouter.IssueCorrupt(_em, e, node, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// Issue IssueConvertNode on every acolyte in the current selection.
        /// Same one-target semantics as IssuePurifyCommands.
        /// </summary>
        public void IssueConvertNodeCommands(Entity node)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<AcolyteTag>(e)) continue;

                CommandRouter.IssueConvertNode(_em, e, node, CommandSource.LocalPlayer);
            }
        }

        public void IssueConvertCommands(Entity keep)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<WorkerTag>(e)) continue;

                CommandRouter.IssueConvert(_em, e, keep, CommandSource.LocalPlayer);
            }
        }

        public void IssueBuildCommands(Entity targetBuilding)
        {
            if (!_em.HasComponent<LocalTransform>(targetBuilding)) return;
            var buildPos = _em.GetComponentData<LocalTransform>(targetBuilding).Position;

            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<CanBuild>(e)) continue;

                CommandRouter.IssueBuild(_em, e, targetBuilding, "", buildPos,
                    CommandSource.LocalPlayer);
            }
        }

        public void IssueRepairCommands(Entity targetBuilding)
        {
            if (!_em.HasComponent<LocalTransform>(targetBuilding)) return;

            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<CanBuild>(e)) continue;

                CommandRouter.IssueRepair(_em, e, targetBuilding,
                    CommandSource.LocalPlayer);
            }
        }

        public bool IsBuildingDamaged(Entity building)
        {
            if (!_em.HasComponent<Health>(building)) return false;
            var hp = _em.GetComponentData<Health>(building);
            return hp.Value < hp.Max;
        }

        public void IssuePatrolCommands(float3 destination)
        {
            var issued = new HashSet<Entity>();
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;

                Entity unit = e;
                if (!issued.Add(unit)) continue;

                CommandRouter.IssuePatrol(_em, unit, destination, CommandSource.LocalPlayer);
            }
        }

        // AoE4-style formation move: layout, rank layering, cohesion gate,
        // group speed and the persistent virtual-leader group all live in
        // FormationMoveCommandHelper / FormationGroupSystem — the input
        // layer only collects the selection and picks the formation shape.
        /// <summary>
        /// Walk the selection onto a Shardroot pickup. The claim is instant
        /// in ShardrootCarrySystem: the first unit within
        /// ShardrootPickupRadius takes it that tick. A HERO in the selection
        /// (King Lexor) is sent straight onto the artifact with a plain move,
        /// so his destination IS the pickup — that is what gives him right of
        /// way over his own escort, who would otherwise reach it a step ahead
        /// from their formation slots and take it first. The rest of the
        /// selection formation-moves there as usual.
        /// </summary>
        public void IssuePickupMove(Entity pickup)
        {
            if (pickup == Entity.Null || !_em.Exists(pickup)
                || !_em.HasComponent<LocalTransform>(pickup)) return;
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            float3 at = _em.GetComponentData<LocalTransform>(pickup).Position;

            var escort = new List<Entity>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                if (_em.HasComponent<TheWaningBorder.Abilities.UniqueUnitTag>(units[i]))
                    CommandRouter.IssueMove(_em, units[i], at, CommandSource.LocalPlayer);
                else
                    escort.Add(units[i]);
            }
            if (escort.Count > 0)
                CommandRouter.IssueFormationMove(_em, escort, at, Shape, CommandSource.LocalPlayer);
        }

        public void IssueFormationMove(float3 clickWorld)
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            CommandRouter.IssueFormationMove(_em, units, clickWorld,
                Shape, CommandSource.LocalPlayer);
        }

        /// <summary>
        /// Right-drag (2026-10-15): a formation move centred on
        /// <paramref name="centre"/> that ARRIVES facing
        /// <paramref name="arriveFacing"/> (XZ, the drag direction) with an
        /// ordered frontage of <paramref name="columns"/> units per block row
        /// (2026-10-20, the drag length; 0 = the shape's default).
        /// </summary>
        public void IssueFormationMove(float3 centre, float3 arriveFacing, int columns = 0)
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            CommandRouter.IssueFormationMove(_em, units, centre,
                Shape, arriveFacing, columns, CommandSource.LocalPlayer);
        }

        /// <summary>
        /// Where the right-drag order would put each unit: the ghost preview.
        /// Runs the SAME FormationMoveCommandHelper.BuildPlan the order will
        /// (read-only), with the facing quantised exactly as the command
        /// quantises it, so the ghost is the layout the units will stand in —
        /// not a second copy of the arithmetic. Fills <paramref name="slots"/>
        /// and returns false when nothing in the selection can move.
        /// </summary>
        public bool PreviewFormationSlots(float3 centre, float3 arriveFacing, List<float3> slots,
            int columns = 0)
        {
            slots.Clear();
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return false;
            float3 facing = CommandRouter.UnpackFacing(CommandRouter.PackFacing(arriveFacing));
            if (!FormationMoveCommandHelper.BuildPlan(_em, units, centre, Shape, facing,
                    FormationMoveCommandHelper.ClampColumns(columns), out var plan))
                return false;
            slots.AddRange(plan.SlotWorld);
            return true;
        }

        /// <summary>True when the selection holds at least one owned unit
        /// (not a building) — i.e. a ground press can become a move.</summary>
        public bool HasOwnedMovableUnit()
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e)) continue;
                if (IsOwnedByLocalPlayer(e)) return true;
            }
            return false;
        }

        /// <summary>
        /// AoE4: selecting a new formation rearranges the units immediately,
        /// even when standing still — re-issue a formation move to the
        /// selection's own centroid.
        /// </summary>
        public void ReSlotSelectionInPlace()
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count < 2) return;

            float3 centroid = float3.zero;
            int n = 0;
            foreach (var e in units)
            {
                if (!_em.HasComponent<LocalTransform>(e)) continue;
                centroid += _em.GetComponentData<LocalTransform>(e).Position;
                n++;
            }
            if (n == 0) return;
            centroid /= n;

            CommandRouter.IssueFormationMove(_em, units, centroid,
                Shape, CommandSource.LocalPlayer);
        }

        /// <summary>Deduplicated owned, movable (non-building) selection.</summary>
        private List<Entity> CollectOwnedMovableSelection()
        {
            var units = new List<Entity>();
            var added = new HashSet<Entity>();
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!added.Add(e)) continue;
                units.Add(e);
            }
            return units;
        }

        /// <summary>Formation groups of queued steps; see QueuedCommand.Group.</summary>
        private static int _nextQueueGroup;

        /// <summary>
        /// Shift+right-click (2026-09-29): QUEUE what was clicked on each
        /// selected unit, behind whatever it is doing.
        ///   enemy            -> Attack (units that can attack; others move there)
        ///   own site         -> Build   (workers)
        ///   damaged building -> Repair  (workers)
        ///   wounded ally     -> Heal    (healers)
        ///   anything else    -> the armed mode (AttackMove / Patrol) or Move
        /// Moves, attack-moves and patrols carry one formation group for the
        /// whole selection, so the route is marched in formation; attacks and
        /// work orders are per unit. The first step starts at once when the
        /// unit is idle — in single player and multiplayer alike.
        /// </summary>
        public void QueueOrderForSelection(float3 clickWorld, Entity target, TargetType targetType,
                                           QueuedCommandType armed)
        {
            _nextQueueGroup = _nextQueueGroup % 32000 + 1;
            int group = _nextQueueGroup;
            byte shape = (byte)Shape;
            bool anyFull = false;

            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (CommandRouter.IsCommandQueueFull(_em, e)) { anyFull = true; continue; }

                var type = armed;
                var stepTarget = Entity.Null;
                float3 point = clickWorld;
                // The same capability tests the ordinary right-click uses.
                bool canAttack = _em.HasComponent<Damage>(e);
                bool canBuild = _em.HasComponent<CanBuild>(e);
                bool canHeal = CanHeal(e);

                if (targetType == TargetType.Enemy && canAttack)
                { type = QueuedCommandType.Attack; stepTarget = target; }
                else if (targetType == TargetType.FriendlyBuilding && canBuild
                         && _em.HasComponent<UnderConstruction>(target))
                { type = QueuedCommandType.Build; stepTarget = target; }
                else if (targetType == TargetType.FriendlyBuilding && canBuild && IsBuildingDamaged(target))
                { type = QueuedCommandType.Repair; stepTarget = target; }
                else if (targetType == TargetType.FriendlyUnit && canHeal && target != e
                         && _em.HasComponent<Health>(target)
                         && _em.GetComponentData<Health>(target).Value < _em.GetComponentData<Health>(target).Max)
                { type = QueuedCommandType.Heal; stepTarget = target; }

                if (stepTarget != Entity.Null && _em.HasComponent<LocalTransform>(stepTarget))
                    point = _em.GetComponentData<LocalTransform>(stepTarget).Position;

                bool grouped = stepTarget == Entity.Null;
                CommandRouter.IssueQueuedWaypoint(_em, e, type, point, stepTarget,
                    CommandSource.LocalPlayer, grouped ? shape : (byte)0, grouped ? group : 0);
            }
            if (anyFull)
                SimSignals.Notify(TheWaningBorder.Core.Localization.Loc.T(
                    "Command queue full — at most 15 queued actions"));
        }

        // Strips CommandQueueFrozen from every entity that carries it. Called
        // on the frame Shift transitions from held → released, so any queues
        // built up during the hold start draining on the next CommandQueueSystem tick.
        public void UnfreezeAllQueues()
        {
            var query = _frozenQuery.Get(_em, FrozenTypes);
            if (!query.IsEmpty)
                _em.RemoveComponent<CommandQueueFrozen>(query);
        }

        // Formation attack-move: same layout/travel machinery as the plain
        // formation move; members auto-engage en route (AoE4 behavior) and
        // detach from the group the moment they acquire a target.
        public void IssueAttackMoveFormation(float3 clickWorld)
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            CommandRouter.IssueFormationAttackMove(_em, units, clickWorld,
                Shape, CommandSource.LocalPlayer);
        }

        /// <summary>Right-drag while attack-move is armed: the formation
        /// attack-move, arriving facing <paramref name="arriveFacing"/>.</summary>
        public void IssueAttackMoveFormation(float3 centre, float3 arriveFacing, int columns = 0)
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            CommandRouter.IssueFormationAttackMove(_em, units, centre,
                Shape, arriveFacing, columns, CommandSource.LocalPlayer);
        }

        /// <summary>The widest any block of the selection could stand — its
        /// biggest type's head count (the layout's rank census), so the input
        /// layer can map a right-drag onto 1..this many columns. 0 when
        /// nothing in the selection can move.</summary>
        public int LargestFormationBlock()
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return 0;
            var census = new int[FormationMoveCommandHelper.RankCount];
            int best = 0;
            foreach (var e in units)
            {
                int r = math.clamp(FormationMoveCommandHelper.FormationRank(_em, e), 0, census.Length - 1);
                best = math.max(best, ++census[r]);
            }
            return best;
        }



        public enum TargetType { Ground, Enemy, FriendlyUnit, FriendlyBuilding, Resource, Pickup }

        public TargetType DetermineTargetType(Entity target)
        {
            if (target == Entity.Null || !_em.Exists(target))
                return TargetType.Ground;

            // A Shardroot pickup on the ground. Checked before the faction
            // test: the pickup carries FactionTag = Border (neutral until
            // claimed), which the hostility test read as an ENEMY with no
            // health -- so right-clicking the artifact produced an attack
            // order that went nowhere (2026-09-15).
            if (_em.HasComponent<ShardrootPickupTag>(target))
                return TargetType.Pickup;

            // Check if it's a resource node (iron mine, veilstone node, or veilsteel node)
            if (_em.HasComponent<IronMineTag>(target))
                return TargetType.Resource;
            if (_em.HasComponent<VeilstoneOutcroppingTag>(target))
                return TargetType.Resource;
            if (_em.HasComponent<VeilsteelDepositTag>(target))
                return TargetType.Resource;

            // Check faction
            if (!_em.HasComponent<FactionTag>(target))
                return TargetType.Ground;

            var targetFaction = _em.GetComponentData<FactionTag>(target).Value;

            // Allies read as FRIENDLY, not enemy: right-clicking a teammate
            // must offer heal / support intent, never an attack order.
            // docs/Design/Teams.md
            if (Alliances.AreHostile(GameSettings.LocalPlayerFaction, targetFaction))
                return TargetType.Enemy;

            if (_em.HasComponent<UnitTag>(target))
                return TargetType.FriendlyUnit;

            if (_em.HasComponent<BuildingTag>(target))
                return TargetType.FriendlyBuilding;

            return TargetType.Ground;
        }



        public struct UnitCapabilities
        {
            public bool CanAttack;
            public bool CanGather;
            public bool CanHeal;
            public bool CanBuildRepair;
            public bool CanPurify;
            /// <summary>Feraldis Corruptor selected — right-click a well to crack it open.</summary>
            public bool CanCorrupt;
            public bool CanConvertNode;
            /// <summary>A siege-damage attacker is selected (a unit, or the
            /// engine on a selected emplacement) — the only thing that may
            /// attack a wall piece. docs/Design/Combat_Pacing.md § The Wall Rule</summary>
            public bool CanAttackWalls;
            /// <summary>An owned shooting building is selected: right-click
            /// an enemy directs its fire (§ Directed building fire).</summary>
            public bool CanDirectBuildingFire;
            /// <summary>At least one owned building in the selection fires
            /// siege, so it may be directed at a wall.</summary>
            public bool CanDirectSiegeFire;
        }

        public UnitCapabilities DetermineCapabilities()
        {
            var caps = new UnitCapabilities();

            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;

                // Can attack if it has a Damage component
                if (_em.HasComponent<Damage>(e))
                    caps.CanAttack = true;

                // Can gather if is a worker
                if (_em.HasComponent<WorkerTag>(e))
                    caps.CanGather = true;

                // Can heal if has heal capability (Litharch, etc.)
                if (CanHeal(e))
                    caps.CanHeal = true;

                // Can build/repair if is a worker
                if (_em.HasComponent<CanBuild>(e))
                    caps.CanBuildRepair = true;

                // Scholar can channel Purification on Active veilstone main nodes.
                if (_em.HasComponent<ScholarTag>(e))
                    caps.CanPurify = true;

                // Acolyte can channel Conversion on Active veilstone main nodes.
                if (_em.HasComponent<AcolyteTag>(e))
                    caps.CanConvertNode = true;

                // Feraldis Corruptor channels corruption on a living well.
                if (_em.HasComponent<CorruptorTag>(e))
                    caps.CanCorrupt = true;

                if (_em.HasComponent<BuildingTag>(e))
                {
                    if (_em.HasComponent<BuildingRangedAttack>(e))
                    {
                        caps.CanDirectBuildingFire = true;
                        if (TheWaningBorder.Systems.Combat.BuildingCombatSystem.MainDamageType(_em, e) == DamageType.Siege)
                            caps.CanDirectSiegeFire = true;
                    }
                    // An emplacement platform: its engine is the attacker.
                    var engine = EmplacedEngineOf(e);
                    if (engine != Entity.Null)
                    {
                        caps.CanAttack = true;
                        if (TheWaningBorder.Systems.Combat.CombatDamageHelper.DealsSiege(_em, engine))
                            caps.CanAttackWalls = true;
                    }
                }
                else if (_em.HasComponent<Damage>(e)
                         && TheWaningBorder.Systems.Combat.CombatDamageHelper.DealsSiege(_em, e))
                {
                    caps.CanAttackWalls = true;
                }
            }

            return caps;
        }

        private bool CanHeal(Entity e)
        {
            // Check for healer tag or component
            // Litharch units can heal
            return _em.HasComponent<LitharchTag>(e);
        }

        /// <summary>
        /// Returns true if the entity belongs to the local player's faction.
        /// </summary>
        public bool IsOwnedByLocalPlayer(Entity e)
        {
            if (!_em.HasComponent<FactionTag>(e)) return false;
            return _em.GetComponentData<FactionTag>(e).Value == GameSettings.LocalPlayerFaction;
        }

        /// <summary>
        /// Returns true if at least one selected entity belongs to the local player.
        /// </summary>
        public bool HasAnyOwnedEntity()
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (IsOwnedByLocalPlayer(e)) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if all owned entities in the selection are buildings.
        /// Used to determine if rally point setting should be triggered.
        /// </summary>
        public bool HasOnlyOwnedBuildings()
        {
            bool foundOwned = false;
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                foundOwned = true;
                if (!_em.HasComponent<BuildingTag>(e))
                    return false;
            }
            return foundOwned;
        }

    }
}
