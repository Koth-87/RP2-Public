using System.Collections.Generic;

namespace Planets.WorldGen.Hydrology.Models
{
    /// <summary>
    /// Represents a spill input from a lake/chunk into a target tile.
    /// Used to feed the FlowAccumulator in the next iteration.
    /// </summary>
    public struct SpillInput
    {
        public int SourceTileID;
        public int TargetTileID;
        public float Amount;
        
        public SpillInput(int source, int target, float amount)
        {
            SourceTileID = source;
            TargetTileID = target;
            Amount = amount;
        }
    }
}