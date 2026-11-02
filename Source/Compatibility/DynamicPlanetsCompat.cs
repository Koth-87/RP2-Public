using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Api;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Dynamic Planets keeps its rings, moons, orbital view, and sun.
    /// RP2 removes the season multiplier and the worldgen temperature remap, and leans the rings to RP2's tilt.
    /// Today's build does not read the API, so the patches are removed by name.
    /// </summary>
    [StaticConstructorOnStartup]
    public class DynamicPlanetsCompat : ICompatibilityModule
    {
        static DynamicPlanetsCompat _instance;
        public static DynamicPlanetsCompat Instance => _instance ?? (_instance = new DynamicPlanetsCompat());

        static DynamicPlanetsCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        public string ModuleID => "astryl.DynamicPlanets";
        public string ModName => "Dynamic Planets";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(System.Collections.Generic.List<FactionDef> factions) { }

        public void Initialize()
        {
            IsActive = ModsConfig.IsActive(ModuleID);
            if (!IsActive)
                return;

            MethodInfo seasons = AccessTools.Method(typeof(GenTemperature), "SeasonalShiftAmplitudeAt");
            Unpatch("DynamicPlanets.Tilt.DP_TiltPatches+Patch_SeasonalShiftAmplitudeAt", "Postfix", seasons);

            MethodInfo generateTile = AccessTools.DeclaredMethod(typeof(WorldGenStep_Terrain), "GenerateTileFor");
            Unpatch("DynamicPlanets.Tilt.DP_TiltPatches+Patch_GenerateTileFor", "Transpiler", generateTile);

            MethodInfo plane = AccessTools.PropertyGetter(AccessTools.TypeByName("DynamicPlanets.Core.RingProfile"), "PlaneRotation");
            if (plane == null)
                Log.Warning("[RP] Dynamic Planets ring plane was not found. Rings will not follow RP2's tilt.");
            else
                Controller.harmony.Patch(plane, postfix: new HarmonyMethod(typeof(DynamicPlanetsCompat), nameof(RingPlane)));

            MethodInfo ready = AccessTools.Method(AccessTools.TypeByName("DynamicPlanets.Layers.WorldComponent_DP_Planet"), "FinalizeInit");
            if (ready == null)
                Log.Warning("[RP] Dynamic Planets world init was not found. Its tilt may overwrite RP2's.");
            else
                Controller.harmony.Patch(ready, postfix: new HarmonyMethod(typeof(DynamicAtmosphereCompat), nameof(DynamicAtmosphereCompat.PushTilt)));

            RPLog.Message("[Compat] Dynamic Planets: RP2 owns seasons and the yearly temperature. Rings use RP2's tilt. Its sun stays.");
        }

        public void RenderIntegrationUI(Listing_Standard list)
        {
            list.Gap(6);
            list.Label("Dynamic Planets");
            list.Label("RP2 supplies the seasonal range and the yearly temperature. Rings lean with RP2's tilt. Dynamic Planets still draws the rings, moons, orbital view, and sun.");
            list.Gap(6);
        }

        public void ExposeData() { }

        public void ResetToDefaults() { }

        /// <summary>Ring axis leans by the climate tilt. North stays +Y rotated about world +X.</summary>
        public static void RingPlane(ref Quaternion __result)
        {
            __result = Quaternion.AngleAxis(RealisticPlanetsApi.TiltDegrees, Vector3.right);
        }

        static void Unpatch(string typeName, string patchName, MethodInfo original)
        {
            System.Type patchType = AccessTools.TypeByName(typeName);
            MethodInfo patch = patchType == null ? null : AccessTools.Method(patchType, patchName);
            if (patch == null || original == null)
            {
                Log.Warning($"[RP] Dynamic Planets patch {typeName}.{patchName} was not found. That part of the other mod is still running.");
                return;
            }

            Controller.harmony.Unpatch(original, patch);
        }
    }
}
