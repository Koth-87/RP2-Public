using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Utilities.Logging;
using Planets.Utilities.WorldGen;
using Planets.WorldGen;
using Planets.WorldGen.Geography;
using Planets.WorldGen.TerrainAnalysis;
using Planets.WorldGen.Constants;
using Planets.Core.Configuration;

namespace Planets.WorldGen.Climate.Layers
{
    /// <summary>
    /// Computes geographic foundation data required by subsequent climate layers:
    /// spherical coordinates, open-ocean and enclosed-water masks, multi-source BFS ocean distances,
    /// nearest-coast continentality, local terrain relief/ruggedness/steepness, hybrid terrain analysis
    /// (stratification, smoothing, prominence, Tier 1 fill-and-spill topology drainage, basin aggregation,
    /// watershed ridge detection and orientation), and cascading-threshold hilliness classification.
    /// </summary>
    public class GeographyLayer : IClimateLayer
    {
        private ClimateData climate;
        private PlanetLayer layer;
        private int tileCount;
        private ClimateConfiguration settings;
        private TerrainAnalysisOrchestrator terrainOrchestrator;
        private HillinessAnalyzer hillinessAnalyzer;

        // Reusable buffer for neighbors
        private readonly List<PlanetTile> neighborBuffer = new List<PlanetTile>();
        
        // Reusable buffers for BFS (GetTilesWithinRings)
        private readonly HashSet<int> bfsResult = new HashSet<int>();
        private readonly List<int> bfsCurrentRing = new List<int>();
        private readonly List<int> bfsNextRing = new List<int>();

        /// <summary>
        /// Layer name for logging and debugging
        /// </summary>
        public string LayerName => "Geography";
        
        public GeographyLayer(ClimateData climateData, PlanetLayer planetLayer, ClimateConfiguration climateConfig = null)
        {
            climate = climateData;
            layer = planetLayer ?? throw new ArgumentNullException(nameof(planetLayer));
            settings = climateConfig ?? new ClimateConfiguration();
            tileCount = climate.Latitude.Length;
            terrainOrchestrator = new TerrainAnalysisOrchestrator();
            hillinessAnalyzer = new HillinessAnalyzer();
        }

        /// <summary>
        /// Execute all geography computation steps in sequence (IClimateLayer interface)
        /// </summary>
        public void Execute(ClimateData climateData)
        {
            ComputeGeography();
        }

        /// <summary>
        /// Internal method: Execute all geography computation steps in sequence
        /// </summary>
        private void ComputeGeography()
        {
            var rest = Stopwatch.StartNew();
            // Base geometry (tile centers and coordinates)
            ComputeBaseGeometry();

            // Ocean classification
            ComputeOceanMask();
            EnsureTrueOcean();

            // Distance to the true ocean. Lakes stay on OceanMask and are not seeds.
            ComputeOceanDistances();

            // Enclosed water, sized by area. Does not replace the true-ocean distance.
            ComputeInlandWater();
            LogOceanShare();

            // Elevation extraction
            ComputeElevationData();
            
            // Continentality (normalized from ocean distance)
            ComputeContinentality();
            
            // Relief and ruggedness (local terrain metrics)
            ComputeReliefAndRuggedness();
            
            // Steepness (average gradient to neighbors)
            ComputeSteepness();
            rest.Stop();
            if (Planets.Core.Controller.DebugEnabled)
                RPLog.D($"[Geography] Setup before terrain analysis took {rest.ElapsedMilliseconds} ms");

            // Hybrid terrain analysis (replaces old basin/ridge systems)
            // Executes terrain analysis steps: stratification, smoothing, prominence, flow, basins, validation
            terrainOrchestrator.Execute(climate, Find.WorldGrid);
            
            // Hilliness classification
            // Uses Relief/Ruggedness (from here) and Prominence/Peaks (from TerrainAnalysisOrchestrator)
            var hills = Stopwatch.StartNew();
            hillinessAnalyzer.Execute(climate, Find.WorldGrid);
            hills.Stop();
            if (Planets.Core.Controller.DebugEnabled)
                RPLog.D($"[Geography] Hilliness took {hills.ElapsedMilliseconds} ms");

            if (Planets.Core.Controller.DebugEnabled)
            {
                RPLog.D($"[Geography] Computed base geography for {tileCount} tiles");
            }
        }

