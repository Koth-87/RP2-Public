using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Planets.Core;
using Planets.UI.Pages;
using Planets.Utilities.Logging;
using Planets.WorldGen.Geography;
using UnityEngine;

namespace Planets.Compatibility.FernyMods
{
    /// <summary>
    /// Compatibility handler for Ferny's Worldbuilder mod.
    /// Manages companion data storage for RP2-specific settings in Worldbuilder presets.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WorldbuilderCompat : ICompatibilityModule
    {
        private static WorldbuilderCompat _instance;
        public static WorldbuilderCompat Instance => _instance ?? (_instance = new WorldbuilderCompat());

        public static bool IsEnabled => Instance.IsActive;

        static WorldbuilderCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "ferny.worldbuilder";
        public string ModName => "Worldbuilder";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }
        public void RenderIntegrationUI(Listing_Standard list) { }
        public void ExposeData() { }
        public void ResetToDefaults() { }

        // Cached types from Worldbuilder
        private Type worldPresetManagerType;
        private Type worldPresetType;
        private Type wbPatchType;
        private Type worldExposeDataPatchType;
        private MethodInfo handlePreviewMethod;
        private MethodInfo drawPreviewMethod;
        private MethodInfo updateCurPresetMethod;
        
        public void Initialize()
        {
            try
            {
                // Detect Worldbuilder
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.D("[Compat] Ferny's Worldbuilder not detected");
                    return;
                }
                
                RPLog.Message("[Compat] Ferny's Worldbuilder detected, initializing compatibility");
                
                // Cache Worldbuilder types
                var wbMod = LoadedModManager.RunningMods
                    .FirstOrDefault(m => m.PackageIdPlayerFacing.ToLower() == ModuleID);
                
                if (wbMod != null)
                {
                    var wbAssembly = wbMod.assemblies.loadedAssemblies.FirstOrDefault();
                    if (wbAssembly != null)
                    {
                        worldPresetManagerType = wbAssembly.GetType("Worldbuilder.WorldPresetManager");
                        worldPresetType = wbAssembly.GetType("Worldbuilder.WorldPreset");
                        wbPatchType = wbAssembly.GetType("Worldbuilder.Page_CreateWorldParams_DoWindowContents_Patch");
                        
                        if (wbPatchType != null)
                        {
                            handlePreviewMethod = AccessTools.Method(wbPatchType, "HandlePreviewAutoGeneration");
                            drawPreviewMethod = AccessTools.Method(wbPatchType, "doWorldPreviewArea");
                            updateCurPresetMethod = AccessTools.Method(wbPatchType, "UpdateCurPreset");
                            worldExposeDataPatchType = wbAssembly.GetType("Worldbuilder.World_ExposeData_Patch");
                            
                            if (handlePreviewMethod == null || drawPreviewMethod == null)
                            {
                                RPLog.Warning($"[Compat] Worldbuilder patch type found but methods missing. handlePreviewMethod: {handlePreviewMethod != null}, drawPreviewMethod: {drawPreviewMethod != null}");
                            }
                            else
                            {
                                RPLog.Message("[Compat] Worldbuilder preview methods successfully cached");
                            }
                        }
                        else
                        {
                            RPLog.Warning("[Compat] Worldbuilder patch type 'Worldbuilder.Page_CreateWorldParams_DoWindowContents_Patch' not found");
                        }

                        if (worldPresetManagerType == null || worldPresetType == null)
                        {
                            RPLog.Warning("[Compat] Worldbuilder assembly found but required types missing");
                            IsActive = false;
                            return;
                        }

                        // Unpatch Worldbuilder's SetupElevationNoise prefix
                        var elevationPatchType = wbAssembly.GetType("Worldbuilder.WorldGenStep_Terrain_SetupElevationNoise_Patch");
                        if (elevationPatchType != null)
                        {
                            var patchMethod = AccessTools.Method(elevationPatchType, "Prefix");
                            var originalMethod = AccessTools.Method(typeof(WorldGenStep_Terrain), "SetupElevationNoise");
                            
                            if (patchMethod != null && originalMethod != null)
                            {
                                Controller.harmony.Unpatch(originalMethod, patchMethod);
                                RPLog.Message("[Compat] Successfully unpatched Worldbuilder's elevation mapping override");
                            }
                        }

                        // Unpatch Worldbuilder's SeasonalShiftAmplitudeAt
                        // This patch overrides RP2's physics-based seasonal temperature calculations
                        var seasonalPatchType = wbAssembly.GetType("Worldbuilder.GenTemperature_SeasonalShiftAmplitudeAt_Patch");
                        if (seasonalPatchType != null)
                        {
                            var originalMethod = AccessTools.Method(typeof(GenTemperature), "SeasonalShiftAmplitudeAt");
                            var patchPrefix = AccessTools.Method(seasonalPatchType, "Prefix");
                            var patchPostfix = AccessTools.Method(seasonalPatchType, "Postfix");
                            
                            if (originalMethod != null)
                            {
                                if (patchPrefix != null) Controller.harmony.Unpatch(originalMethod, patchPrefix);
                                if (patchPostfix != null) Controller.harmony.Unpatch(originalMethod, patchPostfix);
                                RPLog.Message("[Compat] Successfully unpatched Worldbuilder's seasonal temperature override");
                            }
                        }
                    }
                    else
                    {
                        RPLog.Warning("[Compat] Worldbuilder mod found but assembly not loaded");
                        IsActive = false;
                        return;
                    }
                }
                else
                {
                    RPLog.Warning("[Compat] Worldbuilder packageId detected but mod not found in LoadedModManager");
                    IsActive = false;
                    return;
                }
                
                RPLog.Message("[Compat] Worldbuilder compatibility initialized successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to initialize Worldbuilder compatibility: {e}");
                IsActive = false;
            }
        }
        
        public bool HasPreview => IsActive && drawPreviewMethod != null;

        /// <summary>
        /// Clears the cached world preview. Call when the CreateWorldParams page is first opened.
        /// </summary>
        public static void ClearPreview()
        {
            if (!Instance.IsActive || Instance.wbPatchType == null) return;
            try
            {
                var dirtyField = AccessTools.Field(Instance.wbPatchType, "dirty");
                var worldPreviewField = AccessTools.Field(Instance.wbPatchType, "worldPreview");
                // Setting dirty to false prevents it from auto-regenerating the preview
                // from the existing cached Find.World when worldPreview is null.
                dirtyField?.SetValue(null, false);
                worldPreviewField?.SetValue(null, null);
                
                // Also stop any running generation thread
                var shouldCancelField = AccessTools.Field(Instance.wbPatchType, "shouldCancelGeneration");
                shouldCancelField?.SetValue(null, true);
            }
            catch { /* non-critical */ }
        }

        public void DrawPreview(Page_CreateWorldParams page, Rect centerColumnRect)
        {
            if (!HasPreview) return;
            
            try
            {
                SyncPageToWorldbuilderData(page);
                
                const float labelHeight = 28f;
                Rect contentRect = new Rect(centerColumnRect.x, centerColumnRect.y + labelHeight, centerColumnRect.width, centerColumnRect.height - labelHeight);
                
                // Label above the preview
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(centerColumnRect.x, centerColumnRect.y, centerColumnRect.width, labelHeight), "World Preview (Worldbuilder)");
                Text.Anchor = TextAnchor.UpperLeft;
                
                // Worldbuilder expects a SQUARE rect (1:1 aspect). getWorldCameraPreview uses rect dimensions;
                // a non-square rect produces a stretched texture. Center the preview in the remaining space.
                float previewSize = Mathf.Min(contentRect.width, contentRect.height);
                float x = contentRect.x + (contentRect.width - previewSize) * 0.5f;
                float y = contentRect.y + (contentRect.height - previewSize) * 0.5f;
                Rect squareRect = new Rect(x, y, previewSize, previewSize);
                
                // Do NOT call handlePreviewMethod - that auto-starts the preview. User triggers manually via the button.
                drawPreviewMethod?.Invoke(null, new object[] { page, squareRect });
            }
            catch (Exception e)
            {
                RPLog.ErrorOnce($"[Compat] Error drawing Worldbuilder preview: {e}", 9128374);
            }
        }
        
        /// <summary>
        /// Syncs our page's parameters into Worldbuilder's World_ExposeData_Patch.worldGenerationData
        /// so the preview's background worldgen uses correct values. Also ensures curPlanetName and
        /// subdivisions are set for RP2/MLP compatibility.
        /// </summary>
        public static void SyncPageToWorldbuilderData(Page_CreateWorldParams page)
        {
            Instance.SyncPageToWorldbuilderDataInternal(page);
        }

        private void SyncPageToWorldbuilderDataInternal(Page_CreateWorldParams page)
        {
            try
            {
                if (worldExposeDataPatchType == null || updateCurPresetMethod == null) return;
                
                var worldGenDataField = AccessTools.Field(worldExposeDataPatchType, "worldGenerationData");
                var worldGenData = worldGenDataField?.GetValue(null);
                
                // Ensure worldGenerationData exists (PreOpen should init it, but guard against edge cases)
                if (worldGenData == null)
                {
                    var wgdType = AccessTools.TypeByName("Worldbuilder.WorldGenerationData");
                    if (wgdType != null)
                    {
                        worldGenData = Activator.CreateInstance(wgdType);
                        var initMethod = AccessTools.Method(wgdType, "Init");
                        initMethod?.Invoke(worldGenData, null);
                        worldGenDataField?.SetValue(null, worldGenData);
                    }
                }
                
                // UpdateCurPreset copies: rainfall, population, planetCoverage, seedString, temperature, pollution, landmarkDensity
                updateCurPresetMethod?.Invoke(null, new object[] { page });
                
                worldGenData = worldGenDataField?.GetValue(null);
                if (worldGenData == null) return;
                
                var dataType = worldGenData.GetType();
                
                // Worldbuilder's worldGenerationData.seaLevel is float 0-2 (1=normal). Map our SeaLevel enum.
                var seaLevelField = AccessTools.Field(dataType, "seaLevel");
                if (seaLevelField != null)
                {
                    float wbSeaLevel = ConvertRPRSeaLevelToWorldbuilder(Planets_GameComponent.seaLevel);
                    seaLevelField.SetValue(worldGenData, wbSeaLevel);
                }
                
                // Worldbuilder defines its own AxialTilt enum. Both have same ordinal values (VeryLow=0 .. VeryHigh=4).
                var axialTiltField = AccessTools.Field(dataType, "axialTilt");
                if (axialTiltField != null)
                {
                    axialTiltField.SetValue(worldGenData, Enum.ToObject(axialTiltField.FieldType, (int)Planets_GameComponent.axialTilt));
                }
                
                // curPlanetName: ensure it's set (used as world name during preview gen)
                var curPlanetNameField = AccessTools.Field(wbPatchType, "curPlanetName");
                if (!string.IsNullOrEmpty(Planets_GameComponent.planetName))
                {
                    curPlanetNameField?.SetValue(null, Planets_GameComponent.planetName);
                }
                else
                {
                    var curPlanetName = curPlanetNameField?.GetValue(null) as string;
                    if (string.IsNullOrEmpty(curPlanetName))
                    {
                        curPlanetNameField?.SetValue(null, NameGenerator.GenerateName(RulePackDefOf.NamerWorld));
                    }
                }

                // Sync new densities to Worldbuilder's worldGenerationData
                var riverDensityField = AccessTools.Field(dataType, "riverDensity");
                if (riverDensityField != null) riverDensityField.SetValue(worldGenData, Planets_GameComponent.riverDensity);

                var ancientRoadDensityField = AccessTools.Field(dataType, "ancientRoadDensity");
                if (ancientRoadDensityField != null) ancientRoadDensityField.SetValue(worldGenData, Planets_GameComponent.ancientRoadDensity);

                var factionRoadDensityField = AccessTools.Field(dataType, "factionRoadDensity");
                if (factionRoadDensityField != null) factionRoadDensityField.SetValue(worldGenData, Planets_GameComponent.factionRoadDensity);
                
                // subdivisions: keep in sync with Planets_GameComponent.subcount for MLP/RP2
                if (Compatibility.MyLittlePlanetCompat.IsEnabled)
                {
                    try
                    {
                        var defOfType = AccessTools.TypeByName("PlanetLayerSettingsDefOf");
                        var surfaceField = defOfType != null ? AccessTools.Field(defOfType, "Surface") : null;
                        var surface = surfaceField?.GetValue(null);
                        if (surface != null)
                        {
                            var settingsField = AccessTools.Field(surface.GetType(), "settings");
                            var settings = settingsField?.GetValue(surface);
                            if (settings != null)
                            {
                                var subdivField = AccessTools.Field(settings.GetType(), "subdivisions");
                                subdivField?.SetValue(settings, Planets_GameComponent.subcount);
                            }
                        }
                        var modCompatHelper = AccessTools.TypeByName("Worldbuilder.ModCompatibilityHelper");
                        var trySetMLP = modCompatHelper != null ? AccessTools.Method(modCompatHelper, "TrySetMLPSubcount", new[] { typeof(int) }) : null;
                        trySetMLP?.Invoke(null, new object[] { Planets_GameComponent.subcount });
                    }
                    catch { /* MLP sync is best-effort */ }
                }
            }
            catch (Exception e)
            {
                if (Controller.DebugEnabled)
                    RPLog.D($"[Compat] SyncPageToWorldbuilderData: {e.Message}");
            }
        }
        
        private static float ConvertRPRSeaLevelToWorldbuilder(SeaLevel level)
        {
            return level switch
            {
                SeaLevel.Low => 0.5f,
                SeaLevel.SlightlyLow => 0.75f,
                SeaLevel.Normal => 1f,
                SeaLevel.SlightlyHigh => 1.25f,
                SeaLevel.High => 1.5f,
                _ => 1f
            };
        }
        
        /// <summary>
        /// Get the currently loaded Worldbuilder preset name.
        /// </summary>
        public static string GetCurrentPresetName()
        {
            return Instance.GetCurrentPresetNameInternal();
        }

        private string GetCurrentPresetNameInternal()
        {
            if (!IsActive || worldPresetManagerType == null)
                return null;
            
            try
            {
                var currentPresetProp = AccessTools.Property(worldPresetManagerType, "CurrentlyLoadedPreset");
                if (currentPresetProp != null)
                {
                    var preset = currentPresetProp.GetValue(null);
                    if (preset != null)
                    {
                        var nameField = AccessTools.Field(preset.GetType(), "name");
                        return nameField?.GetValue(preset) as string;
                    }
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to get current Worldbuilder preset: {e}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Create preset data from current page settings.
        /// Called when saving a Worldbuilder preset.
        /// </summary>
        public static RealisticPlanetsPresetData CreateDataFromCurrentPage(string presetName)
        {
            var data = new RealisticPlanetsPresetData
            {
                linkedPresetName = presetName,
                seaLevel = Planets_GameComponent.seaLevel,
                axialTilt = Planets_GameComponent.axialTilt,
                worldPresetName = Planets_GameComponent.worldPreset,
                subcount = Planets_GameComponent.subcount
            };
            
            // Try to find our page if it exists (during world creation)
            Page_CreateWorldParamsRP currentPage = null;
            
            if (Find.WindowStack != null)
            {
                foreach (var window in Find.WindowStack.Windows)
                {
                    if (window is Page_CreateWorldParamsRP page)
                    {
                        currentPage = page;
                        break;
                    }
                }
            }
            
            if (Controller.Settings?.climateConfig != null)
            {
                var cc = Controller.Settings.climateConfig;
                data.mountainHeight = cc.mountainHeight.CurrentValue;
                data.mountainousness = cc.mountainousness.CurrentValue;
                data.grpOceanReach = cc.grpOceanReach.CurrentValue;
                data.grpMoistureReach = cc.grpMoistureReach.CurrentValue;
                data.grpRainShadow = cc.grpRainShadow.CurrentValue;
                data.grpCurrents = cc.grpCurrents.CurrentValue;
                data.grpMonsoon = cc.grpMonsoon.CurrentValue;
                data.grpSubsidence = cc.grpSubsidence.CurrentValue;
            }
            if (currentPage != null)
            {
                // Page exists - use its current values
                data.caveSystems = currentPage.CaveSystems;
                data.insectTerritoryScale = currentPage.InsectTerritoryScale;
                data.landmarkDensity = currentPage.LandmarkDensity;
            }
            else
            {
                // Page is gone (saving after world generation) - use saved values from cache
                data.caveSystems = RP_WorldGen_SaveSettings_Patch.LastGeneratedCaveSystems;
                data.insectTerritoryScale = RP_WorldGen_SaveSettings_Patch.LastGeneratedInsectTerritoryScale;
                
                // Landmark density is stored in world info
                if (Find.World?.info != null)
                {
                    data.landmarkDensity = Find.World.info.landmarkDensity;
                }
            }
            
            return data;
        }
    }
    
    /// <summary>
    /// Caches page-only values before world generation so they can be saved in presets.
    /// This is necessary because the page is destroyed after world generation completes.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld))]
    public static class RP_WorldGen_SaveSettings_Patch
    {
        // Static storage for values that need to persist after world generation
        public static float LastGeneratedCaveSystems = 0.5f;
        public static float LastGeneratedInsectTerritoryScale = 1f;
        
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (!WorldbuilderCompat.Instance.IsActive)
                return;
            
            // Try to find the Page_CreateWorldParamsRP and save its values
            Page_CreateWorldParamsRP planetsPage = null;
            
            // Check all windows for our page
            if (Find.WindowStack != null)
            {
                foreach (var window in Find.WindowStack.Windows)
                {
                    if (window is Page_CreateWorldParamsRP page)
                    {
                        planetsPage = page;
                        break;
                    }
                }
            }
            
            if (planetsPage != null)
            {
                // Save the values that aren't stored in game components
                LastGeneratedCaveSystems = planetsPage.CaveSystems;
                LastGeneratedInsectTerritoryScale = planetsPage.InsectTerritoryScale;
                
                if (Controller.DebugEnabled)
                {
                    RPLog.D($"[Compat] Cached world generation settings - " +
                        $"Caves: {LastGeneratedCaveSystems}, " +
                        $"Insects: {LastGeneratedInsectTerritoryScale}");
                }
            }
        }
    }
    
    /// <summary>
    /// Companion data save hook for Worldbuilder.
    /// When Worldbuilder saves a preset, we save our RP2-specific settings alongside it.
    /// </summary>
    [HarmonyPatch]
    public static class WorldPresetManager_SavePreset_Patch
    {
        public static bool Prepare()
        {
            return ModsConfig.IsActive("ferny.Worldbuilder") && AccessTools.TypeByName("Worldbuilder.WorldPresetManager") != null;
        }
        
        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("Worldbuilder.WorldPresetManager");
            if (type != null)
            {
                return AccessTools.Method(type, "SavePreset");
            }
            return null;
        }
        
        [HarmonyPostfix]
        public static void Postfix(object preset, bool __result)
        {
            if (!__result || preset == null)
                return;
            
            try
            {
                var nameField = AccessTools.Field(preset.GetType(), "name");
                string presetName = nameField?.GetValue(preset) as string;
                
                if (!string.IsNullOrEmpty(presetName))
                {
                    // Save our companion data
                    var rpData = WorldbuilderCompat.CreateDataFromCurrentPage(presetName);
                    if (rpData != null)
                    {
                        RealisticPlanetsPresetManager.SavePresetData(rpData);
                    }
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to save Worldbuilder companion data: {e}");
            }
        }
    }
    
    /// <summary>
    /// Companion data delete hook for Worldbuilder.
    /// When Worldbuilder deletes a preset, we delete our companion data too.
    /// </summary>
    [HarmonyPatch]
    public static class WorldPresetManager_DeletePreset_Patch
    {
        public static bool Prepare()
        {
            return ModsConfig.IsActive("ferny.Worldbuilder") && AccessTools.TypeByName("Worldbuilder.WorldPresetManager") != null;
        }
        
        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("Worldbuilder.WorldPresetManager");
            if (type != null)
            {
                return AccessTools.Method(type, "DeletePreset");
            }
            return null;
        }
        
        [HarmonyPrefix]
        public static void Prefix(object preset)
        {
            if (preset == null)
                return;
            
            try
            {
                var nameField = AccessTools.Field(preset.GetType(), "name");
                string presetName = nameField?.GetValue(preset) as string;
                
                if (!string.IsNullOrEmpty(presetName))
                {
                    RealisticPlanetsPresetManager.DeletePresetData(presetName);
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to delete Worldbuilder companion data: {e}");
            }
        }
    }
    
    /// <summary>
    /// Apply RP2 settings when the create world params page is reset.
    /// This happens when Worldbuilder loads a preset - vanilla settings are applied by Worldbuilder,
    /// then our Reset is called, so we re-apply our custom RP2 settings on top.
    /// </summary>
    [HarmonyPatch(typeof(Page_CreateWorldParams), nameof(Page_CreateWorldParams.Reset))]
    [HarmonyPriority(Priority.Last)]
    public static class Page_CreateWorldParams_Reset_WorldbuilderPostfix
    {
        [HarmonyPostfix]
        public static void Postfix(Page_CreateWorldParams __instance)
        {
            if (!(__instance is Page_CreateWorldParamsRP))
                return;
            if (!WorldbuilderCompat.Instance.IsActive)
                return;
            
            string presetName = WorldbuilderCompat.GetCurrentPresetName();
            if (!string.IsNullOrEmpty(presetName) && presetName != "_DEFAULT_")
            {
                var rpData = RealisticPlanetsPresetManager.LoadPresetData(presetName);
                if (rpData != null)
                {
                    // Apply the loaded data to the page
                    var rpPage = (Page_CreateWorldParamsRP)__instance;
                    rpPage.CaveSystems = rpData.caveSystems;
                    if (Controller.Settings?.climateConfig != null)
                    {
                        var cc = Controller.Settings.climateConfig;
                        cc.mountainHeight.CurrentValue = rpData.mountainHeight;
                        cc.mountainousness.CurrentValue = rpData.mountainousness;
                        cc.grpOceanReach.CurrentValue = rpData.grpOceanReach;
                        cc.grpMoistureReach.CurrentValue = rpData.grpMoistureReach;
                        cc.grpRainShadow.CurrentValue = rpData.grpRainShadow;
                        cc.grpCurrents.CurrentValue = rpData.grpCurrents;
                        cc.grpMonsoon.CurrentValue = rpData.grpMonsoon;
                        cc.grpSubsidence.CurrentValue = rpData.grpSubsidence;
                    }
                    rpPage.InsectTerritoryScale = rpData.insectTerritoryScale;
                    
                    // LandmarkDensity is read-only, need to use reflection
                    var landmarkField = AccessTools.Field(typeof(Page_CreateWorldParams), "landmarkDensity");
                    landmarkField?.SetValue(__instance, rpData.landmarkDensity);
                    
                    // Also reapply to game component
                    Planets_GameComponent.seaLevel = rpData.seaLevel;
                    Planets_GameComponent.axialTilt = rpData.axialTilt;
                    Planets_GameComponent.worldPreset = rpData.worldPresetName;
                    Planets_GameComponent.subcount = rpData.subcount;
                    
                    if (Controller.DebugEnabled)
                    {
                        RPLog.D($"[Compat] Applied preset settings from Worldbuilder preset '{presetName}'");
                    }
                }
                else if (Controller.DebugEnabled)
                {
                    RPLog.D($"[Compat] No RP2 companion data for Worldbuilder preset '{presetName}' (vanilla preset)");
                }
            }
        }
    }

}
