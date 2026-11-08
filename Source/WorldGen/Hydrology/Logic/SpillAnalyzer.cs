using System.Collections.Generic;
using RimWorld.Planet;
using Planets.WorldGen.Hydrology.Models;
using UnityEngine;

namespace Planets.WorldGen.Hydrology.Logic
{
    /// <summary>
    /// Analyzes sinks to determine if/how they spill.
    /// Implements Case A/B logic from "Fill and Spill.txt".
    /// Stateless component - all state passed as parameters.
    /// </summary>
    public class SpillAnalyzer
    {
        /// <summary>
        /// Analyze a single sink to determine if it can spill and how.
        /// Returns SpillChunk if sink spills, null otherwise.
        /// </summary>
        public SpillChunk AnalyzeSink(
            WorldGrid grid,
            float[] elevation,
            bool[] oceanMask,
            float[] flowAccumulation,
            int[] initialBasinIDs,
            int sinkTileID,
            IEvaporationInput evaporationInput,
            int nextChunkID)
        {
            float sinkElevation = elevation[sinkTileID];
            float sinkFlow = flowAccumulation[sinkTileID];
            int sinkBasinID = initialBasinIDs[sinkTileID];

            // Rate-based physics: can the sink even maintain a 1-tile pool against open-water evaporation?
            float sinkEvap = evaporationInput.GetEvaporationRate(sinkTileID);
            if (sinkFlow <= sinkEvap)
                return null;
            
            // Find Spill1: lowest adjacent tile higher than sink
            List<PlanetTile> sinkNeighbors = new List<PlanetTile>();
            grid.GetTileNeighbors(sinkTileID, sinkNeighbors);
            
            int spill1TileID = -1;
            float spill1Elevation = float.MaxValue;
            int spill1NeighborIndex = -1;
            
            for (int i = 0; i < sinkNeighbors.Count; i++)
            {
                int nID = sinkNeighbors[i].tileId;
                if (oceanMask[nID])
                    continue;
                
                float nElevation = elevation[nID];
                if (nElevation > sinkElevation && nElevation < spill1Elevation)
                {
                    spill1Elevation = nElevation;
                    spill1TileID = nID;
                    spill1NeighborIndex = i;
                }
            }
            
            // Create spill chunk starting with sink (sinkFlow > sinkEvap is already guaranteed)
            SpillChunk chunk = new SpillChunk(nextChunkID, sinkTileID);
            if (sinkBasinID >= 0)
                chunk.OccupiedBasins.Add(sinkBasinID);
            chunk.Inflow = sinkFlow;
            chunk.WaterSurfaceElevation = sinkElevation;

            // No spill point found — terminal 1-tile pool
            if (spill1TileID < 0)
                return chunk;
            
            int spill1BasinID = initialBasinIDs[spill1TileID];
            
            // CASE A: Spill1 in different basin
            if (spill1BasinID != sinkBasinID)
            {
                chunk.ExitTile = sinkTileID;
                chunk.SpillDirection = spill1NeighborIndex;
                chunk.SpillTargetTile = spill1TileID;
                // Water rises to spill target elevation (the lip)
                chunk.WaterSurfaceElevation = spill1Elevation;
                return chunk;
            }

            // Can inflow also cover Spill1's evaporation after filling the sink?
            float twoTileEvap = sinkEvap + evaporationInput.GetEvaporationRate(spill1TileID);
            if (sinkFlow <= twoTileEvap)
            {
                // Reaches evaporative equilibrium as a 1-tile terminal pool on sinkTileID
                return chunk;
            }
            
            // CASE B: Spill1 in same basin (and sinkFlow > twoTileEvap)
            // Sub-case 1: Spill1 has lower neighbor (other than sink)
            List<PlanetTile> spill1Neighbors = new List<PlanetTile>();
            grid.GetTileNeighbors(spill1TileID, spill1Neighbors);
            
            int spill2TileID = -1;
            float spill2Elevation = float.MaxValue;
            int spill2NeighborIndex = -1;
            
            for (int i = 0; i < spill1Neighbors.Count; i++)
            {
                int nID = spill1Neighbors[i].tileId;
                if (nID == sinkTileID || oceanMask[nID])
                    continue;
                
                float nElevation = elevation[nID];
                if (nElevation < spill1Elevation && nElevation < spill2Elevation)
                {
                    spill2Elevation = nElevation;
                    spill2TileID = nID;
                    spill2NeighborIndex = i;
                }
            }
            
            if (spill2TileID >= 0) // Sub-case 1: Spill1 has lower neighbor
            {
                int spill2BasinID = initialBasinIDs[spill2TileID];
                
                // Sub-case 1a: Spill2 in different basin - switch to Case A logic
                if (spill2BasinID != sinkBasinID)
                {
                    // Mark SpillDirection on Spill1 (exit point from filled area)
                    chunk.AddTile(spill1TileID);
                    chunk.ExitTile = spill1TileID;
                    chunk.SpillDirection = spill2NeighborIndex;
                    chunk.SpillTargetTile = spill2TileID;
                    // Water surface at highest filled tile (spill1), spilling downward to spill2
                    chunk.WaterSurfaceElevation = spill1Elevation;
                    return chunk;
                }
                
                // Sub-case 1b: Spill2 in same basin
                // Fall through to Sub-case 2
            }
            
            // Sub-case 2: Expand filled area until spilling into another basin or reaching evaporative equilibrium (terminal lake)
            ExpandFilledArea(grid, elevation, oceanMask, initialBasinIDs,
                sinkTileID, spill1TileID, sinkFlow, evaporationInput, chunk);
            
            return chunk;
        }
        
