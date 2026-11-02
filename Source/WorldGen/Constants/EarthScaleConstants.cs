using RimWorld;
using UnityEngine;
using Verse;

namespace Planets.WorldGen.Constants
{
    /// <summary>
    /// EARTH-SCALE REFERENCE CONSTANTS
    /// ================================
    /// Physical constants and derived thresholds for Earth-scale world generation.
    /// All distance, area, and drainage calculations must use these values.
    /// 
    /// Key principle: 1 tile = 28.22 km ≈ 796 km²
    /// Elevation range: 0m (sea level) to 9000m
    /// </summary>
    public static class EarthScaleConstants
    {
        // ========== PLANETARY DIMENSIONS (IMMUTABLE EARTH STANDARD) ==========

        /// <summary>Earth's mean radius in kilometers (Immutable)</summary>
        public const float PLANET_RADIUS_KM = 6371f;

        /// <summary>Earth's circumference in kilometers (2π × PLANET_RADIUS_KM)</summary>
        public const float PLANET_CIRCUMFERENCE_KM = 40075f;

        /// <summary>Standard tile count at subdivision 10 (vanilla). Used by My Little Planet compat to reset before applying subdivision.</summary>
        public const int STANDARD_TILES_CIRCUMFERENCE = 1420;

        /// <summary>Number of icosahedron tiles. Can be patched by mods (e.g., MyLittlePlanet)</summary>
        public static int TILES_CIRCUMFERENCE = STANDARD_TILES_CIRCUMFERENCE;

        /// <summary>Map a world-grid subdivision count to the equator tile count used for kilometer conversions.</summary>
        public static void SetForSubdivisions(int subdivisionCount)
        {
            switch (subdivisionCount)
            {
                case 6:
                    TILES_CIRCUMFERENCE = 157;
                    break;
                case 7:
                    TILES_CIRCUMFERENCE = 272;
                    break;
                case 8:
                    TILES_CIRCUMFERENCE = 473;
                    break;
                case 9:
                    TILES_CIRCUMFERENCE = 818;
                    break;
                case 10:
                    TILES_CIRCUMFERENCE = 1420;
                    break;
                case 11:
                    // 1,771,472 tiles at full coverage. 1420 * sqrt(3).
                    TILES_CIRCUMFERENCE = 2460;
                    break;
                case 5:
                    // 2,432 tiles at full coverage. 1420 / sqrt(243).
                    TILES_CIRCUMFERENCE = 91;
                    break;
                default:
                    TILES_CIRCUMFERENCE = STANDARD_TILES_CIRCUMFERENCE;
                    break;
            }
        }

        /// <summary>Kilometers per degree of longitude at equator</summary>
        public const float KM_PER_DEGREE = PLANET_CIRCUMFERENCE_KM / 360f;

        // ========== YEAR LENGTH CONSTANTS ==========
        // Climate/hydrology use Earth-scale physics: mm/year, PET, evaporation.
        // Use EARTH_DAYS_PER_YEAR (365) for mm/year ↔ mm/day conversions.
        // Use RIMWORLD_DAYS_PER_YEAR (60) only for gameplay logic (diseases, seasons, etc.).

        /// <summary>Earth days per year. Use for climate physics (evaporation, rainfall, flow).</summary>
        public const float EARTH_DAYS_PER_YEAR = 365f;

        /// <summary>RimWorld days per year. Use for gameplay logic (diseases per year, growing period display).</summary>
        public const float RIMWORLD_DAYS_PER_YEAR = 60f;

        // ========== ATMOSPHERIC CONSTANTS (IMMUTABLE) ==========

        /// <summary>Earth's tropospheric lapse rate (°C per kilometer of elevation gain).
        /// Physical constant for atmospheric cooling with altitude.</summary>
        public const float TROPOSPHERIC_LAPSE_RATE_C_PER_KM = 6.5f;

        /// <summary>Kilometers per tile (derived). Recalculates if TILES_CIRCUMFERENCE is modified</summary>
        public static float KM_PER_TILE => PLANET_CIRCUMFERENCE_KM / TILES_CIRCUMFERENCE;

        /// <summary>Standard km per tile at subdivision 10. Used for terrain metric scaling.</summary>
        public static float STANDARD_KM_PER_TILE => PLANET_CIRCUMFERENCE_KM / STANDARD_TILES_CIRCUMFERENCE;

