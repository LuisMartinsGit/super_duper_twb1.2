// TutorialDirector.cs
// The tutorial scenario's coach: a chaptered CHECKLIST that watches real game
// state and ticks itself off as the player plays.
//
// It spans the WHOLE game, first camera pan to first purified well:
//   1  Controls
//   2  Territory and economy
//   3  Combat
//   4  Culture — special building, age-up, Temple
//   5  Religion — sects and powers
//   6  The curse — why it wakes, what it costs, how to break it
//   7  The wells — the verb, and how the match is won
//
// IT NEVER GATES THE PLAYER. This is the design rule the rest of the file
// exists to serve:
//   * EVERY step is evaluated on EVERY tick, not just the one being shown. Do
//     things in whatever order you like — raise the Temple in chapter 2, adopt
//     a sect before you have ever fought — and the checklist simply ticks the
//     boxes as you go.
//   * Conditions are ABSOLUTE, measured against the state of the match rather
//     than "since this instruction appeared". Work you did before being asked
//     counts; nothing has to be repeated to satisfy the coach. A NOTE (a step
//     that only explains something) is the one exception and has to be — see
//     Step.NoteSeconds.
//   * Completion LATCHES. Transient conditions (a unit selected, a power
//     recharging) stay ticked once seen, so a box never un-ticks.
//   * The panel shows the first UNFINISHED step as a suggestion. That is all
//     it is — a suggestion. Skip walks forward and pays out the grants for
//     everything it passes, so racing ahead is never punished with an empty
//     bank.
// The coach's frame is not a raycast target either, so it cannot eat a click
// meant for the map.
//
// The tutorial is NOT a bespoke scene. GameSettings.TutorialActive makes
// GameUIManager mount this one component on an otherwise ordinary Age 0 match
// against a single relaxed AI, on whatever map ships. That is the point — a
// tutorial built on a mock-up teaches a mock-up, and it rots the moment the
// real opening changes. Every step reads the same state the game itself reads
// (TerritoryOwnership, Target, TempleLevel, TempleChapelSlot, SmallNodeTag,
// BorderNodeState), so the only way to fail is to change the game, which is
// exactly when the tutorial SHOULD break.
//
// IT DID BREAK, and this is what that looked like (corrected 2026-09-07).
// Chapter 2 taught worker gathering — send a worker to an outcropping, then
// put three on veilstone and three on iron. Worker gathering was DELETED with
// the territory economy (Regions.md §4: four systems, two commands, the AI
// allocator and the input paths), leaving `MinerState` behind as a component
// nothing writes. So the chapter still COMPILED and still read real state; it
// just read state that had stopped moving. "Split your economy" could never
// tick — a permanent wall a first-time player could only Skip past — while
// "Mine veilstone" ticked INSTANTLY off passive territory income, teaching a
// verb that no longer exists. A dead component is the one failure mode
// "read what the game reads" does not catch on its own, so when a mechanic is
// deleted, grep this file for what fed it.
//
// Four scripted helps, all tutorial-only and all deliberate:
//   * GRANTS — a chapter that teaches sects cannot wait out the twenty minutes
//     of economy it would normally take to afford a Temple. Each step's
//     package is paid when it becomes the suggestion or when the player
//     finishes it early, whichever comes first, and is announced in the
//     notification line so nobody mistakes it for their own economy.
//   * The TEMPLE SHORTCUT — whenever a Temple upgrade is running, its target
//     is rewritten to level 4, so one click on the real upgrade button reaches
//     the top tier. Not tied to its step: do it whenever you like. The upgrade
//     still runs through TempleUpgradeSystem, so the era bump, the Religion
//     Point award and the sect lever sync happen exactly as in a real match.
//   * The SCRIPTED CORRUPTION — the curse chapter cannot wait out the two
//     minutes of TENURE on contested veilstone ground that wakes a pocket for
//     real (TerritoryCorruptionSystem), and home ground is corruption-immune
//     under the Fortress's hearth by design (Curse_And_Shardroot.md §2.7
//     amendment). So the chapter queues a real PendingCorruption — the same
//     buffer TerritoryCorruptionSystem writes — 45-55 m out, beyond the hearth
//     so BlightPocketSystem cannot starve it before the player fights it. In
//     fiction it is a ritual that failed somewhere on the map, which is the
//     honest explanation: a broken channel is what wakes the curse.
//     (The trigger it used to name, depletion inside VeilstoneMiningSystem,
//     went with worker gathering — tenure is what wakes pockets now.)
//   * CREEP SPEED-UP — VeilFieldSystem.TutorialCreepMultiplier, raised while
//     the curse chapter is live so the crust visibly advances inside the step
//     rather than after a 190-320 s dormant window. Restored afterwards.
//
// Steps are checked at 4 Hz, never per frame, and finished ones stop being
// evaluated. Apart from the four helps above, nothing here writes to the
// simulation.

