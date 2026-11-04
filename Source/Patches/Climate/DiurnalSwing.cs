using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Climate;
using ClimateData = Planets.WorldGen.Climate.Climate;

namespace Planets.Patches.Climate
{
    /// <summary>
    /// Other mods can read this and skip their own day-night weather patch.
    /// Planetary Lighting's older postfix is removed by name for builds that do not check it.
    /// </summary>
    public static class DiurnalWeather
    {
        public static readonly bool OwnsDayNightWeather = true;
    }

    /// <summary>
    /// Clear-day half-range from the daily-range estimate, and the sun's shape for one call.
    /// The half-range is built once per world. The day's altitude span is cached per tile per day.
    /// </summary>
    public static class DiurnalSwing
    {
        const float EquatorEquinoxDelta = 90f;
        const float CollapsedDelta = 0.5f;

        static World cachedWorld;
        static float[] halfRange;
        static int[] tileDay;
        static float[] tileMaxDelta;

        public static void Invalidate()
        {
            cachedWorld = null;
            halfRange = null;
            tileDay = null;
            tileMaxDelta = null;
        }

        public static float Offset(int absTick, PlanetTile tile)
        {
            int id = tile.tileId;
            if (!TryHalfRange(id, out float half))
                return 0f;

            Vector2 coords = Find.WorldGrid.LongLatOf(tile);
            int laggedTick = absTick - 7500;
            SolarGeometry.GetSunAltitudeAzimuth(coords.x, coords.y, laggedTick, out float currentAlt, out float _, out bool _);

            float decRad = SolarGeometry.GetSolarDeclinationRad(absTick, coords.x);
            float latRad = coords.y * Mathf.Deg2Rad;
            float sinMean = Mathf.Sin(latRad) * Mathf.Sin(decRad);
            float meanAlt = Mathf.Asin(Mathf.Clamp(sinMean, -1f, 1f)) * Mathf.Rad2Deg;
            float delta = currentAlt - meanAlt;
            float maxDelta = DayMaxDelta(id, absTick, latRad, decRad, meanAlt);

            // A day that never rises has no swing. A short arc is a fraction of the equator equinox.
            if (meanAlt + maxDelta <= 0f || maxDelta < CollapsedDelta)
                return 0f;

            float shape = Mathf.Clamp(delta / maxDelta, -1f, 1f);
            float span = Mathf.Clamp01(maxDelta / EquatorEquinoxDelta);
            return shape * half * span;
        }

        public static float ApplyWeather(float swing, PlanetTile tile)
        {
            Map map = ColonyMap(tile);
            if (map == null)
                return swing;

            float cloud = AtmosphereCloud.TryGet(map, out float cover)
                ? cover
                : WeatherCloudiness(map.weatherManager);
            return swing * (1f - Mathf.Clamp01(cloud));
        }

        static bool TryHalfRange(int id, out float half)
        {
            half = 0f;
            World world = Find.World;
            if (world == null || !ClimateData.IsInitialized())
                return false;

            if (halfRange == null || cachedWorld != world || id >= halfRange.Length)
                Build(world);

            if (halfRange == null || id < 0 || id >= halfRange.Length)
                return false;

            half = halfRange[id];
            return true;
        }

        static void Build(World world)
        {
            cachedWorld = world;
            int count = Find.WorldGrid?.TilesCount ?? 0;
            halfRange = new float[count];
            tileDay = null;
            tileMaxDelta = null;

            for (int i = 0; i < count; i++)
            {
                float continentality = Mathf.Clamp01(ClimateData.Continentality(i));
                float latRad = ClimateData.LatitudeDeg(i) * Mathf.Deg2Rad;
                float aridity = DailyRange.ProvisionalAridity(
                    ClimateData.Rainfall(i), ClimateData.AnnualTemp(i), latRad, continentality);
                float full = DailyRange.Estimate(continentality, aridity);
                float half = full * 0.5f;
                if (ClimateData.ElevationKm(i) > 2f)
                    half *= 1.2f;
                halfRange[i] = half;
            }
        }

