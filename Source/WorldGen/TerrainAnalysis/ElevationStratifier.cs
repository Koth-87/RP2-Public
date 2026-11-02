using UnityEngine;
using Planets.Utilities.Logging;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Phase 1: Elevation Stratification.
    /// Divides land elevations into 6 physical relief bands (0 = below sea level, 1–3 = lowlands/highlands, 4 = mountains, 5 = peaks).
    /// Mountain threshold is <c>Max(35% of maxElev, 2000m)</c>.
    /// </summary>
    public class ElevationStratifier : TerrainAnalysisBase
    {
        public override void Execute()
        {
            float maxElev = 0f;
            bool hasLand = false;

            for (int i = 0; i < tileCount; i++)
            {
                if (!climate.OceanMask[i])
                {
                    float elevM = climate.ElevationKm[i] * 1000f;
                    if (elevM > maxElev)
                        maxElev = elevM;
                    hasLand = true;
                }
            }

            if (!hasLand)
            {
                RPLog.Warning("[Terrain Analysis] No land tiles found - skipping band creation");
                return;
            }

            float mtnThreshold = Mathf.Max(maxElev * 0.35f, 2000f);
            float lowStep = mtnThreshold / 3f;
            float lowBand1 = lowStep;
            float lowBand2 = lowStep * 2f;
            float highMid = mtnThreshold + (maxElev - mtnThreshold) * 0.5f;

            climate.BandThresholds[0] = 0f;
            climate.BandThresholds[1] = lowBand1;
            climate.BandThresholds[2] = lowBand2;
            climate.BandThresholds[3] = mtnThreshold;
            climate.BandThresholds[4] = highMid;
            climate.BandThresholds[5] = maxElev;

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    climate.ElevationBand[i] = -1;
                    continue;
                }

                float val = climate.ElevationKm[i] * 1000f;
                if (val < 0f) climate.ElevationBand[i] = 0;
                else if (val < lowBand1) climate.ElevationBand[i] = 1;
                else if (val < lowBand2) climate.ElevationBand[i] = 2;
                else if (val < mtnThreshold) climate.ElevationBand[i] = 3;
                else if (val < highMid) climate.ElevationBand[i] = 4;
                else climate.ElevationBand[i] = 5;
            }

            RPLog.D($"[Terrain Analysis] Bands (Max {maxElev:F0}m): 0-{lowBand1:F0}-{lowBand2:F0}-{mtnThreshold:F0} (Low) | {highMid:F0}-{maxElev:F0} (High)");
        }
    }
}
