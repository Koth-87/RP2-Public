using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Planets.Utilities.Logging;
using Planets.Core;

namespace Planets.Patches.WorldGen
{
    /// <summary>
    /// Patches FeatureWorker_MountainRange to use LargeHills+ threshold for range detection.
    /// 
    /// RATIONALE:
    /// Vanilla uses SmallHills+ (hilliness != Flat) for mountain range detection, which worked
    /// fine with noise-based hilliness generation. With RP's realistic hilliness (based on relief,
    /// prominence, ruggedness) and connected ridge enhancement, SmallHills are more coherent and
    /// widespread. Using the vanilla threshold creates massive map-covering "ranges" instead of
    /// distinct, recognizable features.
    /// 
    /// Changing to LargeHills+ (hilliness >= Hilliness.LargeHills) keeps ranges distinct while
    /// working properly with our connected mountain systems.
    /// </summary>
    [HarmonyPatch(typeof(FeatureWorker_MountainRange), "IsRoot")]
    public static class Patch_FeatureWorker_MountainRange
    {
        /// <summary>
        /// Replaces the IsRoot check to only consider LargeHills or greater as mountain range material.
        /// 
        /// VANILLA: hilliness != Flat (i.e., SmallHills+)
        /// RP:      hilliness >= LargeHills (i.e., LargeHills, Mountainous, Impassable)
        /// </summary>
        public static bool Prefix(PlanetTile tile, ref bool __result)
        {
            // Only consider LargeHills or greater (hilliness >= 3)
            // Vanilla used SmallHills or greater (hilliness >= 2)
            __result = Find.WorldGrid[tile].hilliness >= Hilliness.LargeHills;
            
            // Skip original method - we've replaced it completely
            return false;
        }
    }
}
