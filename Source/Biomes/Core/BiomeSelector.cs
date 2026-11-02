using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;

namespace Planets.Biomes.Core
{
    /// <summary>
    /// Shared biome competition evaluator used by world generation (<see cref="WorldGen.WorldGenerationOrchestrator"/>),
    /// <see cref="DebugTools.Tools.TileInspector"/>, and <see cref="DebugTools.Exports.RawDataExports"/>.
    /// Ensures diagnostics and CSV exports rank candidate biomes with the exact same fit energy,
    /// frequency shifts, and foreign-peak normalization as world generation.
    /// </summary>
    public static class BiomeSelector
    {
        public struct CandidateScore
        {
            public BiomeDef Biome;
            public float Energy;
            public float Score;
            public bool IsForeign;
        }

        /// <summary>
        /// Evaluates all viable competing biomes at <paramref name="tile"/> using the same rules as <c>SelectBestBiome</c>.
        /// Returns candidates sorted by ascending energy (descending effective score <c>100 * exp(-Energy / 2)</c>).
        /// </summary>
        public static List<CandidateScore> EvaluateCandidates(
            Tile tile,
            PlanetTile planetTile,
            PlanetLayer layer = null,
            Dictionary<string, float> peakOverrides = null,
            HashSet<string> loggedWorkerErrors = null)
        {
            var results = new List<CandidateScore>();
            if (tile == null)
                return results;

            layer ??= Find.WorldGrid?.Surface;
            BiomeConfiguration config = Controller.Settings?.biomeConfig;

            foreach (BiomeDef biomeDef in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                if (!CanCompete(biomeDef, config))
                    continue;
                if (biomeDef == BiomeDefOf.Ocean || biomeDef == BiomeDefOf.Lake)
                    continue;

                BiomeWorker worker = biomeDef.Worker;
                if (worker == null || (layer != null && !worker.CanPlaceOnLayer(biomeDef, layer)))
                    continue;

                float frequency = config != null ? config.GetCommonalityMultiplier(biomeDef.defName) : 1f;
                if (frequency <= 0f)
                    continue;
                float frequencyShift = -2f * Mathf.Log(frequency);

                try
                {
                    float energy;
                    bool foreign;
                    if (worker is BiomeWorkerBase rpWorker && !rpWorker.HasCustomScore)
                    {
                        BiomeFit fit = rpWorker.EvaluateFit(biomeDef, tile, planetTile);
                        if (!fit.Viable)
                            continue;
                        energy = fit.Energy + frequencyShift;
                        foreign = false;
                    }
                    else
                    {
                        float rawScore = worker.GetScore(biomeDef, tile, planetTile);
                        if (rawScore <= 0f)
                            continue;

                        float peak = 0f;
                        if (peakOverrides != null)
                            peakOverrides.TryGetValue(biomeDef.defName, out peak);
                        if (peak <= 0f)
                            peak = ForeignPeaks.Peak(biomeDef.defName);
                        if (peak <= 0f && layer != null)
                            peak = ForeignPeaks.Resolve(biomeDef, worker, layer);
                        if (peak <= 0f)
                            continue;

                        float ratio = Mathf.Min(rawScore / peak, 1f);
                        energy = -2f * Mathf.Log(ratio) + frequencyShift;
                        foreign = true;
                    }

                    float effectiveScore = 100f * Mathf.Exp(-energy / 2f);
                    results.Add(new CandidateScore
                    {
                        Biome = biomeDef,
                        Energy = energy,
                        Score = effectiveScore,
                        IsForeign = foreign
                    });
                }
                catch (Exception ex)
                {
                    if (loggedWorkerErrors == null || loggedWorkerErrors.Add(biomeDef.defName))
                        RPLog.W($"BiomeWorker error for {biomeDef.defName}: {ex.Message}");
                }
            }

            results.Sort((a, b) => a.Energy.CompareTo(b.Energy));
            return results;
        }

        public static bool CanCompete(BiomeDef biomeDef, BiomeConfiguration config = null)
        {
            if (biomeDef == null || !biomeDef.implemented || !biomeDef.generatesNaturally || biomeDef.workerClass == null)
                return false;
            config ??= Controller.Settings?.biomeConfig;
            return config == null || config.IsBiomeEnabled(biomeDef.defName);
        }
    }
}
