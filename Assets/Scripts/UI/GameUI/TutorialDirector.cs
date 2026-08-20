// TutorialDirector.cs
// The tutorial scenario's coach: a chaptered CHECKLIST that watches real game
// state and ticks itself off as the player plays.
//
// It spans the WHOLE game, first camera pan to first purified well:
//   1  Controls
//   2  Workers and resources
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
//     counts; nothing has to be repeated to satisfy the coach.
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
// real opening changes. Every step reads the same components the game itself
// reads (MinerState.GatheringResource, GathererHutYield, Target, TempleLevel,
// TempleChapelSlot, SmallNodeTag, BorderNodeState), so the only way to fail is
// to change the game, which is exactly when the tutorial SHOULD break.
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
//   * The SCRIPTED CORRUPTION — the curse chapter cannot wait for the player
//     to mine a distant patch dry, and the home patch is corruption-immune
//     under the Hall's hearth by design (Curse_And_Shardroot.md §2.7
//     amendment). So the chapter queues a real PendingCorruption — the exact
//     path VeilstoneMiningSystem uses — 45-55 m out, beyond the hearth so
//     BlightPocketSystem cannot starve it before the player fights it. In
//     fiction it is a ritual that failed somewhere on the map, which is the
//     honest explanation: a broken channel is what wakes the curse.
//   * CREEP SPEED-UP — VeilFieldSystem.TutorialCreepMultiplier, raised while
//     the curse chapter is live so the crust visibly advances inside the step
//     rather than after a 190-320 s dormant window. Restored afterwards.
//
// Steps are checked at 4 Hz, never per frame, and finished ones stop being
// evaluated. Apart from the four helps above, nothing here writes to the
// simulation.
// Location: Assets/Scripts/UI/GameUI/TutorialDirector.cs

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
using TheWaningBorder.UI.HUD;

