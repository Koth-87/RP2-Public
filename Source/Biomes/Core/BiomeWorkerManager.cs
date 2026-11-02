using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;

namespace Planets.Biomes.Core
{
    /// <summary>
    /// Injects GenericRP2Worker into external (non-RP2) biomes when they are calibrated or have user overrides,
    /// so they score using RP2's climate model. Restores original worker when no longer calibrated/overridden.
    /// </summary>
    public static class BiomeWorkerManager
    {
        private static readonly BiomeWorker GenericWorker = new GenericRP2Worker();
        private static readonly Dictionary<string, BiomeWorker> OriginalWorkers = new Dictionary<string, BiomeWorker>();
        private static FieldInfo _workerField;

        private static FieldInfo WorkerField
        {
            get
            {
                if (_workerField != null) return _workerField;
                _workerField = typeof(BiomeDef).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic);
                return _workerField;
            }
        }

        /// <summary>Returns the original (un-injected) <see cref="BiomeWorker"/> for a foreign <see cref="BiomeDef"/>.</summary>
        public static BiomeWorker GetOriginalWorker(BiomeDef biome)
        {
            if (biome?.workerClass == null)
                return null;
            if (OriginalWorkers.TryGetValue(biome.defName, out BiomeWorker cached) && cached != null)
                return cached;
            try
            {
                var instance = (BiomeWorker)Activator.CreateInstance(biome.workerClass);
                OriginalWorkers[biome.defName] = instance;
                return instance;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>True if the biome is handled natively by RP2 (Planets namespace, substituted vanilla/Odyssey worker, or redirect map).</summary>
        public static bool IsNative(BiomeDef biome)
        {
            if (biome?.workerClass == null)
                return false;
            if (biome.workerClass.Namespace != null && biome.workerClass.Namespace.StartsWith("Planets"))
                return true;
            if (Patches.Biomes.Patch_BiomeDef_Worker.IsSubstitutedDefName(biome.defName))
                return true;
            return Patches.Biomes.BiomeWorkerRedirectPatch.IsMappedWorker(biome.workerClass);
        }

        /// <summary>Apply worker injection and def overrides from the current configuration. Call before biome scoring.</summary>
        public static void Apply(BiomeConfiguration config)
        {
            ApplyWorkers(config);
            ApplyDefOverrides(config);
        }

        /// <summary>Apply or clear generic worker injection. Def values are left alone.</summary>
        public static void ApplyWorkers(BiomeConfiguration config)
        {
            if (config == null || WorkerField == null) return;

            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                if (biome.workerClass == null) continue;
                var meta = config.GetMetadata(biome.defName);
                bool shouldUseGeneric = !IsNative(biome) && meta != null && meta.UseRP2ClimateFit;
                try
                {
                    object current = WorkerField.GetValue(biome);
                    if (shouldUseGeneric && current != GenericWorker)
                    {
                        if (current is BiomeWorker existing && existing != GenericWorker)
                            OriginalWorkers[biome.defName] = existing;
                        WorkerField.SetValue(biome, GenericWorker);
                    }
                    else if (!shouldUseGeneric && current == GenericWorker)
                    {
                        WorkerField.SetValue(biome, null);
                    }
                }
                catch (Exception e)
                {
                    RPLog.Warning($"[BiomeWorkerManager] Failed to set worker for {biome.defName}: {e.Message}");
                }
            }
        }

        /// <summary>Put every biome def's density fields back to the XML values captured at startup.</summary>
        public static void RestoreAllDefs()
        {
            BiomeDefOriginals.RestoreAll();
        }

        /// <summary>Restore every biome def, then apply the overrides currently stored in the configuration.</summary>
        public static void ApplyDefOverrides(BiomeConfiguration config)
        {
            RestoreAllDefs();
            if (config == null) return;
            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                var meta = config.GetMetadata(biome.defName);
                if (meta == null || !meta.UseDefOverrides) continue;
                ApplyOne(biome, meta);
            }
        }