        /// <summary>Scale factor for terrain metrics (relief, prominence, ruggedness) when subdivision differs from standard.
        /// When KM_PER_TILE > standard, thresholds must be multiplied by this factor.</summary>
        public static float TerrainMetricsScaleFactor => KM_PER_TILE / STANDARD_KM_PER_TILE;

        /// <summary>Tile area at subdivision 10. River flow uses this so a basin of the same size in km² has the same flow at every planet scale.</summary>
        public static float TILE_AREA_KM2_AT_SCALE_10 =>
            (PLANET_CIRCUMFERENCE_KM / STANDARD_TILES_CIRCUMFERENCE) * (PLANET_CIRCUMFERENCE_KM / STANDARD_TILES_CIRCUMFERENCE);

        /// <summary>Area per tile in km² (derived). Recalculates if TILES_CIRCUMFERENCE is modified</summary>
        public static float TILE_AREA_KM2 => KM_PER_TILE * KM_PER_TILE;

        // ========== TIER 1 & 2 CLIMATE CONSTANTS (PHYSICS) ==========
        
        // --- RELIEF & HILLINESS ---

        /// <summary>Maximum relief for flat terrain (measured over ~100km / 3-4 tiles)</summary>
        public const float FLAT_TERRAIN_MAX_RELIEF = 200f;

        /// <summary>Maximum relief for small hills terrain</summary>
        public const float SMALL_HILLS_MAX_RELIEF = 400f;

        /// <summary>Maximum relief for large hills terrain</summary>
        public const float LARGE_HILLS_MAX_RELIEF = 800f;

        /// <summary>Maximum relief for mountainous terrain. Above this = impassable</summary>
        public const float MOUNTAINOUS_MAX_RELIEF = 1200f;

        // --- HILLINESS CLASSIFICATION (Cascading thresholds, scaled by TerrainMetricsScaleFactor) ---
        // Relief (m): minimum to trigger each tier
        public const float HILLINESS_RELIEF_SMALL_HILLS_MIN = 220f;
        public const float HILLINESS_RELIEF_LARGE_HILLS_MIN = 380f;
        public const float HILLINESS_RELIEF_MOUNTAINOUS_MIN = 650f;
        public const float HILLINESS_RELIEF_IMPASSABLE_MIN = 1300f;
        // Ruggedness (m)
        public const float HILLINESS_RUGGEDNESS_SMALL_HILLS_MIN = 145f;
        public const float HILLINESS_RUGGEDNESS_LARGE_HILLS_MIN = 150f;
        public const float HILLINESS_RUGGEDNESS_IMPASSABLE_MIN = 1100f;
        // Prominence (m)
        public const float HILLINESS_PROMINENCE_SMALL_HILLS_MIN = 180f;
        public const float HILLINESS_PROMINENCE_LARGE_HILLS_MIN = 240f;
        public const float HILLINESS_PROMINENCE_MOUNTAINOUS_MIN = 450f;
        public const float HILLINESS_PROMINENCE_IMPASSABLE_MIN = 1100f;
        // Steepness (degrees)
        public const float HILLINESS_STEEPNESS_SMALL_HILLS_MIN = 6f;
        public const float HILLINESS_STEEPNESS_LARGE_HILLS_MIN = 7.5f;
        public const float HILLINESS_STEEPNESS_MOUNTAINOUS_MIN = 11f;
        public const float HILLINESS_STEEPNESS_IMPASSABLE_MIN = 27f;

        // --- RIVER ELEVATION BONUSES ---

        /// <summary>Elevation bonus for small hills (vanilla uses 15m, Earth-scale uses 200m)</summary>
        public const float SMALL_HILLS_ELEV_BONUS = 200f;

        /// <summary>Elevation bonus for large hills (vanilla uses 250m, Earth-scale uses 500m)</summary>
        public const float LARGE_HILLS_ELEV_BONUS = 500f;

        /// <summary>Elevation bonus for mountainous terrain (vanilla uses 500m, Earth-scale uses 1200m)</summary>
        public const float MOUNTAINOUS_ELEV_BONUS = 1200f;

        /// <summary>Elevation bonus for impassable terrain (vanilla uses 1000m, Earth-scale uses 2500m)</summary>
        public const float IMPASSABLE_ELEV_BONUS = 2500f;
        
        // --- RIVER & HYDROLOGY THRESHOLDS (TIER 2 GEN) ---
        // Based on Earth drainage area relationships but adapted for map resolution.
        // Reference rainfall (flow→drainage conversion) is user-configurable in HydrologyConfiguration.
        
