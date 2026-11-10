using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;
using Planets.Biomes.Core;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.Utilities.Random;
using Planets.WorldGen.Climate;
using Planets.WorldGen.Constants;
using Planets.WorldGen.Geography;

namespace Planets.WorldGen
{
    /// <summary>
    /// Orchestrates the complete 3-phase world generation pipeline for Realistic Planets.
    /// 
    /// **Pipeline Structure**:
    /// - Phase 1: Terrain Generation (elevation extraction, tile creation, lake conversion)
    /// - Phase 2: Climate Computation (all 11 layers via ClimateOrchestrator)
    /// - Phase 3: Data Application (temperature, rainfall, hilliness, swampiness, biome assignment)
    /// 
    /// **Design Pattern**: Pure orchestration logic with no Harmony patches.
    /// - Called BY Harmony patches (in Patches/WorldGen/)
    /// - Contains NO Harmony attributes
    /// - All business logic lives here, patches are thin wrappers
    /// 
    /// **Feature Toggle Support (current usage)**:
    /// - ClimateOrchestrator runs all 11 layers in Phase 2
    /// - Hydrology toggle is applied during Phase 3 swampiness assignment
    /// - Lake conversion ALWAYS enabled (better than vanilla)
    /// - Ridge enhancement ALWAYS enabled (part of Layer 1 Geography)
    /// </summary>
    public class WorldGenerationOrchestrator
    {
        private readonly WorldGenerationState state;

        /// <summary>Best positive score seen this generation for each foreign or custom-score worker.</summary>
        private readonly Dictionary<string, float> foreignScorePeaks = new Dictionary<string, float>();
        private readonly Dictionary<string, int> biomeWinCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> foreignWinCounts = new Dictionary<string, int>();
        private readonly List<int> fallbackTileSamples = new List<int>();
        private readonly HashSet<string> loggedWorkerErrors = new HashSet<string>();
        private readonly int[] energyBuckets = new int[6];
        private readonly int[] rainfallBuckets = new int[8];
        private bool usedEarthHeightmap;
        private int fallbackTileCount;
        private int foreignWinCount;
        private int deadZoneCount;

        /// <summary>Last Phase 3 summary, including when debug logging is off, for the CSV export.</summary>
        public static string LastSelectionReport;

        /// <summary>Energy of a score of 1. The Phase 1 ocean or lake sits here, so a water biome must score above 1 to replace it.</summary>
        private const float WaterBaselineEnergy = 9.21f;

        /// <summary>
        /// Runs after tile temperature, rainfall, hilliness and swampiness are written,
        /// and before foreign score peaks and biome selection. Geological Landforms cave depth
        /// has to exist by then, because Caverns workers read it during selection.
        /// </summary>
        public Action BeforeBiomeSelection { get; set; }
        
        /// <summary>
        /// Creates orchestrator with fully initialized generation state.
        /// </summary>
        /// <param name="initialState">Validated world generation state (must not be null)</param>
        public WorldGenerationOrchestrator(WorldGenerationState initialState)
        {
            state = initialState ?? throw new ArgumentNullException(nameof(initialState),
                "WorldGenerationState cannot be null");
        }
        
        /// <summary>
        /// Execute the complete 3-phase world generation pipeline.
        /// Returns true on success, false on any failure (caller should fallback to vanilla).
        /// 
        /// Error handling strategy:
        /// - Exceptions are caught and logged
        /// - Returns false to signal failure
        /// - Caller (Harmony patch) can return true to let vanilla run
        /// </summary>
        /// <returns>True if generation succeeded, false if failed (fallback to vanilla)</returns>
        public bool Execute()
        {
            try
            {
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] Starting RP world generation pipeline");
                
                // Phase 1: Generate terrain and tiles
                if (!ExecutePhase1_TerrainGeneration())
                {
                    RPLog.Error("Phase 1 (Terrain Generation) failed");
                    return false;
                }
                
                // Phase 2: Compute climate (11 layers)
                if (!ExecutePhase2_ClimateComputation())
                {
                    RPLog.Error("Phase 2 (Climate Computation) failed");
                    return false;
                }
                
                // Phase 3: Apply data and assign biomes
                if (!ExecutePhase3_DataApplication())
                {
                    RPLog.Error("Phase 3 (Data Application) failed");
                    return false;
                }
                
                // Mark complete
                state.AdvanceToPhase(GenerationPhase.Complete);
                
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] Pipeline complete - world generated successfully");
                
