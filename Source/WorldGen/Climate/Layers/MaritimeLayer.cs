using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.Utilities.WorldGen;
using Planets.WorldGen.Geography;

namespace Planets.WorldGen.Climate.Layers
{
    /// <summary>
    /// Ocean air carried inland (WS29). For each snapshot a steady solve carries the share of ocean air on the
    /// circulation's upper-layer wind at layer speed, plus an eastward storm drift in the westerly belt, and fades
    /// it with distance travelled. Every coast also keeps a short sea-breeze fringe, whatever the wind.
    /// Continentality is one minus that share, local winter weighted most. The same solve carries the sea
    /// temperature, so the coastal blend pulls toward the ocean the air came from instead of the nearest one.
    /// A second solve carries air that crossed land out over the water. Seasonality uses that share for the
    /// ocean's own seasonal swing: open ocean far from land barely swings; ocean downwind of a continent swings more.
    /// Runs only when the new-climate toggle is on, after circulation.
    /// </summary>
    public class MaritimeLayer : IClimateLayer
    {
        /// <summary>Total inflow below this, m/s, counts as calm. A calm tile averages its neighbours instead.</summary>
        const float CalmInflowMs = 0.05f;
        /// <summary>On a map edge, an upwind direction with no neighbour within about 45° is open to the hidden ocean.
        /// An interior tile always has one within 30°, a pentagon within 36°.</summary>
        const float EdgeAlignment = 0.7f;
        /// <summary>Sweeps stop when no share moves more than this tolerance.</summary>
        const float SweepTolerance = 1e-4f;
        const int SweepCap = 200;
        /// <summary>Width of the latitude bands that set the storm drift, degrees.</summary>
        const float DriftBandDeg = 2f;

        readonly ClimateData climate;
        readonly ClimateConfiguration config;
        readonly int tileCount;

        int[] nbrStart;
        int[] nbr;
        Vector3[] nbrDir;
        float[] nbrKm;
        Vector3[] tileEast;
        float meanSpacingKm;

        public string LayerName => "Maritime";

        /// <summary>Coupling round, for the log only.</summary>
        public int Round { get; set; }

        public MaritimeLayer(ClimateData climateData, ClimateConfiguration configuration)
        {
            climate = climateData;
            config = configuration ?? new ClimateConfiguration();
            tileCount = climate.Latitude.Length;
        }

