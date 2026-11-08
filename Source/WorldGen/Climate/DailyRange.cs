using UnityEngine;
using Planets.WorldGen.Astronomy;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Mean daily temperature range, in degrees. Hargreaves-Samani wants this, not the summer-to-winter swing.
    /// </summary>
    public static class DailyRange
    {
        /// <summary>
        /// Multiplier on daily-range evaporation. Stays 1. The shipped formula is the daily range itself.
        /// A debug world still logs the scale that would match the old seasonal-range median. That scale is not applied.
        /// </summary>
        public const float MedianMatchScale = 1f;

        /// <summary>
        /// About 5 C on a wet coast, 12 C on a dry coast, 11 C in a wet interior, 18 to 19 C in a dry interior.
        /// </summary>
        public static float Estimate(float continentality, float provisionalAridity)
        {
            float dryness = 1f - Mathf.Clamp01(provisionalAridity);
            return Mathf.Clamp(5f + 6f * continentality + 8f * dryness, 4f, 20f);
        }

        /// <summary>
        /// Snapshot daily temperature range modulated by spherical solar altitude span (collapses in polar night
        /// and narrows under continuous midnight sun where the sun circles near constant altitude).
        /// </summary>
        public static float EstimateForSnapshot(float baseDailyRangeC, float latDeg, float declinationDeg)
        {
            float span = SolarGeometry.DiurnalAltitudeSpanFactor(latDeg, declinationDeg);
            // Even when the sun does not rise or sets briefly, synoptic weather keeps a small ~1.5 C baseline variation.
            return Mathf.Clamp(1.5f + (baseDailyRangeC - 1.5f) * Mathf.Sqrt(span), 1.5f, 20f);
        }

        /// <summary>
        /// Pass-1 aridity: rainfall over Hargreaves evaporation at a 10 C daily range, with continental stress,
        /// using the planet's axial-tilt annual insolation at this latitude.
        /// </summary>
        public static float ProvisionalAridity(float rainfallMm, float meanTempC, float latRad, float continentality)
        {
            float latDeg = latRad * Mathf.Rad2Deg;
            float tiltDeg = AxialTiltCurves.GetTiltDegrees(Planets.Core.Planets_GameComponent.ActiveAxialTilt);
            var sol = SolarGeometry.GetSolsticeInsolation(Mathf.Abs(latDeg), tiltDeg);
            float qAnn = 0.25f * sol.SummerDailyQ + 0.25f * sol.WinterDailyQ + 0.50f * sol.EquinoxDailyQ;
            float extraterrestrial = 15f * (qAnn / SolarGeometry.SolsticeInsolation.EquatorEquinoxReference);
            float temperature = Mathf.Max(meanTempC + 17.8f, 0.1f);
            float pet = 0.0023f * extraterrestrial * temperature * Mathf.Sqrt(10f) * 365f;
            float stress = 1f + continentality * 0.15f;
            return rainfallMm / Mathf.Max(pet * stress, 50f);
        }
    }
}
