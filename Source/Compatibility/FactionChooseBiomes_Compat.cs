using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Faction Choose Biomes compatibility module.
    /// Integrates FCB's settings window button into RP2's world creation page.
    /// </summary>
    [StaticConstructorOnStartup]
    public class FactionChooseBiomesCompat : ICompatibilityModule
    {
        private static FactionChooseBiomesCompat _instance;
        public static FactionChooseBiomesCompat Instance => _instance ?? (_instance = new FactionChooseBiomesCompat());
        
        public static bool IsEnabled => Instance.IsActive;

        static FactionChooseBiomesCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "FCB.FactionChooseBiomes";
        public string ModName => "Faction Choose Biomes";
        public bool IsActive { get; private set; }
        
        public bool HasWorldCreationButton => true;
        public string ButtonLabel => "MF.OpenFactionChooseBiome".Translate();

        // Cached type
        private Type dialogType;
        
        public void Initialize()
        {
            try
            {
                // Check if Faction Choose Biomes is active
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive) return;
                
                // Get the FactionsWithBiome assembly
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "FactionsWithBiome");
                
                if (assembly == null)
                {
                    RPLog.Warning($"[Compat] {ModName} detected but assembly 'FactionsWithBiome' not found");
                    IsActive = false;
                    return;
                }
                
                // Get the Dialog_SettingForFactionBiome type
                dialogType = assembly.GetType("FactionsWithBiome.Dialog_SettingForFactionBiome");
                
                if (dialogType == null)
                {
                    RPLog.Warning($"[Compat] {ModName}: Dialog_SettingForFactionBiome type not found");
                    IsActive = false;
                    return;
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to initialize {ModName} compatibility: {e}");
                IsActive = false;
            }
        }

        public void RenderIntegrationUI(Listing_Standard list)
        {
            // No Integration tab UI for this mod
        }

        public void ExposeData()
        {
            // No persistent data to save
        }

        public void ResetToDefaults()
        {
            // No defaults to reset
        }

        public void OnWorldCreationButtonClicked(List<FactionDef> factions)
        {
            OpenSettingsWindow(factions);
        }
        
        /// <summary>
        /// Opens the Faction Choose Biomes settings window.
        /// Public static for backward compatibility if needed, but primarily used via interface.
        /// </summary>
        public static void OpenSettingsWindow(List<FactionDef> factions)
        {
            Instance.OpenSettingsWindowInternal(factions);
        }

        private void OpenSettingsWindowInternal(List<FactionDef> factions)
        {
            if (!IsActive || dialogType == null)
                return;
            
            try
            {
                // Dialog_SettingForFactionBiome constructor takes List<FactionDef>
                var window = Activator.CreateInstance(dialogType, factions) as Window;
                
                if (window != null)
                {
                    Find.WindowStack.Add(window);
                    
                    if (Controller.DebugEnabled)
                        RPLog.D($"[Compat] Opened {ModName} settings window");
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to open {ModName} settings window: {e}");
            }
        }
    }
}
