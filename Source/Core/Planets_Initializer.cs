using System;
using System.Linq;
using Verse;
using Planets.Presets.Biomes;
using Planets.WorldGen.Astronomy;
using Planets.Utilities.Logging;
using Planets.Compatibility;
using MapModeFramework;

namespace Planets.Core
{
    /// <summary>
    /// Coordinates initialization of all Realistic Planets systems at startup.
    /// Runs via [StaticConstructorOnStartup] before main menu, ensuring proper initialization order.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Planets_Initializer
    {
        private static bool initialized = false;
        
        static Planets_Initializer()
        {
            try
            {
                InitializeCoreSystems();
                
                // Compatibility modules are initialized automatically via [StaticConstructorOnStartup]
                // We access their properties in VerifyCompatibilityModules to verify status
                VerifyCompatibilityModules();
                
                initialized = true;
                
                if (Controller.DebugEnabled)
                {
                    RPLog.Init("All systems initialized successfully");
                    LogInitializationStatus();
                }
            }
            catch (Exception e)
            {
                RPLog.E($"Critical error during initialization: {e}");
            }
        }
        
        /// <summary>
        /// Initialize core systems that must be ready before anything else.
        /// </summary>
        private static void InitializeCoreSystems()
        {
            // Initialize Map Mode Framework (MMF)
            MapModeFramework.Core.Initialize();

            // Temperature curve is initialized via AxialTiltCurves static constructor with defaults
            // Just verify it's ready
            if (AxialTiltCurves.SeasonalTempVariationCurve == null)
            {
                RPLog.W("Temperature curve was null, initializing with defaults");
                AxialTiltCurves.SetSeasonalCurve();
            }

            // Climate system will be initialized during world generation
            // Not here at the main menu where Find.WorldGrid doesn't exist yet

            BiomePresetManager.MigrateSharedFolder();
        }
        
        /// <summary>
        /// Verify that compatibility modules have initialized.
        /// </summary>
        private static void VerifyCompatibilityModules()
        {
            // Verify and initialize all registered compatibility modules
            ModCompatibilityRegistry.VerifyAndInitialize();
            Planets.Api.RealisticPlanetsApi.LogContract();
        }
        
        /// <summary>
        /// Verify all systems are ready before world generation.
        /// Called from CreateWorldParams UI before starting world gen.
        /// </summary>
        public static bool VerifyReadyForWorldGen()
        {
            bool ready = true;
            
            // Check temperature curve
            if (AxialTiltCurves.SeasonalTempVariationCurve == null)
            {
                RPLog.E("Temperature curve not initialized!");
                AxialTiltCurves.SetSeasonalCurve();
                ready = false;
            }
            
            if (!ready)
            {
                RPLog.W("Had to fix initialization issues before world generation");
            }
            
            return true; // Always return true, we've fixed any issues
        }
        
        /// <summary>
        /// Log detailed initialization status for debugging.
        /// </summary>
        private static void LogInitializationStatus()
        {
            RPLog.Init("Initialization Status:");
            RPLog.Init($"  - Temperature Curve: {(AxialTiltCurves.SeasonalTempVariationCurve != null ? "Ready" : "NULL")}");
            RPLog.Init($"  - Axial Tilt: {Planets_GameComponent.axialTilt}");
            RPLog.Init($"  - Sea Level: {Planets_GameComponent.seaLevel}");
            RPLog.Init($"  - World Preset: {Planets_GameComponent.worldPreset}");
            
            // TODO: Check GeologicalLandforms integration when compat modules are migrated
            // if (GeologicalLandformsCompat.IsActive && GeologicalLandformsCompat.IsMountainCaveIntegrationActive())
            // {
            //     RPLog.Init($"  - GL Mountain/Cave Integration: Active");
            // }
        }
        
        /// <summary>
        /// Check if initialization completed successfully.
        /// </summary>
        public static bool IsInitialized => initialized;
    }
}
