using UnityEngine;

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
        /// Pass-1 aridity: rainfall over Hargreaves evaporation at a 10 C daily range, with continental stress.
        /// Same formula the aridity layer uses before it substitutes the real daily range.
        /// </summary>
        public static float ProvisionalAridity(float rainfallMm, float meanTempC, float latRad, float continentality)
        {
            float extraterrestrial = 5f + 10f * Mathf.Cos(latRad);
            float temperature = Mathf.Max(meanTempC + 17.8f, 0.1f);
            float pet = 0.0023f * extraterrestrial * temperature * Mathf.Sqrt(10f) * 365f;
            float stress = 1f + continentality * 0.15f;
            return rainfallMm / Mathf.Max(pet * stress, 50f);
        }
    }
}
