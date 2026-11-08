using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.API;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.Utilities.WorldGen;
using Planets.WorldGen.Climate;

namespace Planets.Compatibility
{
    /// <summary>
    /// Dynamic Atmosphere keeps its moving wiggle, hurricane tracks, and drawing.
    /// The jet field and the colony drift use RP2 wind at RP2's solstice.
    /// RP2 writes tilt after Dynamic Planets, so the later value is RP2's.
    /// </summary>
    [StaticConstructorOnStartup]
    public class DynamicAtmosphereCompat : ICompatibilityModule
    {
        const int GridW = 128;
        const int GridH = 64;

        static DynamicAtmosphereCompat _instance;
        public static DynamicAtmosphereCompat Instance => _instance ?? (_instance = new DynamicAtmosphereCompat());

        static MethodInfo zonalU;
        static bool windFailed;
        static World indexedWorld;
        static SpatialTileIndex index;
        static int[] cellTile;

        static DynamicAtmosphereCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        public string ModuleID => "astryl.DynamicAtmosphere";
        public string ModName => "Dynamic Atmosphere";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(System.Collections.Generic.List<FactionDef> factions) { }

        public void Initialize()
        {
            IsActive = ModsConfig.IsActive(ModuleID);
            if (!IsActive)
                return;

            Type model = AccessTools.TypeByName("DynamicAtmosphere.DA_CirculationModel");
            Type climate = AccessTools.TypeByName("DynamicAtmosphere.DA_Climate");
            zonalU = model == null ? null : AccessTools.Method(model, "ZonalU");
            MethodInfo fill = model == null ? null : AccessTools.Method(model, "FillGrid");
            MethodInfo wind = model == null ? null : AccessTools.Method(model, "Wind");
            MethodInfo declination = climate == null ? null : AccessTools.Method(climate, "DeclinationAt");

            if (fill == null || wind == null || declination == null || zonalU == null)
            {
                Log.Warning("[RP] Dynamic Atmosphere wind methods were not found. Its own jets stay in use.");
                return;
            }

            Controller.harmony.Patch(fill, postfix: new HarmonyMethod(typeof(DynamicAtmosphereCompat), nameof(FillGrid)));
            Controller.harmony.Patch(wind, postfix: new HarmonyMethod(typeof(DynamicAtmosphereCompat), nameof(Wind)));
            Controller.harmony.Patch(declination, postfix: new HarmonyMethod(typeof(DynamicAtmosphereCompat), nameof(Declination)));
            RPLog.Message("[Compat] Dynamic Atmosphere: jets and colony drift use RP2 wind. Its wiggle and storms stay.");
        }

        public void RenderIntegrationUI(Listing_Standard list)
        {
            list.Gap(6);
            list.Label("Dynamic Atmosphere");
            list.Label("Jet streams and colony cloud drift follow RP2's summer and winter wind. Dynamic Atmosphere still draws the clouds and steers its storms.");
            list.Gap(6);
        }

        public void ExposeData() { }

        public void ResetToDefaults() { }

        /// <summary>Called after Dynamic Planets pushes, and again from RP2's own world init.</summary>
        public static void PushTilt()
        {
            try
            {
                Type api = AccessTools.TypeByName("DynamicAtmosphere.Api");
                MethodInfo set = api == null ? null : AccessTools.Method(api, "SetAxialTilt");
                if (set == null)
                    return;
                set.Invoke(null, new object[] { RealisticPlanetsAPI.TiltDegrees, "Realistic Planets" });
            }
            catch (Exception e)
            {
                Log.Warning("[RP] Could not give Dynamic Atmosphere RP2's tilt: " + e.Message);
            }
        }

        public static void Declination(long ticksAbs, ref float __result)
        {
            __result = Mathf.Sin(2f * Mathf.PI * RealisticPlanetsAPI.YearPhase(ticksAbs));
        }

        public static void Wind(object __instance, float lonDeg, float latDeg, float timeSec, float decl, ref float u, ref float v)
        {
            if (windFailed || !Climate.IsInitialized())
                return;

            try
            {
                int id = NearestTile(lonDeg, latDeg);
                Apply(u, v, id, lonDeg, latDeg, Phase(timeSec), Zonal(__instance, latDeg, decl), out u, out v);
            }
            catch (Exception e)
            {
                windFailed = true;
                Log.Warning("[RP] Dynamic Atmosphere wind hook stopped: " + e.Message);
            }
        }

