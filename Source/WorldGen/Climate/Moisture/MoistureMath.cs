using System;
using System.Collections.Generic;
using UnityEngine;

namespace Planets.WorldGen.Climate.Moisture
{
    /// <summary>
    /// Physical thermodynamics, Fu-Budyko evapotranspiration, storm-track parameterization,
    /// and upwind sweep ordering for the two-layer moisture budget (<see cref="MoistureBudgetLayer"/>).
    /// </summary>
    public static class MoistureMath
    {
        public const float RadiusM = 6371000f;
        public const float YearSeconds = 365f * 86400f;
        public const float RhoAir = 1.2f;
        public const float P0 = 101325f;
        public const float VaporScaleM = 2200f;
        public const float AirScaleM = 8000f;
        public const float IceC = -2.0f;

        // Near-surface humidity h_s = clamp(0.55 + 0.35 * (W / W_s), 0.40, 0.95)
        public const float HsBase = 0.55f;
        public const float HsSlope = 0.35f;
        public const float HsCap = 0.95f;
        public const float RCap = (HsCap - HsBase) / HsSlope;

        // Storm-track eddy mixing K = min(base + 5.25e5 * max(u_bar, 0), 5.0e6)
        public const float StormKPerMs = 5.25e5f;
        public const float StormKCap = 5.0e6f;
        public const float StormOnsetMs = 1.5f;

        // Humid-air saturation ramp (tropics vs westerly storm belt)
        public const float RampTropicsLo = 0.49f;
        public const float RampTropicsHi = 1.19f;
        public const float RampTropicsTauDays = 4.9f;
        public const float RampBeltLo = 0.20f;
        public const float RampBeltHi = 0.82f;
        public const float RampBeltTauDays = 6.3f;

        // Storm-belt ocean evaporation lag and gust
        public const float StormSeaSwing = 0.55f;
        public const float StormGustMs = 5.0f;

        public static float SaturationVaporPressure(float tempC)
        {
            double t = Math.Max(-80.0, Math.Min(60.0, tempC));
            return (float)(610.94 * Math.Exp(17.625 * t / (t + 243.04)));
        }

        public static float SpecificHumidity(float tempC, float pressurePa = P0)
        {
            double e = SaturationVaporPressure(tempC);
            return (float)(0.622 * e / Math.Max(pressurePa - 0.378 * e, 100.0));
        }

        public static float PrecipitableCapacity(float tempC)
        {
            return Mathf.Max(RhoAir * SpecificHumidity(tempC) * VaporScaleM, 1e-3f);
        }

        public static float FuFraction(float phi, float w)
        {
            double p = Math.Max(0.0, Math.Min(1.0e4, phi));
            double pw = Math.Pow(p, w);
            return (float)(1.0 + p - Math.Pow(1.0 + pw, 1.0 / w));
        }

        public static void BudykoMm(float[] rainMm, float[] petMm, bool[] oceanMask, float w, float[] etOut)
        {
            int n = rainMm.Length;
            for (int i = 0; i < n; i++)
            {
                if (oceanMask[i] || rainMm[i] <= 1f || petMm[i] <= 0f)
                {
                    etOut[i] = 0f;
                    continue;
                }
                float phi = petMm[i] / rainMm[i];
                float et = rainMm[i] * FuFraction(phi, w);
                etOut[i] = Mathf.Min(et, petMm[i]);
            }
        }

        /// <summary>
        /// Zonal mean in 2° latitude bins (−89° to +89°), smoothed with a 5-bin (10°) box filter.
        /// </summary>
        public static void ZonalMean(float[] latDeg, float[] values, bool[] mask, out float[] centres, out float[] zmean)
        {
            const int bins = 90;
            centres = new float[bins];
            for (int b = 0; b < bins; b++)
                centres[b] = -89f + 2f * b;

            var total = new double[bins];
            var count = new int[bins];
            int n = latDeg.Length;
            for (int i = 0; i < n; i++)
            {
                if (mask != null && !mask[i])
                    continue;
                int b = Mathf.Clamp((int)((latDeg[i] + 90f) / 2f), 0, bins - 1);
                total[b] += values[i];
                count[b]++;
            }

            var raw = new float[bins];
            int firstGood = -1;
            for (int b = 0; b < bins; b++)
            {
                if (count[b] > 0)
                {
                    raw[b] = (float)(total[b] / count[b]);
                    if (firstGood < 0) firstGood = b;
                }
            }

            if (firstGood < 0)
            {
                zmean = new float[bins];
                return;
            }

            // Fill any empty bins by linear interpolation / edge clamping
            int prev = firstGood;
            for (int b = 0; b < firstGood; b++)
                raw[b] = raw[firstGood];
            for (int b = firstGood + 1; b < bins; b++)
            {
                if (count[b] > 0)
                {
                    if (b > prev + 1)
                    {
                        for (int k = prev + 1; k < b; k++)
                        {
                            float t = (float)(k - prev) / (b - prev);
                            raw[k] = Mathf.Lerp(raw[prev], raw[b], t);
                        }
                    }
                    prev = b;
                }
            }
            for (int b = prev + 1; b < bins; b++)
                raw[b] = raw[prev];

            // 5-bin box filter with edge padding
            zmean = new float[bins];
            for (int b = 0; b < bins; b++)
            {
                float sum = 0f;
                for (int d = -2; d <= 2; d++)
                    sum += raw[Mathf.Clamp(b + d, 0, bins - 1)];
                zmean[b] = sum * 0.2f;
            }
        }

