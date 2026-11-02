using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using RimWorld;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Compatibility module for Alpha Biomes mod.
    /// Syncs Advanced Biomes dialog sliders with Alpha Biomes mod settings (calibrated biome workers).
    /// </summary>
    [StaticConstructorOnStartup]
    public class AlphaBiomesCompat : ICompatibilityModule
    {
        private static AlphaBiomesCompat _instance;
        public static AlphaBiomesCompat Instance => _instance ?? (_instance = new AlphaBiomesCompat());
        
        public static bool IsEnabled => Instance.IsActive;

        static AlphaBiomesCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "sarg.alphabiomes";
        public string ModName => "Alpha Biomes";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }

        // ========== INTERNAL STATE ==========
        private static Type settingsType = null;
        
        // ========== CONSTANTS ==========
        private const string SETTINGS_TYPE_NAME = "AlphaBiomes.AlphaBiomes_Settings";
        
        // Field names for Alpha Biomes multiplier sliders (used to sync Advanced Biomes dialog with AB mod settings)
        private static readonly string[] MultiplierFieldNames = new[]
        {
            "feraliskInfestedJungleMultiplier", "rockyCragsMultiplier", "gallatrossGraveyardMultiplier",
            "gelatinousSuperorganismMultiplier", "idyllicMeadowsMultiplier", "mechanoidIntrusionMultiplier",
            "miasmicMangroveMultiplier", "mycoticJungleMultiplier", "ocularForestMultiplier",
            "propaneLakesMultiplier", "pyroclasticConflagrationMultiplier", "tarPitsMultiplier"
        };
        
        // ========== INITIALIZATION ==========
        
        public void Initialize()
        {
            try
            {
                // Check if Alpha Biomes is active
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Debug("Alpha Biomes not detected");
                    return;
                }
                
                RPLog.Debug("Alpha Biomes detected, initializing compatibility");
                
                // Initialize Alpha Biomes types
                if (!InitializeTypes())
                {
                    RPLog.Warning("Alpha Biomes detected but initialization failed");
                    IsActive = false;
                    return;
                }
                
                RPLog.Debug("Alpha Biomes compatibility initialized successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"Error initializing Alpha Biomes compatibility: {e}");
                IsActive = false;
            }
        }
        
        /// <summary>
        /// Find and cache Alpha Biomes types via reflection
        /// </summary>
        private static bool InitializeTypes()
        {
            try
            {
                // Find Alpha Biomes assembly
                var abAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.FullName.Contains("AlphaBiomes"));
                    
                if (abAssembly == null)
                {
                    RPLog.Warning("Could not find Alpha Biomes assembly");
                    return false;
                }
                
                settingsType = abAssembly.GetType(SETTINGS_TYPE_NAME);
                if (settingsType == null)
                {
                    RPLog.Warning("Could not find Alpha Biomes settings type");
                    return false;
                }
                
                return true;
            }
            catch (Exception e)
            {
                RPLog.Error($"Error finding Alpha Biomes types: {e}");
                return false;
            }
        }
        
        // ========== PUBLIC API METHODS ==========
        
        /// <summary>
        /// Get the current values of all Alpha Biomes multipliers (raw field names as keys).
        /// </summary>
        public static Dictionary<string, float> GetCurrentValues()
        {
            return Instance.GetCurrentValuesInternal();
        }
        
        /// <summary>
        /// Get the current Alpha Biomes slider values keyed by BiomeDef.defName so sliders in our dialog reflect AB settings.
        /// Keys are derived from field names (e.g. "feraliskInfestedJungleMultiplier" -> "FeraliskInfestedJungle").
        /// </summary>
        public static Dictionary<string, float> GetCurrentValuesKeyedByDefName()
        {
            return Instance.GetCurrentValuesKeyedByDefNameInternal();
        }

        private static string FieldNameToDefName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return fieldName;
            if (fieldName.EndsWith("Multiplier"))
                fieldName = fieldName.Substring(0, fieldName.Length - "Multiplier".Length);
            if (fieldName.Length == 0) return fieldName;
            return char.ToUpperInvariant(fieldName[0]) + fieldName.Substring(1);
        }

        private Dictionary<string, float> GetCurrentValuesInternal()
        {
            var result = new Dictionary<string, float>();
            
            if (!IsActive || settingsType == null)
                return result;
                
            try
            {
                foreach (string fieldName in MultiplierFieldNames)
                {
                    var field = settingsType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                    if (field != null)
                    {
                        result[fieldName] = (float)field.GetValue(null);
                    }
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"Error getting Alpha Biomes values: {e}");
            }
            
            return result;
        }
        
        private Dictionary<string, float> GetCurrentValuesKeyedByDefNameInternal()
        {
            var raw = GetCurrentValuesInternal();
            var result = new Dictionary<string, float>(raw.Count);
            foreach (var kvp in raw)
            {
                string defName = FieldNameToDefName(kvp.Key);
                result[defName] = kvp.Value;
            }
            return result;
        }
        
        /// <summary>
        /// Get the Alpha Biomes settings type for external use
        /// </summary>
        public static Type GetSettingsType() => settingsType;
        
        // ========== INTEGRATION CONFIGURATION METHODS ==========
        
        /// <summary>
        /// Render UI for Alpha Biomes integration settings.
        /// Called by IntegrationConfiguration.RenderUI().
        /// </summary>
        public void RenderIntegrationUI(Listing_Standard list)
        {
            // No-op: Alpha Biomes has calibrated biome workers; sliders in Advanced Biomes sync with AB mod settings.
        }
        
        /// <summary>
        /// Save/load settings via Scribe.
        /// Called by IntegrationConfiguration.ExposeData().
        /// </summary>
        public void ExposeData() { }
        
        /// <summary>
        /// Reset settings to default values.
        /// Called by IntegrationConfiguration.ResetToDefaults().
        /// </summary>
        public void ResetToDefaults()
        {
            // No-op: we don't modify Alpha Biomes settings from RP2.
        }
        
    }
}
