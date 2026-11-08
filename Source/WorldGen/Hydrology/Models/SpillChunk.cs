using System.Collections.Generic;

namespace Planets.WorldGen.Hydrology.Models
{
    /// <summary>
    /// Represents a filled depression (lake/reservoir) in the simulation.
    /// A chunk consists of a sink tile and any connected tiles that are filled with water.
    /// Note: ChunkID is mutable to allow updating from temporary ID to ledger ID.
    /// </summary>
    public class SpillChunk
    {
        public int ChunkID { get; set; }
        public int SinkTileID { get; }
        public List<int> FilledTiles { get; } = new List<int>();
        public HashSet<int> OccupiedBasins { get; } = new HashSet<int>();
        
        // Flow tracking
        public float Inflow { get; set; }
        
        // Water surface elevation (max of highest filled tile or spill target elevation)
        public float WaterSurfaceElevation { get; set; }
        
        // Spill tracking
        public int ExitTile { get; set; } = -1;        // Which tile in the chunk is the exit point
        public int SpillDirection { get; set; } = -1;  // Index of neighbor from exit tile
        public int SpillTargetTile { get; set; } = -1; // Target tile ID receiving the spill
        
        public SpillChunk(int chunkID, int sinkTileID)
        {
            ChunkID = chunkID;
            SinkTileID = sinkTileID;
            FilledTiles.Add(sinkTileID);
        }
        
        public void AddTile(int tileID)
        {
            FilledTiles.Add(tileID);
        }
    }
}