        /// <summary>Copy the configuration's def overrides so a world can save its own copy.</summary>
        public static Dictionary<string, DefOverrideData> CaptureOverrides(BiomeConfiguration config)
        {
            var saved = new Dictionary<string, DefOverrideData>();
            if (config?.BiomeMetadataMap == null) return saved;
            foreach (var kvp in config.BiomeMetadataMap)
            {
                var meta = kvp.Value;
                if (meta == null || !meta.UseDefOverrides || !meta.HasAnyDefOverride) continue;
                saved[kvp.Key] = new DefOverrideData
                {
                    animalDensity = meta.AnimalDensity,
                    plantDensity = meta.PlantDensity,
                    diseaseMtbDays = meta.DiseaseMtbDays,
                    settlementSelectionWeight = meta.SettlementSelectionWeight,
                    campSelectionWeight = meta.CampSelectionWeight,
                    pollutionOffset = meta.PollutionOffset,
                    forageability = meta.Forageability,
                    wildPlantRegrowDays = meta.WildPlantRegrowDays,
                    movementDifficulty = meta.MovementDifficulty,
                    geyserCountFactor = meta.GeyserCountFactor,
                    wildAnimalScariaChance = meta.WildAnimalScariaChance,
                    maxFishPopulation = meta.MaxFishPopulation,
                    allowRoads = meta.AllowRoads,
                    allowRivers = meta.AllowRivers,
                    allowFarmingCamps = meta.AllowFarmingCamps,
                    isExtremeBiome = meta.IsExtremeBiome
                };
            }
            return saved;
        }

        /// <summary>
        /// Restore XML values, then apply the overrides saved with one world.
        /// A null dictionary means an older save: leave the XML values in place.
        /// </summary>
        public static void ApplySavedOverrides(Dictionary<string, DefOverrideData> saved)
        {
            RestoreAllDefs();
            if (saved == null) return;
            foreach (var kvp in saved)
            {
                BiomeDef biome = DefDatabase<BiomeDef>.GetNamedSilentFail(kvp.Key);
                DefOverrideData data = kvp.Value;
                if (biome == null || data == null) continue;
                ApplyOne(biome, data);
            }
        }

        private static void ApplyOne(BiomeDef biome, BiomeMetadata meta)
        {
            if (meta.AnimalDensity.HasValue) biome.animalDensity = meta.AnimalDensity.Value;
            if (meta.PlantDensity.HasValue) biome.plantDensity = meta.PlantDensity.Value;
            if (meta.DiseaseMtbDays.HasValue) biome.diseaseMtbDays = meta.DiseaseMtbDays.Value;
            if (meta.SettlementSelectionWeight.HasValue) biome.settlementSelectionWeight = meta.SettlementSelectionWeight.Value;
            if (meta.CampSelectionWeight.HasValue) biome.campSelectionWeight = meta.CampSelectionWeight.Value;
            if (meta.PollutionOffset.HasValue) biome.pollutionOffset = meta.PollutionOffset.Value;
            if (meta.Forageability.HasValue) biome.forageability = meta.Forageability.Value;
            if (meta.WildPlantRegrowDays.HasValue) biome.wildPlantRegrowDays = meta.WildPlantRegrowDays.Value;
            if (meta.MovementDifficulty.HasValue) biome.movementDifficulty = meta.MovementDifficulty.Value;
            if (meta.GeyserCountFactor.HasValue) biome.geyserCountFactor = meta.GeyserCountFactor.Value;
            if (meta.WildAnimalScariaChance.HasValue) biome.wildAnimalScariaChance = meta.WildAnimalScariaChance.Value;
            if (meta.MaxFishPopulation.HasValue) biome.maxFishPopulation = meta.MaxFishPopulation.Value;
            if (meta.AllowRoads.HasValue) biome.allowRoads = meta.AllowRoads.Value;
            if (meta.AllowRivers.HasValue) biome.allowRivers = meta.AllowRivers.Value;
            if (meta.AllowFarmingCamps.HasValue) biome.allowFarmingCamps = meta.AllowFarmingCamps.Value;
            if (meta.IsExtremeBiome.HasValue) biome.isExtremeBiome = meta.IsExtremeBiome.Value;
        }

        private static void ApplyOne(BiomeDef biome, DefOverrideData data)
        {
            if (data.animalDensity.HasValue) biome.animalDensity = data.animalDensity.Value;
            if (data.plantDensity.HasValue) biome.plantDensity = data.plantDensity.Value;
            if (data.diseaseMtbDays.HasValue) biome.diseaseMtbDays = data.diseaseMtbDays.Value;
            if (data.settlementSelectionWeight.HasValue) biome.settlementSelectionWeight = data.settlementSelectionWeight.Value;
            if (data.campSelectionWeight.HasValue) biome.campSelectionWeight = data.campSelectionWeight.Value;
            if (data.pollutionOffset.HasValue) biome.pollutionOffset = data.pollutionOffset.Value;
            if (data.forageability.HasValue) biome.forageability = data.forageability.Value;
            if (data.wildPlantRegrowDays.HasValue) biome.wildPlantRegrowDays = data.wildPlantRegrowDays.Value;
            if (data.movementDifficulty.HasValue) biome.movementDifficulty = data.movementDifficulty.Value;
            if (data.geyserCountFactor.HasValue) biome.geyserCountFactor = data.geyserCountFactor.Value;
            if (data.wildAnimalScariaChance.HasValue) biome.wildAnimalScariaChance = data.wildAnimalScariaChance.Value;
            if (data.maxFishPopulation.HasValue) biome.maxFishPopulation = data.maxFishPopulation.Value;
            if (data.allowRoads.HasValue) biome.allowRoads = data.allowRoads.Value;
            if (data.allowRivers.HasValue) biome.allowRivers = data.allowRivers.Value;
            if (data.allowFarmingCamps.HasValue) biome.allowFarmingCamps = data.allowFarmingCamps.Value;
            if (data.isExtremeBiome.HasValue) biome.isExtremeBiome = data.isExtremeBiome.Value;
        }

