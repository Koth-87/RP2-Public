using System.Collections.Generic;
using Planets.WorldGen.Hydrology.Models;
using RimWorld.Planet;

namespace Planets.WorldGen.Hydrology.Core
{
    /// <summary>
    /// Configuration parameters for the Hydrology Simulation.
    /// </summary>
    public struct HydrologyConfig
    {
        public int MaxIterations;   // Maximum fill-and-spill iterations
        public IFlowInput FlowInput; // Flow input source (Uniform or Climate-based)
        public IEvaporationInput EvaporationInput; // Evaporation rate source (TopologyMode or ClimateMode)
    }

    /// <summary>
    /// Result of the Hydrology Simulation.
    /// </summary>
    public class HydrologyResult
    {
        /// <summary>Rainfall in millimetres accumulated over the upstream basin. Same units as vanilla WorldGenStep_Rivers flow.</summary>
        public float[] FlowAccumulation;
        public int[] InitialBasinIDs;        // Initial basin ID per tile (pre-fill/spill)
        public int[] SpillDirections;        // Spill direction (-1 if none)
        public int[] FlowDirections;         // Flow direction (steepest descent)
        public int[] MergedLakeIDs;          // Merged lake ID per tile (-1 if not in lake)
        public int[] MergedBasinIDs;         // Merged basin ID per tile (by terminus)
    }

    /// <summary>
    /// The Core Hydrology Engine.
    /// Simulates water flow, accumulation, lake formation, and spillage.
    /// </summary>
    public interface IHydrologyEngine
    {
        /// <summary>
        /// Execute the hydrology simulation.
        /// </summary>
        /// <param name="grid">The world grid geometry</param>
        /// <param name="elevation">Elevation map (in km)</param>
        /// <param name="oceanMask">True if tile is ocean</param>
        /// <param name="config">Simulation parameters (includes flow and evaporation inputs)</param>
        /// <returns>The simulation results</returns>
        HydrologyResult Simulate(
            WorldGrid grid,
            float[] elevation,
            bool[] oceanMask,
            HydrologyConfig config
        );
    }
}