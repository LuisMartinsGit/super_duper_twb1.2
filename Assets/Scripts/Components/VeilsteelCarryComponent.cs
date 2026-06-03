// File: Assets/Scripts/Core/Components/VeilsteelCarryComponent.cs
// task-070: Feraldis carry-on-kill resource. Military units (Feraldis-cultured
// only) accumulate Veilsteel shavings from kills, up to 5 slots. Each shaving
// grants +2 % attack once the Feraldis_VeilsteelFrenzy tech is researched.

using Unity.Entities;

/// <summary>
/// Per-unit carry of Veilsteel shavings (Feraldis-only, max 5).
/// VeilsteelKillDropSystem adds and increments this on Feraldis military kills;
/// VeilsteelAttackBonusSystem reads it (post-research) to apply +2 %/shaving attack.
/// </summary>
public struct VeilsteelCarry : IComponentData
{
    /// <summary>Number of shavings carried (0-5).</summary>
    public byte Shavings;
}
