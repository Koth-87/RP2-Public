using UnityEngine;
using Verse;
using System.Collections.Generic;
using RimWorld;
using Planets.Biomes.Core;
using Planets.Compatibility;
using Planets.Utilities.Logging;

namespace Planets.Core.Configuration
{
    /// <summary>
    /// ADVANCED BIOME CONFIGURATION
    /// ===========================
    /// Manages biome spawning rules for ALL biomes (Vanilla, RP, Modded).
    /// 
    /// Features:
    /// - Individual biome toggles (Enabled/Disabled)
        /// - Frequency multipliers (0x to 5x). A saved score offset is folded into this on load.
    /// 
    /// Persistence:
    /// - Stored as a dictionary of BiomeMetadata (unified: placement, calibration, overrides)
    /// - Legacy: can load old BiomeModifiers and migrate to BiomeMetadata
    /// - Can be saved/loaded via Biome Presets
    /// </summary>
    public class BiomeConfiguration : IExposable
    {
        /// <summary>Legacy type for migration from old saves.</summary>
        public class BiomeModifier : IExposable
        {
            public bool Enabled = true;
            public float CommonalityMultiplier = 1.0f;
            public float ScoreOffset = 0.0f;
            public void ExposeData()
            {
                Scribe_Values.Look(ref Enabled, "Enabled", true);
                Scribe_Values.Look(ref CommonalityMultiplier, "CommonalityMultiplier", 1.0f);
                Scribe_Values.Look(ref ScoreOffset, "ScoreOffset", 0.0f);
            }
            public BiomeModifier Clone()
            {
                return new BiomeModifier { Enabled = this.Enabled, CommonalityMultiplier = this.CommonalityMultiplier, ScoreOffset = this.ScoreOffset };
            }
        }

        // Main storage (unified metadata)
        public Dictionary<string, BiomeMetadata> BiomeMetadataMap = new Dictionary<string, BiomeMetadata>();
        private List<string> metadataKeys;
        private List<BiomeMetadata> metadataValues;

        // Legacy: for loading old saves
        private Dictionary<string, BiomeModifier> BiomeModifiers;
        private List<string> modifierKeys;
        private List<BiomeModifier> modifierValues;
        
        private bool initialized = false;

        /// <summary>
        /// Initialize biome modifiers from DefDatabase and external sources.
        /// This should be called once after defs are loaded.
        /// </summary>
        public void Init()
        {
            // Always run Init to ensure all biomes are present (even if previously initialized)
            // This handles cases where Scribe loaded only a partial list, or new mods were added.
            
            // Get external defaults (e.g. from Alpha Biomes settings) so sliders in Advanced Biomes match AB mod settings
            Dictionary<string, float> externalMultipliers = new Dictionary<string, float>();

            if (AlphaBiomesCompat.IsEnabled)
            {
                externalMultipliers = AlphaBiomesCompat.GetCurrentValuesKeyedByDefName();
            }
            
            // Pre-initialize ZBiome_DesertOasis if present to avoid race condition with RP_Oasis check
            var mvbOasisDef = DefDatabase<BiomeDef>.GetNamed("ZBiome_DesertOasis", false);
            if (mvbOasisDef != null && !BiomeMetadataMap.ContainsKey(mvbOasisDef.defName))
            {
                var meta = GetOrCreateMetadata(mvbOasisDef.defName);
                meta.Enabled = MVBOasisCompat.MVBOasisEnabled;
                FillHardcodedDefaults(meta, mvbOasisDef);
            }

            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefs)
            {
                if (!biome.implemented || !biome.generatesNaturally) continue;
                
                    if (!BiomeMetadataMap.ContainsKey(biome.defName))
                {
                    var meta = GetOrCreateMetadata(biome.defName);
                    if (IsBiomeLocked(biome.defName, out string _))
                        meta.Enabled = false;
                    if (TryGetExternalMultiplier(biome.defName, externalMultipliers, out float mult))
                        meta.CommonalityMultiplier = mult;
                        
                    FillHardcodedDefaults(meta, biome);
                }
                else
                {
                    if (IsBiomeLocked(biome.defName, out string _))
                        BiomeMetadataMap[biome.defName].Enabled = false;
                }
            }
            
