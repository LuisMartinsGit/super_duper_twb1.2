// VeilComponents.cs
// THE VEIL — the curse as a continuous, sprawling veilstone sheet
// (Curse & Shardroot canon §2.3, book canon: the world slowly turning to
// veilstone, pushing humanity back inch by inch).
//
// A coarse saturation grid covers the map. Wells are eruption points that
// feed it; saturation creeps outward cell-by-cell (cellular growth) and
// DECAYS wherever its nearest well is not Active — verbs visibly starve
// the sheet. Gameplay reads the grid for: movement/stat debuffs on crust,
// build blocking, iron swallowing, frontier crystallization (the mineable
// edge), and terrain/minimap visuals.
//
// Global namespace per project ECS-component convention.
// Location: Assets/Scripts/Components/VeilComponents.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Singleton saturation field. Allocated by VeilFieldSystem (Persistent),
/// disposed in its OnDestroy. Cell values 0..255.
/// </summary>
public struct VeilField : IComponentData
{
    public NativeArray<byte> Saturation;
    /// <summary>
    /// Per-cell regrow lockout, counted in CA pulses REMAINING. A break
    /// (see <see cref="VeilBreakRequest"/>) writes 0 to <see cref="Saturation"/>
    /// AND stamps a positive value here; the CA refuses to feed/grow a cell
    /// while its cooldown is non-zero, and decrements it one per pulse. When
    /// it reaches 0 the ordinary neighbour-spread rule refills the hole — so
    /// "regrow" needs no special-casing, it's just spread resuming.
    /// </summary>
    public NativeArray<byte> Cooldown;
    public int Width;
    public int Height;
    public float CellSize;
    public float2 Origin;
    public byte Initialised;

    /// <summary>Bumped by VeilFieldSystem every tick the saturation grid
    /// actually changes (growth substep, maintenance pulse, enclosure fill,
    /// or a drained break). Consumers that mirror the crust into other data
    /// (e.g. <c>VeilNavStampSystem</c> stamping impassable cells into the nav
    /// cost field) compare this to skip work on unchanged ticks without
    /// scanning the whole grid.</summary>
    public int Generation;

    /// <summary>Coverage as your design's 0..1 value (0 = clean, 1 = full
    /// crystal). Saturation is stored as a byte 0..255 for compactness; this
    /// is just the normalised read.</summary>
    public float Coverage01(int idx) => Saturation[idx] * (1f / 255f);

    /// <summary>At/above this the cell is CRUST: debuffs apply, building
    /// is blocked, the ground reads as veilstone.</summary>
    public const byte CrustThreshold = 80;
    /// <summary>Deep veil: stronger debuff; swallows iron deposits.</summary>
    public const byte DeepThreshold = 180;
    /// <summary>Visual paint threshold (terrain + minimap tint).</summary>
    public const byte PaintThreshold = 40;

    public int Index(int x, int z) => z * Width + x;

    public bool TryWorldToCell(float3 pos, out int x, out int z)
    {
        x = (int)math.floor((pos.x - Origin.x) / CellSize);
        z = (int)math.floor((pos.z - Origin.y) / CellSize);
        return x >= 0 && x < Width && z >= 0 && z < Height;
    }

    /// <summary>Saturation at a world position (0 outside the grid).</summary>
    public byte SaturationAt(float3 pos)
    {
        if (!Initialised.Equals((byte)1)) return 0;
        if (!TryWorldToCell(pos, out int x, out int z)) return 0;
        return Saturation[Index(x, z)];
    }
}

/// <summary>
/// Marks a unit whose BorderDebuff was applied by the Veil (standing on
/// crust) — so the veil system and the Suppression-aura system don't
/// fight over adding/removing the shared BorderDebuff component.
/// </summary>
public struct VeilDebuffTag : IComponentData { }

