using System.Collections.Generic;
using Planets.Utilities.Logging;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Phase 12: Terrain Analysis Validation.
    /// Logs summary statistics for drainage basins, river systems, major peaks, prominence, and elevation bands.
    /// </summary>
    public class TerrainValidator : TerrainAnalysisBase
    {
        public override void Execute()
        {
            RPLog.D("[Terrain Analysis] Validating results and computing statistics");

            int landTiles = 0;
            int assignedBasins = 0;
            int peakCount = 0;

            var uniqueBasins = new HashSet<int>();
            var uniqueSystems = new HashSet<int>();

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                    continue;

                landTiles++;

                if (climate.MergedBasinIDTopology[i] >= 0)
                {
                    assignedBasins++;
                    uniqueBasins.Add(climate.MergedBasinIDTopology[i]);
                }

                if (climate.RiverSystemIDTopology[i] >= 0)
                    uniqueSystems.Add(climate.RiverSystemIDTopology[i]);

                if (climate.IsMajorPeak[i])
                    peakCount++;
            }

            float basinCoverage = landTiles > 0 ? assignedBasins / (float)landTiles * 100f : 0f;
            float avgTilesPerBasin = uniqueBasins.Count > 0 ? assignedBasins / (float)uniqueBasins.Count : 0f;
            float peakDensity = landTiles > 0 ? peakCount / (float)landTiles * 100f : 0f;

            int[] bandCounts = new int[6];
            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                    continue;

                int band = climate.ElevationBand[i];
                if (band >= 0 && band < 6)
                    bandCounts[band]++;
            }

            var prominences = new List<float>();
            for (int i = 0; i < tileCount; i++)
            {
                if (!climate.OceanMask[i] && climate.Prominence[i] > 0f)
                    prominences.Add(climate.Prominence[i]);
            }
            prominences.Sort();

            float minProm = prominences.Count > 0 ? prominences[0] : 0f;
            float maxProm = prominences.Count > 0 ? prominences[prominences.Count - 1] : 0f;
            float medianProm = prominences.Count > 0 ? prominences[prominences.Count / 2] : 0f;

            RPLog.Message("[Terrain Analysis] === VALIDATION SUMMARY ===");
            RPLog.Message($"  Land tiles: {landTiles}");
            RPLog.Message($"  Drainage basins: {uniqueBasins.Count} (coverage: {basinCoverage:F1}%, avg size: {avgTilesPerBasin:F0} tiles)");
            RPLog.Message($"  River Systems: {uniqueSystems.Count} (aggregated)");
            RPLog.Message($"  Major peaks: {peakCount} (density: {peakDensity:F2}%)");
            RPLog.Message($"  Prominence range: {minProm:F0}m - {maxProm:F0}m (median: {medianProm:F0}m)");

            RPLog.Message("  Elevation bands:");
            string[] bandNames = { "Below sea", "Low coastal", "Interior low", "Highlands", "Mountains", "Peaks" };
            for (int i = 0; i < 6; i++)
            {
                float pct = landTiles > 0 ? bandCounts[i] / (float)landTiles * 100f : 0f;
                RPLog.Message($"    Band {i} ({bandNames[i]}): {bandCounts[i]} tiles ({pct:F1}%)");
            }

            bool valid = true;
            if (basinCoverage < 90f)
            {
                RPLog.Warning($"[Terrain Analysis] Low basin coverage: {basinCoverage:F1}% (expected >90%)");
                valid = false;
            }

            if (uniqueBasins.Count < 10)
            {
                RPLog.Warning($"[Terrain Analysis] Too few basins: {uniqueBasins.Count} (expected >10 for Earth-scale)");
                valid = false;
            }

            if (peakCount == 0)
            {
                float maxElev = climate.BandThresholds?[5] ?? 9000f;
                if (maxElev >= 1500f)
                {
                    RPLog.Warning($"[Terrain Analysis] No peaks detected despite high max elevation ({maxElev:F0}m) - check prominence thresholds");
                    valid = false;
                }
                else
                {
                    RPLog.Message($"[Terrain Analysis] No peaks detected (expected for flat world, max {maxElev:F0}m)");
                }
            }

            if (valid)
                RPLog.Message("[Terrain Analysis] Validation passed - all metrics within expected ranges");
            else
                RPLog.Warning("[Terrain Analysis] Validation completed with warnings - review metrics above");
        }
    }
}
