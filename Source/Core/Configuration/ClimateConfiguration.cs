using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Planets.Core.Configuration
{
    /// <summary>
    /// User-configurable and internal climate model parameters.
    /// Used both as the global mod settings configuration and as a per-world snapshot inside <see cref="WorldGen.Climate.ClimateBakeRecord"/>.
    /// All fields register into <see cref="entries"/> on construction so serialization, resets, modification checks, and deep copies remain synchronized.
    /// </summary>
    public class ClimateConfiguration : IExposable
    {
        private readonly List<IConfigValue> entries = new List<IConfigValue>();

        /// <summary>When true, Scribe writes every entry including values equal to their default (used by <see cref="WorldGen.Climate.ClimateBakeRecord"/>).</summary>
        public bool ForceSaveAll;

        // ========== GROUPED OUTCOME MULTIPLIERS (WS24) ==========

        /// <summary>Multiplier (0 to 2) on inland reach of ocean air moderation (<see cref="maritimeFadeKm"/>, <see cref="maritimeBreezeKm"/>, <see cref="oceanEfoldKm"/>, <see cref="coastBlendKm"/>).</summary>
        public ConfigValue<float> grpOceanReach;

        /// <summary>Multiplier (0 to 2) on inland moisture transport reach (<see cref="moistEddyK"/>, <see cref="rainTimescaleDays"/>).</summary>
        public ConfigValue<float> grpMoistureReach;

        /// <summary>Multiplier (0 to 2) on topographic uplift and rain-shadow strength (<see cref="slopeThreshold"/>).</summary>
        public ConfigValue<float> grpRainShadow;

        /// <summary>Multiplier (0 to 2) on ocean current temperature anomalies (<see cref="warmWBCurrent"/>, <see cref="coolEBCurrent"/>, <see cref="stormWarmCurrent"/>).</summary>
        public ConfigValue<float> grpCurrents;

        /// <summary>Multiplier (0 to 2) on continental heat-low and seasonal monsoon circulation (<see cref="circHeatWarmHpaPerC"/>, <see cref="circSeasonalProfileScale"/>).</summary>
        public ConfigValue<float> grpMonsoon;

        /// <summary>Multiplier (0 to 2) on subtropical sinking-air desert suppression (<see cref="rainThresholdSink"/>).</summary>
        public ConfigValue<float> grpSubsidence;

        // ========== MANUAL TEMPERATURE TARGETS ==========

        /// <summary>When true, use manual polar/equatorial targets instead of the world creation temperature slider.</summary>
        public ConfigValue<bool> manualTemperatureEnabled;

        /// <summary>Manual polar mean temperature (°C) when <see cref="manualTemperatureEnabled"/> is true.</summary>
        public ConfigValue<float> manualPolarTemp;

        /// <summary>Manual equatorial mean temperature (°C) when <see cref="manualTemperatureEnabled"/> is true.</summary>
        public ConfigValue<float> manualEquatorialTemp;

        // ========== TEMPERATURE MODIFIERS ==========

        /// <summary>Exponent shaping the latitude-to-temperature cosine curve (0.8 to 2.0).</summary>
        public ConfigValue<float> latTempExponent;

        /// <summary>Additional cooling (°C) ramping from 70° latitude to the poles.</summary>
        public ConfigValue<float> polarColdExtra;

        // ========== CONTINENTALITY & COASTAL MODERATION ==========

        /// <summary>E-fold distance scale (km) for nearest-coast continentality when the wind-transported maritime layer is not used.</summary>
        public ConfigValue<float> oceanEfoldKm;

        /// <summary>Mean annual temperature penalty (°C) at full continentality.</summary>
        public ConfigValue<float> inlandMeanPenalty;

        /// <summary>E-fold distance (km) over which sea-level land temperature blends toward the nearest ocean when the maritime layer is not used.</summary>
        public ConfigValue<float> coastBlendKm;

        // ========== OCEAN CURRENTS ==========

        /// <summary>Western boundary current warming anomaly (°C).</summary>
        public ConfigValue<float> warmWBCurrent;

        /// <summary>Eastern boundary current cooling anomaly (°C).</summary>
        public ConfigValue<float> coolEBCurrent;

        /// <summary>Mid-latitude (~50°) storm-track ocean warming anomaly (°C).</summary>
        public ConfigValue<float> stormWarmCurrent;

        // ========== MANUAL RAINFALL OVERRIDE ==========

        /// <summary>When true, use <see cref="manualBaseRain"/> instead of the world creation rainfall preset.</summary>
        public ConfigValue<bool> manualRainfallEnabled;

        /// <summary>Manual base rainfall (mm/year) when <see cref="manualRainfallEnabled"/> is true.</summary>
        public ConfigValue<float> manualBaseRain;

        // ========== DEBUG & DEVELOPMENT FLAGS ==========

        /// <summary>Debug toggle: sample elevations from ETOPO 0.25° binary grid on 100% coverage worlds.</summary>
        public ConfigValue<bool> useEarthHeightmap;

        /// <summary>Path to <c>etopo_0p25.bin</c> when <see cref="useEarthHeightmap"/> is enabled.</summary>
        public ConfigValue<string> earthHeightmapPath;

        // ========== DIAGNOSTIC CIRCULATION PARAMETERS ==========

        /// <summary>Broad warm-land pressure response (hPa/°C) applied to both layers.</summary>
        public ConfigValue<float> circHeatWarmHpaPerC;

        /// <summary>Broad cold-air (lower layer) and warm-ocean (both layers) pressure response (hPa/°C).</summary>
        public ConfigValue<float> circHeatColdHpaPerC;

        /// <summary>Air temperature (°C) at and above which a negative temperature departure produces no shallow cold high.</summary>
        public ConfigValue<float> circColdGateC;

        /// <summary>Temperature span (°C) over which the shallow cold-high gate ramps to full strength.</summary>
        public ConfigValue<float> circColdGateSpanC;

        /// <summary>Local temperature anomaly smoothing scale (km).</summary>
        public ConfigValue<float> circSmoothingKm;

        /// <summary>Broad heat-low / cold-high temperature anomaly smoothing scale (km).</summary>
        public ConfigValue<float> circBroadSmoothingKm;

        /// <summary>Pressure response (hPa/°C) for the local 400 km temperature residual inland.</summary>
        public ConfigValue<float> circHeatLocalHpaPerC;

        /// <summary>Pressure response (hPa/°C) for the local 400 km temperature residual at the open ocean coast.</summary>
        public ConfigValue<float> circHeatLocalCoastHpaPerC;

        /// <summary>Distance from open ocean (km) over which the local residual blends from coastal to inland strength.</summary>
        public ConfigValue<float> circLocalCoastKm;

        /// <summary>Multiplier on the latitudinal sea-level pressure profile.</summary>
        public ConfigValue<float> circPressureProfileScale;

        /// <summary>Exponent scaling the latitudinal pressure profile by band ocean fraction relative to the southern-ocean reference.</summary>
        public ConfigValue<float> circCurveOceanPower;

        /// <summary>Multiplier on the seasonal winter-minus-summer ocean pressure delta profile.</summary>
        public ConfigValue<float> circSeasonalProfileScale;

        /// <summary>Weight (0 to 1) shifting the zonal reference temperature from all cells toward ocean-only cells in each latitude band.</summary>
        public ConfigValue<float> circOceanReference;

        /// <summary>Linear Rayleigh friction coefficient over ocean (s⁻¹).</summary>
        public ConfigValue<float> circFrictionOcean;

        /// <summary>Linear Rayleigh friction coefficient over land (s⁻¹).</summary>
        public ConfigValue<float> circFrictionLand;

        /// <summary>Ratio of surface wind speed to layer wind speed over ocean.</summary>
        public ConfigValue<float> circSurfaceOcean;

        /// <summary>Ratio of surface wind speed to layer wind speed over land.</summary>
        public ConfigValue<float> circSurfaceLand;

        // ========== MARITIME ADVECTION PARAMETERS ==========

        /// <summary>E-fold decay distance (km) for ocean-air fraction advected over land.</summary>
        public ConfigValue<float> maritimeFadeKm;

        /// <summary>Eastward storm-track drift added to maritime transport as a multiple of zonal-mean westerly wind.</summary>
        public ConfigValue<float> maritimeStormDrift;

        /// <summary>E-fold decay distance (km) for continental air advected over water.</summary>
        public ConfigValue<float> maritimeOceanFadeKm;

        /// <summary>Baseline open-ocean seasonal amplitude multiplier when unaffected by continental air.</summary>
        public ConfigValue<float> oceanSwingOpenShare;

        /// <summary>Additional ocean seasonal amplitude multiplier per unit of advected continental air.</summary>
        public ConfigValue<float> oceanSwingContinentalGain;

        /// <summary>Omnidirectional coastal sea-breeze e-fold reach (km).</summary>
        public ConfigValue<float> maritimeBreezeKm;

        /// <summary>Weight of the local winter snapshot in annual continentality.</summary>
        public ConfigValue<float> maritimeWinterWeight;

        /// <summary>Maximum sea-level temperature blend weight toward upwind SST for fully maritime land.</summary>
        public ConfigValue<float> maritimeBlendWeight;

        // ========== WS30 / WS31 TWO-LAYER MOISTURE BUDGET ==========

        /// <summary>Enable the upper-troposphere moisture layer on the 40,962-cell coarse mesh (WS31).</summary>
        public ConfigValue<bool> twoLayerMoisture;

        /// <summary>Baseline eddy diffusivity K in the trades (m²/s).</summary>
        public ConfigValue<float> moistEddyK;

        /// <summary>Reference lower-layer depth scale (m).</summary>
        public ConfigValue<float> moistLayerDepthM;

        /// <summary>Fraction (ε) of rising-air moisture that precipitates locally in the lower layer; the remainder enters the upper layer.</summary>
        public ConfigValue<float> rainEfficiency;

        /// <summary>Baseline relative humidity threshold (r_c0) for condensation.</summary>
        public ConfigValue<float> rainThreshold;

        /// <summary>Threshold shift coefficient (β) from rising/sinking air divergence.</summary>
        public ConfigValue<float> rainThresholdSink;

        /// <summary>Humid-air condensation relaxation timescale (days).</summary>
        public ConfigValue<float> rainTimescaleDays;

        /// <summary>Threshold shift coefficient (γ) from directional slope ascent/descent.</summary>
        public ConfigValue<float> slopeThreshold;

        /// <summary>Reference vertical velocity scale w_ref (m/s) for slope ascent.</summary>
        public ConfigValue<float> slopeRefVelocity;

        /// <summary>Bulk aerodynamic ocean evaporation transfer coefficient C_E.</summary>
        public ConfigValue<float> oceanEvapCoeff;

        /// <summary>Sub-grid surface wind gust floor (m/s) for bulk ocean evaporation.</summary>
        public ConfigValue<float> oceanEvapGustMs;

        /// <summary>Fu-Budyko land evapotranspiration shape parameter w.</summary>
        public ConfigValue<float> budykoW;

        /// <summary>Fraction of carrier horizontal divergence retained after subtracting the mass-continuity return flow.</summary>
        public ConfigValue<float> moistDivKeep;

        /// <summary>95th-percentile physical cap (s⁻¹) on the rising-air divergence trigger.</summary>
        public ConfigValue<float> moistTriggerD95;

        /// <summary>Include the 1 + 0.15 × continentality stress factor in PET.</summary>
        public ConfigValue<bool> petContinentalStress;

        /// <summary>Include the high-latitude (>60°) factor in PET.</summary>
        public ConfigValue<bool> petPolarFactor;

        // ========== MOUNTAINS ==========

        /// <summary>Scales land elevation above sea level (0.5 to 1.5).</summary>
        public ConfigValue<float> mountainHeight;

        /// <summary>Scales hilliness classification thresholds (0.0 to 2.0; higher = more mountains).</summary>
        public ConfigValue<float> mountainousness;

        /// <summary>Legacy hilliness bias (-0.9 to +0.9), migrated into <see cref="mountainousness"/> on load.</summary>
        public ConfigValue<float> hillinessBias;

        // ========== EFFECTIVE OUTCOME-SCALED ACCESSORS (WS24-1) ==========

        public float EffectiveMaritimeFadeKm => maritimeFadeKm.CurrentValue * grpOceanReach.CurrentValue;
        public float EffectiveMaritimeBreezeKm => maritimeBreezeKm.CurrentValue * grpOceanReach.CurrentValue;
        public float EffectiveOceanEfoldKm => oceanEfoldKm.CurrentValue * grpOceanReach.CurrentValue;
        public float EffectiveCoastBlendKm => coastBlendKm.CurrentValue * grpOceanReach.CurrentValue;

        public float EffectiveMoistEddyK => moistEddyK.CurrentValue * grpMoistureReach.CurrentValue;
        public float EffectiveRainTimescaleDays => rainTimescaleDays.CurrentValue * Mathf.Max(grpMoistureReach.CurrentValue, 0.1f);

        public float EffectiveSlopeThreshold => slopeThreshold.CurrentValue * grpRainShadow.CurrentValue;

        public float EffectiveWarmWBCurrent => warmWBCurrent.CurrentValue * grpCurrents.CurrentValue;
        public float EffectiveCoolEBCurrent => coolEBCurrent.CurrentValue * grpCurrents.CurrentValue;
        public float EffectiveStormWarmCurrent => stormWarmCurrent.CurrentValue * grpCurrents.CurrentValue;

        public float EffectiveCircHeatWarmHpaPerC => circHeatWarmHpaPerC.CurrentValue * grpMonsoon.CurrentValue;
        public float EffectiveCircSeasonalProfileScale => circSeasonalProfileScale.CurrentValue * grpMonsoon.CurrentValue;

        public float EffectiveRainThresholdSink => rainThresholdSink.CurrentValue * grpSubsidence.CurrentValue;

        public ClimateConfiguration()
        {
            grpOceanReach = new ConfigValue<float>(1f, "grpOceanReach", entries);
            grpMoistureReach = new ConfigValue<float>(1f, "grpMoistureReach", entries);
            grpRainShadow = new ConfigValue<float>(1f, "grpRainShadow", entries);
            grpCurrents = new ConfigValue<float>(1f, "grpCurrents", entries);
            grpMonsoon = new ConfigValue<float>(1f, "grpMonsoon", entries);
            grpSubsidence = new ConfigValue<float>(1f, "grpSubsidence", entries);

            manualTemperatureEnabled = new ConfigValue<bool>(false, "manualTemperatureEnabled", entries);
            manualPolarTemp = new ConfigValue<float>(-29.5f, "manualPolarTemp", entries);
            manualEquatorialTemp = new ConfigValue<float>(31f, "manualEquatorialTemp", entries);

            latTempExponent = new ConfigValue<float>(1.0f, "latTempExponent", entries);
            polarColdExtra = new ConfigValue<float>(-6.0f, "polarColdExtra", entries);

            oceanEfoldKm = new ConfigValue<float>(350f, "oceanEfoldKm", entries, isDebugOnly: true);
            inlandMeanPenalty = new ConfigValue<float>(0.5f, "inlandMeanPenalty", entries);
            coastBlendKm = new ConfigValue<float>(60f, "coastBlendKm", entries, isDebugOnly: true);

            warmWBCurrent = new ConfigValue<float>(2.5f, "warmWBCurrent", entries, isDebugOnly: true);
            coolEBCurrent = new ConfigValue<float>(-2.0f, "coolEBCurrent", entries, isDebugOnly: true);
            stormWarmCurrent = new ConfigValue<float>(0.8f, "stormWarmCurrent", entries, isDebugOnly: true);

            manualRainfallEnabled = new ConfigValue<bool>(false, "manualRainfallEnabled", entries);
            manualBaseRain = new ConfigValue<float>(1000f, "manualBaseRain", entries);

            useEarthHeightmap = new ConfigValue<bool>(false, "useEarthHeightmap", entries, isDebugOnly: true);
            earthHeightmapPath = new ConfigValue<string>("", "earthHeightmapPath", entries, isDebugOnly: true);

            circHeatWarmHpaPerC = new ConfigValue<float>(2.5f, "circHeatWarmHpaPerC", entries, isDebugOnly: true);
            circHeatColdHpaPerC = new ConfigValue<float>(2f, "circHeatColdHpaPerC", entries, isDebugOnly: true);
            circColdGateC = new ConfigValue<float>(5f, "circColdGateC", entries, isDebugOnly: true);
            circColdGateSpanC = new ConfigValue<float>(20f, "circColdGateSpanC", entries, isDebugOnly: true);
            circSmoothingKm = new ConfigValue<float>(400f, "circSmoothingKm", entries, isDebugOnly: true);
            circBroadSmoothingKm = new ConfigValue<float>(2500f, "circBroadSmoothingKm", entries, isDebugOnly: true);
            circHeatLocalHpaPerC = new ConfigValue<float>(0.75f, "circHeatLocalHpaPerC", entries, isDebugOnly: true);
            circHeatLocalCoastHpaPerC = new ConfigValue<float>(1f, "circHeatLocalCoastHpaPerC", entries, isDebugOnly: true);
            circLocalCoastKm = new ConfigValue<float>(800f, "circLocalCoastKm", entries, isDebugOnly: true);
            circPressureProfileScale = new ConfigValue<float>(1f, "circPressureProfileScale", entries, isDebugOnly: true);
            circCurveOceanPower = new ConfigValue<float>(1f, "circCurveOceanPower", entries, isDebugOnly: true);
            circSeasonalProfileScale = new ConfigValue<float>(1f, "circSeasonalProfileScale", entries, isDebugOnly: true);
            circOceanReference = new ConfigValue<float>(1f, "circOceanReference", entries, isDebugOnly: true);
            circFrictionOcean = new ConfigValue<float>(1e-5f, "circFrictionOcean", entries, isDebugOnly: true);
            circFrictionLand = new ConfigValue<float>(7.2e-5f, "circFrictionLand", entries, isDebugOnly: true);
            circSurfaceOcean = new ConfigValue<float>(0.7f, "circSurfaceOcean", entries, isDebugOnly: true);
            circSurfaceLand = new ConfigValue<float>(0.5f, "circSurfaceLand", entries, isDebugOnly: true);

            maritimeFadeKm = new ConfigValue<float>(3000f, "maritimeFadeKm", entries, isDebugOnly: true);
            maritimeStormDrift = new ConfigValue<float>(1f, "maritimeStormDrift", entries, isDebugOnly: true);
            maritimeOceanFadeKm = new ConfigValue<float>(2000f, "maritimeOceanFadeKm", entries, isDebugOnly: true);
            oceanSwingOpenShare = new ConfigValue<float>(0.39f, "oceanSwingOpenShare", entries, isDebugOnly: true);
            oceanSwingContinentalGain = new ConfigValue<float>(0.66f, "oceanSwingContinentalGain", entries, isDebugOnly: true);
            maritimeBreezeKm = new ConfigValue<float>(30f, "maritimeBreezeKm", entries, isDebugOnly: true);
            maritimeWinterWeight = new ConfigValue<float>(0.5f, "maritimeWinterWeight", entries, isDebugOnly: true);
            maritimeBlendWeight = new ConfigValue<float>(0.5f, "maritimeBlendWeight", entries, isDebugOnly: true);

            twoLayerMoisture = new ConfigValue<bool>(true, "twoLayerMoisture", entries, isDebugOnly: true);
            moistEddyK = new ConfigValue<float>(3.0e5f, "moistEddyK", entries, isDebugOnly: true);
            moistLayerDepthM = new ConfigValue<float>(1000f, "moistLayerDepthM", entries, isDebugOnly: true);
            rainEfficiency = new ConfigValue<float>(0.6f, "rainEfficiency", entries, isDebugOnly: true);
            rainThreshold = new ConfigValue<float>(0.75f, "rainThreshold", entries, isDebugOnly: true);
            rainThresholdSink = new ConfigValue<float>(0.15f, "rainThresholdSink", entries, isDebugOnly: true);
            rainTimescaleDays = new ConfigValue<float>(2.0f, "rainTimescaleDays", entries, isDebugOnly: true);
            slopeThreshold = new ConfigValue<float>(0.2f, "slopeThreshold", entries, isDebugOnly: true);
            slopeRefVelocity = new ConfigValue<float>(0.05f, "slopeRefVelocity", entries, isDebugOnly: true);
            oceanEvapCoeff = new ConfigValue<float>(1.9e-3f, "oceanEvapCoeff", entries, isDebugOnly: true);
            oceanEvapGustMs = new ConfigValue<float>(2.5f, "oceanEvapGustMs", entries, isDebugOnly: true);
            budykoW = new ConfigValue<float>(2.6f, "budykoW", entries, isDebugOnly: true);
            moistDivKeep = new ConfigValue<float>(0.35f, "moistDivKeep", entries, isDebugOnly: true);
            moistTriggerD95 = new ConfigValue<float>(2.5e-6f, "moistTriggerD95", entries, isDebugOnly: true);
            petContinentalStress = new ConfigValue<bool>(true, "petContinentalStress", entries, isDebugOnly: true);
            petPolarFactor = new ConfigValue<bool>(true, "petPolarFactor", entries, isDebugOnly: true);

            hillinessBias = new ConfigValue<float>(0f, "hillinessBias", entries);
            mountainHeight = new ConfigValue<float>(1f, "mountainHeight", entries);
            mountainousness = new ConfigValue<float>(1f, "mountainousness", entries);
        }

        public void ExposeData()
        {
            for (int i = 0; i < entries.Count; i++)
                entries[i].Look(ForceSaveAll);

            // Migrate legacy 7d75fba hillinessBias into mountainousness once on load.
            if (Scribe.mode == LoadSaveMode.LoadingVars
                && !Mathf.Approximately(hillinessBias.CurrentValue, 0f)
                && Mathf.Approximately(mountainousness.CurrentValue, mountainousness.DefaultValue))
            {
                mountainousness.CurrentValue = Mathf.Clamp(1f + hillinessBias.CurrentValue, 0f, 2f);
                hillinessBias.CurrentValue = 0f;
            }
        }

        /// <summary>Reset user-facing parameters to default values. Debug checkboxes on the mod settings screen are left untouched.</summary>
        public void ResetToDefaults()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].IsDebugOnly)
                    entries[i].Reset();
            }
        }

        /// <summary>Reset developer model constants (leaving Earth heightmap settings untouched).</summary>
        public void ResetModelConstants()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                IConfigValue entry = entries[i];
                if (entry.IsDebugOnly && entry != useEarthHeightmap && entry != earthHeightmapPath)
                    entry.Reset();
            }
        }

        /// <summary>True if any user-facing parameter differs from its default value.</summary>
        public bool HasModifications()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                IConfigValue entry = entries[i];
                if (entry.IsDebugOnly)
                    continue;
                if (entry.IsModified)
                    return true;
            }
            return false;
        }

        /// <summary>Copy all parameter values from <paramref name="source"/>.</summary>
        public void CopyFrom(ClimateConfiguration source)
        {
            if (source == null)
                return;
            int count = Mathf.Min(entries.Count, source.entries.Count);
            for (int i = 0; i < count; i++)
                entries[i].CopyValueFrom(source.entries[i]);
        }
    }
}
