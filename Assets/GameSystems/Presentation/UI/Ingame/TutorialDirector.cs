// TutorialDirector.cs
// The tutorial scenario's coach: a chaptered CHECKLIST that watches real game
// state and ticks itself off as the player plays.
//
// It spans the WHOLE game (rewritten 2026-09-29 for the fourth territory
// model — docs/Design/Territory_Claims.md, Religion.md, Age_0.md § Age-up by
// landmark):
//   1  Controls
//   2  Economy — workers, huts, extractors on nodes
//   3  Army — soldiers, stances, queued orders
//   4  Territory — standing armies claim, buildings hold, extractors lock
//   5  Culture — the age-up landmark
//   6  The curse — its nodes, its units, and the religion they pay
//   7  Religion — Temple, chapels, powers, sect heroes
//   8  The Shardroot and how the match is won
//
// IT NEVER GATES THE PLAYER. This is the design rule the rest of the file
// exists to serve:
//   * EVERY step is evaluated on EVERY tick, not just the one being shown. Do
//     things in whatever order you like — raise the Temple in chapter 2, claim
//     ground before you have trained a worker — and the checklist simply
//     ticks the boxes as you go.
//   * Conditions are ABSOLUTE, measured against the state of the match rather
//     than "since this instruction appeared". Work you did before being asked
//     counts; nothing has to be repeated to satisfy the coach. A NOTE (a step
//     that only explains something) is the one exception and has to be — see
//     Step.NoteSeconds.
//   * Completion LATCHES. Transient conditions (a unit selected, a power
//     recharging, a curse unit dying) stay ticked once seen, so a box never
//     un-ticks.
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
// (TerritoryOwnership, Target, TempleChapelSlot, SectAdoptionState,
// SmallNodeTag, LastDamagedByFaction), so the only way to fail is to change
// the game, which is exactly when the tutorial SHOULD break.
//
// IT HAS BROKEN TWICE, and both times the same way: a mechanic was deleted and
// the step that taught it kept compiling. 2026-09-07 it was worker gathering
// (a dead WorkerState nothing wrote any more). 2026-09-29 it was the Hall
// claim, the era-paid RP, the Temple levels, the scripted blight pocket and
// the wells — the whole second half of the old coach. When a mechanic is
// deleted, grep this file for what fed it.
//
// One scripted help, tutorial-only and deliberate:
//   * GRANTS — a chapter that teaches sects cannot wait out the curse fights
//     it would normally take to earn the Religion Points. Each step's package
//     (resources and/or RP) is paid when it becomes the suggestion or when the
//     player finishes it early, whichever comes first, and is announced in the
//     notification line so nobody mistakes it for their own economy.
// The curse steps also PING the nearest curse node on the minimap when they
// become the suggestion. That writes nothing to the simulation.
//
// Steps are checked at 4 Hz, never per frame, and finished ones stop being
// evaluated. Apart from the grants, nothing here writes to the simulation.

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

        private delegate bool Check(TutorialDirector t, EntityManager em, Faction f);
        private delegate void Setup(TutorialDirector t, EntityManager em, Faction f);

        private sealed class Step
        {
            public string Chapter;
            public string Title;
            public string Body;
            /// <summary>Resources paid out for this step's lesson.</summary>
            public Cost Grant;
            /// <summary>Religion Points paid out for this step's lesson.</summary>
            public int RpGrant;
            public string GrantLabel;
            /// <summary>Setup that only makes sense once the player is actually
            /// looking at this step (the curse-node ping). Runs when the step
            /// becomes the suggestion.</summary>
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
        private const string Ch2 = "2. Economy";
        private const string Ch3 = "3. Army";
        private const string Ch4 = "4. Territory";
        private const string Ch5 = "5. Culture";
        private const string Ch6 = "6. The curse";
        private const string Ch7 = "7. Religion";
        private const string Ch8 = "8. Victory";

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
                     + "It locks the ground it stands on, and that ground is your first "
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

            // ── 2. Economy ─────────────────────────────────────────────────
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
                Chapter = Ch2,
                Title = "Box-select and build",
                Body = "<b>Drag a box</b> over two or more Workers, then pick <b>Hut</b> from "
                     + "the actions panel and left-click the ground.\n"
                     + "Huts raise your population cap: <b>3</b>, and <b>5 / 8 / 10</b> as "
                     + "you upgrade them. Your Fortress gives a flat <b>10</b>. Hold "
                     + "<b>Shift</b> while placing to keep going.",
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
                // A NOTE: territory income arrives on its own, so there is no
                // action that completes it, and "the bank went up" is true
                // within seconds of the match starting.
                Chapter = Ch2,
                Title = "Your ground is already paying",
                NoteSeconds = 9f,
                Body = "<b>Nobody gathers anything.</b> Income comes from the ground you "
                     + "hold: every territory pays you per minute, and every resource "
                     + "<b>node</b> standing in it pays more.\n"
                     + "Every home territory holds a <b>veilstone outcropping</b>, so "
                     + "veilstone is arriving in your bank right now. Watch the counter.\n"
                     + "An <b>extractor</b> built on a node multiplies what it pays — and, "
                     + "as the next chapters show, it is also what makes the ground truly "
                     + "yours.",
            },
            new Step
            {
                Chapter = Ch2,
                Title = "Build on your nodes",
                Body = "Raise a <b>Gatherer's Hut</b> on a <b>supply node</b> and a <b>Mine</b> "
                     + "on an ore node — iron, veilstone or veilsteel.\n"
                     + "There is <b>one Mine button</b>: the node under your cursor decides "
                     + "which mine rises. Each node takes exactly one extractor, and "
                     + "<b>nothing else can be built on a node</b>.",
                Grant = Cost.Of(supplies: 500, iron: 200), GrantLabel = "a survey fund",
                Done = (t, em, f) => t.HutsBuilt(em, f) >= 1 && t.MinesBuilt(em, f) >= 1,
            },

            // ── 3. Army ────────────────────────────────────────────────────
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
                Title = "Chain your orders",
                Body = "Hold <b>Shift</b> and <b>right-click</b> several points: the orders "
                     + "queue up and run in turn — up to <b>15</b>. The route is drawn on "
                     + "the ground.\n"
                     + "Soldiers start <b>Aggressive</b>: they chase anything in sight. "
                     + "<b>D</b> makes them Defensive, <b>H</b> holds them in place, <b>G</b> "
                     + "turns them loose again. A queued move is obeyed over the stance — "
                     + "if you want them to fight on the way, press <b>A</b> first.",
                Done = (t, em, f) => t._sawQueuedOrders,
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

            // ── 4. Territory ───────────────────────────────────────────────
            new Step
            {
                Chapter = Ch4,
                Title = "Claim ground with your army",
                Body = "<b>Ground belongs to whoever stands on it.</b> March soldiers into a "
                     + "neighbouring territory and keep them there: its meter fills, "
                     + "faster the more population stands on it, and at <b>100</b> the "
                     + "territory is yours. A bar over the territory itself shows the progress.\n"
                     + "Workers and scouts do not claim. Two hostile armies in one territory "
                     + "<b>freeze</b> it — you take ground by clearing it.",
                Grant = Cost.Of(supplies: 400, iron: 300), GrantLabel = "a campaign purse",
                Done = (t, em, f) => t.TerritoriesHeld(f) >= 2,
            },
            new Step
            {
                // A NOTE: holding and locking are states, not actions, and a
                // player who already built an extractor abroad would otherwise
                // have this ticked before it was ever shown.
                Chapter = Ch4,
                Title = "Hold it, or lose it",
                NoteSeconds = 14f,
                Body = "Ground you leave <b>empty and unbuilt</b> decays back to nobody.\n"
                     + "<b>Any building holds</b> a territory against decay — but an enemy "
                     + "army standing there still drains it.\n"
                     + "An <b>extractor on a node</b>, or a <b>Fortress</b>, <b>locks</b> it: "
                     + "the meter cannot be drained at all while one stands. To take locked "
                     + "ground, raze every lock first.\n"
                     + "Lose a territory and <b>every building you had in it collapses</b>. "
                     + "A second Fortress is the most expensive thing you can build — and "
                     + "the surest lock there is.",
            },

            // ── 5. Culture ─────────────────────────────────────────────────
            new Step
            {
                Chapter = Ch5,
                Title = "Build your landmark",
                Body = "The age-up <b>is</b> a building. Pick the <b>Vault of Almiérra</b> from "
                     + "the top of the screen and place it on ground you own.\n"
                     + "It costs <b>600 supplies, 300 iron and 200 veilstone</b>, builds "
                     + "itself in 90 seconds, and every worker you send speeds it up. "
                     + "<b>Only one per match</b> — and if it is destroyed before it "
                     + "finishes, the progress and everything you paid are lost.",
                Grant = Cost.Of(supplies: 600, iron: 300, veilstone: 200),
                GrantLabel = "the landmark's cost",
                Done = (t, em, f) => BuildingFactory.GetFactionChoiceBuilding(em, f) != null,
            },
            new Step
            {
                Chapter = Ch5,
                Title = "Age up",
                Body = "When the Vault finishes, you <b>are</b> Alanthor: Age 0 ends and your "
                     + "culture's units, buildings and upgrades open.\n"
                     + "Protect the site until then. The <b>Advancing</b> pill at the top "
                     + "shows how far it has come.",
                Done = (t, em, f) => t.Culture(em, f) != Cultures.None,
            },

            // ── 6. The curse ───────────────────────────────────────────────
            new Step
            {
                // A NOTE: the curse acts on its own clock; no player action
                // completes this, and "the curse holds ground" is true from
                // the first minute.
                Chapter = Ch6,
                Title = "The curse",
                NoteSeconds = 14f,
                OnSuggest = (t, em, f) => t.PingNearestCurseNode(em, f),
                Body = "The curse claims ground the same way you do — by standing on it, "
                     + "at <b>double weight</b>. It raises <b>curse nodes</b> on resource "
                     + "nodes, and each one <b>locks</b> its territory and fields a "
                     + "garrison that grows over time.\n"
                     + "The purple around a node is <b>cursed ground</b>: it slows and "
                     + "burns anything that stands in it.\n"
                     + "The minimap is pinging the nearest node now.",
            },
            new Step
            {
                Chapter = Ch6,
                Title = "Kill curse creatures",
                OnSuggest = (t, em, f) => t.PingNearestCurseNode(em, f),
                Body = "Curse creatures are the <b>only source of religion</b>. Every kill "
                     + "pays points — the <b>last hit</b> is paid, whatever landed it — and "
                     + "points turn into <b>Religion Points</b>. The first ones come cheap.\n"
                     + "The ring beside your resources shows how close the next one is.",
                Grant = Cost.Of(supplies: 600, iron: 400), GrantLabel = "a war chest",
                Done = (t, em, f) => t._sawCurseKill,
            },
            new Step
            {
                Chapter = Ch6,
                Title = "Destroy a curse node",
                OnSuggest = (t, em, f) => t.PingNearestCurseNode(em, f),
                Body = "Bring a real army — the garrison defends it. Raze the node and "
                     + "you are paid <b>a full Religion Point</b>, and the territory "
                     + "<b>unlocks</b>: stand on it and it is yours to claim.\n"
                     + "The curse is never gone for good. With no node left, it raises a new "
                     + "one somewhere after a few minutes.",
                Grant = Cost.Of(supplies: 800, iron: 600), GrantLabel = "a siege budget",
                Done = (t, em, f) => t._sawCurseNodeRazed,
            },

            // ── 7. Religion ────────────────────────────────────────────────
            new Step
            {
                Chapter = Ch7,
                Title = "Raise the Temple of Ridan",
                Body = "Place the <b>Temple of Ridan</b>: <b>1 Religion Point</b> plus "
                     + "200 supplies and 100 iron.\n"
                     + "It trains the <b>Litharch</b>, adds <b>+50 %</b> to every curse kill "
                     + "and trickles religion on its own. Its <b>Tithe</b> — click your "
                     + "Religion Points — buys an RP for resources, dearer each time.",
                Grant = Cost.Of(supplies: 200, iron: 100), RpGrant = 1,
                GrantLabel = "Temple materials and a Religion Point",
                Done = (t, em, f) => t.HasCompletedTemple(em, f),
            },
            new Step
            {
                Chapter = Ch7,
                Title = "Adopt a sect",
                Body = "The <b>religion panel</b> on the right shows your chapel slots. A "
                     + "chapel adopts a sect: <b>2 RP</b> for a sect of your own culture, "
                     + "<b>3 RP</b> for any other — and before you age up, every sect is "
                     + "3.\nIt grants the sect's passive, its research and its first power.",
                Grant = Cost.Of(supplies: 800, iron: 600, veilstone: 400), RpGrant = 3,
                GrantLabel = "chapel materials and 3 Religion Points",
                Done = (t, em, f) => t.AdoptedSect(em, f) != null,
            },
            new Step
            {
                Chapter = Ch7,
                Title = "Cast a sect power",
                Body = "Your sect's slot carries its cells: <b>P</b> is its always-on "
                     + "passive, the numbered cells are its powers.\n"
                     + "Click a lit one and pick a target on the map. The <b>+</b> cells "
                     + "unlock the other powers, and the chapel's level — bought with RP — "
                     + "is the level of every power it has.",
                Done = (t, em, f) => t.AnySectPowerOnCooldown(em, f),
            },
            new Step
            {
                Chapter = Ch7,
                Title = "Recruit a sect hero",
                Body = "Every sect has <b>one hero</b>, recruited at its chapel for "
                     + "<b>1 RP</b> plus its price. Heroes gain levels by fighting.\n"
                     + "If yours falls, recruit it again — a revival costs resources, never "
                     + "Religion Points.",
                Grant = Cost.Of(supplies: 400, iron: 300, veilstone: 150), RpGrant = 1,
                GrantLabel = "a hero's stipend and a Religion Point",
                Done = (t, em, f) => t.AnySectHeroRecruited(em, f),
            },

            // ── 8. Victory ─────────────────────────────────────────────────
            new Step
            {
                Chapter = Ch8,
                Title = "The Shardroot, and the last one standing",
                NoteSeconds = 16f,
                Body = "Sooner or later a curse creature rides out carrying the "
                     + "<b>Shardroot</b>. Kill it and the artifact is yours to carry and "
                     + "store — and from that moment <b>the whole curse hunts you</b>, "
                     + "bigger and faster, and ignores everyone else.\n"
                     + "There is one way to win: <b>be the last player standing</b>. A "
                     + "faction is out when it has no Fortress, no military building and "
                     + "no Worker left.",
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

        // Latched observations — each is a moment that lasts less than a step
        // (a selection, a queue that runs down, a death animation), polled
        // every tick so it is not missed between two 4 Hz checks.
        /// <summary>Two or more owned workers were selected at once. Not
        /// "box-selected" — box versus shift-click is not reliably
        /// distinguishable, and the lesson is the multi-selection either way.</summary>
        private bool _sawMultiWorkerSelection;
        /// <summary>An owned unit carried a queued (shift) order.</summary>
        private bool _sawQueuedOrders;
        /// <summary>A curse creature died to this player's last hit.</summary>
        private bool _sawCurseKill;
        /// <summary>A curse node collapsed to this player's last hit.</summary>
        private bool _sawCurseNodeRazed;

        // Baselines taken ONCE, at the first tick with a live bank. "Did this
        // number go up" is measured against the start of the MATCH, not the
        // start of the instruction, so a player who trained before being asked
        // to has already satisfied the step.
        private bool _baselined;
        private int _unitsAtMatchStart;

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
        private static readonly ComponentType[] MineQueryTypes =
        {
            ComponentType.ReadOnly<MineTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        private static readonly ComponentType[] VeilstoneMineQueryTypes =
        {
            ComponentType.ReadOnly<VeilstoneMineTag>(),
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
        private static readonly ComponentType[] MilitaryQueryTypes =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Target>(),
            ComponentType.Exclude<CanBuild>(),
        };
        private static readonly ComponentType[] QueuedQueryTypes =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<QueuedCommand>(),
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
        private static readonly ComponentType[] CurseNodeQueryTypes =
        {
            ComponentType.ReadOnly<SmallNodeTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<BuildingCollapseState>(),
        };
        // Enableable components match only while ENABLED, so these two see a
        // death / collapse in progress that somebody's last hit is recorded on.
        private static readonly ComponentType[] DyingCurseUnitQueryTypes =
        {
            ComponentType.ReadOnly<BorderUnitTag>(),
            ComponentType.ReadOnly<DeathAnimationState>(),
            ComponentType.ReadOnly<LastDamagedByFaction>(),
        };
        private static readonly ComponentType[] RazedCurseNodeQueryTypes =
        {
            ComponentType.ReadOnly<SmallNodeTag>(),
            ComponentType.ReadOnly<BuildingCollapseState>(),
            ComponentType.ReadOnly<LastDamagedByFaction>(),
        };

        private CachedEntityQuery _bankQuery, _hutQuery, _unitQuery,
                                  _gathererQuery, _mineQuery, _veilstoneMineQuery,
                                  _barracksQuery, _spearmanQuery, _militaryQuery, _queuedQuery,
                                  _hallQuery, _templeQuery, _curseNodeQuery,
                                  _dyingCurseQuery, _razedNodeQuery;

        // ── Setup ──────────────────────────────────────────────────────────

        private void Awake()
        {
            if (!GameSettings.TutorialActive) { enabled = false; return; }
            _done = new bool[Steps.Length];
            _paid = new bool[Steps.Length];
            _suggested = new bool[Steps.Length];
            Build();
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
                if (!Bank(em, faction, out _)) return;
                _baselined = true;
                _unitsAtMatchStart = CountOwned(em, faction, UnitQueryTypes, ref _unitQuery);
                Suggest(FirstUnfinished(), em, faction);
            }

            // Always-on observers: things the player may do at any point, whose
            // moment would otherwise be missed between ticks or steps.
            ObserveSelection(em, faction);
            ObserveQueuedOrders(em, faction);
            ObserveCurseKills(em, faction);

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
        /// setup, both exactly once.</summary>
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
        /// player who skips ahead to religion is not left without RP.</summary>
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
            bool paid = false;
            if (!step.Grant.IsZero && FactionEconomy.Add(em, faction, step.Grant)) paid = true;
            if (step.RpGrant > 0)
            {
                FactionReligionPointsHelper.Refund(em, faction, step.RpGrant);
                paid = true;
            }
            if (!paid) return;
            PlayerNotificationSystem.Notify(
                string.Format(Loc.T("Tutorial: granted {0}."), Loc.T(step.GrantLabel)));
        }

        /// <summary>
        /// Ping the curse node nearest this player's Fortress on the minimap.
        /// Presentation only. A map where the curse holds no node yet (it
        /// re-seeds after a few minutes) gets a notice instead.
        /// </summary>
        private void PingNearestCurseNode(EntityManager em, Faction faction)
        {
            Entity capital = FindCapital(em, faction);
            float3 origin = capital != Entity.Null && em.HasComponent<LocalTransform>(capital)
                ? em.GetComponentData<LocalTransform>(capital).Position : float3.zero;

            var q = _curseNodeQuery.Get(em, CurseNodeQueryTypes);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xforms = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float bestSq = float.MaxValue;
            float3 best = default;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value != Faction.Border) continue;
                float d2 = math.distancesq(origin.xz, xforms[i].Position.xz);
                if (d2 >= bestSq) continue;
                bestSq = d2;
                best = xforms[i].Position;
            }

            if (bestSq == float.MaxValue)
            {
                PlayerNotificationSystem.Notify(
                    Loc.T("Tutorial: the curse holds no node right now — it will raise one soon."));
                return;
            }
            MinimapPings.Post(best, MinimapPings.Curse, 20f);
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

        /// <summary>Latch "an owned unit is carrying queued orders". A short
        /// queue runs down in seconds, so it is watched, not asked for.</summary>
        private void ObserveQueuedOrders(EntityManager em, Faction faction)
        {
            if (_sawQueuedOrders) return;
            var q = _queuedQuery.Get(em, QueuedQueryTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (tags[i].Value != faction) continue;
                if (em.GetBuffer<QueuedCommand>(ents[i], true).Length > 0)
                { _sawQueuedOrders = true; return; }
            }
        }

        /// <summary>
        /// Latch a curse creature, and a curse node, dying to this player's
        /// last hit — the same record CurseKillReligionSystem pays from. The
        /// death animation and the building collapse both outlast a 4 Hz tick.
        /// </summary>
        private void ObserveCurseKills(EntityManager em, Faction faction)
        {
            if (!_sawCurseKill)
            {
                var q = _dyingCurseQuery.Get(em, DyingCurseUnitQueryTypes);
                using var hits = q.ToComponentDataArray<LastDamagedByFaction>(Allocator.Temp);
                for (int i = 0; i < hits.Length; i++)
                    if (hits[i].Value == faction) { _sawCurseKill = true; break; }
            }
            if (!_sawCurseNodeRazed)
            {
                var q = _razedNodeQuery.Get(em, RazedCurseNodeQueryTypes);
                using var hits = q.ToComponentDataArray<LastDamagedByFaction>(Allocator.Temp);
                for (int i = 0; i < hits.Length; i++)
                    if (hits[i].Value == faction) { _sawCurseNodeRazed = true; break; }
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
        /// Territories this faction has CLAIMED (Territory_Claims.md §2 — a
        /// claim in progress does not count). The Fortress claims home from
        /// the first tick, so this is 1 at start and 2 the moment an army's
        /// meter reaches 100 somewhere else.
        /// </summary>
        private int TerritoriesHeld(Faction faction)
            => TerritoryOwnership.Ready ? TerritoryOwnership.CountOf(faction) : 0;

        /// <summary>Completed Gatherer's Huts this faction owns.</summary>
        private int HutsBuilt(EntityManager em, Faction faction)
            => CountOwned(em, faction, GathererHutQueryTypes, ref _gathererQuery);

        /// <summary>Completed ore extractors of any kind — the one Mine button
        /// raises whichever the node asks for.</summary>
        private int MinesBuilt(EntityManager em, Faction faction)
            => CountOwned(em, faction, MineQueryTypes, ref _mineQuery)
             + CountOwned(em, faction, VeilstoneMineQueryTypes, ref _veilstoneMineQuery);

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

        /// <summary>The culture the landmark gave this faction, read off its
        /// capital (the Fortress carries HallTag and FactionProgress).</summary>
        private byte Culture(EntityManager em, Faction faction)
        {
            var q = _hallQuery.Get(em, HallQueryTypes);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var progress = q.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
                if (tags[i].Value == faction) return progress[i].Culture;
            return Cultures.None;
        }

        private Entity FindCapital(EntityManager em, Faction faction)
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

        /// <summary>Any sect's hero recruited at least once — the latch
        /// SectHeroes.OnSpawned sets (Religion.md §4).</summary>
        private bool AnySectHeroRecruited(EntityManager em, Faction faction)
        {
            if (!SectQuery.TryGetAdoptionState(em, faction, out var state)) return false;
            for (int i = 0; i < SectConfig.SectCount; i++)
                if (state.Get(i).HeroRecruited != 0) return true;
            return false;
        }
    }
}
