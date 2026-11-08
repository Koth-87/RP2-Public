using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using RimWorld.Planet;
using Planets.WorldGen.Hydrology.Models;
using Planets.WorldGen.Hydrology.Logic;
using Planets.Utilities.Logging;
using Planets.Core;
using UnityEngine;

namespace Planets.WorldGen.Hydrology.Core
{
    /// <summary>
    /// Hydrology Engine implementing "Iterative Fresh-State" simulation.
    /// Re-calculates lake geometry from scratch each iteration based on current flow inputs.
    /// Converges when spill patterns stabilize.
    /// </summary>
    public class HydrologySimulator : IHydrologyEngine
    {
        private readonly SpillAnalyzer spillAnalyzer;
        private readonly ChunkMerger chunkMerger;
        private readonly FlowAccumulator flowAccumulator;
        
        public HydrologySimulator()
        {
            spillAnalyzer = new SpillAnalyzer();
            chunkMerger = new ChunkMerger();
            flowAccumulator = new FlowAccumulator();
        }
        
        public HydrologyResult Simulate(
            WorldGrid grid,
            float[] elevation,
            bool[] oceanMask,
            HydrologyConfig config)
        {
            RPLog.D("[Hydrology Engine] Starting Iterative Fresh-State simulation");
            
            int tileCount = elevation.Length;
            
            // Initialize output arrays
            float[] flowAccumulation = new float[tileCount];
            int[] flowDirections = new int[tileCount];
            int[] initialBasinIDs = new int[tileCount];
            
            // Step 1: Calculate flow directions (steepest descent)
            CalculateFlowDirections(grid, elevation, oceanMask, flowDirections, tileCount);
            
            // Step 2: Identify sinks and assign initial basin IDs
            List<int> sinkTiles = IdentifySinks(oceanMask, flowDirections, tileCount);
            AssignInitialBasins(grid, flowDirections, sinkTiles, initialBasinIDs, tileCount, oceanMask);
            
            RPLog.D($"[Hydrology Engine] {sinkTiles.Count} initial sinks identified");

            // Precompute base (zero-spill) watershed inflow per initial basin so ChunkMerger never double-counts internal spills
            var baseBasinInflow = new Dictionary<int, float>(sinkTiles.Count * 2);
            for (int i = 0; i < tileCount; i++)
            {
                if (oceanMask[i])
                    continue;
                int b = initialBasinIDs[i];
                if (b < 0)
                    continue;
                float tileFlow = config.FlowInput.GetInputFlow(i);
                baseBasinInflow.TryGetValue(b, out float cur);
                baseBasinInflow[b] = cur + tileFlow;
            }
            
            // Step 3-6: Iterative Fresh-State Loop
            List<SpillInput> currentSpills = new List<SpillInput>();
            List<SpillChunk> finalChunks = new List<SpillChunk>();
            List<SpillChunk> carriedMultiBasinChunks = new List<SpillChunk>();
            HashSet<int> carriedBasins = new HashSet<int>();
            
            int hashOlder = 0;
            int hashPrevious = 0;
            int period2Hits = 0;
            bool haveOlderHash = false;
            for (int iteration = 0; iteration < config.MaxIterations; iteration++)
            {
                // Step 3: Flow Accumulation (with spills from previous iteration)
                flowAccumulator.RecalculateWithSpills(
                    grid, elevation, oceanMask, flowDirections,
                    config.FlowInput, currentSpills, flowAccumulation, tileCount);
                
                // Step 4: Fill & Spill Analysis (analyze unmerged sinks; carry forward already-merged multi-basin chunks)
                List<SpillChunk> freshChunks = new List<SpillChunk>();
                
                bool useParallel = Controller.Settings.perfParallelHydrology.CurrentValue;

                if (useParallel)
                {
                    ConcurrentBag<SpillChunk> freshChunksBag = new ConcurrentBag<SpillChunk>();
                    Parallel.ForEach(sinkTiles, sinkID => 
                    {
                        if (carriedBasins.Contains(sinkID))
                            return;

                        SpillChunk chunk = spillAnalyzer.AnalyzeSink(
                            grid, elevation, oceanMask, flowAccumulation, initialBasinIDs,
                            sinkID, config.EvaporationInput, sinkID);
                        
                        if (chunk != null)
                        {
                            freshChunksBag.Add(chunk);
                        }
                    });
                    for (int c = 0; c < carriedMultiBasinChunks.Count; c++)
                    {
                        SpillChunk carried = carriedMultiBasinChunks[c];
                        chunkMerger.RefreshCarriedChunk(
                            carried, grid, elevation, oceanMask, initialBasinIDs,
                            config.EvaporationInput, baseBasinInflow, currentSpills);
                        freshChunksBag.Add(carried);
                    }
                    // Ensure deterministic order for subsequent steps (important for stable simulation)
                    freshChunks = freshChunksBag.OrderBy(c => c.ChunkID).ToList();
                }
                else
                {
                    foreach (int sinkID in sinkTiles)
                    {
                        if (carriedBasins.Contains(sinkID))
                            continue;

                        SpillChunk chunk = spillAnalyzer.AnalyzeSink(
                            grid, elevation, oceanMask, flowAccumulation, initialBasinIDs,
                            sinkID, config.EvaporationInput, sinkID);
                        
                        if (chunk != null)
                        {
                            freshChunks.Add(chunk);
                        }
                    }
                    for (int c = 0; c < carriedMultiBasinChunks.Count; c++)
                    {
                        SpillChunk carried = carriedMultiBasinChunks[c];
                        chunkMerger.RefreshCarriedChunk(
                            carried, grid, elevation, oceanMask, initialBasinIDs,
                            config.EvaporationInput, baseBasinInflow, currentSpills);
                        freshChunks.Add(carried);
                    }
                    freshChunks.Sort((a, b) => a.ChunkID.CompareTo(b.ChunkID));
                }
                
                // Step 5: Adjacency Merging + Merge-and-Refill + Net Spill Calculation
                List<SpillInput> nextSpills = chunkMerger.MergeAndCalculateSpills(
                    grid, elevation, freshChunks, out finalChunks, oceanMask, initialBasinIDs, 
                    config.EvaporationInput, baseBasinInflow, currentSpills);
                
                // Step 6: Convergence Check
                if (Controller.DebugEnabled)
                    NoteSpillChange(iteration, config.MaxIterations, currentSpills, nextSpills, ref hashOlder, ref hashPrevious, ref haveOlderHash, ref period2Hits);
                if (SpillsConverged(currentSpills, nextSpills))
                {
                    RPLog.D($"[Hydrology Engine] Converged after {iteration + 1} iterations");
                    break;
                }
                
                currentSpills = nextSpills;
                carriedMultiBasinChunks.Clear();
                carriedBasins.Clear();
                for (int c = 0; c < finalChunks.Count; c++)
                {
                    SpillChunk chunk = finalChunks[c];
                    if (chunk.OccupiedBasins.Count > 1)
                    {
                        carriedMultiBasinChunks.Add(chunk);
                        foreach (int b in chunk.OccupiedBasins)
                            carriedBasins.Add(b);
                    }
                }
                
                if (iteration == config.MaxIterations - 1)
                {
                    RPLog.W($"[Hydrology Engine] Max iterations ({config.MaxIterations}) reached. "
                        + $"Two-step target repeats: {period2Hits}.");
                }
            }
            
            // Step 7: Finalization - Generate output arrays
            int[] mergedLakeIDs = new int[tileCount];
            int[] spillDirections = new int[tileCount];
            int[] mergedBasinIDs = new int[tileCount];
            
            InitializeArrays(mergedLakeIDs, spillDirections, mergedBasinIDs, tileCount);
            
            // Populate lake data
            Dictionary<int, int> lakeSpillTargets = PopulateLakeData(
                finalChunks, mergedLakeIDs, spillDirections, flowAccumulation);
            
            // Assign merged basin IDs using Basin Graph (atomic basin logic)
            CalculateMergedBasins(finalChunks, initialBasinIDs, oceanMask, 
                mergedLakeIDs, lakeSpillTargets, mergedBasinIDs, tileCount);
            
            RPLog.D($"[Hydrology Engine] Complete - {finalChunks.Count} final lakes");
            
            return new HydrologyResult
            {
                FlowAccumulation = flowAccumulation,
                InitialBasinIDs = initialBasinIDs,
                SpillDirections = spillDirections,
                FlowDirections = flowDirections,
                MergedLakeIDs = mergedLakeIDs,
                MergedBasinIDs = mergedBasinIDs
            };
        }
        
