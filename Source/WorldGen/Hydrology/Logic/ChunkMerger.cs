using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using RimWorld.Planet;
using Planets.WorldGen.Hydrology.Models;
using Planets.Utilities.Logging;
using Planets.Core;
using UnityEngine;

namespace Planets.WorldGen.Hydrology.Logic
{
    /// <summary>
    /// Merges touching lakes (chunks) and calculates their consolidated spill.
    /// Implements strict "Adjacent Filled Tiles" merge logic.
    /// </summary>
    public class ChunkMerger
    {
        private System.Diagnostics.Stopwatch _buildAdjacencyTimer = new System.Diagnostics.Stopwatch();
        private System.Diagnostics.Stopwatch _refillTimer = new System.Diagnostics.Stopwatch();
        private System.Diagnostics.Stopwatch _mergeGroupsTimer = new System.Diagnostics.Stopwatch();
        private int _totalRefillCalls = 0;

        /// <summary>
        /// Merge chunks that physically touch, re-expand with combined flow, and calculate net spills.
        /// Implements iterative merge-and-refill until no new merges occur.
        /// </summary>
        public List<SpillInput> MergeAndCalculateSpills(
            WorldGrid grid,
            float[] elevation,
            List<SpillChunk> inputChunks,
            out List<SpillChunk> finalMergedChunks,
            bool[] oceanMask,
            int[] initialBasinIDs,
            IEvaporationInput evaporationInput,
            Dictionary<int, float> baseBasinInflow = null,
            List<SpillInput> activeSpills = null)
        {
            finalMergedChunks = new List<SpillChunk>();
            List<SpillInput> netSpills = new List<SpillInput>();
            
            if (inputChunks.Count == 0)
                return netSpills;
            
            // ITERATIVE MERGE LOOP
            // Repeatedly merge chunks until no new merges occur
            // This handles multi-hop backwater scenarios where merged chunks create new merge opportunities
            List<SpillChunk> currentChunks = inputChunks;
            int iteration = 0;
            const int MAX_MERGE_ITERATIONS = 100;
            bool mergesOccurred;

            _buildAdjacencyTimer.Reset();
            _refillTimer.Reset();
            _mergeGroupsTimer.Reset();
            _totalRefillCalls = 0;
            
            do
            {
                mergesOccurred = false;
                iteration++;
                
                if (iteration > MAX_MERGE_ITERATIONS)
                {
                    RPLog.Warning($"[ChunkMerger] Max merge iterations ({MAX_MERGE_ITERATIONS}) reached - stopping to prevent infinite loop");
                    break;
                }
                
                // 1. Build Adjacency Graph
                // Two chunks are connected only if they are hydrologically unified (backwater scenario)
                // Cascades (High Lake → Low Lake) are NOT merged
                _buildAdjacencyTimer.Start();
                Dictionary<int, List<int>> adjacency = BuildAdjacencyGraph(grid, elevation, currentChunks);
                _buildAdjacencyTimer.Stop();
                
                // 2. Find Connected Components (Merge Groups)
                List<List<SpillChunk>> mergeGroups = FindConnectedComponents(currentChunks, adjacency);
                
                // 3. Process Each Group - create new chunk list for next iteration
                List<SpillChunk> nextChunks = new List<SpillChunk>();
                
                _mergeGroupsTimer.Start();

                bool useParallel = Controller.Settings.perfParallelHydrology.CurrentValue;

                if (useParallel)
                {
                    ConcurrentBag<SpillChunk> nextChunksBag = new ConcurrentBag<SpillChunk>();
                    
                    Parallel.ForEach(mergeGroups, group =>
                    {
                        SpillChunk resultChunk;
                        
                        if (group.Count == 1)
                        {
                            resultChunk = group[0];
                        }
                        else
                        {
                            // Merge occurred
                            resultChunk = MergeGroup(group, initialBasinIDs, baseBasinInflow, activeSpills);
                            
                            // CRITICAL: Re-run fill algorithm with combined flow
                            // The merged chunk may be able to fill additional tiles
                            _refillTimer.Start();
                            resultChunk = RefillMergedChunk(resultChunk, grid, elevation, oceanMask, initialBasinIDs, evaporationInput, baseBasinInflow, activeSpills);
                            _refillTimer.Stop();
                            System.Threading.Interlocked.Increment(ref _totalRefillCalls);
                        }
                        
                        nextChunksBag.Add(resultChunk);
                    });
                    
                    // Ensure deterministic ordering for next iteration
                    nextChunks = nextChunksBag.OrderBy(c => c.ChunkID).ToList();
                    
                    // Check if any merges occurred
                    if (mergeGroups.Any(g => g.Count > 1))
                    {
                        mergesOccurred = true;
                    }
                }
                else
                {
                    foreach (var group in mergeGroups)
                    {
                        SpillChunk resultChunk;
                        
                        if (group.Count == 1)
                        {
                            // No merge - keep as is
                            resultChunk = group[0];
                        }
                        else
                        {
                            // Merge occurred
                            mergesOccurred = true;
                            
                            // Initial merge: union tiles and compute non-double-counted combined flow
                            resultChunk = MergeGroup(group, initialBasinIDs, baseBasinInflow, activeSpills);
                            
                            // CRITICAL: Re-run fill algorithm with combined flow
                            // The merged chunk may be able to fill additional tiles
                            _refillTimer.Start();
                            resultChunk = RefillMergedChunk(resultChunk, grid, elevation, oceanMask, initialBasinIDs, evaporationInput, baseBasinInflow, activeSpills);
                            _refillTimer.Stop();
                            _totalRefillCalls++;
                        }
                        
                        nextChunks.Add(resultChunk);
                    }
                }
                _mergeGroupsTimer.Stop();
                
                currentChunks = nextChunks;
                
            } while (mergesOccurred);
            
            if (iteration > 1)
            {
                RPLog.D($"[ChunkMerger] Merge converged after {iteration} iterations");
            }

            if (Controller.DebugEnabled)
            {
                RPLog.D($"[ChunkMerger] Performance:");
                RPLog.D($"  BuildAdjacency: {_buildAdjacencyTimer.ElapsedMilliseconds}ms");
                RPLog.D($"  MergeGroups: {_mergeGroupsTimer.ElapsedMilliseconds}ms");
                RPLog.D($"  RefillMergedChunk: {_refillTimer.ElapsedMilliseconds}ms ({_totalRefillCalls} calls, avg {(_totalRefillCalls > 0 ? _refillTimer.ElapsedMilliseconds / _totalRefillCalls : 0)}ms)");
            }
            
            // 4. Final processing - calculate spills from converged chunks (inflow minus lake evaporation)
            finalMergedChunks = currentChunks;
            
            foreach (var chunk in finalMergedChunks)
            {
                // Check if chunk has a valid spill (set by RefillMergedChunk or original analysis)
                if (chunk.SpillTargetTile >= 0 && chunk.ExitTile >= 0)
                {
                    float totalEvaporation = 0f;
                    foreach (int t in chunk.FilledTiles)
                        totalEvaporation += evaporationInput.GetEvaporationRate(t);
                    float netSpill = Mathf.Max(0f, chunk.Inflow - totalEvaporation);
                    if (netSpill > 0f)
                        netSpills.Add(new SpillInput(chunk.ExitTile, chunk.SpillTargetTile, netSpill));
                }
            }
            
            return netSpills;
        }
        
