// PresentationState.cs
// The few facts about the screen that non-UI code is allowed to know.
//
// WHY THIS EXISTS
// Almost nothing outside the UI should care what is on screen, and a
// deterministic simulation must not branch on it at all -- what a local player
// can see differs per machine, and a tick that reads it is a tick that can
// disagree between peers.
//
// The exception is the match START. Lockstep must not run tick 0 while the
// loading overlay is still up: the AI would act, income would accrue and the
// curse would spread through seconds the player never saw, and the clock they
// are shown would not match the game they played. So the gate has to know one
// bit -- "is the overlay gone yet".
//
// It used to get that bit by calling TheWaningBorder.UI.Menus.LoadingScreen
// .IsVisible straight from LockstepManager, which put a presentation type in
// the multiplayer layer's dependencies. Now the overlay PUBLISHES the bit here
// and lockstep reads a plain bool from Core. The arrow points from UI down,
// which is the direction that lets the two ever live in separate assemblies.
//
// Keep this tiny. Every field added here is a fact the rest of the game is now
// allowed to depend on, and the reason the old direct call looked reasonable
// too. One bit, published by one owner, read by one gate.

namespace TheWaningBorder.Core
{
    /// <summary>Screen facts published by the presentation layer.</summary>
    public static class PresentationState
    {
        /// <summary>
        /// True while any part of the loading overlay is still on screen, fade
        /// included. Written ONLY by LoadingScreen; read by the lockstep
        /// world-ready gate so tick 0 waits for the player to be looking at
        /// the match.
        ///
        /// Defaults to false so a build with no overlay at all (a test scene, a
        /// headless host) is never gated on something that will never appear.
        /// </summary>
        public static bool LoadingOverlayVisible;

        /// <summary>
        /// True while the player is dragging a building ghost around. Written
        /// ONLY by BuilderCommandPanel; read by the few in-world displays that
        /// only appear during placement (the GathererHut coverage ring).
        ///
        /// Those displays live in GameData beside their building, so without
        /// this the content layer would name a UI panel directly.
        /// </summary>
        public static bool PlacingBuilding;

        /// <summary>
        /// The gameplay camera. Published by GameCamera; read by the few
        /// in-world visuals that need to frame themselves against it (the Hut
        /// evolution cinematic orbits it).
        /// </summary>
        public static UnityEngine.Camera MainCamera;

        /// <summary>
        /// Set true to take player camera control away for a cinematic, false
        /// to hand it back. CameraController watches this and stops reading
        /// input while it is set.
        ///
        /// A REQUEST, not a handle. The Hut cinematic used to fetch
        /// GameCamera.Controller and flip .enabled on it directly, which meant
        /// a building's visual held the input layer's MonoBehaviour and had to
        /// remember to switch it back on. Now it states what it wants and the
        /// camera layer decides how to honour it.
        /// </summary>
        public static bool CameraControlSuspended;

        /// <summary>
        /// What the local player has selected. Published by SelectionSystem;
        /// read by in-world displays that only draw for a selected building
        /// (the GathererHut coverage ring). Null before the first selection.
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<Unity.Entities.Entity> Selection;
    }
}
