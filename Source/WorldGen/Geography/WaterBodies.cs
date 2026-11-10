using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.WorldGen.Constants;

namespace Planets.WorldGen.Geography
{
    /// <summary>
    /// Classifies open ocean vs. enclosed water bodies and computes area-based shore moderation and evaporation strength.
    /// True ocean is water connected to the map edge, or the largest connected water body on a full sphere.
    /// </summary>
    public static class WaterBodies
    {
        /// <summary>Area (km²) at which an enclosed water body reaches ~63% (1 - 1/e) of full inland-sea strength.</summary>
        public const float InlandSeaScaleKm2 = 17000f;

        /// <summary>E-fold distance (km) over which an enclosed body's shore moderation fades inland.</summary>
        public const float InlandWaterEfoldKm = 150f;

        /// <summary>Relative weight of enclosed-water shore moderation compared to open-ocean shore moderation.</summary>
        public const float InlandShoreWeight = 0.5f;

        /// <summary>Evaporation rate of a small lake relative to open ocean.</summary>
        public const float LakeEvapFactor = 0.4f;

        /// <summary>Evaporation rate of a sea-sized enclosed body relative to open ocean.</summary>
        public const float InlandSeaEvapFactor = 0.8f;

        public struct EnclosedBody
        {
            public int Tiles;
            public float AreaKm2;
            public float Strength;
        }

        public static float StrengthForArea(float areaKm2)
        {
            if (areaKm2 <= 0f)
                return 0f;
            return 1f - Mathf.Exp(-areaKm2 / InlandSeaScaleKm2);
        }

        public static bool MarkTrueOcean(PlanetLayer layer, WorldGrid grid, bool[] isTrueOcean)
        {
            int count = layer?.Tiles?.Count ?? 0;
            if (grid == null || isTrueOcean == null || count == 0 || isTrueOcean.Length < count)
                return false;

            for (int i = 0; i < count; i++)
                isTrueOcean[i] = false;

            var queue = new Queue<int>();
            var neighbors = new List<PlanetTile>();
            bool seededFromEdge = false;

            for (int i = 0; i < count; i++)
            {
                if (!IsWater(layer, i) || !grid.IsOnEdge(i))
                    continue;
                Flood(layer, grid, i, isTrueOcean, null, queue, neighbors);
                seededFromEdge = true;
            }

            if (seededFromEdge)
                return false;

            var visited = new bool[count];
            int bestSeed = -1;
            int bestSize = 0;
            for (int i = 0; i < count; i++)
            {
                if (!IsWater(layer, i) || visited[i])
                    continue;
                int size = Flood(layer, grid, i, null, visited, queue, neighbors);
                if (size > bestSize)
                {
                    bestSize = size;
                    bestSeed = i;
                }
            }

            if (bestSeed >= 0)
                Flood(layer, grid, bestSeed, isTrueOcean, null, queue, neighbors);
            return true;
        }

        public static void MeasureEnclosed(PlanetLayer layer, WorldGrid grid, bool[] trueOcean, float[] bodyStrength, List<EnclosedBody> bodies, int[] waterBodyId = null)
        {
            bodies?.Clear();
            int count = layer?.Tiles?.Count ?? 0;
            if (grid == null || bodyStrength == null || count == 0 || bodyStrength.Length < count)
                return;

            bool assignIds = waterBodyId != null && waterBodyId.Length >= count;
            for (int i = 0; i < count; i++)
            {
                bodyStrength[i] = 0f;
                if (assignIds)
                    waterBodyId[i] = trueOcean != null && i < trueOcean.Length && trueOcean[i] ? 0 : -1;
            }

            int nextBodyId = 1;
            var visited = new bool[count];
            var members = new List<int>();
            var queue = new Queue<int>();
            var neighbors = new List<PlanetTile>();
            for (int i = 0; i < count; i++)
            {
                if (!IsEnclosed(layer, trueOcean, i) || visited[i])
                    continue;
                members.Clear();
                FloodEnclosed(layer, grid, trueOcean, i, visited, members, queue, neighbors);
                float area = members.Count * EarthScaleConstants.TILE_AREA_KM2;
                float strength = StrengthForArea(area);
                int bodyId = nextBodyId++;
                for (int m = 0; m < members.Count; m++)
                {
                    int tileId = members[m];
                    bodyStrength[tileId] = strength;
                    if (assignIds)
                        waterBodyId[tileId] = bodyId;
                }
                bodies?.Add(new EnclosedBody { Tiles = members.Count, AreaKm2 = area, Strength = strength });
            }
        }

        private static bool IsEnclosed(PlanetLayer layer, bool[] trueOcean, int tileId)
        {
            if (!IsWater(layer, tileId))
                return false;
            return trueOcean == null || tileId >= trueOcean.Length || !trueOcean[tileId];
        }

        private static void FloodEnclosed(PlanetLayer layer, WorldGrid grid, bool[] trueOcean, int seed, bool[] visited, List<int> members, Queue<int> queue, List<PlanetTile> neighbors)
        {
            queue.Clear();
            queue.Enqueue(seed);
            visited[seed] = true;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                members.Add(current);
                neighbors.Clear();
                grid.GetTileNeighbors(current, neighbors);
                for (int n = 0; n < neighbors.Count; n++)
                {
                    int neighbor = neighbors[n].tileId;
                    if (neighbor < 0 || neighbor >= layer.Tiles.Count || visited[neighbor])
                        continue;
                    if (!IsEnclosed(layer, trueOcean, neighbor))
                        continue;
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
        }

        private static bool IsWater(PlanetLayer layer, int tileId)
        {
            Tile tile = layer.Tiles[tileId];
            return tile != null && tile.elevation <= 0f;
        }

        private static int Flood(PlanetLayer layer, WorldGrid grid, int seed, bool[] mark, bool[] visited, Queue<int> queue, List<PlanetTile> neighbors)
        {
            queue.Clear();
            queue.Enqueue(seed);
            if (visited != null)
                visited[seed] = true;
            if (mark != null)
                mark[seed] = true;

            int size = 0;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                size++;
                neighbors.Clear();
                grid.GetTileNeighbors(current, neighbors);
                for (int n = 0; n < neighbors.Count; n++)
                {
                    int neighbor = neighbors[n].tileId;
                    if (neighbor < 0 || neighbor >= layer.Tiles.Count)
                        continue;
                    if (!IsWater(layer, neighbor))
                        continue;
                    if (visited != null)
                    {
                        if (visited[neighbor])
                            continue;
                        visited[neighbor] = true;
                    }
                    else if (mark[neighbor])
                    {
                        continue;
                    }

                    if (mark != null)
                        mark[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            return size;
        }
    }
}
