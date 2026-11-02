using Planets.Utilities.Logging;
using Planets.WorldGen.Constants;
using Planets.WorldGen.Hydrology.Core;
using Planets.WorldGen.Hydrology.Models;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Phase 4–7: Topographic Fill-and-Spill Simulation.
    /// Runs <see cref="HydrologySimulator"/> with uniform flow (1.0 per tile) and zero evaporation
    /// so closed depressions fill until they spill, establishing the continental drainage topology.
    /// </summary>
    public class FillAndSpillSimulator : TerrainAnalysisBase
    {
        public override void Execute()
        {
            RPLog.D("[Terrain Analysis] Simulating fill-and-spill hydrology");

            const int MAX_ITERATIONS = 500;

            int initialBasinCount = 0;
            for (int i = 0; i < tileCount; i++)
            {
                if (!climate.OceanMask[i] && climate.FlowDirectionTopology[i] < 0)
                    initialBasinCount++;
            }

            var engine = new HydrologySimulator();
            var config = new HydrologyConfig
            {
                MaxIterations = MAX_ITERATIONS,
                FlowInput = new UniformFlowInput(),
                EvaporationInput = new TopologyEvaporationInput()
            };

            HydrologyResult result = engine.Simulate(grid, climate.ElevationKm, climate.OceanMask, config);

            for (int i = 0; i < tileCount; i++)
            {
                climate.FlowAccumulationTopology[i] = result.FlowAccumulation[i];
                climate.InitialBasinID[i] = result.InitialBasinIDs[i];
                climate.SpillDirectionTopology[i] = result.SpillDirections[i];
                climate.FlowDirectionTopology[i] = result.FlowDirections[i];
                climate.MergedLakeIDTopology[i] = result.MergedLakeIDs[i];
                climate.MergedBasinIDTopology[i] = result.MergedBasinIDs[i];
            }

            int finalBasinCount = 0;
            for (int i = 0; i < tileCount; i++)
            {
                if (!climate.OceanMask[i] && climate.FlowDirectionTopology[i] < 0)
                    finalBasinCount++;
            }

            RPLog.D($"[Fill-Spill] Complete: {initialBasinCount} → {finalBasinCount} endorheic sinks");
        }
    }
}