        /// <summary>
        /// Build adjacency graph for chunks that should be merged.
        /// Only merges chunks that are hydrologically unified (backwater scenario).
        /// Cascades (High Lake → Low Lake) are NOT merged to preserve waterfalls.
        /// </summary>
        private Dictionary<int, List<int>> BuildAdjacencyGraph(
            WorldGrid grid, 
            float[] elevation, 
            List<SpillChunk> chunks)
        {
            const float ELEVATION_TOLERANCE = 0.001f; // 1 meter tolerance for all elevation comparisons
            
            Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
            Dictionary<int, SpillChunk> chunkMap = new Dictionary<int, SpillChunk>();
            Dictionary<int, int> tileToChunk = new Dictionary<int, int>();
            
            // Initialize adjacency list and create chunk map
            foreach (var chunk in chunks)
            {
                adjacency[chunk.ChunkID] = new List<int>();
                chunkMap[chunk.ChunkID] = chunk;
            }

            // Map tiles to chunks
            foreach (var chunk in chunks)
            {
                foreach (int tile in chunk.FilledTiles)
                {
                    // If overlap (rare but possible), mark connection
                    if (tileToChunk.TryGetValue(tile, out int existingChunkID))
                    {
                        if (existingChunkID != chunk.ChunkID)
                        {
                            // Overlapping tiles always merge (same physical space)
                            AddEdge(adjacency, chunk.ChunkID, existingChunkID);
                        }
                    }
                    tileToChunk[tile] = chunk.ChunkID;
                }
            }
            
            bool useParallel = Controller.Settings.perfParallelHydrology.CurrentValue;

            if (useParallel)
            {
                ConcurrentBag<(int, int)> edges = new ConcurrentBag<(int, int)>();
                
                Parallel.ForEach(chunks, () => new List<PlanetTile>(), 
                    (chunkA, loopState, localNeighbors) =>
                    {
                        // Check spill relationships
                        if (chunkA.SpillTargetTile >= 0)
                        {
                            if (tileToChunk.TryGetValue(chunkA.SpillTargetTile, out int targetChunkID) && 
                                targetChunkID != chunkA.ChunkID)
                            {
                                SpillChunk chunkB = chunkMap[targetChunkID];
                                float chunkAExitElev = chunkA.ExitTile >= 0 ? elevation[chunkA.ExitTile] : chunkA.WaterSurfaceElevation;
                                
                                // Backwater check
                                if (chunkB.WaterSurfaceElevation >= chunkAExitElev - ELEVATION_TOLERANCE)
                                {
                                    edges.Add((chunkA.ChunkID, targetChunkID));
                                }
                            }
                            // REMOVED: Redundant nested loop (O(N^2)) which searched for spill target in other chunks
                            // Since tileToChunk contains ALL filled tiles, if it's not there, it's not in any chunk.
                        }
                        
                        // Check touching boundaries (using thread-local neighbors list)
                        foreach (int tile in chunkA.FilledTiles)
                        {
                            localNeighbors.Clear();
                            grid.GetTileNeighbors(tile, localNeighbors);
                            
                            foreach (var neighbor in localNeighbors)
                            {
                                if (tileToChunk.TryGetValue(neighbor.tileId, out int neighborChunkID) &&
                                    neighborChunkID != chunkA.ChunkID)
                                {
                                    SpillChunk chunkB = chunkMap[neighborChunkID];
                                    
                                    if (Mathf.Abs(chunkA.WaterSurfaceElevation - chunkB.WaterSurfaceElevation) < ELEVATION_TOLERANCE)
                                    {
                                        edges.Add((chunkA.ChunkID, neighborChunkID));
                                    }
                                }
                            }
                        }
                        
                        return localNeighbors; // Return for reuse
                    },
                    localNeighbors => { /* finalizer */ });
                
                // Post-process: Add edges to adjacency lists (sequential - deterministic)
                foreach (var (a, b) in edges.OrderBy(e => e.Item1).ThenBy(e => e.Item2))
                {
                    if (!adjacency[a].Contains(b)) adjacency[a].Add(b);
                    if (!adjacency[b].Contains(a)) adjacency[b].Add(a);
                }
            }
            else
            {
                // Sequential execution
                foreach (var chunkA in chunks)
                {
                    // Check spill relationships
                    if (chunkA.SpillTargetTile >= 0)
                    {
                        if (tileToChunk.TryGetValue(chunkA.SpillTargetTile, out int targetChunkID) && 
                            targetChunkID != chunkA.ChunkID)
                        {
                            SpillChunk chunkB = chunkMap[targetChunkID];
                            float chunkAExitElev = chunkA.ExitTile >= 0 ? elevation[chunkA.ExitTile] : chunkA.WaterSurfaceElevation;
                            
                            if (chunkB.WaterSurfaceElevation >= chunkAExitElev - ELEVATION_TOLERANCE)
                            {
                                AddEdge(adjacency, chunkA.ChunkID, targetChunkID);
                            }
                        }
                    }
                    
                    // Check touching boundaries
                    List<PlanetTile> neighbors = new List<PlanetTile>();
                    foreach (int tile in chunkA.FilledTiles)
                    {
                        neighbors.Clear();
                        grid.GetTileNeighbors(tile, neighbors);
                        
                        foreach (var neighbor in neighbors)
                        {
                            if (tileToChunk.TryGetValue(neighbor.tileId, out int neighborChunkID) &&
                                neighborChunkID != chunkA.ChunkID)
                            {
                                SpillChunk chunkB = chunkMap[neighborChunkID];
                                
                                if (Mathf.Abs(chunkA.WaterSurfaceElevation - chunkB.WaterSurfaceElevation) < ELEVATION_TOLERANCE)
                                {
                                    AddEdge(adjacency, chunkA.ChunkID, neighborChunkID);
                                }
                            }
                        }
                    }
                }
            }
            
            // Sort adjacency lists for determinism
            foreach (var list in adjacency.Values)
            {
                list.Sort();
            }
            
            return adjacency;
        }
        
