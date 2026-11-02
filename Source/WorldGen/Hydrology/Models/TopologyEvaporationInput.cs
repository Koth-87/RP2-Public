namespace Planets.WorldGen.Hydrology.Models
{
    /// <summary>
    /// Provides zero evaporation for topological terrain analysis.
    /// In Topology Mode, we want to find "The Great Divides" by forcing
    /// all lakes to fill until they spill (no terminal lakes).
    /// </summary>
    public class TopologyEvaporationInput : IEvaporationInput
    {
        public float GetEvaporationRate(int tileID)
        {
            return 0.0f; // Zero evaporation - all lakes must fill until spill
        }
    }
}

