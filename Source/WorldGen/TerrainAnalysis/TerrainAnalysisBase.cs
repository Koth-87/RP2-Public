using System.Collections.Generic;
using RimWorld.Planet;
using Planets.WorldGen.Climate;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Base class for all terrain analysis phases.
    /// Provides shared context (<see cref="ClimateData"/>, <see cref="WorldGrid"/>) and reusable buffers for BFS queries.
    /// </summary>
    public abstract class TerrainAnalysisBase
    {
        protected ClimateData climate;
        protected WorldGrid grid;
        protected int tileCount;

        protected readonly HashSet<int> bfsResult = new HashSet<int>();
        protected readonly List<int> bfsCurrentRing = new List<int>();
        protected readonly List<int> bfsNextRing = new List<int>();
        protected readonly List<PlanetTile> neighborBuffer = new List<PlanetTile>();

        public virtual void Initialize(ClimateData climateData, WorldGrid worldGrid)
        {
            climate = climateData;
            grid = worldGrid;
            tileCount = climate.Latitude.Length;
        }

        public abstract void Execute();
    }
}