        public void Execute(ClimateData climateData)
        {
            Vector3[] june = climate.TransportJune ?? climate.SummerWind;
            Vector3[] dec = climate.TransportDecember ?? climate.WinterWind;
            Vector3[] eq = climate.TransportEquinox ?? climate.EquinoxWind;
            if (june == null || dec == null || eq == null)
            {
                RPLog.W("[Maritime] The circulation winds are missing. Continentality keeps the nearest-coast value.");
                return;
            }

            if (nbrStart == null)
                BuildGraph();

            float fadeKm = Mathf.Max(1f, config.EffectiveMaritimeFadeKm);
            float oceanFadeKm = Mathf.Max(1f, config.maritimeOceanFadeKm.CurrentValue);
            float breezeKm = Mathf.Max(1f, config.EffectiveMaritimeBreezeKm);
            float winterWeight = Mathf.Clamp01(config.maritimeWinterWeight.CurrentValue);
            float blendWeight = config.maritimeBlendWeight.CurrentValue;
            float drift = config.maritimeStormDrift.CurrentValue;

            var fixedShare = new float[tileCount];
            var fixedSst = new float[tileCount];
            var breeze = new float[tileCount];
            var nearSst = new float[tileCount];
            PrepareSources(breezeKm, fixedShare, fixedSst, breeze, nearSst);

            Ensure(ref climate.MaritimeJune);
            Ensure(ref climate.MaritimeDecember);
            Ensure(ref climate.MaritimeEquinox);
            Ensure(ref climate.MaritimeAnnual);
            Ensure(ref climate.UpwindSST);
            Ensure(ref climate.ContinentalAir);
            var heatJune = new float[tileCount];
            var heatDec = new float[tileCount];
            var heatEq = new float[tileCount];
            var landAirJune = new float[tileCount];
            var landAirDec = new float[tileCount];
            var landAirEq = new float[tileCount];
            Vector3[][] rawWinds = { june, dec, eq };
            var driftedWinds = new Vector3[3][];
            Parallel.For(0, 3, s =>
            {
                driftedWinds[s] = WithDrift(rawWinds[s], drift);
            });

            string[] labels = { "June", "December", "Equinox" };
            float[][] marShares = { climate.MaritimeJune, climate.MaritimeDecember, climate.MaritimeEquinox };
            float[][] heats = { heatJune, heatDec, heatEq };
            float[][] landAirs = { landAirJune, landAirDec, landAirEq };
            var landSweeps = new int[3];
            var landChanges = new float[3];
            var landCalms = new int[3];
            var landEdges = new int[3];
            var waterSweeps = new int[3];
            var waterChanges = new float[3];
            var waterCalms = new int[3];
            var waterEdges = new int[3];

            Parallel.For(0, 6, task =>
            {
                int s = task % 3;
                if (task < 3)
                {
                    SolveLand(driftedWinds[s], fadeKm, fixedShare, fixedSst, breeze, nearSst, marShares[s], heats[s],
                        out landSweeps[s], out landChanges[s], out landCalms[s], out landEdges[s]);
                }
                else
                {
                    SolveWater(driftedWinds[s], oceanFadeKm, landAirs[s],
                        out waterSweeps[s], out waterChanges[s], out waterCalms[s], out waterEdges[s]);
                }
            });

            if (Controller.DebugEnabled)
            {
                for (int s = 0; s < 3; s++)
                    LogSolve("ocean air over land", labels[s], landSweeps[s], landChanges[s], landCalms[s], landEdges[s]);
                for (int s = 0; s < 3; s++)
                    LogSolve("land air over water", labels[s], waterSweeps[s], waterChanges[s], waterCalms[s], waterEdges[s]);
            }

            float sideWeight = 0.5f * (1f - winterWeight);
            for (int i = 0; i < tileCount; i++)
            {
                float j = climate.MaritimeJune[i];
                float d = climate.MaritimeDecember[i];
                float e = climate.MaritimeEquinox[i];
                float annual = 0.25f * j + 0.25f * d + 0.5f * e;
                float heat = 0.25f * heatJune[i] + 0.25f * heatDec[i] + 0.5f * heatEq[i];
                climate.MaritimeAnnual[i] = annual;
                bool north = climate.LatitudeSigned[i] >= 0f;
                if (climate.OceanMask[i])
                {
                    climate.UpwindSST[i] = climate.SST[i];
                    climate.Continentality[i] = 0f;
                    float landWinter = north ? landAirDec[i] : landAirJune[i];
                    float landSummer = north ? landAirJune[i] : landAirDec[i];
                    climate.ContinentalAir[i] = Mathf.Clamp01(winterWeight * landWinter + sideWeight * (landSummer + landAirEq[i]));
                    continue;
                }
                climate.ContinentalAir[i] = 1f;
                climate.UpwindSST[i] = annual > 1e-4f ? heat / annual : nearSst[i];
                float winter = north ? d : j;
                float summer = north ? j : d;
                climate.Continentality[i] = 1f - Mathf.Clamp01(winterWeight * winter + sideWeight * (summer + e));
                climate.CoastalModerationStrength[i] = blendWeight * annual;
            }

            if (Controller.DebugEnabled)
                LogShares();
        }

        /// <summary>
        /// Storms travel west to east wherever the mean upper wind is westerly, and carry air with them.
        /// The drift is gain times the band's mean eastward wind, positive part only, added toward the east.
        /// </summary>
        Vector3[] WithDrift(Vector3[] wind, float gain)
        {
            var result = (Vector3[])wind.Clone();
            if (gain <= 0f)
                return result;
            int bands = Mathf.CeilToInt(180f / DriftBandDeg);
            var sum = new double[bands];
            var count = new int[bands];
            var band = new int[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                float latDeg = climate.LatitudeSigned[i] * Mathf.Rad2Deg;
                band[i] = Mathf.Clamp(Mathf.FloorToInt((latDeg + 90f) / DriftBandDeg), 0, bands - 1);
                sum[band[i]] += Vector3.Dot(wind[i], tileEast[i]);
                count[band[i]]++;
            }
            for (int i = 0; i < tileCount; i++)
            {
                float westerly = count[band[i]] > 0 ? Mathf.Max(0f, (float)(sum[band[i]] / count[band[i]])) : 0f;
                result[i] += gain * westerly * tileEast[i];
            }
            return result;
        }

