// DisableNetcodeBootstrap.cs
// Purpose: stop com.unity.netcode from hijacking the default ECS world.
//
// The package is in Packages/manifest.json but the project's multiplayer is
// implemented as its own lockstep layer (Assets/Scripts/Multiplayer/Lockstep).
// Without an explicit override, Unity Entities discovers
// Unity.NetCode.ClientServerBootstrap (an ICustomBootstrap), runs it on game
// boot, and installs NetcodeClientRateManager on the root system groups of
// the default world. With no real netcode client, that rate manager computes
// a negative delta time every frame, ShouldGroupUpdate returns false, and
// the entire SimulationSystemGroup is skipped — MovementSystem,
// TargetingSystem, VeilstingerCombatSystem, ProjectileSystem, etc., never
// tick. The console fills with "Delta time was negative. To avoid undefined
// behaviour the frame is skipped" and combat appears completely broken.
//
// Unity Entities' bootstrap selection (DefaultWorldInitialization.cs
// `CreateBootStrap`) prefers the MOST-DERIVED ICustomBootstrap class, so
// extending ClientServerBootstrap here automatically supersedes the default.
// We override Initialize() to build a vanilla WorldFlags.Game world the same
// way Unity would in the absence of NetCode, and return true so the base
// initialization path is bypassed entirely.

using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;

namespace TheWaningBorder.Bootstrap
{
    public sealed class DisableNetcodeBootstrap : ClientServerBootstrap
    {
        public override bool Initialize(string defaultWorldName)
        {
            // Vanilla world creation mirroring DefaultWorldInitialization.Initialize
            // lines 143-152, minus the bootstrap-discovery recursion that would
            // re-pick this very class. `World` is fully qualified because the
            // project has its own TheWaningBorder.World namespace that
            // shadows Unity.Entities.World when both are in scope.
            var world = new Unity.Entities.World(defaultWorldName, WorldFlags.Game);
            Unity.Entities.World.DefaultGameObjectInjectionWorld = world;

            // NOTE: GetAllSystemTypeIndices returns TypeManager's CACHED, persistent
            // NativeList (TypeManager.GetSystemTypeIndices -> s_SystemFilterTypeMap),
            // NOT a fresh copy the caller owns. TypeManager disposes it at shutdown.
            // Unity's own DefaultWorldInitialization.Initialize passes it straight in
            // and never disposes it. Disposing it here frees the cache entry, so the
            // NEXT world initialisation (launching a second scenario/skirmish, or
            // return-to-menu then start again) gets back a deallocated list and throws
            // ObjectDisposedException in AddSystemsToRootLevelSystemGroups. Do NOT dispose.
            var systemIndices = DefaultWorldInitialization.GetAllSystemTypeIndices(
                WorldSystemFilterFlags.Default);
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systemIndices);

            DisableUnusedPhysics(world);

            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
            return true;
        }

        /// <summary>
        /// com.unity.physics is in the manifest but NO game code uses it
        /// (audited 2026-09-27: no Unity.Physics type, collider, body, query
        /// or PhysicsWorldSingleton read anywhere under Assets/ — raycasts
        /// are all UnityEngine.Physics, a different engine). Its systems still
        /// ran every frame inside FixedStepSimulationSystemGroup, whose
        /// catch-up rate manager runs several substeps on a slow frame — the
        /// late-game logs show both groups in thousands of slow frames,
        /// feeding the spiral they were catching up from.
        ///
        /// PhysicsSystemGroup is always disabled. The whole fixed-step group is
        /// disabled too, but ONLY when every direct child is one we know is
        /// idle here (physics, the fixed-step ECB pair nothing records into,
        /// physics' temporal-coherence injector): a system someone adds to
        /// that group later keeps the group running, and the log says so.
        /// The package itself stays installed (no package changes).
        /// </summary>
        private static void DisableUnusedPhysics(Unity.Entities.World world)
        {
            var physicsGroup = world.GetExistingSystemManaged<Unity.Physics.Systems.PhysicsSystemGroup>();
            if (physicsGroup != null) physicsGroup.Enabled = false;

            var fixedGroup = world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>();
            if (fixedGroup == null) return;

            string blocker = null;
            using (var children = fixedGroup.GetAllSystems(Allocator.Temp))
            {
                for (int i = 0; i < children.Length; i++)
                {
                    string name = world.Unmanaged.ResolveSystemStateRef(children[i]).DebugName.ToString();
                    if (name.EndsWith("PhysicsSystemGroup")
                        || name.EndsWith("BeginFixedStepSimulationEntityCommandBufferSystem")
                        || name.EndsWith("EndFixedStepSimulationEntityCommandBufferSystem")
                        || name.EndsWith("InjectTemporalCoherenceDataSystem"))
                        continue;
                    blocker = name;
                    break;
                }
            }

            if (blocker == null)
            {
                fixedGroup.Enabled = false;
                UnityEngine.Debug.Log("[Bootstrap] Unity Physics unused: PhysicsSystemGroup and " +
                                      "FixedStepSimulationSystemGroup disabled.");
            }
            else
            {
                UnityEngine.Debug.Log("[Bootstrap] Unity Physics unused: PhysicsSystemGroup disabled; " +
                                      $"FixedStepSimulationSystemGroup kept running for '{blocker}'.");
            }
        }
    }
}