        /// <summary>
        /// Calculate flow directions using steepest descent (D8).
        /// </summary>
        private void CalculateFlowDirections(
            WorldGrid grid,
            float[] elevation,
            bool[] oceanMask,
            int[] flowDirections,
            int tileCount)
        {
            List<PlanetTile> neighbors = new List<PlanetTile>();
            
            for (int i = 0; i < tileCount; i++)
            {
                if (oceanMask[i])
                {
                    flowDirections[i] = -1;
                    continue;
                }
                
                neighbors.Clear();
                grid.GetTileNeighbors(i, neighbors);
                
                float currentElev = elevation[i];
                int steepestDir = -1;
                float steepestElev = currentElev;
                int steepestTile = int.MaxValue;
                
                for (int j = 0; j < neighbors.Count; j++)
                {
                    int neighborId = neighbors[j].tileId;
                    float neighborElev = elevation[neighborId];
                    // Equal heights have no slope. The lower tile id keeps the graph a DAG.
                    bool downhill = neighborElev < steepestElev
                        || (neighborElev == steepestElev && neighborElev <= currentElev && neighborId < steepestTile);
                    if (downhill)
                    {
                        steepestElev = neighborElev;
                        steepestDir = j;
                        steepestTile = neighborId;
                    }
                }
                
                flowDirections[i] = steepestDir; // -1 if sink
            }
        }
        
