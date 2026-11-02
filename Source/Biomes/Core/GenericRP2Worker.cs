using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Core
{
    /// <summary>
    /// RP2 biome worker that scores external mod biomes using metadata ideals, tolerances, and secondary overrides
    /// configured in <see cref="BiomeMetadata"/> via <see cref="BiomeWorkerBase.EvaluateFit"/> when <see cref="BiomeMetadata.UseRP2ClimateFit"/> is enabled.
    /// </summary>
    public class GenericRP2Worker : BiomeWorkerBase
    {
        protected override float IdealAnnualTemp => 15f;
        protected override float TempTolerance => 10f;
        protected override float IdealAridityIndex => 1f;
        protected override float AridityTolerance => 0.4f;

        public override string DescribeHardGates()
        {
            BiomeMetadata meta = Controller.Settings?.biomeConfig?.GetMetadata(CurrentBiomeDefName);
            if (meta != null && meta.IgnoreForeignPlacementRules)
                return "None (mod's own placement rules ignored)";
            return "Mod's own BiomeWorker.GetScore > 0";
        }

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (string.IsNullOrEmpty(CurrentBiomeDefName))
                return true;

            BiomeMetadata meta = Controller.Settings?.biomeConfig?.GetMetadata(CurrentBiomeDefName);
            if (meta != null && meta.IgnoreForeignPlacementRules)
                return true;

            BiomeDef def = DefDatabase<BiomeDef>.GetNamedSilentFail(CurrentBiomeDefName);
            BiomeWorker original = BiomeWorkerManager.GetOriginalWorker(def);
            if (original == null)
                return true;

            PlanetLayer layer = Find.WorldGrid?.Surface;
            return original.GetScore(def, tile, new PlanetTile(s.TileID, layer)) > 0f;
        }
    }
}
