using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using System.IO;
using System.IO.Compression;

namespace Planets.Utilities.Serialization
{
    /// <summary>
    /// Utility for efficient binary serialization of large arrays in RimWorld.
    /// 
    /// Problem: RimWorld's Scribe_Collections saves arrays as XML lists, which is verbose (400MB+ for large worlds).
    /// Solution: Convert arrays to compressed Base64 strings (binary serialization).
    /// 
    /// Performance:
    /// - File size: ~10-20x smaller (30MB vs 400MB) -> With compression: ~5-10MB
    /// - Save/Load time: ~10x faster (1s vs 15s)
    /// - Memory: Uses Buffer.BlockCopy for near-instant memory transfer
    /// </summary>
    public static class BinaryDataExposeUtility
    {
        // ========== COMPRESSION HELPERS ==========

        private static byte[] Compress(byte[] data)
        {
            if (data == null) return null;
            using (var memoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                {
                    gzipStream.Write(data, 0, data.Length);
                }
                return memoryStream.ToArray();
            }
        }

        private static byte[] Decompress(byte[] compressedData)
        {
            if (compressedData == null) return null;
            using (var inputStream = new MemoryStream(compressedData))
            using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = gzipStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    outputStream.Write(buffer, 0, bytesRead);
                }
                return outputStream.ToArray();
            }
        }

        // ========== PRIMITIVE ARRAYS (Fastest - BlockCopy) ==========

        public static void Look(ref float[] array, string label)
        {
            byte[] bytes = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (array != null)
                {
                    bytes = new byte[array.Length * 4];
                    Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
                }
            }

            LookBytes(ref bytes, label);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (bytes == null)
                {
                    array = null;
                }
                else if (bytes.Length % 4 != 0)
                {
                    Log.Error($"[RP] Climate array '{label}' byte length {bytes.Length} is not a multiple of 4.");
                    array = null;
                }
                else
                {
                    array = new float[bytes.Length / 4];
                    Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
                }
            }
        }

        public static void Look(ref int[] array, string label)
        {
            byte[] bytes = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (array != null)
                {
                    bytes = new byte[array.Length * 4];
                    Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
                }
            }

            LookBytes(ref bytes, label);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (bytes == null)
                {
                    array = null;
                }
                else if (bytes.Length % 4 != 0)
                {
                    Log.Error($"[RP] Climate array '{label}' byte length {bytes.Length} is not a multiple of 4.");
                    array = null;
                }
                else
                {
                    array = new int[bytes.Length / 4];
                    Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
                }
            }
        }
        
        /// <summary>
        /// Handles the low-level byte array serialization using Base64 string conversion.
        /// This bypasses Scribe's native handling which can be flaky for raw byte arrays.
        /// Updated to support GZip compression for significant file size reduction.
        /// </summary>
        private static void LookBytes(ref byte[] bytes, string label)
        {
            string base64Compressed = null;
            string base64Legacy = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (bytes != null)
                {
                    // Compress by default
                    byte[] compressed = Compress(bytes);
                    if (compressed != null)
                    {
                        base64Compressed = Convert.ToBase64String(compressed);
                    }
                }
                // Save with _z suffix to indicate compressed data
                Scribe_Values.Look(ref base64Compressed, label + "_z");
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Try loading compressed first
                Scribe_Values.Look(ref base64Compressed, label + "_z");
                
                if (!string.IsNullOrEmpty(base64Compressed))
                {
                    try
                    {
                        byte[] compressed = Convert.FromBase64String(base64Compressed);
                        bytes = Decompress(compressed);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[RP] Failed to decompress data for {label}: {ex.Message}");
                        bytes = null;
                    }
                }
                else
                {
                    // Fallback to legacy uncompressed
                    Scribe_Values.Look(ref base64Legacy, label);
                    
                    if (!string.IsNullOrEmpty(base64Legacy))
                    {
                        try
                        {
                            bytes = Convert.FromBase64String(base64Legacy);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[RP] Failed to parse legacy Base64 data for {label}: {ex.Message}");
                            bytes = null;
                        }
                    }
                    else
                    {
                        bytes = null;
                    }
                }
            }
        }
        
        public static void Look(ref byte[] array, string label)
        {
            LookBytes(ref array, label);
        }

        // ========== BOOL ARRAYS (Bit Packing - 8x Compression) ==========

        public static void Look(ref bool[] array, string label)
        {
            byte[] bytes = null;
            int length = 0;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (array != null)
                {
                    length = array.Length;
                    // Pack 8 bools per byte
                    bytes = new byte[(length + 7) / 8];
                    for (int i = 0; i < length; i++)
                    {
                        if (array[i])
                        {
                            bytes[i / 8] |= (byte)(1 << (i % 8));
                        }
                    }
                }
            }

            // We need to save the length separately because the byte array doesn't tell us 
            // the exact number of bools (it's rounded up to nearest 8)
            Scribe_Values.Look(ref length, label + "_len", 0);
            LookBytes(ref bytes, label);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (bytes != null && length > 0)
                {
                    int bitCount = bytes.Length * 8;
                    if (length > bitCount)
                        length = bitCount;
                    array = new bool[length];
                    for (int i = 0; i < length; i++)
                    {
                        // Check if bit is set
                        if ((bytes[i / 8] & (1 << (i % 8))) != 0)
                        {
                            array[i] = true;
                        }
                    }
                }
            }
        }

        // ========== STRUCT ARRAYS (Vector3) ==========

        public static void Look(ref Vector3[] array, string label)
        {
            byte[] bytes = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (array != null)
                {
                    // Vector3 is 3 floats (12 bytes)
                    // We can't use BlockCopy directly on struct arrays safely in all Unity versions/platforms
                    // So we decompose to float array for safety
                    float[] floats = new float[array.Length * 3];
                    for (int i = 0; i < array.Length; i++)
                    {
                        floats[i * 3] = array[i].x;
                        floats[i * 3 + 1] = array[i].y;
                        floats[i * 3 + 2] = array[i].z;
                    }
                    
                    bytes = new byte[floats.Length * 4];
                    Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
                }
            }

            LookBytes(ref bytes, label);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (bytes != null)
                {
                    float[] floats = new float[bytes.Length / 4];
                    Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
                    
                    array = new Vector3[floats.Length / 3];
                    for (int i = 0; i < array.Length; i++)
                    {
                        array[i].x = floats[i * 3];
                        array[i].y = floats[i * 3 + 1];
                        array[i].z = floats[i * 3 + 2];
                    }
                }
            }
        }

        // ========== ENUM ARRAYS (Generic Wrapper) ==========

        /// <summary>
        /// Serializes an enum array by casting to int[] or byte[].
        /// Usage: LookEnum(ref myEnumArray, "label");
        /// </summary>
        public static void LookEnum<T>(ref T[] array, string label) where T : Enum
        {
            // We use int[] as the intermediate format for enums (safe for most enums)
            // Ideally we'd use the underlying type, but simple generic handling is cleaner
            
            int[] ints = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (array != null)
                {
                    ints = new int[array.Length];
                    for (int i = 0; i < array.Length; i++)
                    {
                        ints[i] = Convert.ToInt32(array[i]);
                    }
                }
            }

            // Delegate to the optimized int[] handler
            Look(ref ints, label);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (ints != null)
                {
                    array = new T[ints.Length];
                    for (int i = 0; i < ints.Length; i++)
                    {
                        array[i] = (T)Enum.ToObject(typeof(T), ints[i]);
                    }
                }
            }
        }
    }
}
