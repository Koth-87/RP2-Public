using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.Biomes.Core;

namespace Planets.Compatibility
{
    /// <summary>
    /// Self-contained compatibility module for Biotech DLC.
    /// Handles pollution animals and any other Biotech-specific content.
    /// </summary>
    [StaticConstructorOnStartup]
    public class BiotechCompat : ICompatibilityModule
    {
        private static BiotechCompat _instance;
        public static BiotechCompat Instance => _instance ?? (_instance = new BiotechCompat());

        public static bool IsEnabled => Instance.IsActive;

        static BiotechCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "ludeon.rimworld.biotech";
        public string ModName => "Biotech DLC";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }
        public void RenderIntegrationUI(Listing_Standard list) { }
        public void ExposeData() { }
        public void ResetToDefaults() { }

        // ========== INITIALIZATION ==========
        
        public void Initialize()
        {
            try
            {
                // Check if Biotech is active
                IsActive = ModsConfig.BiotechActive;
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Debug("Biotech DLC not detected");
                    return;
                }
                
                RPLog.Debug("Biotech DLC detected, initializing compatibility");
                
                CopyPollutionAnimals();
                
                RPLog.Debug("Biotech DLC compatibility initialized successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"Error initializing Biotech compatibility: {e}");
                IsActive = false;
            }
        }
        
        // ========== CONTENT COPYING ==========
        
        /// <summary>
        /// Copy pollution animals directly from vanilla BiomeDefs to RP custom biomes.
        /// Uses BiomeContentHelper mappings to determine which vanilla biomes to copy from.
        /// </summary>
        private static void CopyPollutionAnimals()
        {
            int copied = 0;
            
            foreach (var mapping in BiomeContentHelper.BiomeMappings)
            {
                var rpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                var vanillaBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Value, false);
                
                if (rpBiome == null || vanillaBiome == null) continue;
                
                var vanillaPollutionAnimals = BiomeContentHelper.GetPollutionWildAnimals(vanillaBiome);
                if (vanillaPollutionAnimals == null) continue;
                
                foreach (var animal in vanillaPollutionAnimals)
                {
                    // Reduce commonality by half for RP biomes (less pollution-adapted)
                    BiomeContentHelper.AddPollutionAnimal(rpBiome, animal.animal, animal.commonality / 2f);
                    copied++;
                }
            }
            
            if (Controller.DebugEnabled && copied > 0)
            {
                RPLog.Debug($"Copied {copied} pollution animals from vanilla biomes");
            }
        }
    }
}
