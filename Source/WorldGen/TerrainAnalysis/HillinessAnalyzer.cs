using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate;
using Planets.WorldGen.Constants;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Classifies land tiles into <see cref="Hilliness"/> categories using cascading physical thresholds
    /// (relief, ruggedness, prominence, steepness) scaled by mountain frequency, followed by coastal flattening,
    /// high-elevation ridge spine promotion, and 2-pass cellular automata smoothing.
    /// </summary>
    public class HillinessAnalyzer
    {
        private ClimateData climate;
        private WorldGrid worldGrid;
        private int tileCount;
        private float mountainousness = 1f;

        private const bool ENABLE_COASTAL_FLATTENING = true;
        private const bool ENABLE_RIDGE_ENHANCEMENT = true;
        private const bool ENABLE_CELLULAR_SMOOTHING = true;

        private readonly List<PlanetTile> neighborBuffer = new List<PlanetTile>();

        public void Execute(ClimateData climateData, WorldGrid grid, ClimateConfiguration climateConfig = null)
        {
            climate = climateData;
            worldGrid = grid;
            tileCount = climate.ElevationKm.Length;

            LoadConfiguration(climateConfig);

            if (Controller.DebugEnabled)
                RPLog.D($"[HillinessAnalyzer] Starting terrain classification with mountain frequency {mountainousness:F2}");

            ComputeHilliness();

            if (ENABLE_COASTAL_FLATTENING)
                ApplyCoastalFlattening();

            if (ENABLE_RIDGE_ENHANCEMENT)
                ApplyRidgeEnhancement();

            if (ENABLE_CELLULAR_SMOOTHING)
                ApplyCellularSmoothing();

            if (Controller.DebugEnabled)
                LogHillinessDistribution();
        }

        private void ComputeHilliness()
        {
            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                    continue;

                climate.HillinessClassification[i] = ClassifyFromCascadingThresholds(i);
            }
        }

        private Hilliness ClassifyFromCascadingThresholds(int tileID)
        {
            float relief = climate.Relief[tileID];
            float ruggedness = climate.Ruggedness[tileID];
            float prominence = Mathf.Abs(climate.Prominence[tileID]);
            float steepness = climate.Steepness[tileID];

            float freqScale = MountainFrequencyScale(mountainousness);
            // Bounded fractal elevation range across a local neighborhood grows sub-linearly (~S^0.25) with tile spacing,
            // while Prominence already uses a scale-adaptive KmToRings(100 km) smoothing window.
            float metricScale = Mathf.Clamp(Mathf.Pow(EarthScaleConstants.TerrainMetricsScaleFactor, 0.25f), 0.75f, 1.6f) * freqScale;

            if (relief > EarthScaleConstants.HILLINESS_RELIEF_IMPASSABLE_MIN * metricScale
                || ruggedness > EarthScaleConstants.HILLINESS_RUGGEDNESS_IMPASSABLE_MIN * metricScale
                || prominence > EarthScaleConstants.HILLINESS_PROMINENCE_IMPASSABLE_MIN * freqScale
                || steepness > EarthScaleConstants.HILLINESS_STEEPNESS_IMPASSABLE_MIN)
                return Hilliness.Impassable;

            if (relief > EarthScaleConstants.HILLINESS_RELIEF_MOUNTAINOUS_MIN * metricScale
                || steepness > EarthScaleConstants.HILLINESS_STEEPNESS_MOUNTAINOUS_MIN
                || prominence > EarthScaleConstants.HILLINESS_PROMINENCE_MOUNTAINOUS_MIN * freqScale)
                return Hilliness.Mountainous;

            if (relief > EarthScaleConstants.HILLINESS_RELIEF_LARGE_HILLS_MIN * metricScale
                || steepness > EarthScaleConstants.HILLINESS_STEEPNESS_LARGE_HILLS_MIN
                || prominence > EarthScaleConstants.HILLINESS_PROMINENCE_LARGE_HILLS_MIN * freqScale
                || ruggedness > EarthScaleConstants.HILLINESS_RUGGEDNESS_LARGE_HILLS_MIN * metricScale)
                return Hilliness.LargeHills;

            if (relief > EarthScaleConstants.HILLINESS_RELIEF_SMALL_HILLS_MIN * metricScale
                || ruggedness > EarthScaleConstants.HILLINESS_RUGGEDNESS_SMALL_HILLS_MIN * metricScale
                || steepness > EarthScaleConstants.HILLINESS_STEEPNESS_SMALL_HILLS_MIN
                || prominence > EarthScaleConstants.HILLINESS_PROMINENCE_SMALL_HILLS_MIN * freqScale)
                return Hilliness.SmallHills;

            return Hilliness.Flat;
        }

        private void ApplyRidgeEnhancement()
        {
            int band4EnhancedCount = 0;
            int band5EnhancedCount = 0;

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i] || !climate.IsRidgeTile[i])
                    continue;
                if (climate.ElevationBand == null || climate.ElevationBand[i] < 4)
                    continue;

                Hilliness current = climate.HillinessClassification[i];
                if (climate.ElevationBand[i] == 5)
                {
                    if (current != Hilliness.Impassable)
                    {
                        climate.HillinessClassification[i] = Hilliness.Impassable;
                        band5EnhancedCount++;
                    }
                }
                else if (climate.ElevationBand[i] == 4)
                {
                    Hilliness enhanced = PromoteHilliness(current, 1);
                    if (enhanced > Hilliness.Mountainous)
                        enhanced = Hilliness.Mountainous;

                    if (enhanced != current)
                    {
                        climate.HillinessClassification[i] = enhanced;
                        band4EnhancedCount++;
                    }
                }
            }

            if (Controller.DebugEnabled && (band4EnhancedCount > 0 || band5EnhancedCount > 0))
                RPLog.D($"[HillinessAnalyzer] Ridge Enhancement: Band 4 (+1 tier): {band4EnhancedCount}, Band 5 (→Impassable): {band5EnhancedCount}");
        }

        private static Hilliness PromoteHilliness(Hilliness current, int levels)
        {
            for (int i = 0; i < levels; i++)
            {
                switch (current)
                {
                    case Hilliness.Flat: current = Hilliness.SmallHills; break;
                    case Hilliness.SmallHills: current = Hilliness.LargeHills; break;
                    case Hilliness.LargeHills: current = Hilliness.Mountainous; break;
                    case Hilliness.Mountainous: current = Hilliness.Impassable; break;
                }
            }
            return current;
        }

        private static float MountainFrequencyScale(float frequency)
        {
            if (frequency <= 1f)
                return Mathf.Lerp(1.6f, 1f, Mathf.Clamp01(frequency));
            return Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(frequency - 1f));
        }

        private void ApplyCoastalFlattening()
        {
            float band1HeightM = 500f;
            if (climate.BandThresholds != null && climate.BandThresholds.Length > 1)
                band1HeightM = Mathf.Max(100f, climate.BandThresholds[1]);

            float veryLowThreshold = band1HeightM * 0.05f;
            float lowThreshold = band1HeightM * 0.10f;
            float mediumThreshold = band1HeightM * 0.25f;
            float highThreshold = band1HeightM * 0.40f;

            float band3Threshold = 1500f;
            if (climate.BandThresholds != null && climate.BandThresholds.Length > 3)
                band3Threshold = climate.BandThresholds[3];

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                    continue;

                float elevationM = climate.ElevationKm[i] * 1000f;
                bool isHighlandOrAbove = climate.ElevationBand != null
                    ? climate.ElevationBand[i] >= 3
                    : elevationM >= band3Threshold;

                if (isHighlandOrAbove)
                    continue;

                int waterNeighbors = CountWaterNeighbors(i);
                int totalNeighbors = GetNeighborCount(i);
                float waterPercent = (float)waterNeighbors / Mathf.Max(1, totalNeighbors);

                if (elevationM < veryLowThreshold && waterPercent >= 0.4f)
                {
                    climate.HillinessClassification[i] = Hilliness.Flat;
                }
                else if (elevationM < lowThreshold && waterPercent >= 0.4f)
                {
                    if (climate.HillinessClassification[i] > Hilliness.SmallHills)
                        climate.HillinessClassification[i] = Hilliness.SmallHills;
                }
                else if (elevationM < mediumThreshold && waterPercent >= 0.3f)
                {
                    if (climate.HillinessClassification[i] == Hilliness.Impassable)
                        climate.HillinessClassification[i] = Hilliness.Mountainous;
                }
                else if (elevationM < highThreshold && waterPercent >= 0.25f)
                {
                    if (climate.HillinessClassification[i] == Hilliness.Impassable)
                        climate.HillinessClassification[i] = Hilliness.Mountainous;
                }
            }
        }

        private int CountWaterNeighbors(int tileID)
        {
            int count = 0;
            neighborBuffer.Clear();
            worldGrid.GetTileNeighbors(tileID, neighborBuffer);

            foreach (PlanetTile neighbor in neighborBuffer)
            {
                if (climate.OceanMask[neighbor.tileId])
                    count++;
            }
            return count;
        }

        private int GetNeighborCount(int tileID)
        {
            neighborBuffer.Clear();
            worldGrid.GetTileNeighbors(tileID, neighborBuffer);
            return neighborBuffer.Count;
        }

        private void ApplyCellularSmoothing()
        {
            var bufferA = new Hilliness[tileCount];
            var bufferB = new Hilliness[tileCount];

            System.Array.Copy(climate.HillinessClassification, bufferA, tileCount);
            int fillCount = ApplySmoothingPass_Fill(bufferA, bufferB);
            int cleanCount = ApplySmoothingPass_Clean(bufferB, bufferA);
            System.Array.Copy(bufferA, climate.HillinessClassification, tileCount);

            if (Controller.DebugEnabled && (fillCount > 0 || cleanCount > 0))
                RPLog.D($"[HillinessAnalyzer] Cellular Smoothing: {fillCount} tiles promoted (fill), {cleanCount} tiles demoted (clean)");
        }

        private int ApplySmoothingPass_Fill(Hilliness[] sourceBuffer, Hilliness[] targetBuffer)
        {
            int changedCount = 0;

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    targetBuffer[i] = sourceBuffer[i];
                    continue;
                }

                Hilliness current = sourceBuffer[i];
                Hilliness promoted = current;

                if (current == Hilliness.Flat)
                {
                    if (CountNeighborsAtLeast(i, Hilliness.SmallHills, sourceBuffer) >= 6)
                        promoted = Hilliness.SmallHills;
                }
                else if (current == Hilliness.SmallHills)
                {
                    if (CountNeighborsAtLeast(i, Hilliness.LargeHills, sourceBuffer) >= 6)
                        promoted = Hilliness.LargeHills;
                }
                else if (current == Hilliness.LargeHills)
                {
                    if (CountNeighborsAtLeast(i, Hilliness.Mountainous, sourceBuffer) >= 5)
                        promoted = Hilliness.Mountainous;
                }

                targetBuffer[i] = promoted;
                if (promoted != current)
                    changedCount++;
            }

            return changedCount;
        }

        private int ApplySmoothingPass_Clean(Hilliness[] sourceBuffer, Hilliness[] targetBuffer)
        {
            int changedCount = 0;

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    targetBuffer[i] = sourceBuffer[i];
                    continue;
                }

                Hilliness current = sourceBuffer[i];
                Hilliness demoted = current;

                if (climate.IsRidgeTile[i] || current == Hilliness.Impassable)
                {
                    targetBuffer[i] = current;
                    continue;
                }

                if (current == Hilliness.SmallHills)
                {
                    if (CountNeighborsAtLeast(i, Hilliness.SmallHills, sourceBuffer) < 2)
                        demoted = Hilliness.Flat;
                }
                else if (current == Hilliness.LargeHills)
                {
                    if (CountNeighborsAtLeast(i, Hilliness.LargeHills, sourceBuffer) < 2)
                        demoted = Hilliness.SmallHills;
                }
                else if (current == Hilliness.Mountainous)
                {
                    if (CountNeighborsAtLeast(i, Hilliness.Mountainous, sourceBuffer) < 2)
                        demoted = Hilliness.LargeHills;
                }

                targetBuffer[i] = demoted;
                if (demoted != current)
                    changedCount++;
            }

            return changedCount;
        }

        private int CountNeighborsAtLeast(int tileID, Hilliness minLevel, Hilliness[] buffer)
        {
            int count = 0;
            neighborBuffer.Clear();
            worldGrid.GetTileNeighbors(tileID, neighborBuffer);

            foreach (PlanetTile neighbor in neighborBuffer)
            {
                if (buffer[neighbor.tileId] >= minLevel)
                    count++;
            }
            return count;
        }

        private void LogHillinessDistribution()
        {
            int flat = 0, small = 0, large = 0, mountainous = 0, impassable = 0, ocean = 0;

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    ocean++;
                    continue;
                }

                switch (climate.HillinessClassification[i])
                {
                    case Hilliness.Flat: flat++; break;
                    case Hilliness.SmallHills: small++; break;
                    case Hilliness.LargeHills: large++; break;
                    case Hilliness.Mountainous: mountainous++; break;
                    case Hilliness.Impassable: impassable++; break;
                }
            }

            int landTiles = Mathf.Max(1, tileCount - ocean);
            RPLog.D($"[HillinessAnalyzer] Distribution: Flat {100f * flat / landTiles:F1}%, Small {100f * small / landTiles:F1}%, Large {100f * large / landTiles:F1}%, Mountainous {100f * mountainous / landTiles:F1}%, Impassable {100f * impassable / landTiles:F1}%");
        }

        private void LoadConfiguration(ClimateConfiguration climateConfig)
        {
            ClimateConfiguration config = climateConfig ?? Controller.Settings?.climateConfig;
            if (config != null)
                mountainousness = config.mountainousness.CurrentValue;
        }
    }
}
