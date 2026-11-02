using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.Biomes.Odyssey;
using Planets.Biomes.Vanilla;
using Planets.Core;
using Planets.Patches.WorldGen;
using Planets.Utilities.Logging;

namespace Planets.Patches.Biomes
{
    /// <summary>
    /// Substitutes RP2 <see cref="BiomeWorker"/> implementations for the 12 vanilla core biomes
    /// and 5 Odyssey DLC biomes via a postfix on <see cref="BiomeDef.Worker"/>.
    /// </summary>
    [HarmonyPatch(typeof(BiomeDef), "Worker", MethodType.Getter)]
    public static class Patch_BiomeDef_Worker
    {
        private static readonly Dictionary<string, BiomeWorker> workerCache = new Dictionary<string, BiomeWorker>();
        private static bool initialized;

        public static bool IsSubstitutedDefName(string defName)
        {
            EnsureInitialized();
            return !string.IsNullOrEmpty(defName) && workerCache.ContainsKey(defName);
        }

        private static void EnsureInitialized()
        {
            if (initialized)
                return;

            workerCache["AridShrubland"] = new AridShrubland();
            workerCache["BorealForest"] = new BorealForest();
            workerCache["ColdBog"] = new ColdBog();
            workerCache["Desert"] = new Desert();
            workerCache["ExtremeDesert"] = new ExtremeDesert();
            workerCache["IceSheet"] = new IceSheet();
            workerCache["SeaIce"] = new SeaIce();
            workerCache["TemperateForest"] = new TemperateForest();
            workerCache["TemperateSwamp"] = new TemperateSwamp();
            workerCache["TropicalRainforest"] = new TropicalRainforest();
            workerCache["TropicalSwamp"] = new TropicalSwamp();
            workerCache["Tundra"] = new Tundra();

            if (ModsConfig.OdysseyActive)
            {
                workerCache["GlacialPlain"] = new GlacialPlain();
                workerCache["Glowforest"] = new Glowforest();
                workerCache["Scarlands"] = new Scarlands();
                workerCache["LavaField"] = new LavaField();
                workerCache["Grasslands"] = new Grasslands();
            }

            initialized = true;

            if (Controller.DebugEnabled)
                RPLog.Debug($"Initialized biome worker substitution: 12 vanilla + {(ModsConfig.OdysseyActive ? "5 Odyssey" : "0 Odyssey")}");
        }

        static void Postfix(BiomeDef __instance, ref BiomeWorker __result)
        {
            EnsureInitialized();
            if (__instance?.defName != null && workerCache.TryGetValue(__instance.defName, out BiomeWorker worker))
                __result = worker;
        }
    }

    /// <summary>
    /// Logs Odyssey DLC biome distribution in debug mode when RP2 surface terrain generation completes.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenStep_Terrain), "GenerateFresh")]
    public static class Patch_WorldGenStep_Terrain_OdysseyDebug
    {
        static void Postfix(PlanetLayer layer)
        {
            if (!ModsConfig.OdysseyActive || !Controller.DebugEnabled)
                return;
            if (!Patch_WorldGenStep_Terrain.rpTerrainSucceeded || layer == null || layer != Find.WorldGrid?.Surface)
                return;

            string[] odysseyBiomes = { "GlacialPlain", "Glowforest", "Scarlands", "LavaField", "Grasslands" };
            var biomeCounts = new Dictionary<string, int>();

            int totalLandTiles = 0;
            for (int i = 0; i < layer.Tiles.Count; i++)
            {
                Tile tile = layer.Tiles[i];
                if (tile != null && !tile.WaterCovered)
                {
                    totalLandTiles++;
                    if (tile.PrimaryBiome != null && odysseyBiomes.Contains(tile.PrimaryBiome.defName))
                    {
                        biomeCounts.TryGetValue(tile.PrimaryBiome.defName, out int c);
                        biomeCounts[tile.PrimaryBiome.defName] = c + 1;
                    }
                }
            }

            RPLog.Debug("Odyssey Biome Distribution:");
            foreach (string biome in odysseyBiomes)
            {
                int count = biomeCounts.TryGetValue(biome, out int val) ? val : 0;
                float percent = totalLandTiles > 0 ? (float)count / totalLandTiles * 100f : 0f;
                RPLog.Debug($"  {biome}: {count} tiles ({percent:F2}% of land)");
            }

            int totalOdyssey = biomeCounts.Values.Sum();
            float totalPercent = totalLandTiles > 0 ? (float)totalOdyssey / totalLandTiles * 100f : 0f;
            RPLog.Debug($"  Total Odyssey: {totalOdyssey} tiles ({totalPercent:F2}% of land)");
        }
    }