        /// <summary>Clear injected workers so defs fall back to their original workerClass. Optional cleanup.</summary>
        public static void ClearInjectedWorkers()
        {
            if (WorkerField == null) return;
            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                try
                {
                    if (WorkerField.GetValue(biome) == GenericWorker)
                        WorkerField.SetValue(biome, null);
                }
                catch (Exception) { /* ignore */ }
            }
        }
    }

    /// <summary>One biome's def overrides, saved with the world that was generated while they were active.</summary>
    public class DefOverrideData : IExposable
    {
        public float? animalDensity;
        public float? plantDensity;
        public float? diseaseMtbDays;
        public float? settlementSelectionWeight;
        public float? campSelectionWeight;
        public float? pollutionOffset;
        public float? forageability;
        public float? wildPlantRegrowDays;
        public float? movementDifficulty;
        public float? geyserCountFactor;
        public float? wildAnimalScariaChance;
        public float? maxFishPopulation;
        public bool? allowRoads;
        public bool? allowRivers;
        public bool? allowFarmingCamps;
        public bool? isExtremeBiome;

        public void ExposeData()
        {
            Scribe_Values.Look(ref animalDensity, "animalDensity");
            Scribe_Values.Look(ref plantDensity, "plantDensity");
            Scribe_Values.Look(ref diseaseMtbDays, "diseaseMtbDays");
            Scribe_Values.Look(ref settlementSelectionWeight, "settlementSelectionWeight");
            Scribe_Values.Look(ref campSelectionWeight, "campSelectionWeight");
            Scribe_Values.Look(ref pollutionOffset, "pollutionOffset");
            Scribe_Values.Look(ref forageability, "forageability");
            Scribe_Values.Look(ref wildPlantRegrowDays, "wildPlantRegrowDays");
            Scribe_Values.Look(ref movementDifficulty, "movementDifficulty");
            Scribe_Values.Look(ref geyserCountFactor, "geyserCountFactor");
            Scribe_Values.Look(ref wildAnimalScariaChance, "wildAnimalScariaChance");
            Scribe_Values.Look(ref maxFishPopulation, "maxFishPopulation");
            Scribe_Values.Look(ref allowRoads, "allowRoads");
            Scribe_Values.Look(ref allowRivers, "allowRivers");
            Scribe_Values.Look(ref allowFarmingCamps, "allowFarmingCamps");
            Scribe_Values.Look(ref isExtremeBiome, "isExtremeBiome");
        }
    }

    /// <summary>XML values of biome defs, captured once after defs load and before any override is applied.</summary>
    [StaticConstructorOnStartup]
    public static class BiomeDefOriginals
    {
        private struct Snapshot
        {
            public float animalDensity;
            public float plantDensity;
            public float diseaseMtbDays;
            public float settlementSelectionWeight;
            public float campSelectionWeight;
            public float pollutionOffset;
            public float forageability;
            public float wildPlantRegrowDays;
            public float movementDifficulty;
            public float geyserCountFactor;
            public float wildAnimalScariaChance;
            public float maxFishPopulation;
            public bool allowRoads;
            public bool allowRivers;
            public bool allowFarmingCamps;
            public bool isExtremeBiome;
        }

        private static readonly Dictionary<BiomeDef, Snapshot> originals = new Dictionary<BiomeDef, Snapshot>();

        static BiomeDefOriginals()
        {
            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                originals[biome] = new Snapshot
                {
                    animalDensity = biome.animalDensity,
                    plantDensity = biome.plantDensity,
                    diseaseMtbDays = biome.diseaseMtbDays,
                    settlementSelectionWeight = biome.settlementSelectionWeight,
                    campSelectionWeight = biome.campSelectionWeight,
                    pollutionOffset = biome.pollutionOffset,
                    forageability = biome.forageability,
                    wildPlantRegrowDays = biome.wildPlantRegrowDays,
                    movementDifficulty = biome.movementDifficulty,
                    geyserCountFactor = biome.geyserCountFactor,
                    wildAnimalScariaChance = biome.wildAnimalScariaChance,
                    maxFishPopulation = biome.maxFishPopulation,
                    allowRoads = biome.allowRoads,
                    allowRivers = biome.allowRivers,
                    allowFarmingCamps = biome.allowFarmingCamps,
                    isExtremeBiome = biome.isExtremeBiome
                };
            }
        }

        public static float AnimalDensity(BiomeDef biome) => Value(biome, biome.animalDensity, s => s.animalDensity);
        public static float PlantDensity(BiomeDef biome) => Value(biome, biome.plantDensity, s => s.plantDensity);
        public static float DiseaseMtbDays(BiomeDef biome) => Value(biome, biome.diseaseMtbDays, s => s.diseaseMtbDays);
        public static float SettlementSelectionWeight(BiomeDef biome) => Value(biome, biome.settlementSelectionWeight, s => s.settlementSelectionWeight);
        public static float CampSelectionWeight(BiomeDef biome) => Value(biome, biome.campSelectionWeight, s => s.campSelectionWeight);
        public static float PollutionOffset(BiomeDef biome) => Value(biome, biome.pollutionOffset, s => s.pollutionOffset);
        public static float Forageability(BiomeDef biome) => Value(biome, biome.forageability, s => s.forageability);
        public static float WildPlantRegrowDays(BiomeDef biome) => Value(biome, biome.wildPlantRegrowDays, s => s.wildPlantRegrowDays);
        public static float MovementDifficulty(BiomeDef biome) => Value(biome, biome.movementDifficulty, s => s.movementDifficulty);
        public static float GeyserCountFactor(BiomeDef biome) => Value(biome, biome.geyserCountFactor, s => s.geyserCountFactor);
        public static float WildAnimalScariaChance(BiomeDef biome) => Value(biome, biome.wildAnimalScariaChance, s => s.wildAnimalScariaChance);
        public static float MaxFishPopulation(BiomeDef biome) => Value(biome, biome.maxFishPopulation, s => s.maxFishPopulation);
        public static bool AllowRoads(BiomeDef biome) => ValueBool(biome, biome.allowRoads, s => s.allowRoads);
        public static bool AllowRivers(BiomeDef biome) => ValueBool(biome, biome.allowRivers, s => s.allowRivers);
        public static bool AllowFarmingCamps(BiomeDef biome) => ValueBool(biome, biome.allowFarmingCamps, s => s.allowFarmingCamps);
        public static bool IsExtremeBiome(BiomeDef biome) => ValueBool(biome, biome.isExtremeBiome, s => s.isExtremeBiome);

        public static void Restore(BiomeDef biome)
        {
            if (biome == null || !originals.TryGetValue(biome, out Snapshot snapshot)) return;
            ApplySnapshot(biome, snapshot);
        }

        public static void RestoreAll()
        {
            foreach (var kvp in originals)
                ApplySnapshot(kvp.Key, kvp.Value);
        }

        private static void ApplySnapshot(BiomeDef biome, Snapshot s)
        {
            biome.animalDensity = s.animalDensity;
            biome.plantDensity = s.plantDensity;
            biome.diseaseMtbDays = s.diseaseMtbDays;
            biome.settlementSelectionWeight = s.settlementSelectionWeight;
            biome.campSelectionWeight = s.campSelectionWeight;
            biome.pollutionOffset = s.pollutionOffset;
            biome.forageability = s.forageability;
            biome.wildPlantRegrowDays = s.wildPlantRegrowDays;
            biome.movementDifficulty = s.movementDifficulty;
            biome.geyserCountFactor = s.geyserCountFactor;
            biome.wildAnimalScariaChance = s.wildAnimalScariaChance;
            biome.maxFishPopulation = s.maxFishPopulation;
            biome.allowRoads = s.allowRoads;
            biome.allowRivers = s.allowRivers;
            biome.allowFarmingCamps = s.allowFarmingCamps;
            biome.isExtremeBiome = s.isExtremeBiome;
        }

        private static float Value(BiomeDef biome, float fallback, Func<Snapshot, float> pick)
        {
            if (biome != null && originals.TryGetValue(biome, out Snapshot snapshot))
                return pick(snapshot);
            return fallback;
        }

        private static bool ValueBool(BiomeDef biome, bool fallback, Func<Snapshot, bool> pick)
        {
            if (biome != null && originals.TryGetValue(biome, out Snapshot snapshot))
                return pick(snapshot);
            return fallback;
        }
    }
}
