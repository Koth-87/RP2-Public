using System.Collections.Generic;
using System.Linq;
using Planets.Utilities.Logging;
using Planets.Utilities.WorldGen;
using Planets.WorldGen.Climate;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Phase 8–9: Basin Aggregation.
    /// Aggregates atomic merged basins into continental-scale river systems based on terminus identity,
    /// soft (non-mountain) boundaries, and outlet proximity (300 km for ocean-draining basins).
    /// </summary>
    public class BasinAggregator : TerrainAnalysisBase
    {
        private class BasinInfo
        {
            public int ID;
            public int OutletTile;
            public int TerminusID;
            public float FlowAccumulation;
            public int ParentID;
            public HashSet<int> Neighbors = new HashSet<int>();
        }

        private Dictionary<int, BasinInfo> basinMap;
        private Dictionary<int, int> lakeSpillExit;
        private Dictionary<long, bool> edgeIsHard;

        public override void Execute()
        {
            RPLog.D("[Terrain Analysis] Aggregating merged basins into river systems");

            InitializeBasinMetadata();
            BuildBasinGraph();
            AggregateBasins();
            ApplyAggregatedSystems();
            UpdateDrainageClassification();
        }

        private void InitializeBasinMetadata()
        {
            basinMap = new Dictionary<int, BasinInfo>();
            lakeSpillExit = new Dictionary<int, int>();

            for (int i = 0; i < tileCount; i++)
            {
                int lakeID = climate.MergedLakeIDTopology[i];
                int spillDir = climate.SpillDirectionTopology[i];
                if (lakeID >= 0 && spillDir >= 0 && !lakeSpillExit.ContainsKey(lakeID))
                {
                    neighborBuffer.Clear();
                    grid.GetTileNeighbors(i, neighborBuffer);
                    if (spillDir < neighborBuffer.Count)
                        lakeSpillExit[lakeID] = neighborBuffer[spillDir].tileId;
                }

                if (climate.OceanMask[i])
                    continue;

                int basinID = climate.MergedBasinIDTopology[i];
                if (basinID < 0)
                    continue;

                if (!basinMap.TryGetValue(basinID, out BasinInfo info))
                {
                    info = new BasinInfo
                    {
                        ID = basinID,
                        ParentID = basinID,
                        OutletTile = -1,
                        FlowAccumulation = -1f,
                        TerminusID = int.MinValue
                    };
                    basinMap[basinID] = info;
                }

                if (climate.FlowAccumulationTopology[i] > info.FlowAccumulation)
                {
                    info.FlowAccumulation = climate.FlowAccumulationTopology[i];
                    info.OutletTile = i;
                }
            }

            foreach (BasinInfo info in basinMap.Values)
            {
                if (info.OutletTile == -1)
                    continue;
                info.TerminusID = FindBasinTerminus(info.OutletTile);
            }

            RPLog.D($"[Terrain Analysis] Initialized {basinMap.Count} merged basins");
        }

        private int FindBasinTerminus(int startTile)
        {
            int current = startTile;
            var visited = new HashSet<int>();

            while (visited.Count < 1000)
            {
                if (!visited.Add(current))
                    return -current;

                int lakeID = climate.MergedLakeIDTopology[current];
                if (lakeID >= 0)
                {
                    if (lakeSpillExit.TryGetValue(lakeID, out int nextTile))
                    {
                        current = nextTile;
                        continue;
                    }
                    return lakeID;
                }

                if (climate.OceanMask[current])
                {
                    int bodyID = climate.WaterBodyID[current];
                    if (bodyID == 0)
                        return 0;
                    if (bodyID > 0)
                        return -bodyID;
                    return -current;
                }

                int flowDir = climate.FlowDirectionTopology[current];
                if (flowDir < 0)
                    return -current;

                neighborBuffer.Clear();
                grid.GetTileNeighbors(current, neighborBuffer);
                if (flowDir >= neighborBuffer.Count)
                    return -current;

                current = neighborBuffer[flowDir].tileId;
            }

            return -current;
        }

        private void BuildBasinGraph()
        {
            edgeIsHard = new Dictionary<long, bool>();

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                    continue;

                int myBasin = climate.MergedBasinIDTopology[i];
                if (myBasin < 0 || !basinMap.ContainsKey(myBasin))
                    continue;

                bool isHighElev = climate.ElevationBand[i] >= 4;

                neighborBuffer.Clear();
                grid.GetTileNeighbors(i, neighborBuffer);

                foreach (var neighbor in neighborBuffer)
                {
                    int nID = neighbor.tileId;
                    if (climate.OceanMask[nID])
                        continue;

                    int nBasin = climate.MergedBasinIDTopology[nID];
                    if (nBasin < 0 || nBasin == myBasin || !basinMap.ContainsKey(nBasin))
                        continue;

                    basinMap[myBasin].Neighbors.Add(nBasin);

                    long edgeKey = GetEdgeKey(myBasin, nBasin);
                    bool connectionIsLow = !isHighElev && climate.ElevationBand[nID] < 4;

                    if (!edgeIsHard.ContainsKey(edgeKey))
                        edgeIsHard[edgeKey] = true;

                    if (connectionIsLow)
                        edgeIsHard[edgeKey] = false;
                }
            }
        }

        private void AggregateBasins()
        {
            List<BasinInfo> sortedBasins = basinMap.Values.OrderBy(b => b.FlowAccumulation).ToList();
            const float MAX_MERGE_DIST_KM = 300f;

            foreach (BasinInfo basin in sortedBasins)
            {
                int currentID = basin.ID;
                int rootID = FindRoot(currentID);
                if (rootID != currentID)
                    continue;

                BasinInfo rootBasin = basinMap[rootID];
                int bestTargetRoot = -1;
                float bestDist = float.MaxValue;

                foreach (int neighborID in basin.Neighbors)
                {
                    int neighborRootID = FindRoot(neighborID);
                    if (neighborRootID == rootID)
                        continue;

                    BasinInfo targetRoot = basinMap[neighborRootID];
                    if (rootBasin.TerminusID != targetRoot.TerminusID)
                        continue;

                    long edgeKey = GetEdgeKey(currentID, neighborID);
                    if (edgeIsHard.TryGetValue(edgeKey, out bool isHard) && isHard)
                        continue;

                    if (rootBasin.TerminusID == 0)
                    {
                        float dist = ClimateGeometryHelpers.ArcDistanceKm(
                            climate.TileCenters[rootBasin.OutletTile],
                            climate.TileCenters[targetRoot.OutletTile]);

                        if (dist > MAX_MERGE_DIST_KM)
                            continue;

                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestTargetRoot = neighborRootID;
                        }
                    }
                    else
                    {
                        bestTargetRoot = neighborRootID;
                        break;
                    }
                }

                if (bestTargetRoot != -1)
                {
                    BasinInfo targetRoot = basinMap[bestTargetRoot];
                    basin.ParentID = targetRoot.ID;
                    foreach (int inherited in basin.Neighbors)
                    {
                        long oldKey = GetEdgeKey(basin.ID, inherited);
                        long newKey = GetEdgeKey(targetRoot.ID, inherited);
                        if (!edgeIsHard.TryGetValue(oldKey, out bool oldHard))
                            continue;
                        if (!edgeIsHard.ContainsKey(newKey) || !oldHard)
                            edgeIsHard[newKey] = oldHard;
                    }
                    targetRoot.Neighbors.UnionWith(basin.Neighbors);
                }
            }
        }

        private void ApplyAggregatedSystems()
        {
            var uniqueSystems = new HashSet<int>();

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    climate.RiverSystemIDTopology[i] = -1;
                    continue;
                }

                int basinID = climate.MergedBasinIDTopology[i];
                if (basinID >= 0 && basinMap.ContainsKey(basinID))
                {
                    int rootID = FindRoot(basinID);
                    climate.RiverSystemIDTopology[i] = rootID;
                    uniqueSystems.Add(rootID);
                    climate.EndorheicMask[i] = basinMap[rootID].TerminusID != 0;
                }
                else
                {
                    climate.RiverSystemIDTopology[i] = -1;
                }
            }

            RPLog.D($"[Terrain Analysis] Aggregated into {uniqueSystems.Count} river systems");
        }

        private void UpdateDrainageClassification()
        {
            if (climate.DrainageTypeClassification == null)
                return;

            var spillingLakes = new HashSet<int>();
            for (int i = 0; i < tileCount; i++)
            {
                if (climate.SpillDirectionTopology[i] >= 0)
                {
                    int lakeID = climate.MergedLakeIDTopology[i];
                    if (lakeID >= 0)
                        spillingLakes.Add(lakeID);
                }
            }

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                    continue;

                int lakeID = climate.MergedLakeIDTopology[i];
                if (lakeID >= 0)
                {
                    climate.DrainageTypeClassification[i] = spillingLakes.Contains(lakeID)
                        ? DrainageType.FilledSpilling
                        : DrainageType.FilledTerminal;
                }
                else
                {
                    climate.DrainageTypeClassification[i] = climate.EndorheicMask[i]
                        ? DrainageType.EndorheicLake
                        : DrainageType.ExorheicOcean;
                }
            }
        }

        private int FindRoot(int id)
        {
            if (!basinMap.ContainsKey(id))
                return id;

            if (basinMap[id].ParentID != id)
                basinMap[id].ParentID = FindRoot(basinMap[id].ParentID);

            return basinMap[id].ParentID;
        }

        private static long GetEdgeKey(int a, int b)
        {
            if (a < b)
                return ((long)a << 32) | (uint)b;
            return ((long)b << 32) | (uint)a;
        }
    }
}
