// RunaiBuildingBody.cs
// The shared body of the buildings the 2026-10-13 Runai redesign adds
// (docs/Design/Age_1_Runai.md §2, §4-5): every stat from the building SO,
// both the EntityManager and the EntityCommandBuffer path. Each building's own
// class (ThessarasCrossing, GrazingGrounds, Sanctuary) adds its tags on top.

using TheWaningBorder.Economy;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    internal static class RunaiBuildingBody
    {
        public static Entity Base<TCreator>(TCreator c, string buildingId, int pid, float3 p, Faction f,
                                            bool trainer, bool isBase = false)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Building(buildingId);
            var e = c.CreateEntity();
            c.AddComponent(e, new PresentationId { Id = pid });
            c.AddComponent(e, LocalTransform.FromPositionRotationScale(p, quaternion.identity, 1f));
            c.AddComponent(e, new FactionTag { Value = f });
            c.AddComponent(e, new BuildingTag { IsBase = (byte)(isBase ? 1 : 0) });
            c.AddComponent(e, new Health { Value = (int)def.hp, Max = (int)def.hp });
            c.AddComponent(e, new LineOfSight { Radius = def.lineOfSight });
            var grid = BuildingSizeConfig.GetSize(buildingId);
            c.AddComponent(e, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(grid) });
            c.AddComponent(e, new BuildingSize { Width = grid.x, Height = grid.y });
            c.AddComponent(e, new ArmorTypeData { Value = ArmorType.StructureHuman });
            var d = def.defense;
            c.AddComponent(e, d == null ? new Defense { Melee = 1, Ranged = 1 }
                : new Defense { Melee = d.melee, Ranged = d.ranged, Siege = d.siege, Magic = d.magic });
            if (trainer)
            {
                c.AddComponent(e, new ProductionState { Busy = 0, Remaining = 0 });
                c.AddBuffer<ProductionQueueItem>(e);
                c.AddComponent(e, new RallyPoint { Position = p + new float3(3f, 0, 3f), Has = 1 });
            }
            return e;
        }
    }
}
