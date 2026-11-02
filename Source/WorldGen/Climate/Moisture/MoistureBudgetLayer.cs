using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Climate.Circulation;
using Planets.WorldGen.Climate.Mesh;
using Planets.WorldGen.Constants;
using Planets.WorldGen.Solvers;

namespace Planets.WorldGen.Climate.Moisture
{
    /// <summary>
    /// Two-layer steady physical moisture budget and precipitation solver (WS30 + WS31).
    /// Solves column water vapour W (mm) and precipitation across June (northern summer),
    /// December (northern winter), and Equinox snapshots with:
    /// - 0.44 surface + 0.56 upper transport carrier wind,
    /// - crest-blocked mass-continuity return flow (--div-keep 0.35),
    /// - rising-air divergence capped at D95 = 2.5e-6 s⁻¹,
    /// - directional surface-wind slope threshold shift (u_surf · ∇z),
    /// - westerly storm-track eddy diffusivity K(φ), humid-air saturation ramp, and storm-belt ocean evaporation,
    /// - upper-troposphere moisture exchange on the 40,962-cell <see cref="CoarseMesh"/> (<see cref="UpperMoistureLayer"/>),
    /// - Fu-Budyko land evapotranspiration recycling with FAO-56 solar-declination PET (<see cref="PotentialEvaporation"/>).
    /// </summary>
    public class MoistureBudgetLayer : IClimateLayer
    {
        private readonly ClimateData climate;
        private readonly ClimateConfiguration config;
        private readonly int tileCount;
        private float tileAreaM2;
        private float meanEdgeLengthM;

        // Directed tile graph edges (all directed pairs src -> dst)
        private int[] edgeRowStart;
        private int[] edgeSrc;
        private int[] edgeDst;
        private float[] edgeDistM;
        private float[] edgeESrc;
        private float[] edgeNSrc;
        private float[] edgeEDst;
        private float[] edgeNDst;

        // Undirected tile edges (keep: src < dst) for symmetric diffusion and Poisson return flow
        private int[] undirSrc;
        private int[] undirDst;
        private float[] undirDistM;
        private float[] undirLengthM;
        private float[] undirESrc;
        private float[] undirNSrc;
        private float[] undirEDst;
        private float[] undirNDst;

        // Least-squares surface elevation gradient (m/m)
        private float[] slopeEast;
        private float[] slopeNorth;

        // Barycentric interpolation from CoarseMesh cells to tiles, and Poisson return-flow operators
        private CoarseMesh mesh;
        private int[] tileCell;
        private int[] baryA;
        private int[] baryB;
        private int[] baryC;
        private float[] baryWA;
        private float[] baryWB;
        private float[] baryWC;

        private int[] poissonRowStart;
        private int[] poissonCol;
        private float[] poissonVal;
        private float[] poissonDiag;
        private float[] poissonInvDiag;
        private float[] poissonEdgeCond;
        private TransportSystem finePoissonSys;
        private TransportSystem coarsePoissonSys;

        private const int SuperBins = 648; // 18 x 36 (10-degree lat/lon bins)
        private int[] cellBin;
        private TransportSystem superPoissonSys;

        public string LayerName => "MoistureBudget";

        private sealed class SnapshotPrep
        {
            public string Name;
            public int Index;      // 0 = June, 1 = December, 2 = Equinox
            public float Weight;   // 0.25, 0.25, 0.50
            public float[] Temp;
            public float[] SurfaceE;
            public float[] SurfaceN;
            public float[] WindE;
            public float[] WindN;
            public float[] FOut;
            public float[] Divergence;
            public float D95;
            public float[] Climb;
            public float[] Threshold;
            public float[] Capacity;
            public bool[] Evaporates;
            public float[] KLinear;
            public float[] ELinear;
            public float[] ECapped;
            public float[] CRise;
            public float[] KTile;
            public float[] RampLo;
            public float[] RampSpan;
            public float[] RampRate;
            public float[] RampShift;
            public float[] DiffDiag;
            public float[] EdgeInflowSource;

            // Combined CSR inflow + diffusion matrix on tiles: row i has incoming entries from col j
            public int[] RowStart;
            public int[] Col;
            public float[] WeightVal;
            public int[] UpwindOrder;

            // Coarse-mesh restriction of (f_out + diff_diag - inflow - diff) for the two-grid solver
            public float[] CoarseBaseDiag;
            public TransportSystem CoarseSystem;
            public float[] SuperBaseDiag;
            public TransportSystem SuperSystem;
        }

        public MoistureBudgetLayer(ClimateData climateData, ClimateConfiguration configuration)
        {
            climate = climateData ?? throw new ArgumentNullException(nameof(climateData));
            config = configuration ?? new ClimateConfiguration();
            tileCount = climate.Latitude.Length;
            tileAreaM2 = (float)(4.0 * Math.PI * (double)MoistureMath.RadiusM * MoistureMath.RadiusM / tileCount);
            meanEdgeLengthM = Mathf.Sqrt(tileAreaM2 * 2f / Mathf.Sqrt(3f)) / Mathf.Sqrt(3f);
        }

        public void Execute(ClimateData climateData)
        {
            var sw = Stopwatch.StartNew();
            BuildGraphAndSlopes();
            BuildCoarseMappingAndPoisson();

            float tiltDeg = AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt);
            bool useContStress = config.petContinentalStress.CurrentValue;
            bool usePolar = config.petPolarFactor.CurrentValue;

            // Initial PET with humid aridity = 1.0 everywhere (WS30 step 4b round 0)
            var aridity = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
                aridity[i] = 1f;

            var petJul = new float[tileCount];
            var petJan = new float[tileCount];
            var petApr = new float[tileCount];
            var petOct = new float[tileCount];
            var petEq = new float[tileCount];

            PotentialEvaporation.ComputeFourSnapshots(
                climate, aridity, tiltDeg, useContStress, usePolar,
                climate.PotentialEvapotranspiration, climate.DailyRangeC,
                petJul, petJan, petApr, petOct);
            for (int i = 0; i < tileCount; i++)
                petEq[i] = 0.5f * (petApr[i] + petOct[i]);

            // Overall rainfall slider scaling on ocean evaporation C_E (WS30 step 4 / step 13; Normal = 1000 mm/year)
            float baseCe = config.oceanEvapCoeff.CurrentValue;
            float rainScale = climate.BaseRain > 0f ? climate.BaseRain / 1000f : 1f;
            float ce = baseCe * Mathf.Clamp(rainScale, 0.20f, 2.5f);

            // Snapshot temperatures
            var tJune = new float[tileCount];
            var tDec = new float[tileCount];
            var tEq = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                float ann = climate.AnnualMeanTemp[i];
                float half = 0.5f * climate.SeasonalAmp[i];
                float sign = climate.LatitudeSigned[i] >= 0f ? 1f : -1f;
                tJune[i] = ann + sign * half;
                tDec[i] = ann - sign * half;
                tEq[i] = ann;
            }

            // Prepare the 3 snapshots in parallel (June weight 0.25, December weight 0.25, Equinox weight 0.50)
            float coverage = Find.World?.info?.planetCoverage ?? 1f;
            string[] snapNames = { "June", "Dec", "Eq" };
            float[] snapWeights = { 0.25f, 0.25f, 0.50f };
            float[][] snapTemps = { tJune, tDec, tEq };
            Vector3[][] snapSurfWinds = { climate.SummerWind, climate.WinterWind, climate.EquinoxWind };
            Vector3[][] snapTransWinds = { climate.TransportJune, climate.TransportDecember, climate.TransportEquinox };
            var snapshots = new SnapshotPrep[3];
            Parallel.For(0, 3, s =>
            {
                snapshots[s] = PrepareSnapshot(snapNames[s], s, snapWeights[s], snapTemps[s], snapSurfWinds[s], snapTransWinds[s], ce, coverage);
            });

            bool twoLayer = config.twoLayerMoisture.CurrentValue;
            UpperMoistureLayer upperLayer = twoLayer ? new UpperMoistureLayer(climate, tileAreaM2) : null;

            int rounds = 4;
            var etMm = new float[tileCount];
            var rainMm = new float[tileCount];
            var guesses = new float[3][];
            var downFromUpper = new float[3][];
            var snapRainMm = new float[3][];
            var snapHumidMm = new float[3][];
            var snapRiseMm = new float[3][];
            var snapUpperMm = new float[3][];
            var snapOceanEvapMm = new float[3][];
            var snapW = new float[3][];