        static float DayMaxDelta(int id, int absTick, float latRad, float decRad, float meanAlt)
        {
            int day = absTick / GenDate.TicksPerDay;
            if (tileMaxDelta == null || tileDay == null || tileMaxDelta.Length != halfRange.Length)
            {
                tileMaxDelta = new float[halfRange.Length];
                tileDay = new int[halfRange.Length];
                for (int i = 0; i < tileDay.Length; i++)
                    tileDay[i] = int.MinValue;
            }

            if (tileDay[id] == day)
                return tileMaxDelta[id];

            float sinNoon = Mathf.Sin(latRad) * Mathf.Sin(decRad) + Mathf.Cos(latRad) * Mathf.Cos(decRad);
            float noonAlt = Mathf.Asin(Mathf.Clamp(sinNoon, -1f, 1f)) * Mathf.Rad2Deg;
            float maxDelta = Mathf.Max(0f, noonAlt - meanAlt);
            tileDay[id] = day;
            tileMaxDelta[id] = maxDelta;
            return maxDelta;
        }

        static Map ColonyMap(PlanetTile tile)
        {
            var maps = Find.Maps;
            if (maps == null)
                return null;

            int id = tile.tileId;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map != null && map.IsPlayerHome && map.Tile == id)
                    return map;
            }

            return null;
        }

        static float WeatherCloudiness(WeatherManager weather)
        {
            // Gravship landing asks for outdoor temperature while choosing the first weather.
            // RainRate and SnowRate read lastWeather and curWeather, which are still null then.
            if (weather?.curWeather == null || weather.lastWeather == null)
                return 0f;

            float precip = Mathf.Max(Mathf.Clamp01(weather.RainRate), Mathf.Clamp01(weather.SnowRate));
            float lerp = weather.TransitionLerpFactor;
            float fog = Mathf.Lerp(FogAmount(weather.lastWeather), FogAmount(weather.curWeather), lerp);
            float glow = 1f;
            if (weather.lastWeather != null && weather.curWeather != null)
                glow = Mathf.Lerp(weather.lastWeather.maxGlow, weather.curWeather.maxGlow, lerp);
            else if (weather.curWeather != null)
                glow = weather.curWeather.maxGlow;
            float sky = Mathf.Clamp01(1f - glow);
            return Mathf.Clamp01(Mathf.Max(precip, fog, sky));
        }

        static float FogAmount(WeatherDef def)
        {
            if (def?.overlayClasses == null)
                return 0f;

            for (int i = 0; i < def.overlayClasses.Count; i++)
            {
                System.Type overlay = def.overlayClasses[i];
                if (overlay != null && overlay.Name.Contains("Fog"))
                    return 1f;
            }

            return 0f;
        }

        static class AtmosphereCloud
        {
            static bool resolved;
            static bool available;
            static MethodInfo ofMap;
            static PropertyInfo state;
            static FieldInfo cloudCover;

            public static bool TryGet(Map map, out float cover)
            {
                cover = 0f;
                if (!resolved)
                    Resolve();
                if (!available || map == null)
                    return false;

                try
                {
                    object component = ofMap.Invoke(null, new object[] { map });
                    if (component == null)
                        return false;
                    object visual = state.GetValue(component, null);
                    if (visual == null)
                        return false;
                    cover = Mathf.Clamp01((float)cloudCover.GetValue(visual));
                    return true;
                }
                catch
                {
                    available = false;
                    return false;
                }
            }

            static void Resolve()
            {
                resolved = true;
                System.Type componentType = AccessTools.TypeByName("DynamicAtmosphere.MapComponent_Atmosphere");
                ofMap = componentType == null ? null : AccessTools.Method(componentType, "Of");
                state = componentType == null ? null : AccessTools.Property(componentType, "State");
                System.Type stateType = state?.PropertyType;
                cloudCover = stateType == null ? null : AccessTools.Field(stateType, "cloudCover");
                available = ofMap != null && state != null && cloudCover != null && cloudCover.FieldType == typeof(float);
            }
        }
    }
}
