using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.Compatibility;

namespace Planets.Biomes.Core
{
    /// <summary>
    /// Utility class for copying content between biomes.
    /// Used by compatibility modules to add plants, animals, and other content from vanilla/modded biomes to RRP biomes.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BiomeContentHelper
    {
        /// <summary>
        /// Static constructor handles all initialization after defs are loaded
        /// </summary>
        static BiomeContentHelper()
        {
            try
            {
                RPLog.Debug("Initializing vanilla biome content copying");
                
                // Copy all general content
                CopyPlantsFromDefs();
                CopyAnimalsFromDefs();
                CopyModContentFromParentBiomes();
                CopyDiseases();
                CopyPackAnimals();
                CopySoundAmbient();
                
                // Clear caches to ensure our changes take effect
                ClearBiomeCaches();
                                
                RPLog.Debug("Vanilla biome content copying complete");
            }
            catch (Exception e)
            {
                RPLog.Error($"Error initializing vanilla biome content: {e}");
            }
        }

        // Standard mappings of RP biomes to their vanilla counterparts (for content copying)
        public static readonly Dictionary<string, string> BiomeMappings = new Dictionary<string, string>
        {
            { "RP_Permafrost", "Tundra" },
            { "RP_Prairie", "TemperateForest" },
            { "RP_Steppe", "BorealForest" },
            { "RP_Savanna", "AridShrubland" },
            { "RP_Oasis", "Desert" },
            { "RP_ColdDesert", "AridShrubland" }  // Cold arid (Gobi-type); AridShrubland closest for plants/animals
        };

        /// <summary>
        /// Parents used only when copying plants, animals and diseases.
        /// Steppes take arid shrubland and tundra, not boreal forest, so pines are not pulled in.
        /// BiomeMappings stays as it is for mutator compatibility.
        /// </summary>
        public static readonly Dictionary<string, string[]> ContentParents = new Dictionary<string, string[]>
        {
            { "RP_Permafrost", new[] { "Tundra" } },
            { "RP_Prairie", new[] { "TemperateForest" } },
            { "RP_Steppe", new[] { "AridShrubland", "Tundra" } },
            { "RP_Savanna", new[] { "AridShrubland" } },
            { "RP_Oasis", new[] { "Desert" } },
            { "RP_ColdDesert", new[] { "AridShrubland" } }
        };

        private static List<KeyValuePair<string, string>> contentPairs;

        private static List<KeyValuePair<string, string>> ContentPairs
        {
            get
            {
                if (contentPairs != null)
                    return contentPairs;
                contentPairs = new List<KeyValuePair<string, string>>();
                foreach (var mapping in ContentParents)
                {
                    foreach (string parent in mapping.Value)
                        contentPairs.Add(new KeyValuePair<string, string>(mapping.Key, parent));
                }
                return contentPairs;
            }
        }
        
        // ===== Public reflection helpers to access private BiomeDef fields =====
        // These are public so all compatibility modules can use them without duplication
        
        public static List<BiomeAnimalRecord> GetWildAnimals(BiomeDef biome)
        {
            var field = typeof(BiomeDef).GetField("wildAnimals", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(biome) as List<BiomeAnimalRecord>;
        }

        public static List<BiomeAnimalRecord> GetPollutionWildAnimals(BiomeDef biome)
        {
            var field = typeof(BiomeDef).GetField("pollutionWildAnimals", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(biome) as List<BiomeAnimalRecord>;
        }

        public static List<BiomeAnimalRecord> GetCoastalWildAnimals(BiomeDef biome)
        {
            var field = typeof(BiomeDef).GetField("coastalWildAnimals", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(biome) as List<BiomeAnimalRecord>;
        }

        public static List<BiomeDiseaseRecord> GetDiseases(BiomeDef biome)
        {
            var field = typeof(BiomeDef).GetField("diseases", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(biome) as List<BiomeDiseaseRecord>;
        }

        public static List<ThingDef> GetAllowedPackAnimals(BiomeDef biome)
        {
            var field = typeof(BiomeDef).GetField("allowedPackAnimals", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(biome) as List<ThingDef>;
        }
        
        // ===== Plant copying methods =====
        
        /// <summary>
        /// Copy all plants from ThingDefs that specify wildBiomes.
        /// This catches all modded plants that properly specify which biomes they spawn in.
        /// </summary>
        public static void CopyPlantsFromDefs(Predicate<ThingDef> filter = null, Func<float, string, float> commonalityModifier = null)
        {
            int entriesAdded = 0;
            
            foreach (ThingDef plantDef in DefDatabase<ThingDef>.AllDefs)
            {
                if (plantDef.plant?.wildBiomes == null) 
                    continue;
                    
                if (filter != null && !filter(plantDef))
                    continue;
                
                foreach (var wildBiome in plantDef.plant.wildBiomes)
                {
                    // Check each mapping to see if this plant should be added
                    foreach (var mapping in ContentPairs)
                    {
                        if (wildBiome.biome.defName == mapping.Value)
                        {
                            var rrpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                            if (rrpBiome == null) continue;
                            
                            // Skip if already exists
                            if (rrpBiome.wildPlants.Any(p => p.plant == plantDef))
                                continue;
                            
                            // Apply special rules
                            if (ShouldSkipPlant(plantDef, mapping.Key))
                                continue;
                            
                            // Calculate commonality
                            float commonality = wildBiome.commonality;
                            if (commonalityModifier != null)
                                commonality = commonalityModifier(commonality, mapping.Key);
                            else
                                commonality = GetDefaultPlantCommonality(commonality, mapping.Key);
                            
                            // Add the plant
                            var newRecord = new BiomePlantRecord();
                            newRecord.plant = plantDef;
                            newRecord.commonality = commonality;
                            
                            rrpBiome.wildPlants.Add(newRecord);
                            entriesAdded++;
                        }
                    }
                }
            }
            
            if (Controller.DebugEnabled)
            {
                RPLog.Debug($"Added {entriesAdded} plant entries from ThingDefs");
            }
        }
        
        /// <summary>
        /// Copy unofficial mod plants and animals that were written onto a parent biome's lists,
        /// which is how ReGrowth adds its plants.
        /// </summary>
        private static void CopyModContentFromParentBiomes()
        {
            foreach (var mapping in ContentParents)
            {
                string rpBiome = mapping.Key;
                foreach (string parent in mapping.Value)
                {
                    CopyPlants(parent, rpBiome,
                        plant => IsUnofficialContent(plant) && !ShouldSkipPlant(plant, rpBiome),
                        commonality => rpBiome == "RP_Oasis" ? commonality : commonality * 0.5f);
                    CopyAnimals(parent, rpBiome,
                        animal => IsUnofficialContent(animal) && !ShouldSkipAnimal(animal, rpBiome),
                        commonality => commonality * 0.5f);
                }
            }
        }

        private static bool IsUnofficialContent(Def def)
        {
            return def?.modContentPack != null && !def.modContentPack.IsOfficialMod;
        }

        /// <summary>
        /// Copy plants directly from one biome to another
        /// </summary>
        public static void CopyPlants(string fromBiomeName, string toBiomeName, Predicate<ThingDef> filter = null, Func<float, float> commonalityModifier = null)
        {
            var fromBiome = DefDatabase<BiomeDef>.GetNamed(fromBiomeName, false);
            var toBiome = DefDatabase<BiomeDef>.GetNamed(toBiomeName, false);
            
            if (fromBiome == null || toBiome == null)
                return;
                
            int added = 0;
            foreach (var plantRecord in fromBiome.wildPlants)
            {
                if (filter != null && !filter(plantRecord.plant))
                    continue;
                    
                if (toBiome.wildPlants.Any(p => p.plant == plantRecord.plant))
                    continue;
                    
                var newRecord = new BiomePlantRecord();
                newRecord.plant = plantRecord.plant;
                newRecord.commonality = commonalityModifier != null ? 
                    commonalityModifier(plantRecord.commonality) : 
                    plantRecord.commonality;
                    
                toBiome.wildPlants.Add(newRecord);
                added++;
            }
            
            if (Controller.DebugEnabled && added > 0)
            {
                RPLog.Debug($"Copied {added} plants from {fromBiomeName} to {toBiomeName}");
            }
        }
        
        // ===== Animal copying methods =====
        
        /// <summary>
        /// Copy all animals from PawnKindDefs that specify wildBiomes.
        /// This catches all modded animals that properly specify which biomes they spawn in.
        /// </summary>
        public static void CopyAnimalsFromDefs(Predicate<PawnKindDef> filter = null, Func<float, string, float> commonalityModifier = null)
        {
            int wildAdded = 0;
            
            foreach (PawnKindDef animalDef in DefDatabase<PawnKindDef>.AllDefs)
            {
                if (animalDef.RaceProps?.wildBiomes == null) 
                    continue;
                    
                if (filter != null && !filter(animalDef))
                    continue;
                
                foreach (var wildBiome in animalDef.RaceProps.wildBiomes)
                {
                    // Regular wild animals
                    foreach (var mapping in ContentPairs)
                    {
                        if (wildBiome.biome.defName == mapping.Value)
                        {
                            var rrpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                            if (rrpBiome == null) continue;
                            
                            var wildAnimals = GetWildAnimals(rrpBiome);
                            if (wildAnimals == null) continue;
                            
                            // Skip if already exists
                            if (wildAnimals.Any(a => a.animal == animalDef))
                                continue;
                            
                            // Apply special rules
                            if (ShouldSkipAnimal(animalDef, mapping.Key))
                                continue;
                            
                            // Calculate commonality
                            float commonality = wildBiome.commonality;
                            if (commonalityModifier != null)
                                commonality = commonalityModifier(commonality, mapping.Key);
                            else
                                commonality = GetDefaultAnimalCommonality(commonality, mapping.Key);
                            
                            // Add the animal
                            var newRecord = new BiomeAnimalRecord();
                            newRecord.animal = animalDef;
                            newRecord.commonality = commonality;
                            
                            wildAnimals.Add(newRecord);
                            wildAdded++;
                        }
                    }
                }
                

            }
            
            if (Controller.DebugEnabled)
            {
                RPLog.Debug($"Added {wildAdded} wild animal entries from PawnKindDefs");
            }
        }
        
        /// <summary>
        /// Copy animals directly from one biome to another
        /// </summary>
        public static void CopyAnimals(string fromBiomeName, string toBiomeName, Predicate<PawnKindDef> filter = null, Func<float, float> commonalityModifier = null)
        {
            var fromBiome = DefDatabase<BiomeDef>.GetNamed(fromBiomeName, false);
            var toBiome = DefDatabase<BiomeDef>.GetNamed(toBiomeName, false);
            
            if (fromBiome == null || toBiome == null)
                return;
                
            var fromAnimals = GetWildAnimals(fromBiome);
            var toAnimals = GetWildAnimals(toBiome);
            
            if (fromAnimals == null || toAnimals == null)
                return;
                
            int added = 0;
            foreach (var animalRecord in fromAnimals)
            {
                if (filter != null && !filter(animalRecord.animal))
                    continue;
                    
                if (toAnimals.Any(a => a.animal == animalRecord.animal))
                    continue;
                    
                var newRecord = new BiomeAnimalRecord();
                newRecord.animal = animalRecord.animal;
                newRecord.commonality = commonalityModifier != null ? 
                    commonalityModifier(animalRecord.commonality) : 
                    animalRecord.commonality;
                    
                toAnimals.Add(newRecord);
                added++;
            }
            
            if (Controller.DebugEnabled && added > 0)
            {
                RPLog.Debug($"Copied {added} animals from {fromBiomeName} to {toBiomeName}");
            }
        }
        
        /// <summary>
        /// Add a pollution animal to a biome (Biotech)
        /// </summary>
        public static void AddPollutionAnimal(BiomeDef biome, PawnKindDef animal, float commonality)
        {
            // Initialize if needed
            if (GetPollutionWildAnimals(biome) == null)
            {
                var field = typeof(BiomeDef).GetField("pollutionWildAnimals", BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(biome, new List<BiomeAnimalRecord>());
            }
            
            var pollutionAnimals = GetPollutionWildAnimals(biome);
            
            // Skip if already exists
            if (pollutionAnimals.Any(a => a.animal == animal))
                return;
                
            var newRecord = new BiomeAnimalRecord();
            newRecord.animal = animal;
            newRecord.commonality = commonality;
            
            pollutionAnimals.Add(newRecord);
        }
        
        /// <summary>
        /// Add a coastal animal to a biome (Odyssey)
        /// </summary>
        public static void AddCoastalAnimal(BiomeDef biome, PawnKindDef animal, float commonality)
        {
            // Initialize if needed
            if (GetCoastalWildAnimals(biome) == null)
            {
                var field = typeof(BiomeDef).GetField("coastalWildAnimals", BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(biome, new List<BiomeAnimalRecord>());
            }
            
            var coastalAnimals = GetCoastalWildAnimals(biome);
            
            // Skip if already exists
            if (coastalAnimals.Any(a => a.animal == animal))
                return;
                
            var newRecord = new BiomeAnimalRecord();
            newRecord.animal = animal;
            newRecord.commonality = commonality;
            
            coastalAnimals.Add(newRecord);
        }
        
        // ===== Other content copying methods =====
        
        /// <summary>
        /// Copy diseases from vanilla biomes to their RRP equivalents
        /// </summary>
        public static void CopyDiseases()
        {
            foreach (var mapping in ContentPairs)
            {
                var rrpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                var vanillaBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Value, false);
                
                if (rrpBiome == null || vanillaBiome == null) continue;
                
                var rrpDiseases = GetDiseases(rrpBiome);
                var vanillaDiseases = GetDiseases(vanillaBiome);
                
                if (rrpDiseases == null || vanillaDiseases == null) continue;
                
                // Only add diseases that aren't already defined
                foreach (var disease in vanillaDiseases)
                {
                    if (!rrpDiseases.Any(d => d.diseaseInc == disease.diseaseInc))
                    {
                        var newDisease = new BiomeDiseaseRecord();
                        newDisease.diseaseInc = disease.diseaseInc;
                        newDisease.commonality = disease.commonality;
                        rrpDiseases.Add(newDisease);
                    }
                }
            }
        }

        /// <summary>
        /// Copy ambient sounds from vanilla biomes
        /// </summary>
        public static void CopySoundAmbient()
        {
            foreach (var mapping in BiomeMappings)
            {
                var rrpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                var vanillaBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Value, false);
                
                if (rrpBiome == null || vanillaBiome == null) continue;
                
                // Only copy if RRP biome doesn't have sounds defined
                if (rrpBiome.soundsAmbient == null || rrpBiome.soundsAmbient.Count == 0)
                {
                    rrpBiome.soundsAmbient = vanillaBiome.soundsAmbient;
                }
            }
        }
        
        /// <summary>
        /// Copy allowed pack animals from vanilla biomes to their RRP equivalents
        /// </summary>
        public static void CopyPackAnimals()
        {
            foreach (var mapping in BiomeMappings)
            {
                var rrpBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Key, false);
                var vanillaBiome = DefDatabase<BiomeDef>.GetNamed(mapping.Value, false);
                
                if (rrpBiome == null || vanillaBiome == null) continue;
                
                var rrpPackAnimals = GetAllowedPackAnimals(rrpBiome);
                var vanillaPackAnimals = GetAllowedPackAnimals(vanillaBiome);
                
                if (rrpPackAnimals == null || vanillaPackAnimals == null) continue;
                
                // Only add pack animals that aren't already defined
                foreach (var packAnimal in vanillaPackAnimals)
                {
                    if (!rrpPackAnimals.Contains(packAnimal))
                    {
                        rrpPackAnimals.Add(packAnimal);
                    }
                }
            }
        }
        
        /// <summary>
        /// Clear cached data in biomes to ensure changes take effect
        /// </summary>
        public static void ClearBiomeCaches()
        {
            // Get the private fields using reflection
            var cachedWildPlantsField = typeof(BiomeDef).GetField("cachedWildPlants", BindingFlags.NonPublic | BindingFlags.Instance);
            var cachedPlantCommonalitiesField = typeof(BiomeDef).GetField("cachedPlantCommonalities", BindingFlags.NonPublic | BindingFlags.Instance);
            var cachedAnimalCommonalitiesField = typeof(BiomeDef).GetField("cachedAnimalCommonalities", BindingFlags.NonPublic | BindingFlags.Instance);
            
            foreach (var biome in DefDatabase<BiomeDef>.AllDefs)
            {
                // Clear the cached fields
                if (cachedWildPlantsField != null)
                    cachedWildPlantsField.SetValue(biome, null);
            
                if (cachedPlantCommonalitiesField != null)
                    cachedPlantCommonalitiesField.SetValue(biome, null);
            
                if (cachedAnimalCommonalitiesField != null)
                    cachedAnimalCommonalitiesField.SetValue(biome, null);
            }
        
            if (Controller.DebugEnabled)
            {
                RPLog.Debug("Cleared biome caches");
            }
        }
        
        // ===== Special rules and filters =====
        
        /// <summary>
        /// Determines if a plant should be skipped for a specific RRP biome.
        /// Contains biome-specific ecological rules and mod compatibility checks.
        /// </summary>
        public static bool ShouldSkipPlant(ThingDef plant, string rrpBiomeName)
        {
            // Medieval Overhaul compatibility - check first as it's mod-specific
            if (MedievalOverhaulCompat.ShouldSkipPlant(plant))
            {
                return true;
            }
            
            // Steppe: Cold, dry grassland - sparse trees in sheltered valleys
            // Allow only steppe-appropriate trees (birch and poplar) for wood resources
            if (rrpBiomeName == "RP_Steppe" && plant.defName.Contains("Tree"))
            {
                // Allow sparse birch and poplar (real steppes have these near water/valleys)
                if (plant.defName == "Plant_TreeBirch" || plant.defName == "Plant_TreePoplar")
                    return false; // Don't skip these - allow them at reduced commonality
                
                return true; // Skip all other trees
            }
            
            // Prairie: Mixed grassland with scattered trees
            // The XML already defines TreeOak and TreePoplar at appropriate densities
            // Skip dense forest trees that wouldn't naturally occur in prairies
            if (rrpBiomeName == "RP_Prairie" && plant.defName.Contains("Tree"))
            {
                // Skip tropical and dense forest trees inappropriate for prairie
                if (plant.defName == "Plant_TreePine" ||      // Coniferous forest tree
                    plant.defName == "Plant_TreeBirch" ||      // Northern forest tree  
                    plant.defName == "Plant_TreeTeak" ||       // Tropical tree
                    plant.defName == "Plant_TreeCecropia" ||   // Tropical tree
                    plant.defName == "Plant_TreeBamboo" ||     // Tropical/subtropical
                    plant.defName == "Plant_TreePalm" ||       // Tropical tree
                    plant.defName == "Plant_TreeCocoa")        // Tropical tree
                {
                    return true;
                }
                // Allow other trees to be auto-added at reduced commonality
                return false;
            }
            
            // Oasis: Desert spring with palms - no temperate berries
            if (rrpBiomeName == "RP_Oasis" && plant.defName == "Plant_Berry")
            {
                return true;
            }
            
            // Don't skip anima trees - they have gameplay significance
            // Let them auto-add with heavily reduced commonality from the mapping
            
            return false;
        }
        
        /// <summary>
        /// Determines if an animal should be skipped for a specific RRP biome.
        /// Contains biome-specific ecological rules and mod compatibility checks.
        /// </summary>
        public static bool ShouldSkipAnimal(PawnKindDef animal, string rrpBiomeName)
        {
            // Medieval Overhaul compatibility - check first as it's mod-specific
            if (MedievalOverhaulCompat.ShouldSkipAnimal(animal))
            {
                return true;
            }
            
            // Future biome-specific filters could go here if needed
            // But commonality adjustments (halving) generally handle balance well
            
            return false;
        }
        
        private static float GetDefaultPlantCommonality(float baseCommonality, string rrpBiomeName)
        {
            // Default: halve commonality for most biomes
            // Oasis gets special treatment
            return rrpBiomeName == "RP_Oasis" ? baseCommonality : baseCommonality / 2;
        }
        
        private static float GetDefaultAnimalCommonality(float baseCommonality, string rrpBiomeName)
        {
            // All get half commonality for now
            return baseCommonality / 2;
        }
    }
}