            initialized = true;
            RPLog.Debug($"Initialized biome configuration with {BiomeMetadataMap.Count} biomes");
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref BiomeMetadataMap, "BiomeMetadataMap", LookMode.Value, LookMode.Deep, ref metadataKeys, ref metadataValues);
            Scribe_Collections.Look(ref BiomeModifiers, "BiomeModifiers", LookMode.Value, LookMode.Deep, ref modifierKeys, ref modifierValues);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (BiomeMetadataMap == null) BiomeMetadataMap = new Dictionary<string, BiomeMetadata>();
                // Migrate legacy BiomeModifiers into BiomeMetadataMap
                if (BiomeModifiers != null && BiomeModifiers.Count > 0)
                {
                    foreach (var kvp in BiomeModifiers)
                    {
                        if (!BiomeMetadataMap.ContainsKey(kvp.Key))
                            BiomeMetadataMap[kvp.Key] = BiomeMetadata.FromModifier(kvp.Value);
                        else
                        {
                            BiomeMetadataMap[kvp.Key].Enabled = kvp.Value.Enabled;
                            BiomeMetadataMap[kvp.Key].CommonalityMultiplier = kvp.Value.CommonalityMultiplier;
                            BiomeMetadataMap[kvp.Key].ScoreOffset = kvp.Value.ScoreOffset;
                        }
                    }
                    BiomeModifiers = null;
                }