        // ========== BASE GEOMETRY ==========

        /// <summary>
        /// Compute tile centers and basic coordinates
        /// Sets: Latitude, Longitude, LatitudeSigned, TileCenters
        /// </summary>
        private void ComputeBaseGeometry()
        {
            for (int i = 0; i < tileCount; i++)
            {
                Vector2 longLat = Find.WorldGrid.LongLatOf(i);
                Vector3 center = Find.WorldGrid.GetTileCenter(i).normalized;

                // Store absolute latitude (0 to π/2)
                climate.Latitude[i] = Mathf.Abs(longLat.y) * Mathf.Deg2Rad;
                
                // Store signed latitude (-π/2 to π/2) for hemisphere detection
                climate.LatitudeSigned[i] = longLat.y * Mathf.Deg2Rad;
                
                // Store longitude (0 to 2π)
                climate.Longitude[i] = longLat.x * Mathf.Deg2Rad;

                // Store tile center as unit vector for distance calculations
                climate.TileCenters[i] = center;
            }
        }

        // ========== OCEAN CLASSIFICATION ==========

        /// <summary>
        /// Mark ocean tiles
        /// Sets: OceanMask
        /// 
        /// Ocean = any tile with Ocean or Lake biome (elevation <= 0)
        /// WaterCovered property isn't set yet during worldgen, so check biome directly.
        /// 
        /// NOTE: Reads from layer.Tiles (created in base geometry step), not Find.WorldGrid.
        /// </summary>
        private void ComputeOceanMask()
        {
            for (int i = 0; i < tileCount; i++)
            {
                Tile tile = layer.Tiles[i];
                // WaterCovered isn't set during worldgen - check biome and elevation instead
                climate.OceanMask[i] = (tile.biome == BiomeDefOf.Ocean || tile.biome == BiomeDefOf.Lake || tile.elevation <= 0f);
            }
        }

        // ========== OCEAN DISTANCE (MULTI-SOURCE BFS) ==========

        /// <summary>
        /// Phase 1 marks the true ocean. A rebuilt climate grid starts blank, so fill it here when it is empty.
        /// </summary>
        private void EnsureTrueOcean()
        {
            if (climate.TrueOceanMask == null || climate.TrueOceanMask.Length < tileCount || layer == null)
                return;
            for (int i = 0; i < tileCount; i++)
            {
                if (climate.TrueOceanMask[i])
                    return;
            }
            WorldGrid grid = Find.WorldGrid;
            if (grid == null)
                return;
            WaterBodies.MarkTrueOcean(layer, grid, climate.TrueOceanMask);
        }

        private void LogOceanShare()
        {
            if (!Planets.Core.Controller.DebugEnabled || climate.OceanMask == null)
                return;
            int ocean = 0, enclosed = 0, land = 0;
            bool trueKnown = climate.TrueOceanMask != null && climate.TrueOceanMask.Length >= tileCount;
            for (int i = 0; i < tileCount; i++)
            {
                if (!climate.OceanMask[i])
                    land++;
                else if (trueKnown && climate.TrueOceanMask[i])
                    ocean++;
                else
                    enclosed++;
            }
            RPLog.D($"[Geography] Tiles: true ocean {ocean}, enclosed water {enclosed}, land {land}.");
        }