        public static float Interp(float x, float[] xs, float[] ys)
        {
            int n = xs.Length;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];
            int b = Mathf.Clamp((int)((x - xs[0]) / (xs[1] - xs[0])), 0, n - 2);
            float t = (x - xs[b]) / (xs[b + 1] - xs[b]);
            return ys[b] + t * (ys[b + 1] - ys[b]);
        }

        public static float[] StormTrackWeight(float[] latDeg, bool[] trueOcean, float[] eastWind, out float onsetNorth, out float onsetSouth)
        {
            ZonalMean(latDeg, eastWind, trueOcean, out float[] centres, out float[] zmean);
            onsetNorth = 120f;
            onsetSouth = 120f;
            for (int b = 0; b < centres.Length; b++)
            {
                if (centres[b] >= 15f && zmean[b] >= StormOnsetMs && Mathf.Abs(centres[b]) < onsetNorth)
                    onsetNorth = Mathf.Abs(centres[b]);
                if (centres[b] <= -15f && zmean[b] >= StormOnsetMs && Mathf.Abs(centres[b]) < onsetSouth)
                    onsetSouth = Mathf.Abs(centres[b]);
            }

            int n = latDeg.Length;
            var belt = new float[n];
            for (int i = 0; i < n; i++)
            {
                float edge = latDeg[i] >= 0f ? onsetNorth : onsetSouth;
                float x = Mathf.Clamp01((Mathf.Abs(latDeg[i]) - (edge - 10f)) / 15f);
                belt[i] = x * x * (3f - 2f * x);
            }
            return belt;
        }

        public static float[] StormMixing(float[] latDeg, bool[] trueOcean, float[] surfaceEast, float baseK)
        {
            ZonalMean(latDeg, surfaceEast, trueOcean, out float[] centres, out float[] zmean);
            int n = latDeg.Length;
            var kTile = new float[n];
            for (int i = 0; i < n; i++)
            {
                float ubar = Interp(latDeg[i], centres, zmean);
                kTile[i] = Mathf.Min(baseK + StormKPerMs * Mathf.Max(ubar, 0f), StormKCap);
            }
            return kTile;
        }

        /// <summary>
        /// Builds an upstream-first topological sweep order from directed wind edge flows (donor -> receiver).
        /// Keeps edges that supply at least 5% of a receiver's inflow, and breaks loops by picking the unplaced
        /// tile with the fewest remaining unplaced donors (bucket queue 0..6, matching ordered_sweeps.py).
        /// </summary>
        public static int[] BuildUpwindOrder(int n, int[] donor, int[] receiver, float[] flow)
        {
            int m = donor.Length;
            var totalIn = new float[n];
            for (int e = 0; e < m; e++)
                totalIn[receiver[e]] += flow[e];

            var outStart = new int[n + 1];
            var degree = new int[n];
            for (int e = 0; e < m; e++)
            {
                int rc = receiver[e];
                if (flow[e] >= 0.05f * totalIn[rc])
                {
                    outStart[donor[e] + 1]++;
                    degree[rc]++;
                }
            }
            for (int i = 0; i < n; i++)
                outStart[i + 1] += outStart[i];

            var outReceiver = new int[outStart[n]];
            var cursor = (int[])outStart.Clone();
            for (int e = 0; e < m; e++)
            {
                int rc = receiver[e];
                if (flow[e] >= 0.05f * totalIn[rc])
                {
                    int dn = donor[e];
                    outReceiver[cursor[dn]++] = rc;
                }
            }

            var buckets = new Queue<int>[7];
            for (int b = 0; b < 7; b++)
                buckets[b] = new Queue<int>(b == 0 ? n / 4 : 1024);
            for (int i = 0; i < n; i++)
                buckets[Mathf.Clamp(degree[i], 0, 6)].Enqueue(i);

            var placed = new bool[n];
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
                        if (!placed[cand] && Mathf.Clamp(degree[cand], 0, 6) == b)
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

                placed[tile] = true;
                order[count++] = tile;
                int end = outStart[tile + 1];
                for (int k = outStart[tile]; k < end; k++)
                {
                    int dest = outReceiver[k];
                    if (!placed[dest])
                    {
                        degree[dest] = Mathf.Max(0, degree[dest] - 1);
                        buckets[Mathf.Clamp(degree[dest], 0, 6)].Enqueue(dest);
                    }
                }
            }
            return order;
        }
    }
}
