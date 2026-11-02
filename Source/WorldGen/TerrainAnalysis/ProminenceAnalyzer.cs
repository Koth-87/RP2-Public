using System.Collections.Generic;
using UnityEngine;
using Planets.Utilities.Logging;
using Planets.Utilities.WorldGen;
using Planets.WorldGen.Constants;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Phase 3: Prominence and Major Peak Detection.
    /// Computes topographic prominence (<c>raw - smoothed</c>) and selects spatially distributed regional peaks.
    /// </summary>
    public class ProminenceAnalyzer : TerrainAnalysisBase
    {
        public override void Execute()
        {
            CalculateProminence();
            IdentifyMajorPeaks();
        }

        private void CalculateProminence()
        {
            RPLog.D("[Terrain Analysis] Calculating prominence (Raw - Smoothed)");

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    climate.Prominence[i] = 0f;
                    continue;
                }

                float rawElev = climate.ElevationKm[i] * 1000f;
                float smoothElev = climate.SmoothedElevation[i] * 1000f;
                climate.Prominence[i] = Mathf.Max(0f, rawElev - smoothElev);
            }
        }

        private void IdentifyMajorPeaks()
        {
            RPLog.D("[Terrain Analysis] Identifying major peaks (Spatial Binning)");

            for (int i = 0; i < tileCount; i++)
                climate.IsMajorPeak[i] = false;

            float binSizeKm = EarthScaleConstants.TERRAIN_SMOOTHING_RADIUS_KM;
            var spatialIndex = new SpatialTileIndex(binSizeKm, climate);

            var landTiles = new List<int>();
            for (int i = 0; i < tileCount; i++)
            {
                if (!climate.OceanMask[i])
                    landTiles.Add(i);
            }
            spatialIndex.BuildIndex(landTiles);

            int peakCount = 0;
            foreach (List<int> bucket in spatialIndex.GetAllBuckets())
            {
                if (bucket.Count == 0)
                    continue;

                int bestTile = -1;
                float maxElev = -9999f;
                foreach (int tileID in bucket)
                {
                    if (climate.ElevationKm[tileID] > maxElev)
                    {
                        maxElev = climate.ElevationKm[tileID];
                        bestTile = tileID;
                    }
                }

                if (bestTile == -1)
                    continue;

                int band = climate.ElevationBand[bestTile];
                float prominence = climate.Prominence[bestTile];

                if (band < 4 && !(band == 3 && prominence > 1000f))
                    continue;

                float bandMin = band == 0 ? -1000f : climate.BandThresholds[band - 1];
                float bandMax = climate.BandThresholds[band];
                float bandWidth = Mathf.Max(100f, bandMax - bandMin);
                float threshold = bandWidth * 0.15f;

                if (prominence >= threshold)
                {
                    climate.IsMajorPeak[bestTile] = true;
                    peakCount++;
                }
            }

            RPLog.D($"[Terrain Analysis] Identified {peakCount} spatially-distributed major peaks");
        }
    }
}
