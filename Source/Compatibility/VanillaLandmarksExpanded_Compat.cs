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
    /// Compatibility module for Vanilla Landmarks Expanded (VLE).
    /// PackageId: VanillaExpanded.VExplorationE
    /// 
    /// VLE uses TileMutatorDefs with biome whitelists/blacklists.
    /// This module injects RP2 biomes into those lists based on their vanilla equivalents.
    /// </summary>
    [StaticConstructorOnStartup]
    public class VLE_Compat : ICompatibilityModule
    {
        private static VLE_Compat _instance;
        public static VLE_Compat Instance => _instance ?? (_instance = new VLE_Compat());
        
        public static bool IsEnabled => Instance.IsActive;

        static VLE_Compat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "VanillaExpanded.VExplorationE";
        public string ModName => "Vanilla Landmarks Expanded";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }
        public void RenderIntegrationUI(Listing_Standard list) { }
        public void ExposeData() { }
        public void ResetToDefaults() { }

        // Biome mapping: Vanilla -> List of RP2 biomes that should match
        private static readonly Dictionary<string, List<string>> BiomeEquivalents = new Dictionary<string, List<string>>
        {
            { "TemperateForest", new List<string> { "RP_Prairie" } },
            { "Grasslands", new List<string> { "RP_Prairie" } },
            { "AridShrubland", new List<string> { "RP_Savanna" } },
            { "BorealForest", new List<string> { "RP_Steppe" } },
            { "Tundra", new List<string> { "RP_Permafrost", "RP_Steppe" } },
            { "IceSheet", new List<string> { "RP_Permafrost" } },
            { "Desert", new List<string> { "RP_Oasis", "RP_ColdDesert" } },
            { "ExtremeDesert", new List<string> { "RP_ColdDesert" } }
        };

        public void Initialize()
        {
            try
            {
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.Debug("Vanilla Landmarks Expanded not detected");
                    return;
                }
                
                RPLog.Debug("Vanilla Landmarks Expanded detected, injecting biome compatibility");
                
                ApplyBiomePatches();
                
                RPLog.Debug("Vanilla Landmarks Expanded compatibility initialized successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"Error initializing VLE compatibility: {e}");
                IsActive = false;
            }
        }

        private void ApplyBiomePatches()
        {
            // Find the TileMutatorDef type
            Type tileMutatorDefType = GenTypes.GetTypeInAnyAssembly("RimWorld.TileMutatorDef");
            if (tileMutatorDefType == null)
            {
                RPLog.Warning("Could not find type RimWorld.TileMutatorDef");
                return;
            }

            // Get all defs of this type (AllDefs is a property, not a method)
            System.Collections.IEnumerable allMutators = (System.Collections.IEnumerable)GenGeneric.GetStaticPropertyOnGenericType(
                typeof(DefDatabase<>), tileMutatorDefType, "AllDefs");

            if (allMutators == null) return;

            // Reflection to access lists
            FieldInfo whitelistField = tileMutatorDefType.GetField("biomeWhitelist", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo blacklistField = tileMutatorDefType.GetField("biomeBlacklist", BindingFlags.Public | BindingFlags.Instance);

            int patchedCount = 0;

            foreach (Def mutator in allMutators)
            {
                if (!string.Equals(mutator.modContentPack?.PackageId, "VanillaExpanded.VExplorationE", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool patched = false;
                
                // Patch Whitelist
                if (whitelistField != null)
                {
                    List<BiomeDef> whitelist = whitelistField.GetValue(mutator) as List<BiomeDef>;
                    if (whitelist != null && whitelist.Count > 0)
                    {
                        patched |= InjectBiomes(whitelist);
                    }
                }

                // Patch Blacklist
                if (blacklistField != null)
                {
                    List<BiomeDef> blacklist = blacklistField.GetValue(mutator) as List<BiomeDef>;

                    if (blacklist != null && blacklist.Count > 0)
                    {
                        patched |= InjectBiomes(blacklist);
                    }
                }

                if (patched)
                {
                    if (mutator is TileMutatorDef tileMutator)
                        OdysseyCompat.RebuildBiomeSets(tileMutator);
                    patchedCount++;
                }
            }
            
            if (Controller.DebugEnabled)
                RPLog.Debug($"Patched {patchedCount} VLE TileMutators with RP2 biomes");
        }

        private bool InjectBiomes(List<BiomeDef> list)
        {
            bool changed = false;
            // Create a temporary list of biomes to add to avoid modification during enumeration
            HashSet<string> biomesToAdd = new HashSet<string>();

            foreach (BiomeDef biome in list)
            {
                if (BiomeEquivalents.TryGetValue(biome.defName, out List<string> rpBiomes))
                {
                    foreach (string rpBiomeName in rpBiomes)
                    {
                        biomesToAdd.Add(rpBiomeName);
                    }
                }
            }

            foreach (string defName in biomesToAdd)
            {
                // Don't add if already present
                if (list.Any(b => b.defName == defName)) continue;

                BiomeDef rpDef = DefDatabase<BiomeDef>.GetNamedSilentFail(defName);
                if (rpDef != null)
                {
                    list.Add(rpDef);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
