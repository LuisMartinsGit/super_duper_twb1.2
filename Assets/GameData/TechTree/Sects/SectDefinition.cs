// SectDefinition.cs
// ScriptableObject describing one sect's static design data: cluster, lever
// titles + descriptions per level. Effect *behaviour* is dispatched by
// SectEffectDispatcher (Phase 2) which keys off SectId + LeverKind + Level —
// this asset only carries display + parameter data.
//
// One asset per sect, 12 total. Loaded by SectDatabase at startup.
//
// Phase 1 (task-063): asset shape defined; per-sect parameter shapes filled
// in as Phase 2 wires each lever.

using System;
using UnityEngine;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Static design data for a single sect. Treated as immutable at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "TheWaningBorder/Sect Definition", fileName = "Sect_New")]
    public sealed class SectDefinition : ScriptableObject
    {
        public string SectId;

        public SectCluster Cluster;

        public string DisplayName;

        public string Tagline;

        [TextArea(1, 3)]
        public string Identity;

        [TextArea(2, 6)]
        public string LoreNote;

        // The four levers. Each is a 3-level array of SectLevelData.
        public SectLeverData Passive     = new SectLeverData(SectLeverKind.Passive);
        public SectLeverData Building    = new SectLeverData(SectLeverKind.Building);
        public SectLeverData Unit        = new SectLeverData(SectLeverKind.Unit);
        public SectLeverData ActivePower = new SectLeverData(SectLeverKind.ActivePower);

        /// <summary>Returns the lever data block for the given lever kind.</summary>
        public SectLeverData GetLever(SectLeverKind kind)
        {
            return kind switch
            {
                SectLeverKind.Passive     => Passive,
                SectLeverKind.Building    => Building,
                SectLeverKind.Unit        => Unit,
                SectLeverKind.ActivePower => ActivePower,
                _                         => null,
            };
        }
    }

    /// <summary>
    /// One of the four lever channels (Passive/Building/Unit/ActivePower) on a sect.
    /// Carries a name + 3 levels of data. Levels are 1-indexed in player-facing
    /// language but stored 0-indexed here (Levels[0] = Lv I).
    /// </summary>
    [Serializable]
    public sealed class SectLeverData
    {
        public SectLeverKind Kind;

        public string LeverName;

        public SectLevelData[] Levels = new SectLevelData[3];

        public SectLeverData() { }

        public SectLeverData(SectLeverKind kind)
        {
            Kind = kind;
            Levels = new SectLevelData[3];
            for (int i = 0; i < 3; i++) Levels[i] = new SectLevelData();
        }

        /// <summary>Lookup level data by 1-indexed level (1/2/3). Null on out-of-range.</summary>
        public SectLevelData At(int level)
        {
            if (level < 1 || level > 3) return null;
            return Levels[level - 1];
        }
    }

    /// <summary>
    /// Per-level design data for a single lever. Carries the descriptive copy
    /// shown in the UI plus a free-form parameter blob keyed by string —
    /// concrete typed access happens via the corresponding effect dispatcher
    /// in Phase 2 (the dispatcher knows the schema for each sect/lever pair).
    /// </summary>
    [Serializable]
    public sealed class SectLevelData
    {
        [TextArea(2, 6)]
        public string Description;

        public float CooldownSeconds;

        public float DurationSeconds;

        // Phase 2 will add typed parameter fields here as each lever is wired
        // (e.g. AurasBuilding/AuraRadius, PassiveBonusPercent, etc). Keeping the
        // shape minimal in Phase 1 keeps the SOs readable while we figure out
        // the right schema lever-by-lever.
        public SectParam[] Parameters = System.Array.Empty<SectParam>();

        /// <summary>Read a parameter by key, defaulting to <paramref name="fallback"/> if missing.</summary>
        public float Param(string key, float fallback = 0f)
        {
            if (Parameters == null) return fallback;
            for (int i = 0; i < Parameters.Length; i++)
                if (Parameters[i].Key == key) return Parameters[i].Value;
            return fallback;
        }
    }

    /// <summary>Single key-value entry in a SectLevelData parameter list.</summary>
    [Serializable]
    public struct SectParam
    {
        public string Key;
        public float  Value;
    }
}
