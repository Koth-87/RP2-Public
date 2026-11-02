using System.Collections.Generic;
using System.Diagnostics;
using RimWorld.Planet;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Orchestrates the sequential execution of all terrain analysis phases during <see cref="Climate.Layers.GeographyLayer"/>.
    /// </summary>
    public class TerrainAnalysisOrchestrator
    {
        private readonly List<TerrainAnalysisBase> analyzers;

        public TerrainAnalysisOrchestrator()
        {
            analyzers = new List<TerrainAnalysisBase>
            {
                new ElevationStratifier(),
                new ElevationSmoother(),
                new ProminenceAnalyzer(),
                new FillAndSpillSimulator(),
                new BasinAggregator(),
                new WatershedRidgeDetector(),
                new RidgeOrientationCalculator(),
                new TerrainValidator()
            };
        }

        public void Execute(ClimateData climate, WorldGrid grid)
        {
            RPLog.D("[Terrain Analysis] Starting hybrid elevation stratification + spatial smoothing");

            long fillMs = 0;
            long otherMs = 0;
            foreach (TerrainAnalysisBase analyzer in analyzers)
            {
                analyzer.Initialize(climate, grid);
                Stopwatch watch = Stopwatch.StartNew();
                analyzer.Execute();
                watch.Stop();
                if (analyzer is FillAndSpillSimulator)
                    fillMs += watch.ElapsedMilliseconds;
                else
                    otherMs += watch.ElapsedMilliseconds;
            }
            if (Controller.DebugEnabled)
                RPLog.D($"[Terrain Analysis] Fill-and-spill {fillMs} ms, other terrain analysis {otherMs} ms");

            RPLog.D("[Terrain Analysis] Complete");
        }
    }
}