            for (int s = 0; s < 3; s++)
            {
                guesses[s] = new float[tileCount];
                downFromUpper[s] = new float[tileCount];
                snapRainMm[s] = new float[tileCount];
                snapHumidMm[s] = new float[tileCount];
                snapRiseMm[s] = new float[tileCount];
                snapUpperMm[s] = new float[tileCount];
                snapOceanEvapMm[s] = new float[tileCount];
                snapW[s] = new float[tileCount];
                for (int i = 0; i < tileCount; i++)
                    guesses[s][i] = climate.OceanMask[i] ? 0.5f * snapshots[s].Threshold[i] * snapshots[s].Capacity[i] : 0f;
            }

            float[][] petBySnap = { petJul, petJan, petEq };
            float budykoW = config.budykoW.CurrentValue;
            float tauDays = config.EffectiveRainTimescaleDays;
            float rainThresh = config.rainThreshold.CurrentValue;

            for (int round = 0; round < rounds; round++)
            {
                // Split annual land ET across snapshots by each snapshot's PET demand (--et-pet)
                var landEvapRate = SplitLandEvap(etMm, petBySnap);

                Parallel.For(0, 3, s =>
                {
                    SnapshotPrep prep = snapshots[s];
                    SolveSnapshot(
                        prep,
                        landEvapRate[s],
                        guesses[s],
                        downFromUpper[s],
                        upperLayer != null,
                        out float[] col,
                        out float[] humidRate,
                        out float[] riseRate,
                        out float[] liftedRate,
                        out float[] oceanEvapRate,
                        out float[] moistShare);

                    guesses[s] = col;
                    snapW[s] = col;

                    var upperRainRate = new float[tileCount];
                    if (upperLayer != null)
                    {
                        upperLayer.Exchange(
                            s,
                            s,
                            prep.Divergence,
                            liftedRate,
                            tauDays,
                            rainThresh,
                            moistShare,
                            upperRainRate,
                            downFromUpper[s]);
                    }

                    float wYear = prep.Weight * MoistureMath.YearSeconds;
                    for (int i = 0; i < tileCount; i++)
                    {
                        float totRate = humidRate[i] + riseRate[i] + upperRainRate[i];
                        snapRainMm[s][i] = totRate * MoistureMath.YearSeconds;
                        snapHumidMm[s][i] = humidRate[i] * wYear;
                        snapRiseMm[s][i] = riseRate[i] * wYear;
                        snapUpperMm[s][i] = upperRainRate[i] * wYear;
                        snapOceanEvapMm[s][i] = oceanEvapRate[i] * wYear;
                    }
                });

                for (int i = 0; i < tileCount; i++)
                {
                    rainMm[i] = 0.25f * snapRainMm[0][i] + 0.25f * snapRainMm[1][i] + 0.50f * snapRainMm[2][i];
                }

                // Update PET from this round's aridity and update Budyko land ET
                for (int i = 0; i < tileCount; i++)
                    aridity[i] = rainMm[i] / Mathf.Max(climate.PotentialEvapotranspiration[i], 1f);

                PotentialEvaporation.ComputeFourSnapshots(
                    climate, aridity, tiltDeg, useContStress, usePolar,
                    climate.PotentialEvapotranspiration, climate.DailyRangeC,
                    petJul, petJan, petApr, petOct);
                for (int i = 0; i < tileCount; i++)
                    petEq[i] = 0.5f * (petApr[i] + petOct[i]);

                var nextEt = new float[tileCount];
                MoistureMath.BudykoMm(rainMm, climate.PotentialEvapotranspiration, climate.OceanMask, budykoW, nextEt);
                etMm = nextEt;
            }

