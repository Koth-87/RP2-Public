using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// World Tech Level compatibility module.
    /// Integrates WTL's tech level slider into RP2's world creation page.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WorldTechLevelCompat : ICompatibilityModule
    {
        private static WorldTechLevelCompat _instance;
        public static WorldTechLevelCompat Instance => _instance ?? (_instance = new WorldTechLevelCompat());

        public static bool IsEnabled => Instance.IsActive;

        static WorldTechLevelCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "m00nl1ght.WorldTechLevel";
        public string ModName => "World Tech Level";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }
        public void RenderIntegrationUI(Listing_Standard list) { }
        public void ExposeData() { }
        public void ResetToDefaults() { }

        // Cached reflection member
        private MethodInfo doTechLevelSliderMethod;
        
        public void Initialize()
        {
            try
            {
                // Check if World Tech Level is active
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.D("[Compat] World Tech Level not detected");
                    return;
                }
                
                RPLog.Message("[Compat] World Tech Level detected, initializing compatibility");
                
                // Get the patch type that contains DoTechLevelSlider
                // Use robust AccessTools to find the type in any loaded assembly
                var patchType = AccessTools.TypeByName("WorldTechLevel.Patches.Patch_Page_CreateWorldParams");
                
                if (patchType == null)
                {
                    RPLog.Warning("[Compat] World Tech Level detected but Patch_Page_CreateWorldParams type not found");
                    IsActive = false;
                    return;
                }
                
                // Cache the DoTechLevelSlider method
                // Method signature: DoTechLevelSlider(List<FactionDef> factions, ref float pollution, float pos, float width)
                doTechLevelSliderMethod = patchType.GetMethod("DoTechLevelSlider",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                
                if (doTechLevelSliderMethod == null)
                {
                    RPLog.Warning("[Compat] World Tech Level: DoTechLevelSlider method not found");
                    IsActive = false;
                    return;
                }
                
                // Apply patches to WTL
                ApplyWTLPatches();

                RPLog.Message("[Compat] World Tech Level compatibility initialized successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to initialize World Tech Level compatibility: {e}");
                IsActive = false;
            }
        }
        
        /// <summary>
        /// Apply our patches to WTL methods to fix slider width
        /// </summary>
        private void ApplyWTLPatches()
        {
            try
            {
                var harmony = Controller.harmony;
                if (harmony == null)
                {
                    RPLog.Error("[Compat] Harmony instance not available for WTL patching");
                    return;
                }
                
                // Apply the slider width fix transpiler
                if (doTechLevelSliderMethod != null)
                {
                    var transpiler = new HarmonyMethod(typeof(WTLSliderWidthFix), nameof(WTLSliderWidthFix.Transpiler));
                    harmony.Patch(doTechLevelSliderMethod, transpiler: transpiler);
                    
                    if (Controller.DebugEnabled)
                        RPLog.D("[Compat] Applied WTL slider width fix patch");
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to apply WTL patches: {e}");
            }
        }

        /// <summary>
        /// Draws the World Tech Level slider by calling WTL's method.
        /// Returns the updated Y position for the next UI element.
        /// </summary>
        /// <param name="factions">Faction list (ref parameter - WTL may modify)</param>
        /// <param name="pollution">Pollution value (ref parameter - WTL may modify)</param>
        /// <param name="yPos">Current Y position</param>
        /// <param name="width">Available width for the slider</param>
        /// <returns>Updated Y position after drawing the slider</returns>
        public static float DoTechLevelSlider(ref List<FactionDef> factions, ref float pollution, float yPos, float width)
        {
            return Instance.DoTechLevelSliderInternal(ref factions, ref pollution, yPos, width);
        }

        private float DoTechLevelSliderInternal(ref List<FactionDef> factions, ref float pollution, float yPos, float width)
        {
            if (!IsActive || doTechLevelSliderMethod == null)
                return yPos;
            
            try
            {
                // Prepare arguments for reflection call
                // WTL's signature: DoTechLevelSlider(List<FactionDef> factions, ref float pollution, float pos, float width)
                object[] args = new object[] { factions, pollution, yPos, width };
                
                // Call WTL's method
                var result = doTechLevelSliderMethod.Invoke(null, args);
                
                // Update ref parameters (args[0] = factions, args[1] = pollution)
                factions = (List<FactionDef>)args[0];
                pollution = (float)args[1];
                
                // Return updated Y position (or increment if method returns void)
                if (result != null && result is float newYPos)
                {
                    return newYPos;
                }
                else
                {
                    // Fallback: assume standard slider height
                    return yPos + 40f;
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to call World Tech Level DoTechLevelSlider: {e}");
                return yPos;
            }
        }
        
        /// <summary>
        /// Helper method to get a type from any loaded assembly.
        /// </summary>
        private static Type GetTypeFromAnyAssembly(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .FirstOrDefault(t => t.FullName == typeName);
        }

        /// <summary>
        /// Inner class for the slider width fix transpiler
        /// </summary>
        private static class WTLSliderWidthFix
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                bool found = false;
                
                // We're looking for this pattern in WTL's DoTechLevelSlider:
                // var sliderRect = new Rect(200f, pos, width, 30f);
                // WTL uses 'width' directly but our 'width' is already adjusted
                
                for (int i = 0; i < codes.Count - 4; i++)
                {
                    // Pattern: ldc.r4 200, [something], ldarg.3 (width), ldc.r4 30, newobj Rect
                    // Note: In WTL method, width is the 4th argument (index 3 if static, but might vary)
                    // The signature is static: DoTechLevelSlider(List<FactionDef> factions, ref float pollution, float pos, float width)
                    // Arg indexes: 0=factions, 1=pollution, 2=pos, 3=width
                    
                    if (codes[i].opcode == OpCodes.Ldc_R4 && (float)codes[i].operand == 200f)
                    {
                        // Check if this is followed by loading pos (ldarg.2), then width (ldarg.3), then 30f
                        bool foundPattern = false;
                        if (i + 3 < codes.Count &&
                            codes[i + 1].opcode == OpCodes.Ldarg_2 && // pos parameter
                            codes[i + 2].opcode == OpCodes.Ldarg_3 && // width parameter  
                            codes[i + 3].opcode == OpCodes.Ldc_R4 && (float)codes[i + 3].operand == 30f)
                        {
                            foundPattern = true;
                        }
                        // Alternative pattern with locals (just in case compiler optimized diff)
                        else if (i + 3 < codes.Count &&
                            (codes[i + 1].opcode == OpCodes.Ldloc || codes[i + 1].opcode == OpCodes.Ldloc_S) && // pos local
                            codes[i + 2].opcode == OpCodes.Ldarg_3 && // width parameter
                            codes[i + 3].opcode == OpCodes.Ldc_R4 && (float)codes[i + 3].operand == 30f)
                        {
                            foundPattern = true;
                        }
                        
                        if (foundPattern)
                        {
                            // The issue: WTL expects the full column width but we're passing adjusted width
                            // We need to subtract 200f from the width for the slider
                            // Insert after the width is loaded (at position i+3 in pattern relative to i)
                            // But wait, the sequence is: 200f, pos, width, 30f.
                            // We want to change 'width' to 'width - 200f'.
                            // Width is at i+2. So we insert after i+2.
                            
                            // Insert "ldc.r4 200" and "sub" after "ldarg.3"
                            codes.Insert(i + 3, new CodeInstruction(OpCodes.Ldc_R4, 200f));
                            codes.Insert(i + 4, new CodeInstruction(OpCodes.Sub));
                            found = true;
                            
                            if (Controller.DebugEnabled)
                                RPLog.D($"[Compat] Patched WTL slider width at instruction {i}");
                            
                            break;
                        }
                    }
                }
                
                if (!found && Controller.DebugEnabled)
                {
                    RPLog.D("[Compat] WTL slider width pattern not found");
                }
                
                return codes;
            }
        }
    }
}
