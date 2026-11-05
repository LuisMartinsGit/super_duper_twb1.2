// SimpleAISystem.Waste.cs
// A careless tier buys buildings it has no use for (docs/Design/Game_AI.md § 2a),
// so its money leaves through bad decisions instead of piling up. They pay in
// full and tie up its workers. Partial of SimpleAISystem.cs.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        /// <summary>A roster table, not tuning: the SO ids a careless tier overbuys.</summary>
        private static readonly string[] WasteIds = { "Hut", "Barracks", "ArcheryRange" };

        private void TickWaste(EntityManager em, Faction faction)
        {
            if (!HasAgedUp(em, faction)) return;
            if (!AIPacing.WasteDue(em, faction, out int wasted)) return;
            uint roll = math.hash(new int2((int)faction, wasted));
            string id = WasteIds[roll % (uint)WasteIds.Length];
            if (!TryBuildBuilding(em, faction, id)) return;
            AIPacing.NoteWaste(em, faction);
            AILogger.Log(faction, "WASTE", $"bought a needless {id} ({wasted + 1})");
        }
    }
}
