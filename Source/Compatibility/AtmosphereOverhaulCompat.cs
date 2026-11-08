using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.API;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Fallback for the Atmosphere Overhaul 1.6 build that has no climate-anchor API.
    /// RP2 keeps outdoor temperature, seasons, vanilla weather, and sea-ice biomes.
    /// The map-edge wind and temperature come from RP2. The forecast still runs, and its
    /// relaxation target uses the surface temperature AO already copied from the tiles.
    /// Remove these patches when AutodriveSession exposes a public anchor and a flag
    /// that another mod owns the climate mean.
    /// </summary>
    [StaticConstructorOnStartup]
    public class AtmosphereOverhaulCompat : ICompatibilityModule
    {
        const double Kappa = 0.28590637445449829;
        const double ReferencePressure = 100000.0;
        const double ModelTopPressure = 5000.0;
        const double StratificationKelvin = 30.0;
        const double TemperatureFloorKelvin = 200.0;
        const int SurfaceLayer = 7;

        /// <summary>Hybrid B interfaces from GlobalAtmosphereV2Constants.InitialInterfaceB on this build.</summary>
        static readonly double[] InterfaceB =
        {
            0.0,
            0.105263159,
            0.210526317,
            0.347368419,
            0.49473685,
            0.6421053,
            0.7894737,
            0.9157895,
            1.0
        };

        static AtmosphereOverhaulCompat _instance;
        public static AtmosphereOverhaulCompat Instance => _instance ?? (_instance = new AtmosphereOverhaulCompat());

        static Func<object, float[]> readSkin;
        static bool anchorFailed;

        static AtmosphereOverhaulCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        public string ModuleID => "acutus.AtmosphereOverhaul.release";
        public string ModName => "Atmosphere Overhaul";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(System.Collections.Generic.List<FactionDef> factions) { }

        public void Initialize()
        {
            IsActive = ModsConfig.IsActive(ModuleID);
            if (!IsActive)
                return;

            if (TryUsePublicFlag())
            {
                PatchMapEdge();
                RPLog.Message("[Compat] Atmosphere Overhaul: using its climate-mean flag. RP2 keeps temperature, weather, and sea ice. Map air still runs.");
                return;
            }

            PatchAnchor();
            ReleaseClimate();
            RPLog.Message("[Compat] Atmosphere Overhaul: RP2 keeps temperature, weather, and sea ice. The forecast follows RP2's surface temperature, and map air still runs.");
        }

        static bool TryUsePublicFlag()
        {
            Type session = AccessTools.TypeByName("AtmosphereOverhaul.Simulation.Global.V2.AutodriveSession");
            PropertyInfo flag = session == null ? null : AccessTools.Property(session, "ExternalClimateOwnsMean");
            if (flag == null || !flag.CanWrite || flag.PropertyType != typeof(bool))
                return false;
            flag.SetValue(null, true, null);
            return true;
        }

        void PatchAnchor()
        {
            Type core = AccessTools.TypeByName("AtmosphereOverhaul.Simulation.Global.V2.GlobalAtmosphereV2DryCore");
            MethodInfo target = core == null ? null : AccessTools.Method(core, "AutodriveTargetPotentialTemperature");
            FieldInfo skin = core == null ? null : AccessTools.Field(core, "surfaceBaseK");
            if (target == null || skin == null)
            {
                Log.Warning("[RP] Atmosphere Overhaul's forecast anchor was not found. Its latitude curve stays in use.");
                return;
            }

            readSkin = SkinReader(core, skin);
            Controller.harmony.Patch(target, prefix: new HarmonyMethod(typeof(AtmosphereOverhaulCompat), nameof(Anchor)));
        }

        static void ReleaseClimate()
        {
            Unpatch("AtmosphereOverhaul.Patches.TileTemperaturesComp_GetOutdoorTemp_Patch", "Postfix",
                AccessTools.Method(typeof(TileTemperaturesComp), "GetOutdoorTemp", new[] { typeof(PlanetTile) }));
            Unpatch("AtmosphereOverhaul.Patches.WeatherDecider_GlobalWeatherPatch", "Prefix",
                AccessTools.Method(typeof(WeatherDecider), "WeatherDeciderTick"));
            Unpatch("AtmosphereOverhaul.Patches.IncidentWorker_HeatWave_CanFireNowSub_Patch", "Postfix",
                AccessTools.Method(typeof(IncidentWorker_HeatWave), "CanFireNowSub"));
            Unpatch("AtmosphereOverhaul.Patches.IncidentWorker_ColdSnap_CanFireNowSub_Patch", "Postfix",
                AccessTools.Method(typeof(IncidentWorker_ColdSnap), "CanFireNowSub"));
            Unpatch("AtmosphereOverhaul.RimWorld.Global.IncidentWorker_CanFireNow_GlobalGasPatch", "Postfix",
                AccessTools.Method(typeof(IncidentWorker), "CanFireNow", new[] { typeof(IncidentParms) }));

            Type world = AccessTools.TypeByName("AtmosphereOverhaul.RimWorld.Global.GlobalAtmosphereWorldComponent");
            MethodInfo seaIce = world == null ? null : AccessTools.Method(world, "SyncSeaIceBiomes");
            if (seaIce == null)
                Log.Warning("[RP] Atmosphere Overhaul sea-ice update was not found. It may still change ocean biomes.");
            else
                Controller.harmony.Patch(seaIce, prefix: new HarmonyMethod(typeof(AtmosphereOverhaulCompat), nameof(Skip)));

            Type gas = AccessTools.TypeByName("AtmosphereOverhaul.RimWorld.Global.GlobalGasMapCoupling");
            MethodInfo gasTick = gas == null ? null : AccessTools.Method(gas, "Tick");
            if (gasTick == null)
                Log.Warning("[RP] Atmosphere Overhaul gas weather was not found. Forecast dust and ash may still reach maps.");
            else
                Controller.harmony.Patch(gasTick, prefix: new HarmonyMethod(typeof(AtmosphereOverhaulCompat), nameof(Skip)));

            PatchMapEdge();
        }

        static void PatchMapEdge()
        {
            Type map = AccessTools.TypeByName("AtmosphereOverhaul.RimWorld.AtmosphereMapComponent");
            MethodInfo wind = map == null ? null : AccessTools.Method(map, "GetAmbientWind");
            MethodInfo temperature = map == null ? null : AccessTools.Method(map, "GetOutdoorTemperatureSafe", new[] { typeof(float) });
            if (wind == null || temperature == null)
                Log.Warning("[RP] Atmosphere Overhaul map-edge air was not found. The map may still take the forecast wind.");
            else
            {
                Controller.harmony.Patch(wind, postfix: new HarmonyMethod(typeof(AtmosphereOverhaulCompat), nameof(AmbientWind)));
                Controller.harmony.Patch(temperature, postfix: new HarmonyMethod(typeof(AtmosphereOverhaulCompat), nameof(AmbientTemperature)));
            }
        }

        /// <summary>Skips a forecast write that would replace RP2's climate.</summary>
        public static bool Skip() => false;

        public static void AmbientWind(Map ___map, ref Vector2 __result)
        {
            if (___map == null || !___map.Tile.Valid || Find.TickManager == null)
                return;
            if (___map.Biome != null && (___map.Biome.inVacuum || ___map.Biome.noAmbientWind))
                return;

            Vector3 wind = RealisticPlanetsAPI.WindMsAt(___map.Tile, RealisticPlanetsAPI.YearPhase(Find.TickManager.TicksAbs));
            if (wind.sqrMagnitude < 1e-4f)
                return;

            Vector2 longLat = Find.WorldGrid.LongLatOf(___map.Tile);
            EastNorth(longLat.x, longLat.y, wind, out float east, out float north);
            __result = Vector2.ClampMagnitude(new Vector2(east, north), 120f);
        }

        public static void AmbientTemperature(Map ___map, ref float __result)
        {
            if (___map?.mapTemperature == null || ___map.Biome != null && ___map.Biome.inVacuum)
                return;
            try
            {
                __result = ___map.mapTemperature.OutdoorTemp;
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            catch (NullReferenceException)
            {
            }
        }

        static void EastNorth(float lonDeg, float latDeg, Vector3 wind, out float eastward, out float northward)
        {
            float lat = latDeg * Mathf.Deg2Rad;
            float lon = lonDeg * Mathf.Deg2Rad;
            float cosLat = Mathf.Cos(lat);
            Vector3 radial = new Vector3(Mathf.Sin(lon) * cosLat, Mathf.Sin(lat), -Mathf.Cos(lon) * cosLat);
            Vector3 east = Vector3.Cross(radial, Vector3.up);
            if (east.sqrMagnitude < 1e-6f)
                east = Vector3.right;
            east.Normalize();
            Vector3 north = Vector3.Cross(east, radial).normalized;
            eastward = Vector3.Dot(wind, east);
            northward = Vector3.Dot(wind, north);
        }

        static void Unpatch(string typeName, string patchName, MethodInfo original)
        {
            Type patchType = AccessTools.TypeByName(typeName);
            MethodInfo patch = patchType == null ? null : AccessTools.Method(patchType, patchName);
            if (patch == null || original == null)
            {
                Log.Warning($"[RP] Atmosphere Overhaul patch {typeName}.{patchName} was not found. That part of the other mod is still running.");
                return;
            }

            Controller.harmony.Unpatch(original, patch);
        }

        public void RenderIntegrationUI(Listing_Standard list)
        {
            list.Gap(6);
            list.Label("Atmosphere Overhaul");
            list.Label("RP2 keeps temperature, seasons, weather, and sea ice. Atmosphere Overhaul still simulates the air on each map. Its planet forecast is drawn toward RP2's surface temperature.");
            list.Gap(6);
        }

        public void ExposeData() { }

        public void ResetToDefaults() { }

        /// <summary>
        /// Replaces the latitude anchor. Lowest-layer temperature matches the supplied skin.
        /// Potential temperature increases upward by the same 30 K log-pressure term AO uses.
        /// </summary>
        public static bool Anchor(object __instance, int layer, int column, double surfacePressurePascals, ref double __result)
        {
            if (anchorFailed || readSkin == null || layer < 0 || layer > SurfaceLayer || surfacePressurePascals <= ModelTopPressure)
                return true;

            float[] skin;
            try
            {
                skin = readSkin(__instance);
            }
            catch (Exception e)
            {
                anchorFailed = true;
                Log.Warning("[RP] Atmosphere Overhaul surface temperature could not be read. Its latitude curve is back in use: " + e.Message);
                return true;
            }

            if (skin == null || (uint)column >= (uint)skin.Length)
                return true;

            double surface = skin[column];
            if (surface < 180.0 || surface > 340.0)
                return true;

            double reference = LayerCenterPressure(SurfaceLayer, surfacePressurePascals);
            double pressure = LayerCenterPressure(layer, surfacePressurePascals);
            if (reference <= ModelTopPressure || pressure <= ModelTopPressure)
                return true;

            double exner = Math.Pow(pressure / ReferencePressure, Kappa);
            double theta = surface / Math.Pow(reference / ReferencePressure, Kappa) + StratificationKelvin * Math.Log(reference / pressure);
            __result = Math.Max(theta, TemperatureFloorKelvin / exner);
            return false;
        }

        /// <summary>Reads a private field. The dynamic method is owned by their type so the runtime allows it.</summary>
        static Func<object, float[]> SkinReader(Type core, FieldInfo skin)
        {
            DynamicMethod method = new DynamicMethod("RP2_AO_ReadSurfaceBaseK", typeof(float[]), new[] { typeof(object) }, core, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, core);
            il.Emit(OpCodes.Ldfld, skin);
            il.Emit(OpCodes.Ret);
            return (Func<object, float[]>)method.CreateDelegate(typeof(Func<object, float[]>));
        }

        static double LayerCenterPressure(int layer, double surfacePressure)
        {
            return 0.5 * (InterfacePressure(layer, surfacePressure) + InterfacePressure(layer + 1, surfacePressure));
        }

        static double InterfacePressure(int index, double surfacePressure)
        {
            double b = InterfaceB[index];
            return ModelTopPressure * (1.0 - b) + b * surfacePressure;
        }
    }
}