        /// <summary>
        /// Identify all sink tiles (local minima).
        /// </summary>
        private List<int> IdentifySinks(bool[] oceanMask, int[] flowDirections, int tileCount)
        {
            List<int> sinks = new List<int>();
            for (int i = 0; i < tileCount; i++)
            {
                if (!oceanMask[i] && flowDirections[i] < 0)
                {
                    sinks.Add(i);
                }
            }
            return sinks;
        }
        
        /// <summary>
        /// Assign initial basin IDs by tracing each tile to its sink.
        /// </summary>
        private void AssignInitialBasins(
            WorldGrid grid,
            int[] flowDirections,
            List<int> sinks,
            int[] initialBasinIDs,
            int tileCount,
            bool[] oceanMask)
        {
            List<PlanetTile> neighbors = new List<PlanetTile>();
            
            // Initialize to -1
            for (int i = 0; i < tileCount; i++)
            {
                initialBasinIDs[i] = -1;
            }
            
            // Mark sinks with their own ID
            foreach (int sink in sinks)
            {
                initialBasinIDs[sink] = sink;
            }
            
            // Identify and mark coastal tiles (land tiles flowing to ocean) as terminuses
            for (int i = 0; i < tileCount; i++)
            {
                if (oceanMask[i]) continue; // Skip ocean tiles
                if (initialBasinIDs[i] != -1) continue; // Already assigned (land sink)
                
                int flowDir = flowDirections[i];
                if (flowDir >= 0)
                {
                    neighbors.Clear();
                    grid.GetTileNeighbors(i, neighbors);
                    if (flowDir < neighbors.Count)
                    {
                        int target = neighbors[flowDir].tileId;
                        // Only mark as coastal if target is OCEAN, not land sink
                        if (target >= 0 && target < tileCount && oceanMask[target])
                        {
                            initialBasinIDs[i] = i;
                        }
                    }
                }
            }
            
            // Trace all other tiles to find their sink, memoizing the entire path
            var path = new List<int>(64);
            for (int i = 0; i < tileCount; i++)
            {
                if (oceanMask[i] || initialBasinIDs[i] != -1) continue;
                
                path.Clear();
                int current = i;
                int resolvedBasin = -1;
                int hops = 0;
                
                while (hops < 5000)
                {
                    if (initialBasinIDs[current] != -1)
                    {
                        resolvedBasin = initialBasinIDs[current];
                        break;
                    }
                    path.Add(current);
                    int dir = flowDirections[current];
                    if (dir < 0)
                        break;
                    
                    neighbors.Clear();
                    grid.GetTileNeighbors(current, neighbors);
                    if (dir >= neighbors.Count) break;
                    
                    current = neighbors[dir].tileId;
                    hops++;
                }

                for (int p = 0; p < path.Count; p++)
                    initialBasinIDs[path[p]] = resolvedBasin;
            }
        }
        
