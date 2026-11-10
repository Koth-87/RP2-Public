using System;
using System.Diagnostics;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate.Circulation;
using Planets.WorldGen.Climate.Layers;
using Planets.WorldGen.Climate.Moisture;
using Planets.Core.Configuration;
using Planets.Core;
using Planets.WorldGen.Constants;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Orchestrates sequential execution of all 11 climate layers.
    /// 
    /// Responsibilities:
    /// - Execute layers in dependency order (documented in InitializeLayers)
    /// - Handle errors gracefully with fail-fast approach
    /// - Provide debug logging of progress
    /// - Populate provided ClimateData with computed results
    /// 
    /// Usage:
    /// ```csharp
    /// var climateData = new ClimateData();
    /// climateData.InitializeArrays(tileCount);
    /// var orchestrator = new ClimateOrchestrator(climateData, config);
    /// orchestrator.Compute();
    /// ```
    /// </summary>
    public class ClimateOrchestrator
    {
        private ClimateData climateData;
        private ClimateConfiguration config;
        private FeatureConfiguration features;
        private HydrologyConfiguration hydrologyConfig;
        private RimWorld.Planet.PlanetLayer planetLayer;
        private GeographyLayer geography;
        private TemperatureLayer temperature;
        private SeasonalityLayer seasonality;
        private CirculationLayer circulation;
        private MaritimeLayer maritime;

        /// <summary>Temperature, wind, and ocean air feed each other. Two rounds, each logged; see RunCoupledRounds.</summary>
        private const int CouplingRounds = 2;
        private MoistureBudgetLayer moistureBudget;
        private HydrologyLayer hydrology;
        private bool computationComplete = false;

        /// <summary>
        /// Initialize orchestrator with existing ClimateData and configuration.
        /// ClimateData must already have arrays initialized via InitializeArrays().
        /// </summary>
        /// <param name="existingClimateData">Pre-initialized ClimateData (must not be null)</param>
        /// <param name="layer">PlanetLayer with tiles created in Phase 1 (must not be null)</param>
        /// <param name="configuration">Climate configuration (defaults to new instance if null)</param>
        /// <param name="featureConfiguration">Feature toggles (null = Tier 2 hydrology disabled)</param>
        /// <exception cref="ArgumentNullException">If climateData or layer is null</exception>
        /// <exception cref="InvalidOperationException">If ClimateData arrays not initialized</exception>
        public ClimateOrchestrator(
            ClimateData existingClimateData,
            RimWorld.Planet.PlanetLayer layer,
            ClimateConfiguration configuration = null,
            FeatureConfiguration featureConfiguration = null,
            HydrologyConfiguration hydrologyConfiguration = null)
        {
            climateData = existingClimateData ?? throw new ArgumentNullException(nameof(existingClimateData),
                "ClimateData cannot be null. Create and initialize ClimateData before creating orchestrator.");
            
            planetLayer = layer ?? throw new ArgumentNullException(nameof(layer),
                "PlanetLayer cannot be null. Tiles must be created in Phase 1 before climate computation.");
            
            // Validate ClimateData is initialized
            if (climateData.Latitude == null || climateData.Latitude.Length == 0)
            {
                throw new InvalidOperationException(
                    "ClimateData arrays not initialized. Call ClimateData.InitializeArrays(tileCount) before creating orchestrator.");
            }
            
            config = configuration ?? new ClimateConfiguration();
            features = featureConfiguration;
            hydrologyConfig = hydrologyConfiguration ?? Controller.Settings?.hydrologyConfig;
            InitializeLayers();
        }

        /// <summary>
        /// Initialize all climate layers.
        /// Execution order in <see cref="ComputeSteps"/>:
        /// 1. Geography (base geometry, water bodies, ocean distances, TerrainAnalysisOrchestrator, HillinessAnalyzer)
        /// 2. Temperature &amp; Seasonality (initial pass)
        /// 3. Coupled Circulation + Maritime + Temperature + Seasonality rounds
        /// 4. MoistureBudget (two-layer physical moisture budget, seasonal &amp; annual rainfall, PET, daily range, and aridity index)
        /// 5. Hydrology (swampiness + rain-weighted river flow)
        /// </summary>
        private void InitializeLayers()
        {
            geography = new GeographyLayer(climateData, planetLayer, config);
            temperature = new TemperatureLayer(climateData, config);
            seasonality = new SeasonalityLayer(climateData, config);
            circulation = new CirculationLayer(climateData, config);
            maritime = new MaritimeLayer(climateData, config);
            moistureBudget = new MoistureBudgetLayer(climateData, config, circulation);
            hydrology = new HydrologyLayer(climateData, features, hydrologyConfig, config);
        }

        /// <summary>
        /// Execute all layers in sequence.
        /// Uses fail-fast approach: first error stops pipeline and throws.
        /// Debug logging shows progress if debug mode enabled.
        /// </summary>
        /// <exception cref="Exception">Throws if any layer fails, with layer name in message</exception>
        public void Compute()
        {
            foreach (object _ in ComputeSteps())
            {
            }
        }

        /// <summary>
        /// Executes all climate layers in sequence, yielding between stages when driven by a coroutine
        /// (such as <see cref="ClimateDiagnostics"/> load-time overlay rebuilds).
        /// </summary>
        public System.Collections.IEnumerable ComputeSteps()
        {
            if (computationComplete)
                throw new InvalidOperationException("Compute() already completed. Create new orchestrator to recompute.");

            var pipeline = Stopwatch.StartNew();
            ClearMaritime();
            ExecuteLayer(geography);
            yield return null;
            ExecuteLayer(temperature);
            ExecuteLayer(seasonality);
            yield return null;
            RunCoupledRounds();
            yield return null;
            ExecuteLayer(moistureBudget);
            yield return null;
            ExecuteLayer(hydrology);
            pipeline.Stop();
            if (Controller.DebugEnabled)
                RPLog.D($"[ClimateOrchestrator] Climate total {pipeline.ElapsedMilliseconds} ms");

            computationComplete = true;
            climateData.ArraysReady = true;

            if (Controller.DebugEnabled)
                RPLog.D("[ClimateOrchestrator] All climate stages executed successfully");
        }

        /// <summary>
        /// Get computed climate data (only valid after Compute() completes).
        /// </summary>
        /// <returns>ClimateData with all arrays populated by layer execution</returns>
        /// <exception cref="InvalidOperationException">If Compute() has not completed</exception>
        private void ExecuteLayer(IClimateLayer layer)
        {
            try
            {
                if (Controller.DebugEnabled)
                    RPLog.D($"[ClimateOrchestrator] Executing {layer.LayerName}");
                var watch = Stopwatch.StartNew();
                layer.Execute(climateData);
                watch.Stop();
                if (Controller.DebugEnabled)
                    RPLog.D($"[ClimateOrchestrator] {layer.LayerName} took {watch.ElapsedMilliseconds} ms");
            }
            catch (Exception e)
            {
                throw new Exception($"Climate computation failed in {layer.LayerName}: {e.Message}", e);
            }
        }

        /// <summary>
        /// Temperature makes the wind, the wind carries ocean air inland, and that air changes the temperature.
        /// Each round runs circulation on the current temperature, carries ocean air on that wind, then redoes
        /// the final temperature and the seasons. After the last round, temperature, seasons, and continentality
        /// agree with each other; the wind is one round behind them. Each round logs how far they moved.
        /// A later coupling (rain heating, the ocean's answer to the wind) joins a round without changing this order.
        /// </summary>
        private void RunCoupledRounds()
        {
            circulation.ClearRainHeating();
            for (int round = 1; round <= CouplingRounds; round++)
            {
                ExecuteLayer(circulation);
                temperature.RefreshCurrentsFromWind();
                float[] continentalityBefore = (float[])climateData.Continentality.Clone();
                float[] temperatureBefore = (float[])climateData.AnnualMeanTemp.Clone();
                maritime.Round = round;
                ExecuteLayer(maritime);
                if (climateData.MaritimeAnnual == null)
                    return;
                var watch = Stopwatch.StartNew();
                temperature.RefreshFinal();
                watch.Stop();
                if (Controller.DebugEnabled)
                    RPLog.D($"[ClimateOrchestrator] Final temperature, round {round}, took {watch.ElapsedMilliseconds} ms");
                ExecuteLayer(seasonality);
                if (Controller.DebugEnabled)
                    LogRoundChange(round, continentalityBefore, temperatureBefore);
            }

            // Final circulation pass so the wind and ocean currents agree with the finished round-2 temperature and seasonality.
            ExecuteLayer(circulation);
            temperature.RefreshCurrentsFromWind();
        }

        /// <summary>Median and 99th percentile change on land. Round 1 starts from the nearest-coast continentality, so its change is large by design.</summary>
        private void LogRoundChange(int round, float[] continentalityBefore, float[] temperatureBefore)
        {
            var dc = new System.Collections.Generic.List<float>();
            var dt = new System.Collections.Generic.List<float>();
            for (int i = 0; i < climateData.Continentality.Length; i++)
            {
                if (climateData.OceanMask[i])
                    continue;
                dc.Add(Math.Abs(climateData.Continentality[i] - continentalityBefore[i]));
                dt.Add(Math.Abs(climateData.AnnualMeanTemp[i] - temperatureBefore[i]));
            }
            if (dc.Count == 0)
                return;
            dc.Sort();
            dt.Sort();
            int p99 = Math.Min(dc.Count - 1, dc.Count * 99 / 100);
            RPLog.D($"[ClimateOrchestrator] Coupling round {round} change on land: continentality median {dc[dc.Count / 2]:F3}, 99th {dc[p99]:F3}; " +
                    $"annual temperature median {dt[dt.Count / 2]:F2} °C, 99th {dt[p99]:F2} °C.");
        }

        private void ClearMaritime()
        {
            climateData.MaritimeJune = null;
            climateData.MaritimeDecember = null;
            climateData.MaritimeEquinox = null;
            climateData.MaritimeAnnual = null;
            climateData.UpwindSST = null;
            climateData.ContinentalAir = null;
        }

        public ClimateData GetResult()
        {
            if (!computationComplete)
                throw new InvalidOperationException("Compute() must complete before accessing results");
            return climateData;
        }

        /// <summary>
        /// Check if computation has completed successfully.
        /// </summary>
        public bool IsComplete => computationComplete;
    }
}