/// <summary>
/// Miner-infection accumulator (curse canon: neglect a villager digging at
/// the veil and it takes root in them). <see cref="Progress"/> counts seconds
/// of cumulative exposure to veil haze near the miner; it climbs while the
/// miner stands in haze and recovers while it's clear. When it reaches
/// <c>VeilCrustConstants.InfectionSeconds</c> the miner is consumed and a
/// hostile curse creature erupts in its place — its tier scaling with how
/// late in the match the eruption happens (Crystalling → Veilstinger →
/// Godsplinter). Added lazily by VeilFieldSystem the first time a miner is
/// exposed; never removed (it simply idles at 0 once the miner walks clear).
/// </summary>
public struct InfectionState : IComponentData
{
    public float Progress;
}

// NOTE: the Veil has NO deposit entities of any kind. Mining it is
// position-targeted (GatherVeilCommand + VeilMiningSystem): the miner digs
// at the closest crusted vertex of this grid and the field drains under
// the pick. The former VeilCrystalCluster harvest-anchor lattice was
// removed with that change.

/// <summary>
/// §2.5b exposure accumulator (VeilExposureSystem). Seconds of cumulative
/// crust exposure; climbs while the unit stands on crust, recovers (faster)
/// while clear. Damage-over-time applies only above
/// <c>VeilCrustConstants.ExposureGraceSeconds</c> — crossing a thin finger
/// is free, loitering is not. Added lazily on first exposure; never removed
/// (idles at 0 once clear).
/// </summary>
public struct ExposureState : IComponentData
{
    public float Seconds;
}

/// <summary>
/// Stamped by VeilExposureSystem on a unit its damage-over-time killed.
/// DeathSystem skips the blood splat for tagged deaths — the curse must
/// never farm its own blood-spawner loop (§2.5b loop damping); only real
/// combat feeds blood-curse births.
/// </summary>
public struct CurseKilledTag : IComponentData { }

/// <summary>
/// Marks a SmallNode — the small destructible crystal growth anchoring an
/// Age 0 blight pocket (§2.5b). Fed into the veil CA as an extra Active
/// feeder while alive; starved (SmallNodeStarveDps) while its cell is
/// suppressed by hearth/ward/influence. Death (any cause) collapses its
/// pocket: BlightPocketSystem stamps a field break and pays the residue.
/// </summary>
public struct SmallNodeTag : IComponentData { }

/// <summary>
/// A veilstone node that rolled corruption and is now TELEGRAPHING it
/// (2026-08-04): purple ping + notification fire immediately, the curse
/// node rises CorruptionTelegraphSeconds later — reaction window for the
/// player and the AI alike. Buffer on the blight-pocket registry entity.
/// </summary>
[InternalBufferCapacity(4)]
public struct PendingCorruption : IBufferElementData
{
    public Unity.Mathematics.float3 Pos;
    public double At; // sim time the SmallNode rises
}

/// <summary>
/// One blight pocket, tracked on the BlightPocketSystem singleton's buffer.
/// Registered by BlightPocketBootstrap at spawn; the system seeds the haze
/// disc once the VeilField exists, then watches the small node and fires the
/// collapse exactly once.
/// </summary>
[InternalBufferCapacity(8)]
public struct BlightPocket : IBufferElementData
{
    public Entity SmallNode;
    public float2 Center;
    public float Radius;
    /// <summary>0 = haze disc not yet stamped into the field.</summary>
    public byte Seeded;
    public byte Collapsed;
}

/// <summary>
/// A pending "break off a chunk of the frontier" write, queued on the field
/// entity's buffer and drained by VeilFieldSystem each frame. Every break is
/// just a field write — it clears <see cref="VeilField.Saturation"/> to 0 in a
/// world-space radius and stamps a regrow <see cref="VeilField.Cooldown"/>.
/// The crystals vanish because they are only a VIEW of the field; nothing
/// else needs to know a break happened.
///
/// Debug input (VeilFieldDebugOverlay) and, later, the player break command
/// both append here — one funnel, so the field stays the single writer.
/// </summary>
[InternalBufferCapacity(8)]
public struct VeilBreakRequest : IBufferElementData
{
    /// <summary>World-space centre (X,Z) of the break.</summary>
    public float2 Position;
    /// <summary>World-space radius (metres) cleared to 0.</summary>
    public float Radius;
}