        public static void FillGrid(object __instance, int gw, int gh, float timeSec, float decl, float[] outU, float[] outV)
        {
            if (windFailed || !Climate.IsInitialized() || outU == null || outV == null)
                return;
            if (gw != GridW || gh != GridH || outU.Length < gw * gh)
                return;

            try
            {
                float phase = Phase(timeSec);
                EnsureTiles();
                for (int row = 0; row < gh; row++)
                {
                    float lat = ((row + 0.5f) / gh) * 180f - 90f;
                    float zonal = Zonal(__instance, lat, decl);
                    for (int col = 0; col < gw; col++)
                    {
                        int cell = row * gw + col;
                        float lon = ((col + 0.5f) / gw) * 360f - 180f;
                        Apply(outU[cell], outV[cell], cellTile[cell], lon, lat, phase, zonal, out outU[cell], out outV[cell]);
                    }
                }
            }
            catch (Exception e)
            {
                windFailed = true;
                Log.Warning("[RP] Dynamic Atmosphere wind grid hook stopped: " + e.Message);
            }
        }

        static void Apply(float theirU, float theirV, int tileId, float lonDeg, float latDeg, float phase, float zonal, out float u, out float v)
        {
            PlanetLayer surface = Find.WorldGrid?.Surface;
            if (tileId < 0 || surface == null || !HasWind)
            {
                u = theirU;
                v = theirV;
                return;
            }

            Vector3 wind = RealisticPlanetsAPI.WindAt(new PlanetTile(tileId, surface), phase);
            Tangent(lonDeg, latDeg, wind, out float east, out float north);
            u = east + (theirU - zonal);
            v = north + theirV;
        }

        static bool HasWind
        {
            get
            {
                ClimateData climate = Find.World?.GetComponent<ClimateData>();
                return climate?.SummerWind != null && climate.SummerWind.Length > 0;
            }
        }

        static float Zonal(object model, float latDeg, float decl)
        {
            return (float)zonalU.Invoke(model, new object[] { latDeg, decl });
        }

        static float Phase(float timeSec)
        {
            long ticksGame = (long)(timeSec * 60f);
            long start = Current.Game != null ? Find.TickManager.gameStartAbsTick : 0L;
            return RealisticPlanetsAPI.YearPhase(ticksGame + start);
        }

        static void EnsureTiles()
        {
            World world = Find.World;
            PlanetLayer surface = world?.grid?.Surface;
            if (world == null || surface == null)
                return;
            if (cellTile != null && indexedWorld == world)
                return;

            indexedWorld = world;
            cellTile = new int[GridW * GridH];
            index = new SpatialTileIndex(500f, id => surface.GetTileCenter(id));
            int count = surface.TilesCount;
            int[] ids = new int[count];
            for (int i = 0; i < count; i++)
                ids[i] = i;
            index.BuildIndex(ids);

            float radius = surface.Radius;
            for (int row = 0; row < GridH; row++)
            {
                float latDeg = ((row + 0.5f) / GridH) * 180f - 90f;
                float lat = latDeg * Mathf.Deg2Rad;
                float cosLat = Mathf.Cos(lat);
                float y = Mathf.Sin(lat);
                for (int col = 0; col < GridW; col++)
                {
                    float lonDeg = ((col + 0.5f) / GridW) * 360f - 180f;
                    float lon = lonDeg * Mathf.Deg2Rad;
                    Vector3 pos = new Vector3(Mathf.Sin(lon) * cosLat, y, -Mathf.Cos(lon) * cosLat) * radius;
                    cellTile[row * GridW + col] = index.GetClosestTile(pos);
                }
            }
        }

        static int NearestTile(float lonDeg, float latDeg)
        {
            EnsureTiles();
            if (cellTile == null)
                return -1;

            int col = Mathf.Clamp(Mathf.FloorToInt((lonDeg + 180f) / 360f * GridW), 0, GridW - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((latDeg + 90f) / 180f * GridH), 0, GridH - 1);
            return cellTile[row * GridW + col];
        }

        static void Tangent(float lonDeg, float latDeg, Vector3 wind, out float eastward, out float northward)
        {
            float lat = latDeg * Mathf.Deg2Rad;
            float lon = lonDeg * Mathf.Deg2Rad;
            float cosLat = Mathf.Cos(lat);
            Vector3 n = new Vector3(Mathf.Sin(lon) * cosLat, Mathf.Sin(lat), -Mathf.Cos(lon) * cosLat);
            Vector3 east = Vector3.Cross(n, Vector3.up);
            if (east.sqrMagnitude < 1e-6f)
                east = Vector3.right;
            east.Normalize();
            Vector3 north = Vector3.Cross(east, n).normalized;
            eastward = Vector3.Dot(wind, east);
            northward = Vector3.Dot(wind, north);
        }
    }
}
