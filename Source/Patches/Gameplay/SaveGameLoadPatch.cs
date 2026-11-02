using HarmonyLib;
using Verse;
using Planets.WorldGen.Astronomy;

namespace Planets.Patches.Gameplay
{
    /// <summary>
    /// Ensures seasonal temperature curve is properly initialized when loading a saved game.
    /// This is critical because the axial tilt curve must be set up before temperature calculations occur.
    /// </summary>
    [HarmonyPatch(typeof(SavedGameLoaderNow), "LoadGameFromSaveFileNow")]
    public static class SaveGameLoadPatch
    {
        public static void Postfix()
        {
            // Initialize seasonal temperature variation curve based on world's axial tilt setting
            // (replaces old Planets_TemperatureTuning.SetSeasonalCurve())
            AxialTiltCurves.SetSeasonalCurve();
        }
    }
}
