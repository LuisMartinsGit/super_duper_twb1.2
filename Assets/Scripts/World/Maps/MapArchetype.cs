// MapArchetype.cs
// One of 5 macro layouts the procedural map generator can produce.
// GameSettings.MapArchetype picks which archetype RegionPlacer + TerrainShape use.

namespace TheWaningBorder.World.Maps
{
    public enum MapArchetype
    {
        // Mostly flat playable mass with 3-6 isolated highlands. No coastline.
        Plain = 0,
        // Land on one side of a jittered coastline; mountains inland.
        Coastal = 1,
        // Meandering river through plain terrain, players on opposite banks.
        River = 2,
        // Closed landmass surrounded by water, central ridge spine.
        Island = 3,
        // Two large landmasses connected by a narrow strip.
        Isthmus = 4,
    }
}