using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.World.Regions;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class TutorialDirector : MonoBehaviour
    {
        private const float CheckInterval = 0.25f;
        private const float PanelWidth = 900f;
        /// <summary>Grace period after a step completes, so the player reads
        /// the "done" state before the next instruction replaces it.</summary>
        private const float AdvanceDelay = 1.6f;

        /// <summary>How much faster the tendril heartbeat runs during the
        /// curse chapter. 4x turns a 190-320 s dormant window into 48-80 s.</summary>
        private const float CurseChapterCreepSpeed = 4f;

        /// <summary>Closest the scripted corruption may land to the Fortress.
        /// Must clear HallHearthRadius (34 m) or the hearth starves the pocket
        /// at 20 dps before the player can bring an army to it. It must ALSO
        /// land off this player's territory — see ScriptedCorruptionSeat.</summary>
        private const float ScriptedCorruptionDistance = 50f;

        /// <summary>How far out ScriptedCorruptionSeat will look for unowned
        /// ground, and in what steps. Territories average ~90 m across
        /// (Regions.md §5), so a couple of territory-widths is plenty; beyond
        /// that the node is too far to march an army to inside the chapter.</summary>
        private const float ScriptedCorruptionSearchMax = 170f;
        private const float ScriptedCorruptionSearchStep = 15f;
        private const int ScriptedCorruptionBearings = 8;

        private delegate bool Check(TutorialDirector t, EntityManager em, Faction f);
        private delegate void Setup(TutorialDirector t, EntityManager em, Faction f);

        private sealed class Step
        {
            public string Chapter;
            public string Title;
            public string Body;
            /// <summary>Resources paid out for this step's lesson.</summary>
            public Cost Grant;
            public string GrantLabel;
            /// <summary>Scripted setup that only makes sense once the player is
            /// actually looking at this step (the corruption, the creep
            /// speed-up). Runs when the step becomes the suggestion.</summary>
            public Setup OnSuggest;
            /// <summary>Reads sim state. ABSOLUTE — "is this true of the match
            /// now", not "did it happen since the instruction appeared" — so
            /// work done ahead of the coach still counts. Latched by the
            /// caller, so a transient truth is enough.
            ///
            /// Null on a NOTE — see <see cref="NoteSeconds"/>.</summary>
            public Check Done;

            /// <summary>
            /// Marks this step a NOTE: there is nothing to do, and it ticks
            /// itself off after this many seconds ON SCREEN.
            ///
            /// This is the one deliberate exception to the ABSOLUTE rule
            /// above, and it exists because that rule has a hole for steps
            /// that only explain. A note's condition would have to be
            /// something already true of the match — "you hold a territory",
            /// "the curse holds ground" — and ScanAll ticks every true step
            /// BEFORE the panel picks what to show, so such a step is marked
            /// done and skipped without ever being displayed. The player would
            /// never read the one thing it exists to say.
            ///
            /// Dwelling is safe here precisely because a note asks for no
            /// work: there is nothing a player could have done ahead of the
            /// coach for it to fail to credit.
            /// </summary>
            public float NoteSeconds;
        }

        private const string Ch1 = "1. Controls";
        private const string Ch2 = "2. Territory & economy";
        private const string Ch3 = "3. Combat";
        private const string Ch4 = "4. Culture";
        private const string Ch5 = "5. Religion";
        private const string Ch6 = "6. The curse";
        private const string Ch7 = "7. The wells";

        private static readonly Step[] Steps =
        {
            // ── 1. Controls ────────────────────────────────────────────────
            new Step
            {
                Chapter = Ch1,
                Title = "Look around",
                Body = "Push the mouse to any <b>screen edge</b> to pan, or use the "
                     + "<b>arrow keys</b>. Hold the <b>middle mouse button</b> to drag the "
                     + "view, or click the minimap to jump.\n"
                     + "Find your <b>Fortress</b> — the capital your warband starts around. "
                     + "It holds the ground it stands on, and that ground is your first "
                     + "<b>territory</b>.",
                Done = (t, em, f) => t.CameraTravelled() > 40f,
            },
            new Step
            {
                Chapter = Ch1,
                Title = "Zoom",
                // Q/E rotation and R/F tilt are DELIBERATELY disabled — the RTS
                // camera holds a fixed angle and only pans (CameraController:
                // HandleRotation/HandleTilt exist but are never called). Teaching
                // keys that do nothing makes a first-time player think the
                // tutorial, or their keyboard, is broken. Only zoom is real.
                Body = "<b>Scroll wheel</b> zooms in and out.\n"
                     + "Pull back to read a fight, push in to see what a building is doing.",
                Done = (t, em, f) => t.ZoomChanged() > 0.15f,
            },

            // ── 2. Workers and resources ───────────────────────────────────
            new Step
            {
                Chapter = Ch2,
                Title = "Select and move",
                Body = "<b>Left-click</b> a single Worker to select it. <b>Right-click</b> "
                     + "the ground to send it there.\n"
                     + "Its stats appear bottom-left; what it can do appears beside them.",
                Done = (t, em, f) => t.WorkerOrderedToMove(em, f),
            },
            new Step
            {
                // A NOTE, not a task. This asked the player to raise a
                // Veilstone Mine, which they cannot do here or anywhere:
                // "VeilstoneMine" is absent from
                // EntityExtractors.BuildableBuildings, so it never appears in
                // the builder palette at all. (Its minEra of 1 is NOT the
                // blocker — factions start at era 1.) Of the extractors that
                // ARE in the palette, the iron Mine and the Smelter are era 2,
                // so in Age 0 the Gatherer's Hut is the only one a player can
                // raise. The lesson needs no building anyway: the node pays on
                // its own, which is the point.
                //
                // Deliberately does NOT promise the Veilstone Mine later —
                // until it is in the palette that would be a promise the game
                // does not keep. Add it back to the text when it ships.
                Chapter = Ch2,
                Title = "Your ground is already paying",
                NoteSeconds = 9f,
                Body = "<b>Nobody gathers anything.</b> Income comes from the ground you "
                     + "hold: every territory pays you per minute, and every resource "
                     + "<b>node</b> standing in it pays more — with nothing built on it "
                     + "and nobody working it.\n"
                     + "Your home territory holds a <b>veilstone outcropping</b>, so that "
                     + "veilstone is arriving in your bank right now, just for holding the "
                     + "ground. Watch the counter.\n"
                     + "<b>An extractor multiplies a node, it does not unlock one.</b> In "
                     + "this age the <b>Gatherer's Hut</b> is the only one you can raise — "
                     + "the rest arrive with your culture. So for now the way to earn more "
                     + "is to hold more ground.",
            },
            new Step
            {
                Chapter = Ch2,
                Title = "Box-select and build",
                Body = "<b>Drag a box</b> over two or more Workers, then pick <b>Hut</b> from "
                     + "the actions panel and left-click the ground.\n"
                     + "Huts raise your population cap. Hold <b>Shift</b> while placing to "
                     + "keep going.",
                Grant = Cost.Of(supplies: 300, iron: 150), GrantLabel = "a building fund",
                Done = (t, em, f) => t._sawMultiWorkerSelection
                                     && t.CountOwned(em, f, HutQueryTypes, ref t._hutQuery) > 0,
            },
            new Step
            {
                Chapter = Ch2,
                Title = "Train more workers",
                Body = "Select your <b>Fortress</b> and click <b>Worker</b> in the actions "
                     + "panel.\n"
                     + "The queue strip above the panel shows what is in production — "
                     + "<b>right-click a queued chip</b> to cancel it and get the cost back.",
                Done = (t, em, f) => t.CountOwned(em, f, UnitQueryTypes, ref t._unitQuery)
                                     > t._unitsAtMatchStart,
            },
            new Step
            {
                Chapter = Ch2,
                Title = "Claim a second territory",
                Body = "You may only build inside ground you already hold — with one "
                     + "exception, and it is the whole game: the <b>Hall</b>.\n"
                     + "A Hall is the only building you can raise on unclaimed ground, and "
                     + "raising it <b>claims that territory</b>. Pick Hall, place it in a "
                     + "neighbouring region, and the ground becomes yours.\n"
                     + "It costs <b>450 supplies and 450 iron</b> — the largest purchase in "
                     + "the game, because it is the only one that makes your economy bigger. "
                     + "<b>One Hall per territory</b>, and a claim <b>dies with its Hall</b>: "
                     + "kill the building, the ground goes back to unclaimed.",
                Grant = Cost.Of(supplies: 500, iron: 500), GrantLabel = "a claim pot",
                Done = (t, em, f) => t.TerritoriesHeld(f) >= 2,
            },
            new Step
            {
                Chapter = Ch2,
                Title = "Work your supply nodes",
                Body = "A <b>Gatherer's Hut</b> must stand <b>on a supply node</b>, and each "
                     + "node takes one. So a territory's supply-node count IS its hut cap — "
                     + "an ordinary territory has <b>two</b>, and a home like yours has "
                     + "<b>four</b>.\n"
                     + "Build <b>three</b>. Each one roughly triples what its node pays.",
                Grant = Cost.Of(supplies: 400, iron: 60), GrantLabel = "a survey fund",
                Done = (t, em, f) => t.HutsBuilt(em, f) >= 3,
            },

            // ── 3. Combat ──────────────────────────────────────────────────
            new Step
            {
                Chapter = Ch3,
                Title = "Raise a Barracks",
                Body = "Build a <b>Barracks</b>, then train <b>three Spearmen</b>.\n"
                     + "Units counter each other — select one and read the <b>Bonus vs</b> "
                     + "line in its stats.",
                Grant = Cost.Of(supplies: 700, iron: 500), GrantLabel = "an army budget",
                Done = (t, em, f) =>
                    t.CountOwned(em, f, BarracksQueryTypes, ref t._barracksQuery) > 0
                    && t.CountOwned(em, f, SpearmanQueryTypes, ref t._spearmanQuery) >= 3,
            },
            new Step
            {
                Chapter = Ch3,
                Title = "Take the fight out",
                Body = "<b>Box-select</b> your soldiers and <b>right-click an enemy</b> to "
                     + "attack.\nPress <b>A</b> then click the ground to attack-move — they "
                     + "will engage anything they meet on the way.",
                Done = (t, em, f) => t.MilitaryHasEnemyTarget(em, f),
            },

            // ── 4. Culture ─────────────────────────────────────────────────
            new Step
            {
                Chapter = Ch4,
                Title = "Choose your special building",
                // "Shrine of Ridan", not "Ahridan": the SO's displayName and the
                // god the Temple is named for. The typo made this the one body
                // whose PT key never matched, so the Portuguese build silently
                // showed it in English.
                Body = "Pick one of <b>Shrine of Ridan</b>, <b>Vault of Almiérra</b> or "
                     + "<b>Fiendstone Keep</b> from the top of the screen and place it. "
                     + "Hover each for what it does.\n"
                     + "This choice is final for the match, and it is what unlocks your "
                     + "culture.",
                Grant = Cost.Of(supplies: 600, iron: 400),
                GrantLabel = "enough for a special building",
                Done = (t, em, f) => BuildingFactory.GetFactionChoiceBuilding(em, f) != null,
            },
            new Step
            {
                Chapter = Ch4,
                Title = "Age up",
                Body = "When it finishes, <b>SELECT CULTURE</b> appears at the top. Click it "
                     + "and commit.\nThat ends Age 0 and opens your culture's units, "
                     + "buildings and upgrades — and your <b>verb</b>, which is how the "
                     + "match is won.",
                Grant = Cost.Of(supplies: 500, iron: 300, veilstone: 200),
                GrantLabel = "the age-up cost",
                Done = (t, em, f) => t.Culture(em, f) != Cultures.None,
            },
            new Step
            {
                Chapter = Ch4,
                Title = "Raise the Temple of Ridan",
                Body = "Place the <b>Temple of Ridan</b>.\n"
                     + "It holds your chapel slots and every sect you will ever adopt.",
                Grant = Cost.Of(supplies: 700, iron: 500, veilstone: 250),
                GrantLabel = "Temple materials",
                Done = (t, em, f) => t.HasCompletedTemple(em, f),
            },
            new Step
            {
                Chapter = Ch4,
                Title = "Upgrade the Temple",
                Body = "Select the Temple and start its upgrade.\n"
                     + "Each level raises your <b>era</b>, which pays <b>Religion Points</b> "
                     + "and advances every sect you have adopted.\n"
                     + "<i>Tutorial shortcut: this one upgrade carries it to level 4 — the "
                     + "top — so the next chapters have everything they need.</i>",
                Grant = Cost.Of(supplies: 1200, iron: 900, veilstone: 600),
                GrantLabel = "Temple upgrade stone",
                Done = (t, em, f) => t.TempleAtMaxLevel(em, f),
            },

            // ── 5. Religion ────────────────────────────────────────────────
            new Step
            {
                Chapter = Ch5,
                Title = "Adopt a sect",
                Body = "The <b>religion panel</b> on the right shows your chapel slots and "
                     + "your <b>Religion Points</b>.\n"
                     + "RP is not income. You get a fixed amount per era — <b>6, then 8, "
                     + "then 10</b> — plus <b>1</b> for a Shrine, and anything unspent "
                     + "carries to the next era at <b>two to one</b>. There is no way to "
                     + "farm more.\nSo the sects you choose <i>are</i> your build. Click a "
                     + "slot, read the roster on hover, and commit.",
                Grant = Cost.Of(supplies: 800, iron: 600, veilstone: 400),
                GrantLabel = "chapel materials",
                Done = (t, em, f) => t.AdoptedSect(em, f) != null,
            },
            new Step
            {
                Chapter = Ch5,
                Title = "Cast a sect power",
                Body = "Your sect's slot now carries four cells: <b>P</b> is its always-on "
                     + "passive, <b>1 2 3</b> are its actives, unlocked by Temple level.\n"
                     + "Hover each for what it does, then click a lit one and pick a target "
                     + "on the map.",
                Done = (t, em, f) => t.AnySectPowerOnCooldown(em, f),
            },

            // ── 6. The curse ───────────────────────────────────────────────
            new Step
            {
                Chapter = Ch6,
                Title = "The curse wakes",
                Body = "<b>A ritual has failed somewhere on the map.</b> A channeler began "
                     + "their rite and died before finishing it, and the curse has awakened "
                     + "as a consequence.\n"
                     + "A veilstone node near you is <b>corrupting</b>. In a few seconds a "
                     + "<b>Curse Node</b> rises there and hazes the whole patch. Watch the "
                     + "purple spread.\n"
                     + "This is also what <b>holding ground</b> costs. Keep a veilstone "
                     + "territory that is not your home for <b>two minutes</b> and its "
                     + "pocket wakes. Your home is exempt — your Fortress projects a "
                     + "suppression ring, and the curse can never wake inside your "
                     + "influence. It is the ground you had to leave home for that bites.",
                OnSuggest = (t, em, f) => t.BeginCurseChapter(em, f),
                Done = (t, em, f) => t.ScriptedCurseNodeRisen(em),
            },
            new Step
            {
                Chapter = Ch6,
                Title = "Break the Curse Node",
                Body = "Bring your army. It has <b>1800 HP</b> and is built to resist a "
                     + "starting force — this is a real commitment.\n"
                     + "Kill it and the pocket <b>shatters</b>: the ground clears and it pays "
                     + "out <b>five veilstone nodes</b>. You get the patch back and a bonus.\n"
                     + "Leave it and it keeps feeding, and the crust it lays down denies you "
                     + "the ground: a few seconds' grace, then damage that scales with "
                     + "depth, plus slower movement and worse stats.\n"
                     + "The other way is to <b>starve</b> it. A pocket cannot live on ground "
                     + "somebody holds — <b>claim the territory</b> and it dies on its own. "
                     + "That is the same rule twice: taking ground is how you grow, and it "
                     + "is also how you clean.",
                Done = (t, em, f) => t.ScriptedCurseNodeBroken(em),
            },
            // A NOTE: the curse's expansion happens TO the player on a timer,
            // so there is no action that completes it. Do not "fix" this into
            // a condition that waits for a conquest — the first lands at 240 s
            // and the next every 150 s, so on a fast run it would be a wall of
            // exactly the kind chapter 2 used to be. Nor into a state test
            // like "the curse holds ground": that is true at match start, and
            // ScanAll would retire the step before the panel ever showed it.
            new Step
            {
                Chapter = Ch6,
                Title = "The curse takes ground",
                NoteSeconds = 12f,
                Body = "The pocket you just broke was the curse being <b>provoked</b>. It is "
                     + "also a <b>territorial power</b>, and it expands the way you do.\n"
                     + "It holds every well territory from the first minute. Every couple of "
                     + "minutes it takes <b>one more</b> — always a territory that is next to "
                     + "ground it already holds, <b>carries veilstone</b>, and has <b>no "
                     + "Hall</b>. Look at the territory map: what it can take next is "
                     + "readable, exactly like your own expansion.\n"
                     + "Each territory it takes gets a <b>curse anchor</b>. Kill the anchor "
                     + "and the ground reverts at once — anchors die, wells do not. And "
                     + "every cursed veilstone territory <b>sends waves at you</b>, so "
                     + "ground you leave hall-less becomes a front line.\n"
                     + "<b>A Hall is a wall.</b> Claiming veilstone ground is how you stop "
                     + "the map being eaten — expansion is defence.",
            },

            // ── 7. The wells ───────────────────────────────────────────────
            new Step
            {
                Chapter = Ch7,
                Title = "Train a Holy Scholar",
                Body = "The giant veilstone formations are the <b>wells</b> — selecting one "
                     + "reads <i>Veilstone Hive</i>. They are the largest income on the map "
                     + "and the only way the match is won.\n"
                     + "Every well is <b>dormant</b> until a player reaches for it. The curse "
                     + "has held the ground around them since the first minute — but the "
                     + "wells themselves are asleep, and a sleeping well does not fight "
                     + "you.\n"
                     + "Claiming one needs a ritualist. Alanthor's is the <b>Holy Scholar</b>, "
                     + "trained at the <b>Temple of Ridan at level 3 or higher</b> — yours is "
                     + "at 4. It has 90 HP and no answer to anything: a key, not a soldier.",
                Grant = Cost.Of(supplies: 600, iron: 400), GrantLabel = "a Scholar's stipend",
                Done = (t, em, f) =>
                    t.CountOwned(em, f, ScholarQueryTypes, ref t._scholarQuery) > 0,
            },
            new Step
            {
                Chapter = Ch7,
                Title = "Purify a well",
                Body = "Send the Scholar to a well <b>with your army around it</b> and begin "
                     + "the rite.\nTwo things happen the instant the channel starts, and "
                     + "neither can be undone:\n"
                     + "<b>The well wakes, permanently.</b> It begins feeding the curse and "
                     + "never sleeps again — and every player is told who woke it. Waking one "
                     + "on a rival's doorstep costs them ground whether you finish or not.\n"
                     + "<b>You are committed.</b> Break the channel — Scholar killed, dragged "
                     + "off, interrupted — and the well answers with the <b>Backlash</b>: "
                     + "five escalating waves of crystal creatures that keep coming whether "
                     + "you stay or run. That is the failed ritual you were told about.\n"
                     + "Each culture has one verb: Alanthor <b>purifies</b>, Runai "
                     + "<b>pacifies</b>, Feraldis <b>destroys</b>. Hold every well in your "
                     + "verb-state at once and you win.",
                Grant = Cost.Of(supplies: 1500, iron: 1000, veilstone: 800),
                GrantLabel = "a campaign chest",
                Done = (t, em, f) => t.OwnsAnyWell(em, f),
            },
        };

        // ── State ──────────────────────────────────────────────────────────

        private int _index;
        private float _timer;
        private float _completedAt = -1f;

        /// <summary>When the current step became the suggestion. Only a NOTE
        /// reads it — see <see cref="Step.NoteSeconds"/>.</summary>
        private float _suggestedAt = -1f;
        private bool _finished;

        /// <summary>Latched completion, one per step. Once a box is ticked it
        /// never un-ticks, so transient conditions (a worker selected, a power
        /// recharging) survive the moment that satisfied them.</summary>
        private bool[] _done;
        /// <summary>Grant already paid, one per step.</summary>
        private bool[] _paid;
        /// <summary>OnSuggest already fired, one per step.</summary>
        private bool[] _suggested;

        private Vector3 _cameraStart;
        private bool _haveCameraStart;
        private float _zoomStart = -1f;

        /// <summary>Latched: two or more owned workers were selected at once.
        /// Not "box-selected" — box versus shift-click is not reliably
        /// distinguishable, and the lesson is the multi-selection either way.</summary>
        private bool _sawMultiWorkerSelection;

        // Baselines taken ONCE, at the first tick with a live bank. "Did this
        // number go up" is measured against the start of the MATCH, not the
        // start of the instruction, so a player who trained before being asked
        // to has already satisfied the step.
        //
        // The veilstone baseline that used to live here is GONE with the
        // gathering step it served. Territory income pays veilstone passively
        // now, so "the bank went up" is true within seconds of the match
        // starting and measures nothing the player did.
        private bool _baselined;
        private int _unitsAtMatchStart;

        // Curse chapter bookkeeping.
        private bool _curseChapterBegun;
        private float2 _scriptedCorruptionAt;
        private Entity _scriptedCurseNode = Entity.Null;
        private bool _scriptedNodeSeen;

        private GameObject _root;
        private TMP_Text _eyebrow, _title, _body;

        private static readonly ComponentType[] BankQueryTypes =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionResources>(),
        };
        private static readonly ComponentType[] HutQueryTypes =
        {
            ComponentType.ReadOnly<HutTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] UnitQueryTypes =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] GathererHutQueryTypes =
        {
            ComponentType.ReadOnly<GathererHutTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        private static readonly ComponentType[] BarracksQueryTypes =
        {
            ComponentType.ReadOnly<BarracksTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        private static readonly ComponentType[] SpearmanQueryTypes =
        {
            ComponentType.ReadOnly<SpearmanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] ScholarQueryTypes =
        {
            ComponentType.ReadOnly<ScholarTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] MilitaryQueryTypes =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Target>(),
            ComponentType.Exclude<CanBuild>(),
        };
        private static readonly ComponentType[] HallQueryTypes =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionProgress>(),
        };
        private static readonly ComponentType[] TempleQueryTypes =
        {
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] SmallNodeQueryTypes =
        {
            ComponentType.ReadOnly<SmallNodeTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static readonly ComponentType[] PocketRegistryQueryTypes =
        {
            ComponentType.ReadWrite<BlightPocket>(),
        };
        private static readonly ComponentType[] WellQueryTypes =
        {
            ComponentType.ReadOnly<BorderMainNodeTag>(),
            ComponentType.ReadOnly<BorderNodeState>(),
        };

        private CachedEntityQuery _bankQuery, _hutQuery, _unitQuery,
                                  _gathererQuery, _barracksQuery, _spearmanQuery,
                                  _scholarQuery, _militaryQuery, _hallQuery, _templeQuery,
                                  _smallNodeQuery, _pocketRegistryQuery, _wellQuery;

        // ── Setup ──────────────────────────────────────────────────────────

        private void Awake()
        {
            if (!GameSettings.TutorialActive) { enabled = false; return; }
            _done = new bool[Steps.Length];
            _paid = new bool[Steps.Length];
            _suggested = new bool[Steps.Length];
            Build();
        }

        private void OnDestroy()
        {
            // Never leave the shipped pacing overridden.
            TheWaningBorder.Systems.Border.VeilFieldSystem.TutorialCreepMultiplier = 1f;
        }

        private void Build()
        {
            _root = GameUIKit.Rect(transform, "TutorialCoach").gameObject;
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(40f, 220f);
            rt.sizeDelta = new Vector2(PanelWidth, 0f);

            // The coach sits OVER the battlefield for the whole match, and
            // RTSInputManager.ShouldBlockInput treats a pointer over any uGUI
            // graphic as "the UI owns this click". A raycast-target backdrop
            // here therefore swallowed selection and move orders across a
            // large slab of the left-hand screen. The frame is decoration —
            // only its two buttons take input.
            var chrome = GameUIKit.PanelChrome(rt);
            chrome.raycastTarget = false;

            var stack = GameUIKit.VStack(rt, 24f, 10f);
            stack.childForceExpandHeight = false;
            var fitter = _root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _eyebrow = GameUIKit.Text(rt, "eyebrow", "", 22f, GameUIKit.TextDim,
                TextAlignmentOptions.Left, wrap: false);
            _eyebrow.characterSpacing = 4f;
            GameUIKit.FixHeight(_eyebrow.gameObject, 28f);

            _title = GameUIKit.Text(rt, "title", "", 36f, GameUIKit.Gold);
            _title.fontStyle = FontStyles.Bold;
            GameUIKit.FixHeight(_title.gameObject, 46f);

            _body = GameUIKit.Text(rt, "body", "", 25f, GameUIKit.TextMain);

            var row = GameUIKit.Rect(rt, "buttons");
            GameUIKit.FixHeight(row.gameObject, 64f);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;

            MakeButton(row, "skip", Loc.T("Skip this step"),
                Loc.T("Tick this one off and suggest the next. Steps can be done in any order — "
                + "skipping still pays out its resource package, so jumping ahead never "
                + "leaves you short."), SkipCurrent);
            MakeButton(row, "end", Loc.T("End tutorial"),
                Loc.T("Dismiss the coach. The match carries on as a normal skirmish."), Finish);

            ShowStep();
        }

        private static void MakeButton(Transform parent, string name, string label,
            string tooltip, System.Action click)
        {
            var rt = GameUIKit.Rect(parent, name);
            var bg = GameUIKit.Image(rt, "bg", GameUIKit.ButtonBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);
            var text = GameUIKit.Text(rt, "label", label, 24f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);

            UITooltip.Relay(bg.gameObject).OnLeftClick = click;
            UITooltip.Bind(bg.gameObject, tooltip);
        }

        // ── Loop ───────────────────────────────────────────────────────────

        private void Update()
        {
            if (_finished) return;

            _timer += Time.unscaledDeltaTime;
            if (_timer < CheckInterval) return;
            _timer = 0f;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            // Baselines need a faction bank, which the bootstrap creates after
            // this component exists. Until then there is nothing to measure.
            if (!_baselined)
            {
                if (!Bank(em, faction, out var start)) return;
                _baselined = true;
                _unitsAtMatchStart = CountOwned(em, faction, UnitQueryTypes, ref _unitQuery);
                Suggest(FirstUnfinished(), em, faction);
            }

            // Always-on observers: things the player may do at any point, whose
            // moment would otherwise be missed between ticks or steps.
            ObserveSelection(em, faction);
            DriveTempleUpgradeToMax(em, faction);
            TrackScriptedCurseNode(em);

            ScanAll(em, faction);

            // The suggestion only moves once the player has had a moment to
            // read its "DONE" state.
            if (_index < Steps.Length && _done[_index])
            {
                if (_completedAt < 0f)
                {
                    _completedAt = Time.unscaledTime;
                    _eyebrow.text = ChapterLine(_index) + Loc.T("   —   DONE");
                    _title.color = new Color(0.50f, 0.72f, 0.42f);
                }
                else if (Time.unscaledTime - _completedAt >= AdvanceDelay)
                {
                    _completedAt = -1f;
                    Suggest(FirstUnfinished(), em, faction);
                }
            }
        }

        /// <summary>
        /// Evaluate EVERY unfinished step, not just the suggested one. This is
        /// what lets the player range ahead: build the Temple during chapter 2
        /// and its box ticks itself, with its grant paid out so nothing was
        /// lost by not waiting to be asked.
        /// </summary>
        private void ScanAll(EntityManager em, Faction faction)
        {
            for (int i = 0; i < Steps.Length; i++)
            {
                if (_done[i]) continue;

                var step = Steps[i];
                if (step.NoteSeconds > 0f)
                {
                    // A note ticks only while it is the one on screen, and
                    // only once it has been there long enough to read. Any
                    // state-based condition would be true at match start and
                    // this loop would retire it before it was ever shown.
                    if (i != _index || _suggestedAt < 0f
                        || Time.unscaledTime - _suggestedAt < step.NoteSeconds)
                        continue;
                }
                else if (!step.Done(this, em, faction)) continue;

                _done[i] = true;
                Pay(i, em, faction);   // finished early? still paid
                PlayerNotificationSystem.Notify(string.Format(i == _index
                    ? Loc.T("Tutorial: {0} — done")
                    : Loc.T("Tutorial: {0} — done (ahead of the coach)"),
                    Loc.T(Steps[i].Title)));
            }
        }

        private int FirstUnfinished()
        {
            for (int i = 0; i < Steps.Length; i++)
                if (!_done[i]) return i;
            return Steps.Length;
        }

        /// <summary>Point the panel at a step. Pays its grant and runs its
        /// scripted setup, both exactly once.</summary>
        private void Suggest(int index, EntityManager em, Faction faction)
        {
            _index = index;
            _completedAt = -1f;
            _suggestedAt = Time.unscaledTime;   // a NOTE dwells from here
            if (_index >= Steps.Length) { Finish(); return; }

            Pay(_index, em, faction);
            if (!_suggested[_index])
            {
                _suggested[_index] = true;
                Steps[_index].OnSuggest?.Invoke(this, em, faction);
            }
            ShowStep();
        }

        /// <summary>Skip button: tick the suggestion off by hand and move on.
        /// Walking forward this way still pays every grant it passes, so a
        /// player who skips ahead to the wells is not left broke.</summary>
        private void SkipCurrent()
        {
            if (_finished || _index >= Steps.Length) return;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            _done[_index] = true;
            Pay(_index, em, faction);
            Suggest(FirstUnfinished(), em, faction);
        }

        private void ShowStep()
        {
            var step = Steps[_index];
            _eyebrow.text = ChapterLine(_index);
            _title.text = Loc.T(step.Title);
            _title.color = GameUIKit.Gold;
            _body.text = Loc.T(step.Body);
        }

        private string ChapterLine(int index)
        {
            int done = 0;
            for (int i = 0; i < _done.Length; i++) if (_done[i]) done++;
            return string.Format(Loc.T("{0}   ·   {1} / {2} DONE   ·   ANY ORDER"),
                Loc.T(Steps[index].Chapter).ToUpperInvariant(), done, Steps.Length);
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            GameSettings.TutorialActive = false;
            TheWaningBorder.Systems.Border.VeilFieldSystem.TutorialCreepMultiplier = 1f;
            if (_root != null) _root.SetActive(false);
            PlayerNotificationSystem.Notify(
                Loc.T("Tutorial complete — the match continues as a normal skirmish."));
        }

        // ── Scripted help ──────────────────────────────────────────────────

        /// <summary>
        /// Pay a step's package, exactly once, whether the player got there by
        /// following the coach, by finishing it early, or by skipping past it.
        /// Announced, because a silent gift teaches a false economy.
        /// </summary>
        private void Pay(int index, EntityManager em, Faction faction)
        {
            if (index < 0 || index >= Steps.Length || _paid[index]) return;
            _paid[index] = true;

            var step = Steps[index];
            if (step.Grant.IsZero) return;
            if (!FactionEconomy.Add(em, faction, step.Grant)) return;
            PlayerNotificationSystem.Notify(
                string.Format(Loc.T("Tutorial: granted {0}."), Loc.T(step.GrantLabel)));
        }

        /// <summary>
        /// Open the curse chapter: speed the heartbeat up so the crust visibly
        /// moves inside the lesson, and queue a real corruption a march away
        /// from the Hall.
        ///
        /// The corruption goes through PendingCorruption — the same buffer
        /// TerritoryCorruptionSystem writes when contested veilstone ground has
        /// been held for its two minutes — so the player gets the ordinary
        /// telegraph, ping and rise, and BlightPocketSystem owns the node
        /// exactly as it would any other. Placed beyond the Hall's 34 m hearth
        /// on purpose: inside it, suppression starves the pocket at 20 dps and
        /// it would die before the player could bring an army.
        /// </summary>
        private void BeginCurseChapter(EntityManager em, Faction faction)
        {
            if (_curseChapterBegun) return;
            _curseChapterBegun = true;

            TheWaningBorder.Systems.Border.VeilFieldSystem.TutorialCreepMultiplier =
                CurseChapterCreepSpeed;

            Entity hall = FindHall(em, faction);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall))
            {
                PlayerNotificationSystem.Notify(
                    Loc.T("Tutorial: no Hall found — skip this step to continue."));
                return;
            }
            float3 origin = em.GetComponentData<LocalTransform>(hall).Position;

            var registry = _pocketRegistryQuery.Get(em, PocketRegistryQueryTypes);
            using var registries = registry.ToEntityArray(Allocator.Temp);
            if (registries.Length == 0)
            {
                PlayerNotificationSystem.Notify(
                    Loc.T("Tutorial: the curse is not active on this map — skip this step."));
                return;
            }

            float3 at = ScriptedCorruptionSeat(origin, faction);
            at.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(at.x, at.z);
            _scriptedCorruptionAt = new float2(at.x, at.z);

            // BlightPocketSystem compares At against SystemAPI.Time.ElapsedTime
            // — the ECS SIM clock, not Unity's wall clock. Time.timeAsDouble
            // counts from application start (menus and loading included), so
            // using it here would schedule the rise minutes into the past or
            // future depending on how long the player sat in the menu.
            double simNow = Unity.Entities.World.DefaultGameObjectInjectionWorld
                .Time.ElapsedTime;

            var pending = em.GetBuffer<PendingCorruption>(registries[0]);
            pending.Add(new PendingCorruption
            {
                Pos = at,
                At = simNow + VeilCrustConstants.CorruptionTelegraphSeconds,
            });
            MinimapPings.Post(at, MinimapPings.Curse, 20f);
            PlayerNotificationSystem.NotifyError(
                Loc.T("A ritual has failed — the curse is waking east of your Fortress!"));
        }

        /// <summary>
        /// Where to seat the scripted corruption: the nearest point that is
        /// NOT on territory this player owns.
        ///
        /// It used to be a flat 50 m due east, chosen to clear the Fortress's
        /// 34 m hearth. That was the right rule when suppression came from
        /// BUILDINGS depositing influence. It is not any more: influence is a
        /// straight rasterize of territory OWNERSHIP now (Regions.md §3b), so
        /// every cell of ground you hold reads full strength and
        /// BlightPocketSystem starves anything standing on it at 20 dps — a
        /// pocket inside your own territory dies in 90 s. With territories
        /// averaging ~90 m across, a fixed 50 m offset lands on or just inside
        /// the home border, so the node the chapter is about could quietly
        /// dissolve before the player finished reading about it.
        ///
        /// Walks bearings at increasing range and takes the first seat whose
        /// territory is not this faction's. Deterministic order, and it falls
        /// back to the old fixed offset rather than refusing to place at all —
        /// a map with no partition (RegionMap not ready) must still get its
        /// curse chapter.
        /// </summary>
        private static float3 ScriptedCorruptionSeat(float3 origin, Faction faction)
        {
            float3 fallback = origin + new float3(ScriptedCorruptionDistance, 0f, 0f);
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return fallback;

            // East first, so the "east of your Fortress" reading stays true on
            // the common map; then around the compass.
            for (float range = ScriptedCorruptionDistance;
                 range <= ScriptedCorruptionSearchMax;
                 range += ScriptedCorruptionSearchStep)
            {
                for (int b = 0; b < ScriptedCorruptionBearings; b++)
                {
                    float angle = b * (2f * math.PI / ScriptedCorruptionBearings);
                    float x = origin.x + math.cos(angle) * range;
                    float z = origin.z + math.sin(angle) * range;

                    int t = RegionMap.RegionAt(x, z);
                    if (t == RegionMap.None) continue;              // rim / cliff
                    if (TerritoryOwnership.OwnerOf(t) == (int)faction) continue;

                    return new float3(x, 0f, z);
                }
            }
            return fallback;
        }

        /// <summary>
        /// Bind to the curse node that rises from the scripted corruption, and
        /// notice when it dies. Tracked by identity rather than by counting
        /// nodes, so an unrelated pocket elsewhere on the map neither completes
        /// the step nor un-completes it.
        /// </summary>
        private void TrackScriptedCurseNode(EntityManager em)
        {
            if (!_curseChapterBegun || _scriptedNodeSeen) return;

            var q = _smallNodeQuery.Get(em, SmallNodeQueryTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xforms = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float bestSq = VeilCrustConstants.PocketRadius * VeilCrustConstants.PocketRadius * 4f;
            for (int i = 0; i < ents.Length; i++)
            {
                float2 p = new float2(xforms[i].Position.x, xforms[i].Position.z);
                float d2 = math.distancesq(p, _scriptedCorruptionAt);
                if (d2 > bestSq) continue;
                bestSq = d2;
                _scriptedCurseNode = ents[i];
                _scriptedNodeSeen = true;
            }
        }

        private bool ScriptedCurseNodeRisen(EntityManager em) => _scriptedNodeSeen;

        private bool ScriptedCurseNodeBroken(EntityManager em)
        {
            if (!_scriptedNodeSeen) return false;
            if (em.Exists(_scriptedCurseNode)) return false;

            // The lesson is over; hand the map back its shipped pacing.
            TheWaningBorder.Systems.Border.VeilFieldSystem.TutorialCreepMultiplier = 1f;
            return true;
        }

        /// <summary>
        /// The player starts the Temple upgrade themselves — they need to learn
        /// the button. The moment an upgrade is running, its target is
        /// rewritten to the top level, so one click reaches level 4 and the
        /// religion chapters have their full tier ladder. TempleUpgradeSystem
        /// still completes it normally: era bump, Religion Point award, sect
        /// lever sync and toast all happen as they do in a real match.
        ///
        /// Runs on EVERY tick rather than from its step, because the player is
        /// free to upgrade the Temple whenever they feel like it and should get
        /// the promised shortcut either way.
        /// </summary>
        private void DriveTempleUpgradeToMax(EntityManager em, Faction faction)
        {
            Entity temple = FindTemple(em, faction);
            if (temple == Entity.Null) return;
            if (!em.HasComponent<TempleUpgradeState>(temple)) return;

            var up = em.GetComponentData<TempleUpgradeState>(temple);
            if (up.TargetLevel >= TempleLevelConfig.MaxLevel) return;

            up.TargetLevel = TempleLevelConfig.MaxLevel;
            em.SetComponentData(temple, up);
            PlayerNotificationSystem.Notify(string.Format(
                Loc.T("Tutorial: this upgrade will carry the Temple to level {0}."),
                TempleLevelConfig.MaxLevel));
        }

        // ── Observers ──────────────────────────────────────────────────────

        /// <summary>
        /// Latch "two or more workers selected at once". Polled rather than
        /// checked inside the step, because a selection can come and go
        /// between two 4 Hz ticks of an unrelated step.
        /// </summary>
        private void ObserveSelection(EntityManager em, Faction faction)
        {
            if (_sawMultiWorkerSelection) return;
            var selection = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (selection == null) return;

            int workers = 0;
            for (int i = 0; i < selection.Count; i++)
            {
                var e = selection[i];
                if (!em.Exists(e) || !em.HasComponent<CanBuild>(e)) continue;
                if (!em.HasComponent<FactionTag>(e)) continue;
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                if (++workers >= 2) { _sawMultiWorkerSelection = true; return; }
            }
        }

        // ── Condition helpers ──────────────────────────────────────────────

        private float CameraTravelled()
        {
            var cam = Camera.main;
            if (cam == null) return 0f;
            if (!_haveCameraStart)
            {
                _cameraStart = cam.transform.position;
                _haveCameraStart = true;
                return 0f;
            }
            return Vector3.Distance(_cameraStart, cam.transform.position);
        }

        private float ZoomChanged()
        {
            float now = TheWaningBorder.CameraRig.CameraController.ZoomNormalized;
            if (_zoomStart < 0f) { _zoomStart = now; return 0f; }
            return Mathf.Abs(now - _zoomStart);
        }

        /// <summary>An owned worker is selected AND carries a live destination
        /// — i.e. the player has clicked one and sent it somewhere.</summary>
        private bool WorkerOrderedToMove(EntityManager em, Faction faction)
        {
            var selection = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (selection == null) return false;
            for (int i = 0; i < selection.Count; i++)
            {
                var e = selection[i];
                if (!em.Exists(e) || !em.HasComponent<CanBuild>(e)) continue;
                if (!em.HasComponent<FactionTag>(e)
                    || em.GetComponentData<FactionTag>(e).Value != faction) continue;
                if (em.HasComponent<DesiredDestination>(e)
                    && em.GetComponentData<DesiredDestination>(e).Has != 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Territories this faction holds. The Fortress claims its home ground,
        /// so this is 1 from the first tick and 2 the moment a Hall finishes on
        /// unclaimed ground — which is exactly the lesson the step is checking.
        ///
        /// Read from TerritoryOwnership rather than counted from Halls: a
        /// second Hall in the same territory claims nothing, so counting
        /// buildings would tick the step for a purchase that took no ground.
        /// </summary>
        private int TerritoriesHeld(Faction faction)
            => TerritoryOwnership.Ready ? TerritoryOwnership.CountOf(faction) : 0;

        /// <summary>Completed Gatherer's Huts this faction owns. Replaces the
        /// old best-coverage check: a hut has no coverage any more, and no
        /// per-territory cap either — it stands on a supply node, one hut per
        /// node, so "work the nodes you hold" is what the rule is now.</summary>
        private int HutsBuilt(EntityManager em, Faction faction)
        {
            var q = _gathererQuery.Get(em, GathererHutQueryTypes);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i].Value == faction) n++;
            return n;
        }

        private bool MilitaryHasEnemyTarget(EntityManager em, Faction faction)
        {
            var q = _militaryQuery.Get(em, MilitaryQueryTypes);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var targets = q.ToComponentDataArray<Target>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value != faction) continue;
                var target = targets[i].Value;
                if (target == Entity.Null || !em.Exists(target)) continue;
                if (!em.HasComponent<FactionTag>(target)) continue;
                if (em.GetComponentData<FactionTag>(target).Value != faction) return true;
            }
            return false;
        }

        private bool Bank(EntityManager em, Faction faction, out FactionResources bank)
        {
            bank = default;
            var q = _bankQuery.Get(em, BankQueryTypes);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var banks = q.ToComponentDataArray<FactionResources>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value != faction) continue;
                bank = banks[i];
                return true;
            }
            return false;
        }

        private int CountOwned(EntityManager em, Faction faction, ComponentType[] types,
            ref CachedEntityQuery cache)
        {
            var q = cache.Get(em, types);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i].Value == faction) count++;
            return count;
        }

        private byte Culture(EntityManager em, Faction faction)
        {
            var q = _hallQuery.Get(em, HallQueryTypes);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var progress = q.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
                if (tags[i].Value == faction) return progress[i].Culture;
            return Cultures.None;
        }

        private Entity FindHall(EntityManager em, Faction faction)
        {
            var q = _hallQuery.Get(em, HallQueryTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
                if (tags[i].Value == faction) return ents[i];
            return Entity.Null;
        }

        private Entity FindTemple(EntityManager em, Faction faction)
        {
            var q = _templeQuery.Get(em, TempleQueryTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
                if (tags[i].Value == faction) return ents[i];
            return Entity.Null;
        }

        private bool HasCompletedTemple(EntityManager em, Faction faction)
        {
            Entity temple = FindTemple(em, faction);
            return temple != Entity.Null && !em.HasComponent<UnderConstruction>(temple);
        }

        private bool TempleAtMaxLevel(EntityManager em, Faction faction)
        {
            Entity temple = FindTemple(em, faction);
            return temple != Entity.Null
                && em.HasComponent<TempleLevel>(temple)
                && em.GetComponentData<TempleLevel>(temple).Level >= TempleLevelConfig.MaxLevel;
        }

        /// <summary>First sect adopted into a chapel slot (state 2), or null.</summary>
        private string AdoptedSect(EntityManager em, Faction faction)
        {
            Entity temple = FindTemple(em, faction);
            if (temple == Entity.Null || !em.HasBuffer<TempleChapelSlot>(temple)) return null;
            var slots = em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].State == 2) return slots[i].SectId.ToString();
            return null;
        }

        /// <summary>A sect power fired = that tier is now recharging.</summary>
        private bool AnySectPowerOnCooldown(EntityManager em, Faction faction)
        {
            Entity temple = FindTemple(em, faction);
            if (temple == Entity.Null || !em.HasBuffer<TempleChapelSlot>(temple)) return false;

            var slots = em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].State != 2) continue;
                string sectId = slots[i].SectId.ToString();
                for (int tier = 1; tier <= 3; tier++)
                    if (SectActivePowerHelper.CooldownRemaining(em, faction, sectId, tier) > 0f)
                        return true;
            }
            return false;
        }

        /// <summary>Any well currently claimed by the local player — purified,
        /// pacified or destroyed, per their culture's verb.</summary>
        private bool OwnsAnyWell(EntityManager em, Faction faction)
        {
            var q = _wellQuery.Get(em, WellQueryTypes);
            using var states = q.ToComponentDataArray<BorderNodeState>(Allocator.Temp);
            for (int i = 0; i < states.Length; i++)
                if (states[i].State != NodeState.Active && states[i].OwnerFaction == faction)
                    return true;
            return false;
        }
    }
}