        private void AddEdge(Dictionary<int, List<int>> adj, int a, int b)
        {
            if (!adj[a].Contains(b)) adj[a].Add(b);
            if (!adj[b].Contains(a)) adj[b].Add(a);
        }
        
        private List<List<SpillChunk>> FindConnectedComponents(
            List<SpillChunk> chunks, 
            Dictionary<int, List<int>> adjacency)
        {
            List<List<SpillChunk>> groups = new List<List<SpillChunk>>();
            HashSet<int> visited = new HashSet<int>();
            Dictionary<int, SpillChunk> chunkMap = new Dictionary<int, SpillChunk>();
            
            foreach (var c in chunks) chunkMap[c.ChunkID] = c;
            
            foreach (var chunk in chunks)
            {
                if (visited.Contains(chunk.ChunkID)) continue;
                
                List<SpillChunk> group = new List<SpillChunk>();
                Queue<int> queue = new Queue<int>();
                
                queue.Enqueue(chunk.ChunkID);
                visited.Add(chunk.ChunkID);
                
                while (queue.Count > 0)
                {
                    int currentID = queue.Dequeue();
                    group.Add(chunkMap[currentID]);
                    
                    if (adjacency.TryGetValue(currentID, out var neighbors))
                    {
                        foreach (int neighborID in neighbors)
                        {
                            if (!visited.Contains(neighborID))
                            {
                                visited.Add(neighborID);
                                queue.Enqueue(neighborID);
                            }
                        }
                    }
                }
                groups.Add(group);
            }
            
            return groups;
        }
        
