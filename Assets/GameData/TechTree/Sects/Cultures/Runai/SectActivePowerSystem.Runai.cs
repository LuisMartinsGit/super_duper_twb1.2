// Effect bodies for the Runai canon power kinds — today, the Sect of
// Witness's three (docs/Design/Sects.md §"Sect of Witness"). The dispatch
// switch lives in SectActivePowerSystem.cs; this file is only the bodies,
// split by CLUSTER exactly as SectActivePowerSystem.Alanthor.cs is.
//
// Determinism note, because two of these three could easily have been written
// the wrong way: NOTHING here asks FogOfWarManager what is visible. The fog is
// a MonoBehaviour stamped on a render-frame cadence, so a lockstep peer
// running at a different frame rate would answer differently and the two would
// fork. "What can I see" is instead computed from the sim itself — every
// entity of the caster's faction that carries LineOfSight, spy eyes included —
// which is the same set the fog is built from and is identical on every peer
// at the same tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    public static partial class SectActivePowerHelper
    {
        #region Cached queries

        // Never CreateEntityQuery on a repeating path — Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_EyeSources =
        {
            ComponentType.ReadOnly<LineOfSight>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_EyeSources;

        #endregion

        // ── Spy Network ─────────────────────────────────────────────────

        /// <summary>
        /// Spy Network. Turns the ONE enemy unit under the cast point into an
        /// unwitting eye. It keeps fighting for its own side — nothing about it
        /// changes except that the Witness player now sees what it sees.
        /// </summary>
        private static void ApplySpyNetwork(EntityManager em, Faction faction,
            float3 center, float radius, float duration, byte level)
        {
            float r2 = radius * radius;
            float life = duration > 0f ? duration : SectEffectDuration.Permanent;

            var query = QC_UnitTagLocalTransformFactionTag.Get(em, QT_UnitTagLocalTransformFactionTag);
            using var entities = query.ToEntityArray(Allocator.Temp);

            Entity best = Entity.Null;
            float bestD2 = float.MaxValue;

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var owner = em.GetComponentData<FactionTag>(e).Value;
                if (!Alliances.AreHostile(faction, owner)) continue;
                if (em.HasComponent<WitnessSpy>(e)) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2 || d2 >= bestD2) continue;

                best = e;
                bestD2 = d2;
            }

            // Single Target genuinely means one: the NEAREST candidate, not
            // every unit that happens to fall inside the pick tolerance.
            if (best != Entity.Null) PlantSpy(em, faction, best, life, level);
        }

        /// <summary>
        /// Make one enemy unit a spy for <paramref name="owner"/> and give it
        /// an eye. Public because the cascade recruits the same way the cast
        /// does — a spy made by contagion is not a lesser spy.
        /// </summary>
        public static void PlantSpy(EntityManager em, Faction owner, Entity unit,
            float life, byte level)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (em.HasComponent<WitnessSpy>(unit)) return;

            float sight = em.HasComponent<LineOfSight>(unit)
                ? em.GetComponentData<LineOfSight>(unit).Radius
                : 0f;
            float3 at = em.HasComponent<LocalTransform>(unit)
                ? em.GetComponentData<LocalTransform>(unit).Position
                : float3.zero;

            // FactionTag + LocalTransform + LineOfSight is the trio the fog
            // stamps vision from, so the owner starts seeing through this unit
            // on the next fog frame with no special support anywhere.
            var eye = em.CreateEntity(
                typeof(WitnessEye), typeof(LocalTransform),
                typeof(FactionTag), typeof(LineOfSight));
            em.SetComponentData(eye, new WitnessEye { Host = unit });
            em.SetComponentData(eye, LocalTransform.FromPositionRotationScale(
                at, quaternion.identity, 1f));
            em.SetComponentData(eye, new FactionTag { Value = owner });
            em.SetComponentData(eye, new LineOfSight { Radius = sight });

            em.AddComponentData(unit, new WitnessSpy
            {
                Owner         = owner,
                TimeRemaining = life,
                Eye           = eye,
                Level         = level,
            });
        }

        // ── Blinding Glare ──────────────────────────────────────────────

        /// <summary>
        /// Blinding Glare. Enemies in the circle lose all vision;
        /// <paramref name="lockAbilities"/> (Lv III) also stops them starting
        /// one. Their own sight radius is stored so it can be handed back —
        /// zeroing it without recording it would permanently blind anything
        /// caught twice.
        /// </summary>
        private static void ApplyBlind(EntityManager em, Faction faction,
            float3 center, float radius, float duration, bool lockAbilities)
        {
            float r2 = radius * radius;
            var query = QC_UnitTagLocalTransformFactionTag.Get(em, QT_UnitTagLocalTransformFactionTag);
            using var entities = query.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (!Alliances.AreHostile(faction, em.GetComponentData<FactionTag>(e).Value)) continue;
                if (!em.HasComponent<LineOfSight>(e)) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                var los = em.GetComponentData<LineOfSight>(e);

                if (em.HasComponent<SectBlinded>(e))
                {
                    // Re-cast REFRESHES rather than stacking, and must not
                    // re-record the already-zeroed radius as the original.
                    var held = em.GetComponentData<SectBlinded>(e);
                    held.TimeRemaining = duration;
                    held.LocksAbilities = (byte)(lockAbilities ? 1 : held.LocksAbilities);
                    ecb.SetComponent(e, held);
                    continue;
                }

                ecb.AddComponent(e, new SectBlinded
                {
                    TimeRemaining  = duration,
                    OriginalRadius = los.Radius,
                    LocksAbilities = (byte)(lockAbilities ? 1 : 0),
                });
                ecb.SetComponent(e, new LineOfSight { Radius = 0f });
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        // ── Nowhere to Hide ─────────────────────────────────────────────

        /// <summary>
        /// Nowhere to Hide. Damages every hostile unit the caster can
        /// currently SEE, anywhere on the map — its reach is not a radius but
        /// however much of the enemy army the player has managed to reveal,
        /// which is what makes it a multiplier on Spy Network rather than a
        /// damage spell.
        ///
        /// <paramref name="hitBuildings"/> (Lv II+) extends it to structures.
        /// </summary>
        private static void ApplyRevealedStrike(EntityManager em, Faction faction,
            int dmg, bool hitBuildings)
        {
            // Every sight source the caster's side owns: units, buildings, and
            // the invisible eyes riding its spies. Deliberately NOT the fog
            // texture — see the determinism note at the top of this file.
            var srcQ = QC_EyeSources.Get(em, QT_EyeSources);
            using var srcEnts = srcQ.ToEntityArray(Allocator.Temp);
            var eyes = new NativeList<float4>(Allocator.Temp);   // xyz = pos, w = r^2

            for (int i = 0; i < srcEnts.Length; i++)
            {
                var e = srcEnts[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                float r = em.GetComponentData<LineOfSight>(e).Radius;
                if (r <= 0f) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                eyes.Add(new float4(p.x, p.y, p.z, r * r));
            }

            if (eyes.Length > 0)
            {
                var uq = QC_UnitTagLocalTransformFactionTagHealth.Get(
                    em, QT_UnitTagLocalTransformFactionTagHealth);
                using var units = uq.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < units.Length; i++)
                {
                    var e = units[i];
                    if (!Alliances.AreHostile(faction, em.GetComponentData<FactionTag>(e).Value)) continue;
                    if (!Seen(eyes, em.GetComponentData<LocalTransform>(e).Position)) continue;

                    var hp = em.GetComponentData<Health>(e);
                    hp.Value = math.max(0, hp.Value - dmg);
                    em.SetComponentData(e, hp);
                }

                if (hitBuildings)
                {
                    var bq = QC_BuildingTagLocalTransformFactionTagHealth.Get(
                        em, QT_BuildingTagLocalTransformFactionTagHealth);
                    using var buildings = bq.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < buildings.Length; i++)
                    {
                        var e = buildings[i];
                        var fac = em.GetComponentData<FactionTag>(e).Value;
                        // The same two exemptions every area power carries:
                        // walls answer only to siege (Combat_Pacing.md), and
                        // Border structures are verb objectives, not targets.
                        if (fac == faction || fac == Faction.Border) continue;
                        if (em.HasComponent<WallTag>(e)) continue;
                        if (!Seen(eyes, em.GetComponentData<LocalTransform>(e).Position)) continue;

                        var hp = em.GetComponentData<Health>(e);
                        hp.Value = math.max(0, hp.Value - dmg);
                        em.SetComponentData(e, hp);
                    }
                }
            }

            eyes.Dispose();
        }

        private static bool Seen(in NativeList<float4> eyes, float3 p)
        {
            for (int i = 0; i < eyes.Length; i++)
            {
                var e = eyes[i];
                float dx = e.x - p.x, dz = e.z - p.z;
                if (dx * dx + dz * dz <= e.w) return true;
            }
            return false;
        }
    }
}
