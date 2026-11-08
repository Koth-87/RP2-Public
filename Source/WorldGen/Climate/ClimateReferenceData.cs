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
        /// Reference equator-to-pole ocean temperature drop (0–15° minus 70–90° ocean mean, °C) on a Normal-preset planet.
        /// </summary>
        public const float ReferenceOceanThermalSpanC = 47.0f;

        /// <summary>
        /// Analytical 3-cell sea-level pressure anomaly (hPa) from Held-Hou Hadley mechanics and Ferrel baroclinic
        /// eddy momentum flux, scaled by the planet's ocean equator-to-pole thermal span (<paramref name="oceanSpanScale"/>)
        /// and rotation rate ratio (<paramref name="omegaRatio"/> = EarthOmega / Omega).
        /// Replaces the empirical 19-point ERA5 Southern Ocean pressure lookup table.
        /// </summary>
        public static float SeaLevelPressureAnomaly(float degreesFromThermalEquator, float oceanSpanScale = 1f, float omegaRatio = 1f)
        {
            float x = Mathf.Clamp(Mathf.Abs(degreesFromThermalEquator), 0f, 90f);
            float dtScale = Mathf.Clamp(oceanSpanScale, 0.35f, 1.8f);
            float rotRatio = Mathf.Max(omegaRatio, 0.1f);

            float phiH = Mathf.Clamp(33.5f * Mathf.Sqrt(rotRatio) * Mathf.Pow(dtScale, 0.25f), 18f, 50f);
            float phiS = Mathf.Clamp(64.5f * Mathf.Pow(rotRatio, 0.33f), phiH + 15f, 82f);

            float pRidge = 7.72f * dtScale;
            float pTrough = -27.53f * dtScale;
            float pPole = -13.50f * dtScale;

            if (x <= phiH)
            {
                float s1 = x / phiH;
                return pRidge * Mathf.Pow(Mathf.Sin(0.5f * Mathf.PI * s1), 2.50f);
            }
            if (x <= phiS)
            {
                float s2 = (x - phiH) / (phiS - phiH);
                float w2 = 0.5f * (1f - Mathf.Cos(Mathf.PI * Mathf.Pow(s2, 1.15f)));
                return pRidge + (pTrough - pRidge) * w2;
            }
            float s3 = Mathf.Clamp01((x - phiS) / Mathf.Max(90f - phiS, 1f));
            float w3 = 0.5f * (1f - Mathf.Cos(Mathf.PI * Mathf.Pow(s3, 0.72f)));
            return pTrough + (pPole - pTrough) * w3;
        }

        /// <summary>
        /// Physical ocean-fetch reference by latitude: 0.77 in the Hadley basin belt (&lt;= 27.5°), rising smoothly
        /// to 1.0 in the circumpolar Ferrel storm belt (&gt;= 47.5°) where an unobstructed ocean ring carries full
        /// baroclinic eddy momentum. Replaces the empirical 17-point ERA5 ocean-share lookup table.
        /// </summary>
        public static float OceanShareAt(float latDeg)
        {
            float x = Mathf.Abs(latDeg);
            return 0.77f + 0.23f * Mathf.Clamp01((x - 27.5f) / 20.0f);
        }

        /// <summary>
        /// Analytical winter-minus-summer ocean pressure half-swing (hPa) from winter Hadley ridge intensification
        /// (centered near 22.5°) and winter baroclinic storm-belt deepening (poleward of 42°), scaled by
        /// <paramref name="oceanSpanScale"/>. Replaces the empirical 16-point ERA5 seasonal pressure table.
        /// </summary>
        public static float SeasonalPressureDeltaAt(float latDeg, float oceanSpanScale = 1f)
        {
            float x = Mathf.Abs(latDeg);
            float dtScale = Mathf.Clamp(oceanSpanScale, 0.35f, 1.8f);
            float hDiff = (x - 22.5f) / 11.0f;
            float hadleyWin = 1.55f * Mathf.Exp(-(hDiff * hDiff));
            float stormWin = -1.30f * (0.5f * (1f + (float)System.Math.Tanh((x - 42.0f) / 8.0f)));
            return dtScale * (hadleyWin + stormWin) * Mathf.Clamp01(x / 5.0f);
        }

        /// <summary>Sample the analytical 3-cell pressure profile at 5° intervals for diagnostic header exports.</summary>
        public static (float degreesFromEquator, float anomalyHpa)[] SamplePressureProfile(float oceanSpanScale = 1f)
        {
            var pts = new (float, float)[19];
            for (int i = 0; i < 19; i++)
            {
                float deg = i * 5f;
                pts[i] = (deg, SeaLevelPressureAnomaly(deg, oceanSpanScale));
            }
            return pts;
        }

        /// <summary>Sample the analytical seasonal pressure delta at 5° intervals for diagnostic header exports.</summary>
        public static (float degreesFromEquator, float deltaHpa)[] SampleSeasonalPressureDelta(float oceanSpanScale = 1f)
        {
            var pts = new (float, float)[16];
            for (int i = 0; i < 16; i++)
            {
                float deg = i * 5f;
                pts[i] = (deg, SeasonalPressureDeltaAt(deg, oceanSpanScale));
            }
            return pts;
        }

        /// <summary>Sample the physical ocean-share reference at 5° band centers for diagnostic header exports.</summary>
        public static (float degreesFromEquator, float share)[] SampleOceanShareReference()
        {
            var pts = new (float, float)[17];
            for (int i = 0; i < 17; i++)
            {
                float deg = 2.5f + i * 5f;
                pts[i] = (deg, OceanShareAt(deg));
            }
            return pts;
        }
    }
}
