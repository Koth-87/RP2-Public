using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Vanilla Fishing Expanded compatibility module.
    /// Adds RP2 biomes to VCEF's temperature-based fishing categories (non-Odyssey only).
    /// </summary>
    [StaticConstructorOnStartup]
    public class VFEFishingCompat : ICompatibilityModule
    {
        private static VFEFishingCompat _instance;
        public static VFEFishingCompat Instance => _instance ?? (_instance = new VFEFishingCompat());

        public static bool IsEnabled => Instance.IsActive;

        static VFEFishingCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "VanillaExpanded.VCEF";
        public string ModName => "Vanilla Fishing Expanded";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }
        public void RenderIntegrationUI(Listing_Standard list) { }
        public void ExposeData() { }
        public void ResetToDefaults() { }

        // Mapping of RP2 biomes to VCEF temperature categories
        private static readonly Dictionary<string, string> BiomeToTempCategory = new Dictionary<string, string>
        {
            { "RP_Prairie", "VCEF_BiomeWarm" },        // TemperateForest equivalent
            { "RP_Steppe", "VCEF_BiomeCold" },        // BorealForest equivalent  
            { "RP_Savanna", "VCEF_BiomeHot" },         // AridShrubland equivalent
            { "RP_Oasis", "VCEF_BiomeHot" },           // Desert variant
            { "RP_Permafrost", "VCEF_BiomeCold" },     // Tundra equivalent
            { "RP_ColdDesert", "VCEF_BiomeCold" }      // New in RP2
        };
        
        public void Initialize()
        {
            try
            {
                // Check if VCEF is active
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.D("[Compat] Vanilla Fishing Expanded not detected");
                    return;
                }
                
                RPLog.Message("[Compat] Vanilla Fishing Expanded detected, initializing compatibility");
                
                // If Odyssey is active, VCEF uses vanilla fish system instead of BiomeTempDef
                if (ModsConfig.OdysseyActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.D("[Compat] Odyssey detected - VCEF uses vanilla fish system (handled by DLC_Odyssey_Compat)");
                    return;
                }
                
                // For non-Odyssey, add RP2 biomes to VCEF temperature categories
                AddRP2BiomesToTempCategories();
                
                RPLog.Message("[Compat] Vanilla Fishing Expanded compatibility initialized successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to initialize Vanilla Fishing Expanded compatibility: {e}");
                IsActive = false;
            }
        }
        
        /// <summary>
        /// Add RP2 biomes to VCEF's temperature-based fishing categories.
        /// Uses reflection to modify VCEF's BiomeTempDef instances.
        /// </summary>
        private void AddRP2BiomesToTempCategories()
        {
            try
            {
                // Get VCE-Fishing assembly
                var vfeAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.FullName.Contains("VCE-Fishing"));
                
                if (vfeAssembly == null)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Warning("[Compat] Could not find VCE-Fishing assembly");
                    return; // Don't disable here since it's instance
                }
                
                // Get BiomeTempDef type
                var biomeTempDefType = vfeAssembly.GetType("VCE_Fishing.BiomeTempDef");
                if (biomeTempDefType == null)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Warning("[Compat] Could not find BiomeTempDef type");
                    return; // Don't disable here since it's instance
                }
                
                // Get all BiomeTempDef instances using generic DefDatabase
                var getAllMethod = typeof(DefDatabase<>)
                    .MakeGenericType(biomeTempDefType)
                    .GetProperty("AllDefs", BindingFlags.Public | BindingFlags.Static);
                
                if (getAllMethod == null)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Warning("[Compat] Could not get AllDefs property for BiomeTempDef");
                    return; // Don't disable here since it's instance
                }
                
                var allBiomeTempDefs = getAllMethod.GetValue(null) as IEnumerable;
                if (allBiomeTempDefs == null)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Warning("[Compat] Could not get BiomeTempDef instances");
                    return; // Don't disable here since it's instance
                }
                
                // Get the biomes field (List<string>)
                var biomesField = biomeTempDefType.GetField("biomes", 
                    BindingFlags.Public | BindingFlags.Instance);
                
                if (biomesField == null)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Warning("[Compat] Could not find biomes field in BiomeTempDef");
                    return; // Don't disable here since it's instance
                }
                
                int added = 0;
                
                // Add RP2 biomes to appropriate temperature categories
                foreach (var tempDef in allBiomeTempDefs)
                {
                    if (tempDef == null) continue;
                    
                    // Get the defName
                    var defNameField = biomeTempDefType.GetField("defName", 
                        BindingFlags.Public | BindingFlags.Instance);
                    string defName = defNameField?.GetValue(tempDef) as string;
                    
                    if (string.IsNullOrEmpty(defName)) continue;
                    
                    // Get the biomes list
                    var biomesList = biomesField.GetValue(tempDef) as IList<string>;
                    if (biomesList == null) continue;
                    
                    // Check each RP2 biome mapping
                    foreach (var mapping in BiomeToTempCategory)
                    {
                        // Check if this temp category matches
                        if (defName == mapping.Value)
                        {
                            // Check if the RP2 biome exists
                            var rpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                            if (rpBiome != null && !biomesList.Contains(mapping.Key))
                            {
                                biomesList.Add(mapping.Key);
                                added++;
                                
                                if (Controller.DebugEnabled)
                                    RPLog.D($"[Compat] Added {mapping.Key} to VCEF fishing category {mapping.Value}");
                            }
                        }
                    }
                }
                
                if (added > 0)
                {
                    RPLog.Message($"[Compat] Successfully added {added} RP2 biomes to VCEF fishing categories");
                }
                else if (Controller.DebugEnabled)
                {
                    RPLog.D("[Compat] No RP2 biomes needed to be added to VCEF (may already be patched)");
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Error adding RP2 biomes to VCEF: {e}");
            }
        }
    }
}
