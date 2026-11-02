using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;
using Planets.Utilities.Random;

namespace Planets.Biomes.Compat.MoreVanillaBiomes
{
    /// <summary>
    /// More Vanilla Biomes Sandbar — water only, elev ≥ -15, temp ≥ 0°C; deterministic 15% pass (DeterministicRng.Passes), score 100 when pass.
    /// </summary>
    public class Sandbar : BiomeWorkerBase
    {
        private static readonly FieldInfo SpawnField = AccessTools.Field("VanillaBiomes.BiomeSettings:spawnSandbar");
        public override bool HasCustomScore => true;
        private const float PassChance = 0.15f;
        private const int PassSalt = 17003;
        public override float DefaultNoisePassProbability => PassChance;

        protected override bool AllowWaterTiles => true;
        protected override float IdealAnnualTemp => 30f;
        protected override float TempTolerance => 30f;
        protected override float IdealAridityIndex => 1.136f;
        protected override float AridityTolerance => 1.136f;
        protected override float IdealElevation => 500f;
        protected override float ElevationTolerance => 1000f;

        public override string DescribeHardGates() => "WaterCovered, Elevation >= -15m, AnnualTemp >= 0°C, 15% deterministic pass";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (SpawnField != null && SpawnField.GetValue(null) is bool spawn && !spawn)
                return false;
            if (!s.WaterCovered) return false;
            if (s.Elevation < -15f) return false;
            if (s.AnnualTemp < 0f) return false;
            return true;
        }

        public override float GetScore(BiomeDef biome, Tile tile, PlanetTile planetTile)
        {
            int tileID = planetTile.tileId;
            ClimateSample s = ClimateSample.FromTile(tile, tileID);
            if (!s.WaterCovered) return -100f;
            if (!PassesHardGates(tile, in s)) return -100f;
            var meta = Planets.Core.Controller.Settings?.biomeConfig?.GetMetadata(biome.defName);
            float passProb = meta != null && meta.UseVarNoise ? meta.NoisePassProbability : PassChance;
            if (!DeterministicRng.Passes(tileID, passProb, PassSalt))
                return 0f;
            return 100f;
        }

        protected override float CalculateSecondaryPercentage(Tile tile, int tileID) => 0f;
    }
}
