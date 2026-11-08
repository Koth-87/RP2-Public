using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Planets.API;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Fallback for Celestial Lighting builds that resolve tilt through Planets.WorldGen.AxialTiltCurves.
    /// That type now lives in Planets.WorldGen.Astronomy, so their bind fails and the sky stays on their own tilt.
    /// Their read is answered with <see cref="RealisticPlanetsAPI.TiltDegrees"/>. Their sky stays theirs.
    /// </summary>
    [StaticConstructorOnStartup]
    public class CelestialLightingCompat : ICompatibilityModule
    {
        private static CelestialLightingCompat _instance;
        public static CelestialLightingCompat Instance => _instance ?? (_instance = new CelestialLightingCompat());

        private static bool geometryResolved;
        private static FieldInfo geometryFlag;

        static CelestialLightingCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        public string ModuleID => "joof.celestiallighting";
        public string ModName => "Celestial Lighting";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(System.Collections.Generic.List<FactionDef> factions) { }

        public void Initialize()
        {
            IsActive = ModsConfig.IsActive(ModuleID);
            if (!IsActive)
                return;

            System.Type reader = AccessTools.TypeByName("CelestialLighting.RealisticPlanetsCompat");
            MethodInfo read = reader == null ? null : AccessTools.Method(reader, "TryReadTilt");
            if (read == null)
            {
                RPLog.W("[Compat] Celestial Lighting's tilt read was not found. Its sky will stay on its own tilt.");
                return;
            }

            Controller.harmony.Patch(read, prefix: new HarmonyMethod(typeof(CelestialLightingCompat), nameof(ReadTilt)));
            RPLog.Message("[Compat] Celestial Lighting: its sky uses RP2's tilt. The sky itself stays Celestial Lighting's.");
        }

        public void RenderIntegrationUI(Listing_Standard list)
        {
            list.Gap(6);
            list.Label("Celestial Lighting");
            list.Label("Its sky uses RP2's axial tilt. Celestial Lighting still draws the sun, shadows, and moon.");
            list.Gap(6);
        }

        public void ExposeData() { }

        public void ResetToDefaults() { }

        /// <summary>
        /// Skip their type bind. A game in progress gets RP2's tilt; their own declination math then runs on that number.
        /// No game, or their geometry flag off, leaves the read as "no tilt", which is the answer they already treat as their own sky.
        /// </summary>
        public static bool ReadTilt(ref float tilt, ref bool __result)
        {
            tilt = 0f;
            __result = false;
            if (Current.Game == null || !WantsRp2Geometry())
                return false;

            tilt = RealisticPlanetsAPI.TiltDegrees;
            __result = true;
            return false;
        }

        private static bool WantsRp2Geometry()
        {
            if (!geometryResolved)
            {
                geometryResolved = true;
                System.Type features = AccessTools.TypeByName("CelestialLighting.CelestialLightingFeatures");
                geometryFlag = features == null ? null : AccessTools.Field(features, "RealisticPlanetsGeometry");
            }

            if (geometryFlag == null || geometryFlag.FieldType != typeof(bool))
                return true;
            return (bool)geometryFlag.GetValue(null);
        }
    }
}
