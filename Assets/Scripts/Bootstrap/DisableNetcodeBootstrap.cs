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

            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
            return true;
        }
    }
}
