using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Planets.Utilities.Logging;
using Planets.WorldGen.Constants;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.WorldGen.Hydrology.Core;
using Planets.WorldGen.Hydrology.Models;

namespace Planets.WorldGen.Climate.Layers
{
    /// <summary>
    /// Computes drainage-based wetland intensity (<c>Swampiness</c>), rain-weighted river flow (<c>FlowRain</c>)
    /// on the Tier 1 drainage tree for <see cref="Patches.WorldGen.Patch_DrainageRivers"/>, and optional Tier 2 climate-driven hydrology.
    /// </summary>
    public class HydrologyLayer : IClimateLayer
    {
        private ClimateData climate;
        private FeatureConfiguration features;
        private int tileCount;

        public string LayerName => "Hydrology";

        public HydrologyLayer(ClimateData climateData, FeatureConfiguration featureConfiguration = null)
        {
            climate = climateData;
            features = featureConfiguration;
            tileCount = climate.Latitude.Length;
        }

        /// <summary>
        /// Execute hydrology computation (IClimateLayer interface)
        /// </summary>
        public void Execute(ClimateData climateData)
        {
            ComputeHydrology();
        }

        /// <summary>
        /// Compute hydrology: swampiness (Tier 1) and optionally Tier 2 climate-run results.
        /// When Tier 2 is on, swampiness is recomputed after the Tier 2 run from climate-driven flow.
        /// </summary>
        private void ComputeHydrology()
        {
            ComputeSwampiness(useTier2Flow: false);

            if (features != null && features.IsTier2HydrologyEnabled)
            {
                RunTier2Hydrology();
                ComputeSwampiness(useTier2Flow: true);
            }

            ComputeFlowRain();

            if (Planets.Core.Controller.DebugEnabled)
            {
                int swampyTiles = 0;
                for (int i = 0; i < tileCount; i++)
                {
                    if (climate.Swampiness[i] > 0.5f)
                        swampyTiles++;
                }
                RPLog.D($"[Hydrology] Computed swampiness for {tileCount} tiles ({swampyTiles} highly swampy)");
            }
        }

        /// <summary>
        /// Run the hydrology engine with climate runoff and bulk lake evaporation; write to *Hydrology arrays.
        /// Unit contract: flow = Runoff (Rainfall − LandEvap, mm/year) × area (km²), evaporation = BulkLakeEvap (mm/year) × area (km²).
        /// </summary>
        private void RunTier2Hydrology()
        {
            var grid = Find.WorldGrid;
            if (grid == null) return;
            if (climate.AnnualRainfall == null || climate.PotentialEvapotranspiration == null
                || climate.ElevationKm == null || climate.OceanMask == null) return;
            if (climate.FlowAccumulationHydrology == null || climate.FlowAccumulationHydrology.Length < tileCount) return;

            const int MAX_ITERATIONS = 500;

            var flowInput = new ClimateFlowInput(climate.AnnualRainfall, climate.LandEvaporationMm);
            var evaporationInput = new ClimateEvaporationInput(
                climate.PotentialEvapotranspiration,
                Controller.Settings?.hydrologyConfig,
                climate,
                Controller.Settings?.climateConfig);
            var config = new HydrologyConfig
            {
                MaxIterations = MAX_ITERATIONS,
                FlowInput = flowInput,
                EvaporationInput = evaporationInput
            };

            var engine = new HydrologySimulator();
            HydrologyResult result = engine.Simulate(grid, climate.ElevationKm, climate.OceanMask, config);

            for (int i = 0; i < tileCount; i++)
            {
                climate.FlowAccumulationHydrology[i] = result.FlowAccumulation[i];
                climate.SpillDirectionHydrology[i] = result.SpillDirections[i];
                climate.FlowDirectionHydrology[i] = result.FlowDirections[i];
                climate.MergedLakeIDHydrology[i] = result.MergedLakeIDs[i];
                climate.MergedBasinIDHydrology[i] = result.MergedBasinIDs[i];
                climate.EndorheicMaskHydrology[i] = result.MergedLakeIDs[i] >= 0;
            }

            // RiverSystemIDHydrology: use merged basin ID as system ID for now
            for (int i = 0; i < tileCount; i++)
                climate.RiverSystemIDHydrology[i] = climate.MergedBasinIDHydrology[i];

            ClassifyTier2RiversAndLakes();

            if (Controller.DebugEnabled)
                RPLog.D("[Hydrology] Tier 2 climate-run complete");
        }