        /// <summary>
        /// One line per try: how many spills switched target, how many only changed amount,
        /// and whether this target map is the same as two tries ago.
        /// </summary>
        private static void NoteSpillChange(
            int iteration,
            int maxIterations,
            List<SpillInput> previous,
            List<SpillInput> current,
            ref int hashOlder,
            ref int hashPrevious,
            ref bool haveOlderHash,
            ref int period2Hits)
        {
            var prevMap = new Dictionary<int, SpillInput>(previous.Count);
            for (int i = 0; i < previous.Count; i++)
                prevMap[previous[i].SourceTileID] = previous[i];

            int targetsChanged = 0;
            int amountsChanged = 0;
            float maxAbsDelta = 0f;
            int hash = 17;
            var seen = new HashSet<int>();
            var amountDeltas = new List<string>();
            for (int i = 0; i < current.Count; i++)
            {
                SpillInput curr = current[i];
                unchecked
                {
                    hash += curr.SourceTileID * 73856093 ^ curr.TargetTileID * 19349663;
                }
                seen.Add(curr.SourceTileID);
                if (!prevMap.TryGetValue(curr.SourceTileID, out SpillInput prev))
                    targetsChanged++;
                else if (prev.TargetTileID != curr.TargetTileID)
                    targetsChanged++;
                else
                {
                    float delta = curr.Amount - prev.Amount;
                    float abs = Mathf.Abs(delta);
                    if (abs > 0.01f)
                    {
                        amountsChanged++;
                        if (abs > maxAbsDelta)
                            maxAbsDelta = abs;
                        if (amountDeltas.Count < 8)
                            amountDeltas.Add($"tile {curr.SourceTileID}->{curr.TargetTileID} {prev.Amount:F4} -> {curr.Amount:F4} (delta {delta:F4})");
                    }
                }
            }
            for (int i = 0; i < previous.Count; i++)
            {
                if (!seen.Contains(previous[i].SourceTileID))
                    targetsChanged++;
            }

            bool period2 = haveOlderHash && hash == hashOlder && hash != hashPrevious;
            if (period2)
                period2Hits++;
            RPLog.D($"[Hydrology] iter {iteration + 1} spills {current.Count} targetsChanged {targetsChanged} amountsChanged {amountsChanged} maxAbsDelta {maxAbsDelta:F4} period2 {period2}");
            bool detail = amountsChanged > 0 && amountsChanged <= 8
                && (iteration < 25 || iteration % 50 == 49 || iteration == maxIterations - 1);
            if (detail)
            {
                for (int i = 0; i < amountDeltas.Count; i++)
                    RPLog.D("[Hydrology]   " + amountDeltas[i]);
            }
            hashOlder = hashPrevious;
            hashPrevious = hash;
            haveOlderHash = true;
        }