            WriteOutputs(snapshots, rainMm, etMm, snapRainMm, snapHumidMm, snapRiseMm, snapUpperMm, snapOceanEvapMm, snapW, upperLayer);
            sw.Stop();
            if (Controller.DebugEnabled)
                RPLog.D($"[MoistureBudget] Solved 4-round two-layer moisture budget in {sw.ElapsedMilliseconds} ms.");
        }

        private void BuildGraphAndSlopes()
        {
            WorldGrid grid = Find.WorldGrid;
            var nb = new List<PlanetTile>(8);
            var counts = new int[tileCount];
            int totalEdges = 0;
            for (int i = 0; i < tileCount; i++)
            {
                nb.Clear();
                grid.GetTileNeighbors(i, nb);
                counts[i] = nb.Count;
                totalEdges += nb.Count;
            }

            edgeRowStart = new int[tileCount + 1];
            edgeSrc = new int[totalEdges];
            edgeDst = new int[totalEdges];
            var eastVec = new Vector3[tileCount];
            var northVec = new Vector3[tileCount];
            Vector3 up = new Vector3(0f, 1f, 0f);

            for (int i = 0; i < tileCount; i++)
            {
                Vector3 p = climate.TileCenters[i].normalized;
                Vector3 e = Vector3.Cross(p, up);
                if (e.sqrMagnitude < 1e-12f)
                    e = new Vector3(1f, 0f, 0f);
                else
                    e.Normalize();
                Vector3 n = Vector3.Cross(e, p).normalized;
                eastVec[i] = e;
                northVec[i] = n;
            }

            int pos = 0;
            int undirCount = 0;
            for (int i = 0; i < tileCount; i++)
            {
                edgeRowStart[i] = pos;
                nb.Clear();
                grid.GetTileNeighbors(i, nb);
                for (int k = 0; k < nb.Count; k++)
                {
                    int j = nb[k].tileId;
                    edgeSrc[pos] = i;
                    edgeDst[pos] = j;
                    pos++;
                    if (i < j)
                        undirCount++;
                }
            }
            edgeRowStart[tileCount] = pos;

            edgeDistM = new float[totalEdges];
            edgeESrc = new float[totalEdges];
            edgeNSrc = new float[totalEdges];
            edgeEDst = new float[totalEdges];
            edgeNDst = new float[totalEdges];

            undirSrc = new int[undirCount];
            undirDst = new int[undirCount];
            undirDistM = new float[undirCount];
            undirLengthM = new float[undirCount];
            undirESrc = new float[undirCount];
            undirNSrc = new float[undirCount];
            undirEDst = new float[undirCount];
            undirNDst = new float[undirCount];

            float invSqrt3 = 1f / Mathf.Sqrt(3f);
            int uPos = 0;
            double distSum = 0.0;
            for (int e = 0; e < totalEdges; e++)
            {
                int s = edgeSrc[e];
                int d = edgeDst[e];
                Vector3 ps = climate.TileCenters[s];
                Vector3 pd = climate.TileCenters[d];
                Vector3 step = pd - ps;
                float dist = step.magnitude;
                Vector3 dir = dist > 1e-12f ? step / dist : Vector3.zero;
                float dot = Mathf.Clamp(Vector3.Dot(ps, pd), -1f, 1f);
                float arc = Mathf.Acos(dot) * MoistureMath.RadiusM;
                float dM = arc > 1f ? arc : dist * MoistureMath.RadiusM;

                float es = Vector3.Dot(dir, eastVec[s]);
                float ns = Vector3.Dot(dir, northVec[s]);
                float ed = Vector3.Dot(dir, eastVec[d]);
                float nd = Vector3.Dot(dir, northVec[d]);

                edgeDistM[e] = dM;
                edgeESrc[e] = es;
                edgeNSrc[e] = ns;
                edgeEDst[e] = ed;
                edgeNDst[e] = nd;

                if (s < d)
                {
                    float cl = Mathf.Max(dM, 1f);
                    undirSrc[uPos] = s;
                    undirDst[uPos] = d;
                    undirDistM[uPos] = cl;
                    undirLengthM[uPos] = cl * invSqrt3;
                    undirESrc[uPos] = es;
                    undirNSrc[uPos] = ns;
                    undirEDst[uPos] = ed;
                    undirNDst[uPos] = nd;
                    distSum += cl;
                    uPos++;
                }
            }

            // Compute actual spherical hexagon tile area and edge length from measured neighbor distances
            // so tileAreaM2 is exact across all planet scales (subdivisions 5–11) and all coverages (5%–100%).
            if (undirCount > 0)
            {
                float meanDistM = (float)(distSum / undirCount);
                tileAreaM2 = 0.5f * Mathf.Sqrt(3f) * meanDistM * meanDistM;
                meanEdgeLengthM = meanDistM * invSqrt3;
            }

            // Least-squares surface elevation slope (m/m), normalized by TerrainMetricsScaleFactor
            // so a 1-tile mountain range on smaller subdivisions (5–9) has the same physical slope as at subdivision 10.
            float slopeScale = 1f / Mathf.Max(EarthScaleConstants.TerrainMetricsScaleFactor, 1f);
            slopeEast = new float[tileCount];
            slopeNorth = new float[tileCount];
            var see = new double[tileCount];
            var snn = new double[tileCount];
            var sen = new double[tileCount];
            var sez = new double[tileCount];
            var snz = new double[tileCount];

            for (int e = 0; e < totalEdges; e++)
            {
                int s = edgeSrc[e];
                int d = edgeDst[e];
                double effDist = edgeDistM[e] * (double)slopeScale;
                double de = edgeESrc[e] * effDist;
                double dn = edgeNSrc[e] * effDist;
                double zs = climate.OceanMask[s] ? 0.0 : Math.Max(climate.ElevationKm[s] * 1000.0, 0.0);
                double zd = climate.OceanMask[d] ? 0.0 : Math.Max(climate.ElevationKm[d] * 1000.0, 0.0);
                double dz = zd - zs;
                see[s] += de * de;
                snn[s] += dn * dn;
                sen[s] += de * dn;
                sez[s] += de * dz;
                snz[s] += dn * dz;
            }

            for (int i = 0; i < tileCount; i++)
            {
                double det = see[i] * snn[i] - sen[i] * sen[i];
                if (Math.Abs(det) > 1.0)
                {
                    slopeEast[i] = (float)((snn[i] * sez[i] - sen[i] * snz[i]) / det);
                    slopeNorth[i] = (float)((see[i] * snz[i] - sen[i] * sez[i]) / det);
                }
            }
        }

        private void BuildCoarseMappingAndPoisson()
        {
            mesh = CoarseMesh.Get();
            tileCell = climate.TileCell;
            int cells = mesh.Count;

            baryA = new int[tileCount];
            baryB = new int[tileCount];
            baryC = new int[tileCount];
            baryWA = new float[tileCount];
            baryWB = new float[tileCount];
            baryWC = new float[tileCount];

            for (int t = 0; t < tileCount; t++)
            {
                CirculationLayer.FindBarycentric(
                    mesh, climate.TileCenters[t].normalized, tileCell[t],
                    out baryA[t], out baryB[t], out baryC[t],
                    out baryWA[t], out baryWB[t], out baryWC[t]);
            }

            // Build crest-blocked Poisson return-flow matrix on tiles and its coarse restriction
            int uCount = undirSrc.Length;
            poissonEdgeCond = new float[uCount];
            poissonDiag = new float[tileCount];
            var rowLists = new List<int>[tileCount];
            var valLists = new List<float>[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                rowLists[i] = new List<int>(6);
                valLists[i] = new List<float>(6);
            }

            var cRows = new List<int>[cells];
            var cVals = new List<float>[cells];
            var cDiag = new float[cells];
            for (int c = 0; c < cells; c++)
            {
                cRows[c] = new List<int>(6);
                cVals[c] = new List<float>(6);
                cDiag[c] = 1e-4f;
            }

            for (int e = 0; e < uCount; e++)
            {
                int s = undirSrc[e];
                int d = undirDst[e];
                float zs = climate.OceanMask[s] ? 0f : Mathf.Max(climate.ElevationKm[s] * 1000f, 0f);
                float zd = climate.OceanMask[d] ? 0f : Mathf.Max(climate.ElevationKm[d] * 1000f, 0f);
                float crest = Mathf.Max(zs, zd);
                float terrainPass = Mathf.Exp(-(crest / 1800f) * (crest / 1800f));
                float cond = (undirLengthM[e] / undirDistM[e]) * terrainPass;
                poissonEdgeCond[e] = cond;

                rowLists[s].Add(d);
                valLists[s].Add(cond);
                rowLists[d].Add(s);
                valLists[d].Add(cond);
                poissonDiag[s] += cond;
                poissonDiag[d] += cond;

                int cs = tileCell[s];
                int cd = tileCell[d];
                if (cs != cd && cond > 0f)
                {
                    // Scale by 1 / 4.65 to match the barycentric prolongation stiffness (P_const^T * lap * P_bary)
                    float cCond = cond / 4.65f;
                    cDiag[cs] += cCond;
                    cDiag[cd] += cCond;
                    AddCoarseEntry(cRows[cs], cVals[cs], cd, cCond);
                    AddCoarseEntry(cRows[cd], cVals[cd], cs, cCond);
                }
            }

            poissonInvDiag = new float[tileCount];
            poissonRowStart = new int[tileCount + 1];
            int nnz = 0;
            for (int i = 0; i < tileCount; i++)
            {
                poissonRowStart[i] = nnz;
                nnz += rowLists[i].Count;
                poissonInvDiag[i] = 1f / Mathf.Max(poissonDiag[i] + 1e-6f, 1e-6f);
            }
            poissonRowStart[tileCount] = nnz;
            poissonCol = new int[nnz];
            poissonVal = new float[nnz];
            int p = 0;
            for (int i = 0; i < tileCount; i++)
            {
                List<int> rl = rowLists[i];
                List<float> vl = valLists[i];
                for (int k = 0; k < rl.Count; k++)
                {
                    poissonCol[p] = rl[k];
                    poissonVal[p] = vl[k];
                    p++;
                }
            }

            coarsePoissonSys = BuildCoarseTransportSystem(cells, cRows, cVals, cDiag);

            // Build 648-bin (18 x 36, 10-degree lat/lon) super-coarse restriction of coarsePoissonSys
            cellBin = new int[cells];
            for (int c = 0; c < cells; c++)
            {
                Vector3 pos = mesh.Position[c];
                float lat = Mathf.Asin(Mathf.Clamp(pos.y, -1f, 1f)) * Mathf.Rad2Deg;
                float lon = Mathf.Atan2(pos.x, pos.z) * Mathf.Rad2Deg;
                int lb = Mathf.Clamp((int)((lat + 90f) / 10f), 0, 17);
                int ob = Mathf.Clamp((int)((lon + 180f) / 10f), 0, 35);
                cellBin[c] = lb * 36 + ob;
            }
            superPoissonSys = RestrictToSuperBins(coarsePoissonSys, cDiag, out _);

            var fineOrder = new int[tileCount];
            var fineDiag = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                fineOrder[i] = i;
                fineDiag[i] = Mathf.Max(poissonDiag[i] + 1e-6f, 1e-6f);
            }
            finePoissonSys = new TransportSystem
            {
                Count = tileCount,
                RowStart = poissonRowStart,
                Column = poissonCol,
                Weight = poissonVal,
                Diagonal = fineDiag,
                Rhs = new float[tileCount],
                SweepOrder = fineOrder
            };
        }

        private static void AddCoarseEntry(List<int> cols, List<float> vals, int targetCol, float weight)
        {
            int idx = cols.IndexOf(targetCol);
            if (idx >= 0)
                vals[idx] += weight;
            else
            {
                cols.Add(targetCol);
                vals.Add(weight);
            }
        }

        private static TransportSystem BuildCoarseTransportSystem(int cells, List<int>[] cRows, List<float>[] cVals, float[] cDiag)
        {
            var rStart = new int[cells + 1];
            int total = 0;
            for (int c = 0; c < cells; c++)
            {
                rStart[c] = total;
                total += cRows[c].Count;
            }
            rStart[cells] = total;
            var col = new int[total];
            var val = new float[total];
            int pos = 0;
            for (int c = 0; c < cells; c++)
            {
                List<int> rl = cRows[c];
                List<float> vl = cVals[c];
                for (int k = 0; k < rl.Count; k++)
                {
                    col[pos] = rl[k];
                    val[pos] = vl[k];
                    pos++;
                }
            }
            var order = new int[cells];
            for (int c = 0; c < cells; c++)
                order[c] = c;
            return new TransportSystem
            {
                Count = cells,
                RowStart = rStart,
                Column = col,
                Weight = val,
                Diagonal = (float[])cDiag.Clone(),
                Rhs = new float[cells],
                SweepOrder = order
            };
        }

        private SnapshotPrep PrepareSnapshot(
            string name,
            int index,
            float weight,
            float[] temp,
            Vector3[] surfaceWind,
            Vector3[] transportWind,
            float ce,
            float coverage)
        {
            var surfaceE = new float[tileCount];
            var surfaceN = new float[tileCount];
            var windE = new float[tileCount];
            var windN = new float[tileCount];
            Vector3 up = new Vector3(0f, 1f, 0f);

            for (int i = 0; i < tileCount; i++)
            {
                Vector3 p = climate.TileCenters[i].normalized;
                Vector3 e = Vector3.Cross(p, up);
                if (e.sqrMagnitude < 1e-12f)
                    e = new Vector3(1f, 0f, 0f);
                else
                    e.Normalize();
                Vector3 n = Vector3.Cross(e, p).normalized;

                Vector3 sw = surfaceWind != null && i < surfaceWind.Length ? surfaceWind[i] : Vector3.zero;
                Vector3 tw = transportWind != null && i < transportWind.Length ? transportWind[i] : sw;
                float se = Vector3.Dot(sw, e);
                float sn = Vector3.Dot(sw, n);
                float te = Vector3.Dot(tw, e);
                float tn = Vector3.Dot(tw, n);

                surfaceE[i] = se;
                surfaceN[i] = sn;
                windE[i] = 0.44f * se + 0.56f * te;
                windN[i] = 0.44f * sn + 0.56f * tn;
            }

            // Mass-continuity return flow projection (--div-keep 0.35)
            float divKeep = config.moistDivKeep.CurrentValue;
            ProjectTransport(
                windE, windN, divKeep, coverage,
                out int[] donor, out int[] receiver, out float[] flow,
                out float[] fOut, out float[] rawNet, out float[] edgeInflowSource);

            var divergence = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
                divergence[i] = rawNet[i] / tileAreaM2;

            float d95 = Percentile95(divergence);
            if (d95 < 1e-8f) d95 = 1e-8f;
            float triggerD95 = config.moistTriggerD95.CurrentValue;
            if (triggerD95 > 0f && d95 > triggerD95)
            {
                float scale = triggerD95 / d95;
                for (int i = 0; i < tileCount; i++)
                    divergence[i] *= scale;
                d95 = triggerD95;
            }

            // Directional slope term using surface wind (--surf-slope)
            var climb = new float[tileCount];
            var threshold = new float[tileCount];
            var cRise = new float[tileCount];
            float rc0 = config.rainThreshold.CurrentValue;
            float beta = config.EffectiveRainThresholdSink;
            float gamma = config.EffectiveSlopeThreshold;
            float wRef = Mathf.Max(config.slopeRefVelocity.CurrentValue, 1e-4f);

            for (int i = 0; i < tileCount; i++)
            {
                float cl = surfaceE[i] * slopeEast[i] + surfaceN[i] * slopeNorth[i];
                climb[i] = cl;
                float th = Mathf.Clamp(rc0 + beta * divergence[i] / d95, 0.5f, 0.98f);
                th = Mathf.Clamp(th - gamma * cl / wRef, 0.35f, 0.98f);
                threshold[i] = th;
                cRise[i] = Mathf.Max(0f, -divergence[i]);
            }

            // Latitude in degrees for storm-track and eddy mixing
            var latDeg = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
                latDeg[i] = climate.LatitudeSigned[i] * Mathf.Rad2Deg;

            float[] belt = MoistureMath.StormTrackWeight(latDeg, climate.TrueOceanMask, windE, out float onsetN, out float onsetS);
            float[] kTile = MoistureMath.StormMixing(latDeg, climate.TrueOceanMask, surfaceE, config.EffectiveMoistEddyK);

            var capacity = new float[tileCount];
            var evaporates = new bool[tileCount];
            var kLinear = new float[tileCount];
            var eLinear = new float[tileCount];
            var eCapped = new float[tileCount];
            var rampLo = new float[tileCount];
            var rampSpan = new float[tileCount];
            var rampRate = new float[tileCount];
            var rampShift = new float[tileCount];

            float gust = config.oceanEvapGustMs.CurrentValue;
            float tauScale = Mathf.Max(config.EffectiveRainTimescaleDays / 2.0f, 0.1f);
            for (int i = 0; i < tileCount; i++)
            {
                float b = belt[i];
                float lo = MoistureMath.RampTropicsLo + b * (MoistureMath.RampBeltLo - MoistureMath.RampTropicsLo);
                float hi = MoistureMath.RampTropicsHi + b * (MoistureMath.RampBeltHi - MoistureMath.RampTropicsHi);
                float tauDays = (MoistureMath.RampTropicsTauDays + b * (MoistureMath.RampBeltTauDays - MoistureMath.RampTropicsTauDays)) * tauScale;
                rampLo[i] = lo;
                rampSpan[i] = Mathf.Max(hi - lo, 0.05f);
                rampRate[i] = 1f / (tauDays * 86400f);
                rampShift[i] = threshold[i] - rc0;

                float tAir = temp[i];
                float cap = MoistureMath.PrecipitableCapacity(tAir);
                capacity[i] = cap;
                float qAir = MoistureMath.SpecificHumidity(tAir);

                float swing = 1f - b * (1f - MoistureMath.StormSeaSwing);
                float tSea = climate.AnnualMeanTemp[i] + swing * (tAir - climate.AnnualMeanTemp[i]);
                float surfSpd = Mathf.Sqrt(surfaceE[i] * surfaceE[i] + surfaceN[i] * surfaceN[i]);
                float stormGust = b * MoistureMath.StormGustMs;
                float spd = Mathf.Max(Mathf.Sqrt(surfSpd * surfSpd + stormGust * stormGust), gust);

                float qSea = MoistureMath.SpecificHumidity(tSea);
                bool isWater = climate.OceanMask[i];
                bool isIce = isWater && tSea <= MoistureMath.IceC;
                bool evap = isWater && !isIce;
                evaporates[i] = evap;

                if (evap)
                {
                    float coeff = MoistureMath.RhoAir * ce * spd * MoistureMath.HsSlope * qAir / cap;
                    float baseEv = MoistureMath.RhoAir * ce * spd * (qSea - MoistureMath.HsBase * qAir);
                    float capEv = MoistureMath.RhoAir * ce * spd * (qSea - MoistureMath.HsCap * qAir);
                    if (baseEv <= 0f)
                        coeff = 0f;
                    kLinear[i] = coeff;
                    eLinear[i] = Mathf.Max(baseEv, 0f);
                    eCapped[i] = Mathf.Max(capEv, 0f);
                }
            }

            // Build combined CSR matrix (inflow + symmetric diffusion) and coarse restriction for two-grid solver
            BuildCombinedTransportAndDiffusion(
                donor, receiver, flow, fOut, kTile,
                out int[] rowStart, out int[] col, out float[] weightVal,
                out float[] diffDiag, out int[] upwindOrder,
                out float[] coarseBaseDiag, out TransportSystem coarseSys,
                out float[] superBaseDiag, out TransportSystem superSys);

            return new SnapshotPrep
            {
                Name = name,
                Index = index,
                Weight = weight,
                Temp = temp,
                SurfaceE = surfaceE,
                SurfaceN = surfaceN,
                WindE = windE,
                WindN = windN,
                FOut = fOut,
                Divergence = divergence,
                D95 = d95,
                Climb = climb,
                Threshold = threshold,
                Capacity = capacity,
                Evaporates = evaporates,
                KLinear = kLinear,
                ELinear = eLinear,
                ECapped = eCapped,
                CRise = cRise,
                KTile = kTile,
                RampLo = rampLo,
                RampSpan = rampSpan,
                RampRate = rampRate,
                RampShift = rampShift,
                DiffDiag = diffDiag,
                EdgeInflowSource = edgeInflowSource,
                RowStart = rowStart,
                Col = col,
                WeightVal = weightVal,
                UpwindOrder = upwindOrder,
                CoarseBaseDiag = coarseBaseDiag,
                CoarseSystem = coarseSys,
                SuperBaseDiag = superBaseDiag,
                SuperSystem = superSys
            };
        }

        private void ProjectTransport(
            float[] eastWind,
            float[] northWind,
            float divKeep,
            float coverage,
            out int[] donor,
            out int[] receiver,
            out float[] flow,
            out float[] fOut,
            out float[] rawNet,
            out float[] edgeInflowSource)
        {
            int uCount = undirSrc.Length;
            var rawFlux = new float[uCount];
            var foRaw = new float[tileCount];
            var fiRaw = new float[tileCount];
            rawNet = new float[tileCount];

            for (int e = 0; e < uCount; e++)
            {
                int s = undirSrc[e];
                int d = undirDst[e];
                float len = undirLengthM[e];
                float un = 0.5f * (
                    eastWind[s] * undirESrc[e] + northWind[s] * undirNSrc[e]
                    + eastWind[d] * undirEDst[e] + northWind[d] * undirNDst[e]);
                float rf = un * len;
                rawFlux[e] = rf;
                if (rf >= 0f)
                {
                    foRaw[s] += rf;
                    fiRaw[d] += rf;
                }
                else
                {
                    foRaw[d] -= rf;
                    fiRaw[s] -= rf;
                }

                // Subtract each tile's own constant wind when accumulating divergence so a uniform wind has zero geometric mesh divergence.
                // Note: at tile d, the outward normal toward s is -dir, and the velocity difference relative to u_d is (u_s - u_d) = -(u_d - u_s),
                // so the two minus signs cancel and both s and d add +du * len.
                float duS = 0.5f * ((eastWind[d] - eastWind[s]) * undirESrc[e] + (northWind[d] - northWind[s]) * undirNSrc[e]);
                float duD = 0.5f * ((eastWind[d] - eastWind[s]) * undirEDst[e] + (northWind[d] - northWind[s]) * undirNDst[e]);
                rawNet[s] += duS * len;
                rawNet[d] += duD * len;
            }

            // Smooth single-tile divergence spikes with 1 pass of 50/50 neighbor averaging
            var smoothNet = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                int start = edgeRowStart[i];
                int end = edgeRowStart[i + 1];
                int cnt = end - start;
                if (cnt <= 0)
                {
                    smoothNet[i] = rawNet[i];
                    continue;
                }
                float sum = 0f;
                for (int k = start; k < end; k++)
                    sum += rawNet[edgeDst[k]];
                smoothNet[i] = 0.5f * rawNet[i] + 0.5f * (sum / cnt);
            }
            Array.Copy(smoothNet, rawNet, tileCount);

            // At partial coverage (< 100%), account for open map-edge boundary inflow and outflow
            // from/to the surrounding hidden ocean so there is no artificial convergence/divergence ring along the edge.
            var edgeOutflow = new float[tileCount];
            edgeInflowSource = new float[tileCount];
            if (coverage < 0.989f)
            {
                const float edgeAlign = 0.70f;
                for (int i = 0; i < tileCount; i++)
                {
                    int start = edgeRowStart[i];
                    int end = edgeRowStart[i + 1];
                    if (end - start >= 6)
                        continue;
                    float ue = eastWind[i];
                    float un = northWind[i];
                    float spd = Mathf.Sqrt(ue * ue + un * un);
                    if (spd < 0.1f)
                        continue;
                    float dirE = ue / spd;
                    float dirN = un / spd;
                    float bestUp = -1f;
                    float bestDown = -1f;
                    for (int k = start; k < end; k++)
                    {
                        float dotDown = edgeESrc[k] * dirE + edgeNSrc[k] * dirN;
                        if (dotDown > bestDown) bestDown = dotDown;
                        if (-dotDown > bestUp) bestUp = -dotDown;
                    }
                    if (bestUp < edgeAlign)
                    {
                        float inFl = spd * meanEdgeLengthM;
                        fiRaw[i] += inFl;
                        float latRad = climate.Latitude[i];
                        float c = Mathf.Cos(latRad);
                        float tHidden = -18f + 45f * c * c;
                        float wHidden = 0.78f * MoistureMath.PrecipitableCapacity(tHidden);
                        edgeInflowSource[i] = inFl * wHidden;
                    }
                    if (bestDown < edgeAlign)
                    {
                        float outFl = spd * meanEdgeLengthM;
                        foRaw[i] += outFl;
                        edgeOutflow[i] = outFl;
                    }
                }
            }

            var rhs = new float[tileCount];
            double mean = 0.0;
            float remove = 1f - divKeep;
            for (int i = 0; i < tileCount; i++)
            {
                float r = remove * rawNet[i];
                rhs[i] = r;
                mean += r;
            }
            float meanF = (float)(mean / tileCount);
            for (int i = 0; i < tileCount; i++)
                rhs[i] -= meanF;

            var phi = new float[tileCount];
            var phiNext = new float[tileCount];
            int cells = mesh.Count;
            bool useCoarse = tileCount > cells * 2;

            if (useCoarse)
            {
                // Two-cycle barycentric coarse-mesh + 4-Jacobi-sweep return-flow potential solve (subdivisions 9–11)
                var cRhs = new float[cells];
                var cx = new float[cells];
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    Array.Clear(cRhs, 0, cells);
                    for (int i = 0; i < tileCount; i++)
                    {
                        double lapPhi = (poissonDiag[i] + 1e-6) * phi[i];
                        int end = poissonRowStart[i + 1];
                        for (int k = poissonRowStart[i]; k < end; k++)
                            lapPhi -= poissonVal[k] * (double)phi[poissonCol[k]];
                        cRhs[tileCell[i]] += (float)(rhs[i] - lapPhi);
                    }

                    SolveCoarsePoisson(cRhs, cx);
                    for (int i = 0; i < tileCount; i++)
                        phi[i] += baryWA[i] * cx[baryA[i]] + baryWB[i] * cx[baryB[i]] + baryWC[i] * cx[baryC[i]];

                    for (int sweep = 0; sweep < 4; sweep++)
                    {
                        for (int i = 0; i < tileCount; i++)
                        {
                            double adjPhi = rhs[i];
                            int end = poissonRowStart[i + 1];
                            for (int k = poissonRowStart[i]; k < end; k++)
                                adjPhi += poissonVal[k] * (double)phi[poissonCol[k]];
                            phiNext[i] = 0.2f * phi[i] + 0.8f * poissonInvDiag[i] * (float)adjPhi;
                        }
                        Array.Copy(phiNext, phi, tileCount);
                    }
                }
            }
            else
            {
                // On smaller planet scales (subdivisions 5–8), solve the Poisson potential directly on the tile graph
                var localFine = new TransportSystem
                {
                    Count = finePoissonSys.Count,
                    RowStart = finePoissonSys.RowStart,
                    Column = finePoissonSys.Column,
                    Weight = finePoissonSys.Weight,
                    Diagonal = finePoissonSys.Diagonal,
                    Rhs = rhs,
                    SweepOrder = finePoissonSys.SweepOrder
                };
                TransportSolution fSol = SteadyTransportSolver.Solve(localFine, 1e-4f, 60);
                Array.Copy(fSol.X, phi, tileCount);
            }

            var dList = new List<int>(uCount);
            var rList = new List<int>(uCount);
            var fList = new List<float>(uCount);
            fOut = new float[tileCount];
            Array.Copy(edgeOutflow, fOut, tileCount);

            for (int e = 0; e < uCount; e++)
            {
                int s = undirSrc[e];
                int d = undirDst[e];
                float adjFlux = rawFlux[e] - poissonEdgeCond[e] * (phi[s] - phi[d]);
                if (Mathf.Abs(adjFlux) <= 1e-6f)
                    continue;
                if (adjFlux >= 0f)
                {
                    dList.Add(s);
                    rList.Add(d);
                    fList.Add(adjFlux);
                    fOut[s] += adjFlux;
                }
                else
                {
                    float fl = -adjFlux;
                    dList.Add(d);
                    rList.Add(s);
                    fList.Add(fl);
                    fOut[d] += fl;
                }
            }

            donor = dList.ToArray();
            receiver = rList.ToArray();
            flow = fList.ToArray();
        }

        private void BuildCombinedTransportAndDiffusion(
            int[] donor,
            int[] receiver,
            float[] flow,
            float[] fOut,
            float[] kTile,
            out int[] rowStart,
            out int[] col,
            out float[] weightVal,
            out float[] diffDiag,
            out int[] upwindOrder,
            out float[] coarseBaseDiag,
            out TransportSystem coarseSys,
            out float[] superBaseDiag,
            out TransportSystem superSys)
        {
            upwindOrder = MoistureMath.BuildUpwindOrder(tileCount, donor, receiver, flow);

            var rowCols = new List<int>[tileCount];
            var rowVals = new List<float>[tileCount];
            diffDiag = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                rowCols[i] = new List<int>(7);
                rowVals[i] = new List<float>(7);
            }

            int cells = mesh.Count;
            var cRows = new List<int>[cells];
            var cVals = new List<float>[cells];
            coarseBaseDiag = new float[cells];
            for (int c = 0; c < cells; c++)
            {
                cRows[c] = new List<int>(6);
                cVals[c] = new List<float>(6);
            }

            // 1. Symmetric diffusion across undirected edges
            int uCount = undirSrc.Length;
            for (int e = 0; e < uCount; e++)
            {
                int s = undirSrc[e];
                int d = undirDst[e];
                float kEdge = 0.5f * (kTile[s] + kTile[d]);
                float cond = kEdge * undirLengthM[e] / undirDistM[e];
                rowCols[s].Add(d);
                rowVals[s].Add(cond);
                rowCols[d].Add(s);
                rowVals[d].Add(cond);
                diffDiag[s] += cond;
                diffDiag[d] += cond;

                int cs = tileCell[s];
                int cd = tileCell[d];
                if (cs != cd)
                {
                    coarseBaseDiag[cs] += cond;
                    coarseBaseDiag[cd] += cond;
                    AddCoarseEntry(cRows[cs], cVals[cs], cd, cond);
                    AddCoarseEntry(cRows[cd], cVals[cd], cs, cond);
                }
            }

            // 2. Upwind advective inflow: receiver gets flow from donor
            for (int i = 0; i < tileCount; i++)
                coarseBaseDiag[tileCell[i]] += fOut[i];

            int m = donor.Length;
            for (int e = 0; e < m; e++)
            {
                int dn = donor[e];
                int rc = receiver[e];
                float fl = flow[e];
                List<int> cols = rowCols[rc];
                List<float> vals = rowVals[rc];
                int idx = cols.IndexOf(dn);
                if (idx >= 0)
                    vals[idx] += fl;
                else
                {
                    cols.Add(dn);
                    vals.Add(fl);
                }

                int cDn = tileCell[dn];
                int cRc = tileCell[rc];
                if (cDn == cRc)
                    coarseBaseDiag[cRc] -= fl;
                else
                    AddCoarseEntry(cRows[cRc], cVals[cRc], cDn, fl);
            }

            rowStart = new int[tileCount + 1];
            int total = 0;
            for (int i = 0; i < tileCount; i++)
            {
                rowStart[i] = total;
                total += rowCols[i].Count;
            }
            rowStart[tileCount] = total;
            col = new int[total];
            weightVal = new float[total];
            int pos = 0;
            for (int i = 0; i < tileCount; i++)
            {
                List<int> rl = rowCols[i];
                List<float> vl = rowVals[i];
                for (int k = 0; k < rl.Count; k++)
                {
                    col[pos] = rl[k];
                    weightVal[pos] = vl[k];
                    pos++;
                }
            }

            coarseSys = BuildCoarseTransportSystem(cells, cRows, cVals, coarseBaseDiag);
            superSys = RestrictToSuperBins(coarseSys, coarseBaseDiag, out superBaseDiag);
        }

        private TransportSystem RestrictToSuperBins(TransportSystem cSys, float[] cBaseDiag, out float[] sBaseDiag)
        {
            var sRows = new List<int>[SuperBins];
            var sVals = new List<float>[SuperBins];
            sBaseDiag = new float[SuperBins];
            for (int b = 0; b < SuperBins; b++)
            {
                sRows[b] = new List<int>(8);
                sVals[b] = new List<float>(8);
                sBaseDiag[b] = 1e-4f;
            }

            int cells = cSys.Count;
            int[] cRowStart = cSys.RowStart;
            int[] cCol = cSys.Column;
            float[] cWeight = cSys.Weight;
            for (int c = 0; c < cells; c++)
            {
                int bc = cellBin[c];
                sBaseDiag[bc] += cBaseDiag[c];
                int end = cRowStart[c + 1];
                for (int k = cRowStart[c]; k < end; k++)
                {
                    int nb = cellBin[cCol[k]];
                    float w = cWeight[k];
                    if (bc == nb)
                        sBaseDiag[bc] -= w;
                    else
                        AddCoarseEntry(sRows[bc], sVals[bc], nb, w);
                }
            }
            return BuildCoarseTransportSystem(SuperBins, sRows, sVals, sBaseDiag);
        }

        private void SolveCoarsePoisson(float[] cRhs, float[] x)
        {
            int cells = coarsePoissonSys.Count;
            int[] cRowStart = coarsePoissonSys.RowStart;
            int[] cCol = coarsePoissonSys.Column;
            float[] cVal = coarsePoissonSys.Weight;
            float[] cDiag = coarsePoissonSys.Diagonal;

            Array.Clear(x, 0, cells);
            var xNext = new float[cells];
            var sRhs = new float[SuperBins];
            var localSuper = new TransportSystem
            {
                Count = superPoissonSys.Count,
                RowStart = superPoissonSys.RowStart,
                Column = superPoissonSys.Column,
                Weight = superPoissonSys.Weight,
                Diagonal = superPoissonSys.Diagonal,
                Rhs = sRhs,
                SweepOrder = superPoissonSys.SweepOrder
            };

            for (int v = 0; v < 5; v++)
            {
                for (int s = 0; s < 3; s++)
                {
                    for (int c = 0; c < cells; c++)
                    {
                        double sum = cRhs[c];
                        int end = cRowStart[c + 1];
                        for (int k = cRowStart[c]; k < end; k++)
                            sum += cVal[k] * (double)x[cCol[k]];
                        xNext[c] = 0.2f * x[c] + 0.8f * (float)(sum / Math.Max(cDiag[c], 1e-6f));
                    }
                    Array.Copy(xNext, x, cells);
                }

                Array.Clear(sRhs, 0, SuperBins);
                for (int c = 0; c < cells; c++)
                {
                    double ax = cDiag[c] * (double)x[c];
                    int end = cRowStart[c + 1];
                    for (int k = cRowStart[c]; k < end; k++)
                        ax -= cVal[k] * (double)x[cCol[k]];
                    sRhs[cellBin[c]] += (float)(cRhs[c] - ax);
                }

                TransportSolution sSol = SteadyTransportSolver.Solve(localSuper, 1e-5f, 80);
                float[] sx = sSol.X;
                for (int c = 0; c < cells; c++)
                    x[c] += sx[cellBin[c]];

                for (int s = 0; s < 3; s++)
                {
                    for (int c = 0; c < cells; c++)
                    {
                        double sum = cRhs[c];
                        int end = cRowStart[c + 1];
                        for (int k = cRowStart[c]; k < end; k++)
                            sum += cVal[k] * (double)x[cCol[k]];
                        xNext[c] = 0.2f * x[c] + 0.8f * (float)(sum / Math.Max(cDiag[c], 1e-6f));
                    }
                    Array.Copy(xNext, x, cells);
                }
            }
        }

        private void SolveSnapshot(
            SnapshotPrep prep,
            float[] landEvap,
            float[] guess,
            float[] subsidence,
            bool twoLayer,
            out float[] column,
            out float[] humidRate,
            out float[] riseRain,
            out float[] lifted,
            out float[] oceanEvap,
            out float[] deepOut)
        {
            column = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
                column[i] = Mathf.Max(guess[i], 0f);

            var deep = new float[tileCount];
            var rampCoeff = new float[tileCount];
            var diag = new float[tileCount];
            var rhs = new float[tileCount];
            float eps = config.rainEfficiency.CurrentValue;

            for (int picard = 0; picard < 3; picard++)
            {
                for (int i = 0; i < tileCount; i++)
                {
                    float hum = column[i] / prep.Capacity[i];
                    if (twoLayer)
                    {
                        float s = Mathf.Clamp01((hum - 0.45f) / 0.35f);
                        float target = s * s * (3f - 2f * s);
                        deep[i] = picard == 0 ? target : 0.5f * (deep[i] + target);
                    }
                    else
                    {
                        deep[i] = 1f;
                    }

                    float vent = twoLayer ? prep.CRise[i] * deep[i] : eps * prep.CRise[i];
                    bool capped = prep.Evaporates[i] && hum >= MoistureMath.RCap;
                    float ke = capped ? 0f : prep.KLinear[i];
                    float e0 = capped ? prep.ECapped[i] : prep.ELinear[i];
                    float transDiag = prep.FOut[i] + prep.DiffDiag[i] + tileAreaM2 * vent;
                    bool calmSource = transDiag < 1f && (e0 + landEvap[i]) > 0f;

                    float x = Mathf.Clamp01((hum - prep.RampShift[i] - prep.RampLo[i]) / prep.RampSpan[i]);
                    float targetCoeff = prep.RampRate[i] * x * x * (3f - 2f * x);
                    if (calmSource && targetCoeff < prep.RampRate[i])
                        targetCoeff = prep.RampRate[i];
                    rampCoeff[i] = picard == 0 ? targetCoeff : 0.5f * (rampCoeff[i] + targetCoeff);

                    float d = transDiag + tileAreaM2 * (ke + rampCoeff[i]);
                    float r = tileAreaM2 * (e0 + landEvap[i] + (subsidence != null ? subsidence[i] : 0f)) + prep.EdgeInflowSource[i];
                    if (d < 1f)
                    {
                        d = 1f;
                        r = column[i];
                    }
                    diag[i] = d;
                    rhs[i] = r;
                }

                SolveLinearTwoGrid(prep, diag, rhs, column);
            }

            humidRate = new float[tileCount];
            riseRain = new float[tileCount];
            lifted = new float[tileCount];
            oceanEvap = new float[tileCount];
            deepOut = deep;

            for (int i = 0; i < tileCount; i++)
            {
                float w = column[i];
                humidRate[i] = Mathf.Max(rampCoeff[i] * w, 0f);
                float export = deep[i];
                riseRain[i] = eps * export * prep.CRise[i] * w;
                lifted[i] = twoLayer ? (1f - eps) * export * prep.CRise[i] * w : 0f;

                if (prep.Evaporates[i])
                {
                    float hum = w / prep.Capacity[i];
                    bool capped = hum >= MoistureMath.RCap;
                    oceanEvap[i] = capped ? prep.ECapped[i] : Mathf.Max(prep.ELinear[i] - prep.KLinear[i] * w, 0f);
                }
            }
        }

        private void SolveLinearTwoGrid(SnapshotPrep prep, float[] diag, float[] rhs, float[] x)
        {
            int cells = mesh.Count;
            int[] order = prep.UpwindOrder;
            int[] rStart = prep.RowStart;
            int[] col = prep.Col;
            float[] val = prep.WeightVal;

            if (tileCount <= cells * 2)
            {
                // On smaller planet scales (subdivisions 5–8), the tile graph is already coarse (<= 65,612 tiles),
                // so solve directly on the tiles with upstream-preconditioned BiCGStab + Gauss-Seidel sweeps.
                var fSys = new TransportSystem
                {
                    Count = tileCount,
                    RowStart = rStart,
                    Column = col,
                    Weight = val,
                    Diagonal = diag,
                    Rhs = rhs,
                    SweepOrder = order
                };
                TransportSolution fSol = SteadyTransportSolver.Solve(fSys, 1e-4f, 40);
                float[] fx = fSol.X;
                for (int i = 0; i < tileCount; i++)
                    x[i] = Mathf.Max(fx[i], 0f);
                for (int s = 0; s < tileCount; s++)
                {
                    int i = order[s];
                    double acc = rhs[i];
                    int end = rStart[i + 1];
                    for (int k = rStart[i]; k < end; k++)
                        acc += val[k] * (double)x[col[k]];
                    x[i] = Mathf.Max((float)(acc / diag[i]), 0f);
                }
                return;
            }

            TransportSystem cSys = prep.CoarseSystem;
            float[] cDiag = cSys.Diagonal;
            float[] cRhs = cSys.Rhs;
            Array.Copy(prep.CoarseBaseDiag, cDiag, cells);

            TransportSystem sSys = prep.SuperSystem;
            float[] sDiag = sSys.Diagonal;
            float[] sRhs = sSys.Rhs;
            Array.Copy(prep.SuperBaseDiag, sDiag, SuperBins);

            for (int i = 0; i < tileCount; i++)
            {
                float localSink = diag[i] - (prep.FOut[i] + prep.DiffDiag[i]);
                int cell = tileCell[i];
                cDiag[cell] += localSink;
                sDiag[cellBin[cell]] += localSink;
            }
            for (int c = 0; c < cells; c++)
            {
                if (cDiag[c] < 1f)
                    cDiag[c] = 1f;
            }
            for (int b = 0; b < SuperBins; b++)
            {
                if (sDiag[b] < 1f)
                    sDiag[b] = 1f;
            }

            var corr = new float[cells];
            var corrNext = new float[cells];
            int[] cRowStart = cSys.RowStart;
            int[] cCol = cSys.Column;
            float[] cVal = cSys.Weight;

            // Two two-grid cycles: 1 ordered sweep -> 2-level coarse correction -> 1 ordered sweep
            for (int cycle = 0; cycle < 2; cycle++)
            {
                // Pre-smoothing upstream Gauss-Seidel sweep
                for (int s = 0; s < tileCount; s++)
                {
                    int i = order[s];
                    double acc = rhs[i];
                    int end = rStart[i + 1];
                    for (int k = rStart[i]; k < end; k++)
                        acc += val[k] * (double)x[col[k]];
                    x[i] = (float)(acc / diag[i]);
                }

                // Restrict residual to CoarseMesh cells
                Array.Clear(cRhs, 0, cells);
                for (int i = 0; i < tileCount; i++)
                {
                    double ax = diag[i] * (double)x[i];
                    int end = rStart[i + 1];
                    for (int k = rStart[i]; k < end; k++)
                        ax -= val[k] * (double)x[col[k]];
                    cRhs[tileCell[i]] += (float)(rhs[i] - ax);
                }

                // Fast 2-level solve on CoarseMesh (4 Jacobi sweeps + 648-bin super-coarse solve + 4 Jacobi sweeps)
                Array.Clear(corr, 0, cells);
                for (int v = 0; v < 2; v++)
                {
                    for (int sw = 0; sw < 3; sw++)
                    {
                        for (int c = 0; c < cells; c++)
                        {
                            double sum = cRhs[c];
                            int end = cRowStart[c + 1];
                            for (int k = cRowStart[c]; k < end; k++)
                                sum += cVal[k] * (double)corr[cCol[k]];
                            corrNext[c] = 0.2f * corr[c] + 0.8f * (float)(sum / cDiag[c]);
                        }
                        Array.Copy(corrNext, corr, cells);
                    }

                    Array.Clear(sRhs, 0, SuperBins);
                    for (int c = 0; c < cells; c++)
                    {
                        double ax = cDiag[c] * (double)corr[c];
                        int end = cRowStart[c + 1];
                        for (int k = cRowStart[c]; k < end; k++)
                            ax -= cVal[k] * (double)corr[cCol[k]];
                        sRhs[cellBin[c]] += (float)(cRhs[c] - ax);
                    }

                    TransportSolution sSol = SteadyTransportSolver.Solve(sSys, 1e-4f, 50);
                    float[] sx = sSol.X;
                    for (int c = 0; c < cells; c++)
                        corr[c] += sx[cellBin[c]];

                    for (int sw = 0; sw < 3; sw++)
                    {
                        for (int c = 0; c < cells; c++)
                        {
                            double sum = cRhs[c];
                            int end = cRowStart[c + 1];
                            for (int k = cRowStart[c]; k < end; k++)
                                sum += cVal[k] * (double)corr[cCol[k]];
                            corrNext[c] = 0.2f * corr[c] + 0.8f * (float)(sum / cDiag[c]);
                        }
                        Array.Copy(corrNext, corr, cells);
                    }
                }

                for (int i = 0; i < tileCount; i++)
                    x[i] += corr[tileCell[i]];

                // Post-smoothing upstream Gauss-Seidel sweep
                for (int s = 0; s < tileCount; s++)
                {
                    int i = order[s];
                    double acc = rhs[i];
                    int end = rStart[i + 1];
                    for (int k = rStart[i]; k < end; k++)
                        acc += val[k] * (double)x[col[k]];
                    x[i] = Mathf.Max((float)(acc / diag[i]), 0f);
                }
            }
        }

        private float[][] SplitLandEvap(float[] etMm, float[][] petBySnap)
        {
            float[] weights = { 0.25f, 0.25f, 0.50f };
            var rates = new float[][]
            {
                new float[tileCount],
                new float[tileCount],
                new float[tileCount]
            };

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i] || etMm[i] <= 0f)
                    continue;
                float e0 = Mathf.Max(petBySnap[0][i], 0.1f);
                float e1 = Mathf.Max(petBySnap[1][i], 0.1f);
                float e2 = Mathf.Max(petBySnap[2][i], 0.1f);
                float mix = weights[0] * e0 + weights[1] * e1 + weights[2] * e2;
                float annualRate = etMm[i] / MoistureMath.YearSeconds;
                rates[0][i] = annualRate * e0 / mix;
                rates[1][i] = annualRate * e1 / mix;
                rates[2][i] = annualRate * e2 / mix;
            }
            return rates;
        }

        private void WriteOutputs(
            SnapshotPrep[] snapshots,
            float[] rainMm,
            float[] etMm,
            float[][] snapRainMm,
            float[][] snapHumidMm,
            float[][] snapRiseMm,
            float[][] snapUpperMm,
            float[][] snapOceanEvapMm,
            float[][] snapW,
            UpperMoistureLayer upperLayer)
        {
            Ensure(ref climate.PrecipitableWaterMm);
            Ensure(ref climate.UpperColumnWaterMm);
            Ensure(ref climate.OceanEvaporationMm);
            Ensure(ref climate.LandEvaporationMm);
            Ensure(ref climate.RainHumidMm);
            Ensure(ref climate.RainRisingMm);
            Ensure(ref climate.RainUpperMm);
            Ensure(ref climate.DistanceAlongWindKm);
            Ensure(ref climate.OceanWaterShare);
            Ensure(ref climate.EquinoxCondensationMm);
            Ensure(ref climate.DriestSeasonRainMm);
            Ensure(ref climate.WettestSeasonRainMm);

            float wRef = Mathf.Max(config.slopeRefVelocity.CurrentValue, 1e-4f);
            float[] uJun = upperLayer?.GetCellColumn(0);
            float[] uDec = upperLayer?.GetCellColumn(1);
            float[] uEq = upperLayer?.GetCellColumn(2);

            for (int i = 0; i < tileCount; i++)
            {
                float rAnn = rainMm[i];
                float rJun = snapRainMm[0][i];
                float rDec = snapRainMm[1][i];
                float rEq = snapRainMm[2][i];

                climate.AdvectedCondensationMm[i] = rAnn;
                climate.AnnualRainfall[i] = Mathf.Max(0f, rAnn);
                climate.AridityIndex[i] = Mathf.Max(0.01f, climate.AnnualRainfall[i] / Mathf.Max(climate.PotentialEvapotranspiration[i], 50f));
                climate.SummerCondensationMm[i] = rJun;
                climate.WinterCondensationMm[i] = rDec;
                climate.EquinoxCondensationMm[i] = rEq;

                float driest = Mathf.Min(rJun, Mathf.Min(rDec, rEq));
                float wettest = Mathf.Max(rJun, Mathf.Max(rDec, rEq));
                climate.DriestSeasonRainMm[i] = driest;
                climate.WettestSeasonRainMm[i] = wettest;

                // Physical summer monsoon rain excess (mm in the local summer quarter above the annual rate),
                // gated by warm temperatures (>8°C), minimum annual rain (>250 mm), and summer-vs-winter contrast.
                float rSum = climate.LatitudeSigned[i] >= 0f ? rJun : rDec;
                float rWin = climate.LatitudeSigned[i] >= 0f ? rDec : rJun;
                float summerExcessMm = 0.25f * Mathf.Max(0f, rSum - rAnn);
                float tempGate = Mathf.Clamp01((climate.AnnualMeanTemp[i] - 8f) / 10f);
                float rainGate = Mathf.Clamp01((rAnn - 250f) / 350f);
                float seasonContrast = Mathf.Clamp01((rSum - rWin) / Mathf.Max(rSum, 200f));
                climate.MonsoonBoost[i] = climate.OceanMask[i]
                    ? 0f
                    : summerExcessMm * tempGate * rainGate * seasonContrast;

                float wAnn = 0.25f * snapW[0][i] + 0.25f * snapW[1][i] + 0.50f * snapW[2][i];
                climate.PrecipitableWaterMm[i] = wAnn;
                climate.AdvectedIncomingMoistureMm[i] = rAnn + etMm[i];
                climate.AdvectedRemainingMoistureMm[i] = wAnn;
                float capAnn = 0.25f * snapshots[0].Capacity[i] + 0.25f * snapshots[1].Capacity[i] + 0.50f * snapshots[2].Capacity[i];
                climate.AdvectedDryingRatio[i] = Mathf.Clamp01(wAnn / Mathf.Max(capAnn, 1f));
                climate.TemperatureCapacity[i] = capAnn;

                if (uJun != null)
                {
                    int c = tileCell[i];
                    climate.UpperColumnWaterMm[i] = 0.25f * uJun[c] + 0.25f * uDec[c] + 0.50f * uEq[c];
                }

                climate.LandEvaporationMm[i] = etMm[i];
                climate.OceanEvaporationMm[i] = snapOceanEvapMm[0][i] + snapOceanEvapMm[1][i] + snapOceanEvapMm[2][i];
                climate.RainHumidMm[i] = snapHumidMm[0][i] + snapHumidMm[1][i] + snapHumidMm[2][i];
                climate.RainRisingMm[i] = snapRiseMm[0][i] + snapRiseMm[1][i] + snapRiseMm[2][i];
                climate.RainUpperMm[i] = snapUpperMm[0][i] + snapUpperMm[1][i] + snapUpperMm[2][i];

                // Pack indices from directional surface-wind slopes (WS30 step 3 / step 8)
                const float packSlopeRef = 0.006f;
                float upSum = 0f;
                float leeSum = 0f;
                for (int s = 0; s < 3; s++)
                {
                    float cl = snapshots[s].Climb[i];
                    float w = snapshots[s].Weight;
                    upSum += w * Mathf.Clamp(Mathf.Max(0f, cl) / packSlopeRef, 0f, 2.5f);
                    leeSum += w * Mathf.Clamp(Mathf.Max(0f, -cl) / packSlopeRef, 0f, 2.0f);
                }
                climate.UpwindReliefIndex[i] = upSum;
                climate.LeewardShadowIndex[i] = leeSum;

                // Ocean water share vs recycled share estimate
                climate.OceanWaterShare[i] = climate.OceanMask[i]
                    ? 1f
                    : Mathf.Clamp01(1f - etMm[i] / Mathf.Max(rAnn + etMm[i], 1f));
            }

            ComputeDistanceAlongWind(snapshots);
        }

        private void ComputeDistanceAlongWind(SnapshotPrep[] snapshots)
        {
            // Downwind fetch (km) from the true ocean along the annual-mean carrying wind (WS29 step 8)
            var annE = new float[tileCount];
            var annN = new float[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                annE[i] = 0.25f * snapshots[0].WindE[i] + 0.25f * snapshots[1].WindE[i] + 0.50f * snapshots[2].WindE[i];
                annN[i] = 0.25f * snapshots[0].WindN[i] + 0.25f * snapshots[1].WindN[i] + 0.50f * snapshots[2].WindN[i];
            }

            const float maxKm = 15000f;
            float[] distKm = climate.DistanceAlongWindKm;
            var order = new List<int>(tileCount);
            for (int i = 0; i < tileCount; i++)
            {
                distKm[i] = climate.TrueOceanMask != null && climate.TrueOceanMask[i] ? 0f : maxKm;
            }

            // Relax along upwind edges for 40 passes
            int uCount = undirSrc.Length;
            for (int pass = 0; pass < 40; pass++)
            {
                bool changed = false;
                for (int e = 0; e < uCount; e++)
                {
                    int s = undirSrc[e];
                    int d = undirDst[e];
                    float along = 0.5f * (
                        annE[s] * undirESrc[e] + annN[s] * undirNSrc[e]
                        + annE[d] * undirEDst[e] + annN[d] * undirNDst[e]);
                    if (Mathf.Abs(along) <= 0.3f)
                        continue;
                    int up = along >= 0f ? s : d;
                    int dn = along >= 0f ? d : s;
                    float stepKm = undirDistM[e] * 0.001f;
                    float cand = distKm[up] + stepKm;
                    if (cand + 0.1f < distKm[dn])
                    {
                        distKm[dn] = cand;
                        changed = true;
                    }
                }
                if (!changed)
                    break;
            }
        }

        private void Ensure(ref float[] array)
        {
            if (array == null || array.Length != tileCount)
                array = new float[tileCount];
        }

        private static float Percentile95(float[] values)
        {
            var copy = (float[])values.Clone();
            Array.Sort(copy);
            int idx = Mathf.Clamp((int)(0.95f * (copy.Length - 1)), 0, copy.Length - 1);
            return copy[idx];
        }
    }
}
