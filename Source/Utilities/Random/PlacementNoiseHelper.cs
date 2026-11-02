using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace Planets.Utilities.Random
{
    /// <summary>
    /// Deterministic placement noise for compat/original-style biome workers (Alpha Biomes, ReGrowth, MVB, RP 1.5 Oasis)
    /// and vanilla Odyssey host-restricted biomes (GlacialPlain, LavaField, Scarlands).
    /// Builds Perlin × RidgedMultifractal pipelines (with optional InverseLerp) using world seed + key so placement
    /// is seed-consistent and tunable. Use with <see cref="DeterministicRng.Passes"/> for percent-pass emulation.
    /// </summary>
    public static class PlacementNoiseHelper
    {
        private static Dictionary<(string key, int worldSeed), ModuleBase> _cache = new Dictionary<(string, int), ModuleBase>();
        private static int _cachedSeed = -1;

        // Vanilla Odyssey single-Perlin cache: (worldSeed, seedPart, freq, lac, pers, oct) -> ModuleBase
        private static Dictionary<(int seed, int part, double f, double l, double p, int o), ModuleBase> _vanillaCache = new Dictionary<(int, int, double, double, double, int), ModuleBase>();
        private static int _vanillaCachedSeed = -1;

        /// <summary>Vanilla Odyssey Perlin seed parts (from decompiled Assembly-CSharp).</summary>
        public const int VanillaGlacialPlainSeedPart = 44319114;
        public const int VanillaLavaFieldSeedPart = 967525656;
        public const int VanillaScarlandsSeedPart = 417235213;

        /// <summary>Preset names matching Alpha Biomes / ReGrowth style noises.</summary>
        public const string PresetRadiation = "Radiation";
        public const string PresetWeirdness = "Weirdness";
        public const string PresetIdyllicity = "Idyllicity";
        public const string PresetVolcanic = "Volcanic";
        /// <summary>ReGrowth BiomePerlin-style (e.g. Aspen Forest).</summary>
        public const string PresetBiomePerlin = "BiomePerlin";

        // Alpha/ReGrowth use: Perlin(0.09*freqMult, 2, 0.4, 6, seed), RidgedMultifractal(freq, 2, 6, seed), each ScaleBias(0.5, 0.5), then Multiply.
        private const double Lacunarity = 2.0;
        private const double Persistence = 0.4;
        private const int Octaves = 6;
        private const double ScaleBiasScale = 0.5;
        private const double ScaleBiasBias = 0.5;
        private const double PerlinFreqMult = 0.09;

        /// <summary>
        /// Build a noise module for the given key and world seed (cached). Uses world seed + key hash so no Rand.
        /// </summary>
        /// <param name="key">Preset name or custom key (e.g. PresetRadiation or "BiomePerlin_AspenForest").</param>
        /// <param name="worldSeed">World seed.</param>
        /// <returns>ModuleBase to sample at (x,y,z).</returns>
        public static ModuleBase GetNoiseModule(string key, int worldSeed)
        {
            if (_cachedSeed != worldSeed)
            {
                _cache.Clear();
                _cachedSeed = worldSeed;
            }

            if (_cache.TryGetValue((key, worldSeed), out ModuleBase cached))
                return cached;

            int salt = key.GetHashCode();
            int seed1 = worldSeed + salt;
            int seed2 = worldSeed + salt * 31;

            double ridgedFreq = GetRidgedFrequency(key);
            double freqMult = 1.0;
            var perlin = new Perlin(PerlinFreqMult * freqMult, Lacunarity, Persistence, Octaves, seed1, QualityMode.High);
            var ridged = new RidgedMultifractal(ridgedFreq * freqMult, Lacunarity, Octaves, seed2, QualityMode.High);
            var perlinSb = new ScaleBias(ScaleBiasScale, ScaleBiasBias, perlin);
            var ridgedSb = new ScaleBias(ScaleBiasScale, ScaleBiasBias, ridged);
            ModuleBase combined = new Multiply(perlinSb, ridgedSb);

            // Radiation and BiomePerlin (ReGrowth): scale by InverseLerp(noise, 750f, 650f) then Multiply (emphasizes lower raw values in their coordinate space).
            if (key == PresetRadiation || key == PresetBiomePerlin || key.StartsWith("BiomePerlin_"))
            {
                var invLerp = new InverseLerp(combined, 750f, 650f);
                combined = new Multiply(combined, invLerp);
            }

            _cache[(key, worldSeed)] = combined;
            return combined;
        }

        private static double GetRidgedFrequency(string key)
        {
            switch (key)
            {
                case PresetRadiation:
                case PresetWeirdness:
                    return 0.025;
                case PresetIdyllicity:
                    return 0.05;
                case PresetVolcanic:
                    return 0.005;
                case PresetBiomePerlin:
                    return 0.025;
                default:
                    if (key != null && key.StartsWith("BiomePerlin_"))
                        return 0.025;
                    return 0.025;
            }
        }

        /// <summary>
        /// Sample placement noise at a tile (3D tile center). Value is typically in [0,1] after ScaleBias/Multiply.
        /// </summary>
        public static float GetNoiseValue(string key, int tileID, int worldSeed)
        {
            ModuleBase module = GetNoiseModule(key, worldSeed);
            Vector3 tileCenter = Find.WorldGrid.GetTileCenter(tileID);
            float v = (float)module.GetValue(tileCenter.x, tileCenter.y, tileCenter.z);
            return Mathf.Clamp01(v);
        }

        /// <summary>Convenience: sample using current world seed (Find.World.info.Seed, or 0 if unavailable).</summary>
        public static float GetNoiseValue(string key, int tileID)
        {
            int seed = Find.World?.info?.Seed ?? 0;
            return GetNoiseValue(key, tileID, seed);
        }

        /// <summary>
        /// Evaluate threshold for compat workers: pass when (noise &gt;= threshold) if passIfAbove, else when (noise &lt; threshold).
        /// Returns bonusIfPass when passing, -penaltyIfFail when failing.
        /// </summary>
        /// <param name="key">Preset or custom key.</param>
        /// <param name="tileID">Tile to evaluate.</param>
        /// <param name="worldSeed">World seed.</param>
        /// <param name="threshold">Noise threshold (e.g. 0.5).</param>
        /// <param name="passIfAbove">True: pass when noise &gt;= threshold (e.g. Idyllicity). False: pass when noise &lt; threshold (e.g. Ocular Forest).</param>
        /// <param name="bonusIfPass">Score modifier when passing.</param>
        /// <param name="penaltyIfFail">Score modifier when failing (returned as negative).</param>
        public static float EvaluateThreshold(string key, int tileID, int worldSeed, float threshold, bool passIfAbove, float bonusIfPass, float penaltyIfFail)
        {
            float noise = GetNoiseValue(key, tileID, worldSeed);
            bool pass = passIfAbove ? (noise >= threshold) : (noise < threshold);
            return pass ? bonusIfPass : -penaltyIfFail;
        }

        /// <summary>Convenience: evaluate using current world seed.</summary>
        public static float EvaluateThreshold(string key, int tileID, float threshold, bool passIfAbove, float bonusIfPass, float penaltyIfFail)
        {
            int seed = Find.World?.info?.Seed ?? 0;
            return EvaluateThreshold(key, tileID, seed, threshold, passIfAbove, bonusIfPass, penaltyIfFail);
        }

        // ========== Vanilla Odyssey single-Perlin (normalized, threshold 0.9) ==========

        /// <summary>
        /// Build vanilla Odyssey-style single Perlin (normalized, invert false). Cached per (worldSeed, seedPart, params).
        /// </summary>
        public static ModuleBase GetVanillaOdysseyModule(int worldSeed, int seedPart, double freq, double lacunarity, double persistence, int octaves)
        {
            if (_vanillaCachedSeed != worldSeed)
            {
                _vanillaCache.Clear();
                _vanillaCachedSeed = worldSeed;
            }
            var key = (worldSeed, seedPart, freq, lacunarity, persistence, octaves);
            if (_vanillaCache.TryGetValue(key, out ModuleBase cached))
                return cached;
            int seed = Gen.HashCombineInt(worldSeed, seedPart);
            var perlin = new Perlin(freq, lacunarity, persistence, octaves, true, false, seed, QualityMode.Medium);
            _vanillaCache[key] = perlin;
            return perlin;
        }

        /// <summary>
        /// Sample vanilla Odyssey Perlin at tile center. Normalized output is in [0,1]; clamped for safety.
        /// </summary>
        public static float GetVanillaOdysseyNoiseValue(int tileID, int worldSeed, int seedPart, double freq, double lacunarity, double persistence, int octaves)
        {
            ModuleBase module = GetVanillaOdysseyModule(worldSeed, seedPart, freq, lacunarity, persistence, octaves);
            Vector3 tileCenter = Find.WorldGrid.GetTileCenter(tileID);
            float v = (float)module.GetValue(tileCenter.x, tileCenter.y, tileCenter.z);
            return Mathf.Clamp01(v);
        }

        /// <summary>True when vanilla Odyssey noise at tile &gt;= threshold (e.g. 0.9).</summary>
        public static bool PassesVanillaOdysseyThreshold(int tileID, int worldSeed, int seedPart, double freq, double lacunarity, double persistence, int octaves, float threshold)
        {
            return GetVanillaOdysseyNoiseValue(tileID, worldSeed, seedPart, freq, lacunarity, persistence, octaves) >= threshold;
        }

        /// <summary>
        /// Scarlands: pass only when tile and at least one neighbor have vanilla Scarlands noise &gt;= 0.9 (enforces multi-tile clumps).
        /// Uses vanilla params: seed 417235213, (0.2, 0.5, 0.1, 4).
        /// </summary>
        public static bool PassesVanillaScarlandsWithNeighbor(int tileID, int worldSeed)
        {
            const float threshold = 0.9f;
            if (GetVanillaOdysseyNoiseValue(tileID, worldSeed, VanillaScarlandsSeedPart, 0.2, 0.5, 0.1, 4) < threshold)
                return false;
            List<PlanetTile> neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(tileID, neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (GetVanillaOdysseyNoiseValue(neighbors[i].tileId, worldSeed, VanillaScarlandsSeedPart, 0.2, 0.5, 0.1, 4) >= threshold)
                    return true;
            }
            return false;
        }
    }
}
