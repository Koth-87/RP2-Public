using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.WorldGen.Climate;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Utility methods for formatting climate-related UI text.
    /// Follows the pattern of SeaLevelUtility and AxialTiltUtility.
    /// </summary>
    public static class ClimateUtility
    {
        /// <summary>
        /// Format growing period description matching vanilla's Zone_Growing.GrowingQuadrumsDescription() format.
        /// Uses vanilla's accurate calculation method directly.
        /// </summary>
        /// <param name="tileID">Tile ID to get growing period for</param>
        /// <param name="plantDef">Optional plant def for custom temperature ranges (defaults to standard 6-42°C)</param>
        /// <returns>Formatted string like "20/60 days (Decembary - Aprimay)" or "GrowYearRound" or "NoGrowingPeriod"</returns>
        public static string FormatGrowingPeriodDescription(int tileID, ThingDef plantDef = null)
        {
            // Use vanilla's method directly for accurate results
            // This matches what MapMode_GrowingPeriod uses for caching
            try
            {
                var world = Find.World;
                if (world == null || Find.WorldGrid == null)
                    return "NoGrowingPeriod".Translate();

                PlanetTile planetTile = new PlanetTile(tileID);
                if (!planetTile.Valid)
                    return "NoGrowingPeriod".Translate();

                return Zone_Growing.GrowingQuadrumsDescription(planetTile, plantDef);
            }
            catch
            {
                return "NoGrowingPeriod".Translate();
            }
        }
    }
}
