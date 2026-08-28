// ResourceComponents.cs
// Components for resource nodes (iron mines, veilstone deposits, etc.)
// All ECS components live in global namespace per project convention.

using Unity.Entities;

/// <summary>
/// Shared questions about resource nodes, so the "is this a node?" tag trio
/// (iron / veilstone / veilsteel) has ONE definition. It was open-coded in the
/// training rally hand-off, the click router and the rally overlay, which is
/// exactly the shape that goes stale the day a fourth resource lands.
/// </summary>
public static class ResourceNodeQuery
{
    /// <summary>True for any node a worker can be sent to gather.</summary>
    public static bool IsGatherable(EntityManager em, Entity e)
    {
        if (e == Entity.Null || !em.Exists(e)) return false;
        return em.HasComponent<IronMineTag>(e)
            || em.HasComponent<VeilstoneOutcroppingTag>(e)
            || em.HasComponent<VeilsteelDepositTag>(e);
    }

    /// <summary>
    /// The centre of the build cell a node occupies. Nodes are snapped to
    /// their cell at creation, so this normally just reads the transform —
    /// it is re-derived rather than trusted so a rally marker lands dead
    /// centre on the cell even if a node was ever placed unsnapped.
    /// </summary>
    public static bool TryGetCellCentre(EntityManager em, Entity node, out Unity.Mathematics.float3 centre)
    {
        centre = default;
        if (!IsGatherable(em, node)) return false;
        if (!em.HasComponent<Unity.Transforms.LocalTransform>(node)) return false;

        var p = em.GetComponentData<Unity.Transforms.LocalTransform>(node).Position;
        var c = BuildGrid.CellCentre(BuildGrid.WorldToCell(p));
        centre = new Unity.Mathematics.float3(c.x, p.y, c.y);
        return true;
    }
}

/// <summary>
/// A SUPPLY NODE — the ground a Gatherer's Hut must be built on
/// (docs/Design/Regions.md §4). No amount, no depletion: it is a place, not a
/// resource, and it pays nothing. The hut standing on it is what pays.
///
/// Deliberately absent from ResourceNodeQuery.IsGatherable — nothing gathers
/// from it, and answering true there would enrol it in the rally hand-off, the
/// click router and the rally overlay by accident.
/// </summary>
public struct SupplyNodeTag : IComponentData { }

/// <summary>
/// HOW MUCH IS LEFT IN THE GROUND, for territory income.
///
/// Territory yield used to be a flat 75/min per node, forever. A node that
/// never runs down makes the opening map allocation the whole economy: once
/// the regions are divided, income is fixed for the rest of the match and
/// there is no reason to take more ground or to invest in what you hold. It
/// also let banks run away — one logged AI reached 15,242 veilstone.
///
/// Draining it turns holdings into a clock. Yield falls as the reserve
/// empties, so a territory is worth most when freshly taken, and the answer to
/// a thinning node is to expand or to build/upgrade the extraction building on
/// it — which is exactly the pressure the design asks for.
///
/// EXTRACTION DRAWS FASTER THAN IT PAYS BACK IN THE LONG RUN: a mine adds
/// yield per level, and every point of yield comes out of this reserve. That
/// is the trade — upgrading is a decision to spend the node sooner.
///
/// Separate from IronDepositState (worker mining, now retired by
/// Regions.md §4) on purpose: this is the TERRITORY tick's ledger and it
/// applies to every node kind including veilstone and veilsteel.
/// </summary>
public struct NodeReserve : IComponentData
{
    /// <summary>Units of resource this node can still pay out.</summary>
    public float Remaining;
    /// <summary>What it held when untouched, so yield can scale by fraction
    /// rather than by an absolute that means nothing on its own.</summary>
    public float Initial;
}

/// <summary>Marker tag for iron mine/deposit entities.</summary>
public struct IronMineTag : IComponentData { }

public struct IronDepositState : IComponentData
{
    public int RemainingIron;
    /// <summary>
    /// Bootstrap-time deposit capacity. Set once by IronDepositBootstrap and
    /// never mutated. UI uses this as the denominator for "Remaining: N / Max"
    /// readouts and the world-space depletion bar (task-108). Pre-task-108
    /// saves load with InitialIron == 0; callers must fall back to
    /// RemainingIron in that case.
    /// </summary>
    public int InitialIron;
    public byte Depleted;  // 1 = exhausted
}

/// <summary>
/// Marker tag for the Veilsteel "Sharp Crystals" map resource node.
/// A veilsteel deposit reuses <see cref="IronDepositState"/> for its
/// remaining/initial amounts — it behaves exactly like an iron deposit
/// (fixed amount, mined until gone), only the resource credited to the
/// faction bank differs. Spawned by VeilsteelDepositBootstrap from
/// VeilsteelDepositMarker scene markers as a SINGLE node holding 1500 units.
/// </summary>
public struct VeilsteelDepositTag : IComponentData { }