        /// <summary>
        /// Populate RiverPresence, RiverMagnitude, WaterBodyType from Tier 2 flow and lake data (WS21).
        /// Fill-and-spill water balance is the sole lake classifier:
        /// - 2+ flooded tiles in a lake chunk -> WaterBodyType = 2 (Lake biome when LakeBiomeConversionEnabled is on).
        /// - 1 flooded tile in a lake chunk -> WaterBodyType = 3 (stays land biome; eligible for single-tile mutator).
        /// </summary>
        private void ClassifyTier2RiversAndLakes()
        {
            if (climate.OceanMask == null || climate.FlowAccumulationHydrology == null
                || climate.MergedLakeIDHydrology == null || climate.RiverPresence == null
                || climate.RiverMagnitude == null || climate.WaterBodyType == null)
                return;

            float referenceRainfall = Mathf.Max(1f, Controller.Settings?.hydrologyConfig?.referenceRainfallMm?.CurrentValue ?? 1000f);

            var lakeTileCount = new Dictionary<int, int>();
            for (int i = 0; i < tileCount; i++)
            {
                int lid = climate.MergedLakeIDHydrology[i];
                if (lid < 0 || climate.OceanMask[i])
                    continue;
                lakeTileCount.TryGetValue(lid, out int c);
                lakeTileCount[lid] = c + 1;
            }

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    climate.RiverPresence[i] = 0f;
                    climate.RiverMagnitude[i] = 0;
                    climate.WaterBodyType[i] = 0;
                    continue;
                }

                float flowVolume = climate.FlowAccumulationHydrology[i];
                int lakeId = climate.MergedLakeIDHydrology[i];

                if (lakeId >= 0)
                {
                    lakeTileCount.TryGetValue(lakeId, out int count);
                    climate.WaterBodyType[i] = count >= 2 ? (byte)2 : (byte)3;
                    climate.RiverPresence[i] = 0f;
                    climate.RiverMagnitude[i] = 0;
                    continue;
                }

                float equivalentDrainageKm2 = flowVolume / referenceRainfall;
                byte magnitude = 0;
                if (equivalentDrainageKm2 >= EarthScaleConstants.HUGE_RIVER_MIN_DRAINAGE_KM2) magnitude = 4;
                else if (equivalentDrainageKm2 >= EarthScaleConstants.MAJOR_RIVER_MIN_DRAINAGE_KM2) magnitude = 3;
                else if (equivalentDrainageKm2 >= EarthScaleConstants.RIVER_MIN_DRAINAGE_KM2) magnitude = 2;
                else if (equivalentDrainageKm2 >= EarthScaleConstants.STREAM_MIN_DRAINAGE_KM2) magnitude = 1;