    /// <summary>
    /// Registry of vanilla and Odyssey worker types substituted by <see cref="Patch_BiomeDef_Worker"/>.
    /// </summary>
    public static class BiomeWorkerRedirectPatch
    {
        private static readonly Dictionary<Type, Type> VanillaToRRPMap = new Dictionary<Type, Type>
        {
            { typeof(BiomeWorker_AridShrubland), typeof(AridShrubland) },
            { typeof(BiomeWorker_BorealForest), typeof(BorealForest) },
            { typeof(BiomeWorker_ColdBog), typeof(ColdBog) },
            { typeof(BiomeWorker_Desert), typeof(Desert) },
            { typeof(BiomeWorker_ExtremeDesert), typeof(ExtremeDesert) },
            { typeof(BiomeWorker_IceSheet), typeof(IceSheet) },
            { typeof(BiomeWorker_SeaIce), typeof(SeaIce) },
            { typeof(BiomeWorker_TemperateForest), typeof(TemperateForest) },
            { typeof(BiomeWorker_TemperateSwamp), typeof(TemperateSwamp) },
            { typeof(BiomeWorker_TropicalRainforest), typeof(TropicalRainforest) },
            { typeof(BiomeWorker_TropicalSwamp), typeof(TropicalSwamp) },
            { typeof(BiomeWorker_Tundra), typeof(Tundra) },
        };

        private static readonly Dictionary<string, string> OdysseyBiomes = new Dictionary<string, string>
        {
            { "BiomeWorker_GlacialPlain", "Planets.Biomes.Odyssey.GlacialPlain" },
            { "BiomeWorker_Glowforest", "Planets.Biomes.Odyssey.Glowforest" },
            { "BiomeWorker_Grasslands", "Planets.Biomes.Odyssey.Grasslands" },
            { "BiomeWorker_LavaField", "Planets.Biomes.Odyssey.LavaField" },
            { "BiomeWorker_Scarlands", "Planets.Biomes.Odyssey.Scarlands" },
        };

        public static void Initialize()
        {
            if (ModsConfig.IsActive("Ludeon.RimWorld.Odyssey"))
            {
                foreach (var kvp in OdysseyBiomes)
                    TryAddMapping(kvp.Key, kvp.Value);
                if (Controller.DebugEnabled)
                    RPLog.Debug($"Added {OdysseyBiomes.Count} Odyssey biome mappings for IsMappedWorker");
            }
            if (Controller.DebugEnabled)
                RPLog.Debug($"BiomeWorker registry: {VanillaToRRPMap.Count} mapped types");
        }

        private static void TryAddMapping(string vanillaTypeName, string rrpTypeName)
        {
            try
            {
                Type vanillaType = null;
                Type rrpType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (vanillaType == null)
                        vanillaType = assembly.GetType($"RimWorld.{vanillaTypeName}");
                    if (rrpType == null)
                        rrpType = assembly.GetType(rrpTypeName);
                    if (vanillaType != null && rrpType != null)
                        break;
                }
                if (vanillaType != null && rrpType != null)
                    VanillaToRRPMap[vanillaType] = rrpType;
            }
            catch (Exception e)
            {
                if (Controller.DebugEnabled)
                    RPLog.Warning($"Failed to add mapping {vanillaTypeName} -> {rrpTypeName}: {e.Message}");
            }
        }

        public static bool IsMappedWorker(Type workerType)
        {
            return workerType != null && VanillaToRRPMap.ContainsKey(workerType);
        }

        public static string GetDiagnosticInfo()
        {
            var info = new List<string>
            {
                "BiomeWorker Substitution (via BiomeDef.Worker patch):",
                $"  Mapped worker types: {VanillaToRRPMap.Count}",
                $"  Odyssey DLC: {(ModsConfig.IsActive("Ludeon.RimWorld.Odyssey") ? "Active" : "Inactive")}"
            };
            if (Controller.DebugEnabled)
            {
                info.Add("  Mapped types:");
                foreach (var kvp in VanillaToRRPMap)
                    info.Add($"    {kvp.Key.Name} -> {kvp.Value.Name}");
            }
            return string.Join("\n", info);
        }
    }

    [StaticConstructorOnStartup]
    public static class BiomeWorkerPatchInitializer
    {
        static BiomeWorkerPatchInitializer()
        {
            BiomeWorkerRedirectPatch.Initialize();
            if (!Controller.DebugEnabled)
                return;

            RPLog.Debug("Verifying BiomeWorker substitution patch...");
            try
            {
                RPLog.Debug(BiomeWorkerRedirectPatch.GetDiagnosticInfo());
                var harmony = new Harmony("rimworld.mod.koth.realisticplanets");
                bool workerGetterPatched = harmony.GetPatchedMethods()
                    .Any(m => m.DeclaringType == typeof(BiomeDef) && m.Name == "get_Worker");

                if (workerGetterPatched)
                    RPLog.Debug("BiomeDef.Worker patch verified - vanilla and Odyssey biomes use RP2 workers.");
                else
                    RPLog.Warning("BiomeDef.Worker patch not found. Check Harmony patches.");
            }
            catch (Exception e)
            {
                RPLog.Warning($"Failed to verify BiomeWorker patches: {e}");
            }
        }
    }
}