        private SpillChunk MergeGroup(
            List<SpillChunk> group,
            int[] initialBasinIDs,
            Dictionary<int, float> baseBasinInflow,
            List<SpillInput> activeSpills)
        {
            SpillChunk master = group[0];
            SpillChunk merged = new SpillChunk(master.ChunkID, master.SinkTileID);
            
            HashSet<int> tiles = new HashSet<int>();
            foreach (var component in group)
            {
                foreach (int t in component.FilledTiles)
                    tiles.Add(t);
            }
            
            merged.FilledTiles.Clear();
            merged.FilledTiles.AddRange(tiles);
            merged.Inflow = ComputeCombinedInflow(tiles, group, initialBasinIDs, baseBasinInflow, activeSpills);
            
            return merged;
        }

        /// <summary>
        /// Computes total inflow into a merged chunk without double-counting internal spills between basins inside the chunk.
        /// Equals the sum of base (non-spill) watershed inflows for all initial basins occupied by the chunk, plus any
        /// active spills whose source tile is OUTSIDE the occupied basins and whose target tile is INSIDE the occupied basins.
        /// </summary>
        private static float ComputeCombinedInflow(
            HashSet<int> filledTiles,
            List<SpillChunk> fallbackGroup,
            int[] initialBasinIDs,
            Dictionary<int, float> baseBasinInflow,
            List<SpillInput> activeSpills)
        {
            if (baseBasinInflow == null || initialBasinIDs == null)
            {
                float fallback = 0f;
                if (fallbackGroup != null)
                {
                    for (int i = 0; i < fallbackGroup.Count; i++)
                        fallback += fallbackGroup[i].Inflow;
                }
                return fallback;
            }

            HashSet<int> occupiedBasins = new HashSet<int>();
            foreach (int t in filledTiles)
            {
                int b = initialBasinIDs[t];
                if (b >= 0)
                    occupiedBasins.Add(b);
            }

            float total = 0f;
            foreach (int b in occupiedBasins)
            {
                if (baseBasinInflow.TryGetValue(b, out float bInflow))
                    total += bInflow;
            }

            if (activeSpills != null)
            {
                for (int i = 0; i < activeSpills.Count; i++)
                {
                    SpillInput sp = activeSpills[i];
                    if (sp.SourceTileID < 0 || sp.TargetTileID < 0)
                        continue;
                    int srcBasin = initialBasinIDs[sp.SourceTileID];
                    int dstBasin = initialBasinIDs[sp.TargetTileID];
                    if (!occupiedBasins.Contains(srcBasin) && occupiedBasins.Contains(dstBasin))
                        total += sp.Amount;
                }
            }

            return total;
        }
        
