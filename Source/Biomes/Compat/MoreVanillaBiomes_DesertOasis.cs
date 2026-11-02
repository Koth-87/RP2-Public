using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.WorldGen.Climate;
using Planets.Utilities.Random;

namespace Planets.Biomes.Compat.MoreVanillaBiomes
{
    /// <summary>
    /// More Vanilla Biomes Desert Oasis — requires Desert or ExtremeDesert host (score ≥ 50), deterministic 0.3% pass, returns hostScore + 10.
    /// </summary>
    public class DesertOasis : BiomeWorkerBase
    {
        private static readonly FieldInfo SpawnField = AccessTools.Field("VanillaBiomes.BiomeSettings:spawnDesertOasis");
        public override bool HasCustomScore => true;
        private const float PassChance = 0.003f;
        private const int PassSalt = 17001;
        public override float DefaultNoisePassProbability => PassChance;

        protected override float IdealAnnualTemp => 25f;
        protected override float TempTolerance => 5f;
        protected override float IdealAridityIndex => 0.139f;
        protected override float AridityTolerance => 0.139f;
        protected override float IdealElevation => 500f;
        protected override float ElevationTolerance => 1000f;

        public override string DescribeHardGates() => "Host: Desert or ExtremeDesert (score >= 50), AnnualTemp >= -10°C, Aridity <= 0.278, 0.3% deterministic pass";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (SpawnField != null && SpawnField.GetValue(null) is bool spawn && !spawn)
                return false;
            if (s.AnnualTemp < -10f) return false;
            if (s.Aridity > 0.278f) return false;
            return true;
        }

        public override float GetScore(BiomeDef biome, Tile tile, PlanetTile planetTile)
        {
            int tileID = planetTile.tileId;
            if (tile.WaterCovered) return -100f;
            ClimateSample s = ClimateSample.FromTile(tile, tileID);
            if (!PassesHardGates(tile, in s)) return -100f;

            float bestHostScore = GetBestHostScore(tile, planetTile);
            if (bestHostScore < MinHostScore)
                return -100f;

            var meta = Controller.Settings?.biomeConfig?.GetMetadata(biome.defName);
            float passProb = meta != null && meta.UseVarNoise ? meta.NoisePassProbability : PassChance;
            if (!DeterministicRng.Passes(tileID, passProb, PassSalt))
                return 0f;
            return bestHostScore + 10f;
        }

        private float GetBestHostScore(Tile tile, PlanetTile planetTile)
        {
            BiomeConfiguration biomeConfig = Controller.Settings?.biomeConfig;
            float best = -100f;
            foreach (string hostDefName in AllowedHostBiomes)
            {
                if (biomeConfig != null && (!biomeConfig.IsBiomeEnabled(hostDefName) || biomeConfig.GetCommonalityMultiplier(hostDefName) <= 0f))
                    continue;
                BiomeDef hostBiome = DefDatabase<BiomeDef>.GetNamedSilentFail(hostDefName);
                if (hostBiome?.Worker == null)
                    continue;
                float hostScore = hostBiome.Worker.GetScore(hostBiome, tile, planetTile);
                if (hostScore > best)
                    best = hostScore;
            }
            return best;
        }

        protected override float CalculateSecondaryPercentage(Tile tile, int tileID) => 0f;

        protected override bool IsHostRestricted => true;
        protected override string[] AllowedHostBiomes => new[] { "Desert", "ExtremeDesert" };
        protected override float MinHostScore => 50f;
    }
}
