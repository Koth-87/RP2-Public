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
                bool foundSlider = false;
                bool foundLabel = false;
                const float labelWidth = 150f;

                // RP sliders are Rect(150, y, width - 150, 30). WTL draws Rect(200, pos, width, 30)
                // and a 200-wide label, so its track starts 50px later and is 50px shorter.
                for (int i = 0; i < codes.Count - 3; i++)
                {
                    if (!foundLabel
                        && codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float zero && zero == 0f
                        && (codes[i + 1].opcode == OpCodes.Ldarg_2 || codes[i + 1].IsLdloc())
                        && codes[i + 2].opcode == OpCodes.Ldc_R4 && codes[i + 2].operand is float label && label == 200f
                        && codes[i + 3].opcode == OpCodes.Ldc_R4 && codes[i + 3].operand is float labelHeight && labelHeight == 30f)
                    {
                        codes[i + 2].operand = labelWidth;
                        foundLabel = true;
                    }

                    if (!foundSlider
                        && codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float origin && origin == 200f
                        && (codes[i + 1].opcode == OpCodes.Ldarg_2 || codes[i + 1].IsLdloc())
                        && codes[i + 2].opcode == OpCodes.Ldarg_3
                        && codes[i + 3].opcode == OpCodes.Ldc_R4 && codes[i + 3].operand is float sliderHeight && sliderHeight == 30f)
                    {
                        codes[i].operand = labelWidth;
                        codes.Insert(i + 3, new CodeInstruction(OpCodes.Ldc_R4, labelWidth));
                        codes.Insert(i + 4, new CodeInstruction(OpCodes.Sub));
                        foundSlider = true;
                        i += 2;
                    }
                }

                if (Controller.DebugEnabled)
                {
                    if (foundSlider)
                        RPLog.D("[Compat] Patched WTL slider to the 150px RP column.");
                    else
                        RPLog.D("[Compat] WTL slider width pattern not found");
                }
                
                return codes;
            }
        }
    }
}
