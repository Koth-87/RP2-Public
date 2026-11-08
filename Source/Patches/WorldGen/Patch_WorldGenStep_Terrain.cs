using System;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;
using Planets.Biomes.Core;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.WorldGen;
using Planets.WorldGen.Geography;

namespace Planets.Patches.WorldGen
{
    /// <summary>
    /// Thin Harmony patch that intercepts vanilla WorldGenStep_Terrain and delegates to
    /// WorldGenerationOrchestrator for complete terrain/climate generation.
    /// 
    /// **Intervention Point**: WorldGenStep_Terrain.GenerateFresh (SeedPart: 83469557)
    /// 
    /// **What RP Replaces**:
    /// - Vanilla noise-based temperature/rainfall/hilliness/swampiness
    /// - Random biome selection
    /// - Entire per-tile generation loop
    /// 
    /// **What RP Preserves**:
    /// - Vanilla elevation noise (continent shapes)
    /// - WorldGenStep pipeline structure
    /// - Returns control to vanilla for roads, settlements, features
    /// 
    /// **Error Handling**:
    /// - Returns false (skip vanilla) on success
    /// - Returns true (run vanilla) on any failure
    /// - Comprehensive logging for debugging
    /// 
    /// **Design**: This is a THIN layer containing NO business logic.
    /// All world generation logic lives in WorldGenerationOrchestrator.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenStep_Terrain), "GenerateFresh")]
    public static class Patch_WorldGenStep_Terrain
    {
        /// <summary>True after RP terrain generation succeeded for the layer currently being built. Lakes skips vanilla only then.</summary>
        public static bool rpTerrainSucceeded;

        /// <summary>
        /// Harmony Prefix that completely replaces vanilla terrain generation.
        /// Returns false to skip vanilla, true to let vanilla run (fallback on error).
        /// </summary>
        public static bool Prefix(WorldGenStep_Terrain __instance, string seed, PlanetLayer layer)
        {
            rpTerrainSucceeded = false;
            WorldGenerationState state = null;
            try
            {
                // Start timer for elapsed time tracking
                RPLog.StartTimer();
                
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] RP intercepting WorldGenStep_Terrain.GenerateFresh");
                
                // Step 1: Extract vanilla elevation noise (preserves continent shapes)
                ModuleBase elevationNoise = ExtractVanillaElevationNoise(layer);
                if (elevationNoise == null)
                {
                    RPLog.Error("Failed to extract vanilla elevation noise - falling back to vanilla");
                    return true; // Let vanilla run
                }
                
                // Step 2: Get configuration from Controller.Settings
                var settings = Controller.Settings;
                var climateConfig = settings?.climateConfig ?? new Core.Configuration.ClimateConfiguration();
                var hydroConfig = settings?.hydrologyConfig ?? new Core.Configuration.HydrologyConfiguration();
                var features = settings?.featureConfig ?? new Core.Configuration.FeatureConfiguration();
                
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] Configuration loaded from Controller.Settings");
                
                // Step 3: Create WorldGenerationState (validates all inputs)
                try
                {
                    state = new WorldGenerationState(
                        layer,
                        elevationNoise,
                        climateConfig,
                        hydroConfig,
                        features
                    );
                }
                catch (Exception e)
                {
                    RPLog.Error($"Failed to create WorldGenerationState: {e.Message} - falling back to vanilla");
                    return true; // Let vanilla run
                }
                
                ModuleBase elevationAdapter = null;
                ModuleBase hillinessAdapter = null;
                ModuleBase temperatureOffsetAdapter = null;
                ModuleBase rainfallAdapter = null;
                ModuleBase swampinessAdapter = null;
                ModuleBase hillsPatchesAdapter = null;

