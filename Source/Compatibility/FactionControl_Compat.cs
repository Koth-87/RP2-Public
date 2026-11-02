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
    /// Faction Control compatibility module.
    /// Integrates FC's settings window button into RP2's world creation page.
    /// FC manages faction spawning entirely, so we hide vanilla's population slider.
    /// </summary>
    [StaticConstructorOnStartup]
    public class FactionControlCompat : ICompatibilityModule
    {
        private static FactionControlCompat _instance;
        public static FactionControlCompat Instance => _instance ?? (_instance = new FactionControlCompat());

        public static bool IsEnabled => Instance.IsActive;

        static FactionControlCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "thereallemon.factioncontrol";
        public string ModName => "Faction Control";
        public bool IsActive { get; private set; }
        
        public bool HasWorldCreationButton => true;
        public string ButtonLabel => "Faction Control"; // Hardcoded name, FC doesn't expose a key for this typically

        // Cached type
        private Type settingsWindowType;
        
        public void Initialize()
        {
            try
            {
                // Check if Faction Control is active
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive) return;
                
                // Get the FactionControl assembly
                var fcAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "FactionControl");
                
                if (fcAssembly == null)
                {
                    RPLog.Warning($"[Compat] {ModName} detected but assembly not found");
                    IsActive = false;
                    return;
                }
                
                // Get the SettingsWindow type
                settingsWindowType = fcAssembly.GetType("FactionControl.SettingsWindow");
                
                if (settingsWindowType == null)
                {
                    RPLog.Warning($"[Compat] {ModName}: SettingsWindow type not found");
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
            // No RP-side settings needed - FC has its own settings window
        }

        public void ExposeData()
        {
            // No persistent settings needed
        }

        public void ResetToDefaults()
        {
            // No settings to reset
        }

        public void OnWorldCreationButtonClicked(List<FactionDef> factions)
        {
            ToggleSettingsWindow();
        }

        /// <summary>
        /// Toggles the Faction Control settings window.
        /// If the window is open, closes it. If closed, opens it.
        /// </summary>
        public static void ToggleSettingsWindow()
        {
            Instance.ToggleSettingsWindowInternal();
        }

        private void ToggleSettingsWindowInternal()
        {
            if (!IsActive || settingsWindowType == null)
                return;
            
            try
            {
                // Check if FC settings window is already open
                var existingWindow = Find.WindowStack.Windows
                    .FirstOrDefault(w => w.GetType() == settingsWindowType);
                
                if (existingWindow != null)
                {
                    // Window is open - close it
                    Find.WindowStack.TryRemove(existingWindow);
                    
                    if (Controller.DebugEnabled)
                        RPLog.D($"[Compat] Closed {ModName} settings window");
                }
                else
                {
                    // Window is closed - open it
                    var window = Activator.CreateInstance(settingsWindowType) as Window;
                    
                    if (window != null)
                    {
                        Find.WindowStack.Add(window);
                        
                        if (Controller.DebugEnabled)
                            RPLog.D($"[Compat] Opened {ModName} settings window");
                    }
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to toggle {ModName} settings window: {e}");
            }
        }
    }
}
