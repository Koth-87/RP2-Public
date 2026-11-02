using System;
using System.IO;
using Verse;
using RimWorld.Planet;
using Planets.Core;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Geography;
using Planets.Utilities.Logging;

namespace Planets.Compatibility.FernyMods
{
    /// <summary>
    /// Stores Realistic Planets 2 specific settings that accompany a Worldbuilder preset.
    /// Ported from old RP code with RP2 field updates:
    /// - Removed: rainfallModifier (RP2 uses rainfall slider directly), mountains (GL slider removed)
    /// - Kept: seaLevel, axialTilt, worldPresetName, caveSystems, insectTerritoryScale, subcount, landmarkDensity
    /// </summary>
    public class RealisticPlanetsPresetData : IExposable
    {
        public string linkedPresetName;
        public SeaLevel seaLevel = SeaLevel.Normal;
        public AxialTilt axialTilt = AxialTilt.Normal;
        public string worldPresetName = "Planets.Vanilla";
        
        // Geological Landforms compatibility (only caves - mountains slider removed)
        public float caveSystems = 0.5f;
        public float mountainHeight = 1f;
        public float mountainousness = 1f;

        // WS24 grouped climate outcome multipliers
        public float grpOceanReach = 1f;
        public float grpMoistureReach = 1f;
        public float grpRainShadow = 1f;
        public float grpCurrents = 1f;
        public float grpMonsoon = 1f;
        public float grpSubsidence = 1f;
        
        // VFE Insectoids compatibility
        public float insectTerritoryScale = 1f;
        
        // My Little Planet compatibility
        public int subcount = 10;
        
        // Odyssey DLC compatibility
        public LandmarkDensity landmarkDensity = LandmarkDensity.Normal;
        
        public void ExposeData()
        {
            Scribe_Values.Look(ref linkedPresetName, "linkedPresetName");
            Scribe_Values.Look(ref seaLevel, "seaLevel", SeaLevel.Normal);
            Scribe_Values.Look(ref axialTilt, "axialTilt", AxialTilt.Normal);
            Scribe_Values.Look(ref worldPresetName, "worldPresetName", "Planets.Vanilla");
            Scribe_Values.Look(ref caveSystems, "caveSystems", 0.5f);
            Scribe_Values.Look(ref mountainHeight, "mountainHeight", 1f);
            Scribe_Values.Look(ref mountainousness, "mountainousness", 1f);
            Scribe_Values.Look(ref grpOceanReach, "grpOceanReach", 1f);
            Scribe_Values.Look(ref grpMoistureReach, "grpMoistureReach", 1f);
            Scribe_Values.Look(ref grpRainShadow, "grpRainShadow", 1f);
            Scribe_Values.Look(ref grpCurrents, "grpCurrents", 1f);
            Scribe_Values.Look(ref grpMonsoon, "grpMonsoon", 1f);
            Scribe_Values.Look(ref grpSubsidence, "grpSubsidence", 1f);
            Scribe_Values.Look(ref insectTerritoryScale, "insectTerritoryScale", 1f);
            Scribe_Values.Look(ref subcount, "subcount", 10);
            Scribe_Values.Look(ref landmarkDensity, "landmarkDensity", LandmarkDensity.Normal);
        }
    }
    
    /// <summary>
    /// Manages saving and loading Realistic Planets preset data alongside Worldbuilder presets.
    /// </summary>
    public static class RealisticPlanetsPresetManager
    {
        private static readonly string PresetFolderPath = Path.Combine(GenFilePaths.ConfigFolderPath, "RealisticPlanets2_Presets");
        
        static RealisticPlanetsPresetManager()
        {
            // Ensure our preset folder exists
            DirectoryInfo dir = new DirectoryInfo(PresetFolderPath);
            if (!dir.Exists)
            {
                dir.Create();
            }
        }
        
        private static string GetPresetFilePath(string presetName)
        {
            return Path.Combine(PresetFolderPath, $"RP_{presetName}.xml");
        }
        
        public static void SavePresetData(RealisticPlanetsPresetData data)
        {
            if (data == null || string.IsNullOrEmpty(data.linkedPresetName))
            {
                RPLog.Error("[Compat] Cannot save preset data without a linked preset name");
                return;
            }
            
            string filePath = GetPresetFilePath(data.linkedPresetName);
            
            try
            {
                Scribe.saver.InitSaving(filePath, "RealisticPlanetsPreset");
                data.ExposeData();
                Scribe.saver.FinalizeSaving();
                
                if (Controller.DebugEnabled)
                {
                    RPLog.D($"[Compat] Saved preset data for '{data.linkedPresetName}'");
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to save preset data: {e}");
            }
        }
        
        public static RealisticPlanetsPresetData LoadPresetData(string presetName)
        {
            if (string.IsNullOrEmpty(presetName))
                return null;
                
            string filePath = GetPresetFilePath(presetName);
            
            if (!File.Exists(filePath))
            {
                if (Controller.DebugEnabled)
                {
                    RPLog.D($"[Compat] No preset data found for '{presetName}'");
                }
                return null;
            }
            
            try
            {
                Scribe.loader.InitLoading(filePath);
                RealisticPlanetsPresetData data = new RealisticPlanetsPresetData();
                data.ExposeData();
                Scribe.loader.FinalizeLoading();
                
                if (Controller.DebugEnabled)
                {
                    RPLog.D($"[Compat] Loaded preset data for '{presetName}'");
                }
                
                return data;
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to load preset data for '{presetName}': {e}");
                return null;
            }
        }
        
        public static void DeletePresetData(string presetName)
        {
            if (string.IsNullOrEmpty(presetName))
                return;
                
            string filePath = GetPresetFilePath(presetName);
            
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    
                    if (Controller.DebugEnabled)
                    {
                        RPLog.D($"[Compat] Deleted preset data for '{presetName}'");
                    }
                }
                catch (Exception e)
                {
                    RPLog.Error($"[Compat] Failed to delete preset data: {e}");
                }
            }
        }
    }
}
