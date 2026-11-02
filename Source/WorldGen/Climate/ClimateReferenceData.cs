using UnityEngine;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// IMMUTABLE CLIMATE REFERENCE DATA
    /// ================================
    /// Contains 3 latitude reference curves that define Earth's climate patterns.
    /// These multipliers are used to interpolate climate values across latitudes.
    /// 
    /// All values are:
    /// - MULTIPLIERS (not absolute values)
    /// - Applied via latitude-based linear interpolation
    /// - Immutable (never user-configurable, never reset)
    /// - Foundation for realistic Earth-like climate generation
    /// </summary>
    public static class ClimateReferenceData
    {
        // ========== CONTINENTAL AMPLIFICATION BY LATITUDE ==========
        /// <summary>
        /// How much an interior (continentality = 1) multiplies the coastal seasonal range.
        /// A coast (continentality = 0) stays at 1x at every latitude.
        /// 
        /// Range: 1.30x-2.06x (peak at 60°). The previous curve was 1.8x-3.8x.
        /// Its excess over the coast was about twice the full CRU annual range on inland
        /// lowland, so each point is now 1 + 0.38 * (old value - 1). The shape is unchanged:
        /// weakest at the equator, peak at 60°, weaker again at the pole. Coasts stay at 1x
        /// because continentality scales the excess. The obliquity scale is separate.
        ///
        /// A 50° dip would have fitted Earth's 50° band more tightly. That band's "inland"
        /// tiles are milder than their distance to the nearest coast, because the ocean is
        /// upwind. The same is true of western Europe. Along-wind continentality handles
        /// that. A dip here would make a genuinely continental 50° continent too mild.
        /// </summary>
        public static readonly (float lat, float maxMult)[] ContinentalAmplificationPoints = {
            (0f,   1.30f),  // Equator
            (10f,  1.38f),  // Tropical
            (20f,  1.49f),  // Subtropical
            (30f,  1.68f),  // Warm temperate
            (40f,  1.84f),  // Mid-latitude
            (50f,  1.95f),  // Cool temperate
            (60f,  2.06f),  // Subpolar peak
            (70f,  1.84f),  // High subpolar
            (80f,  1.57f),  // Polar approach
            (90f,  1.46f)   // Pole
        };

        /// <summary>
        /// Ocean latitude curve offsets relative to the temperature preset's equator and pole.
        /// Keeps the Normal ocean endpoints at 29°C (equator) and −33.5°C (pole), while hotter or colder presets shift the ocean accordingly.
        /// </summary>
        public const float OceanEquatorOffset = -2f;
        public const float OceanPoleOffset = -4f;

        /// <summary>
        /// Cooling of the land surface with height, °C per km. Lower than the free atmosphere's 6.5°C/km because solar heating of elevated
        /// plateaus warms the surface air relative to open air at the same altitude. Circulation uses this same rate for sea-level-equivalent temperature.
        /// </summary>
        public const float LandSurfaceLapsePerKm = 4.5f;

        /// <summary>
        /// Zonal-mean sea-level pressure anomaly from the equator, hPa, of the southern hemisphere:
        /// ERA5 2025, every longitude, the 12-month mean. That hemisphere has almost no land in its
        /// storm belt, so this is the curve of an ocean world. A world scales it by its own band ocean
        /// share over <see cref="OceanShareReference"/>, and the heat terms add the land.
        /// 70° to 90° keep the earlier hemisphere-averaged values, because ERA5 there is Antarctic ice.
        /// Argument is degrees from the thermal equator. Calibration/Earth/prepare_ocean_profile.py writes the same numbers.
        /// </summary>
        public static readonly (float degreesFromEquator, float anomalyHpa)[] SeaLevelPressureProfile =
        {
            (0f, 0f),
            (5f, 0.21f),
            (10f, 1.03f),
            (15f, 2.50f),
            (20f, 4.30f),
            (25f, 6.23f),
            (30f, 7.62f),
            (35f, 7.59f),
            (40f, 5.18f),
            (45f, 0.07f),
            (50f, -7.59f),
            (55f, -16.52f),
            (60f, -24.25f),
            (65f, -26.00f),
            (70f, -23.00f),
            (75f, -19.50f),
            (80f, -16.50f),
            (85f, -14.50f),
            (90f, -13.50f)
        };

        /// <summary>
        /// Ocean share of Earth's southern hemisphere in 5° bands, keyed by band centre, where
        /// <see cref="SeaLevelPressureProfile"/> was measured. Poleward of 70° it is 1, because the
        /// curve there is not the southern measurement.
        /// </summary>
        public static readonly (float degreesFromEquator, float share)[] OceanShareReference =
        {
            (2.5f, 0.765f),
            (7.5f, 0.771f),
            (12.5f, 0.799f),
            (17.5f, 0.764f),
            (22.5f, 0.755f),
            (27.5f, 0.783f),
            (32.5f, 0.839f),
            (37.5f, 0.933f),
            (42.5f, 0.964f),
            (47.5f, 0.974f),
            (52.5f, 0.985f),
            (57.5f, 0.999f),
            (62.5f, 0.997f),
            (67.5f, 0.794f),
            (72.5f, 1f),
            (77.5f, 1f),
            (82.5f, 1f),
            (87.5f, 1f)
        };

        /// <summary>
        /// Winter minus summer sea-level pressure over the ocean, hPa, half the range, both
        /// hemispheres averaged. ERA5 2025, June and December. Winter hemisphere adds it,
        /// summer hemisphere subtracts it. Calibration/Earth/prepare_seasonal_pressure.py writes the same numbers.
        /// </summary>
        public static readonly (float degreesFromEquator, float deltaHpa)[] SeasonalPressureDelta =
        {
            (0f, 0f),
            (5f, 0.12f),
            (10f, 0.57f),
            (15f, 1.15f),
            (20f, 1.51f),
            (25f, 1.50f),
            (30f, 0.88f),
            (35f, 0.13f),
            (40f, -0.69f),
            (45f, -1.12f),
            (50f, -1.20f),
            (55f, -0.69f),
            (60f, -0.86f),
            (65f, -1.49f),
            (70f, -1.16f),
            (75f, -1.51f)
        };

        public static float SeaLevelPressureAnomaly(float degreesFromThermalEquator)
        {
            return Interpolate(SeaLevelPressureProfile, Mathf.Abs(degreesFromThermalEquator));
        }

        public static float OceanShareAt(float latDeg)
        {
            return Interpolate(OceanShareReference, Mathf.Abs(latDeg));
        }

        public static float SeasonalPressureDeltaAt(float latDeg)
        {
            return Interpolate(SeasonalPressureDelta, Mathf.Abs(latDeg));
        }

        /// <summary>Linear between points, held flat past either end.</summary>
        static float Interpolate((float x, float y)[] table, float x)
        {
            if (x <= table[0].x)
                return table[0].y;
            if (x >= table[table.Length - 1].x)
                return table[table.Length - 1].y;
            for (int i = 0; i < table.Length - 1; i++)
            {
                float a = table[i].x;
                float b = table[i + 1].x;
                if (x < a || x > b)
                    continue;
                float span = b - a;
                float t = span <= 0f ? 0f : (x - a) / span;
                return table[i].y + t * (table[i + 1].y - table[i].y);
            }
            return 0f;
        }
    }
}
