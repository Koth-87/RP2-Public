using System;
using System.Collections.Generic;
using UnityEngine;
using Planets.WorldGen.Climate.Mesh;
using Planets.WorldGen.Solvers;

namespace Planets.WorldGen.Climate.Moisture
{
    /// <summary>
    /// Upper-troposphere moisture layer on the 40,962-cell <see cref="CoarseMesh"/> (WS31).
    /// Receives unprecipitated moisture lifted from the lower layer, advects it on the upper
    /// circulation wind with eddy mixing, rains where the upper column nears its colder saturation,
    /// and returns remaining moisture to the lower layer where air subsides.
    /// </summary>
    public sealed class UpperMoistureLayer
    {
        private const float Gravity = 9.80665f;
        private const float LayerPa = 35000f; // 850 hPa to 500 hPa
        private const float LevelPa = 70000f; // 700 hPa
        private const float HeightKm = 3.0f;
        private const float LapsePerKm = 6.5f;
        private const float EddyDiffusivity = 3.0e5f;

        private readonly CoarseMesh mesh;
        private readonly int cellCount;
        private readonly int tileCount;
        private readonly int[] tileCell;
        private readonly int[] cellCoverTile;
        private readonly float tileAreaM2;
        private readonly float[] toTile;
        private readonly float[] spacingM;

        // Symmetric eddy diffusion on the coarse mesh
        private int[] diffRowStart;
        private int[] diffCol;
        private float[] diffVal;
        private float[] diffDiag;

        // Per-snapshot transport & capacity on the coarse mesh (0 = Jul/June, 1 = Jan/Dec, 2 = Eq/Apr/Oct)
        private readonly TransportSystem[] systems = new TransportSystem[3];
        private readonly float[][] transportOut = new float[3][];
        private readonly float[][] capacity = new float[3][];
        private readonly float[][] column = new float[4][];

        public UpperMoistureLayer(ClimateData climate, float tileArea)
        {
            mesh = CoarseMesh.Get();
            cellCount = mesh.Count;
            tileCount = climate.Latitude.Length;
            tileCell = climate.TileCell;
            cellCoverTile = climate.CellCoverTile;
            tileAreaM2 = tileArea;

            var tileSum = new double[cellCount];
            for (int t = 0; t < tileCount; t++)
                tileSum[tileCell[t]] += tileAreaM2;
            if (cellCoverTile != null)
            {
                for (int c = 0; c < cellCount; c++)
                {
                    if (tileSum[c] == 0.0 && cellCoverTile[c] >= 0)
                        tileSum[c] = mesh.AreaM2[c];
                }
            }

            toTile = new float[cellCount];
            for (int c = 0; c < cellCount; c++)
                toTile[c] = (float)(mesh.AreaM2[c] / Math.Max(tileSum[c], 1.0));

            spacingM = ComputeCellSpacing();
            BuildDiffusion();
            BuildCapacities(climate);
            BuildTransport(0, climate.CellTransportJuneEast, climate.CellTransportJuneNorth);
            BuildTransport(1, climate.CellTransportDecEast, climate.CellTransportDecNorth);
            BuildTransport(2, climate.CellTransportEqEast, climate.CellTransportEqNorth);

            for (int s = 0; s < 4; s++)
                column[s] = new float[cellCount];
        }

        public float[] GetCellColumn(int snapshotIndex) => column[snapshotIndex];

        private float[] ComputeCellSpacing()
        {
            var spacing = new float[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                int[] nbr = mesh.Neighbors[i];
                if (nbr.Length == 0)
                {
                    spacing[i] = 110000f;
                    continue;
                }
                double sum = 0.0;
                Vector3 pi = mesh.Position[i];
                for (int k = 0; k < nbr.Length; k++)
                {
                    float dot = Mathf.Clamp(Vector3.Dot(pi, mesh.Position[nbr[k]]), -1f, 1f);
                    sum += Math.Acos(dot) * CoarseMesh.EarthRadiusM;
                }
                spacing[i] = (float)(sum / nbr.Length);
            }
            return spacing;
        }

        private void BuildDiffusion()
        {
            var rows = new List<int>[cellCount];
            var vals = new List<float>[cellCount];
            diffDiag = new float[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                rows[i] = new List<int>(6);
                vals[i] = new List<float>(6);
            }

            for (int i = 0; i < cellCount; i++)
            {
                int[] nbr = mesh.Neighbors[i];
                float[] len = mesh.EdgeLengthM[i];
                for (int k = 0; k < nbr.Length; k++)
                {
                    int j = nbr[k];
                    if (i >= j)
                        continue;
                    float dist = Mathf.Max(0.5f * (spacingM[i] + spacingM[j]), 1f);
                    float cond = EddyDiffusivity * len[k] / dist;
                    rows[i].Add(j);
                    vals[i].Add(cond);
                    rows[j].Add(i);
                    vals[j].Add(cond);
                    diffDiag[i] += cond;
                    diffDiag[j] += cond;
                }
            }

            diffRowStart = new int[cellCount + 1];
            int total = 0;
            for (int i = 0; i < cellCount; i++)
            {
                diffRowStart[i] = total;
                total += rows[i].Count;
            }
            diffRowStart[cellCount] = total;
            diffCol = new int[total];
            diffVal = new float[total];
            int pos = 0;
            for (int i = 0; i < cellCount; i++)
            {
                List<int> r = rows[i];
                List<float> v = vals[i];
                for (int k = 0; k < r.Count; k++)
                {
                    diffCol[pos] = r[k];
                    diffVal[pos] = v[k];
                    pos++;
                }
            }
        }

