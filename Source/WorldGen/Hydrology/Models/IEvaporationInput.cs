namespace Planets.WorldGen.Hydrology.Models
{
    /// <summary>
    /// Abstract source of evaporation rate for the Hydrology Engine.
    /// Allows the engine to simulate both topological drainage (zero evaporation)
    /// and hydrological equilibrium (climate-based evaporation).
    /// 
    /// Implementations:
    /// - TopologyEvaporationInput: Returns 0.0 for topology mode (forces all lakes to fill until spill)
    /// - ClimateEvaporationInput: Returns climate-based evaporation (placeholder for Advanced Hydrology)
    /// </summary>
    public interface IEvaporationInput
    {
        /// <summary>
        /// Get the evaporation rate for a specific tile.
        /// </summary>
        /// <param name="tileID">The world grid tile ID</param>
        /// <returns>Evaporation rate (e.g. 0.0 for topology mode, or mm/year for hydrology mode)</returns>
        float GetEvaporationRate(int tileID);
    }
}