        /// <summary>
        /// Compute distance to the nearest true-ocean tile.
        /// Sets: OceanDistKm, NearestOceanTile
        /// 
        /// Algorithm: Breadth-first search propagating from all ocean tiles
        /// Distance metric: Arc distance along planet surface in km
        /// Cutoff: 4000 km (Earth-scale reasonable limit)
        /// </summary>
        private void ComputeOceanDistances()
        {
            Queue<int> queue = new Queue<int>();
            
            // Use arrays for O(1) access instead of Dictionary
            float[] distances = new float[tileCount];
            bool[] visited = new bool[tileCount];

            int oceanCount = 0;
            
            // Initialize: start from true ocean. Lakes are water, and they are not the coast.
            bool[] seeds = climate.TrueOceanMask != null && climate.TrueOceanMask.Length >= climate.OceanMask.Length
                ? climate.TrueOceanMask
                : climate.OceanMask;
            for (int i = 0; i < seeds.Length && i < tileCount; i++)
            {
                if (seeds[i])
                {
                    queue.Enqueue(i);
                    distances[i] = 0f;
                    visited[i] = true;
                    climate.NearestOceanTile[i] = i;
                    oceanCount++;
                }
            }

            if (Planets.Core.Controller.DebugEnabled)
            {
                RPLog.D($"[Geography] Found {oceanCount} ocean tiles for distance calculation");
            }

            // BFS: Propagate distance to neighbors
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                float currentDist = distances[current];

                // Get neighbors of current tile using reusable buffer
                neighborBuffer.Clear();
                Find.WorldGrid.GetTileNeighbors(current, neighborBuffer);

                foreach (PlanetTile neighborTile in neighborBuffer)
                {
                    int neighbor = neighborTile.tileId;
                    
                    // Skip if already visited
                    if (visited[neighbor])
                        continue;

                    // Calculate arc distance in km
                    float arcKm = ClimateGeometryHelpers.ArcDistanceKm(climate.TileCenters[current], climate.TileCenters[neighbor]);
                    float newDist = currentDist + arcKm;

                    distances[neighbor] = newDist;
                    visited[neighbor] = true;
                    climate.NearestOceanTile[neighbor] = climate.NearestOceanTile[current];

                    // Continue search up to reasonable distance
                    // 4000km covers most continental interiors at Earth scale
                    if (newDist < EarthScaleConstants.OCEAN_DISTANCE_MAX_KM)
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Fill result arrays
            for (int i = 0; i < climate.OceanDistKm.Length; i++)
            {
                if (visited[i])
                {
                    climate.OceanDistKm[i] = distances[i];
                }
                else
                {
                    // Unreachable (very rare on Earth) - set to max distance
                    climate.OceanDistKm[i] = EarthScaleConstants.OCEAN_DISTANCE_MAX_KM;
                    climate.NearestOceanTile[i] = -1;
                }
            }
        }

        /// <summary>
        /// Size each enclosed water body, then keep each tile's strongest shore influence.
        /// A one-tile lake is a few percent. A sea-sized body levels off near full strength.
        /// </summary>
        private void ComputeInlandWater()
        {
            if (climate.InlandBodyStrength == null || climate.InlandBodyStrength.Length < tileCount)
                return;

            var bodies = new List<WaterBodies.EnclosedBody>();
            WorldGrid grid = Find.WorldGrid;
            if (grid == null)
                return;
            WaterBodies.MeasureEnclosed(layer, grid, climate.TrueOceanMask, climate.InlandBodyStrength, bodies);
            LogEnclosedBodies(bodies);

            float[] influence = climate.InlandWaterInfluence;
            float[] distance = climate.InlandWaterDistKm;
            if (influence == null || distance == null || influence.Length < tileCount || distance.Length < tileCount)
                return;

            var inQueue = new bool[tileCount];
            var queue = new Queue<int>();
            for (int i = 0; i < tileCount; i++)
            {
                influence[i] = climate.InlandBodyStrength[i];
                distance[i] = influence[i] > 0f ? 0f : -1f;
                if (influence[i] > 0f)
                {
                    queue.Enqueue(i);
                    inQueue[i] = true;
                }
            }

            float efold = WaterBodies.InlandWaterEfoldKm;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                inQueue[current] = false;
                float currentInfluence = influence[current];
                if (currentInfluence < 1e-4f)
                    continue;

                neighborBuffer.Clear();
                grid.GetTileNeighbors(current, neighborBuffer);
                for (int n = 0; n < neighborBuffer.Count; n++)
                {
                    int neighbor = neighborBuffer[n].tileId;
                    if (neighbor < 0 || neighbor >= tileCount)
                        continue;
                    float arcKm = ClimateGeometryHelpers.ArcDistanceKm(climate.TileCenters[current], climate.TileCenters[neighbor]);
                    float nextInfluence = currentInfluence * Mathf.Exp(-arcKm / efold);
                    if (nextInfluence <= influence[neighbor] + 1e-5f)
                        continue;
                    influence[neighbor] = nextInfluence;
                    distance[neighbor] = distance[current] + arcKm;
                    if (!inQueue[neighbor])
                    {
                        queue.Enqueue(neighbor);
                        inQueue[neighbor] = true;
                    }
                }
            }
        }