                // Step 3.5: Initialize GL translation layer if active
                if (Compatibility.GeologicalLandformsCompat.IsEnabled)
                {
                    try
                    {
                        // Disable GL's Mountains slider (users use RP2's Hilliness Bias instead)
                        Compatibility.GeologicalLandformsCompat.DisableMountainSlider();
                        
                        // Create translation layer adapters
                        elevationAdapter = new Compatibility.GeologicalLandforms.RP2ElevationAdapter();
                        hillinessAdapter = new Compatibility.GeologicalLandforms.RP2HillinessAdapter();
                        temperatureOffsetAdapter = new Compatibility.GeologicalLandforms.RP2TemperatureOffsetAdapter();
                        rainfallAdapter = new Compatibility.GeologicalLandforms.RP2RainfallAdapter();
                        swampinessAdapter = new Compatibility.GeologicalLandforms.RP2SwampinessAdapter();
                        hillsPatchesAdapter = new Compatibility.GeologicalLandforms.RP2HillsPatchesAdapter();
                        
                        // Inject adapters into instance so GL's postfixes can sample them
                        var noiseElevationField = typeof(WorldGenStep_Terrain).GetField("noiseElevation", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var noiseMountainLinesField = typeof(WorldGenStep_Terrain).GetField("noiseMountainLines", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var noiseTemperatureOffsetField = typeof(WorldGenStep_Terrain).GetField("noiseTemperatureOffset", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var noiseRainfallField = typeof(WorldGenStep_Terrain).GetField("noiseRainfall", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var noiseSwampinessField = typeof(WorldGenStep_Terrain).GetField("noiseSwampiness",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var noiseHillsPatchesMicroField = typeof(WorldGenStep_Terrain).GetField("noiseHillsPatchesMicro",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var noiseHillsPatchesMacroField = typeof(WorldGenStep_Terrain).GetField("noiseHillsPatchesMacro",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        // Verify fields exist and inject adapters
                        if (noiseElevationField == null || noiseMountainLinesField == null || 
                            noiseTemperatureOffsetField == null || noiseRainfallField == null ||
                            noiseSwampinessField == null || noiseHillsPatchesMicroField == null ||
                            noiseHillsPatchesMacroField == null)
                        {
                            RPLog.Warning($"[WorldGen] GL noise fields not all found - " +
                                $"elevation:{noiseElevationField != null}, " +
                                $"mountains:{noiseMountainLinesField != null}, " +
                                $"temp:{noiseTemperatureOffsetField != null}, " +
                                $"rainfall:{noiseRainfallField != null}, " +
                                $"swampiness:{noiseSwampinessField != null}, " +
                                $"hillsMicro:{noiseHillsPatchesMicroField != null}, " +
                                $"hillsMacro:{noiseHillsPatchesMacroField != null}");
                        }
                        
                        noiseElevationField?.SetValue(__instance, elevationAdapter);
                        noiseMountainLinesField?.SetValue(__instance, hillinessAdapter);
                        noiseTemperatureOffsetField?.SetValue(__instance, temperatureOffsetAdapter);
                        noiseRainfallField?.SetValue(__instance, rainfallAdapter);
                        noiseSwampinessField?.SetValue(__instance, swampinessAdapter);
                        noiseHillsPatchesMicroField?.SetValue(__instance, hillsPatchesAdapter);
                        noiseHillsPatchesMacroField?.SetValue(__instance, hillsPatchesAdapter);
                        
                        if (Controller.DebugEnabled)
                            RPLog.D("[WorldGen] Injected GL translation layer adapters (elevation, hilliness, temperature, rainfall, swampiness, hills patches)");
                    }
                    catch (Exception e)
                    {
                        RPLog.Error($"Failed to initialize GL translation layer: {e}");
                        // Continue anyway - GL features just won't work properly
                    }
                }
                
                // Step 4: Create orchestrator and execute 3-phase pipeline.
                // Cave depth must be ready before biome selection, so the callback runs
                // after climate is written onto the tiles and before workers are scored.
                var orchestrator = new WorldGenerationOrchestrator(state);
                if (Compatibility.GeologicalLandformsCompat.IsEnabled)
                {
                    ModuleBase caveHilliness = hillinessAdapter;
                    ModuleBase caveElevation = elevationAdapter;
                    orchestrator.BeforeBiomeSelection = () =>
                    {
                        try
                        {
                            Compatibility.GeologicalLandformsCompat.BuildSpatialIndex();
                            if (Controller.DebugEnabled)
                                RPLog.D("[WorldGen] GL spatial index ready for cave systems and feature generation");
                        }
                        catch (Exception e)
                        {
                            RPLog.Error($"Failed to build GL spatial index: {e}");
                        }

                        try
                        {
                            Compatibility.GeologicalLandformsCompat.TryCalculateCaveSystems(caveHilliness, caveElevation);
                        }
                        catch (Exception e)
                        {
                            RPLog.Error($"Failed to calculate GL cave systems: {e}");
                        }
                    };
                }
                bool success = orchestrator.Execute();
                
                if (!success)
                {
                    RPLog.Error("RP world generation failed - falling back to vanilla");
                    AbandonClimate(state);
                    return true; // Let vanilla run
                }
                
                // Step 5: Success! Skip vanilla terrain generation
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] RP terrain generation complete - skipping vanilla");

                // Keep this world's density overrides on the game, not on the shared biome defs.
                Current.Game?.GetComponent<Planets_GameComponent>()?.RememberDefOverrides(
                    BiomeWorkerManager.CaptureOverrides(Controller.Settings?.biomeConfig));

                rpTerrainSucceeded = true;
                return false; // Skip vanilla
            }
            catch (Exception e)
            {
                Exception reported = e.InnerException ?? e;
                RPLog.Error($"RP patch crashed: {reported}\nFalling back to vanilla");
                AbandonClimate(state);
                return true; // Let vanilla run
            }
        }

        /// <summary>Null the climate arrays so workers do not score a failed generation from zeros.</summary>
        private static void AbandonClimate(WorldGenerationState state)
        {
            state?.ClimateData?.ClearAllArrays();
        }
        
        /// <summary>
        /// Extract vanilla elevation noise generator by calling vanilla's setup method.
        /// Isolates RNG state to preserve determinism.
        /// 
        /// CRITICAL: Must call ApplySeaLevelElevationRange() BEFORE SetupElevationNoise()
        /// to configure vanilla's noise module to output values in the correct range.
        /// 
        /// This preserves vanilla's continent shapes while allowing RP to compute
        /// all other climate parameters.
        /// 
        /// Implementation: Uses reflection to call private SetupElevationNoise method
        /// and extract the noiseElevation field.
        /// </summary>
        private static ModuleBase ExtractVanillaElevationNoise(PlanetLayer layer)
        {
            bool pushed = false;
            bool rangeOverridden = false;
            FloatRange originalRange = default;
            try
            {
                // STEP 1: Apply sea level elevation range BEFORE setup
                // This configures vanilla's noise module to output values in the target range
                rangeOverridden = ApplySeaLevelElevationRange(out originalRange);
                
                // Push RNG state to isolate vanilla noise generation
                Rand.PushState();
                pushed = true;
                
                // Create temporary WorldGenStep_Terrain instance for vanilla noise setup
                var terrainStep = new WorldGenStep_Terrain();
                
                // STEP 2: Call vanilla's SetupElevationNoise
                // This initializes the elevation noise generator using vanilla's algorithm
                // with the ElevationRange we just configured
                
                // Get private SetupElevationNoise method via reflection
                var setupMethod = typeof(WorldGenStep_Terrain).GetMethod(
                    "SetupElevationNoise",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                );
                
                if (setupMethod == null)
                {
                    RPLog.Error("Could not find SetupElevationNoise method via reflection");
                    return null;
                }
                
                // Call the setup method
                setupMethod.Invoke(terrainStep, new object[] { layer });
                
                // Get private noiseElevation field via reflection
                var noiseField = typeof(WorldGenStep_Terrain).GetField(
                    "noiseElevation",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                );
                
                if (noiseField == null)
                {
                    RPLog.Error("Could not find noiseElevation field via reflection");
                    return null;
                }
                
                // Extract the noise generator
                var elevationNoise = noiseField.GetValue(terrainStep) as ModuleBase;
                
                if (elevationNoise == null)
                {
                    RPLog.Error("Extracted elevation noise is null");
                    return null;
                }
                
                if (Controller.DebugEnabled)
                    RPLog.D("[WorldGen] Successfully extracted vanilla elevation noise");
                
                return elevationNoise;
            }
            catch (Exception e)
            {
                Exception reported = e.InnerException ?? e;
                RPLog.Error($"Failed to extract vanilla elevation noise: {reported}");
                return null;
            }
            finally
            {
                if (pushed)
                    Rand.PopState();
                if (rangeOverridden)
                    RestoreElevationRange(originalRange);
            }
        }
        
        /// <summary>
        /// Apply sea level-based elevation range adjustments.
        /// A floor of 0 or above produces no water tiles and breaks river generation and the ocean-distance search. The floor stays negative.
        /// MUST be called BEFORE SetupElevationNoise() to affect the noise generation.
        /// 
        /// This sets the static ElevationRange field on WorldGenStep_Terrain, which
        /// vanilla's SetupElevationNoise() uses to configure the noise module's output range.
        /// </summary>
        private static bool ApplySeaLevelElevationRange(out FloatRange originalRange)
        {
            originalRange = default;
            FloatRange newRange;
            
            switch (Planets_GameComponent.seaLevel)
            {
                case SeaLevel.High:  // Water world.
                    newRange = new FloatRange(-2000f, 9000f);
                    break;

                case SeaLevel.EarthLike:
                    newRange = new FloatRange(-1500f, 9000f);
                    break;

                case SeaLevel.SlightlyHigh:
                    newRange = new FloatRange(-1200f, 9000f);
                    break;
                    
                case SeaLevel.Normal:  // Vanilla-like
                    newRange = new FloatRange(-900f, 9000f);
                    break;
                    
                case SeaLevel.Low:
                    newRange = new FloatRange(-600f, 9000f);
                    break;
                    
                default:
                    // Fallback to Normal
                    newRange = new FloatRange(-900f, 9000f);
                    break;
            }
            
            // Use HarmonyLib AccessTools to set the static ElevationRange field
            var elevationRangeField = AccessTools.Field(typeof(WorldGenStep_Terrain), "ElevationRange");
            if (elevationRangeField == null)
            {
                RPLog.Warning("Could not find ElevationRange field via reflection - sea level adjustments may not work");
                return false;
            }

            if (elevationRangeField.GetValue(null) is FloatRange current)
                originalRange = current;

            elevationRangeField.SetValue(null, newRange);
            if (!(elevationRangeField.GetValue(null) is FloatRange applied)
                || applied.min != newRange.min
                || applied.max != newRange.max)
            {
                RPLog.Warning($"ElevationRange write did not stick. Wanted {newRange.min}m to {newRange.max}m.");
            }

            if (Controller.DebugEnabled)
                RPLog.D($"[WorldGen] Set ElevationRange for {Planets_GameComponent.seaLevel}: {newRange.min}m to {newRange.max}m");
            return true;
        }

        private static void RestoreElevationRange(FloatRange originalRange)
        {
            var elevationRangeField = AccessTools.Field(typeof(WorldGenStep_Terrain), "ElevationRange");
            elevationRangeField?.SetValue(null, originalRange);
        }
    }
}
