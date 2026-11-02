namespace Planets.WorldGen.Hydrology.Models
{
    /// <summary>
    /// Provides uniform flow input (1 unit per tile) for topological terrain analysis.
    /// This simulates "potential" drainage area regardless of rainfall.
    /// </summary>
    public class UniformFlowInput : IFlowInput
    {
        public float GetInputFlow(int tileID)
        {
            return 1.0f; // Each tile contributes 1 unit of drainage area
        }
    }
}