namespace TheWaningBorder.UI.GameUI
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

        /// <summary>Where the scripted corruption lands, relative to the Hall.
        /// Must clear HallHearthRadius (34 m) or the hearth starves the pocket
        /// at 20 dps before the player can bring an army to it.</summary>
        private const float ScriptedCorruptionDistance = 50f;

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
            /// caller, so a transient truth is enough.</summary>
            public Check Done;
        }

        private const string Ch1 = "1. Controls";
        private const string Ch2 = "2. Workers & resources";
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
                     + "Find your <b>Hall</b> — the big building your warband starts around.",
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
                Chapter = Ch2,
                Title = "Mine veilstone",
                Body = "Right-click a <b>veilstone outcropping</b> with a worker selected.\n"
                     + "Mined resources go straight to your bank — no hauling, no drop-off "
                     + "building.\n<b>Veilstone is the one resource the curse controls.</b> "
                     + "The patch by your base is what the world had spare; everything after "
                     + "it has to be taken.",
                Done = (t, em, f) => t.Bank(em, f, out var bank)
                                     && bank.Veilstone > t._veilstoneAtMatchStart,
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
                Body = "Select your <b>Hall</b> and click <b>Worker</b> in the actions panel.\n"
                     + "The queue strip above the panel shows what is in production — "
                     + "<b>right-click a queued chip</b> to cancel it and get the cost back.",
                Done = (t, em, f) => t.CountOwned(em, f, UnitQueryTypes, ref t._unitQuery)
                                     > t._unitsAtMatchStart,
            },
            new Step
            {
                Chapter = Ch2,
                Title = "Split your economy",
                Body = "Put <b>three workers on veilstone and three on iron</b>.\n"
                     + "They feed different things: iron buys soldiers and buildings, "
                     + "veilstone buys everything the Temple and the sects need.",
                Done = (t, em, f) => t.MinersOn(em, f, ResourceVeilstone) >= 3
                                     && t.MinersOn(em, f, ResourceIron) >= 3,
            },
            new Step
            {
                Chapter = Ch2,
                Title = "Place a good Gatherer's Hut",
                Body = "A Gatherer's Hut earns from the open ground inside its circle. While "
                     + "placing it, the preview shows a <b>yield percentage</b> — blocked "
                     + "ground, other huts' circles and the map edge all eat into it.\n"
                     + "Find a spot reading <b>90% or better</b>.",
                Grant = Cost.Of(supplies: 400), GrantLabel = "a survey fund",
                Done = (t, em, f) => t.BestHutYield(em, f) >= 0.90f,
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
                Body = "Pick one of <b>Shrine of Ahridan</b>, <b>Vault of Almiérra</b> or "
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
                     + "This is also what happens when a patch runs dry: <b>the last node of "
                     + "any patch always corrupts.</b> Your home patch is safe — your Hall "
                     + "projects a suppression ring, and the curse can never wake inside "
                     + "your influence. It is the patches you have to leave home for that "
                     + "bite.",
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
                     + "Leave it and it keeps feeding — the haze taxes anyone mining there, "
                     + "and crusted ground costs you: a few seconds' grace, then damage that "
                     + "scales with depth, plus slower movement and worse stats.\n"
                     + "The other way is to <b>starve</b> it. Push influence over it — a "
                     + "tower, or an upgraded building, since every level widens a "
                     + "building's influence — and it dies on its own.",
                Done = (t, em, f) => t.ScriptedCurseNodeBroken(em),
            },

            // ── 7. The wells ───────────────────────────────────────────────
            new Step
            {
                Chapter = Ch7,
                Title = "Train a Holy Scholar",
                Body = "The giant veilstone formations are the <b>wells</b> — selecting one "
                     + "reads <i>Veilstone Hive</i>. They are the largest income on the map "
                     + "and the only way the match is won.\n"
                     + "Every well is <b>dormant</b> until a player reaches for it. That is "
                     + "why the map was quiet.\n"
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
        // start of the instruction, so a player who mined before being asked
        // to has already satisfied the step.
        private bool _baselined;
        private int _veilstoneAtMatchStart;
        private int _unitsAtMatchStart;

        // Curse chapter bookkeeping.
        private bool _curseChapterBegun;
        private float2 _scriptedCorruptionAt;
        private Entity _scriptedCurseNode = Entity.Null;
        private bool _scriptedNodeSeen;

        private GameObject _root;
        private TMP_Text _eyebrow, _title, _body;

        private const byte ResourceIron = 0;
        private const byte ResourceVeilstone = 1;

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
        private static readonly ComponentType[] MinerQueryTypes =
        {
            ComponentType.ReadOnly<MinerState>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] GathererHutQueryTypes =
        {
            ComponentType.ReadOnly<GathererHutYield>(),
            ComponentType.ReadOnly<FactionTag>(),
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

        private CachedEntityQuery _bankQuery, _hutQuery, _unitQuery, _minerQuery,
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
                _veilstoneAtMatchStart = start.Veilstone;
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
                if (!Steps[i].Done(this, em, faction)) continue;

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
        /// VeilstoneMiningSystem writes — so the player gets the ordinary
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

            float3 at = origin + new float3(ScriptedCorruptionDistance, 0f, 0f);
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
                Loc.T("A ritual has failed — the curse is waking east of your Hall!"));
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
            float now = TheWaningBorder.Input.CameraController.ZoomNormalized;
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

        /// <summary>Owned workers currently assigned to a resource — moving to
        /// a deposit counts, so the step ticks as soon as they are sent rather
        /// than when the first swing lands.</summary>
        private int MinersOn(EntityManager em, Faction faction, byte resource)
        {
            var q = _minerQuery.Get(em, MinerQueryTypes);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var miners = q.ToComponentDataArray<MinerState>(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value != faction) continue;
                if (miners[i].State == MinerWorkState.Idle) continue;
                if (miners[i].GatheringResource != resource) continue;
                count++;
            }
            return count;
        }

        private float BestHutYield(EntityManager em, Faction faction)
        {
            var q = _gathererQuery.Get(em, GathererHutQueryTypes);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var yields = q.ToComponentDataArray<GathererHutYield>(Allocator.Temp);
            float best = 0f;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i].Value == faction && yields[i].Ratio > best) best = yields[i].Ratio;
            return best;
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
