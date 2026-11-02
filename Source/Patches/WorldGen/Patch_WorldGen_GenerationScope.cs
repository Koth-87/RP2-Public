using System;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Planets.Core;

namespace Planets.Patches.WorldGen
{
    /// <summary>
    /// Clears the RP generation flag even when world generation throws.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld))]
    public static class Patch_WorldGenerator_GenerationScope
    {
        public static Exception Finalizer(Exception __exception)
        {
            Planets_GameComponent.generatingWithRP = false;
            return __exception;
        }
    }

    /// <summary>
    /// Keeps this game's sea level, tilt, and scale, then returns the shared statics to defaults
    /// so the next non-RP world does not inherit them.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
    public static class Patch_Game_InitNewGame
    {
        public static void Prefix(Game __instance)
        {
            __instance.GetComponent<Planets_GameComponent>()?.CaptureGenerationParameters();
        }

        public static void Postfix()
        {
            Planets_GameComponent.ResetGenerationStatics();
        }
    }
}
