using System;
using System.Collections.Generic;
using Planets.WorldGen.Constants;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Lightweight facade for accessing climate data after worldgen.
    /// All methods delegate to ClimateData WorldComponent.
    /// 
    /// Used by debug tools, exports, and tile inspectors to query climate values.
    /// </summary>
    public static class Climate
    {
        private static ClimateData _cache;
        
        public static void ClearCache()
        {
            _cache = null;
            Planets.Patches.Climate.DiurnalSwing.Invalidate();
        }

        /// <summary>
        /// Cached ClimateData instance to avoid millions of GetComponent lookups during biome generation.
        /// Invalidates if world changes.
        /// </summary>
        private static ClimateData Cache
        {
            get
            {
                var world = Find.World;
                if (world == null) return null;
                
                // Return valid cache if same world
                if (_cache != null && _cache.world == world)
                    return _cache;
                    
                // Refresh cache
                _cache = world.GetComponent<ClimateData>();
                return _cache;
            }
        }
        
        // ========== TEMPERATURE DATA ==========
        
        /// <summary>Annual mean temperature (°C). Falls back to tile.temperature if ClimateData not available (e.g. saves from vanilla/MMF).</summary>
        public static float AnnualTemp(int id)
        {
            if (Cache?.AnnualMeanTemp != null && id >= 0 && id < Cache.AnnualMeanTemp.Length)
                return Cache.AnnualMeanTemp[id];
            
            // Fallback to vanilla tile data for saves created without RP2
            var worldGrid = Find.WorldGrid;
            if (worldGrid != null && id >= 0 && id < worldGrid.TilesCount)
            {
                var tile = worldGrid[id];
                if (tile != null)
                    return tile.temperature;
            }
            
            return 15f; // Final fallback
        }
        
        /// <summary>Maximum summer temperature (°C)</summary>
        public static float SummerMax(int id)
        {
            float[] values = Cache?.MaxSummerTemp;
            if (values != null && id >= 0 && id < values.Length)
                return values[id];
            return 25f;
        }
        
        /// <summary>Minimum winter temperature (°C)</summary>
        public static float WinterMin(int id)
        {
            float[] values = Cache?.MinWinterTemp;
            if (values != null && id >= 0 && id < values.Length)
                return values[id];
            return 5f;
        }
        
        /// <summary>True when this tile has a stored seasonal amplitude. Callers that patch GenTemperature use this so a missing array does not call back into the patch.</summary>
        public static bool HasSeasonalAmp(int id)
        {
            float[] values = Cache?.SeasonalAmp;
            return values != null && id >= 0 && id < values.Length;
        }

        /// <summary>Seasonal temperature amplitude - full annual range (warmest to coldest, °C). Missing data uses the axial-tilt curve, which is already a full range.</summary>
        public static float SeasonalAmp(int id)
        {
            if (HasSeasonalAmp(id))
                return Cache.SeasonalAmp[id];

            var worldGrid = Find.WorldGrid;
            if (worldGrid != null && id >= 0 && id < worldGrid.TilesCount)
            {
                PlanetTile planetTile = new PlanetTile(id);
                if (planetTile.Valid)
                {
                    float latDeg = Mathf.Abs(worldGrid.LongLatOf(planetTile).y);
                    var curve = Planets.WorldGen.Astronomy.AxialTiltCurves.SeasonalTempVariationCurve;
                    if (curve != null)
                        return curve.Evaluate(latDeg);
                }
            }

            return 10f;
        }
        
        /// <summary>Seasonal shift amplitude - half-range for vanilla integration (°C)</summary>
        public static float SeasonalHalfRange(int id) => HasSeasonalAmp(id) ? Cache.SeasonalAmp[id] / 2f : 5f;
        
        /// <summary>Growing period in twelfths (0-12). Returns 0 as fallback - actual calculation done by MapMode_GrowingPeriod caching.</summary>
        public static int GrowingPeriod(int id) => 0; // No longer stored in ClimateData - calculated on-demand

        // ========== RAINFALL & MOISTURE ==========
        
        /// <summary>Annual rainfall (mm/year). Falls back to tile.rainfall if ClimateData not available (e.g. saves from vanilla/MMF).</summary>
        public static float Rainfall(int id)
        {
            if (Cache?.AnnualRainfall != null && id >= 0 && id < Cache.AnnualRainfall.Length)
                return Cache.AnnualRainfall[id];
            
            // Fallback to vanilla tile data for saves created without RP2
            var worldGrid = Find.WorldGrid;
            if (worldGrid != null && id >= 0 && id < worldGrid.TilesCount)
            {
                var tile = worldGrid[id];
                if (tile != null)
                    return tile.rainfall;
            }
            
            return 1000f; // Final fallback
        }
        
        /// <summary>Aridity index (rainfall/PET ratio). Falls back to estimated value from rainfall/temperature for vanilla/MMF saves.</summary>
        public static float Aridity(int id)
        {
            if (Cache?.AridityIndex != null && id >= 0 && id < Cache.AridityIndex.Length)
                return Cache.AridityIndex[id];
            
            // Fallback: estimate from rainfall and temperature
            // Simple PET estimation: PET ≈ 2.1 * (temp + 17.8)^0.5 * rainfall^0.5 (Thornthwaite approximation)
            float rainfall = Rainfall(id);
            float temp = AnnualTemp(id);
            
            if (rainfall > 0f && temp > -20f) // Avoid division by zero or extreme cold
            {
                // Simplified PET estimate: ~2mm per degree C above freezing, scaled by temperature
                float petEstimate = Mathf.Max(100f, (temp + 20f) * 10f); // Rough PET estimate in mm/year
                return rainfall / petEstimate;
            }
            
            return 1.0f; // Final fallback (neutral aridity)
        }
        
        /// <summary>Monsoon boost (mm/year additional)</summary>
        public static float MonsoonBoost(int id)
        {
            float[] values = Cache?.MonsoonBoost;
            return InRange(values, id) ? values[id] : 0f;
        }

        /// <summary>Annualized rainfall rate (mm/year) of the driest snapshot. Falls back to annual rainfall when unavailable.</summary>
        public static float DriestSeasonRain(int id)
        {
            float[] values = Cache?.DriestSeasonRainMm;
            return InRange(values, id) ? values[id] : Rainfall(id);
        }

        /// <summary>Annualized rainfall rate (mm/year) of the wettest snapshot. Falls back to annual rainfall when unavailable.</summary>
        public static float WettestSeasonRain(int id)
        {
            float[] values = Cache?.WettestSeasonRainMm;
            return InRange(values, id) ? values[id] : Rainfall(id);
        }

        /// <summary>Potential evapotranspiration (mm/year). Falls back to Rainfall / Aridity when unavailable.</summary>
        public static float PET(int id)
        {
            float[] values = Cache?.PotentialEvapotranspiration;
            if (InRange(values, id) && values[id] > 0f)
                return values[id];
            float ai = Aridity(id);
            return ai > 0.01f ? Rainfall(id) / ai : 1000f;
        }

        /// <summary>Elevation in meters.</summary>
        public static float Elevation(int id)
        {
            float[] values = Cache?.ElevationKm;
            if (InRange(values, id))
                return values[id] * 1000f;
            var worldGrid = Find.WorldGrid;
            if (worldGrid != null && id >= 0 && id < worldGrid.TilesCount)
            {
                var tile = worldGrid[id];
                if (tile != null)
                    return tile.elevation;
            }
            return 0f;
        }
        
        // ========== GEOGRAPHY ==========
        
        /// <summary>Distance from ocean (km). Falls back to calculated distance for vanilla/MMF saves.</summary>
        public static float OceanDistKm(int id)
        {
            if (Cache?.OceanDistKm != null && id >= 0 && id < Cache.OceanDistKm.Length)
                return Cache.OceanDistKm[id];
            
            // Fallback: calculate distance to nearest ocean tile
            var worldGrid = Find.WorldGrid;
            if (worldGrid != null && id >= 0 && id < worldGrid.TilesCount)
            {
                var surfaceTile = worldGrid[id];
                if (surfaceTile != null)
                {
                    // Check if tile itself is ocean
                    if (surfaceTile.PrimaryBiome == BiomeDefOf.Ocean)
                        return 0f;
                    
                    // Simple BFS to find nearest ocean (limited to reasonable distance)
                    int maxSearchDistance = EarthScaleConstants.KmToRings(EarthScaleConstants.OCEAN_DISTANCE_MAX_SEARCH_KM);
                    var visited = new HashSet<int>();
                    var queue = new Queue<(int tileId, int distance)>();
                    queue.Enqueue((id, 0));
                    visited.Add(id);
                    
                    var tmpNeighbors = new List<PlanetTile>();
                    
                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        int currentId = current.tileId;
                        int distance = current.distance;
                        
                        if (distance > maxSearchDistance)
                            break;
                        
                        var currentSurfaceTile = worldGrid[currentId];
                        if (currentSurfaceTile != null && currentSurfaceTile.PrimaryBiome == BiomeDefOf.Ocean)
                        {
                            return distance * EarthScaleConstants.KM_PER_TILE;
                        }
                        
                        // Check neighbors
                        PlanetTile currentPlanetTile = new PlanetTile(currentId);
                        tmpNeighbors.Clear();
                        worldGrid.GetTileNeighbors(currentPlanetTile, tmpNeighbors);
                        
                        foreach (var neighborTile in tmpNeighbors)
                        {
                            int neighborId = neighborTile.tileId;
                            if (neighborId >= 0 && neighborId < worldGrid.TilesCount && visited.Add(neighborId))
                            {
                                queue.Enqueue((neighborId, distance + 1));
                            }
                        }
                    }
                }
            }
            
            return 50f; // Final fallback (moderate distance)
        }
        
        /// <summary>Continentality (0=coastal, 1=deep continental). Falls back to estimated value from distance to ocean for vanilla/MMF saves.</summary>
        public static float Continentality(int id)
        {
            if (Cache?.Continentality != null && id >= 0 && id < Cache.Continentality.Length)
                return Cache.Continentality[id];
            
            // Fallback: estimate from distance to ocean
            float oceanDist = OceanDistKm(id);
            if (oceanDist > 0f)
            {
                // Use same e-fold distance as RP2 (350km) for consistency
                const float oceanEfoldKm = 350f;
                return 1f - Mathf.Exp(-oceanDist / oceanEfoldKm);
            }
            
            return 0.5f; // Final fallback (moderate continentality)
        }
        
        /// <summary>Elevation above sea level (km)</summary>
        public static float ElevationKm(int id)
        {
            float[] values = Cache?.ElevationKm;
            return InRange(values, id) ? values[id] : 0f;
        }
        
        /// <summary>Absolute latitude (degrees)</summary>
        public static float LatitudeDeg(int id)
        {
            float[] values = Cache?.Latitude;
            if (!InRange(values, id))
                return 30f;
            return Math.Abs(values[id] * 180f / (float)Math.PI);
        }
        
        // ========== OROGRAPHIC EFFECTS ==========
        
        /// <summary>Windward exposure factor (0-1+)</summary>
        public static float Windward(int id)
        {
            float[] values = Cache?.UpwindReliefIndex;
            return InRange(values, id) ? values[id] : 0f;
        }
        
        /// <summary>Leeward shadow intensity (0-1)</summary>
        public static float Leeward(int id)
        {
            float[] values = Cache?.LeewardShadowIndex;
            return InRange(values, id) ? values[id] : 0f;
        }
        
        /// <summary>Orographic multiplier (combined windward/leeward). Deprecated - use OrographicDisplayValue.</summary>
        public static float OrographicMult(int id) => OrographicDisplayValue(id);

        /// <summary>Display value for orographic map mode (0.2 leeward to 8.0 windward, 1.0 neutral).
        /// Derived from UpwindReliefIndex - LeewardShadowIndex to match the Plan 2 advection-based system.
        /// Uses asymmetric scaling so leeward values are not visually collapsed into one red bucket.</summary>
        public static float OrographicDisplayValue(int id)
        {
            float windward = Windward(id);
            float leeward = Leeward(id);
            float net = Mathf.Clamp(windward - leeward, -2f, 2f);

            // Preserve detail on the leeward side (0.2-1.0) while still allowing a wide
            // windward dynamic range (1.0-8.0) for strong uplift ridges.
            float display = net < 0f
                ? 1f + 0.4f * net   // net -2 -> 0.2
                : 1f + 3.5f * net;  // net +2 -> 8.0

            return Mathf.Clamp(display, 0.2f, 8f);
        }
        
        // ========== HELPER METHODS ==========
        
        /// <summary>Is this tile coastal (within 5km of ocean)?</summary>
        public static bool IsCoastal(int id) => OceanDistKm(id) < 5f;
        
        /// <summary>Is this tile deep continental (>70% continentality)?</summary>
        public static bool IsContinental(int id) => Continentality(id) > 0.7f;
        
        /// <summary>Does this tile have significant monsoon influence?</summary>
        public static bool HasMonsoon(int id)
        {
            return MonsoonBoost(id) >= 150f;
        }
        
        /// <summary>Is this tile on a windward slope?</summary>
        public static bool IsWindward(int id) => Windward(id) > 0.5f;
        
        /// <summary>Is this tile in a rain shadow?</summary>
        public static bool IsLeeward(int id) => Leeward(id) > 0.5f;
        
        /// <summary>Check if climate data is available (checks Latitude as fundamental array populated first during worldgen)</summary>
        public static bool IsInitialized() => Cache != null && Cache.ArraysReady;
        
        // ========== HYDROLOGY DATA ACCESS ==========
        
        /// <summary>Flow accumulation (upstream drainage area in tile units) – from Topology run.</summary>
        public static float FlowAccumulation(int id)
        {
            float[] values = Cache?.FlowAccumulationTopology;
            return InRange(values, id) ? values[id] : 1f;
        }
        
        /// <summary>Flow direction (neighbor index 0-7, or -1 for no flow) – from Topology run.</summary>
        public static int FlowDirection(int id)
        {
            int[] values = Cache?.FlowDirectionTopology;
            return InRange(values, id) ? values[id] : -1;
        }
        
        /// <summary>Is this tile in an endorheic (closed) basin?</summary>
        public static bool IsEndorheic(int id)
        {
            bool[] values = Cache?.EndorheicMask;
            return InRange(values, id) && values[id];
        }
        
        /// <summary>Initial drainage basin ID - pre-fill/spill (-1 if no basin assigned)</summary>
        public static int InitialBasinID(int id)
        {
            int[] values = Cache?.InitialBasinID;
            return InRange(values, id) ? values[id] : -1;
        }
        
        /// <summary>Merged drainage basin ID - includes fill-and-spill connections (-1 if no basin assigned) – from Topology run.</summary>
        public static int MergedBasinID(int id)
        {
            int[] values = Cache?.MergedBasinIDTopology;
            return InRange(values, id) ? values[id] : -1;
        }

        private static bool InRange(Array values, int id) => values != null && id >= 0 && id < values.Length;
        
        /// <summary>Swampiness score (0-1) for wetland/marsh terrain. Falls back to tile.swampiness for vanilla/MMF saves.</summary>
        public static float Swampiness(int id)
        {
            if (Cache?.Swampiness != null && id >= 0 && id < Cache.Swampiness.Length)
                return Cache.Swampiness[id];
            
            // Fallback to vanilla tile data for saves created without RP2
            var worldGrid = Find.WorldGrid;
            if (worldGrid != null && id >= 0 && id < worldGrid.TilesCount)
            {
                var tile = worldGrid[id];
                if (tile != null)
                    return tile.swampiness;
            }
            
            return 0f; // Final fallback
        }

        // ========== TIER 2 HYDROLOGY (safe defaults when Tier 2 off or data missing) ==========

        /// <summary>River magnitude class (0=none, 1=stream, 2=river, 3=major, 4=huge). Returns 0 if Tier 2 data missing.</summary>
        public static byte RiverMagnitude(int id) => (byte)(Cache?.RiverMagnitude != null && id >= 0 && id < Cache.RiverMagnitude.Length ? Cache.RiverMagnitude[id] : 0);

        /// <summary>River presence score 0–1. Returns 0 if Tier 2 data missing.</summary>
        public static float RiverPresence(int id) => Cache?.RiverPresence != null && id >= 0 && id < Cache.RiverPresence.Length ? Cache.RiverPresence[id] : 0f;

        /// <summary>Water body type (0=none, 1=river, 2=lake). Returns 0 if Tier 2 data missing.</summary>
        public static byte WaterBodyType(int id) => (byte)(Cache?.WaterBodyType != null && id >= 0 && id < Cache.WaterBodyType.Length ? Cache.WaterBodyType[id] : 0);
    }
}
