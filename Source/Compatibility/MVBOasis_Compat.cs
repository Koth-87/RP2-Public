using System;
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
    /// More Vanilla Biomes Oasis compatibility module. Detects MVB, reads its spawnDesertOasis
    /// setting via reflection, and exposes MVBOasisEnabled. BiomeConfiguration uses this to
    /// enable ZBiome_DesertOasis only when MVB's oasis is on, and to lock RP_Oasis when
    /// MVB Oasis is present and enabled. Worker replacement is handled by BiomeCompatWorkerRedirect.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MVBOasisCompat : ICompatibilityModule
    {
        private static MVBOasisCompat _instance;
        public static MVBOasisCompat Instance => _instance ?? (_instance = new MVBOasisCompat());

        public static bool IsEnabled => Instance.IsActive;

        static MVBOasisCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "zylle.MoreVanillaBiomes";
        public string ModName => "More Vanilla Biomes";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }
        public void RenderIntegrationUI(Listing_Standard list) { }
        public void ExposeData() { }
        public void ResetToDefaults() { }

        // Public API: read MVB setting dynamically so dialog and Init() always reflect current MVB settings
        public static bool MVBOasisEnabled => Instance.GetMVBOasisEnabled();
        
        // Cached reflection for dynamic read
        private static Type cachedSettingsType;
        private static FieldInfo cachedSpawnOasisField;
        
        public void Initialize()
        {
            try
            {
                // Check if More Vanilla Biomes is active
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.D("[Compat] More Vanilla Biomes not detected");
                    return;
                }
                
                RPLog.Message("[Compat] More Vanilla Biomes detected, initializing oasis compatibility");
                
                // Get MVB's settings type (cache for dynamic read)
                cachedSettingsType = GetTypeFromAnyAssembly("VanillaBiomes.BiomeSettings");
                if (cachedSettingsType == null)
                {
                    RPLog.Warning("[Compat] MVB detected but BiomeSettings type not found");
                    IsActive = false;
                    return;
                }
                
                cachedSpawnOasisField = cachedSettingsType.GetField("spawnDesertOasis",
                    BindingFlags.Public | BindingFlags.Static);
                
                if (cachedSpawnOasisField == null)
                {
                    RPLog.Warning("[Compat] MVB detected but spawnDesertOasis field not found");
                    IsActive = false;
                    return;
                }
                
                // Get MVB's oasis biome def
                var oasisDef = DefDatabase<BiomeDef>.GetNamed("ZBiome_DesertOasis", false);
                if (oasisDef == null)
                {
                    RPLog.Warning("[Compat] MVB detected but ZBiome_DesertOasis not found");
                    IsActive = false;
                    return;
                }
                
                if (GetMVBOasisEnabled())
                {
                    RPLog.Message("[Compat] MVB Oasis integration: MVB Oasis enabled (RP2 Oasis will be disabled)");
                }
                else
                {
                    RPLog.Message("[Compat] MVB Oasis integration: MVB Oasis disabled (RP2 Oasis available)");
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to initialize MVB Oasis compatibility: {e}");
                IsActive = false;
            }
        }
        
        /// <summary>
        /// Read MVB's spawnDesertOasis setting dynamically so our Biomes dialog and Init() always reflect current MVB mod settings.
        /// </summary>
        private bool GetMVBOasisEnabled()
        {
            if (!IsActive || cachedSpawnOasisField == null) return false;
            try
            {
                return (bool)cachedSpawnOasisField.GetValue(null);
            }
            catch
            {
                return false;
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
    }
}