        private void BuildCapacities(ClimateData climate)
        {
            float[] annual = climate.CellAnnual;
            float[] amp = climate.CellAmp;
            float[] signs = { 1f, -1f, 0f };
            for (int s = 0; s < 3; s++)
            {
                var cap = new float[cellCount];
                float sign = signs[s];
                for (int c = 0; c < cellCount; c++)
                {
                    float hemi = mesh.Position[c].y >= 0f ? 1f : -1f;
                    float surface = annual[c] + sign * hemi * 0.5f * amp[c];
                    float air = surface - LapsePerKm * HeightKm;
                    float qSat = MoistureMath.SpecificHumidity(air, LevelPa);
                    float held = qSat * (LayerPa / Gravity);
                    cap[c] = Mathf.Max(held, 1f);
                }
                capacity[s] = cap;
            }
        }

        private void BuildTransport(int windSlot, float[] east, float[] north)
        {
            var rowCols = new List<int>[cellCount];
            var rowWeights = new List<float>[cellCount];
            var fOut = new float[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                rowCols[i] = new List<int>(6);
                rowWeights[i] = new List<float>(6);
                int kStart = diffRowStart[i];
                int kEnd = diffRowStart[i + 1];
                for (int k = kStart; k < kEnd; k++)
                {
                    rowCols[i].Add(diffCol[k]);
                    rowWeights[i].Add(diffVal[k]);
                }
            }

            for (int src = 0; src < cellCount; src++)
            {
                mesh.Frame(mesh.Position[src], out Vector3 eVec, out Vector3 nVec);
                int[] nbr = mesh.Neighbors[src];
                float[] len = mesh.EdgeLengthM[src];
                for (int k = 0; k < nbr.Length; k++)
                {
                    int dst = nbr[k];
                    Vector3 step = mesh.Position[dst] - mesh.Position[src];
                    float ae = Vector3.Dot(step, eVec);
                    float an = Vector3.Dot(step, nVec);
                    float horiz = Mathf.Sqrt(ae * ae + an * an);
                    if (horiz < 1e-8f)
                        continue;
                    float dirE = ae / horiz;
                    float dirN = an / horiz;
                    float un = 0.5f * ((east[src] + east[dst]) * dirE + (north[src] + north[dst]) * dirN);
                    if (un <= 1e-6f)
                        continue;
                    float flow = un * len[k];
                    fOut[src] += flow;
                    // Add flow to receiver row (dst receives from donor src)
                    List<int> cols = rowCols[dst];
                    List<float> weights = rowWeights[dst];
                    int found = cols.IndexOf(src);
                    if (found >= 0)
                        weights[found] += flow;
                    else
                    {
                        cols.Add(src);
                        weights.Add(flow);
                    }
                }
            }

            var rowStart = new int[cellCount + 1];
            int nnz = 0;
            for (int i = 0; i < cellCount; i++)
            {
                rowStart[i] = nnz;
                nnz += rowCols[i].Count;
            }
            rowStart[cellCount] = nnz;
            var colArr = new int[nnz];
            var valArr = new float[nnz];
            int idx = 0;
            for (int i = 0; i < cellCount; i++)
            {
                List<int> cList = rowCols[i];
                List<float> wList = rowWeights[i];
                for (int k = 0; k < cList.Count; k++)
                {
                    colArr[idx] = cList[k];
                    valArr[idx] = wList[k];
                    idx++;
                }
            }

            var order = new int[cellCount];
            for (int i = 0; i < cellCount; i++)
                order[i] = i;

            transportOut[windSlot] = fOut;
            systems[windSlot] = new TransportSystem
            {
                Count = cellCount,
                RowStart = rowStart,
                Column = colArr,
                Weight = valArr,
                Diagonal = new float[cellCount],
                Rhs = new float[cellCount],
                SweepOrder = order
            };
        }

