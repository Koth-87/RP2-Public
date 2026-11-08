using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.API;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Fallback for Realistic Axial Tilt builds that do not read an RP2 flag yet.
    /// Removes their yearly-temperature and plant patches by name. See ICompatibilityModule.
    /// Their sky, shadows, and moon stay. Their tilt read looks up a type name that is not this
    /// build's, so that call is answered with <see cref="RealisticPlanetsAPI.TiltDegrees"/>.
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

            MethodInfo tilt = AccessTools.Method("RealisticAxialTilt.Compat.RealisticPlanets2Compat:GetTiltDegrees");
            if (tilt == null)
                RPLog.W("[Compat] Realistic Axial Tilt's tilt read was not found. Its sky will stay on its own fallback tilt.");
            else
                Controller.harmony.Patch(tilt, prefix: new HarmonyMethod(typeof(RealisticAxialTiltCompat), nameof(TiltDegrees)));

            RPLog.Message("[Compat] Realistic Axial Tilt: RP2 owns yearly temperature and plant seasons. Its sky uses RP2's tilt.");
        }

        public void RenderIntegrationUI(Listing_Standard list)
        {
            list.Gap(6);
            list.Label("Realistic Axial Tilt");
            list.Label("RP2 supplies the yearly temperature shift, plant rest, winter dormancy, and the tilt. Realistic Axial Tilt still draws the sun, shadows, and moon.");
            list.Gap(6);
        }

        public void ExposeData() { }

        public void ResetToDefaults() { }

        /// <summary>Their lookup asks for Planets.Planets_GameComponent. This build keeps the tilt on Planets.Core.Planets_GameComponent.</summary>
        public static bool TiltDegrees(ref float __result)
        {
            __result = RealisticPlanetsAPI.TiltDegrees;
            return false;
        }

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