        /// <summary>
        /// Expand filled area for Case B sub-case 2.
        /// Fills depression using a priority-frontier approach until reaching different basin or rate balance fails.
        /// Uses rate-based physics: lakes fill until Inflow > Evaporation (or until spill point).
        /// Returns true if expansion succeeded and spill was executed.
        /// </summary>
        private bool ExpandFilledArea(
            WorldGrid grid,
            float[] elevation,
            bool[] oceanMask,
            int[] initialBasinIDs,
            int sinkTileID,
            int spill1TileID,
            float sinkFlow,
            IEvaporationInput evaporationInput,
            SpillChunk chunk)
        {
            int sinkBasinID = initialBasinIDs[sinkTileID];
            float sinkElevation = elevation[sinkTileID];
            
            // Track filled tiles (Closed Set) and running evaporation sum
            HashSet<int> filledTiles = new HashSet<int> { sinkTileID, spill1TileID };
            chunk.AddTile(spill1TileID);
            float totalEvaporation = evaporationInput.GetEvaporationRate(sinkTileID) + evaporationInput.GetEvaporationRate(spill1TileID);
            
            // Track highest filled tile elevation for comparison
            float highestFilledElevation = Mathf.Max(sinkElevation, elevation[spill1TileID]);
            
            // Track candidate neighbors (Open Set)
            // Key: TileID, Value: (Elevation, SourceTileID, SourceNeighborIndex)
            Dictionary<int, (float elev, int source, int dir)> candidates = new Dictionary<int, (float, int, int)>();
            List<PlanetTile> neighbors = new List<PlanetTile>(8);
            
            // Helper to add neighbors to candidates
            void AddNeighbors(int tileID)
            {
                neighbors.Clear();
                grid.GetTileNeighbors(tileID, neighbors);
                
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int nID = neighbors[i].tileId;
                    
                    // Skip if already filled or ocean
                    if (filledTiles.Contains(nID) || oceanMask[nID])
                        continue;
                        
                    float nElev = elevation[nID];
                    
                    if (!candidates.ContainsKey(nID))
                    {
                        candidates[nID] = (nElev, tileID, i);
                    }
                    else if (nElev < candidates[nID].elev)
                    {
                        candidates[nID] = (nElev, tileID, i);
                    }
                }
            }
            
            // Initialize frontier with neighbors of Sink AND Spill1
            AddNeighbors(sinkTileID);
            AddNeighbors(spill1TileID);
            
            // Iterative expansion
            while (candidates.Count > 0)
            {
                // Find lowest candidate (absolute lowest frontier tile)
                int nextTileID = -1;
                float nextElevation = float.MaxValue;
                int sourceTileID = -1;
                int sourceNeighborIndex = -1;
                
                foreach (var kvp in candidates)
                {
                    if (kvp.Value.elev < nextElevation || (kvp.Value.elev == nextElevation && kvp.Key < nextTileID))
                    {
                        nextTileID = kvp.Key;
                        nextElevation = kvp.Value.elev;
                        sourceTileID = kvp.Value.source;
                        sourceNeighborIndex = kvp.Value.dir;
                    }
                }
                
                if (nextTileID < 0) break;
                
                // Remove from frontier
                candidates.Remove(nextTileID);
                
                // Q1: Is this tile LOWER than the highest filled tile?
                // If yes, we spill immediately (must be different basin)
                if (nextElevation < highestFilledElevation)
                {
                    chunk.ExitTile = sourceTileID;
                    chunk.SpillDirection = sourceNeighborIndex;
                    chunk.SpillTargetTile = nextTileID;
                    chunk.WaterSurfaceElevation = highestFilledElevation;
                    return true;
                }
                
                // Q2: RATE-BASED PHYSICS - Can we reach this tile (higher than current water level)?
                // If inflow <= evaporation, we've reached equilibrium (terminal lake)
                if (sinkFlow <= totalEvaporation)
                {
                    // Terminal lake - set water surface to highest filled tile
                    chunk.WaterSurfaceElevation = highestFilledElevation;
                    return false;
                }
                
                // Q3: Check basin - same or different?
                int nextBasinID = initialBasinIDs[nextTileID];
                if (nextBasinID != sinkBasinID)
                {
                    // Different basin - spill to it (don't fill)
                    chunk.ExitTile = sourceTileID;
                    chunk.SpillDirection = sourceNeighborIndex;
                    chunk.SpillTargetTile = nextTileID;
                    // Water surface rises to spill target elevation
                    chunk.WaterSurfaceElevation = nextElevation;
                    return true;
                }
                
                // Same basin - verify inflow can cover this tile's open-water evaporation before flooding it
                float nextEvap = evaporationInput.GetEvaporationRate(nextTileID);
                if (sinkFlow <= totalEvaporation + nextEvap)
                {
                    chunk.WaterSurfaceElevation = highestFilledElevation;
                    return false;
                }

                filledTiles.Add(nextTileID);
                chunk.AddTile(nextTileID);
                totalEvaporation += nextEvap;
                
                // Update highest filled elevation
                if (nextElevation > highestFilledElevation)
                {
                    highestFilledElevation = nextElevation;
                }
                
                // Add valid neighbors of the new tile to frontier
                AddNeighbors(nextTileID);
            }
            
            // Loop exited without finding spill - terminal lake
            // All same-basin tiles filled, no different-basin exits found
            chunk.WaterSurfaceElevation = highestFilledElevation;
            return false;
        }
    }
}