        /// <summary>
        /// Re-run fill algorithm on merged chunk with combined flow.
        /// The merged chunk may have enough hydraulic head to fill additional tiles.
        /// Uses optimized Priority-Frontier approach with rate-based physics.
        /// </summary>
        private SpillChunk RefillMergedChunk(
            SpillChunk mergedChunk,
            WorldGrid grid,
            float[] elevation,
            bool[] oceanMask,
            int[] initialBasinIDs,
            IEvaporationInput evaporationInput,
            Dictionary<int, float> baseBasinInflow = null,
            List<SpillInput> activeSpills = null)
        {
            // Find the lowest tile in the merged chunk (effective sink / base elevation)
            int lowestTile = -1;
            float lowestElevation = float.MaxValue;
            foreach (int tile in mergedChunk.FilledTiles)
            {
                if (elevation[tile] < lowestElevation)
                {
                    lowestElevation = elevation[tile];
                    lowestTile = tile;
                }
            }
            
            if (lowestTile < 0)
                return mergedChunk; // Empty chunk, shouldn't happen
            
            // Get union of all initial basins currently occupied by the merged chunk
            HashSet<int> occupiedBasins = new HashSet<int>();
            foreach (int tile in mergedChunk.FilledTiles)
            {
                occupiedBasins.Add(initialBasinIDs[tile]);
            }
            
            // Track filled tiles (start with existing merged tiles) and running evaporation sum
            HashSet<int> filledTiles = new HashSet<int>(mergedChunk.FilledTiles);
            float combinedFlow = ComputeCombinedInflow(filledTiles, null, initialBasinIDs, baseBasinInflow, activeSpills);
            mergedChunk.Inflow = combinedFlow;
            float totalEvaporation = 0f;
            
            // Track highest filled tile elevation for comparison
            float highestFilledElevation = float.MinValue;
            foreach (int tile in filledTiles)
            {
                totalEvaporation += evaporationInput.GetEvaporationRate(tile);
                float tileElev = elevation[tile];
                if (tileElev > highestFilledElevation)
                {
                    highestFilledElevation = tileElev;
                }
            }

            // Reset spill tracking before refill; will be populated only if refill reaches a spill lip
            mergedChunk.ExitTile = -1;
            mergedChunk.SpillDirection = -1;
            mergedChunk.SpillTargetTile = -1;
            mergedChunk.WaterSurfaceElevation = highestFilledElevation;
            
            // NEW: Use SimpleMinHeap instead of Dictionary for O(log N) operations
            SimpleMinHeap frontier = new SimpleMinHeap();
            HashSet<int> inFrontier = new HashSet<int>();
            List<PlanetTile> neighbors = new List<PlanetTile>(8);

            // Helper to add neighbors to frontier
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
                    
                    if (!inFrontier.Contains(nID))
                    {
                        frontier.Insert(nElev, nID, tileID, i);
                        inFrontier.Add(nID);
                    }
                }
            }
            
