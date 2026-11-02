using UnityEngine;
using Planets.Core.Configuration;
using Planets.WorldGen.Climate;
using Planets.WorldGen.Climate.Moisture;
using Planets.WorldGen.Constants;

namespace Planets.WorldGen.Hydrology.Models
{
    /// <summary>
    /// Provides open-water lake evaporation for Tier 2 hydrology (WS21 / WS30).
    /// Uses WS30's bulk aerodynamic open-water formula on the tile's climate state (falling back to PET when
    /// moisture arrays are unavailable), scaled by <see cref="HydrologyConfiguration.evaporationMultiplier"/>
    /// and clamped to <see cref="HydrologyConfiguration.minEvaporationMmDay"/>..<see cref="HydrologyConfiguration.maxEvaporationMmDay"/>.
    /// </summary>
    public class ClimateEvaporationInput : IEvaporationInput
    {
        private readonly ClimateData climate;
        private readonly float[] potentialEvapotranspiration;
        private readonly HydrologyConfiguration config;
        private readonly float ce;
        private readonly float gustMs;

        public ClimateEvaporationInput(
            float[] potentialEvapotranspiration,
            HydrologyConfiguration config = null,
            ClimateData climate = null,
            ClimateConfiguration climateConfig = null)
        {
            this.potentialEvapotranspiration = potentialEvapotranspiration;
            this.config = config;
            this.climate = climate;
            this.ce = climateConfig != null ? climateConfig.oceanEvapCoeff.CurrentValue : 1.9e-3f;
            this.gustMs = climateConfig != null ? climateConfig.oceanEvapGustMs.CurrentValue : 2.5f;
        }

        public float GetEvaporationRate(int tileID)
        {
            float evapMmYear = ComputeBulkLakeEvapMmYear(tileID);
            if (evapMmYear <= 0f && potentialEvapotranspiration != null && tileID >= 0 && tileID < potentialEvapotranspiration.Length)
                evapMmYear = Mathf.Max(0f, potentialEvapotranspiration[tileID]);

            if (config != null)
            {
                float evapMmDay = evapMmYear / EarthScaleConstants.EARTH_DAYS_PER_YEAR;
                evapMmDay *= config.evaporationMultiplier.CurrentValue;
                evapMmDay = Mathf.Clamp(evapMmDay, config.minEvaporationMmDay.CurrentValue, config.maxEvaporationMmDay.CurrentValue);
                evapMmYear = evapMmDay * EarthScaleConstants.EARTH_DAYS_PER_YEAR;
            }

            return evapMmYear * EarthScaleConstants.TILE_AREA_KM2;
        }

        private float ComputeBulkLakeEvapMmYear(int tileID)
        {
            if (climate?.AnnualMeanTemp == null || tileID < 0 || tileID >= climate.AnnualMeanTemp.Length)
                return 0f;

            float tAir = climate.AnnualMeanTemp[tileID];
            if (tAir <= MoistureMath.IceC)
                return 50f;

            float qSat = MoistureMath.SpecificHumidity(tAir);
            float relHum = climate.AdvectedDryingRatio != null && tileID < climate.AdvectedDryingRatio.Length
                ? Mathf.Clamp(climate.AdvectedDryingRatio[tileID], 0.05f, 0.95f)
                : 0.50f;
            float spd = climate.WindSpeed != null && tileID < climate.WindSpeed.Length
                ? Mathf.Max(climate.WindSpeed[tileID], gustMs)
                : gustMs;

            // WS26 / WS30 open-water lake bulk evaporation (0.8 of open-ocean bulk rate, converted from mm/s to mm/year)
            float dq = Mathf.Max(qSat * (1f - MoistureMath.HsSlope * relHum), 0.15f * qSat);
            float rateMmPerSec = 0.8f * MoistureMath.RhoAir * ce * spd * dq;
            return rateMmPerSec * MoistureMath.YearSeconds;
        }
    }
}