                return true;
            }
            catch (Exception e)
            {
                RPLog.Error($"World generation pipeline failed: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }
        
        // ========== PHASE 1: TERRAIN GENERATION ==========
        
        /// <summary>
        /// Phase 1: Generate terrain and tiles.
        /// 
        /// Steps:
        /// 1. Extract elevation from vanilla noise (preserves continent shapes)
        /// 2. Apply SeaLevel adjustment (modifies elevation range for water coverage)
        /// 3. Create tiles with placeholder values
        /// 4. Convert isolated water bodies to lakes (ALWAYS, not conditional)
        /// 
        /// Lake conversion:
        /// - True ocean is water connected to the map edge, or the largest body when nothing is
        /// - Every other below-sea-level body becomes the Lake biome
        /// </summary>
        private bool ExecutePhase1_TerrainGeneration()
        {
            try
            {
                state.AdvanceToPhase(GenerationPhase.GeneratingTerrain);

                var elevationNoise = state.VanillaElevationNoise;
                var worldGrid = state.WorldGrid;
                
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] Phase 1: Extracting elevation and creating tiles");
                
                // Earth heightmap: sea level 0 is Earth's coastline. The sea-level setting only
                // changes the noise range, so it does not move this coastline. Mountain scaling
                // and polar landmasses are skipped because the heightmap already has both.
                usedEarthHeightmap = TryFillEarthElevations(worldGrid);
                if (!usedEarthHeightmap)
                {
                    for (int i = 0; i < state.TileCount; i++)
                    {
                        Vector3 tileCenter = worldGrid.GetTileCenter(i);
                        state.ElevationData[i] = elevationNoise.GetValue(tileCenter);
                    }

                    float mountainHeight = Controller.Settings?.climateConfig?.mountainHeight.CurrentValue ?? 1f;
                    if (!Mathf.Approximately(mountainHeight, 1f))
                    {
                        for (int i = 0; i < state.TileCount; i++)
                        {
                            if (state.ElevationData[i] > 0f)
                                state.ElevationData[i] *= mountainHeight;
                        }
                    }

                    InsertPolarLandmasses();
                }
                
                // Log elevation statistics for verification
                LogElevationStatistics();
                
                // Create tiles with extracted elevation
                CreateTilesWithElevation();
                
                // Convert isolated water bodies to lakes (ALWAYS enabled)
                int lakesCreated = ConvertIsolatedWaterToLakes();
                
                // Normalize ocean depths (must happen after lake detection)
                NormalizeOceanDepths();
                WarnIfAlmostNoWater();

                if (Controller.DebugEnabled)
                    RPLog.D($"[WorldGen] Phase 1 complete - {lakesCreated} lakes created");
                
                return true;
            }
            catch (Exception e)
            {
                RPLog.Error($"Phase 1 failed: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Log elevation statistics for verification.
        /// 
        /// The elevation data is already in the correct range - vanilla's SetupElevationNoise()
        /// configured the noise module based on the ElevationRange we set in Patch_WorldGenStep_Terrain.
        /// 
        /// SeaLevel floors (Patch_WorldGenStep_Terrain). Ocean share is what the geography log measures, not these old guesses.
        /// Normal (-900 m) matches vanilla's share, about 39–52% ocean on a full sphere.
        /// Earth-like (enum SlightlyHigh, -1900 m) is aimed at Earth's share, about 71% ocean.
        /// High (-3000 m) is the island setting. Low and Slightly low stay shallower. The floor stays negative.
        /// </summary>
        /// <summary>
        /// Fill elevations from the ETOPO grid when the debug checkbox is on, the path is a
        /// 720 by 1440 int16 file, and coverage is 100%. Otherwise leave the noise path to run.
        /// </summary>
        private bool TryFillEarthElevations(WorldGrid worldGrid)
        {
            if (!Controller.EarthHeightmapAvailable)
                return false;

            ClimateConfiguration config = Controller.Settings?.climateConfig;
            if (config == null || !config.useEarthHeightmap.CurrentValue)
                return false;

            float coverage = Find.World?.info?.planetCoverage ?? 0f;
            if (coverage < 0.999f)
            {
                RPLog.W($"[WorldGen] Earth heightmap needs 100% coverage (this world is {coverage:P0}). Generating from noise.");
                return false;
            }

            string path = config.earthHeightmapPath.CurrentValue;
            if (!EarthHeightmap.TryLoad(path, out string error))
            {
                RPLog.W("[WorldGen] Earth heightmap not used. " + error + " Generating from noise.");
                return false;
            }

            for (int i = 0; i < state.TileCount; i++)
            {
                Vector2 longLat = worldGrid.LongLatOf(i);
                state.ElevationData[i] = EarthHeightmap.SampleMeters(longLat.x, longLat.y);
            }

            if (Controller.DebugEnabled)
                RPLog.D("[WorldGen] Elevation from Earth heightmap " + path);
            return true;
        }

        private void WarnIfAlmostNoWater()
        {
            if (!Controller.DebugEnabled || state.TileCount <= 0 || state.ElevationData == null)
                return;
            int water = 0;
            for (int i = 0; i < state.TileCount; i++)
            {
                if (state.ElevationData[i] <= 0f)
                    water++;
            }
            if (water < state.TileCount * 0.02f)
                RPLog.W($"[WorldGen] Water is {water} of {state.TileCount} tiles, under 2%. Rivers and ocean distance need water tiles.");
        }

        private void LogElevationStatistics()
        {
            var seaLevel = Planets_GameComponent.seaLevel;
            
            // Calculate actual min/max/mean from extracted elevation data
            float minElev = float.MaxValue;
            float maxElev = float.MinValue;
            float sumElev = 0f;
            int waterCount = 0;
            
            for (int i = 0; i < state.TileCount; i++)
            {
                float elev = state.ElevationData[i];
                minElev = Mathf.Min(minElev, elev);
                maxElev = Mathf.Max(maxElev, elev);
                sumElev += elev;
                if (elev <= 0f) waterCount++;
            }
            
            float meanElev = sumElev / state.TileCount;
            float waterPercent = (waterCount * 100f) / state.TileCount;
            
            if (Controller.DebugEnabled)
                RPLog.D($"[WorldGen] Elevation extracted: {minElev:F1}m to {maxElev:F1}m (mean: {meanElev:F1}m, {waterPercent:F1}% water) | SeaLevel: {seaLevel}");
        }
        
        /// <summary>
        /// Create tiles with extracted elevation and placeholder values.
        /// Climate data will be applied in Phase 3.
        /// 
        /// In RimWorld 1.6, tiles are stored in layer.Tiles, not WorldGrid directly.
        /// We must create NEW Tile objects and add them to layer.Tiles.
        /// </summary>
        private void CreateTilesWithElevation()
        {
            var layer = state.Layer;
            
            // Clear any existing tiles (mimics vanilla WorldGenStep_Tiles)
            layer.Tiles.Clear();
            
            for (int i = 0; i < state.TileCount; i++)
            {
                float elevation = state.ElevationData[i];
                
                // Create a new PlanetTile (represents geometry) and SurfaceTile (represents surface data)
                // PlanetTile is the geometry/position, SurfaceTile is the game data for surface layer
                var planetTile = new PlanetTile(i, layer);
                var tile = new SurfaceTile(planetTile);
                
                // Set elevation
                tile.elevation = elevation;
                
                // Placeholder values (will be replaced in Phase 3)
                tile.hilliness = Hilliness.Flat;
                tile.temperature = 15f;
                tile.rainfall = 1000f;
                tile.swampiness = 0f;
                tile.biome = elevation <= 0f ? BiomeDefOf.Ocean : BiomeDefOf.TemperateForest;
                
                // Add to layer's tile collection
                layer.Tiles.Add(tile);
            }
        }
        
        /// <summary>
        /// Mark the true ocean, then set every other below-sea-level body to the Lake biome.
        /// There is no size cutoff. A later water-biome set can split these lakes again.
        /// </summary>
        private int ConvertIsolatedWaterToLakes()
        {
            var layer = state.Layer;
            var worldGrid = state.WorldGrid;
            int tileCount = state.TileCount;
            bool[] isOceanConnected = state.ClimateData.TrueOceanMask;
            if (isOceanConnected == null)
                return 0;
            bool usedLargest = WaterBodies.MarkTrueOcean(layer, worldGrid, isOceanConnected);
            if (usedLargest && Controller.DebugEnabled)
                RPLog.D("[WorldGen] No edge water. The largest water body is the true ocean.");

            int nextBodyID = 1;
            for (int i = 0; i < tileCount; i++)
            {
                if (isOceanConnected[i])
                    state.ClimateData.WaterBodyID[i] = 0;
            }

            bool[] visited = new bool[tileCount];
            var queue = new Queue<int>();
            int lakesCreated = 0;

            for (int i = 0; i < tileCount; i++)
            {
                var tile = layer.Tiles[i];
                if (tile.elevation > 0f || visited[i] || isOceanConnected[i])
                    continue;

                var cluster = new List<int>();
                queue.Clear();
                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    cluster.Add(current);
                    worldGrid.GetTileNeighbors(current, tempNeighbors);
                    for (int n = 0; n < tempNeighbors.Count; n++)
                    {
                        int neighbor = tempNeighbors[n].tileId;
                        if (neighbor < 0 || neighbor >= tileCount)
                            continue;
                        var nTile = layer.Tiles[neighbor];
                        if (visited[neighbor] || nTile.elevation > 0f || isOceanConnected[neighbor])
                            continue;
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }

                int bodyID = nextBodyID++;
                for (int c = 0; c < cluster.Count; c++)
                {
                    int tileId = cluster[c];
                    state.ClimateData.WaterBodyID[tileId] = bodyID;
                    layer.Tiles[tileId].biome = BiomeDefOf.Lake;
                }
                lakesCreated++;
            }

            return lakesCreated;
        }
        
        /// <summary>
        /// Inserts small, irregular landmasses at the poles to serve as anchors for ice caps.
        /// North and south use Gen.HashCombineInt(seed, 1) and Gen.HashCombineInt(seed, 2), so seed 0 still produces two different poles.
        /// Per-pole base radius varies slightly (DeterministicRng). A morphological pass fills holes and removes scattered edge tiles.
        /// </summary>
        private void InsertPolarLandmasses()
        {
            if (Controller.DebugEnabled)
                RPLog.D("[WorldGen] Inserting polar landmasses...");

            var worldGrid = state.WorldGrid;
            int seed = Find.World.info.Seed;
            int northSeed = Gen.HashCombineInt(seed, 1);
            int southSeed = Gen.HashCombineInt(seed, 2);

            // Per-pole variable size: base radius center 6.5°, offset ±0.5° from the two pole seeds
            const float baseRadiusCenter = 6.5f;
            const float sizeOffsetRange = 0.5f;
            float unitNorth = DeterministicRng.UnitFromSeed(northSeed, DeterministicRng.Salts.PolarLandmassSize);
            float unitSouth = DeterministicRng.UnitFromSeed(southSeed, DeterministicRng.Salts.PolarLandmassSize);
            float baseRadiusNorth = baseRadiusCenter + (unitNorth * 2f - 1f) * sizeOffsetRange;
            float baseRadiusSouth = baseRadiusCenter + (unitSouth * 2f - 1f) * sizeOffsetRange;

            // Two Perlin modules so north and south poles have different shapes (same params, different seed)
            Verse.Noise.ModuleBase polarNoiseNorth = new Verse.Noise.Perlin(1.0, 2.0, 0.5, 3, northSeed, Verse.Noise.QualityMode.Medium);
            Verse.Noise.ModuleBase polarNoiseSouth = new Verse.Noise.Perlin(1.0, 2.0, 0.5, 3, southSeed, Verse.Noise.QualityMode.Medium);

            // Quick cull: max possible base radius + modulation buffer
            float maxBaseRadius = baseRadiusCenter + sizeOffsetRange;
            float cullThreshold = maxBaseRadius + 5f;

            int convertedCount = 0;

            for (int i = 0; i < state.TileCount; i++)
            {
                if (state.ElevationData[i] > 0f) continue;

                Vector2 longLat = worldGrid.LongLatOf(i);
                float lat = longLat.y;
                float distFromPole = 90f - Mathf.Abs(lat);

                if (distFromPole > cullThreshold) continue;

                float baseRadius = lat >= 0f ? baseRadiusNorth : baseRadiusSouth;
                Verse.Noise.ModuleBase polarNoise = lat >= 0f ? polarNoiseNorth : polarNoiseSouth;

                Vector3 pos = worldGrid.GetTileCenter(i);
                float noiseVal = (float)polarNoise.GetValue(pos);
                float effectiveRadius = baseRadius + (noiseVal * 3f);

                if (distFromPole < effectiveRadius)
                {
                    float distFactor = Mathf.Clamp01(distFromPole / effectiveRadius);
                    float baseElevation = Mathf.Lerp(200f, 30f, distFactor * distFactor);
                    float newElevation = baseElevation + noiseVal * 50f;
                    state.ElevationData[i] = Mathf.Max(10f, newElevation);
                    convertedCount++;
                }
            }

            // Morphological pass: more solid shape (fill holes, remove scattered edge) without changing wavy border
            const float polarRegionDist = baseRadiusCenter + 2f; // degrees from pole
            const int fillLandNeighbors = 4;
            const int erodeWaterNeighbors = 4;
            float[] snapshot = new float[state.TileCount];
            Array.Copy(state.ElevationData, snapshot, state.TileCount);

            for (int i = 0; i < state.TileCount; i++)
            {
                float distFromPole = 90f - Mathf.Abs(worldGrid.LongLatOf(i).y);
                if (distFromPole > polarRegionDist) continue;
                if (snapshot[i] > 0f) continue; // already land

                worldGrid.GetTileNeighbors(i, tempNeighbors);
                int landNeighbors = 0;
                for (int j = 0; j < tempNeighbors.Count; j++)
                {
                    int nid = tempNeighbors[j].tileId;
                    if (nid >= 0 && nid < state.TileCount && snapshot[nid] > 0f) landNeighbors++;
                }
                if (landNeighbors >= fillLandNeighbors)
                    state.ElevationData[i] = 10f;
            }

            for (int i = 0; i < state.TileCount; i++)
            {
                float distFromPole = 90f - Mathf.Abs(worldGrid.LongLatOf(i).y);
                if (distFromPole > polarRegionDist) continue;
                if (state.ElevationData[i] <= 0f) continue;

                worldGrid.GetTileNeighbors(i, tempNeighbors);
                int waterNeighbors = 0;
                for (int j = 0; j < tempNeighbors.Count; j++)
                {
                    int nid = tempNeighbors[j].tileId;
                    if (nid >= 0 && nid < state.TileCount && state.ElevationData[nid] <= 0f) waterNeighbors++;
                }
                if (waterNeighbors >= erodeWaterNeighbors)
                    state.ElevationData[i] = -50f;
            }

            if (Controller.DebugEnabled)
                RPLog.D($"[WorldGen] Polar landmass insertion complete - converted {convertedCount} tiles");
        }

    /// <summary>
    /// Normalize ocean depths to standard ranges.
    /// Runs after ConvertIsolatedWaterToLakes has identified True Ocean vs Lakes.
    /// 
    /// Ranges:
    /// - Coastal (water adjacent to land): 0 to -50m
    /// - Inland Lakes (WaterBodyID != 0): -50 to -250m
    /// - Deep Ocean (TrueOceanMask): -50 to -500m
    /// 
    /// This creates consistent depth tiers for gameplay mechanics.
    /// </summary>
    private void NormalizeOceanDepths()
    {
        var worldGrid = state.WorldGrid;
        var layer = state.Layer;
        int tileCount = state.TileCount;
        
        // Use a consistent noise for depth variation
        var depthNoise = new Perlin(0.1, 2.0, 0.5, 4, Find.World.info.Seed, QualityMode.Medium);

        for (int i = 0; i < tileCount; i++)
        {
            float currentElev = state.ElevationData[i];
            
            // Skip land
            if (currentElev >= 0f) continue;
            
            // Determine target depth range
            float minDepth, maxDepth; // min is closer to 0 (higher), max is deeper (lower)
            
            // Check if Coastal (any neighbor is land)
            bool isCoastal = false;
            worldGrid.GetTileNeighbors(i, tempNeighbors);
            for (int j = 0; j < tempNeighbors.Count; j++)
            {
                if (state.ElevationData[tempNeighbors[j].tileId] >= 0f)
                {
                    isCoastal = true;
                    break;
                }
            }
            
            if (isCoastal)
            {
                // Coastal: 0 to -50m
                minDepth = 0f;
                maxDepth = -50f;
            }
            else if (state.ClimateData.WaterBodyID[i] != 0) // Inland Lake (ID != 0)
            {
                // Lakes: -50 to -250m
                minDepth = -50f;
                maxDepth = -250f;
            }
            else // True Ocean (Deep)
            {
                // Deep Ocean: -50 to -500m
                minDepth = -50f;
                maxDepth = -500f;
            }
            
            // Map noise (-1 to 1) to depth range
            // We use the tile position for consistent noise
            Vector3 pos = worldGrid.GetTileCenter(i);
            float n = (float)depthNoise.GetValue(pos); // -1 to 1
            float t = (n + 1f) * 0.5f; // 0 to 1
            
            float newElev = Mathf.Lerp(minDepth, maxDepth, t);
            
            // Apply new elevation to both state data and layer tile
            state.ElevationData[i] = newElev;
            if (layer.Tiles.Count > i) // Safety check
            {
                layer.Tiles[i].elevation = newElev;
            }
        }
    }
        
        // ========== PHASE 2: CLIMATE COMPUTATION ==========
        
        /// <summary>
        /// Phase 2: Compute climate using ClimateOrchestrator.
        /// 
        /// Executes all 11 climate layers via ClimateOrchestrator.
        /// 
        /// Layer execution order (from ClimateOrchestrator):
        /// 1. Geography (includes ridge enhancement, drainage basins)
        /// 2. Temperature
        /// 3. Seasonality
        /// 4. Wind
        /// 5. Moisture
        /// 6. Moisture Advection
        /// 7. Orography
        /// 8. Monsoon
        /// 9. Rainfall
        /// 10. Aridity
        /// 11. Hydrology
        /// 
        /// Note: ClimateOrchestrator handles layer instantiation and execution.
        /// Hydrology toggles are applied during Phase 3 when writing swampiness.
        /// </summary>
        private bool ExecutePhase2_ClimateComputation()
        {
            try
            {
                state.AdvanceToPhase(GenerationPhase.ComputingClimate);
                
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] Phase 2: Computing climate (11 layers)");
                
                // Set temperature baseline values (PoleMean, EquatorMean) from vanilla OverallTemperature
                // These are used by TemperatureLayer for latitude-based temperature interpolation
                SetTemperatureBaselines(state.ClimateData);
                
                // Set rainfall baseline value (BaseRain) from vanilla OverallRainfall or manual override
                // This is used by RainfallLayer as the starting point for rainfall calculations
                SetRainfallBaseline(state.ClimateData, state.ClimateConfig);
                state.ClimateData.EarthGeography = usedEarthHeightmap;
                
                // Create and execute ClimateOrchestrator with state's ClimateData and PlanetLayer
                // ClimateOrchestrator will populate the provided ClimateData in-place
                // Pass Layer so GeographyLayer can read from layer.Tiles (created in Phase 1)
                var orchestrator = new ClimateOrchestrator(state.ClimateData, state.Layer, state.ClimateConfig, state.Features, state.HydroConfig);
                
                // Execute all layers (orchestrator populates state.ClimateData directly)
                orchestrator.Compute();
                Planets.API.RealisticPlanetsAPI.LogContract();

                // Keep the meters this pass used. Round the tile afterwards so vanilla's save matches.
                state.ClimateData.RememberExactElevations(state.Layer);
                BakeQuantizer.QuantizeElevations(state.Layer, null);
                
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] Phase 2 complete - climate computed");
                
                return true;
            }
            catch (Exception e)
            {
                RPLog.Error($"Phase 2 failed: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Set temperature baseline values (PoleMean, EquatorMean).
        /// When climateConfig.manualTemperatureEnabled is true, uses manual polar/equatorial targets from Advanced Climate.
        /// Otherwise uses vanilla OverallTemperature from Find.World.info.overallTemperature (CreateWorldParams page).
        /// 
        /// Calibrated on the Earth heightmap against CRU inland lowland and ERA5 sea surface.
        /// The other steps keep the same spacing around Normal: +10.5°C at the pole and −4°C at the equator
        /// relative to the previous ladder. Final tile temperatures still add seasonality, lapse rate, and coast blend.
        ///
        /// Pole → equator:
        /// - VeryCold: −59.5°C → 6°C
        /// - Cold: −49.5°C → 14°C
        /// - LittleBitColder: −39.5°C → 23°C
        /// - Normal: −29.5°C → 31°C
        /// - LittleBitWarmer: −19.5°C → 38°C
        /// - Hot: −9.5°C → 45°C
        /// - VeryHot: 0.5°C → 51°C
        /// </summary>
        private void SetTemperatureBaselines(ClimateData climateData)
        {
            var climateConfig = state.ClimateConfig;
            if (climateConfig.manualTemperatureEnabled.CurrentValue)
            {
                climateData.PoleMean = climateConfig.manualPolarTemp.CurrentValue;
                climateData.EquatorMean = climateConfig.manualEquatorialTemp.CurrentValue;
                if (Controller.DebugEnabled)
                    RPLog.D($"[WorldGen] Temperature baselines set: Pole={climateData.PoleMean}°C, Equator={climateData.EquatorMean}°C (manual)");
                return;
            }

            var temperature = Find.World.info.overallTemperature;
            
            switch (temperature)
            {
                case OverallTemperature.VeryCold:
                    climateData.PoleMean = -59.5f;
                    climateData.EquatorMean = 6f;
                    break;
                case OverallTemperature.Cold:
                    climateData.PoleMean = -49.5f;
                    climateData.EquatorMean = 14f;
                    break;
                case OverallTemperature.LittleBitColder:
                    climateData.PoleMean = -39.5f;
                    climateData.EquatorMean = 23f;
                    break;
                case OverallTemperature.Normal:
                    climateData.PoleMean = -29.5f;
                    climateData.EquatorMean = 31f;
                    break;
                case OverallTemperature.LittleBitWarmer:
                    climateData.PoleMean = -19.5f;
                    climateData.EquatorMean = 38f;
                    break;
                case OverallTemperature.Hot:
                    climateData.PoleMean = -9.5f;
                    climateData.EquatorMean = 45f;
                    break;
                case OverallTemperature.VeryHot:
                    climateData.PoleMean = 0.5f;
                    climateData.EquatorMean = 51f;
                    break;
                default:
                    climateData.PoleMean = -29.5f;
                    climateData.EquatorMean = 31f;
                    if (Controller.DebugEnabled)
                        RPLog.Warning($"Unknown OverallTemperature value: {temperature}, using Normal defaults");
                    break;
            }
            
            if (Controller.DebugEnabled)
                RPLog.D($"[WorldGen] Temperature baselines set: Pole={climateData.PoleMean}°C, Equator={climateData.EquatorMean}°C (from {temperature})");
        }
        
        /// <summary>
        /// Set rainfall baseline value (BaseRain) based on vanilla OverallRainfall or manual override.
        /// When climateConfig.manualRainfallEnabled is true, uses manualBaseRain from Advanced Climate.
        /// Otherwise reads from Find.World.info.overallRainfall set by CreateWorldParams page.
        /// 
        /// Mappings assigned below:
        /// - AlmostNone: 250mm
        /// - Little: 500mm
        /// - LittleBitLess: 750mm
        /// - Normal: 1000mm
        /// - LittleBitMore: 1250mm
        /// - High: 1500mm
        /// - VeryHigh: 1750mm
        /// </summary>
        private void SetRainfallBaseline(ClimateData climateData, ClimateConfiguration climateConfig)
        {
            if (climateConfig != null && climateConfig.manualRainfallEnabled.CurrentValue)
            {
                climateData.BaseRain = climateConfig.manualBaseRain.CurrentValue;
                if (Controller.DebugEnabled)
                    RPLog.D($"[WorldGen] Rainfall baseline set: BaseRain={climateData.BaseRain}mm/year (manual override)");
                return;
            }

            var rainfall = Find.World.info.overallRainfall;
            
            switch (rainfall)
            {
                case OverallRainfall.AlmostNone:
                    climateData.BaseRain = 200f;
                    break;
                case OverallRainfall.Little:
                    climateData.BaseRain = 400f;
                    break;
                case OverallRainfall.LittleBitLess:
                    climateData.BaseRain = 700f;
                    break;
                case OverallRainfall.Normal:
                    climateData.BaseRain = 1000f;
                    break;
                case OverallRainfall.LittleBitMore:
                    climateData.BaseRain = 1400f;
                    break;
                case OverallRainfall.High:
                    climateData.BaseRain = 1900f;
                    break;
                case OverallRainfall.VeryHigh:
                    climateData.BaseRain = 2600f;
                    break;
                default:
                    // Fallback to Normal (Earth-like)
                    climateData.BaseRain = 1000f;
                    if (Controller.DebugEnabled)
                        RPLog.Warning($"Unknown OverallRainfall value: {rainfall}, using Normal default (1000mm)");
                    break;
            }
            
            if (Controller.DebugEnabled)
                RPLog.D($"[WorldGen] Rainfall baseline set: BaseRain={climateData.BaseRain}mm/year (from {rainfall})");
        }
        
        // ========== PHASE 3: DATA APPLICATION ==========
        
        /// <summary>
        /// Phase 3: Apply climate data to tiles and assign biomes.
        /// 
        /// Steps:
        /// 1. Apply temperature/rainfall to ALL tiles (land and water)
        /// 2. Apply hilliness to land tiles only (from GeographyLayer)
        /// 3. Apply swampiness:
        ///    - If hydrology enabled: use ClimateData.Swampiness (from HydrologyLayer)
        ///    - If hydrology disabled: calculate fallback swampiness heuristic
        /// 4. Assign biomes via BiomeWorker competition (handles ocean/sea ice)
        /// 
        /// Water tiles get climate data too:
        /// - Temperature = Sea Surface Temperature (SST) for sea ice spawning
        /// - Rainfall = Oceanic rainfall (not used by vanilla but available)
        /// 
        /// Biome selection keeps the closest allowed biome (lowest fit energy).
        /// Foreign workers compete only when their own score is positive.
        /// </summary>
        private bool ExecutePhase3_DataApplication()
        {
            try
            {
                state.AdvanceToPhase(GenerationPhase.ApplyingData);

                // Inject GenericRP2Worker for external biomes that are calibrated or overridden
                if (Controller.Settings?.biomeConfig != null)
                    BiomeWorkerManager.Apply(Controller.Settings.biomeConfig);
                
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] Phase 3: Applying climate data and assigning biomes");

                foreignScorePeaks.Clear();
                biomeWinCounts.Clear();
                foreignWinCounts.Clear();
                fallbackTileSamples.Clear();
                loggedWorkerErrors.Clear();
                Array.Clear(energyBuckets, 0, energyBuckets.Length);
                Array.Clear(rainfallBuckets, 0, rainfallBuckets.Length);
                fallbackTileCount = 0;
                foreignWinCount = 0;
                deadZoneCount = 0;
                
                var layer = state.Layer;
                var climateData = state.ClimateData;
                bool hydrologyEnabled = state.Features.earthScaleHydrology.CurrentValue;
                
                // Apply climate data to ALL tiles (land and water) before any biome reads tile fields.
                for (int i = 0; i < state.TileCount; i++)
                {
                    try
                    {
                        var tile = layer.Tiles[i];
                        // Use ClimateData.OceanMask instead of checking tile.biome (which hasn't been set yet)
                        bool isWater = climateData.OceanMask[i];
                        
                        // 1. Apply temperature to ALL tiles (land and water)
                        // Water tiles get SST (Sea Surface Temperature) for sea ice spawning
                        tile.temperature = climateData.AnnualMeanTemp[i];
                        
                        // 2. Apply rainfall to ALL tiles
                        tile.rainfall = Mathf.Max(0f, climateData.AnnualRainfall[i]);
                        
                        // 3. Apply hilliness to LAND tiles only (water is always flat)
                        // Hilliness comes from HillinessAnalyzer (includes ridge enhancement, coastal flattening, etc.)
                        if (!isWater)
                        {
                            tile.hilliness = climateData.HillinessClassification[i];
                        }
                        else
                        {
                            tile.hilliness = Hilliness.Flat;
                        }
                        
                        // 4. Apply swampiness to LAND tiles only
                        if (!isWater)
                        {
                            if (hydrologyEnabled)
                            {
                                // Use HydrologyLayer computed swampiness
                                tile.swampiness = climateData.Swampiness?[i] ?? 0f;
                            }
                            else
                            {
                                // Calculate fallback swampiness heuristic
                                tile.swampiness = CalculateFallbackSwampiness(i, tile);
                                // Sync to cache so Climate.Swampiness() returns correct value
                                if (climateData.Swampiness != null && i < climateData.Swampiness.Length)
                                {
                                    climateData.Swampiness[i] = tile.swampiness;
                                }
                            }
                        }
                        else
                        {
                            tile.swampiness = 0f; // Water tiles have no swampiness
                        }
                    }
                    catch (Exception ex)
                    {
                        RPLog.Error($"Phase 3 failed at tile {i}: {ex.Message}\n{ex.StackTrace}");
                        throw;  // Re-throw to trigger outer catch
                    }
                }

                // Match the tile and scoring values a save will reload, before biomes are chosen.
                BakeQuantizer.QuantizeAppliedClimate(layer, climateData);

                if (BeforeBiomeSelection != null)
                {
                    try
                    {
                        BeforeBiomeSelection();
                    }
                    catch (Exception e)
                    {
                        RPLog.Error($"BeforeBiomeSelection failed: {e.Message}\n{e.StackTrace}");
                    }
                }

                CollectForeignScorePeaks(layer);

                bool tier2Lakes = state.Features?.IsTier2HydrologyEnabled == true
                    && state.HydroConfig?.LakeConversionEnabled?.CurrentValue != false
                    && climateData.WaterBodyType != null;

                for (int i = 0; i < state.TileCount; i++)
                {
                    try
                    {
                        var tile = layer.Tiles[i];
                        PlanetTile planetTile = new PlanetTile(i, layer);
                        if (tier2Lakes && i < climateData.WaterBodyType.Length && climateData.WaterBodyType[i] == 2 && !climateData.OceanMask[i])
                        {
                            // Above-sea-level basins are not water-covered, so the Sea Ice worker cannot claim them.
                            // A summer that stays below 0°C is ice; a summer that reaches 0°C stays open water.
                            bool frozen = climateData.MaxSummerTemp != null
                                && i < climateData.MaxSummerTemp.Length
                                && climateData.MaxSummerTemp[i] < 0f;
                            BiomeDef standing = frozen ? BiomeDefOf.SeaIce : BiomeDefOf.Lake;
                            tile.biome = standing;
                            tile.hilliness = Hilliness.Flat;
                            tile.swampiness = 0f;
                            if (climateData.HillinessClassification != null && i < climateData.HillinessClassification.Length)
                                climateData.HillinessClassification[i] = Hilliness.Flat;
                            if (climateData.Swampiness != null && i < climateData.Swampiness.Length)
                                climateData.Swampiness[i] = 0f;
                            biomeWinCounts.TryGetValue(standing.defName, out int lc);
                            biomeWinCounts[standing.defName] = lc + 1;
                            if (frozen)
                                ClampSeaIceRainfall(tile, climateData, i);
                            continue;
                        }
                        tile.biome = SelectBestBiome(tile, planetTile);
                        ClampSeaIceRainfall(tile, climateData, i);
                    }
                    catch (Exception ex)
                    {
                        RPLog.Error($"Phase 3 biome selection failed at tile {i}: {ex.Message}\n{ex.StackTrace}");
                        throw;
                    }
                }

                LogSelectionSummary();
                climateData.bakeRecord.Capture(state.ClimateConfig, state.HydroConfig, "Generation");
                climateData.bakeRecord.earthHeightmap = usedEarthHeightmap;
                climateData.ArraysReady = true;
                
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] Phase 3 complete - data applied and biomes assigned");
                
                return true;
            }
            catch (Exception e)
            {
                RPLog.Error($"Phase 3 failed: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// Sea Ice stands in for a frozen polar desert, but the moisture solve can leave rainforest totals on it.
        /// Clamp the published rainfall to the Ice Sheet band, 100–250 mm.
        /// </summary>
        private static void ClampSeaIceRainfall(Tile tile, ClimateData climateData, int tileId)
        {
            if (tile?.biome != BiomeDefOf.SeaIce)
                return;
            float maxAllowedRain = DeterministicRng.Range(tileId, 100f, 250f, DeterministicRng.Salts.SeaIceExtent);
            tile.rainfall = Mathf.Min(tile.rainfall, maxAllowedRain);
            BakeQuantizer.QuantizeRainfallTile(tile, climateData.AnnualRainfall, tileId);
        }

        /// <summary>
        /// The Advanced Biomes checkbox applies to every biome that has metadata, including vanilla.
        /// Ocean and Lake are not chosen through this flag: on water, the Phase 1 ocean or lake is a baseline candidate.
        /// </summary>
        private bool IsBiomeEnabled(BiomeDef biomeDef)
        {
            // Check biome configuration if available
            if (Controller.Settings?.biomeConfig != null)
            {
                return Controller.Settings.biomeConfig.IsBiomeEnabled(biomeDef.defName);
            }
            
            // If settings aren't loaded yet, default to enabled (shouldn't happen during world gen)
            return true;
        }

        /// <summary>
        /// Calculate fallback swampiness when hydrology system is disabled.
        /// Uses climate-based heuristic combining elevation, humidity, temperature, and hilliness.
        /// 
        /// Swamp requirements:
        /// - Low elevation (<500m) - swamps form in lowlands
        /// - High humidity (AI >0.65, rainfall >600mm) - need sufficient moisture
        /// - Moderate temperature (5-25°C optimal) - too cold/hot inhibits swamps
        /// - Flat terrain - hilliness penalty
        /// 
        /// Design: Geometric mean prevents "one high factor compensates for others".
        /// All factors must be present for swamp formation.
        /// </summary>
        private float CalculateFallbackSwampiness(int tileId, Tile tile)
        {
            var climateData = state.ClimateData;
            
            float elevation = climateData.ElevationKm?[tileId] ?? 0f; // in km
            float rainfall = climateData.AnnualRainfall?[tileId] ?? 0f;
            float aridityIndex = climateData.AridityIndex?[tileId] ?? 0f;
            float temperature = climateData.AnnualMeanTemp?[tileId] ?? 15f;
            
            // 1. Elevation factor (0-1, higher at lower elevations)
            float elevFactor = 0f;
            if (elevation < 0.5f) // Below 500m
            {
                elevFactor = Mathf.Clamp01(1f - elevation / 0.5f);
            }
            
            // 2. Humidity factor (0-1, higher with more rainfall and higher aridity index)
            // Threshold lowered from 0.8 to 0.65 to allow sub-humid swamps (Florida, Louisiana)
            float humidityFactor = 0f;
            if (aridityIndex > 0.65f && rainfall > 600f)
            {
                humidityFactor = Mathf.Clamp01((aridityIndex - 0.65f) / 1.35f);
            }
            
            // 3. Temperature factor (0-1, optimal 5-25°C)
            float tempFactor = 0f;
            if (temperature > -5f && temperature < 30f)
            {
                if (temperature >= 5f && temperature <= 25f)
                {
                    tempFactor = 1f; // Peak swamp temperature range
                }
                else if (temperature < 5f)
                {
                    tempFactor = (temperature + 5f) / 10f; // Ramp from -5 to 5
                }
                else // temperature > 25
                {
                    tempFactor = (30f - temperature) / 5f; // Ramp from 25 to 30
                }
            }
            
            // 4. Hilliness penalty (swamps are flat)
            float hillinessFactor = tile.hilliness switch
            {
                Hilliness.Flat => 1f,
                Hilliness.SmallHills => 0.7f,
                Hilliness.LargeHills => 0.3f,
                Hilliness.Mountainous => 0.1f,
                Hilliness.Impassable => 0f,
                _ => 0.5f
            };
            
            // 5. Combine all factors using geometric mean
            // Geometric mean ensures ALL factors must be present (no compensation)
            float swampiness = Mathf.Pow(
                elevFactor * humidityFactor * tempFactor * hillinessFactor,
                0.25f // 4th root for 4 factors
            );
            
            // 6. Add deterministic pseudo-random spatial variation for patchiness
            // Creates natural-looking swamp distribution rather than uniform coverage
            float noiseVariation = DeterministicRng.Range(tileId, 0.5f, 1.5f, DeterministicRng.Salts.SwampinessPatchiness);
            swampiness *= noiseVariation;
            
            // 7. Scale down to make swamps rarer (~5-10% of suitable tiles)
            swampiness *= 0.6f;
            
            return Mathf.Clamp01(swampiness);
        }
        
        /// <summary>
        /// Pick the allowed biome with the lowest fit energy.
        /// A failed gate is not a candidate. Frequency 0 removes a biome.
        /// Frequency otherwise shifts energy by -2*ln(frequency), which is what multiplying a positive score used to do.
        /// Ocean and Lake are not scored. On water, the Phase 1 ocean or lake remains unless some other biome fits better than a score of 1.
        /// </summary>
        private BiomeDef SelectBestBiome(Tile tile, PlanetTile planetTile)
        {
            var config = Controller.Settings?.biomeConfig;
            float bestEnergy = float.MaxValue;
            BiomeDef bestBiome = null;
            bool bestIsForeign = false;

            List<BiomeSelector.CandidateScore> candidates = BiomeSelector.EvaluateCandidates(
                tile, planetTile, state.Layer, foreignScorePeaks, loggedWorkerErrors);
            if (candidates.Count > 0)
            {
                bestBiome = candidates[0].Biome;
                bestEnergy = candidates[0].Energy;
                bestIsForeign = candidates[0].IsForeign;
            }

            if (tile.WaterCovered)
            {
                BiomeDef baseline = tile.biome == BiomeDefOf.Lake ? BiomeDefOf.Lake : BiomeDefOf.Ocean;
                if (baseline != null && WaterBaselineEnergy < bestEnergy)
                {
                    bestEnergy = WaterBaselineEnergy;
                    bestBiome = baseline;
                    bestIsForeign = false;
                }
            }

            if (bestBiome == null)
                bestBiome = SelectFallbackBiome(tile, planetTile, config, out bestEnergy);

            if (bestBiome != null)
            {
                string winner = bestBiome.defName;
                biomeWinCounts.TryGetValue(winner, out int wins);
                biomeWinCounts[winner] = wins + 1;
                if (bestIsForeign)
                {
                    foreignWinCount++;
                    foreignWinCounts.TryGetValue(winner, out int foreignWins);
                    foreignWinCounts[winner] = foreignWins + 1;
                }
                if (!tile.WaterCovered)
                    RecordLandSelection(bestEnergy, tile.rainfall);
            }

            return bestBiome ?? BiomeDefOf.TemperateForest;
        }

        /// <summary>
        /// Used only when every land biome failed a gate. Picks the closest land biome with gates ignored.
        /// </summary>
        private BiomeDef SelectFallbackBiome(Tile tile, PlanetTile planetTile, BiomeConfiguration config, out float bestEnergy)
        {
            fallbackTileCount++;
            if (fallbackTileSamples.Count < 20)
                fallbackTileSamples.Add(planetTile.tileId);

            bestEnergy = float.MaxValue;
            BiomeDef bestBiome = null;

            foreach (BiomeDef biomeDef in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                if (!CanCompete(biomeDef))
                    continue;
                if (!(biomeDef.Worker is BiomeWorkerBase rpWorker) || rpWorker.HasCustomScore)
                    continue;
                if (rpWorker.PlacesOnWater || rpWorker.RequiresHost)
                    continue;
                if (!rpWorker.CanPlaceOnLayer(biomeDef, state.Layer))
                    continue;

                float frequency = config != null ? config.GetCommonalityMultiplier(biomeDef.defName) : 1f;
                if (frequency <= 0f)
                    continue;

                BiomeFit fit = rpWorker.EvaluateFit(biomeDef, tile, planetTile, ignoreGates: true);
                if (!fit.Viable)
                    continue;

                float energy = fit.Energy - 2f * Mathf.Log(frequency);
                if (energy < bestEnergy)
                {
                    bestEnergy = energy;
                    bestBiome = biomeDef;
                }
            }

            return bestBiome ?? BiomeDefOf.TemperateForest;
        }

        private bool CanCompete(BiomeDef biomeDef)
        {
            if (biomeDef == null || !biomeDef.implemented || !biomeDef.generatesNaturally || biomeDef.workerClass == null)
                return false;
            return IsBiomeEnabled(biomeDef);
        }

        /// <summary>
        /// One pass so a foreign biome's best score on this world is known before selection.
        /// A tile where that worker scores its peak then ties a perfect RP2 fit.
        /// </summary>
        private void CollectForeignScorePeaks(PlanetLayer layer)
        {
            foreach (BiomeDef biomeDef in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                if (!CanCompete(biomeDef))
                    continue;
                if (biomeDef == BiomeDefOf.Ocean || biomeDef == BiomeDefOf.Lake)
                    continue;

                BiomeWorker worker = biomeDef.Worker;
                if (worker == null || !worker.CanPlaceOnLayer(biomeDef, state.Layer))
                    continue;
                if (worker is BiomeWorkerBase rpWorker && !rpWorker.HasCustomScore)
                    continue;

                float peak = ForeignPeaks.Resolve(biomeDef, worker, layer);
                if (peak > 0f)
                    foreignScorePeaks[biomeDef.defName] = peak;
            }
        }

        private void RecordLandSelection(float energy, float rainfall)
        {
            int energyBucket = energy < 0.5f ? 0
                : energy < 1f ? 1
                : energy < 2f ? 2
                : energy < 4f ? 3
                : energy < 8f ? 4
                : 5;
            energyBuckets[energyBucket]++;
            if (energy > 8f)
                deadZoneCount++;

            int rainBucket = rainfall <= 1000f ? 0
                : rainfall <= 2000f ? 1
                : rainfall <= 3000f ? 2
                : rainfall <= 4000f ? 3
                : rainfall <= 4400f ? 4
                : rainfall <= 6000f ? 5
                : rainfall <= 8000f ? 6
                : 7;
            if (rainBucket < rainfallBuckets.Length)
                rainfallBuckets[rainBucket]++;
        }

        private void LogSelectionSummary()
        {
            int tileWins = 0;
            foreach (var count in biomeWinCounts.Values)
                tileWins += count;

            var report = new System.Text.StringBuilder();
            report.AppendLine("metric,value");
            report.AppendLine("tiles," + tileWins);
            report.AppendLine("biomes," + biomeWinCounts.Count);
            report.AppendLine("foreign_wins," + foreignWinCount);
            report.AppendLine("gate_fallbacks," + fallbackTileCount);
            report.AppendLine("land_winner_energy_above_8," + deadZoneCount);
            report.AppendLine("energy_below_0.5," + energyBuckets[0]);
            report.AppendLine("energy_0.5_to_1," + energyBuckets[1]);
            report.AppendLine("energy_1_to_2," + energyBuckets[2]);
            report.AppendLine("energy_2_to_4," + energyBuckets[3]);
            report.AppendLine("energy_4_to_8," + energyBuckets[4]);
            report.AppendLine("energy_above_8," + energyBuckets[5]);
            report.AppendLine("land_rain_to_1000," + rainfallBuckets[0]);
            report.AppendLine("land_rain_to_2000," + rainfallBuckets[1]);
            report.AppendLine("land_rain_to_3000," + rainfallBuckets[2]);
            report.AppendLine("land_rain_to_4000," + rainfallBuckets[3]);
            report.AppendLine("land_rain_to_4400," + rainfallBuckets[4]);
            report.AppendLine("land_rain_to_6000," + rainfallBuckets[5]);
            report.AppendLine("land_rain_to_8000," + rainfallBuckets[6]);
            report.AppendLine("land_rain_above_8000," + rainfallBuckets[7]);
            foreach (var kvp in biomeWinCounts)
                report.AppendLine("wins_" + kvp.Key + "," + kvp.Value);
            foreach (var kvp in foreignWinCounts)
                report.AppendLine("foreign_wins_" + kvp.Key + "," + kvp.Value);
            if (fallbackTileSamples.Count > 0)
                report.AppendLine("first_fallback_tiles," + string.Join(" ", fallbackTileSamples));
            AppendLandRainfallLevel(report);
            LastSelectionReport = report.ToString();

            if (!Controller.DebugEnabled)
                return;

            RPLog.D($"[WorldGen] Biome selection: {tileWins} tiles, {biomeWinCounts.Count} biomes, {foreignWinCount} foreign wins, {fallbackTileCount} gate fallbacks, {deadZoneCount} land tiles with winner energy above 8.");
            RPLog.D("[WorldGen] Winner energy buckets (<0.5, <1, <2, <4, <8, >=8): " + string.Join(", ", energyBuckets));
            RPLog.D("[WorldGen] Land rainfall buckets (<=1000, 2000, 3000, 4000, 4400, 6000, 8000, above): " + string.Join(", ", rainfallBuckets));
            if (fallbackTileSamples.Count > 0)
                RPLog.D("[WorldGen] First fallback tiles: " + string.Join(", ", fallbackTileSamples));
        }

        /// <summary>
        /// Land median and mean of annual rainfall. WS12-13 step 1 reads this line.
        /// </summary>
        private void AppendLandRainfallLevel(System.Text.StringBuilder report)
        {
            float[] rain = state.ClimateData?.AnnualRainfall;
            bool[] ocean = state.ClimateData?.OceanMask;
            if (rain == null || ocean == null)
                return;
            int count = Math.Min(rain.Length, ocean.Length);
            var land = new System.Collections.Generic.List<float>();
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                if (ocean[i])
                    continue;
                land.Add(rain[i]);
                sum += rain[i];
            }
            if (land.Count == 0)
                return;
            land.Sort();
            float median = (land.Count % 2 == 1)
                ? land[land.Count / 2]
                : (land[land.Count / 2 - 1] + land[land.Count / 2]) * 0.5f;
            float mean = (float)(sum / land.Count);
            var info = Find.World?.info;
            report.AppendLine("climate_model," + ClimateModelVersion.Current);
            report.AppendLine("overall_rainfall," + (info != null ? info.overallRainfall.ToString() : ""));
            report.AppendLine("coverage," + (info != null ? info.planetCoverage.ToString("F2") : ""));
            report.AppendLine("subcount," + Planets_GameComponent.subcount);
            report.AppendLine("land_rain_tiles," + land.Count);
            report.AppendLine("land_rain_median_mm," + median.ToString("F1"));
            report.AppendLine("land_rain_mean_mm," + mean.ToString("F1"));
            Log.Message("[RP] WS12-13 land rainfall: model " + ClimateModelVersion.Current
                + ", " + (info != null ? info.overallRainfall.ToString() : "?")
                + ", coverage " + (info != null ? info.planetCoverage.ToString("P0") : "?")
                + ", scale " + Planets_GameComponent.subcount
                + ", land " + land.Count
                + ", median " + median.ToString("F1") + " mm"
                + ", mean " + mean.ToString("F1") + " mm");
        }
        
        // ========== UTILITY ==========
        
        /// <summary>
        /// Temp list for neighbor queries (reused to avoid allocations).
        /// </summary>
        private static readonly List<PlanetTile> tempNeighbors = new List<PlanetTile>();
    }
}