                climate.RiverMagnitude[i] = magnitude;
                climate.WaterBodyType[i] = magnitude > 0 ? (byte)1 : (byte)0;
                climate.RiverPresence[i] = magnitude > 0 ? 1f : Mathf.Clamp01(equivalentDrainageKm2 / EarthScaleConstants.STREAM_MIN_DRAINAGE_KM2);
            }
        }

        /// <summary>
        /// Compute swampiness score based on flow accumulation and terrain factors.
        /// Replaces vanilla's noise-based swampiness with drainage-based approach.
        /// When useTier2Flow is true (Tier 2 enabled), uses FlowAccumulationHydrology; otherwise Topology.
        /// 
        /// Swampiness depends on:
        /// 1. Flow accumulation (water from upstream)
        /// 2. Low gradient (flat terrain holds water)
        /// 3. Sufficient rainfall (need water input)
        /// 4. Low aridity (deserts don't have swamps)
        /// 5. Low elevation (lowlands more swampy)
        /// </summary>
        private void ComputeSwampiness(bool useTier2Flow = false)
        {
            float[] flowAccumulation = useTier2Flow && climate.FlowAccumulationHydrology != null && climate.FlowAccumulationHydrology.Length >= tileCount
                ? climate.FlowAccumulationHydrology
                : climate.FlowAccumulationTopology;
            float referenceRainfall = useTier2Flow
                ? Mathf.Max(1f, Controller.Settings?.hydrologyConfig?.referenceRainfallMm?.CurrentValue ?? 1000f)
                : 1000f;

            for (int i = 0; i < tileCount; i++)
            {
                // Skip ocean tiles
                if (climate.OceanMask[i])
                {
                    climate.Swampiness[i] = 0f;
                    continue;
                }

                // Factor 1: Flow accumulation (wetness from upstream drainage)
                // Topology: flow in tile count → equivalent km² = flow * TILE_AREA_KM2. Tier 2: flow in mm·km²/year → equivalent km² = flow / referenceRainfall.
                float flowVolume = flowAccumulation[i];
                float equivalentDrainageKm2 = useTier2Flow
                    ? flowVolume / referenceRainfall
                    : flowVolume * EarthScaleConstants.TILE_AREA_KM2;
                float flowFactor = Mathf.Min(1.0f, equivalentDrainageKm2 / EarthScaleConstants.SWAMP_MIN_DRAINAGE_KM2);

                // Factor 2: Low gradient (flat areas hold water better)
                // Relief < 400m = very swampy, > 800m = not swampy
                float gradientFactor = 1.0f - Mathf.Min(1.0f, climate.Relief[i] / 800f);

                // Factor 3: Climate - need sufficient rainfall for swamps
                float climateFactor;
                if (climate.AnnualRainfall[i] < 400f)
                {
                    // Too dry for swamps
                    climateFactor = 0.0f;
                }
                else
                {
                    // Scale from 400mm to 1000mm
                    climateFactor = Mathf.Min(1.0f, (climate.AnnualRainfall[i] - 400f) / 600f);
                }

                // Factor 4: Aridity - higher aridity = less swampy
                // AridityIndex is rainfall / PET, uncapped.
                // Values above 1 are treated as 1 here.
                float aridityFactor = Mathf.Clamp01(climate.AridityIndex[i]);

                // Factor 5: Elevation - lowlands more swampy
                // < 500m = full factor, > 2000m = no swamps
                float elevationFactor = 1.0f - Mathf.Min(1.0f, climate.ElevationKm[i] / 2.0f);

                // Combine factors multiplicatively (all must be favorable)
                float baseSwampiness = flowFactor * gradientFactor * climateFactor * 
                                       aridityFactor * elevationFactor;

                // Apply hilliness modifier from HillinessClassification (set by HillinessAnalyzer before this layer)
                int hillinessKey = climate.HillinessClassification != null && i < climate.HillinessClassification.Length
                    ? (int)climate.HillinessClassification[i]
                    : 1; // fallback Flat
                float hillinessFactor = EarthScaleConstants.GetWetlandFactorForHilliness(hillinessKey);

                // Final swampiness score
                climate.Swampiness[i] = Mathf.Clamp01(baseSwampiness * hillinessFactor);
            }
        }

        /// <summary>
        /// Rain-weighted flow on the Tier 1 tree (or from Tier 2 evaporating runoff when Tier 2 is enabled), in the same units vanilla uses for river size.
        /// </summary>
        private void ComputeFlowRain()
        {
            WorldGrid grid = Find.WorldGrid;
            PlanetLayer surface = grid?.Surface;
            if (surface?.Tiles == null || climate.FlowDirectionTopology == null)
                return;
            int count = Math.Min(tileCount, surface.Tiles.Count);
            if (climate.FlowRain == null || climate.FlowRain.Length < count)
                climate.FlowRain = new float[count];

            if (features != null && features.IsTier2HydrologyEnabled && climate.FlowAccumulationHydrology != null && climate.FlowAccumulationHydrology.Length >= count)
            {
                float invRefArea = 1f / Mathf.Max(1f, EarthScaleConstants.TILE_AREA_KM2_AT_SCALE_10);
                for (int i = 0; i < count; i++)
                    climate.FlowRain[i] = Mathf.Max(0f, climate.FlowAccumulationHydrology[i] * invRefArea);
                return;
            }

            int[] downstream = new int[count];
            int[] waiting = new int[count];
            var upstream = new List<int>[count];
            var buffer = new List<PlanetTile>();
            for (int i = 0; i < count; i++)
            {
                downstream[i] = Downstream(i, surface, buffer);
                if (downstream[i] < 0)
                    continue;
                waiting[downstream[i]]++;
                if (upstream[downstream[i]] == null)
                    upstream[downstream[i]] = new List<int>();
                upstream[downstream[i]].Add(i);
            }

            float areaFactor = EarthScaleConstants.TILE_AREA_KM2 / EarthScaleConstants.TILE_AREA_KM2_AT_SCALE_10;
            var queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (waiting[i] == 0)
                    queue.Enqueue(i);
            }

            int processed = 0;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                processed++;
                Tile tile = surface.Tiles[i];
                float incoming = 0f;
                List<int> children = upstream[i];
                if (children != null)
                {
                    for (int c = 0; c < children.Count; c++)
                        incoming += climate.FlowRain[children[c]];
                }
                float rain = tile != null ? tile.rainfall : 0f;
                float temp = tile != null ? tile.temperature : 0f;
                float accumulated = rain * areaFactor + incoming;
                climate.FlowRain[i] = Mathf.Max(0f, accumulated - WorldGenStep_Rivers.CalculateTotalEvaporation(accumulated, temp));
                int next = downstream[i];
                if (next >= 0 && --waiting[next] == 0)
                    queue.Enqueue(next);
            }

            if (processed >= count)
                return;

            int broken = 0;
            while (processed < count)
            {
                int lowest = -1;
                float lowestElev = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    if (waiting[i] <= 0 && queue.Contains(i))
                        continue;
                    if (waiting[i] <= 0)
                        continue;
                    float elev = surface.Tiles[i] != null ? surface.Tiles[i].elevation : 0f;
                    if (elev < lowestElev)
                    {
                        lowestElev = elev;
                        lowest = i;
                    }
                }
                if (lowest < 0)
                    break;
                downstream[lowest] = -1;
                waiting[lowest] = 0;
                queue.Enqueue(lowest);
                broken++;
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    processed++;
                    Tile tile = surface.Tiles[i];
                    float incoming = 0f;
                    List<int> children = upstream[i];
                    if (children != null)
                    {
                        for (int c = 0; c < children.Count; c++)
                            incoming += climate.FlowRain[children[c]];
                    }
                    float rain = tile != null ? tile.rainfall : 0f;
                    float temp = tile != null ? tile.temperature : 0f;
                    float accumulated = rain * areaFactor + incoming;
                    climate.FlowRain[i] = Mathf.Max(0f, accumulated - WorldGenStep_Rivers.CalculateTotalEvaporation(accumulated, temp));
                    int next = downstream[i];
                    if (next >= 0 && --waiting[next] == 0)
                        queue.Enqueue(next);
                }
            }
            if (broken > 0 && Controller.DebugEnabled)
                RPLog.D($"[Hydrology] Broke {broken} drainage cycles at the lowest tile.");
        }

        private int Downstream(int tileId, PlanetLayer surface, List<PlanetTile> buffer)
        {
            Tile tile = surface.Tiles[tileId];
            if (tile == null || tile.biome == BiomeDefOf.Ocean)
                return -1;
            int slot = -1;
            if (tileId < climate.FlowDirectionTopology.Length && climate.FlowDirectionTopology[tileId] >= 0)
                slot = climate.FlowDirectionTopology[tileId];
            else if (climate.SpillDirectionTopology != null && tileId < climate.SpillDirectionTopology.Length && climate.SpillDirectionTopology[tileId] >= 0)
                slot = climate.SpillDirectionTopology[tileId];
            if (slot < 0)
                return -1;
            buffer.Clear();
            Find.WorldGrid.GetTileNeighbors(new PlanetTile(tileId, surface), buffer);
            if (slot >= buffer.Count)
                return -1;
            return buffer[slot].tileId;
        }

    }
}
