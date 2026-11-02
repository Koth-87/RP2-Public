using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.Biomes.Core;

namespace Planets.Compatibility
{
    /// <summary>
    /// Self-contained compatibility module for Odyssey DLC.
    /// Handles all Odyssey-related content: coastal animals, fish types, landmarks, mutators, terrain features.
    /// </summary>
    [StaticConstructorOnStartup]
    public class OdysseyCompat : ICompatibilityModule
    {
        private static OdysseyCompat _instance;
        public static OdysseyCompat Instance => _instance ?? (_instance = new OdysseyCompat());

        public static bool IsEnabled => Instance.IsActive;

        static OdysseyCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "ludeon.rimworld.odyssey";
        public string ModName => "Odyssey DLC";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }

        // ========== PUBLIC API ==========
        
        /// <summary>
        /// Check if Odyssey has wetland mutator system
        /// </summary>
        public static bool HasWetlands => IsEnabled && 
            DefDatabase<TileMutatorDef>.GetNamedSilentFail("Wetland") != null;
        
        /// <summary>
        /// Check if Odyssey has landmark system
        /// </summary>
        public static bool HasLandmarks => IsEnabled && DefDatabase<LandmarkDef>.DefCount > 0;
        
        // ========== SETTINGS ==========
        // Stored on IntegrationConfiguration so they save during the Mod constructor,
        // before this module has registered.
        private float MinOasisLandmarkCoverageValue
        {
            get => Controller.Settings?.integrationConfig?.odysseyMinOasisLandmarkCoverage ?? 0f;
            set
            {
                if (Controller.Settings?.integrationConfig != null)
                    Controller.Settings.integrationConfig.odysseyMinOasisLandmarkCoverage = value;
            }
        }

        public static float MinOasisLandmarkCoverage => Instance.MinOasisLandmarkCoverageValue;
        
        // ========== INITIALIZATION ==========
        
        public void Initialize()
        {
            try
            {
                // Check if Odyssey is active
                IsActive = ModsConfig.OdysseyActive;
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Debug("Odyssey DLC not detected");
                    return;
                }
                
                RPLog.Debug("Odyssey DLC detected, initializing compatibility");
                
                // Copy Odyssey-specific content to RP biomes
                CopyCoastalAnimals();
                CopyFishTypes();
                
                // Apply mutator compatibility patches
                PatchTileMutators();
                
                // Apply special landmark cases
                ApplySpecialLandmarkCases();
                
                // Apply Harmony patches for landmark control
                ApplyLandmarkPatches();
                
                RPLog.Debug("Odyssey DLC compatibility initialized successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"Error initializing Odyssey compatibility: {e}");
                IsActive = false;
            }
        }
        
        // ========== CONTENT COPYING ==========
        
        /// <summary>
        /// Copy coastal animals from vanilla biomes to RP biomes
        /// </summary>
        private static void CopyCoastalAnimals()
        {
            int coastalAdded = 0;
            
            // Copy from BiomeDefs directly
            foreach (var mapping in BiomeContentHelper.BiomeMappings)
            {
                var rpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                var vanillaBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Value, false);
                
                if (rpBiome == null || vanillaBiome == null) continue;
                
                var vanillaCoastalAnimals = BiomeContentHelper.GetCoastalWildAnimals(vanillaBiome);
                if (vanillaCoastalAnimals == null) continue;
                
                foreach (var animal in vanillaCoastalAnimals)
                {
                    BiomeContentHelper.AddCoastalAnimal(rpBiome, animal.animal, animal.commonality);
                    coastalAdded++;
                }
            }
            