        /// <summary>Minimum drainage area (km²) for stream formation (~2 tiles at standard res)</summary>
        public const float STREAM_MIN_DRAINAGE_KM2 = 1600f;
        
        /// <summary>Minimum drainage area (km²) for river formation (~8 tiles)</summary>
        public const float RIVER_MIN_DRAINAGE_KM2 = 6400f;
        
        /// <summary>Minimum drainage area (km²) for major river formation (~30 tiles)</summary>
        public const float MAJOR_RIVER_MIN_DRAINAGE_KM2 = 25000f;
        
        /// <summary>Minimum drainage area (km²) for huge river formation (~125 tiles)</summary>
        public const float HUGE_RIVER_MIN_DRAINAGE_KM2 = 100000f;
        
        /// <summary>Minimum drainage area (km²) for swamp formation</summary>
        public const float SWAMP_MIN_DRAINAGE_KM2 = 1600f;

        // --- WETLAND FORMATION FACTORS ---

        /// <summary>Wetland formation factor for flat terrain</summary>
        public const float HILLINESS_FLAT_WETLAND_FACTOR = 1.0f;

        /// <summary>Wetland formation factor for small hills</summary>
        public const float HILLINESS_SMALL_HILLS_WETLAND_FACTOR = 0.7f;

        /// <summary>Wetland formation factor for large hills</summary>
        public const float HILLINESS_LARGE_HILLS_WETLAND_FACTOR = 0.3f;

        /// <summary>Wetland formation factor for mountainous/impassable terrain</summary>
        public const float HILLINESS_MOUNTAINOUS_WETLAND_FACTOR = 0.1f;
        
        // Sea-level lakes have no size cap. Every body that is not the true ocean is a Lake.

        // ========== MAP MODE VISUALIZATION ONLY (MMF) ==========
        // These constants are used by MapModeFramework to visualize data but not used in core physics
        
        /// <summary>Minimum flow intensity for wetland formation (0.0-1.0 scale)</summary>
        public const float WETLAND_MIN_INTENSITY = 0.4f;

        /// <summary>Flow intensity threshold for strong wetland potential (0.0-1.0 scale)</summary>
        public const float WETLAND_HIGH_INTENSITY = 0.5f;

        /// <summary>Flow intensity threshold for wetland cluster centers (0.0-1.0 scale)</summary>
        public const float WETLAND_CLUSTER_INTENSITY = 0.7f;

        // ========== RIDGE AND PEAK DETECTION ==========
        
        /// <summary>Maximum distance (km) for connecting ridge peaks into continuous mountain ranges.
        /// Based on typical spacing in Appalachians (20-50km), Himalayas (100-300km), Andes (50-150km)</summary>
        public const float MAX_RIDGE_CONNECTION_KM = 350f;
        
        /// <summary>Smoothing radius (km) for band-weighted elevation smoothing in terrain analysis.
        /// Approximately 1/3 of target basin diameter for optimal feature detection.</summary>
        public const float TERRAIN_SMOOTHING_RADIUS_KM = 224f;  // 8 rings at 28km/tile
        
        // ========== DISTANCE THRESHOLDS (IMMUTABLE - EARTH-SCALE) ==========
        
        /// <summary>Maximum distance for ocean distance search</summary>
        public const float OCEAN_DISTANCE_MAX_KM = 4000f;

        /// <summary>Maximum BFS search distance (km) when computing ocean distance fallback. Use KmToRings() to get tile count.</summary>
        public const float OCEAN_DISTANCE_MAX_SEARCH_KM = 1500f;
        
        /// <summary>Default influence radius for orographic effects</summary>
        public const float OROGRAPHY_INFLUENCE_RADIUS_KM = 600f;
        
        /// <summary>Distance decay e-fold for ridge influence</summary>
        public const float RIDGE_DISTANCE_WEIGHT_EFOLD_KM = 200f;
        
        /// <summary>Distance for leeward rain shadow decay</summary>
        public const float LEEWARD_DECAY_DISTANCE_KM = 300f;
        
        /// <summary>Windward onset: Far distance</summary>
        public const float WINDWARD_ONSET_DISTANCE_FAR_KM = 300f;
        
        /// <summary>Windward onset: Mid distance</summary>
        public const float WINDWARD_ONSET_DISTANCE_MID_KM = 150f;
        
        /// <summary>Windward onset: Near distance</summary>
        public const float WINDWARD_ONSET_DISTANCE_NEAR_KM = 50f;
        
