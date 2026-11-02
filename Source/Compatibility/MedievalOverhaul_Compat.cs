using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Handles Medieval Overhaul mod compatibility for Realistic Planets.
    /// Manages settings for creature/plant distribution in RP biomes.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MedievalOverhaulCompat : ICompatibilityModule
    {
        private static MedievalOverhaulCompat _instance;
        public static MedievalOverhaulCompat Instance => _instance ?? (_instance = new MedievalOverhaulCompat());

        public static bool IsEnabled => Instance.IsActive;

        static MedievalOverhaulCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "dankpyon.medieval.overhaul";
        public string ModName => "Medieval Overhaul";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }

        // ========== SETTINGS ==========
        // Stored on IntegrationConfiguration so they save during the Mod constructor,
        // before this module has registered.
        private bool AllowCreaturesValue
        {
            get => Controller.Settings?.integrationConfig?.medievalCreaturesInNewBiomes ?? true;
            set
            {
                if (Controller.Settings?.integrationConfig != null)
                    Controller.Settings.integrationConfig.medievalCreaturesInNewBiomes = value;
            }
        }

        private bool AllowPlantsValue
        {
            get => Controller.Settings?.integrationConfig?.medievalPlantsInNewBiomes ?? true;
            set
            {
                if (Controller.Settings?.integrationConfig != null)
                    Controller.Settings.integrationConfig.medievalPlantsInNewBiomes = value;
            }
        }

        public static bool AllowCreaturesInNewBiomes => Instance.AllowCreaturesValue;
        public static bool AllowPlantsInNewBiomes => Instance.AllowPlantsValue;
        
        // ========== INITIALIZATION ==========
        
        public void Initialize()
        {
            try
            {
                // Check if Medieval Overhaul is active
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Debug("Medieval Overhaul not detected");
                    return;
                }
                
                RPLog.Debug("Medieval Overhaul detected, initializing compatibility");
                
                // Biome worker redirect handled by BiomeCompatWorkerRedirect
                // The plant/animal filtering is handled in BiomeContentHelper module
                // via ShouldSkipPlant and ShouldSkipAnimal checks
                
                RPLog.Debug("Medieval Overhaul compatibility initialized");
            }
            catch (Exception e)
            {
                RPLog.Error($"Error initializing Medieval Overhaul compatibility: {e}");
                IsActive = false;
            }
        }
        
        // ========== FILTERING METHODS ==========
        
        /// <summary>
        /// Check if a plant should be skipped based on Medieval Overhaul settings.
        /// Called by BiomeContentHelper during content copying.
        /// </summary>
        public static bool ShouldSkipPlant(ThingDef plant)
        {
            if (!ModsConfig.IsActive("DankPyon.Medieval.Overhaul"))
                return false;

            bool allow = Controller.Settings?.integrationConfig?.medievalPlantsInNewBiomes ?? true;
            return !allow && plant.defName.StartsWith("DankPyon_");
        }
        
        /// <summary>
        /// Check if an animal should be skipped based on Medieval Overhaul settings.
        /// Called by BiomeContentHelper during content copying.
        /// </summary>
        public static bool ShouldSkipAnimal(PawnKindDef animal)
        {
            if (!ModsConfig.IsActive("DankPyon.Medieval.Overhaul"))
                return false;

            bool allow = Controller.Settings?.integrationConfig?.medievalCreaturesInNewBiomes ?? true;
            return !allow && animal.defName.StartsWith("DankPyon_");
        }
        
        // ========== INTEGRATION CONFIGURATION METHODS ==========
        
        /// <summary>
        /// Render UI for Medieval Overhaul integration settings.
        /// Called by IntegrationConfiguration.RenderUI().
        /// </summary>
        public void RenderIntegrationUI(Listing_Standard list)
        {
            if (!IsActive) return;
            
            list.Gap(12f);
            Text.Font = GameFont.Medium;
            list.Label("Medieval Overhaul");
            Text.Font = GameFont.Small;
            list.Gap(6f);
            
            bool creatures = AllowCreaturesValue;
            bool plants = AllowPlantsValue;
            list.CheckboxLabeled(
                "Allow Medieval Overhaul creatures in new biomes",
                ref creatures,
                "When enabled, Medieval Overhaul animals will spawn in Realistic Planets custom biomes (Savanna, Steppe, Oasis, Prairie, Permafrost). Requires a restart."
            );
            list.CheckboxLabeled(
                "Allow Medieval Overhaul plants in new biomes",
                ref plants,
                "When enabled, Medieval Overhaul plants will grow in Realistic Planets custom biomes (Savanna, Steppe, Oasis, Prairie, Permafrost). Requires a restart."
            );
            AllowCreaturesValue = creatures;
            AllowPlantsValue = plants;
        }
        
        /// <summary>
        /// Save/load settings via Scribe.
        /// Called by IntegrationConfiguration.ExposeData().
        /// </summary>
        public void ExposeData()
        {
            // medievalCreaturesInNewBiomes and medievalPlantsInNewBiomes are scribed by IntegrationConfiguration.
        }
        
        /// <summary>
        /// Reset settings to default values.
        /// Called by IntegrationConfiguration.ResetToDefaults().
        /// </summary>
        public void ResetToDefaults()
        {
            AllowCreaturesValue = true;
            AllowPlantsValue = true;
        }

        // Biome worker redirect now handled by BiomeCompatWorkerRedirect (worker-type-based)
    }
}
