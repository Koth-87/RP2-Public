using System;
using UnityEngine;
using Verse;
using Planets.Utilities.Serialization;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Quantized gameplay-pack arrays. The codecs match BakeQuantizer.
    /// </summary>
    public static class ClimatePack
    {
        public static void LookHundredths(ref float[] values, string label)
        {
            LookSigned(ref values, label, 100f, short.MinValue, short.MaxValue);
        }

        public static void LookThousandths(ref float[] values, string label)
        {
            LookUnsigned(ref values, label, 1000f);
        }

        public static void LookMillimeters(ref float[] values, string label)
        {
            LookUnsigned(ref values, label, 1f);
        }

        public static void LookUnitByte(ref float[] values, string label)
        {
            byte[] raw = null;
            if (Scribe.mode == LoadSaveMode.Saving && values != null)
            {
                raw = new byte[values.Length];
                for (int i = 0; i < values.Length; i++)
                    raw[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(values[i] * 255f), 0, 255);
            }
            BinaryDataExposeUtility.Look(ref raw, label);
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;
            if (raw == null)
            {
                values = null;
                return;
            }
            values = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                values[i] = raw[i] / 255f;
        }

        public static void LookFlow(ref float[] values, string label)
        {
            ushort[] raw = null;
            if (Scribe.mode == LoadSaveMode.Saving && values != null)
            {
                raw = new ushort[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    double safe = 1.0 + Math.Max(0.0, values[i]);
                    int packed = (int)Math.Round(Math.Log(safe, 2.0) * 2048.0);
                    raw[i] = (ushort)Mathf.Clamp(packed, 0, ushort.MaxValue);
                }
            }
            LookUInt16(ref raw, label);
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;
            if (raw == null)
            {
                values = null;
                return;
            }
            values = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                values[i] = (float)(Math.Pow(2.0, raw[i] / 2048.0) - 1.0);
        }

        private static void LookSigned(ref float[] values, string label, float scale, int min, int max)
        {
            short[] raw = null;
            if (Scribe.mode == LoadSaveMode.Saving && values != null)
            {
                raw = new short[values.Length];
                for (int i = 0; i < values.Length; i++)
                    raw[i] = (short)Mathf.Clamp(Mathf.RoundToInt(values[i] * scale), min, max);
            }
            byte[] bytes = null;
            if (Scribe.mode == LoadSaveMode.Saving && raw != null)
            {
                bytes = new byte[raw.Length * 2];
                Buffer.BlockCopy(raw, 0, bytes, 0, bytes.Length);
            }
            BinaryDataExposeUtility.Look(ref bytes, label);
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;
            if (bytes == null)
            {
                values = null;
                return;
            }
            if (bytes.Length % 2 != 0)
            {
                Log.Error($"[RP] Climate pack '{label}' byte length {bytes.Length} is not a multiple of 2.");
                values = null;
                return;
            }
            raw = new short[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, raw, 0, bytes.Length);
            values = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                values[i] = raw[i] / scale;
        }

        private static void LookUnsigned(ref float[] values, string label, float scale)
        {
            ushort[] raw = null;
            if (Scribe.mode == LoadSaveMode.Saving && values != null)
            {
                raw = new ushort[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    int packed = Mathf.RoundToInt(values[i] * scale);
                    raw[i] = (ushort)Mathf.Clamp(packed, 0, ushort.MaxValue);
                }
            }
            LookUInt16(ref raw, label);
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;
            if (raw == null)
            {
                values = null;
                return;
            }
            values = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                values[i] = raw[i] / scale;
        }

        private static void LookUInt16(ref ushort[] raw, string label)
        {
            byte[] bytes = null;
            if (Scribe.mode == LoadSaveMode.Saving && raw != null)
            {
                bytes = new byte[raw.Length * 2];
                Buffer.BlockCopy(raw, 0, bytes, 0, bytes.Length);
            }
            BinaryDataExposeUtility.Look(ref bytes, label);
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;
            if (bytes == null)
            {
                raw = null;
                return;
            }
            if (bytes.Length % 2 != 0)
            {
                Log.Error($"[RP] Climate pack '{label}' byte length {bytes.Length} is not a multiple of 2.");
                raw = null;
                return;
            }
            raw = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, raw, 0, bytes.Length);
        }
    }
}
