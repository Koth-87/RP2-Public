using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Fallback for Realistic Axial Tilt builds that do not read an RP2 flag yet.
    /// Removes their yearly-temperature and plant patches by name. See ICompatibilityModule.
    /// Their sky, shadows, and moon stay. A public flag they can check comes with the geometry API.
    /// </summary>
    [StaticConstructorOnStartup]
    public class RealisticAxialTiltCompat : ICompatibilityModule
    {
        private static RealisticAxialTiltCompat _instance;
        public static RealisticAxialTiltCompat Instance => _instance ?? (_instance = new RealisticAxialTiltCompat());

        static RealisticAxialTiltCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        public string ModuleID => "dsweber.RealisticAxialTilt";
        public string ModName => "Realistic Axial Tilt";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(System.Collections.Generic.List<FactionDef> factions) { }

        public void Initialize()
        {
            IsActive = ModsConfig.IsActive(ModuleID);
            if (!IsActive)
                return;

            Unpatch("RealisticAxialTilt.Patches.RP2AnnualTempCorrectionPatch", "Postfix",
                AccessTools.Method(typeof(WorldGenStep_Terrain), "GenerateFresh"));
            Unpatch("RealisticAxialTilt.Patches.PlantRestingPatch", "Prefix",
                AccessTools.PropertyGetter(typeof(Plant), "Resting"));
            MethodInfo tickLong = AccessTools.Method(typeof(Plant), nameof(Plant.TickLong));
            Unpatch("RealisticAxialTilt.Patches.PlantWinterDormancyPatch", "Prefix", tickLong);
            Unpatch("RealisticAxialTilt.Patches.PlantLightDormancyPatch", "Postfix", tickLong);

            RPLog.Message("[Compat] Realistic Axial Tilt: RP2 owns yearly temperature and plant seasons. Its sky is unchanged.");
        }

        public void RenderIntegrationUI(Listing_Standard list)
        {
            list.Gap(6);
            list.Label("Realistic Axial Tilt");
            list.Label("RP2 supplies the yearly temperature shift, plant rest, and winter dormancy. Realistic Axial Tilt still draws the sun, shadows, and moon.");
            list.Gap(6);
        }

        public void ExposeData() { }

        public void ResetToDefaults() { }

        private static void Unpatch(string typeName, string patchName, MethodBase original)
        {
            System.Type patchType = AccessTools.TypeByName(typeName);
            MethodInfo patch = patchType == null ? null : AccessTools.Method(patchType, patchName);
            if (patch == null || original == null)
            {
                RPLog.W($"[Compat] Realistic Axial Tilt patch {typeName}.{patchName} was not found. That part of the other mod is still running.");
                return;
            }

            Controller.harmony.Unpatch(original, patch);
        }
    }
}