        private static void LogEnclosedBodies(List<WaterBodies.EnclosedBody> bodies)
        {
            if (!Planets.Core.Controller.DebugEnabled || bodies == null)
                return;
            int listed = 0;
            float largestArea = 0f;
            float largestStrength = 0f;
            int largestTiles = 0;
            for (int i = 0; i < bodies.Count; i++)
            {
                WaterBodies.EnclosedBody body = bodies[i];
                if (body.AreaKm2 > largestArea)
                {
                    largestArea = body.AreaKm2;
                    largestStrength = body.Strength;
                    largestTiles = body.Tiles;
                }
                if (body.AreaKm2 >= 1000f)
                    listed++;
            }
            RPLog.D($"[WS26] Enclosed water: {bodies.Count} bodies. Largest {largestTiles} tiles, {largestArea:F0} km2, strength {largestStrength:F3}. Bodies of at least 1000 km2: {listed}.");
            for (int i = 0; i < bodies.Count; i++)
            {
                WaterBodies.EnclosedBody body = bodies[i];
                if (body.AreaKm2 < 1000f)
                    continue;
                RPLog.D($"[WS26] Enclosed body: {body.Tiles} tiles, {body.AreaKm2:F0} km2, strength {body.Strength:F3}.");
            }
        }

        // ========== ELEVATION DATA ==========

        /// <summary>
        /// Extract and convert elevation data from tiles
        /// Sets: ElevationKm
        /// 
        /// Conversion: meters → kilometers for climate calculations
        /// (Lapse rate is in °C/km, so we work in km internally)
        /// 
        /// NOTE: Reads from layer.Tiles (created in base geometry step), not Find.WorldGrid.
        /// </summary>
        private void ComputeElevationData()
        {
            if (climate.ColumnDepth == null || climate.ColumnDepth.Length != tileCount)
                climate.ColumnDepth = new float[tileCount];

            for (int i = 0; i < tileCount; i++)
            {
                Tile tile = layer.Tiles[i];
                
                // Convert from meters to kilometers
                float elevKm = tile.elevation / 1000f;
                climate.ElevationKm[i] = elevKm;
                float heightKm = climate.OceanMask != null && climate.OceanMask[i] ? 0f : Mathf.Max(0f, elevKm);
                climate.ColumnDepth[i] = Mathf.Exp(-heightKm / 8f);
            }
        }

        // ========== CONTINENTALITY ==========

        /// <summary>
        /// Compute continentality index from ocean distance
        /// Sets: Continentality
        /// 
        /// Formula: Continentality = 1 - e^(-distance / efold)
        /// - Coast (0km): 0.0 (fully maritime)
        /// - 350km: 0.63 (semi-continental)
        /// - 700km: 0.87 (continental)
        /// - 1050km+: 0.95+ (deep continental)
        /// 
        /// Uses oceanEfoldKm (default 350km) from ClimateConfiguration.
        /// This sets Continentality only. Seasonal range and the inland mean-temperature
        /// penalty read Continentality. The blend of annual mean temperature toward
        /// sea surface temperature uses coastBlendKm, not this distance.
        /// </summary>
        private void ComputeContinentality()
        {
            float efoldKm = Mathf.Max(1f, settings.EffectiveOceanEfoldKm);
            
            for (int i = 0; i < tileCount; i++)
            {
                // Ocean tiles have continentality = 0 (fully maritime)
                if (climate.OceanMask[i])
                {
                    climate.Continentality[i] = 0f;
                    continue;
                }
                
                // Land tiles: true ocean, plus the one strongest enclosed body.
                float oceanDist = climate.OceanDistKm[i];
                float oceanInfluence = Mathf.Exp(-oceanDist / efoldKm);
                float inlandInfluence = climate.InlandWaterInfluence != null && i < climate.InlandWaterInfluence.Length
                    ? climate.InlandWaterInfluence[i]
                    : 0f;
                climate.Continentality[i] = 1f - Mathf.Clamp01(oceanInfluence + WaterBodies.InlandShoreWeight * inlandInfluence);
            }
        }

        // ========== RELIEF AND RUGGEDNESS ==========

