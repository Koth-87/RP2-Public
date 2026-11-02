using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Fallback for Planetary Lighting builds that still postfix OffsetFromSunCycle.
    /// RP2 owns the day-night weather shrink. Their sky and shadows stay.
    /// </summary>
    [StaticConstructorOnStartup]
    public class PlanetaryLightingWeatherCompat : ICompatibilityModule
    {
        private static PlanetaryLightingWeatherCompat _instance;
        public static PlanetaryLightingWeatherCompat Instance => _instance ?? (_instance = new PlanetaryLightingWeatherCompat());

        static PlanetaryLightingWeatherCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        public string ModuleID => "koth.PlanetaryLighting";
        public string ModName => "Planetary Lighting";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(System.Collections.Generic.List<FactionDef> factions) { }

        public void Initialize()
        {
            IsActive = ModsConfig.IsActive(ModuleID);
            if (!IsActive)
                return;

            MethodInfo original = AccessTools.Method(typeof(GenTemperature), "OffsetFromSunCycle");
            System.Type patchType = AccessTools.TypeByName("Planets.Patches.Patch_WeatherTempEffects");
            MethodInfo postfix = patchType == null ? null : AccessTools.Method(patchType, "Postfix");
            if (postfix == null || original == null)
                return;

            Controller.harmony.Unpatch(original, postfix);
            RPLog.Message("[Compat] Planetary Lighting: RP2 owns the day-night weather swing. Lighting still draws the sky.");
        }

        public void RenderIntegrationUI(Listing_Standard list) { }

        public void ExposeData() { }

        public void ResetToDefaults() { }
    }
}
