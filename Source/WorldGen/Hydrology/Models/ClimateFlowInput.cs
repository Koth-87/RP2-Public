using UnityEngine;
using Planets.WorldGen.Constants;

namespace Planets.WorldGen.Hydrology.Models
{
    /// <summary>
    /// Provides climate-based runoff input for Tier 2 hydrology (WS21 / WS30).
    /// Unit: Runoff (Rainfall − Budyko LandEvaporation, mm/year) × Tile area (km²) → volume-equivalent (mm·km²/year),
    /// matching lake evaporation units so the fill-and-spill engine and the WS30 moisture budget agree on water conservation.
    /// </summary>
    public class ClimateFlowInput : IFlowInput
    {
        private readonly float[] annualRainfall;
        private readonly float[] landEvaporation;

        public ClimateFlowInput(float[] annualRainfall, float[] landEvaporation = null)
        {
            this.annualRainfall = annualRainfall;
            this.landEvaporation = landEvaporation;
        }

        public float GetInputFlow(int tileID)
        {
            if (annualRainfall == null || tileID < 0 || tileID >= annualRainfall.Length)
                return 0f;
            float rainMm = Mathf.Max(0f, annualRainfall[tileID]);
            float evapMm = (landEvaporation != null && tileID < landEvaporation.Length)
                ? Mathf.Max(0f, landEvaporation[tileID])
                : 0f;
            float runoffMm = Mathf.Max(0f, rainMm - evapMm);
            return runoffMm * EarthScaleConstants.TILE_AREA_KM2;
        }
    }
}