        /// <summary>
        /// Check if spill patterns have converged (same targets and amounts).
        /// Uses strict set comparison to avoid false positives.
        /// </summary>
        private bool SpillsConverged(List<SpillInput> previous, List<SpillInput> current)
        {
            if (previous.Count != current.Count)
            {
                return false;
            }
            
            // Create dictionary for O(1) lookup of previous spills
            // Key: SourceTileID (unique per spill list)
            Dictionary<int, SpillInput> prevMap = new Dictionary<int, SpillInput>();
            foreach (var spill in previous)
            {
                prevMap[spill.SourceTileID] = spill;
            }
            
            // Verify every current spill matches a previous one
            foreach (var curr in current)
            {
                if (!prevMap.TryGetValue(curr.SourceTileID, out SpillInput prev))
                {
                    return false; // New spill source appeared
                }
                
                if (prev.TargetTileID != curr.TargetTileID)
                {
                    return false; // Target changed (path shift)
                }
                
                if (Mathf.Abs(prev.Amount - curr.Amount) > 0.01f)
                {
                    return false; // Flow amount changed
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Initialize result arrays to default values.
        /// </summary>
        private void InitializeArrays(
            int[] lakeIDs,
            int[] spillDirs,
            int[] basinIDs,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                lakeIDs[i] = -1;
                spillDirs[i] = -1;
                basinIDs[i] = -1;
            }
        }
        
        /// <summary>
        /// Populate lake-related arrays from final chunks.
        /// Returns a map of LakeID -> SpillTargetTile for basin tracing.
        /// </summary>
        private Dictionary<int, int> PopulateLakeData(
            List<SpillChunk> finalChunks,
            int[] mergedLakeIDs,
            int[] spillDirections,
            float[] flowAccumulation)
        {
            Dictionary<int, int> lakeSpillTargets = new Dictionary<int, int>();
            
            foreach (var chunk in finalChunks)
            {
                // Mark all filled tiles with this lake ID
                foreach (int tile in chunk.FilledTiles)
                {
                    mergedLakeIDs[tile] = chunk.ChunkID;
                    // Overwrite flow accumulation with lake's total inflow for uniform visualization
                    flowAccumulation[tile] = chunk.Inflow;
                }
                
                // Mark spill direction on exit tile
                if (chunk.ExitTile >= 0 && chunk.SpillDirection >= 0)
                {
                    spillDirections[chunk.ExitTile] = chunk.SpillDirection;
                }
                
                // Record spill target for path tracing
                lakeSpillTargets[chunk.ChunkID] = chunk.SpillTargetTile;
            }
            
            return lakeSpillTargets;
        }
        
        /// <summary>
        /// Calculate merged basin IDs using Basin Graph (atomic basin logic).
        /// If Initial Basin A spills into Basin B, ALL tiles in Basin A belong to Basin B's watershed.
        /// Uses Union-Find to resolve ultimate roots (ocean or terminal lakes).
        /// </summary>
        private void CalculateMergedBasins(
            List<SpillChunk> finalChunks,
            int[] initialBasinIDs,
            bool[] oceanMask,
            int[] mergedLakeIDs,
            Dictionary<int, int> lakeSpillTargets,
            int[] mergedBasinIDs,
            int tileCount)
        {
            // Step 1: Build Basin Graph with unification
            // Returns both the spill graph and the unification map
            var (basinSpillTargets, basinUnificationMap) = BuildBasinGraph(finalChunks, initialBasinIDs, oceanMask, lakeSpillTargets);
            
            // Step 2: Resolve Roots using Union-Find
            // Roots are basins that spill to Ocean or Terminal Lakes
            Dictionary<int, int> basinRootMap = ResolveBasinRoots(basinSpillTargets, basinUnificationMap);
            
            // Step 3: Assign Merged Basin IDs
            // Map every tile: InitialBasinID -> UnifiedBasinID -> RootBasinID -> MergedBasinID
            
            // Helper to resolve unified basin root (same logic as BuildBasinGraph.FindRoot with path compression)
            // Uses memoization to cache results and perform path compression
            Dictionary<int, int> unifiedRootCache = new Dictionary<int, int>();
            
            int FindUnifiedRoot(int basinID, Dictionary<int, int> unificationMap)
            {
                // Check cache first
                if (unifiedRootCache.TryGetValue(basinID, out int cachedRoot))
                {
                    return cachedRoot;
                }
                
                // Not in unification map - is its own root
                if (!unificationMap.ContainsKey(basinID))
                {
                    unifiedRootCache[basinID] = basinID;
                    return basinID;
                }
                
                // Follow parent chain to find root
                int root = basinID;
                List<int> path = new List<int> { basinID };
                
                while (unificationMap.TryGetValue(root, out int parent) && parent != root)
                {
                    // Check if parent is already cached (path compression opportunity)
                    if (unifiedRootCache.TryGetValue(parent, out int cachedParentRoot))
                    {
                        root = cachedParentRoot;
                        break;
                    }
                    
                    path.Add(parent);
                    root = parent;
                }
                
                // Path compression: update all nodes in path to point directly to root
                foreach (int node in path)
                {
                    unifiedRootCache[node] = root;
                }
                
                return root;
            }
            
            Dictionary<int, int> rootToMergedBasinID = new Dictionary<int, int>();
            int nextMergedBasinID = 0;
            
            for (int i = 0; i < tileCount; i++)
            {
                int initialBasinID = initialBasinIDs[i];
                if (initialBasinID < 0) continue; // Ocean or unassigned
                
                // Resolve initial basin to unified basin root
                int unifiedBasinID = FindUnifiedRoot(initialBasinID, basinUnificationMap);
                
                // Get the root basin for this unified basin (via spill graph)
                int rootBasinID = basinRootMap.TryGetValue(unifiedBasinID, out int root) ? root : unifiedBasinID;
                
                // Assign a merged basin ID to this root
                if (!rootToMergedBasinID.TryGetValue(rootBasinID, out int mergedBasinID))
                {
                    mergedBasinID = nextMergedBasinID++;
                    rootToMergedBasinID[rootBasinID] = mergedBasinID;
                }
                
                mergedBasinIDs[i] = mergedBasinID;
            }
            
            RPLog.D($"[Hydrology Engine] {nextMergedBasinID} merged basins identified (from {basinSpillTargets.Count} initial basins)");
        }
        
        /// <summary>
        /// Build Basin Graph: Map Initial Basin ID -> Target Basin ID (where it spills).
        /// First unifies basins that are part of the same chunk, then builds spill graph.
        /// Returns tuple: (basinSpillTargets, basinUnificationMap)
        /// </summary>
        private (Dictionary<int, int>, Dictionary<int, int>) BuildBasinGraph(
            List<SpillChunk> finalChunks,
            int[] initialBasinIDs,
            bool[] oceanMask,
            Dictionary<int, int> lakeSpillTargets)
        {
            // Step 1: Unify basins that are part of the same chunk
            // Use Union-Find to track which initial basins have been unified by lakes
            Dictionary<int, int> basinParent = new Dictionary<int, int>();
            
            int FindRoot(int basinID)
            {
                if (!basinParent.ContainsKey(basinID))
                {
                    basinParent[basinID] = basinID; // Self-parent = root
                    return basinID;
                }
                
                // Path compression
                if (basinParent[basinID] != basinID)
                {
                    basinParent[basinID] = FindRoot(basinParent[basinID]);
                }
                
                return basinParent[basinID];
            }
            
            void UnifyBasins(int basinA, int basinB)
            {
                int rootA = FindRoot(basinA);
                int rootB = FindRoot(basinB);
                
                if (rootA != rootB)
                {
                    // Unify by making one root point to the other
                    basinParent[rootB] = rootA;
                }
            }
            
            // Unify all basins touched by each chunk
            HashSet<int> coastalSinks = new HashSet<int>(); // Track coastal sinks for sanity check
            
            foreach (var chunk in finalChunks)
            {
                HashSet<int> touchedBasins = new HashSet<int>();
                foreach (int tile in chunk.FilledTiles)
                {
                    int basinID = initialBasinIDs[tile];
                    if (basinID >= 0)
                    {
                        touchedBasins.Add(basinID);
                    }
                }
                
                // Unify all touched basins
                if (touchedBasins.Count > 1)
                {
                    int firstBasin = -1;
                    foreach (int basinID in touchedBasins)
                    {
                        if (firstBasin < 0)
                        {
                            firstBasin = basinID;
                        }
                        else
                        {
                            UnifyBasins(firstBasin, basinID);
                        }
                    }
                }
                
                // Track if this chunk spills to ocean (coastal sink)
                if (chunk.SpillTargetTile >= 0 && oceanMask[chunk.SpillTargetTile])
                {
                    int sinkBasinID = initialBasinIDs[chunk.SinkTileID];
                    if (sinkBasinID >= 0)
                    {
                        coastalSinks.Add(FindRoot(sinkBasinID));
                    }
                }
            }
            
            // Step 2: Build spill graph using unified basin IDs
            Dictionary<int, int> basinSpillTargets = new Dictionary<int, int>();
            
            foreach (var chunk in finalChunks)
            {
                // Get the unified basin ID for this chunk
                // Use the chunk's sink basin, but resolve to its root
                int sinkBasinID = initialBasinIDs[chunk.SinkTileID];
                if (sinkBasinID < 0) continue; // Invalid basin
                
                int sourceBasinID = FindRoot(sinkBasinID);
                
                // Where does this chunk spill?
                if (chunk.SpillTargetTile >= 0)
                {
                    // Check if spill target is ocean
                    if (oceanMask[chunk.SpillTargetTile])
                    {
                        // Spills to ocean - mark as root (no target basin)
                        // If this unified basin already has a spill target, that's an error
                        if (basinSpillTargets.ContainsKey(sourceBasinID) && basinSpillTargets[sourceBasinID] != -1)
                        {
                            RPLog.Warning($"[Basin Graph] Unified basin {sourceBasinID} has multiple spill targets (ocean and {basinSpillTargets[sourceBasinID]})");
                        }
                        basinSpillTargets[sourceBasinID] = -1;
                    }
                    else
                    {
                        // Spills to another basin - get its unified basin ID
                        int targetInitialBasinID = initialBasinIDs[chunk.SpillTargetTile];
                        if (targetInitialBasinID >= 0)
                        {
                            int targetUnifiedBasinID = FindRoot(targetInitialBasinID);
                            
                            // If this unified basin already has a spill target, that's an error
                            if (basinSpillTargets.ContainsKey(sourceBasinID))
                            {
                                int existingTarget = basinSpillTargets[sourceBasinID];
                                if (existingTarget != targetUnifiedBasinID && existingTarget != -1)
                                {
                                    RPLog.Warning($"[Basin Graph] Unified basin {sourceBasinID} has multiple spill targets ({existingTarget} and {targetUnifiedBasinID})");
                                }
                            }
                            
                            basinSpillTargets[sourceBasinID] = targetUnifiedBasinID;
                        }
                        else
                        {
                            // Target is ocean or invalid - mark as root
                            basinSpillTargets[sourceBasinID] = -1;
                        }
                    }
                }
                else
                {
                    // Terminal lake - mark as root (no spill)
                    basinSpillTargets[sourceBasinID] = -1;
                }
            }
            
            // SANITY CHECK: Verify coastal sinks map correctly
            // Coastal basins should only be unified if they're part of the same lake or have legitimate spill paths
            if (Controller.DebugEnabled && coastalSinks.Count > 0)
            {
                // Check if multiple distinct coastal sinks got unified without a spill path
                Dictionary<int, int> coastalRootCounts = new Dictionary<int, int>();
                foreach (int coastalRoot in coastalSinks)
                {
                    if (!coastalRootCounts.ContainsKey(coastalRoot))
                        coastalRootCounts[coastalRoot] = 0;
                    coastalRootCounts[coastalRoot]++;
                }
                
                // If we have fewer unified roots than original coastal sinks, some were merged
                if (coastalRootCounts.Count < coastalSinks.Count)
                {
                    RPLog.D($"[Basin Graph] {coastalSinks.Count} coastal chunks unified to {coastalRootCounts.Count} roots (expected: legitimate lake merges)");
                }
            }
            
            return (basinSpillTargets, basinParent);
        }
        
        private int ResolveRootWithCycleGuard(
            int basinID,
            Dictionary<int, int> basinSpillTargets,
            Dictionary<int, int> rootMap,
            HashSet<int> visiting)
        {
            if (rootMap.TryGetValue(basinID, out int cachedRoot))
                return cachedRoot;

            if (!basinSpillTargets.TryGetValue(basinID, out int targetBasinID) || targetBasinID < 0)
            {
                rootMap[basinID] = basinID;
                return basinID;
            }

            if (!visiting.Add(basinID))
            {
                // Cycle detected in spill graph; break at current basin
                rootMap[basinID] = basinID;
                return basinID;
            }

            int root = ResolveRootWithCycleGuard(targetBasinID, basinSpillTargets, rootMap, visiting);
            visiting.Remove(basinID);
            rootMap[basinID] = root;
            return root;
        }

        /// <summary>
        /// Resolve basin roots using Union-Find (path compression).
        /// Traverses the basin graph to find ultimate roots (ocean or terminal lakes).
        /// Returns dictionary: unifiedBasinID -> rootBasinID.
        /// Only contains entries for unified basin IDs (not initial basin IDs).
        /// </summary>
        private Dictionary<int, int> ResolveBasinRoots(
            Dictionary<int, int> basinSpillTargets,
            Dictionary<int, int> basinUnificationMap)
        {
            Dictionary<int, int> rootMap = new Dictionary<int, int>();
            HashSet<int> visiting = new HashSet<int>();
            
            // Find root for each unified basin that has a spill target
            foreach (int basinID in basinSpillTargets.Keys)
            {
                visiting.Clear();
                ResolveRootWithCycleGuard(basinID, basinSpillTargets, rootMap, visiting);
            }
            
            // Also ensure all unified basin roots have entries (even if they don't spill)
            // These are roots themselves (basins that don't have chunks or don't spill)
            // We need to find all unified basin roots from the unification map
            HashSet<int> unifiedRoots = new HashSet<int>();
            foreach (var kvp in basinUnificationMap)
            {
                // Find the root of this basin in the unification hierarchy
                int current = kvp.Key;
                int guard = 0;
                while (basinUnificationMap.TryGetValue(current, out int parent) && parent != current && guard++ < 10000)
                {
                    current = parent;
                }
                unifiedRoots.Add(current);
            }
            
            // Add all unified roots that aren't already in rootMap
            foreach (int unifiedRoot in unifiedRoots)
            {
                if (!rootMap.ContainsKey(unifiedRoot))
                {
                    // Unified basin doesn't spill (or wasn't in finalChunks) - it's its own root
                    rootMap[unifiedRoot] = unifiedRoot;
                }
            }
            
            return rootMap;
        }
    }
}