            // Initialize frontier by adding neighbors of ALL existing tiles
            foreach (int tile in filledTiles)
            {
                AddNeighbors(tile);
            }
            
            // Iteratively expand the filled area
            while (frontier.Count > 0)
            {
                // Find lowest candidate (absolute lowest frontier tile)
                var (nextElevation, nextTileID, sourceTileID, sourceNeighborIndex) = frontier.ExtractMin();
                inFrontier.Remove(nextTileID);

                if (filledTiles.Contains(nextTileID)) continue;

                int nextBasinID = initialBasinIDs[nextTileID];
                
                // Q1: Is this tile LOWER than the highest filled tile?
                // If yes, spill immediately (external basin)
                if (nextElevation < highestFilledElevation && !occupiedBasins.Contains(nextBasinID))
                {
                    mergedChunk.ExitTile = sourceTileID;
                    mergedChunk.SpillDirection = sourceNeighborIndex;
                    mergedChunk.SpillTargetTile = nextTileID;
                    mergedChunk.WaterSurfaceElevation = highestFilledElevation;
                    break;
                }
                
                // Q2: RATE-BASED PHYSICS - Can we reach this tile?
                if (combinedFlow <= totalEvaporation)
                {
                    mergedChunk.WaterSurfaceElevation = highestFilledElevation;
                    break;
                }
                
                // Q3: Check if external basin (not in occupied basins)
                if (!occupiedBasins.Contains(nextBasinID))
                {
                    // External basin - spill to it (don't fill)
                    mergedChunk.ExitTile = sourceTileID;
                    mergedChunk.SpillDirection = sourceNeighborIndex;
                    mergedChunk.SpillTargetTile = nextTileID;
                    // Water surface rises to spill target elevation
                    mergedChunk.WaterSurfaceElevation = nextElevation;
                    break;
                }
                
                // Internal basin - fill it and continue
                filledTiles.Add(nextTileID);
                totalEvaporation += evaporationInput.GetEvaporationRate(nextTileID);
                if (occupiedBasins.Add(nextBasinID))
                {
                    // Newly absorbed internal basin: update combined inflow without double-counting internal spills
                    combinedFlow = ComputeCombinedInflow(filledTiles, null, initialBasinIDs, baseBasinInflow, activeSpills);
                    mergedChunk.Inflow = combinedFlow;
                }
                
                // Update highest filled elevation
                if (nextElevation > highestFilledElevation)
                {
                    highestFilledElevation = nextElevation;
                    mergedChunk.WaterSurfaceElevation = highestFilledElevation;
                }
                
                // Update frontier
                AddNeighbors(nextTileID);
            }
            
            // Update merged chunk with expanded geometry
            mergedChunk.FilledTiles.Clear();
            mergedChunk.FilledTiles.AddRange(filledTiles);
            
            return mergedChunk;
        }
    }
}
