using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Biomes.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Centralized biome worker redirect for mod compatibility.
    /// Replaces mod biome workers with RP2 compat workers so modded biomes use our climate model.
    /// Uses worker-type-based matching: any BiomeDef that uses a mapped mod worker gets our replacement.
    /// Biomes without defs (workers only) are never seen here; only DefDatabase biomes are processed.
    /// </summary>
    public static class BiomeCompatWorkerRedirect
    {
        /// <summary>Mod worker type name -> RP2 worker type name. Resolved at runtime.</summary>
        private static readonly (string ModWorker, string RrpWorker)[] RedirectMappings =
        {
            // More Vanilla Biomes (water, host-restricted, and wetland biomes that need RP2-specific scoring)
            ("VanillaBiomes.BiomeWorker_Sandbar", "Planets.Biomes.Compat.MoreVanillaBiomes.Sandbar"),
            ("VanillaBiomes.BiomeWorker_Marsh", "Planets.Biomes.Compat.MoreVanillaBiomes.Marsh"),
            ("VanillaBiomes.BiomeWorker_IceFloes", "Planets.Biomes.Compat.MoreVanillaBiomes.IceFloes"),
            ("VanillaBiomes.BiomeWorker_DesertOasis", "Planets.Biomes.Compat.MoreVanillaBiomes.DesertOasis"),

            // Medieval Overhaul (original DarkForest worker returns a flat constant 40 with no noise gate)
            ("MedievalOverhaul.BiomeWorker_DarkForest", "Planets.Biomes.Compat.MedievalOverhaul.DarkForest"),
            ("DankPyon.BiomeWorker_DarkForest", "Planets.Biomes.Compat.MedievalOverhaul.DarkForest"),
        };

        private static Dictionary<Type, Type> _resolvedMap;
        private static bool _initialized;

        /// <summary>Run redirects for all biomes. Call after DefDatabase is loaded.</summary>
        public static void RunRedirects()
        {
            if (_initialized) return;

            _resolvedMap = new Dictionary<Type, Type>();
            foreach (var (modWorker, rrpWorker) in RedirectMappings)
            {
                var modType = GenTypes.GetTypeInAnyAssembly(modWorker);
                var rrpType = GenTypes.GetTypeInAnyAssembly(rrpWorker);
                if (modType != null && rrpType != null && typeof(BiomeWorker).IsAssignableFrom(rrpType))
                    _resolvedMap[modType] = rrpType;
            }

            int replaced = 0;
            foreach (var biome in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                if (biome?.workerClass == null) continue;
                if (!_resolvedMap.TryGetValue(biome.workerClass, out var rrpType)) continue;

                biome.workerClass = rrpType;
                Traverse.Create(biome).Field("worker").SetValue(null);  // Clear cached worker (BiomeDef uses "worker")
                replaced++;
                if (Controller.DebugEnabled)
                    RPLog.Debug($"[BiomeCompat] Replaced worker for {biome.defName} with {rrpType.Name}");
            }

            _initialized = true;
            if (Controller.DebugEnabled)
                RPLog.Debug($"[BiomeCompat] Redirected {replaced} biome workers");
        }
    }
}