        public void Exchange(
            int snapshotIndex,
            int windSlot,
            float[] divergence,
            float[] lifted,
            float tauDays,
            float threshold,
            float[] moistShare,
            float[] rainTileOut,
            float[] downTileOut)
        {
            float[] prev = column[snapshotIndex];
            var incoming = new double[cellCount];
            var weightSum = new double[cellCount];
            var coveredSum = new double[cellCount];
            var downSum = new double[cellCount];

            for (int t = 0; t < tileCount; t++)
            {
                int c = tileCell[t];
                double liftMass = lifted[t] * (double)tileAreaM2;
                incoming[c] += liftMass;
                if (moistShare != null)
                {
                    weightSum[c] += moistShare[t] * (double)tileAreaM2;
                    coveredSum[c] += tileAreaM2;
                }
                if (divergence[t] > 0f)
                    downSum[c] += divergence[t] * (double)tileAreaM2;
            }
            if (cellCoverTile != null)
            {
                for (int c = 0; c < cellCount; c++)
                {
                    int ct = cellCoverTile[c];
                    if (ct < 0)
                        continue;
                    double cellArea = mesh.AreaM2[c];
                    // Spread the covering tile's lifted moisture into uncovered coarse cells on smaller planet scales
                    incoming[c] = lifted[ct] * cellArea;
                    if (moistShare != null)
                    {
                        weightSum[c] = moistShare[ct] * cellArea;
                        coveredSum[c] = cellArea;
                    }
                    downSum[c] = divergence[ct] > 0f ? divergence[ct] * cellArea : 0.0;
                }
            }

            var source = new double[cellCount];
            var downRate = new float[cellCount];
            for (int c = 0; c < cellCount; c++)
            {
                if (moistShare != null && coveredSum[c] > 0.0)
                {
                    double here = weightSum[c] / coveredSum[c];
                    source[c] = incoming[c] * here;
                    incoming[c] -= source[c];
                }
                downRate[c] = (float)(downSum[c] / Math.Max(mesh.AreaM2[c], 1.0));
            }

            TransportSystem sys = systems[windSlot];
            float[] fOut = transportOut[windSlot];
            float[] cap = capacity[windSlot];
            float tau = tauDays * 86400f;
            var col = new float[cellCount];
            Array.Copy(prev, col, cellCount);
            var raining = new bool[cellCount];

            for (int step = 0; step < 6; step++)
            {
                for (int c = 0; c < cellCount; c++)
                {
                    float humid = raining[c] ? 1f / tau : 0f;
                    float area = mesh.AreaM2[c];
                    float d = fOut[c] + diffDiag[c] + area * (humid + downRate[c]);
                    float r = (float)incoming[c] + area * humid * threshold * cap[c];
                    if (d < 1f)
                    {
                        d = 1f;
                        r = col[c];
                    }
                    sys.Diagonal[c] = d;
                    sys.Rhs[c] = r;
                }

                TransportSolution sol = SteadyTransportSolver.Solve(sys, 1e-5f, 80);
                bool anyChanged = false;
                for (int c = 0; c < cellCount; c++)
                {
                    float val = Mathf.Max(sol.X[c], 0f);
                    col[c] = val;
                    bool nr = val >= threshold * cap[c];
                    if (nr != raining[c])
                    {
                        raining[c] = nr;
                        anyChanged = true;
                    }
                }
                if (!anyChanged)
                    break;
            }

            Array.Copy(col, prev, cellCount);
            var cellRainRate = new float[cellCount];
            for (int c = 0; c < cellCount; c++)
            {
                float rCell = raining[c] ? Mathf.Max((col[c] - threshold * cap[c]) / tau, 0f) : 0f;
                float sRate = (float)(source[c] / Math.Max(mesh.AreaM2[c], 1.0));
                cellRainRate[c] = (rCell + sRate) * toTile[c];
            }

            if (tileCount < cellCount && cellCoverTile != null)
            {
                // On smaller planet scales (subdivisions 5–8), multiple coarse cells cover one tile: average over all cells covering each tile
                var rainSum = new double[tileCount];
                var colSum = new double[tileCount];
                var areaSum = new double[tileCount];
                for (int t = 0; t < tileCount; t++)
                {
                    int c = tileCell[t];
                    double a = mesh.AreaM2[c];
                    rainSum[t] += cellRainRate[c] * a;
                    colSum[t] += col[c] * a;
                    areaSum[t] += a;
                }
                for (int c = 0; c < cellCount; c++)
                {
                    int ct = cellCoverTile[c];
                    if (ct < 0)
                        continue;
                    double a = mesh.AreaM2[c];
                    rainSum[ct] += cellRainRate[c] * a;
                    colSum[ct] += col[c] * a;
                    areaSum[ct] += a;
                }
                for (int t = 0; t < tileCount; t++)
                {
                    double invA = 1.0 / Math.Max(areaSum[t], 1.0);
                    rainTileOut[t] = (float)(rainSum[t] * invA);
                    downTileOut[t] = Mathf.Max(divergence[t], 0f) * (float)(colSum[t] * invA);
                }
            }
            else
            {
                for (int t = 0; t < tileCount; t++)
                {
                    int c = tileCell[t];
                    rainTileOut[t] = cellRainRate[c];
                    downTileOut[t] = Mathf.Max(divergence[t], 0f) * col[c];
                }
            }
        }
    }
}