        /// <summary>
        /// Compute local terrain complexity metrics
        /// Sets: Relief, Ruggedness
        /// 
        /// These are terrain descriptors used in Hilliness classification and Orography.
        /// 
        /// **Relief**: Local elevation range (2-ring neighborhood)
        ///   - Measures steepness/complexity at ~56km scale
        ///   - Low: <200m (flat plains)
        ///   - Medium: 200-800m (hills)
        ///   - High: >800m (mountains/plateaus)
        /// 
        /// **Ruggedness**: Elevation variance (2-ring neighborhood)
        ///   - Measures roughness/fragmentation
        ///   - Low variance: smooth terrain
        ///   - High variance: broken/jagged terrain
        /// 
        /// Note: Prominence is computed in TerrainAnalysisOrchestrator using band-weighted smoothing.
        /// </summary>
        private void ComputeReliefAndRuggedness()
        {
            int rings = Mathf.Clamp(Mathf.RoundToInt(56f / EarthScaleConstants.KM_PER_TILE), 1, 3);
            for (int i = 0; i < tileCount; i++)
            {
                float myElev = layer.Tiles[i].elevation;

                // Local ~56 km neighborhood for relief/ruggedness (2 rings at subdivision 10, 1 ring on coarser scales, 3 on subdivision 11)
                ClimateGeometryHelpers.GetTilesWithinRings(i, rings, bfsResult, bfsCurrentRing, bfsNextRing, neighborBuffer);

                // Find elevation range in local neighborhood
                float minElev = myElev;
                float maxElev = myElev;
                float sumSqDiff = 0f;
                int sampleCount = 1;

                foreach (int neighbor in bfsResult)
                {
                    if (neighbor == i)
                        continue;
                    if (climate.OceanMask != null && neighbor >= 0 && neighbor < climate.OceanMask.Length && climate.OceanMask[neighbor])
                        continue;

                    float nElev = layer.Tiles[neighbor].elevation;
                    minElev = Mathf.Min(minElev, nElev);
                    maxElev = Mathf.Max(maxElev, nElev);
                    sampleCount++;

                    float diff = nElev - myElev;
                    sumSqDiff += diff * diff;
                }

                climate.Relief[i] = maxElev - minElev;
                climate.Ruggedness[i] = Mathf.Sqrt(sumSqDiff / sampleCount);
            }
        }

        // ========== STEEPNESS ==========

        /// <summary>
        /// Compute average terrain steepness (gradient) to neighbors in degrees.
        /// Sets: Steepness
        /// 
        /// Calculates the average slope angle from a tile to its immediate neighbors.
        /// Used as one of the hilliness classification metrics.
        /// 
        /// Note: Uses EarthScaleConstants.KM_PER_TILE (sensitive to subdivision settings).
        /// </summary>
        private void ComputeSteepness()
        {
            WorldGrid worldGrid = Find.WorldGrid;
            // Normalize single-hop hillslope steepness to the standard ~28.2 km reference step so a 1-tile mountain step
            // has the same steepness angle in degrees across all planet scales (subdivisions 5–11).
            float distKm = EarthScaleConstants.STANDARD_KM_PER_TILE;
            
            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    climate.Steepness[i] = 0f;
                    continue;
                }
                
                float myElev = climate.ElevationKm[i] * 1000f; // Convert to meters
                float totalGradient = 0f;
                int neighborCount = 0;

                neighborBuffer.Clear();
                worldGrid.GetTileNeighbors(i, neighborBuffer);

                foreach (PlanetTile neighbor in neighborBuffer)
                {
                    if (climate.OceanMask[neighbor.tileId])
                        continue; // Skip ocean neighbors
                    
                    float nElev = climate.ElevationKm[neighbor.tileId] * 1000f;
                    float elevDiff = Mathf.Abs(myElev - nElev);

                    // Distance between neighbors (normalized to reference hillslope step)
                    float gradient = elevDiff / (distKm * 1000f); // Rise over run

                    // Convert to degrees: arctan(gradient)
                    float gradientDegrees = Mathf.Atan(gradient) * Mathf.Rad2Deg;

                    totalGradient += gradientDegrees;
                    neighborCount++;
                }

                climate.Steepness[i] = neighborCount > 0 ? totalGradient / neighborCount : 0f;
            }
        }

        // ========== HELPER METHODS ==========

        // Geometry helpers are now in ClimateGeometryHelpers for shared use
        // Use: ClimateGeometryHelpers.GetTilesWithinRings(tileID, rings)
        // Use: ClimateGeometryHelpers.ArcDistanceKm(a, b)
    }
}
