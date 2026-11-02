using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;
using Planets.Utilities.Random;

namespace Planets.Biomes.Compat.MoreVanillaBiomes
{
    /// <summary>
    /// More Vanilla Biomes Ice Floes — shallow cold water (<c>elevation &gt; -75m</c>), annual temperature -21.45 to -12°C, 7% deterministic pass.
    /// </summary>
    public class IceFloes : BiomeWorkerBase
    {
        private static readonly FieldInfo SpawnField = AccessTools.Field("VanillaBiomes.BiomeSettings:spawnIceberg");
        public override bool HasCustomScore => true;
        private const float PassChance = 0.07f;
        private const int PassSalt = 17002;
        public override float DefaultNoisePassProbability => PassChance;

        protected override bool AllowWaterTiles => true;
        protected override float IdealAnnualTemp => -16.7f;
        protected override float TempTolerance => 5f;
        protected override float IdealAridityIndex => 0.15f;
        protected override float AridityTolerance => 0.10f;
        protected override float IdealPrecipitation => 2500f;
        protected override float PrecipitationTolerance => 2500f;
        protected override float IdealElevation => 500f;
        protected override float ElevationTolerance => 1000f;

        public override string DescribeHardGates() => "WaterCovered, Elevation > -75m (or Temp < -20.5°C), -21.45°C <= AnnualTemp <= -12°C, 7% deterministic pass";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (SpawnField != null && SpawnField.GetValue(null) is bool spawn && !spawn)
                return false;
            if (!s.WaterCovered)
                return false;
            float t = s.AnnualTemp;
            return t >= -21.45f && t <= -12f;
        }

        public override float GetScore(BiomeDef biome, Tile tile, PlanetTile planetTile)
        {
            int tileID = planetTile.tileId;
            ClimateSample s = ClimateSample.FromTile(tile, tileID);
            if (!s.WaterCovered || !PassesHardGates(tile, in s))
                return -100f;

            var meta = Planets.Core.Controller.Settings?.biomeConfig?.GetMetadata(biome.defName);
            float passProb = meta != null && meta.UseVarNoise ? meta.NoisePassProbability : PassChance;
            if (!DeterministicRng.Passes(tileID, passProb, PassSalt))
                return -100f;

            float t = s.AnnualTemp;
            if (t < -20.5f)
                return 110f;
            if (s.Elevation > -75f)
                return 105f + (-t - 12f);
            return -100f;
        }

        protected override float CalculateSecondaryPercentage(Tile tile, int tileID) => 0f;
    }
}
