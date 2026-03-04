// Assets/Scripts/CrystalCurse/CrystalConfig.cs
using UnityEngine;

[CreateAssetMenu(menuName="CrystalCurse/Config")]
public class CrystalConfig : ScriptableObject
{
    [Header("Faction")]
    //public Faction CrystalFaction = Faction.White; // change if you added Faction.Crystal

    [Header("Spread")]
    public float TickSeconds = 1.0f;

    [Header("XP Curve")]
    public int L1to2 = 600;
    public int L2to3 = 1200;
    public int L3to4 = 2400;
    public int L4to5 = 3600;

    [Header("Harass Intervals (sec) by Level 1..5")]
    public float[] HarassIntervals = new float[] { 60, 60, 50, 40, 40 };

    [Header("Rally Cooldown (sec)")]  
    public float RallyCd = 90;

    [Header("Storm (L3 Active)")]
    public float StormCd = 120;
    public float StormDuration = 10;
    public float StormRadius = 12;
    public float StormTickDmg = 12;
    public float StormTicksPerSec = 5;

    [Header("Embrace (L5 Ultimate)")]
    public float EmbraceCd = 240;
    public float EmbraceDuration = 25;
    public float NonCrystalDps = 8;

    [Header("Global Buff on Cursed (L5)")]
    public float DmgPct = 20;
    public float DrPct = 20;
    public float MovePct = 20;
}
