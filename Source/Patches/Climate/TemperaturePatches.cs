using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.WorldGen;
using ClimateData = Planets.WorldGen.Climate.Climate;

namespace Planets.Patches.Climate
{
    /// <summary>
    /// Temperature system patches - replaces vanilla temperature calculations with RP's climate system.
    /// 
    /// Four integrated patches:
    /// 1. SeasonalShiftAmplitudeAt   - Physics-based seasonal amplitude from AxialTiltCurves
    /// 2. GetTemperatureCurve        - Custom temperature curves for all 7 OverallTemperature presets
    /// 3. Min/MaxTemperatureAtTile   - Use RP climate data for tooltips/UI
    /// 4. OffsetFromSunCycle         - Climate-driven diurnal (day/night) temperature swing
    /// 
    /// Patch 2's curves are tuned against the current temperature baselines
    /// defined in WorldGenerationOrchestrator.SetTemperatureBaselines
    /// (PoleMean/EquatorMean pairs for each OverallTemperature preset), so the
    /// vanilla temperature slider presets and RP's climate outputs stay in sync.
    /// All four patches share the same OverallTemperature enum semantics.
    /// </summary>

    // ========== PATCH 1: SEASONAL AMPLITUDE ==========

    /// <summary>
    /// Replaces vanilla seasonal amplitude with RP's seasonal shift amplitude.
    /// 
    /// SEMANTICS:
    /// - ClimateData.SeasonalAmp stores FULL ANNUAL RANGE (warmest to coldest)
    /// - Vanilla expects SEASONAL SHIFT AMPLITUDE (deviation from mean = half-range)
    /// - Therefore we return SeasonalAmp / 2
    /// 
    /// The computed SeasonalAmp accounts for:
    /// - Base amplitude from axial tilt
    /// - Ocean damping (thermal inertia reduces ocean swings)
    /// - Continental amplification (interior land has larger swings)
    /// - Elevation damping (high mountains have reduced seasonality)
    /// 
    /// This ensures growing period calculations use the correct seasonal variation.
    /// </summary>
    [HarmonyPatch(typeof(GenTemperature), "SeasonalShiftAmplitudeAt")]
    public static class Patch_SeasonalAmplitude
    {
        public static bool Prefix(PlanetTile tile, ref float __result)
        {
            if (TemperaturePatchScope.ModelOff(Controller.Settings?.featureConfig?.enableSeasonalAmplitudeModel))
                return true;
            if (!TemperaturePatchScope.IsSurface(tile) || !ClimateData.HasSeasonalAmp(tile.tileId))
                return true;

            // SeasonalAmp is the full annual range. Vanilla expects the half-range shift.
            float latitude = Find.WorldGrid.LongLatOf(tile).y;
            float shiftAmplitude = ClimateData.SeasonalAmp(tile.tileId) / 2f;
            __result = latitude >= 0f ? shiftAmplitude : -shiftAmplitude;
            return false;
        }
    }

    // ========== PATCH 2: TEMPERATURE PRESET CURVES ==========

    /// <summary>
    /// Replaces vanilla temperature curves with custom-tuned curves for all 7 OverallTemperature presets.
    /// These curves map latitude → temperature offset for each preset level.
    /// 
    /// Note: Normal preset is NOT overridden - vanilla curve is used.
    /// Curves are kept inline rather than extracted to data files (simpler maintenance).
    /// </summary>
    [HarmonyPatch(typeof(OverallTemperatureUtility), "GetTemperatureCurve")]
    public static class Patch_TemperatureCurves
    {
        public static void Postfix(OverallTemperature overallTemperature, ref SimpleCurve __result)
        {
            if (TemperaturePatchScope.ModelOff(Controller.Settings?.featureConfig?.enableTemperatureCurves))
                return;

            // Each curve maps latitude → temperature delta
            // Format: CurvePoint(latitude, temperatureDelta)
            
            switch (overallTemperature)
            {
                case OverallTemperature.VeryCold:
                    __result = new SimpleCurve
                    {
                        { new CurvePoint(-9999f, -9999f), true },
                        { new CurvePoint(-50f, -90f), true },
                        { new CurvePoint(-40f, -75f), true },
                        { new CurvePoint(0f, -50f), true },
                        { new CurvePoint(20f, -43f), true },
                        { new CurvePoint(25f, -33f), true },
                        { new CurvePoint(30f, -23.5f), true },
                        { new CurvePoint(50f, -22f), true }
                    };
                    break;

                case OverallTemperature.Cold:
                    __result = new SimpleCurve
                    {
                        { new CurvePoint(-9999f, -9999f), true },
                        { new CurvePoint(-50f, -78f), true },
                        { new CurvePoint(-25f, -48f), true },
                        { new CurvePoint(-20f, -33f), true },
                        { new CurvePoint(-13f, -23f), true },
                        { new CurvePoint(0f, -20f), true },
                        { new CurvePoint(30f, -11f), true },
                        { new CurvePoint(60f, 17f), true }
                    };
                    break;

                case OverallTemperature.LittleBitColder:
                    __result = new SimpleCurve
                    {
                        { new CurvePoint(-9999f, -9999f), true },
                        { new CurvePoint(-20f, -25f), true },
                        { new CurvePoint(-15f, -18f), true },
                        { new CurvePoint(-5f, -16f), true },
                        { new CurvePoint(40f, 27f), true },
                        { new CurvePoint(9999f, 9999f), true }
                    };
                    break;

                case OverallTemperature.LittleBitWarmer:
                    __result = new SimpleCurve
                    {
                        { new CurvePoint(-9999f, -9999f), true },
                        { new CurvePoint(-45f, -32f), true },
                        { new CurvePoint(40f, 53f), true },
                        { new CurvePoint(120f, 123f), true },
                        { new CurvePoint(9999f, 9999f), true }
                    };
                    break;

                case OverallTemperature.Hot:
                    __result = new SimpleCurve
                    {
                        { new CurvePoint(-45f, -14f), true },
                        { new CurvePoint(-25f, -4f), true },
                        { new CurvePoint(-22f, 10f), true },
                        { new CurvePoint(-10f, 33f), true },
                        { new CurvePoint(40f, 65f), true },
                        { new CurvePoint(120f, 128f), true },
                        { new CurvePoint(9999f, 9999f), true }
                    };
                    break;

                case OverallTemperature.VeryHot:
                    __result = new SimpleCurve
                    {
                        { new CurvePoint(-45f, 40f), true },
                        { new CurvePoint(0f, 55f), true },
                        { new CurvePoint(33f, 95f), true },
                        { new CurvePoint(40f, 103f), true },
                        { new CurvePoint(120f, 135f), true },
                        { new CurvePoint(9999f, 9999f), true }
                    };
                    break;

                // Normal preset uses vanilla curve (no override)
                default:
                    break;
            }
        }
    }

