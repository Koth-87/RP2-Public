using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimWorld.Planet;
using UnityEngine;
using Planets.Utilities.Logging;
using Planets.WorldGen.Constants;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Phase 2: Band-Weighted Spatial Smoothing.
    /// Computes regional smoothed elevation using band-weighted averaging to preserve sharp transitions between peaks and valleys.
    /// </summary>
    public class ElevationSmoother : TerrainAnalysisBase
    {
        private sealed class ThreadState
        {
            public readonly int[] Queue;
            public readonly int[] Stamp;
            public int CurStamp;

            public ThreadState(int tileCount, int maxQueue)
            {
                Queue = new int[maxQueue];
                Stamp = new int[tileCount];
                CurStamp = 0;
            }
        }

        public override void Execute()
        {
            int smoothingRingRadius = EarthScaleConstants.KmToRings(EarthScaleConstants.TERRAIN_SMOOTHING_RADIUS_KM);
            RPLog.D($"[Terrain Analysis] Computing band-weighted smoothed elevation ({smoothingRingRadius} rings)");

            var nbrStart = new int[tileCount + 1];
            var nb = new List<PlanetTile>(8);
            int total = 0;
            for (int i = 0; i < tileCount; i++)
            {
                nbrStart[i] = total;
                nb.Clear();
                grid.GetTileNeighbors(i, nb);
                total += nb.Count;
            }
            nbrStart[tileCount] = total;
            var nbr = new int[total];
            int cursor = 0;
            for (int i = 0; i < tileCount; i++)
            {
                nb.Clear();
                grid.GetTileNeighbors(i, nb);
                for (int k = 0; k < nb.Count; k++)
                    nbr[cursor++] = nb[k].tileId;
            }

            int maxQueue = Mathf.Min(tileCount, 6 * (smoothingRingRadius + 2) * (smoothingRingRadius + 2));
            bool[] ocean = climate.OceanMask;
            float[] elev = climate.ElevationKm;
            int[] band = climate.ElevationBand;
            float[] smoothed = climate.SmoothedElevation;

            Parallel.ForEach(
                Partitioner.Create(0, tileCount),
                () => new ThreadState(tileCount, maxQueue),
                (range, _, state) =>
                {
                    int[] q = state.Queue;
                    int[] stamp = state.Stamp;
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        if (ocean[i])
                        {
                            smoothed[i] = elev[i];
                            continue;
                        }

                        int s = ++state.CurStamp;
                        q[0] = i;
                        stamp[i] = s;
                        int head = 0;
                        int tail = 1;

                        for (int ring = 1; ring <= smoothingRingRadius; ring++)
                        {
                            int ringEnd = tail;
                            while (head < ringEnd)
                            {
                                int tile = q[head++];
                                int end = nbrStart[tile + 1];
                                for (int k = nbrStart[tile]; k < end; k++)
                                {
                                    int nID = nbr[k];
                                    if (stamp[nID] != s)
                                    {
                                        stamp[nID] = s;
                                        q[tail++] = nID;
                                    }
                                }
                            }
                            if (head == tail)
                                break;
                        }

                        int myBand = band[i];
                        float weightedSum = 0f;
                        float totalWeight = 0f;
                        for (int idx = 0; idx < tail; idx++)
                        {
                            int neighborID = q[idx];
                            if (ocean[neighborID])
                                continue;
                            float weight = GetBandWeight(myBand, band[neighborID]);
                            weightedSum += elev[neighborID] * weight;
                            totalWeight += weight;
                        }

                        smoothed[i] = totalWeight > 0f ? weightedSum / totalWeight : elev[i];
                    }
                    return state;
                },
                _ => { });
        }

        private static float GetBandWeight(int myBand, int neighborBand)
        {
            int bandDiff = Mathf.Abs(myBand - neighborBand);
            return bandDiff switch
            {
                0 => 1.0f,
                1 => 0.5f,
                _ => 0.1f
            };
        }
    }
}
