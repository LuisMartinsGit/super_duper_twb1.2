// Feraldis burns the curse out of its own attack lanes — and gets paid for it.
// Canon: docs/Design/Age_1_Feraldis.md — "The Warpath".
//
// The two cultures relate to the crust in OPPOSITE ways, and that asymmetry
// is the design:
//
//   ALANTHOR turtles. The crust is a free outer wall — it guards their
//   flanks, funnels attackers into their towers, and costs them nothing.
//   Curse growth partly HELPS them.
//
//   FERALDIS attacks. That same crust is a moat around every target they
//   want to reach, and their whole kit — raiders, chariots, a 3.2-speed
//   Corruptor that must physically walk to a well — dies crossing it. They
//   cannot out-turtle the curse. So they BURN THROUGH IT: crust dies under a
//   Feraldis advance, an attack carves its own corridor, and the corridor
//   holds only while the army does.
//
// AND THE BURNING PAYS. Every cell of crust actually destroyed yields
// veilstone (VeilstonePerCellCleared). This is Curse_And_Shardroot §2.6's
// standing promise — "Feraldis is the ONLY culture that earns veilstone FROM
// the curse" — finally delivered, on destruction rather than on mining. It
// is self-limiting: a cell can only be cleared once until the veil regrows,
// so income tracks how much curse the faction is genuinely deleting rather
// than how long its army has been standing still.
//
// War Totems burn far harder than soldiers and pay the same way — that is
// what lets a planted totem hold its patch instead of being swallowed.
//
// VeilField.Saturation is sim state on the main thread, so this is a
// SystemBase on a slow pulse. Deterministic: it only ever subtracts a fixed
// amount from cells inside a radius, and counts the crossings.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisWarpathSystem : SystemBase
    {
        private float _tick;

        /// <summary>Fractional veilstone owed per faction — banked as whole
        /// units so a slow clear rate is never rounded away to nothing.</summary>
        private readonly float[] _purse = new float[8];

        protected override void OnCreate()
        {
            RequireForUpdate<VeilField>();
        }

        protected override void OnUpdate()
        {
            _tick -= SystemAPI.Time.DeltaTime;
            if (_tick > 0f) return;
            float slice = WarpathInterval;
            _tick = WarpathInterval;

            var field = SystemAPI.GetSingleton<VeilField>();
            if (field.Initialised == 0 || !field.Saturation.IsCreated) return;

            var em = EntityManager;
            var cleared = new int[8];

            // --- Marching soldiers burn a narrow lane. ---
            // Keyed on FeraldisSoldier.Is, NOT on FeraldisUnitTag: a Feraldis
            // player's Spearmen and Archers come from the shared roster and
            // carry no Feraldis tag, so tag-keying left most of the army
            // unable to clear anything.
            float marchBurn = WarpathBurnPerSecond * slice;
            foreach (var (xf, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                var f = faction.ValueRO.Value;
                if (!FeraldisSoldier.Is(em, entity, f)) continue;
                int fi = (int)f;
                if (fi < 0 || fi >= 8) continue;
                cleared[fi] += Burn(ref field, xf.ValueRO.Position, WarpathBurnRadius, marchBurn);
            }

            // --- War Totems keep their own patch clear, much harder. ---
            float totemBurn = TotemBurnPerSecond * slice;
            foreach (var (xf, faction) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<WarTotemTag>())
            {
                var f = faction.ValueRO.Value;
                if (CultureConfig.GetCompletedCulture(em, f) != Cultures.Feraldis) continue;
                int fi = (int)f;
                if (fi < 0 || fi >= 8) continue;
                cleared[fi] += Burn(ref field, xf.ValueRO.Position, TotemBurnRadius, totemBurn);
            }

            // --- Pay out. ---
            for (int f = 0; f < 8; f++)
            {
                if (cleared[f] > 0)
                    _purse[f] += cleared[f] * VeilstonePerCellCleared;

                int whole = (int)_purse[f];
                if (whole <= 0) continue;
                _purse[f] -= whole;
                FactionEconomy.Add(em, (Faction)f, new Cost { Veilstone = whole });
            }
        }

        /// <summary>
        /// Subtract saturation from every cell inside a world-space disc,
        /// clamped at zero, and return how many cells this burn actually
        /// DESTROYED — i.e. dropped from crust down below the crust
        /// threshold. Only real destruction pays; grinding already-clear
        /// ground earns nothing.
        /// </summary>
        private static int Burn(ref VeilField field, float3 centre, float radius, float amount)
        {
            int cx = (int)math.floor((centre.x - field.Origin.x) / field.CellSize);
            int cz = (int)math.floor((centre.z - field.Origin.y) / field.CellSize);
            int r = (int)math.ceil(radius / field.CellSize);
            float r2 = radius * radius;
            byte cut = (byte)math.clamp((int)amount, 0, 255);
            if (cut == 0) return 0;

            int destroyed = 0;
            for (int z = cz - r; z <= cz + r; z++)
            {
                if (z < 0 || z >= field.Height) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= field.Width) continue;
                    float dx = (x - cx) * field.CellSize;
                    float dz = (z - cz) * field.CellSize;
                    if (dx * dx + dz * dz > r2) continue;

                    int idx = field.Index(x, z);
                    byte s = field.Saturation[idx];
                    if (s == 0) continue;

                    byte after = s > cut ? (byte)(s - cut) : (byte)0;
                    field.Saturation[idx] = after;

                    if (s >= VeilField.CrustThreshold && after < VeilField.CrustThreshold)
                        destroyed++;
                }
            }
            return destroyed;
        }
    }
}
