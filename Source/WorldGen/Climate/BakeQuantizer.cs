using System;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Quantizes generated tile and climate values to the exact steps used by vanilla tile serialization
    /// (<see cref="SurfaceLayer"/>) and RP2's gameplay pack (<see cref="ClimatePack"/>).
    /// Temperature, rainfall, swampiness, and gameplay-pack arrays are quantized before biome selection
    /// so generated and reloaded worlds are identical. Elevation is quantized onto tiles only after Phase 2 drainage.
    /// </summary>
    public static class BakeQuantizer
    {
        public static float Elevation(float elevation, bool waterCovered)
        {
            float stored = waterCovered ? elevation : Mathf.Max(elevation, 1f);
            int raw = Mathf.Clamp(Mathf.RoundToInt((float)(stored + 8192.0)), 0, ushort.MaxValue);
            return (float)(raw - 8192);
        }

        public static float Temperature(float temperature)
        {
            int raw = Mathf.Clamp(Mathf.RoundToInt((float)((temperature + 300.0) * 10.0)), 0, ushort.MaxValue);
            return (float)(raw / 10.0 - 300.0);
        }

        public static float Rainfall(float rainfall)
        {
            return Mathf.Clamp(Mathf.RoundToInt(rainfall), 0, ushort.MaxValue);
        }

        public static float Swampiness(float swampiness)
        {
            int raw = Mathf.Clamp(Mathf.RoundToInt(swampiness * byte.MaxValue), 0, byte.MaxValue);
            return (float)raw / byte.MaxValue;
        }

        public static float SeasonalAmp(float amplitude)
        {
            int raw = Mathf.Clamp(Mathf.RoundToInt(amplitude * 100f), short.MinValue, short.MaxValue);
            return raw / 100f;
        }

        public static float Thousandths(float value)
        {
            int raw = Mathf.Clamp(Mathf.RoundToInt(value * 1000f), 0, ushort.MaxValue);
            return raw / 1000f;
        }

        public static float UnitByte(float value)
        {
            int raw = Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, byte.MaxValue);
            return raw / 255f;
        }

        public static float WholeMillimeters(float millimeters)
        {
            return Mathf.Clamp(Mathf.RoundToInt(millimeters), 0, ushort.MaxValue);
        }

        public static float FlowAccumulation(float flow)
        {
            double safe = 1.0 + Math.Max(0.0, flow);
            int raw = (int)Math.Round(Math.Log(safe, 2.0) * 2048.0);
            raw = Mathf.Clamp(raw, 0, ushort.MaxValue);
            return (float)(Math.Pow(2.0, raw / 2048.0) - 1.0);
        }

        public static void QuantizeElevations(PlanetLayer layer, float[] elevationData)
        {
            if (layer?.Tiles == null)
                return;

            int count = layer.Tiles.Count;
            for (int i = 0; i < count; i++)
            {
                Tile tile = layer.Tiles[i];
                if (tile == null)
                    continue;
                bool water = tile.WaterCovered;
                float quantized = Elevation(tile.elevation, water);
                tile.elevation = quantized;
                if (elevationData != null && i < elevationData.Length)
                    elevationData[i] = quantized;
            }
        }

        public static void QuantizeAppliedClimate(PlanetLayer layer, ClimateData climate)
        {
            if (layer?.Tiles == null || climate == null)
                return;

            int count = layer.Tiles.Count;
            for (int i = 0; i < count; i++)
            {
                Tile tile = layer.Tiles[i];
                if (tile == null)
                    continue;

                tile.temperature = Temperature(tile.temperature);
                tile.rainfall = Rainfall(tile.rainfall);
                tile.swampiness = Swampiness(tile.swampiness);

                Write(climate.AnnualMeanTemp, i, tile.temperature);
                Write(climate.AnnualRainfall, i, tile.rainfall);
                Write(climate.Swampiness, i, tile.swampiness);
            }

            QuantizeGameplayPackArrays(climate);
        }

        /// <summary>Quantizes the gameplay-pack arrays on <paramref name="climate"/> and refreshes summer/winter extremes.</summary>
        public static void QuantizeGameplayPackArrays(ClimateData climate)
        {
            if (climate == null)
                return;

            Quantize(climate.SeasonalAmp, SeasonalAmp);
            Quantize(climate.AridityIndex, Thousandths);
            Quantize(climate.Continentality, UnitByte);
            Quantize(climate.UpwindReliefIndex, Thousandths);
            Quantize(climate.LeewardShadowIndex, Thousandths);
            Quantize(climate.MonsoonBoost, WholeMillimeters);
            Quantize(climate.DriestSeasonRainMm, WholeMillimeters);
            Quantize(climate.WettestSeasonRainMm, WholeMillimeters);
            Quantize(climate.FlowAccumulationTopology, FlowAccumulation);

            if (climate.AnnualMeanTemp == null || climate.SeasonalAmp == null)
                return;
            int n = Math.Min(climate.AnnualMeanTemp.Length, climate.SeasonalAmp.Length);
            for (int i = 0; i < n; i++)
            {
                float half = climate.SeasonalAmp[i] / 2f;
                Write(climate.MaxSummerTemp, i, climate.AnnualMeanTemp[i] + half);
                Write(climate.MinWinterTemp, i, climate.AnnualMeanTemp[i] - half);
            }
        }

        public static void QuantizeRainfallTile(Tile tile, float[] annualRainfall, int tileId)
        {
            if (tile == null)
                return;
            tile.rainfall = Rainfall(tile.rainfall);
            Write(annualRainfall, tileId, tile.rainfall);
        }

        private static void Quantize(float[] values, Func<float, float> encode)
        {
            if (values == null)
                return;
            for (int i = 0; i < values.Length; i++)
                values[i] = encode(values[i]);
        }

        private static void Write(float[] values, int index, float value)
        {
            if (values != null && index >= 0 && index < values.Length)
                values[index] = value;
        }
    }
}
