// ShardrootTrialDriver.cs
// Runs the Shardroot trial scenario's timeline. The scenario is a study of
// the artifact loop in docs/Design/Curse_And_Shardroot.md 3.1, so the
// driver's only job is to keep the pressure that makes each phase happen:
//
//   * a GRACE period in which the Red army holds, so the player can start
//     the 20 s attunement in peace or choose to fight first;
//   * the RELEASE, when every Red unit attack-moves onto the artifact --
//     the race for it, and the first chance to see a carrier die and drop it;
//   * REINFORCEMENT: when the Red army standing on the field is down to
//     ReinforceAtFraction of what its last wave put there, a fresh army
//     spawns at the Red start and marches -- so the pressure resumes the
//     moment the player has nearly won, a Shardbound king can be worn down
//     to his detonation, and an enshrining Temple can be attacked.
//
// Notices about the artifact itself (picked up, dropped, hero awakened,
// enshrined) come from the Shardroot systems, not from here.

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    public sealed class ShardrootTrialDriver : MonoBehaviour
    {
        public float GraceSeconds = 60f;
        /// <summary>A new army spawns when live Red units fall to this
        /// fraction of the count the last wave established.</summary>
        public float ReinforceAtFraction = 0.10f;
        public int MaxWaves = 6;

        private float3 _artifact;
        private float3 _redStart;
        private bool _released;
        private int _waveBaseline;    // Red units alive right after the last spawn
        private int _wavesSent;

        public void Configure(float3 artifact, float3 redStart)
        {
            _artifact = artifact;
            _redStart = redStart;
            _released = false;
            _waveBaseline = 0;
            _wavesSent = 0;
        }

        /// <summary>RecenterScenario moves entities, not this driver's
        /// authored points; it calls this with the same offset.</summary>
        public void Shift(float dx, float dz)
        {
            _artifact.x += dx; _artifact.z += dz;
            _redStart.x += dx; _redStart.z += dz;
            _artifact = TerrainUtility.SnapToTerrain(_artifact);
            _redStart = TerrainUtility.SnapToTerrain(_redStart);
        }

        void Update()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            float now = SimClock.Now;

            if (!_released)
            {
                if (now < GraceSeconds) return;
                _released = true;
                _waveBaseline = CountRed(em);
                SendRedAt(em, _artifact);
                SimSignals.Notify("The Red army marches on the Shardroot.");
                TWBLog.Log($"[ShardrootTrial] grace over -- Red ({_waveBaseline}) released at the artifact");
                return;
            }

            if (_wavesSent >= MaxWaves) return;
            int alive = CountRed(em);
            if (alive > _waveBaseline * ReinforceAtFraction) return;
            _wavesSent++;

            // A fresh army at the Red start: the same five battalions the
            // first one had (ScenarioSetup's 5 x 4 Alanthor block), then the
            // same order.
            const float spacing = 12f;
            for (int col = 0; col < 3; col++)
                ScenarioSetup.SpawnBattalion(em, "Alanthor_Swordsman",
                    _redStart + new float3((col - 1) * spacing, 0f, 0f), Faction.Red);
            for (int col = 0; col < 2; col++)
                ScenarioSetup.SpawnBattalion(em, "Alanthor_Crossbowman",
                    _redStart + new float3((col - 0.5f) * spacing, 0f, -10f), Faction.Red);
            _waveBaseline = CountRed(em);
            SendRedAt(em, _artifact);
            SimSignals.Notify($"Red reinforcements: army {_wavesSent + 1} marches.");
            TWBLog.Log($"[ShardrootTrial] Red down to {alive}; army {_wavesSent + 1} ({_waveBaseline} strong) sent");
        }

        private static int CountRed(EntityManager em)
            => ScenarioSetup.CountUnits(em, Faction.Red);

        private static void SendRedAt(EntityManager em, float3 target)
            => ScenarioSetup.AttackMoveAllBattalions(em, Faction.Red, target);
    }
}
