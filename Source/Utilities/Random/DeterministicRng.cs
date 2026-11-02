using UnityEngine;
using Verse;

namespace Planets.Utilities.Random
{
    /// <summary>
    /// Unified deterministic random number generator for Realistic Planets.
    /// Provides consistent pseudo-random values without affecting the global Rand state.
    /// </summary>
    public static class DeterministicRng
    {
        /// <summary>
        /// Get a deterministic pseudo-random value between 0 and 1 based on tile ID and world seed.
        /// This is the preferred method as it ensures consistency across the same world.
        /// </summary>
        /// <param name="tileId">The tile ID to generate randomness for</param>
        /// <param name="salt">Additional salt value for different random streams</param>
        /// <returns>A float between 0 and 1</returns>
        public static float Unit(int tileId, int salt)
        {
            int worldSeed = Find.World?.info?.Seed ?? 0;
            
            int hash = Gen.HashCombineInt(tileId, worldSeed);
            hash = Gen.HashCombineInt(hash, salt);
            
            uint uhash = (uint)hash;
            return UnitFromHash(uhash);
        }
        
        /// <summary>
        /// Get a deterministic pseudo-random float in a range.
        /// </summary>
        /// <param name="tileId">The tile ID to generate randomness for</param>
        /// <param name="min">Minimum value</param>
        /// <param name="max">Maximum value</param>
        /// <param name="salt">Additional salt value for different random streams</param>
        /// <returns>A float between min and max</returns>
        public static float Range(int tileId, float min, float max, int salt)
        {
            float unit = Unit(tileId, salt);
            return min + unit * (max - min);
        }
        
        /// <summary>
        /// Deterministic "percent pass" for placement: returns true with probability passProbability (seed-consistent).
        /// Use to emulate Rand.Value &lt; 0.008 (0.8% pass) etc. without affecting global Rand.
        /// </summary>
        /// <param name="tileId">Tile ID</param>
        /// <param name="passProbability">Pass chance in 0–1 (e.g. 0.008f = 0.8%)</param>
        /// <param name="salt">Salt for this placement type (use Salts or unique int per biome)</param>
        public static bool Passes(int tileId, float passProbability, int salt)
        {
            if (passProbability <= 0f) return false;
            if (passProbability >= 1f) return true;
            return Unit(tileId, salt) < passProbability;
        }

        /// <summary>
        /// Get a deterministic pseudo-random value in [0, 1] from a seed only (no tile ID).
        /// Use for one-off per-stream values (e.g. per-pole size offset) that must not correlate with tile-based streams.
        /// </summary>
        /// <param name="seed">Seed value (e.g. world seed or reverse-seed)</param>
        /// <param name="salt">Salt for different streams</param>
        /// <returns>A float between 0 and 1</returns>
        public static float UnitFromSeed(int seed, int salt)
        {
            int hash = Gen.HashCombineInt(seed, salt);
            uint uhash = (uint)hash;
            return UnitFromHash(uhash);
        }

        /// <summary>
        /// Deterministic "percent pass" with position-based decorrelation.
        /// Simple approach: hash tileID normally, but XOR in position coordinates to break spatial correlation
        /// from sequential tile IDs. This prevents 40-60 tile patches while keeping the code simple.
        /// </summary>
        /// <param name="tileID">Tile ID</param>
        /// <param name="worldPos">3D world position (e.g. Find.WorldGrid.GetTileCenter(tileID))</param>
        /// <param name="passProbability">Pass chance in 0–1</param>
        /// <param name="salt">Salt for this placement type</param>
        public static bool PassesWithPosition(int tileID, Vector3 worldPos, float passProbability, int salt)
        {
            if (passProbability <= 0f) return false;
            if (passProbability >= 1f) return true;
            
            int worldSeed = Find.World?.info?.Seed ?? 0;
            int hash = Gen.HashCombineInt(tileID, worldSeed);
            hash = Gen.HashCombineInt(hash, salt);
            // Simple position offset: multiply by large prime, XOR to break correlation
            hash ^= unchecked((int)(worldPos.x * 73856093f));
            hash ^= unchecked((int)(worldPos.y * 19349663f));
            hash ^= unchecked((int)(worldPos.z * 83492791f));
            
            uint uhash = (uint)hash;
            return UnitFromHash(uhash) < passProbability;
        }

        /// <summary>Map a hash onto [0, 1) using the top 24 bits.</summary>
        private static float UnitFromHash(uint uhash) => (uhash >> 8) * (1f / 16777216f);

        /// <summary>
        /// Predefined salt values for various random streams to avoid correlation.
        /// Use these as the salt parameter to get independent random sequences.
        /// </summary>
        public static class Salts
        {
            public const int BiomePlacement = 123456789;
            public const int OasisPlacement = 135791357;
            public const int GenericBiomeNoise = 246802468;
            public const int RiverGeneration = 987654321;
            public const int FeaturePlacement = 555666777;
            public const int ClimateVariation = 111222333;
            public const int TerrainDetail = 444555666;
            public const int VegetationDensity = 777888999;
            public const int AnimalSpawning = 222333444;
            public const int ResourceDistribution = 666777888;
            public const int PolarLandmassSize = 333444555;
            public const int SeaIceExtent = 888999000;
            public const int SwampinessPatchiness = 111333555;
        }
    }
}
