using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.WorldGen.Astronomy;

namespace Planets.Patches.Gameplay
{
    /// <summary>
    /// Outdoor plants follow the sun that is actually in the sky.
    /// A lighting mod that patches GenCelestial supplies that sun. Otherwise RP2's own geometry does.
    /// </summary>
    public static class PlantSun
    {
        private const float DeepNightHours = 4f;

        private static bool resolved;
        private static bool useLiveSun;
        private static Func<float, int, float, Vector3> liveSunPosition;
        private static Func<float, Vector3> surfaceNormal;

        public static bool IsSunDown(Plant plant)
        {
            return Elevation(plant) < 0f;
        }

        /// <summary>True during the darkest four hours, and only while the sun is below the horizon.</summary>
        public static bool IsDeepNight(Plant plant)
        {
            if (Elevation(plant) >= 0f)
                return false;

            float dayPercent = GenLocalDate.DayPercent(plant);
            float fromMidnight = Mathf.Min(dayPercent, 1f - dayPercent);
            return fromMidnight < (DeepNightHours / 24f) * 0.5f;
        }

        private static float Elevation(Plant plant)
        {
            Vector2 longLat = Find.WorldGrid.LongLatOf(plant.Map.Tile);
            int ticks = Find.TickManager.TicksAbs;
            EnsureLiveSun();
            if (useLiveSun)
            {
                int day = GenDate.DayOfYear(ticks, longLat.x);
                float dayPercent = GenDate.DayPercent(ticks, longLat.x);
                Vector3 sun = liveSunPosition(longLat.y, day, dayPercent);
                Vector3 up = surfaceNormal(longLat.y);
                float sinAlt = Vector3.Dot(sun.normalized, up.normalized);
                return Mathf.Asin(Mathf.Clamp(sinAlt, -1f, 1f)) * Mathf.Rad2Deg;
            }

            SolarGeometry.GetSunAltitudeAzimuth(longLat.x, longLat.y, ticks, out float altitudeDeg, out _, out _);
            return altitudeDeg;
        }

        private static void EnsureLiveSun()
        {
            if (resolved)
                return;
            resolved = true;

            MethodInfo sunPosition = AccessTools.Method(typeof(GenCelestial), "SunPosition", new[] { typeof(float), typeof(int), typeof(float) });
            MethodInfo sunUnmodified = AccessTools.Method(typeof(GenCelestial), "SunPositionUnmodified");
            MethodInfo normal = AccessTools.Method(typeof(GenCelestial), "SurfaceNormal");
            if (sunPosition == null || normal == null || (!Patched(sunPosition) && !Patched(sunUnmodified)))
                return;

            liveSunPosition = AccessTools.MethodDelegate<Func<float, int, float, Vector3>>(sunPosition);
            surfaceNormal = AccessTools.MethodDelegate<Func<float, Vector3>>(normal);
            useLiveSun = liveSunPosition != null && surfaceNormal != null;
        }

        private static bool Patched(MethodInfo method)
        {
            if (method == null)
                return false;
            HarmonyLib.Patches info = Harmony.GetPatchInfo(method);
            return info != null && (info.Prefixes.Count > 0 || info.Postfixes.Count > 0);
        }
    }

    [HarmonyPatch(typeof(Plant), "Resting", MethodType.Getter)]
    public static class Patch_PlantRest
    {
        public static bool Prefix(Plant __instance, ref bool __result)
        {
            if (Controller.Settings?.featureConfig?.enableShortPlantRest?.CurrentValue != true)
                return true;
            if (__instance?.def?.plant == null || __instance.def.plant.growMinGlow <= 0f)
                return true;
            if (!__instance.Spawned || __instance.Map == null)
                return true;

            __result = PlantSun.IsDeepNight(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.TickLong))]
    public static class Patch_PlantWinterDark
    {
        private const int DarkDaysBeforeDormancy = 3;
        private static readonly AccessTools.FieldRef<Plant, int> UnlitTicks =
            AccessTools.FieldRefAccess<Plant, int>("unlitTicks");

        public static void Postfix(Plant __instance)
        {
            if (Controller.Settings?.featureConfig?.enablePlantDormancy?.CurrentValue != true)
                return;
            Plant plant = __instance;
            if (plant?.def?.plant == null || !plant.Spawned || plant.Map == null)
                return;

            PlantProperties props = plant.def.plant;
            if (props.dieIfLeafless || !props.dieIfNoSunlight || props.growMinGlow <= 0f)
                return;
            if (plant.Position.Roofed(plant.Map))
                return;
            if (!PlantSun.IsSunDown(plant))
                return;

            if (UnlitTicks(plant) >= DarkDaysBeforeDormancy * GenDate.TicksPerDay || plant.LeaflessNow)
            {
                plant.MakeLeafless(Plant.LeaflessCause.Cold, false);
                UnlitTicks(plant) = 0;
            }
        }
    }
}
