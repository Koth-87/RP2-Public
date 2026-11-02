using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.Core;
using Planets.Compatibility.FernyMods;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using UnityEngine;

namespace Planets.Patches.WorldGen
{
    /// <summary>
    /// Patches vanilla WorldGenerator to apply the custom planet name.
    /// Runs unconditionally, as Worldbuilder doesn't override `__result.info.name` via Harmony directly.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld))]
    public static class Patch_WorldGenerator_PlanetName
    {
        public static void Postfix(RimWorld.Planet.World __result)
        {
            if (!string.IsNullOrEmpty(Planets_GameComponent.planetName) && __result != null && __result.info != null)
            {
                __result.info.name = Planets_GameComponent.planetName;
            }
        }
    }

    /// <summary>
    /// Applies ancient road density to WorldGenStep_AncientSites.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenStep_AncientSites), "GenerateAncientSites")]
    public static class Patch_WorldGenStep_AncientSites
    {
        public static bool Prepare()
        {
            return !ModsConfig.IsActive("ferny.Worldbuilder");
        }

        public static void Prefix(WorldGenStep_AncientSites __instance, out FloatRange __state)
        {
            __state = __instance.ancientSitesPer100kTiles;
            
            float density = Planets_GameComponent.ancientRoadDensity;
            if (Mathf.Approximately(density, 1f))
            {
                __state = new FloatRange(-1f, -1f); // Use -1 as "don't restore" flag
                return;
            }

            __instance.ancientSitesPer100kTiles = new FloatRange(__state.min * density, __state.max * density);
        }

        public static Exception Finalizer(WorldGenStep_AncientSites __instance, FloatRange __state, Exception __exception)
        {
            if (__state.min >= 0f)
                __instance.ancientSitesPer100kTiles = __state;
            return __exception;
        }
    }

    /// <summary>
    /// Patches the compiler-generated lambda in WorldGenStep_Roads.GenerateRoadEndpoints
    /// which checks Rand.Value > 0.05f for faction settlements.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_WorldGenStep_Roads_Lambda
    {
        public static bool Prepare() => !ModsConfig.IsActive("ferny.Worldbuilder");

        private static MethodBase TargetMethod()
        {
            foreach (var nestType in typeof(WorldGenStep_Roads).GetNestedTypes(AccessTools.all))
            {
                foreach (var meth in AccessTools.GetDeclaredMethods(nestType))
                {
                    if (meth.Name.Contains("GenerateRoadEndpoints") && meth.ReturnType == typeof(bool))
                    {
                        return meth;
                    }
                }
            }
            return null;
        }

        public static float GetRoadDensityDivisor()
        {
            float density = Planets_GameComponent.factionRoadDensity;
            // Prevent division by zero. If density is 0, we want chance to be very high so no roads form.
            return density <= 0.001f ? 0.001f : density;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            bool found = false;
            foreach (var code in codes)
            {
                yield return code;
                if (!found && code.opcode == OpCodes.Ldc_R4 && (float)code.operand == 0.05f)
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_WorldGenStep_Roads_Lambda), nameof(GetRoadDensityDivisor)));
                    yield return new CodeInstruction(OpCodes.Div);
                    found = true;
                }
            }
        }
    }

    /// <summary>
    /// Applies faction road density to extra road nodes in WorldGenStep_Roads.GenerateRoadEndpoints.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenStep_Roads), "GenerateRoadEndpoints")]
    public static class Patch_WorldGenStep_Roads_ExtraNodes
    {
        public static bool Prepare() => !ModsConfig.IsActive("ferny.Worldbuilder");

        public static float GetRoadDensity() => Planets_GameComponent.factionRoadDensity;

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var methodToHook = AccessTools.PropertyGetter(typeof(FloatRange), nameof(FloatRange.RandomInRange));
            var codes = instructions.ToList();
            bool found = false;
            foreach (var code in codes)
            {
                yield return code;
                if (!found && code.Calls(methodToHook))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_WorldGenStep_Roads_ExtraNodes), nameof(GetRoadDensity)));
                    yield return new CodeInstruction(OpCodes.Mul);
                    found = true;
                }
            }
        }
    }
}