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
    /// VFE Insectoids 2 compatibility module.
    /// Integrates with VFE Insectoids 2's territory scale system.
    /// </summary>
    [StaticConstructorOnStartup]
    public class VFEInsectoidsCompat : ICompatibilityModule
    {
        private static VFEInsectoidsCompat _instance;
        public static VFEInsectoidsCompat Instance => _instance ?? (_instance = new VFEInsectoidsCompat());

        public static bool IsEnabled => Instance.IsActive;

        static VFEInsectoidsCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "OskarPotocki.VFE.Insectoid2";
        public string ModName => "VFE Insectoids 2";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }
        public void RenderIntegrationUI(Listing_Standard list) { }
        public void ExposeData() { }
        public void ResetToDefaults() { }

        // Cached reflection members
        private Type gameComponentType;
        private FieldInfo instanceField;
        private FieldInfo territoryScaleField;
        
        public void Initialize()
        {
            try
            {
                // Check if VFE Insectoids 2 is active
                // Package ID is case-insensitive
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.D("[Compat] VFE Insectoids 2 not detected");
                    return;
                }
                
                RPLog.Message("[Compat] VFE Insectoids 2 detected, initializing compatibility");
                
                // Get the GameComponent type
                gameComponentType = GetTypeFromAnyAssembly("VFEInsectoids.GameComponent_Insectoids");
                
                if (gameComponentType == null)
                {
                    RPLog.Warning("[Compat] VFE Insectoids 2 detected but GameComponent_Insectoids type not found");
                    IsActive = false;
                    return;
                }
                
                // Cache the Instance static field
                instanceField = gameComponentType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceField == null)
                {
                    RPLog.Warning("[Compat] VFE Insectoids 2: Instance field not found");
                    IsActive = false;
                    return;
                }
                
                // Cache the insectTerritoryScale instance field (CORRECT field name)
                territoryScaleField = gameComponentType.GetField("insectTerritoryScale", BindingFlags.Public | BindingFlags.Instance);
                if (territoryScaleField == null)
                {
                    RPLog.Warning("[Compat] VFE Insectoids 2: insectTerritoryScale field not found");
                    IsActive = false;
                    return;
                }
                
                RPLog.Message("[Compat] VFE Insectoids 2 compatibility initialized successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to initialize VFE Insectoids 2 compatibility: {e}");
                IsActive = false;
            }
        }
        
        /// <summary>
        /// Get the GameComponent type for VFE Insectoids.
        /// Used by Page_CreateWorldParamsRP for component lookup.
        /// </summary>
        public static Type GetGameComponentType()
        {
            return Instance.gameComponentType;
        }
        
        /// <summary>
        /// Set the insect territory scale value on the game component.
        /// Call this when world generation starts.
        /// </summary>
        public static void SetInsectTerritoryScale(float scale)
        {
            Instance.SetInsectTerritoryScaleInternal(scale);
        }

        private void SetInsectTerritoryScaleInternal(float scale)
        {
            if (!IsActive || instanceField == null || territoryScaleField == null)
                return;
            
            try
            {
                // Get the singleton instance
                var instance = instanceField.GetValue(null);
                
                if (instance == null)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Warning("[Compat] VFE Insectoids 2: GameComponent.Instance is null (game not started yet?)");
                    return;
                }
                
                // Set the territory scale
                territoryScaleField.SetValue(instance, scale);
                
                if (Controller.DebugEnabled)
                    RPLog.D($"[Compat] Set VFE Insectoids 2 territory scale to {scale:F2}");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to set VFE Insectoids 2 territory scale: {e}");
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