        /// <summary>
        /// Water holds its own share: 1 on the true ocean, its body strength on enclosed water. The breeze is the
        /// nearest-coast fade at the breeze length, plus the enclosed-water shore influence at its usual weight.
        /// </summary>
        void PrepareSources(float breezeKm, float[] fixedShare, float[] fixedSst, float[] breeze, float[] nearSst)
        {
            bool haveTrue = climate.TrueOceanMask != null && climate.TrueOceanMask.Length >= tileCount;
            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    bool open = !haveTrue || climate.TrueOceanMask[i];
                    float body = climate.InlandBodyStrength != null && i < climate.InlandBodyStrength.Length
                        ? Mathf.Clamp01(climate.InlandBodyStrength[i])
                        : 0f;
                    fixedShare[i] = open ? 1f : body;
                    fixedSst[i] = climate.SST[i];
                    nearSst[i] = climate.SST[i];
                    continue;
                }
                float inland = climate.InlandWaterInfluence != null && i < climate.InlandWaterInfluence.Length
                    ? climate.InlandWaterInfluence[i]
                    : 0f;
                breeze[i] = Mathf.Clamp01(Mathf.Exp(-climate.OceanDistKm[i] / breezeKm) + WaterBodies.InlandShoreWeight * inland);
                int nearest = climate.NearestOceanTile != null ? climate.NearestOceanTile[i] : -1;
                nearSst[i] = nearest >= 0 ? climate.SST[nearest] : climate.LatitudinalSeaLevelTemp[i];
            }
        }

        /// <summary>
        /// Ocean air over land for one snapshot. Water is fixed at its own share and sea temperature. A map-edge land
        /// tile whose upwind side is open takes air from the hidden ocean. The carried share A and the breeze B
        /// combine as 1 - (1 - A)(1 - B).
        /// </summary>
        void SolveLand(Vector3[] wind, float fadeKm, float[] fixedShare, float[] fixedSst,
            float[] breeze, float[] nearSst, float[] share, float[] heat,
            out int sweeps, out float change, out int calm, out int edges)
        {
            var solve = new bool[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                solve[i] = !climate.OceanMask[i];
                share[i] = solve[i] ? 0f : fixedShare[i];
                heat[i] = solve[i] ? 0f : fixedShare[i] * fixedSst[i];
            }
            float edgeKeep = Mathf.Exp(-meanSpacingKm / fadeKm);
            Carry(wind, fadeKm, solve, share, heat, edgeKeep, true, out calm, out edges, out sweeps, out change);

            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                    continue;
                float a = Mathf.Clamp01(share[i]);
                float b = breeze[i];
                float carriedHeat = heat[i];
                share[i] = 1f - (1f - a) * (1f - b);
                heat[i] = carriedHeat + (1f - a) * b * nearSst[i];
            }
        }

        /// <summary>
        /// Air that crossed land, carried out over the water for one snapshot. Every land tile is fixed at 1: air that
        /// has crossed a continent is continental, whatever coast it left from. It fades with distance over water.
        /// A map-edge water tile whose upwind side is open takes hidden-ocean air, which has crossed no land.
        /// </summary>
        void SolveWater(Vector3[] wind, float fadeKm, float[] landAir,
            out int sweeps, out float change, out int calm, out int edges)
        {
            var solve = new bool[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                solve[i] = climate.OceanMask[i];
                landAir[i] = solve[i] ? 0f : 1f;
            }
            Carry(wind, fadeKm, solve, landAir, null, 0f, false, out calm, out edges, out sweeps, out change);
        }

        /// <summary>
        /// Steady carry on the tile grid. An unknown tile takes its upwind neighbours' value, weighted by how hard each
        /// blows into it, times exp(-distance / fade); a calm tile averages all neighbours the same way. Fixed tiles keep
        /// what the caller put in. Gauss-Seidel sweeps in upstream order, value and heat together.
        /// </summary>
        void Carry(Vector3[] wind, float fadeKm, bool[] solve, float[] value, float[] heat, float edgeValue, bool edgeHeat,
            out int calm, out int edges, out int sweeps, out float change)
        {
            var rowStart = new int[tileCount + 1];
            var column = new List<int>(nbr.Length);
            var weight = new List<float>(nbr.Length);
            var intoList = new List<float>(nbr.Length);
            var unknown = new bool[tileCount];
            calm = 0;
            edges = 0;

            for (int i = 0; i < tileCount; i++)
            {
                rowStart[i] = column.Count;
                if (!solve[i])
                    continue;

                int start = nbrStart[i];
                int end = nbrStart[i + 1];
                Vector3 own = wind[i];
                if (own.sqrMagnitude > 0.01f)
                {
                    Vector3 upwind = -own.normalized;
                    float best = -1f;
                    for (int k = start; k < end; k++)
                        best = Mathf.Max(best, Vector3.Dot(nbrDir[k], upwind));
                    if (best < EdgeAlignment)
                    {
                        edges++;
                        value[i] = edgeValue;
                        if (heat != null)
                            heat[i] = edgeHeat ? edgeValue * HiddenOceanTemperature(climate.Latitude[i]) : 0f;
                        continue;
                    }
                }

                unknown[i] = true;
                float total = 0f;
                for (int k = start; k < end; k++)
                    total += Mathf.Max(0f, -Vector3.Dot(wind[nbr[k]], nbrDir[k]));
                bool isCalm = total < CalmInflowMs;
                if (isCalm)
                    calm++;
                for (int k = start; k < end; k++)
                {
                    float into = isCalm ? 1f / (end - start) : Mathf.Max(0f, -Vector3.Dot(wind[nbr[k]], nbrDir[k])) / total;
                    if (into <= 1e-6f)
                        continue;
                    column.Add(nbr[k]);
                    weight.Add(into * Mathf.Exp(-nbrKm[k] / fadeKm));
                    intoList.Add(into);
                }
            }
            rowStart[tileCount] = column.Count;
            int[] cols = column.ToArray();
            float[] w = weight.ToArray();
            float[] intoArr = intoList.ToArray();

            // Upstream first. Loops break at the lowest remaining in-degree, then the lower tile id.
            int[] order = BuildSweepOrder(tileCount, rowStart, cols, intoArr);

            sweeps = 0;
            change = 0f;
            for (; sweeps < SweepCap; sweeps++)
            {
                change = 0f;
                for (int s = 0; s < order.Length; s++)
                {
                    int i = order[s];
                    if (!unknown[i])
                        continue;
                    float v = 0f;
                    float h = 0f;
                    for (int k = rowStart[i]; k < rowStart[i + 1]; k++)
                    {
                        v += w[k] * value[cols[k]];
                        if (heat != null)
                            h += w[k] * heat[cols[k]];
                    }
                    change = Mathf.Max(change, Mathf.Abs(v - value[i]));
                    value[i] = v;
                    if (heat != null)
                        heat[i] = h;
                }
                if (change < SweepTolerance)
                {
                    sweeps++;
                    break;
                }
            }
        }

        void LogSolve(string what, string label, int sweeps, float change, int calm, int edges)
        {
            if (!Controller.DebugEnabled)
                return;
            RPLog.D($"[Maritime] Round {Round} {label} {what}: {sweeps} sweeps, last change {change:E2}, {calm} calm tiles, {edges} map-edge tiles fed by the hidden ocean.");
            if (change >= SweepTolerance)
                RPLog.W($"[Maritime] Round {Round} {label} {what} stopped at {SweepCap} sweeps before its tolerance.");
        }

        static int[] BuildSweepOrder(int n, int[] rowStart, int[] cols, float[] into)
        {
            const float OrderingFlux = 0.05f;
            var indegree = new int[n];
            var outCount = new int[n + 1];
            for (int i = 0; i < n; i++)
            {
                int end = rowStart[i + 1];
                for (int k = rowStart[i]; k < end; k++)
                {
                    if (into[k] < OrderingFlux)
                        continue;
                    indegree[i]++;
                    outCount[cols[k] + 1]++;
                }
            }
            for (int i = 0; i < n; i++)
                outCount[i + 1] += outCount[i];

            var downstream = new int[outCount[n]];
            var cursor = (int[])outCount.Clone();
            for (int i = 0; i < n; i++)
            {
                int end = rowStart[i + 1];
                for (int k = rowStart[i]; k < end; k++)
                {
                    if (into[k] < OrderingFlux)
                        continue;
                    downstream[cursor[cols[k]]++] = i;
                }
            }

            var buckets = new Queue<int>[7];
            for (int b = 0; b < 7; b++)
                buckets[b] = new Queue<int>(b == 0 ? n / 4 : 1024);
            for (int i = 0; i < n; i++)
                buckets[Mathf.Clamp(indegree[i], 0, 6)].Enqueue(i);

            var settled = new bool[n];
            var order = new int[n];
            int count = 0;
            while (count < n)
            {
                int tile = -1;
                for (int b = 0; b < 7; b++)
                {
                    Queue<int> q = buckets[b];
                    while (q.Count > 0)
                    {
                        int cand = q.Dequeue();
                        if (!settled[cand] && Mathf.Clamp(indegree[cand], 0, 6) == b)
                        {
                            tile = cand;
                            break;
                        }
                    }
                    if (tile >= 0)
                        break;
                }
                if (tile < 0)
                    break;

                settled[tile] = true;
                order[count++] = tile;
                int end = outCount[tile + 1];
                for (int k = outCount[tile]; k < end; k++)
                {
                    int dest = downstream[k];
                    if (!settled[dest])
                    {
                        indegree[dest] = Mathf.Max(0, indegree[dest] - 1);
                        buckets[Mathf.Clamp(indegree[dest], 0, 6)].Enqueue(dest);
                    }
                }
            }
            return order;
        }

        void BuildGraph()
        {
            WorldGrid grid = Find.WorldGrid;
            var buffer = new List<PlanetTile>();
            nbrStart = new int[tileCount + 1];
            tileEast = new Vector3[tileCount];
            var ids = new List<int>(tileCount * 6);
            var dirs = new List<Vector3>(tileCount * 6);
            var kms = new List<float>(tileCount * 6);
            double spacing = 0.0;
            for (int i = 0; i < tileCount; i++)
            {
                nbrStart[i] = ids.Count;
                Vector3 here = climate.TileCenters[i];
                Vector3 east = Vector3.Cross(here.normalized, Vector3.up);
                tileEast[i] = east.sqrMagnitude > 1e-8f ? east.normalized : Vector3.right;
                buffer.Clear();
                grid.GetTileNeighbors(i, buffer);
                foreach (PlanetTile n in buffer)
                {
                    int j = n.tileId;
                    if (j < 0 || j >= tileCount)
                        continue;
                    Vector3 there = climate.TileCenters[j];
                    float km = ClimateGeometryHelpers.ArcDistanceKm(here, there);
                    ids.Add(j);
                    dirs.Add((there - here).normalized);
                    kms.Add(km);
                    spacing += km;
                }
            }
            nbrStart[tileCount] = ids.Count;
            nbr = ids.ToArray();
            nbrDir = dirs.ToArray();
            nbrKm = kms.ToArray();
            meanSpacingKm = nbr.Length > 0 ? (float)(spacing / nbr.Length) : 30f;
        }

        float HiddenOceanTemperature(float latRad)
        {
            float equator = climate.EquatorMean + ClimateReferenceData.OceanEquatorOffset;
            float pole = climate.PoleMean + ClimateReferenceData.OceanPoleOffset;
            return pole + (equator - pole) * Mathf.Cos(latRad);
        }

        void LogShares()
        {
            var cont = new List<float>();
            var annual = new List<float>();
            var landAir = new List<float>();
            for (int i = 0; i < tileCount; i++)
            {
                if (climate.OceanMask[i])
                {
                    landAir.Add(climate.ContinentalAir[i]);
                    continue;
                }
                cont.Add(climate.Continentality[i]);
                annual.Add(climate.MaritimeAnnual[i]);
            }
            if (cont.Count == 0)
                return;
            cont.Sort();
            annual.Sort();
            landAir.Sort();
            float airMedian = landAir.Count > 0 ? landAir[landAir.Count / 2] : 0f;
            float air90 = landAir.Count > 0 ? landAir[landAir.Count * 9 / 10] : 0f;
            RPLog.D($"[Maritime] Round {Round} land: continentality 10/50/90% {cont[cont.Count / 10]:F2} / {cont[cont.Count / 2]:F2} / {cont[cont.Count * 9 / 10]:F2}, " +
                    $"yearly ocean-air share median {annual[annual.Count / 2]:F2}. Water: continental-air share median {airMedian:F2}, 90% {air90:F2}.");
        }

        void Ensure(ref float[] values)
        {
            if (values == null || values.Length != tileCount)
                values = new float[tileCount];
        }
    }
}
