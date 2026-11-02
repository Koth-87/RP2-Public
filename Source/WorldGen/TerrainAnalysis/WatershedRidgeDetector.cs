using Planets.Utilities.Logging;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Phase 10: Watershed Ridge Detection.
    /// Identifies high-elevation watershed divides (Band 4–5 tiles bordering different river systems)
    /// and integrates regional major peaks into <c>IsRidgeTile</c> (Tier 2).
    /// </summary>
    public class WatershedRidgeDetector : TerrainAnalysisBase
    {
        public override void Execute()
        {
            RPLog.D("[Terrain Analysis] Detecting watershed ridges (Hierarchical)");

            int ridgeCount = 0;
            int majorRidgeCount = 0;

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    climate.IsRidgeTile[i] = false;
                    climate.RidgeTier[i] = 0;
                    continue;
                }

                climate.IsRidgeTile[i] = false;
                climate.RidgeTier[i] = 0;

                if (climate.ElevationBand[i] < 4)
                    continue;

                int myBasin = climate.MergedBasinIDTopology[i];
                int mySystem = climate.RiverSystemIDTopology[i];
                if (myBasin < 0 || mySystem < 0)
                    continue;

                neighborBuffer.Clear();
                grid.GetTileNeighbors(i, neighborBuffer);

                bool isRiverSystemDivide = false;
                foreach (var neighbor in neighborBuffer)
                {
                    if (climate.OceanMask[neighbor.tileId])
                        continue;

                    int nSystem = climate.RiverSystemIDTopology[neighbor.tileId];
                    if (nSystem >= 0 && nSystem != mySystem)
                    {
                        isRiverSystemDivide = true;
                        break;
                    }
                }

                if (isRiverSystemDivide)
                {
                    climate.IsRidgeTile[i] = true;
                    climate.RidgeTier[i] = 2;
                    ridgeCount++;
                    majorRidgeCount++;
                }
            }

            int peakAddedCount = 0;
            for (int i = 0; i < tileCount; i++)
            {
                if (climate.IsMajorPeak[i] && !climate.IsRidgeTile[i])
                {
                    climate.IsRidgeTile[i] = true;
                    climate.RidgeTier[i] = 2;
                    peakAddedCount++;
                }
            }

            RPLog.D($"[Terrain Analysis] Identified ridges: {majorRidgeCount} major (river system divides at high elevation), {peakAddedCount} isolated peaks added");
            RPLog.D($"[Terrain Analysis] Total ridge tiles: {ridgeCount + peakAddedCount}");
        }
    }
}
