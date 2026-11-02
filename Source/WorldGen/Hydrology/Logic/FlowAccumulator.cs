using System.Collections.Generic;
using RimWorld.Planet;
using Planets.WorldGen.Hydrology.Models;

namespace Planets.WorldGen.Hydrology.Logic
{
    /// <summary>
    /// Calculates flow accumulation with spill contributions.
    /// Precomputes the elevation-sorted land tile order and downstream tile targets once per simulation.
    /// </summary>
    public class FlowAccumulator
    {
        private int[] cachedSortedLandTiles;
        private int[] cachedDownstreamTile;

        public void RecalculateWithSpills(
            WorldGrid grid,
            float[] elevation,
            bool[] oceanMask,
            int[] flowDirections,
            IFlowInput flowInput,
            List<SpillInput> spillInputs,
            float[] flowAccumulation,
            int tileCount)
        {
            if (cachedSortedLandTiles == null || cachedDownstreamTile == null || cachedDownstreamTile.Length != tileCount)
                BuildTopologyOrder(grid, elevation, oceanMask, flowDirections, tileCount);

            for (int i = 0; i < tileCount; i++)
                flowAccumulation[i] = oceanMask[i] ? 0f : flowInput.GetInputFlow(i);

            for (int s = 0; s < spillInputs.Count; s++)
            {
                SpillInput spill = spillInputs[s];
                if (spill.TargetTileID >= 0 && spill.TargetTileID < tileCount && !oceanMask[spill.TargetTileID])
                    flowAccumulation[spill.TargetTileID] += spill.Amount;
            }

            int n = cachedSortedLandTiles.Length;
            for (int idx = 0; idx < n; idx++)
            {
                int tileID = cachedSortedLandTiles[idx];
                int downstreamID = cachedDownstreamTile[tileID];
                if (downstreamID >= 0)
                    flowAccumulation[downstreamID] += flowAccumulation[tileID];
            }
        }

        private void BuildTopologyOrder(
            WorldGrid grid,
            float[] elevation,
            bool[] oceanMask,
            int[] flowDirections,
            int tileCount)
        {
            var sorted = new List<(int tileID, float elevation)>(tileCount / 2);
            cachedDownstreamTile = new int[tileCount];
            var neighbors = new List<PlanetTile>(8);

            for (int i = 0; i < tileCount; i++)
            {
                cachedDownstreamTile[i] = -1;
                if (oceanMask[i])
                    continue;
                sorted.Add((i, elevation[i]));

                int flowDir = flowDirections[i];
                if (flowDir >= 0)
                {
                    neighbors.Clear();
                    grid.GetTileNeighbors(i, neighbors);
                    if (flowDir < neighbors.Count)
                    {
                        int down = neighbors[flowDir].tileId;
                        if (down >= 0 && down < tileCount && !oceanMask[down])
                            cachedDownstreamTile[i] = down;
                    }
                }
            }

            // Higher ground first. Equal heights drain toward the lower tile id, so the higher id goes first.
            sorted.Sort((a, b) =>
            {
                int byElevation = b.elevation.CompareTo(a.elevation);
                return byElevation != 0 ? byElevation : b.tileID.CompareTo(a.tileID);
            });

            cachedSortedLandTiles = new int[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
                cachedSortedLandTiles[i] = sorted[i].tileID;
        }
    }
}
