using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;
using Verse;
using Verse.Noise;
using RimWorld.Planet;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility.GeologicalLandforms
{
    /// <summary>
    /// Critical performance patch for Geological Landforms compatibility.
    /// Transpiles GL's CalcCaveSystems to bypass expensive spatial lookups (Vector3 -> TileID)
    /// by injecting the loop variable 'tileIdx' directly into our noise adapters.
    /// Reduces execution time from ~70s to <1s.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_GL_Performance
    {
        private static bool IsActive => 
            ModsConfig.IsActive("m00nl1ght.GeologicalLandforms") && 
            Controller.Settings.perfOptimizeGL.CurrentValue;

        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            // Only apply if GL is active and optimization is enabled
            return IsActive;
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method("GeologicalLandforms.Patches.Patch_RimWorld_WorldGenStep_Terrain:CalcCaveSystems");
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);
            
            // Scan for GetTileCenter call to identify loop variable
            int loopVarIndex = -1;
            var getTileCenter = AccessTools.Method(typeof(WorldGrid), nameof(WorldGrid.GetTileCenter));
            // Handle PlanetTile implicit cast (common in some mod environments or specific RW versions)
            var opImplicit = AccessTools.Method(typeof(PlanetTile), "op_Implicit", new[] { typeof(int) });

            if (getTileCenter == null)
            {
                RPLog.Error("[Compat] Patch_GL_Performance: WorldGrid.GetTileCenter method not found.");
                return instructions;
            }
            
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(getTileCenter))
                {
                    // The instruction before this should load the index (skipping Nops and other noise)
                    int backIndex = i - 1;
                    while (backIndex >= 0 && (codes[backIndex].opcode == OpCodes.Nop || codes[backIndex].opcode == OpCodes.Br || codes[backIndex].opcode == OpCodes.Br_S))
                    {
                        backIndex--;
                    }

                    if (backIndex >= 0)
                    {
                        var prev = codes[backIndex];

                        // Special handling: If there's an implicit cast to PlanetTile before GetTileCenter
                        // (seen in some IL dumps: ldloc -> op_Implicit -> GetTileCenter)
                        if (opImplicit != null && prev.Calls(opImplicit))
                        {
                            backIndex--;
                            if (backIndex >= 0) prev = codes[backIndex];
                        }
                        
                        if (Controller.DebugEnabled)
                        {
                            RPLog.D($"[Compat] Found GetTileCenter at index {i}, checking instruction at {backIndex}: {prev.opcode} {prev.operand}");
                        }
                        
                        if (prev.IsLdloc())
                        {
                            if (prev.opcode == OpCodes.Ldloc_0) loopVarIndex = 0;
                            else if (prev.opcode == OpCodes.Ldloc_1) loopVarIndex = 1;
                            else if (prev.opcode == OpCodes.Ldloc_2) loopVarIndex = 2;
                            else if (prev.opcode == OpCodes.Ldloc_3) loopVarIndex = 3;
                            else if (prev.opcode == OpCodes.Ldloc_S && prev.operand is LocalBuilder lb1) loopVarIndex = lb1.LocalIndex;
                            else if (prev.opcode == OpCodes.Ldloc && prev.operand is LocalBuilder lb2) loopVarIndex = lb2.LocalIndex;
                            else if (prev.operand is byte b) loopVarIndex = b;
                            else if (prev.operand is sbyte sb) loopVarIndex = sb;
                            else if (prev.operand is int idx) loopVarIndex = idx;
                        }
                    }
                    
                    if (loopVarIndex != -1)
                    {
                        if (Controller.DebugEnabled)
                        {
                            RPLog.D($"[Compat] Identified loop variable as local index {loopVarIndex}");
                        }
                        break;
                    }
                }
            }

            if (loopVarIndex == -1)
            {
                RPLog.Error("[Compat] Patch_GL_Performance: Could not identify loop variable in CalcCaveSystems. " +
                    "The method may have been modified by another transpiler. Optimization disabled.");
                return instructions; 
            }

            // Method signatures we are looking for
            var getValueDouble = AccessTools.Method(typeof(ModuleBase), nameof(ModuleBase.GetValue), 
                new[] { typeof(double), typeof(double), typeof(double) });
            var getValueVector = AccessTools.Method(typeof(ModuleBase), nameof(ModuleBase.GetValue), 
                new[] { typeof(Vector3) });

            // Our replacement methods
            var hookDouble = AccessTools.Method(typeof(Patch_GL_Performance), nameof(GetValueFast));
            var hookVector = AccessTools.Method(typeof(Patch_GL_Performance), nameof(GetValueFastVector));

            bool found = false;

            // Now apply the patch
            for (int i = 0; i < codes.Count; i++)
            {
                // Case 1: GetValue(double, double, double)
                if (codes[i].Calls(getValueDouble))
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = hookDouble;
                    InsertTileIndex(codes, i, loopVarIndex);
                    i++; // Skip inserted instruction
                    found = true;
                }
                // Case 2: GetValue(Vector3)
                else if (getValueVector != null && codes[i].Calls(getValueVector))
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = hookVector;
                    InsertTileIndex(codes, i, loopVarIndex);
                    i++; // Skip inserted instruction
                    found = true;
                }
            }

            if (!found)
            {
                RPLog.Error("[Compat] Patch_GL_Performance: Could not find GetValue calls. Aborting patch.");
                return instructions;
            }
            
            if (Controller.DebugEnabled)
                RPLog.Message($"[Compat] Patch_GL_Performance applied. Loop variable index: {loopVarIndex}");

            return codes;
        }

        private static void InsertTileIndex(List<CodeInstruction> codes, int index, int loopVarIndex)
        {
            codes.Insert(index, CodeInstruction.LoadLocal(loopVarIndex));
        }

        public static double GetValueFast(ModuleBase module, double x, double y, double z, int tileIdx)
        {
            // Fast path for our adapters
            if (module is IRP2NoiseAdapter rp2Adapter)
            {
                return rp2Adapter.GetValueForTile(tileIdx);
            }

            // Fallback for standard modules
            return module.GetValue(x, y, z);
        }

        public static double GetValueFastVector(ModuleBase module, Vector3 pos, int tileIdx)
        {
            // Fast path for our adapters
            if (module is IRP2NoiseAdapter rp2Adapter)
            {
                return rp2Adapter.GetValueForTile(tileIdx);
            }

            // Fallback for standard modules
            // We know ModuleBase has GetValue(double, double, double)
            return module.GetValue(pos.x, pos.y, pos.z);
        }
    }
}
