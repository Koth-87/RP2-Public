using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.Utilities.Text;

namespace Planets.Patches.UI
{
    /// <summary>
    /// Adds world generation parameters to the Planet info tab.
    /// 
    /// Displays:
    /// - World preset
    /// - Sea level
    /// - Rainfall modifier
    /// - Temperature setting
    /// - Axial tilt
    /// - Population level
    /// 
    /// Also adjusts tab height to fit additional content:
    /// - Vanilla: 200px
    /// - With RP: 300px  
    /// - With RP + Worldbuilder: 425px
    /// 
    /// Includes Worldbuilder compatibility via transpiler to adjust button positions.
    /// </summary>

    // ========== PATCH 1: ADD WORLD PARAMETERS TO DESCRIPTION ==========

    /// <summary>
    /// Appends world generation parameters to the Planet tab description.
    /// Uses WorldInfoStrings helpers for formatting.
    /// Includes duplicate-check to prevent re-entry.
    /// </summary>
    [HarmonyPatch(typeof(WITab_Planet), "Desc", MethodType.Getter)]
    public static class Patch_PlanetDesc
    {
        private static string cachedWorldPresetsLabel;
        private static string cachedSeaLevelLabel;
        private static string cachedAxialTiltLabel;

        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            cachedWorldPresetsLabel ??= "Planets.WorldPresets".Translate();
            cachedSeaLevelLabel ??= "Planets.SeaLevel".Translate();
            cachedAxialTiltLabel ??= "Planets.AxialTilt".Translate();

            // Check if we've already added our info to prevent duplicates
            if (__result.Contains(cachedWorldPresetsLabel) ||
                __result.Contains(cachedSeaLevelLabel) ||
                __result.Contains(cachedAxialTiltLabel))
            {
                return;
            }

            var sb = new StringBuilder(__result);
            {
                var preset = Planets_GameComponent.ActiveWorldPreset;
                var seaLevel = WorldInfoStrings.GetSeaLevel();
                var rainfall = WorldInfoStrings.GetRainfall();
                var temperature = WorldInfoStrings.GetTemperature();
                var axialTilt = WorldInfoStrings.GetAxialTilt();
                var population = WorldInfoStrings.GetPopulation();

                sb.Append("Planets.WorldPresets", preset);
                sb.Append("Planets.SeaLevel", seaLevel);
                sb.Append("PlanetRainfall", rainfall);
                sb.Append("PlanetTemperature", temperature);
                sb.Append("Planets.AxialTilt", axialTilt);
                sb.Append("PlanetPopulation", population);
            }
            __result = sb.ToString();
        }
    }

    // ========== PATCH 2: ADJUST TAB HEIGHT ==========

    /// <summary>
    /// Adjusts WITab_Planet window size to fit additional content.
    /// </summary>
    [HarmonyPatch(typeof(WITab_Planet), "FillTab")]
    public static class Patch_PlanetTabSize
    {
        private static bool hasLoggedError = false;
        private static bool? cachedWorldbuilderActive;
        private static readonly FieldInfo WinSizeField = AccessTools.Field(typeof(WITab_Planet), "WinSize");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)] // Run after Worldbuilder's patch
        public static void Postfix(WITab_Planet __instance)
        {
            try
            {
                cachedWorldbuilderActive ??= ModsConfig.IsActive("ferny.Worldbuilder");
                float targetHeight = cachedWorldbuilderActive.Value ? 425f : 300f;

                if (__instance.size.y < targetHeight)
                {
                    __instance.size.y = targetHeight;
                }

                if (WinSizeField != null)
                {
                    var winSizeValue = (Vector2)WinSizeField.GetValue(null);
                    if (winSizeValue.y < targetHeight)
                    {
                        winSizeValue.y = targetHeight;
                        WinSizeField.SetValue(null, winSizeValue);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedError)
                {
                    RPLog.Warning($"Failed to adjust WITab_Planet size: {ex.Message}");
                    hasLoggedError = true;
                }
            }
        }
    }

    // ========== PATCH 3: WORLDBUILDER WINDOW HEIGHT TRANSPILER ==========

    /// <summary>
    /// Transpiler to make Worldbuilder use 425px height for the Planet tab (instead of 370px).
    /// Worldbuilder positions its buttons relative to size.y, so if we patch the height before
    /// it runs, buttons will be correctly positioned. Runs before Patch_PlanetTabSize as backup.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WorldbuilderButtonTranspiler
    {
        static WorldbuilderButtonTranspiler()
        {
            try
            {
                if (!LoadedModManager.RunningMods.Any(m =>
                    m.PackageIdPlayerFacing.ToLower() == "ferny.worldbuilder"))
                    return;

                var worldbuilderAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Worldbuilder");
                if (worldbuilderAssembly == null) return;

                var patchType = worldbuilderAssembly.GetType("Worldbuilder.WITab_Planet_FillTab_Patch");
                var constructorPatch = worldbuilderAssembly.GetType("Worldbuilder.WITab_Planet_Constructor_Patch");
                if (patchType == null && constructorPatch == null) return;

                var harmony = new Harmony("Planets.WorldbuilderButtonTranspiler");

                var transpiler = new HarmonyMethod(typeof(WorldbuilderButtonTranspiler), nameof(Transpiler));
                if (patchType != null)
                {
                    var postfix = patchType.GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public);
                    if (postfix != null)
                        harmony.Patch(postfix, transpiler: transpiler);
                }
                if (constructorPatch != null)
                {
                    var postfix = constructorPatch.GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public);
                    if (postfix != null)
                        harmony.Patch(postfix, transpiler: transpiler);
                }
            }
            catch (Exception ex)
            {
                if (Controller.DebugEnabled)
                    RPLog.D($"Worldbuilder transpiler: {ex.Message}");
            }
        }

        /// <summary>
        /// Replaces Worldbuilder's window height (330f or 370f) with 425f so buttons position correctly.
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float f)
                {
                    if (Math.Abs(f - 330f) < 0.01f || Math.Abs(f - 370f) < 0.01f)
                    {
                        codes[i] = new CodeInstruction(OpCodes.Ldc_R4, 425f);
                        break;
                    }
                }
            }
            return codes;
        }
    }
}