        /// <summary>Windward onset: Immediate distance</summary>
        public const float WINDWARD_ONSET_DISTANCE_IMMEDIATE_KM = 10f;

        // ========== COASTAL & MONSOON CONSTANTS ==========
        
        /// <summary>Maximum distance for sea breeze effect on wind direction</summary>
        public const float SEA_BREEZE_MAX_DIST_KM = 200f;
        
        /// <summary>Decay distance (e-fold) for coastal moisture source in advection</summary>
        public const float COASTAL_MOISTURE_DECAY_KM = 300f;
        
        /// <summary>Maximum distance for basin edge detection (temperature currents)</summary>
        public const float BASIN_EDGE_MAX_DIST_KM = 700f;
        
        /// <summary>Maximum distance from ocean for monsoon zone consideration</summary>
        public const float MONSOON_MAX_DIST_KM = 800f;
        
        /// <summary>Decay distance (e-fold) for monsoon moisture penetration</summary>
        public const float MONSOON_DECAY_KM = 750f;

        /// <summary>Step decay when propagating monsoon inland (per neighbor step)</summary>
        public const float MONSOON_PROPAGATION_STEP_DECAY = 0.88f;
        /// <summary>Max ocean distance (km) for monsoon source tiles in BFS propagation</summary>
        public const float MONSOON_PROPAGATION_SOURCE_MAX_KM = 400f;
        /// <summary>Relief (m) above which monsoon propagation is strongly reduced</summary>
        public const float MONSOON_PROPAGATION_RELIEF_BLOCK_M = 800f;
        /// <summary>Relief (m) above which monsoon propagation is blocked</summary>
        public const float MONSOON_PROPAGATION_RELIEF_STOP_M = 1200f;

        // ========== OROGRAPHY (FLOW-PATH WINDWARD) ==========

        /// <summary>Meters of accumulated climb per 1.0 UpwindReliefIndex (500m climb → 1.0)</summary>
        public const float WINDWARD_ACCUMULATED_CLIMB_SCALE_M = 500f;
        /// <summary>Leeward cone threshold cos(45°)</summary>
        public const float LEEWARD_CONE_THRESHOLD = 0.707f;
        /// <summary>Leeward LogSumExp aggregation tau</summary>
        public const float LEEWARD_LOGSUMEXP_TAU = 0.5f;

        // ========== HELPER METHODS: UNIT CONVERSIONS ==========\

        /// <summary>Convert drainage area in km² to tile count</summary>
        public static float DrainageKm2ToTiles(float km2) => km2 / TILE_AREA_KM2;

        /// <summary>Convert tile count to drainage area in km²</summary>
        public static float TilesToDrainageKm2(float tiles) => tiles * TILE_AREA_KM2;

        /// <summary>Convert kilometers to tile ring count for world grid searches</summary>
        public static int KmToRings(float km) => Mathf.CeilToInt(km / KM_PER_TILE);

        // ========== HELPER METHODS: DERIVED THRESHOLDS ==========

        /// <summary>
        /// Get wetland formation factor for a given hilliness value
        /// </summary>
        /// <param name="hilliness">Hilliness enum: 0=Undefined, 1=Flat, 2=SmallHills, 3=LargeHills, 4=Mountainous, 5=Impassable</param>
        /// <returns>Wetland formation multiplier (0.0-1.0)</returns>
        public static float GetWetlandFactorForHilliness(int hilliness)
        {
            return hilliness switch
            {
                0 => HILLINESS_FLAT_WETLAND_FACTOR,                   // Undefined → default to flat
                1 => HILLINESS_FLAT_WETLAND_FACTOR,                   // Flat
                2 => HILLINESS_SMALL_HILLS_WETLAND_FACTOR,            // SmallHills
                3 => HILLINESS_LARGE_HILLS_WETLAND_FACTOR,            // LargeHills
                4 => HILLINESS_MOUNTAINOUS_WETLAND_FACTOR,            // Mountainous
                5 => HILLINESS_MOUNTAINOUS_WETLAND_FACTOR,            // Impassable
                _ => HILLINESS_FLAT_WETLAND_FACTOR,                   // Unknown → default to flat
            };
        }

        // ========== SCALING METHODS (REMOVED) ==========
        // EarthScaleConstants is designed for constant Earth-sized planet physics.
        // Scaling is handled by changing tile resolution (TILES_CIRCUMFERENCE), 
        // not by changing physical constants.
    }
}
