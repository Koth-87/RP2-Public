namespace Planets.WorldGen.Hydrology.Models
{
    /// <summary>
    /// Abstract source of water flow input for the Hydrology Engine.
    /// Allows the engine to simulate both topological drainage (uniform input)
    /// and hydrological water transport (rainfall input).
    /// 
    /// Implementations:
    /// - UniformFlowInput: Returns 1.0 per tile for topology mode
    /// - ClimateFlowInput: Returns climate-based rainfall (placeholder for Advanced Hydrology)
    /// </summary>
    public interface IFlowInput
    {
        /// <summary>
        /// Get the initial flow volume for a specific tile.
        /// </summary>
        /// <param name="tileID">The world grid tile ID</param>
        /// <returns>Flow volume (e.g. 1.0f for topology, or mm/year for hydrology)</returns>
        float GetInputFlow(int tileID);
    }
}