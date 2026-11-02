using HarmonyLib;
using RimWorld.Planet;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Patches.WorldGen
{
    /// <summary>
    /// Skips vanilla lake generation when RP terrain succeeded.
    /// RP marks the true ocean in the terrain phase and sets every other below-sea-level body to the Lake biome.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenStep_Lakes), "GenerateFresh")]
    public static class Patch_WorldGenStep_Lakes
    {
        /// <summary>
        /// Harmony Prefix that unconditionally skips vanilla lake generation.
        /// Returns false to skip vanilla (RP handles it in terrain phase).
        /// </summary>
        public static bool Prefix(string seed, PlanetLayer layer)
        {
            if (!Patch_WorldGenStep_Terrain.rpTerrainSucceeded)
                return true;

            if (Controller.DebugEnabled)
            {
                RPLog.D("[WorldGen] Skipping vanilla lake generation - RP handles lake conversion in terrain phase");
            }
            
            return false;
        }
    }
}