                // Legacy modifiers are copied after each metadata record has already folded its own offset.
                if (BiomeMetadataMap != null)
                {
                    foreach (var meta in BiomeMetadataMap.Values)
                        meta.FoldScoreOffsetIntoFrequency();
                }
            }
        }
        
        /// <summary>
        /// Try to get multiplier from external dict by defName, with fallback for prefixed defNames (e.g. AB_FeraliskInfestedJungle).
        /// </summary>
        private static bool TryGetExternalMultiplier(string defName, Dictionary<string, float> external, out float mult)
        {
            if (external != null && external.TryGetValue(defName, out mult)) return true;
            int idx = defName.IndexOf('_');
            if (idx >= 0 && idx < defName.Length - 1)
            {
                string suffix = defName.Substring(idx + 1);
                if (external != null && external.TryGetValue(suffix, out mult)) return true;
            }
            mult = 0f;
            return false;
        }

        /// <summary>
        /// Resets a single biome to its default configuration (checking compatibility and external mods).
        /// Keeps calibration/override data; resets only placement (enabled, multiplier, offset).
        /// </summary>
        public void ResetBiomeToDefault(string defName)
        {
            if (!BiomeMetadataMap.TryGetValue(defName, out var meta))
                return;

            Dictionary<string, float> externalMultipliers = null;
            if (AlphaBiomesCompat.IsEnabled)
            {
                externalMultipliers = AlphaBiomesCompat.GetCurrentValuesKeyedByDefName();
            }

            if (IsBiomeLocked(defName, out string _))
                meta.Enabled = false;
            else
            {
                meta.Enabled = defName == "ZBiome_DesertOasis" ? MVBOasisCompat.MVBOasisEnabled : true;
            }
            meta.CommonalityMultiplier = TryGetExternalMultiplier(defName, externalMultipliers, out float mult) ? mult : 1.0f;
            meta.ScoreOffset = 0.0f;
        }

        /// <summary>
        /// Reset all to computed defaults (clears metadata and re-inits).
        /// </summary>
        public void ResetToDefaults()
        {
            BiomeMetadataMap.Clear();
            initialized = false;
            Init();
        }

        // ========== ACCESSORS (Used by Orchestrator and UI) ==========

        public bool IsBiomeEnabled(string defName)
        {
            if (!initialized && BiomeMetadataMap.Count == 0) Init();
            if (BiomeMetadataMap.TryGetValue(defName, out var meta)) return meta.Enabled;
            return true;
        }

        public float GetCommonalityMultiplier(string defName)
        {
            if (BiomeMetadataMap.TryGetValue(defName, out var meta)) return meta.CommonalityMultiplier;
            return 1.0f;
        }

        public float GetScoreOffset(string defName)
        {
            if (BiomeMetadataMap.TryGetValue(defName, out var meta)) return meta.ScoreOffset;
            return 0.0f;
        }

        public BiomeMetadata GetMetadata(string defName)
        {
            if (!initialized && BiomeMetadataMap.Count == 0) Init();
            BiomeMetadataMap.TryGetValue(defName, out var meta);
            return meta;
        }

        /// <summary>Gets or creates metadata for a biome (used by Init and analysis).</summary>
        public BiomeMetadata GetOrCreateMetadata(string defName)
        {
            if (!BiomeMetadataMap.TryGetValue(defName, out var meta))
            {
                meta = new BiomeMetadata();
                BiomeMetadataMap[defName] = meta;
            }
            return meta;
        }

        /// <summary>
        /// Populates the biome's metadata with its hardcoded default scoring toggles and calibration values
        /// directly from its BiomeWorker and BiomeConditionRegistry. 
        /// </summary>
        public static void FillHardcodedDefaults(BiomeMetadata target, BiomeDef biome)
        {
            if (target == null || biome == null) return;
            
            var worker = biome.Worker as Planets.Biomes.Core.BiomeWorkerBase;
            if (worker != null)
            {
                worker.FillCalibrationDefaults(target, biome.defName);
                target.UseWorkerOverrides = false;
                target.UserIdealTemp = target.IdealTemp;
                target.UserTempTolerance = target.TempTolerance;
                target.UserIdealRain = target.IdealRain;
                target.UserRainTolerance = target.RainTolerance;
                target.UserIdealElevation = target.IdealElevation;
                target.UserElevationTolerance = target.ElevationTolerance;
                target.UserIdealElevation2 = target.IdealElevation2;
                target.UserElevationTolerance2 = target.ElevationTolerance2;
            }
            else
            {
                target.UseWorkerOverrides = false;
                target.UserIdealTemp = 15f;
                target.UserTempTolerance = 10f;
                target.UserIdealRain = 1000f;
                target.UserRainTolerance = 500f;
                target.UserIdealElevation = 500f;
                target.UserElevationTolerance = 1000f;
                target.UseVarDualElevation = false;
                target.UserIdealElevation2 = 500f;
                target.UserElevationTolerance2 = 1000f;
                target.UserElevationWeight1 = 1f;
                target.UserElevationWeight2 = 1f;
                target.IdealAridity = 1f;
                target.AridityTolerance = 0.4f;
            }

            var usedConditions = Planets.Biomes.Core.BiomeConditionRegistry.GetUsedConditions(biome.defName);
            if (usedConditions != null)
            {
                target.UseVarTemperature = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Temperature);
                target.UseVarRainfall = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Rainfall);
                target.UseVarAridity = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Aridity);
                target.UseVarElevation = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Elevation);
                target.UseVarDualElevation = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.DualElevation);
                target.UseVarSwampiness = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Swampiness);
                target.UseVarHilliness = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Hilliness);
                target.UseVarContinentality = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Continentality);
                target.UseVarSeasonality = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Seasonality);
                target.UseVarWindward = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Windward);
                target.UseVarLeeward = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Leeward);
                target.UseVarFlowAccumulation = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.FlowAccumulation);
                target.UseVarSummerMax = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.SummerMax);
                target.UseVarWinterMin = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.WinterMin);
                target.UseVarMonsoonBoost = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.MonsoonBoost);
                target.UseVarNoise = usedConditions.Contains(Planets.Biomes.Core.BiomeConditionRegistry.Noise);
            }
            else if (biome.workerClass?.Namespace?.StartsWith("Planets.Biomes.Compat") == true)
            {
                float idealTemp = target.IdealTemp;
                if (idealTemp == 0f && target.UserIdealTemp != 0f) idealTemp = target.UserIdealTemp;
                bool cold = idealTemp < 0f;
                target.UseVarTemperature = true;
                target.UseVarRainfall = cold;
                target.UseVarAridity = !cold;
                target.UseVarElevation = true;
                target.UseVarSwampiness = false;
                target.UseVarHilliness = false;
                target.UseVarContinentality = false;
                target.UseVarSeasonality = false;
                target.UseVarWindward = false;
                target.UseVarLeeward = false;
                target.UseVarFlowAccumulation = false;
                target.UseVarSummerMax = false;
                target.UseVarWinterMin = false;
                target.UseVarMonsoonBoost = false;
                target.UseVarNoise = false;
            }
            else
            {
                // Fallback for biomes not in registry (mostly vanilla or older mods before BAACS)
                target.UseVarTemperature = true;
                target.UseVarAridity = true;
                target.UseVarElevation = true;
                target.UseVarRainfall = false;
                target.UseVarSwampiness = false;
                target.UseVarHilliness = false;
                target.UseVarContinentality = false;
                target.UseVarSeasonality = false;
                target.UseVarWindward = false;
                target.UseVarLeeward = false;
                target.UseVarFlowAccumulation = false;
                target.UseVarSummerMax = false;
                target.UseVarWinterMin = false;
                target.UseVarMonsoonBoost = false;
                target.UseVarNoise = false;
            }
        }

        /// <summary>
        /// Checks if a biome is locked (forced disabled) due to compatibility reasons.
        /// Locked biomes cannot be enabled by the user and appear greyed out in the UI.
        /// </summary>
        /// <param name="defName">BiomeDef defName to check</param>
        /// <param name="reason">Human-readable reason for the lock (if locked)</param>
        /// <returns>True if the biome is locked, false otherwise</returns>
        public bool IsBiomeLocked(string defName, out string reason)
        {
            reason = null;
            
            // RP_Prairie is redundant with Odyssey's Grassland
            if (defName == "RP_Prairie" && ModsConfig.OdysseyActive)
            {
                reason = "Disabled by RimWorld Odyssey compatibility (redundant biome)";
                return true;
            }
            
            // RP_Oasis defers to MVB's Oasis when MVB is active and enabled
            if (defName == "RP_Oasis" && DefDatabase<BiomeDef>.GetNamed("ZBiome_DesertOasis", false) != null && IsBiomeEnabled("ZBiome_DesertOasis"))
            {
                reason = "Disabled by More Vanilla Biomes compatibility (using MVB Oasis)";
                return true;
            }
            
            return false;
        }
    }
}
