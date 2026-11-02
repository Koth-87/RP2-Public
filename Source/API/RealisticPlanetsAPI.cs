using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Climate;

namespace Planets.Api
{
    /// <summary>
    /// Stable entry point for other mods. Look this type up by name. Do not reference this assembly.
    /// Bump <see cref="ApiVersion"/> only when a name or a meaning changes.
    /// </summary>
    public static class RealisticPlanetsApi
    {
        public const int ApiVersion = 2;

        /// <summary>
        /// Axial tilt in degrees for the loaded world, or the current setting before a world exists.
        /// Normal is 22.5. The yearly sun-budget in the climate bake is zero at that value.
        /// </summary>
        public static float TiltDegrees =>
            AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt);

        /// <summary>
        /// Year fraction. 0 is the spring equinox. 0.25 is day 15, the northern summer solstice.
        /// 0.75 is the opposite solstice, southern summer. The same number is used in both hemispheres.
        /// </summary>
        public static float YearPhase(long absTick) => SolarGeometry.YearPhase(absTick);

        /// <summary>
        /// 1 at the northern warmest day (phase 1/3, day 20), 0 at the opposite thermal extremum (phase 5/6),
        /// 0.5 at the thermal equinoxes.
        /// </summary>
        public static float NorthernSummerWeight(float yearPhase)
        {
            return Mathf.Clamp01(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * (yearPhase - 1f / 3f)));
        }

        /// <summary>
        /// Sun altitude in degrees and azimuth in radians. Same geometry as the day-night temperature swing.
        /// </summary>
        public static void SunAltitudeAzimuth(float longitudeDeg, float latitudeDeg, int absTick, out float altitudeDeg, out float azimuthRad)
        {
            SolarGeometry.GetSunAltitudeAzimuth(longitudeDeg, latitudeDeg, absTick, out altitudeDeg, out azimuthRad, out _);
        }

        /// <summary>
        /// True while the climate bake owns annual temperature, including the axial sun-budget,
        /// and the seasonal range. Other mods should not remap latitude temperature during worldgen
        /// and should not multiply <c>SeasonalShiftAmplitudeAt</c>.
        /// </summary>
        public static bool OwnsYearlySunBudget => true;

        /// <summary>
        /// True while RP2 is applying plant rest, winter dormancy, or both.
        /// Off when both plant settings are off. Sky, shadows, and the moon are not part of this flag.
        /// </summary>
        public static bool OwnsPlantSeasons
        {
            get
            {
                FeatureConfiguration features = Controller.Settings?.featureConfig;
                if (features == null)
                    return true;
                return features.enablePlantDormancy.CurrentValue || features.enableShortPlantRest.CurrentValue;
            }
        }

        /// <summary>
        /// Wind velocity in metres per second at one tile for a year phase (API v2).
        /// Blends between the equinox field and the nearer thermal solstice field.
        /// </summary>
        public static Vector3 WindMsAt(PlanetTile tile, float yearPhase)
        {
            if (!tile.Valid || Current.Game == null)
                return Vector3.zero;

            ClimateData climate = Find.World?.GetComponent<ClimateData>();
            int id = tile.tileId;
            Vector3[] summer = climate?.SummerWind;
            Vector3[] winter = climate?.WinterWind;
            Vector3[] equinox = climate?.EquinoxWind;
            if (summer != null && winter != null && id >= 0 && id < summer.Length && id < winter.Length)
            {
                float weight = NorthernSummerWeight(yearPhase);
                if (equinox != null && id < equinox.Length)
                {
                    return weight >= 0.5f
                        ? Vector3.Lerp(equinox[id], summer[id], (weight - 0.5f) * 2f)
                        : Vector3.Lerp(winter[id], equinox[id], weight * 2f);
                }
                return summer[id] * weight + winter[id] * (1f - weight);
            }

            Vector3[] annual = climate?.WindDirection;
            if (annual != null && id >= 0 && id < annual.Length)
            {
                float speed = climate.WindSpeed != null && id < climate.WindSpeed.Length ? climate.WindSpeed[id] : 1f;
                return annual[id] * speed;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Wind direction at one tile for a year phase. Unit length where there is wind; zero below 0.5 m/s.
        /// </summary>
        public static Vector3 WindAt(PlanetTile tile, float yearPhase)
        {
            if (!tile.Valid || Current.Game == null)
                return Vector3.zero;

            ClimateData climate = Find.World?.GetComponent<ClimateData>();
            int id = tile.tileId;
            if (climate?.EquinoxWind != null && id >= 0 && id < climate.EquinoxWind.Length)
            {
                Vector3 ms = WindMsAt(tile, yearPhase);
                return ms.sqrMagnitude >= 0.25f ? ms.normalized : Vector3.zero;
            }

            Vector3[] summer = climate?.SummerWind;
            Vector3[] winter = climate?.WinterWind;
            if (summer != null && winter != null && id >= 0 && id < summer.Length && id < winter.Length)
            {
                float weight = NorthernSummerWeight(yearPhase);
                return summer[id] * weight + winter[id] * (1f - weight);
            }

            Vector3[] annual = climate?.WindDirection;
            if (annual != null && id >= 0 && id < annual.Length)
            {
                float speed = climate.WindSpeed != null && id < climate.WindSpeed.Length ? climate.WindSpeed[id] : 1f;
                return annual[id] * speed;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Dev log. Reads the public names by reflection: tilt, the day-15 phase, both flags, and one tile's wind.
        /// </summary>
        public static void LogContract()
        {
            if (!Controller.DebugEnabled)
                return;
            if (Current.Game == null)
            {
                RPLog.D("[API] Contract log skipped: no game yet.");
                return;
            }

            try
            {
                Type api = AccessTools.TypeByName("Planets.Api.RealisticPlanetsApi");
                if (api == null)
                {
                    RPLog.W("[API] RealisticPlanetsApi was not found by name.");
                    return;
                }

                int version = (int)api.GetField(nameof(ApiVersion)).GetRawConstantValue();
                float tilt = (float)api.GetProperty(nameof(TiltDegrees)).GetValue(null);
                MethodInfo phaseMethod = api.GetMethod(nameof(YearPhase), new[] { typeof(long) });
                long solsticeTick = 15L * GenDate.TicksPerDay;
                float phase = (float)phaseMethod.Invoke(null, new object[] { solsticeTick });
                bool sun = (bool)api.GetProperty(nameof(OwnsYearlySunBudget)).GetValue(null);
                bool plants = (bool)api.GetProperty(nameof(OwnsPlantSeasons)).GetValue(null);

                string wind = "no world";
                PlanetTile tile = FirstSurfaceTile();
                if (tile.Valid)
                {
                    MethodInfo windMethod = api.GetMethod(nameof(WindAt));
                    Vector3 vector = (Vector3)windMethod.Invoke(null, new object[] { tile, phase });
                    wind = $"tile {tile.tileId} ({vector.x:0.00}, {vector.y:0.00}, {vector.z:0.00})";
                }

                RPLog.D($"[API] v{version}: tilt {tilt:0.0}°, day-15 phase {phase:0.000}, sun-budget {sun}, plants {plants}, wind {wind}");
            }
            catch (Exception e)
            {
                RPLog.W("[API] Contract log failed: " + e.Message);
            }
        }

        static PlanetTile FirstSurfaceTile()
        {
            if (Current.Game == null)
                return PlanetTile.Invalid;
            PlanetLayer surface = Find.WorldGrid?.Surface;
            if (surface == null || surface.TilesCount <= 0)
                return PlanetTile.Invalid;
            return new PlanetTile(0, surface);
        }
    }
}
