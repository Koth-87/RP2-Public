namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Contract for climate layer implementations.
    /// Enables orchestrator to call layers polymorphically in sequence.
    /// 
    /// Each layer reads its input dependencies from ClimateData and writes its outputs.
    /// Layers must be stateless (all data stored in ClimateData, not in layer instance).
    /// </summary>
    public interface IClimateLayer
    {
        /// <summary>
        /// Friendly name for this layer (e.g., "Geography", "Temperature").
        /// Used for logging and debugging.
        /// </summary>
        string LayerName { get; }

        /// <summary>
        /// Execute this layer's computations.
        /// 
        /// Layer reads its input dependencies from climateData arrays and writes its outputs.
        /// Must complete all computation within this call.
        /// 
        /// If computation fails, throw an exception. The orchestrator uses fail-fast:
        /// first error stops the entire pipeline.
        /// </summary>
        /// <param name="climateData">Mutable climate data container (input + output)</param>
        void Execute(ClimateData climateData);
    }
}