    // ========== PATCH 3: MIN/MAX TEMPERATURE FOR UI ==========

    /// <summary>
    /// Replaces vanilla MinTemperatureAtTile/MaxTemperatureAtTile with RP's detailed climate values.
    /// Ensures tooltips, map modes, and all UI displays show correct seasonal temperature extremes.
    /// 
    /// Uses Climate facade for safe access to climate cache (with vanilla fallback).
    /// </summary>
    [HarmonyPatch(typeof(GenTemperature))]
    public static class Patch_MinMaxTemperature
    {
        /// <summary>
        /// Replace vanilla's MinTemperatureAtTile with RP's WinterMin from climate cache.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("MinTemperatureAtTile")]
        public static bool MinTemperatureAtTile_Prefix(PlanetTile tile, ref float __result)
        {
            if (!tile.Valid)
            {
                __result = 21f;
                return false;
            }

            if (TemperaturePatchScope.ModelOff(Controller.Settings?.featureConfig?.enableMinMaxTempModel))
                return true;
            if (!TemperaturePatchScope.IsSurface(tile) || !ClimateData.IsInitialized())
                return true;

            __result = ClimateData.WinterMin(tile.tileId);
            return false;
        }

        /// <summary>
        /// Replace vanilla's MaxTemperatureAtTile with RP's SummerMax from climate cache.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("MaxTemperatureAtTile")]
        public static bool MaxTemperatureAtTile_Prefix(PlanetTile tile, ref float __result)
        {
            if (!tile.Valid)
            {
                __result = 21f;
                return false;
            }

            if (TemperaturePatchScope.ModelOff(Controller.Settings?.featureConfig?.enableMinMaxTempModel))
                return true;
            if (!TemperaturePatchScope.IsSurface(tile) || !ClimateData.IsInitialized())
                return true;

            __result = ClimateData.SummerMax(tile.tileId);
            return false;
        }
    }

    // ========== PATCH 4: DIURNAL TEMPERATURE FLUCTUATION ==========

    /// <summary>
    /// Replaces vanilla's fixed +/- 7°C daily swing with a climate-driven amplitude based on Sun Altitude.
    /// 
    /// Logic:
    /// - Half-range comes from the daily-range estimate (dry inland wider, wet coast narrower),
    ///   with a 1.2 lift above 2 km. Built once per world from saved climate.
    /// - The sun's altitude through the day sets the shape. A full equatorial day reaches that half-range.
    ///   A short arc, polar day, or a day that never rises stays small or flat.
    /// - On a colony map, cloud cover pulls the result toward zero. Seasonal amplitude is untouched.
    /// </summary>
    [HarmonyPatch(typeof(GenTemperature), "OffsetFromSunCycle")]
    public static class Patch_DiurnalFluctuation
    {
        public static bool Prefix(int absTick, PlanetTile tile, ref float __result)
        {
            if (TemperaturePatchScope.ModelOff(Controller.Settings?.featureConfig?.enableDiurnalModel))
                return true;
            if (!TemperaturePatchScope.IsSurface(tile) || !ClimateData.IsInitialized())
                return true;

            __result = DiurnalSwing.Offset(absTick, tile);
            if (!TemperaturePatchScope.ModelOff(Controller.Settings?.featureConfig?.enableWeatherDiurnal))
                __result = DiurnalSwing.ApplyWeather(__result, tile);
            return false;
        }
    }

    static class TemperaturePatchScope
    {
        public static bool ModelOff(ConfigValue<bool> toggle) => toggle != null && !toggle.CurrentValue;

        public static bool IsSurface(PlanetTile tile)
        {
            WorldGrid grid = Find.WorldGrid;
            return tile.Valid && grid?.Surface != null && tile.Layer == grid.Surface;
        }
    }
}
