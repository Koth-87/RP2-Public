using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Biomes.Core
{
    /// <summary>
    /// Peak score for a foreign biome worker. The probe uses synthetic tiles so it never writes the world.
    /// The observed maximum is this world's best score, for workers that read cave depth or other world data.
    /// </summary>
    public static class ForeignPeaks
    {
        public struct Record
        {
            public float ProbePeak;
            public float ObservedPeak;
            public float PeakTemperature;
            public float PeakRainfall;
            public float PeakElevation;
            public float HalfTempMin;
            public float HalfTempMax;
            public float HalfRainMin;
            public float HalfRainMax;
            public float HalfElevMin;
            public float HalfElevMax;
            public bool ProbeStoppedEarly;
        }

        private static readonly Dictionary<string, Record> probes = new Dictionary<string, Record>();

        private static readonly float[] Temperatures = BuildRange(-45f, 45f, 3f);
        private static readonly float[] Rainfalls = { 0f, 100f, 200f, 400f, 700f, 1000f, 1500f, 2000f, 3000f, 4000f, 6000f };
        private static readonly float[] LandElevations = { 5f, 300f, 800f, 1500f, 2500f, 4000f };
        private static readonly float[] WaterElevations = { -50f, -200f };
        private static readonly Hilliness[] Hillinesses = { Hilliness.Flat, Hilliness.LargeHills, Hilliness.Impassable };
        private static readonly float[] Swampinesses = { 0f, 0.8f };

        public static float Peak(string defName)
        {
            if (defName != null && probes.TryGetValue(defName, out Record record))
                return record.ObservedPeak > 0f ? record.ObservedPeak : record.ProbePeak;
            return 0f;
        }

        public static bool TryGet(string defName, out Record record)
        {
            if (defName != null && probes.TryGetValue(defName, out record))
                return true;
            record = default;
            return false;
        }

        public static Record ProbeOrGet(BiomeDef def, BiomeWorker worker, PlanetLayer layer = null)
        {
            if (def == null || worker == null)
                return default;
            if (probes.TryGetValue(def.defName, out Record existing) && existing.ProbePeak > 0f)
                return existing;
            layer ??= Find.WorldGrid?.Surface;
            if (layer == null)
                return default;
            Record record = Probe(def, worker, layer);
            probes[def.defName] = record;
            return record;
        }

        /// <summary>Probe once per def for this process, then take this world's 95th-percentile positive score (falling back to the synthetic probe peak).</summary>
        public static float Resolve(BiomeDef def, BiomeWorker worker, PlanetLayer layer)
        {
            if (def == null || worker == null)
                return 0f;

            if (!probes.TryGetValue(def.defName, out Record record))
            {
                record = Probe(def, worker, layer);
                probes[def.defName] = record;
            }

            float observed = Observe(def, worker, layer);
            record.ObservedPeak = observed;
            probes[def.defName] = record;

            if (Controller.DebugEnabled)
            {
                RPLog.D($"[ForeignPeaks] {def.defName}: probe {record.ProbePeak:F1} at {record.PeakTemperature:F0} C, {record.PeakRainfall:F0} mm, {record.PeakElevation:F0} m; observed p95 {observed:F1}"
                    + (record.ProbeStoppedEarly ? "; probe stopped after exceptions" : ""));
            }

            return observed > 0f ? observed : record.ProbePeak;
        }

        private static Record Probe(BiomeDef def, BiomeWorker worker, PlanetLayer layer)
        {
            Record record = new Record
            {
                HalfTempMin = float.MaxValue,
                HalfTempMax = float.MinValue,
                HalfRainMin = float.MaxValue,
                HalfRainMax = float.MinValue,
                HalfElevMin = float.MaxValue,
                HalfElevMax = float.MinValue
            };

            int[] anchors = PickAnchors(layer);
            bool scoresOnWater = ScoresOnWater(def, worker, layer, anchors);
            float[] elevations = scoresOnWater ? Combine(LandElevations, WaterElevations) : LandElevations;

            int exceptions = 0;
            foreach (int anchor in anchors)
            {
                if (anchor < 0)
                    continue;
                PlanetTile planetTile = new PlanetTile(anchor, layer);
                BiomeDef anchorBiome = layer.Tiles[anchor]?.biome;
                for (int t = 0; t < Temperatures.Length; t++)
                for (int r = 0; r < Rainfalls.Length; r++)
                for (int e = 0; e < elevations.Length; e++)
                for (int h = 0; h < Hillinesses.Length; h++)
                for (int s = 0; s < Swampinesses.Length; s++)
                {
                    float score;
                    try
                    {
                        SurfaceTile synthetic = new SurfaceTile(planetTile);
                        synthetic.elevation = elevations[e];
                        synthetic.temperature = Temperatures[t];
                        synthetic.rainfall = Rainfalls[r];
                        synthetic.swampiness = Swampinesses[s];
                        synthetic.hilliness = Hillinesses[h];
                        synthetic.PrimaryBiome = anchorBiome;
                        score = worker.GetScore(def, synthetic, planetTile);
                    }
                    catch (Exception ex)
                    {
                        exceptions++;
                        if (exceptions == 1)
                            RPLog.W($"[ForeignPeaks] {def.defName} probe failed: {ex.Message}");
                        if (exceptions >= 20)
                        {
                            record.ProbeStoppedEarly = true;
                            FinishHalfRange(ref record);
                            return record;
                        }
                        continue;
                    }

                    if (score > record.ProbePeak)
                    {
                        record.ProbePeak = score;
                        record.PeakTemperature = Temperatures[t];
                        record.PeakRainfall = Rainfalls[r];
                        record.PeakElevation = elevations[e];
                    }
                }
            }

            if (record.ProbePeak > 0f)
                MeasureHalfPeak(def, worker, layer, anchors, elevations, ref record);
            FinishHalfRange(ref record);
            return record;
        }

        private static void MeasureHalfPeak(BiomeDef def, BiomeWorker worker, PlanetLayer layer, int[] anchors, float[] elevations, ref Record record)
        {
            float bar = record.ProbePeak * 0.5f;
            foreach (int anchor in anchors)
            {
                if (anchor < 0)
                    continue;
                PlanetTile planetTile = new PlanetTile(anchor, layer);
                BiomeDef anchorBiome = layer.Tiles[anchor]?.biome;
                for (int t = 0; t < Temperatures.Length; t++)
                for (int r = 0; r < Rainfalls.Length; r++)
                for (int e = 0; e < elevations.Length; e++)
                for (int h = 0; h < Hillinesses.Length; h++)
                for (int s = 0; s < Swampinesses.Length; s++)
                {
                    float score;
                    try
                    {
                        SurfaceTile synthetic = new SurfaceTile(planetTile);
                        synthetic.elevation = elevations[e];
                        synthetic.temperature = Temperatures[t];
                        synthetic.rainfall = Rainfalls[r];
                        synthetic.swampiness = Swampinesses[s];
                        synthetic.hilliness = Hillinesses[h];
                        synthetic.PrimaryBiome = anchorBiome;
                        score = worker.GetScore(def, synthetic, planetTile);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (score < bar)
                        continue;
                    record.HalfTempMin = Mathf.Min(record.HalfTempMin, Temperatures[t]);
                    record.HalfTempMax = Mathf.Max(record.HalfTempMax, Temperatures[t]);
                    record.HalfRainMin = Mathf.Min(record.HalfRainMin, Rainfalls[r]);
                    record.HalfRainMax = Mathf.Max(record.HalfRainMax, Rainfalls[r]);
                    record.HalfElevMin = Mathf.Min(record.HalfElevMin, elevations[e]);
                    record.HalfElevMax = Mathf.Max(record.HalfElevMax, elevations[e]);
                }
            }
        }

        private static bool ScoresOnWater(BiomeDef def, BiomeWorker worker, PlanetLayer layer, int[] anchors)
        {
            foreach (int anchor in anchors)
            {
                if (anchor < 0)
                    continue;
                try
                {
                    PlanetTile planetTile = new PlanetTile(anchor, layer);
                    SurfaceTile synthetic = new SurfaceTile(planetTile);
                    synthetic.elevation = -50f;
                    synthetic.temperature = 0f;
                    synthetic.rainfall = 500f;
                    synthetic.hilliness = Hilliness.Flat;
                    synthetic.swampiness = 0f;
                    synthetic.PrimaryBiome = layer.Tiles[anchor]?.biome;
                    if (worker.GetScore(def, synthetic, planetTile) > 0f)
                        return true;
                }
                catch (Exception)
                {
                }
            }
            return false;
        }

        private static float Observe(BiomeDef def, BiomeWorker worker, PlanetLayer layer)
        {
            int count = layer?.Tiles?.Count ?? 0;
            if (count <= 0)
                return 0f;

            var positiveScores = new List<float>(256);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    float score = worker.GetScore(def, layer.Tiles[i], new PlanetTile(i, layer));
                    if (score > 0f)
                        positiveScores.Add(score);
                }
                catch (Exception ex)
                {
                    RPLog.W($"[ForeignPeaks] {def.defName} observed scan stopped: {ex.Message}");
                    break;
                }
            }
            if (positiveScores.Count == 0)
                return 0f;
            positiveScores.Sort();
            int idx95 = Mathf.Clamp((int)((positiveScores.Count - 1) * 0.95f), 0, positiveScores.Count - 1);
            return positiveScores[idx95];
        }

        private static int[] PickAnchors(PlanetLayer layer)
        {
            return new[]
            {
                ClosestLand(layer, 10f),
                ClosestLand(layer, 40f),
                ClosestLand(layer, 65f)
            };
        }

        private static int ClosestLand(PlanetLayer layer, float targetLatitude)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            WorldGrid grid = Find.WorldGrid;
            int count = layer?.Tiles?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                Tile tile = layer.Tiles[i];
                if (tile == null || tile.elevation <= 0f || grid == null)
                    continue;
                float distance = Mathf.Abs(Mathf.Abs(grid.LongLatOf(i).y) - targetLatitude);
                if (distance < bestDistance || (distance == bestDistance && (best < 0 || i < best)))
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        private static float[] BuildRange(float from, float to, float step)
        {
            int count = Mathf.RoundToInt((to - from) / step) + 1;
            float[] values = new float[count];
            for (int i = 0; i < count; i++)
                values[i] = from + step * i;
            return values;
        }

        private static float[] Combine(float[] first, float[] second)
        {
            float[] values = new float[first.Length + second.Length];
            first.CopyTo(values, 0);
            second.CopyTo(values, first.Length);
            return values;
        }

        private static void FinishHalfRange(ref Record record)
        {
            if (record.HalfTempMin == float.MaxValue)
            {
                record.HalfTempMin = record.PeakTemperature;
                record.HalfTempMax = record.PeakTemperature;
                record.HalfRainMin = record.PeakRainfall;
                record.HalfRainMax = record.PeakRainfall;
                record.HalfElevMin = record.PeakElevation;
                record.HalfElevMax = record.PeakElevation;
            }
        }
    }
}