            if (Controller.DebugEnabled && coastalAdded > 0)
            {
                RPLog.Debug($"Added {coastalAdded} coastal animal entries");
            }
        }
        
        /// <summary>
        /// Copy fish types from vanilla biomes to RP biomes
        /// </summary>
        private static void CopyFishTypes()
        {
            int copied = 0;
            
            foreach (var mapping in BiomeContentHelper.BiomeMappings)
            {
                var rpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                var vanillaBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Value, false);
                
                if (rpBiome == null || vanillaBiome == null) continue;
                
                // Only copy if RP biome doesn't already have fish types defined
                if (rpBiome.fishTypes == null && vanillaBiome.fishTypes != null)
                {
                    rpBiome.fishTypes = new BiomeFishTypes();
                    
                    // Deep copy the fish lists
                    if (vanillaBiome.fishTypes.freshwater_Common != null)
                        rpBiome.fishTypes.freshwater_Common = new List<FishChance>(vanillaBiome.fishTypes.freshwater_Common);
                    if (vanillaBiome.fishTypes.freshwater_Uncommon != null)
                        rpBiome.fishTypes.freshwater_Uncommon = new List<FishChance>(vanillaBiome.fishTypes.freshwater_Uncommon);
                    if (vanillaBiome.fishTypes.saltwater_Common != null)
                        rpBiome.fishTypes.saltwater_Common = new List<FishChance>(vanillaBiome.fishTypes.saltwater_Common);
                    if (vanillaBiome.fishTypes.saltwater_Uncommon != null)
                        rpBiome.fishTypes.saltwater_Uncommon = new List<FishChance>(vanillaBiome.fishTypes.saltwater_Uncommon);
                    
                    // Copy the rare catches set maker
                    rpBiome.fishTypes.rareCatchesSetMaker = vanillaBiome.fishTypes.rareCatchesSetMaker;
                    
                    copied++;
                }
            }
            
            if (Controller.DebugEnabled && copied > 0)
            {
                RPLog.Debug($"Copied fish types to {copied} biomes");
            }
        }
        
        // ========== MUTATOR COMPATIBILITY ==========
        
        /// <summary>
        /// Make RP biomes compatible with Odyssey tile mutators
        /// </summary>
        private static void PatchTileMutators()
        {
            int mutatorsModified = 0;
            
            foreach (TileMutatorDef mutator in DefDatabase<TileMutatorDef>.AllDefs)
            {
                if (mutator.biomeWhitelist == null) 
                    continue;
                
                bool modified = false;
                var biomesToAdd = new List<BiomeDef>();
                
                // Check each vanilla biome in the whitelist
                foreach (var vanillaBiome in mutator.biomeWhitelist.ToList())
                {
                    // Find if this vanilla biome has an RP equivalent
                    foreach (var mapping in BiomeContentHelper.BiomeMappings)
                    {
                        if (mapping.Value == vanillaBiome.defName)
                        {
                            var rpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                            if (rpBiome != null && !mutator.biomeWhitelist.Contains(rpBiome))
                            {
                                biomesToAdd.Add(rpBiome);
                                modified = true;
                            }
                        }
                    }
                }
                
                // Add all new biomes at once
                foreach (var biome in biomesToAdd)
                {
                    mutator.biomeWhitelist.Add(biome);
                }
                
                // Rebuild the HashSet if we modified the list
                if (modified)
                {
                    RebuildBiomeSets(mutator);
                    mutatorsModified++;
                }
            }
            
            if (Controller.DebugEnabled)
            {
                RPLog.Debug($"Modified {mutatorsModified} tile mutators for landmark compatibility");
            }
        }
        
        // ========== LANDMARK SPECIAL CASES ==========
        
        /// <summary>
        /// Apply special case rules for specific landmarks
        /// </summary>
        private static void ApplySpecialLandmarkCases()
        {
            // Special case: Oasis mutator should strongly prefer RP_Oasis
            var oasisMutator = DefDatabase<TileMutatorDef>.GetNamed("Oasis", false);
            if (oasisMutator?.biomeWhitelist != null)
            {
                var rpOasisBiome = DefDatabase<BiomeDef>.GetNamed("RP_Oasis", false);
                if (rpOasisBiome != null && !oasisMutator.biomeWhitelist.Contains(rpOasisBiome))
                {
                    oasisMutator.biomeWhitelist.Add(rpOasisBiome);
                    RebuildBiomeSets(oasisMutator);
                    
                    if (Controller.DebugEnabled)
                        RPLog.Debug("Added RP_Oasis to Oasis mutator whitelist");
                }
            }
            
            ApplyBlacklistSpecialCase("Oasis", new[] { "RP_Steppe", "RP_Permafrost" });
        }

        public static void RebuildBiomeSets(TileMutatorDef mutator)
        {
            if (mutator == null)
                return;
            WhitelistSet?.SetValue(mutator, mutator.biomeWhitelist == null ? null : new HashSet<BiomeDef>(mutator.biomeWhitelist));
            BlacklistSet?.SetValue(mutator, mutator.biomeBlacklist == null ? null : new HashSet<BiomeDef>(mutator.biomeBlacklist));
        }

        private static readonly FieldInfo WhitelistSet = AccessTools.Field(typeof(TileMutatorDef), "biomeWhitelistSet");
        private static readonly FieldInfo BlacklistSet = AccessTools.Field(typeof(TileMutatorDef), "biomeBlacklistSet");
        
        /// <summary>
        /// Helper method to add biomes to mutator blacklists
        /// </summary>
        private static void ApplyBlacklistSpecialCase(string mutatorName, string[] biomeNames)
        {
            var mutator = DefDatabase<TileMutatorDef>.GetNamed(mutatorName, false);
            if (mutator == null) 
                return;
            
            // Initialize blacklist if needed
            if (mutator.biomeBlacklist == null)
                mutator.biomeBlacklist = new List<BiomeDef>();
            
            bool modified = false;
            foreach (var biomeName in biomeNames)
            {
                var biome = DefDatabase<BiomeDef>.GetNamed(biomeName, false);
                if (biome != null && !mutator.biomeBlacklist.Contains(biome))
                {
                    mutator.biomeBlacklist.Add(biome);
                    modified = true;
                }
            }
            
            if (modified)
            {
                RebuildBiomeSets(mutator);
                
                if (Controller.DebugEnabled)
                {
                    RPLog.Debug($"Added blacklist special cases for {mutatorName}");
                }
            }
        }
        
        // ========== HARMONY PATCHES ==========
        
        /// <summary>
        /// Apply Harmony patches for landmark spawning control
        /// </summary>
        private static void ApplyLandmarkPatches()
        {
            var harmony = Controller.harmony;
            if (harmony == null)
            {
                RPLog.Error("Harmony instance not available for Odyssey patching");
                return;
            }
            
            // Apply coverage enforcement patch (always active when Odyssey is loaded)
            var ensureCoveragePatch = new HarmonyMethod(typeof(LandmarkPatches), nameof(LandmarkPatches.EnsureOasisCoverage_Postfix));
            ensureCoveragePatch.priority = Priority.Last;
            harmony.Patch(
                AccessTools.Method(typeof(WorldGenStep_Landmarks), "GenerateFresh"),
                postfix: ensureCoveragePatch
            );
            
            if (Controller.DebugEnabled)
            {
                RPLog.Debug("Patched Oasis coverage for RP_Oasis biome");
            }
        }
        
        /// <summary>
        /// Inner class containing the Harmony patch methods
        /// </summary>
        private static class LandmarkPatches
        {
            /// <summary>
            /// Ensure minimum coverage of oasis landmarks in oasis biomes; when Tier 2 hydrology is on, sync Lake landmarks from RP2 hydrology.
            /// </summary>
            public static void EnsureOasisCoverage_Postfix()
            {
                if (!Instance.IsActive)
                    return;

                // Safer way to get surface layer (needed for Oasis and Tier 2 lake sync)
                PlanetLayer surfaceLayer = null;
                if (Find.WorldGrid?.PlanetLayers != null)
                {
                    foreach (var kvp in Find.WorldGrid.PlanetLayers)
                    {
                        if (kvp.Value?.Def?.defName == "Surface")
                        {
                            surfaceLayer = kvp.Value;
                            break;
                        }
                    }
                }
                
                if (surfaceLayer == null)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Warning("Could not find surface layer - world might not be fully initialized");
                    return;
                }

                if (Instance.MinOasisLandmarkCoverageValue > 0f)
                {
                    RunOasisCoverage(surfaceLayer);
                }
            }

            private static void RunOasisCoverage(PlanetLayer surfaceLayer)
            {
                // Find all RP_Oasis tiles in the world (strictly land tiles)
                var oasisBiomeTiles = new List<int>();
                for (int i = 0; i < Find.WorldGrid.TilesCount; i++)
                {
                    var tile = Find.WorldGrid[i];
                    if (tile != null && !tile.WaterCovered && tile.PrimaryBiome?.defName == "RP_Oasis")
                    {
                        oasisBiomeTiles.Add(i);
                    }
                }
                
                if (oasisBiomeTiles.Count == 0)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Debug("No RP_Oasis tiles found in world");
                    return;
                }
                
                // Count how many already have Oasis landmarks
                int tilesWithLandmarks = 0;
                foreach (int tileId in oasisBiomeTiles)
                {
                    var landmark = Find.World.landmarks[new PlanetTile(tileId)];
                    if (landmark?.def.defName == "Oasis")
                    {
                        tilesWithLandmarks++;
                    }
                }
                
                // Calculate minimum required based on coverage setting
                int minRequired = Mathf.CeilToInt(oasisBiomeTiles.Count * Instance.MinOasisLandmarkCoverageValue);
                
                if (tilesWithLandmarks >= minRequired)
                {
                    if (Controller.DebugEnabled)
                    {
                        float coverage = (float)tilesWithLandmarks / oasisBiomeTiles.Count;
                        RPLog.Debug($"Oasis landmark coverage sufficient: {coverage:P} " +
                                  $"({tilesWithLandmarks}/{oasisBiomeTiles.Count} tiles)");
                    }
                    return;
                }
                
                // Need to add more landmarks
                int needToAdd = minRequired - tilesWithLandmarks;
                
                // Find eligible tiles (no existing landmark)
                var eligibleTiles = oasisBiomeTiles.Where(tileId => 
                {
                    var landmark = Find.World.landmarks[new PlanetTile(tileId)];
                    return landmark == null;
                }).ToList();
                
                if (eligibleTiles.Count == 0)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Warning($"No eligible RP_Oasis tiles for additional landmarks");
                    return;
                }
                
                // Add landmarks to random eligible tiles
                var oasisLandmarkDef = DefDatabase<LandmarkDef>.GetNamed("Oasis", false);
                if (oasisLandmarkDef == null)
                    return;
                    
                eligibleTiles.Shuffle();
                int added = 0;
                
                for (int i = 0; i < Mathf.Min(needToAdd, eligibleTiles.Count); i++)
                {
                    var planetTile = new PlanetTile(eligibleTiles[i]);
                    Find.World.landmarks.AddLandmark(oasisLandmarkDef, planetTile, surfaceLayer, true);
                    added++;
                }
                
                if (Controller.DebugEnabled)
                {
                    float newCoverage = (float)(tilesWithLandmarks + added) / oasisBiomeTiles.Count;
                    RPLog.Debug($"Added {added} Oasis landmarks to reach minimum coverage " +
                              $"(new coverage: {newCoverage:P})");
                }
            }
        }
        
        // ========== INTEGRATION CONFIGURATION METHODS ==========
        
        /// <summary>
        /// Render UI for Odyssey integration settings.
        /// Called by IntegrationConfiguration.RenderUI().
        /// </summary>
        public void RenderIntegrationUI(Listing_Standard list)
        {
            if (!IsActive) return;
            
            list.Gap(12f);
            Text.Font = GameFont.Medium;
            list.Label("Odyssey DLC");
            Text.Font = GameFont.Small;
            list.Gap(6f);
            
            // Oasis landmark coverage slider
            float coverage = MinOasisLandmarkCoverageValue;
            list.Label($"Minimum Oasis Landmark Coverage: {coverage:P0}");
            MinOasisLandmarkCoverageValue = list.Slider(coverage, 0f, 1f);
            
            list.Gap(4f);
            Text.Font = GameFont.Tiny;
            list.Label("Controls how many RP_Oasis biome tiles must have Odyssey Oasis landmarks.");
            list.Label("0% = vanilla behavior, 100% = all Oasis tiles have landmarks");
            Text.Font = GameFont.Small;
        }
        
        /// <summary>
        /// Save/load settings via Scribe.
        /// Called by IntegrationConfiguration.ExposeData().
        /// </summary>
        public void ExposeData()
        {
            // odysseyMinOasisLandmarkCoverage is scribed by IntegrationConfiguration.
        }
        
        /// <summary>
        /// Reset settings to default values.
        /// Called by IntegrationConfiguration.ResetToDefaults().
        /// </summary>
        public void ResetToDefaults()
        {
            MinOasisLandmarkCoverageValue = 0f;
        }
    }
}
