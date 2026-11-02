using System;
using System.IO;
using UnityEngine;

namespace Planets.WorldGen.Geography
{
    /// <summary>
    /// Loads and bilinearly samples the ETOPO 2022 0.25° ice-surface elevation binary grid
    /// (<c>720 x 1440</c> little-endian int16 meters, 90°N to 90°S, 180°W eastward).
    /// </summary>
    public static class EarthHeightmap
    {
        public const int Rows = 720;
        public const int Columns = 1440;
        public const int ExpectedBytes = Rows * Columns * 2;

        private static short[] meters;
        private static string loadedPath;

        public static bool TryLoad(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "The Earth heightmap path is empty.";
                return false;
            }
            if (!File.Exists(path))
            {
                error = "The Earth heightmap file was not found: " + path;
                return false;
            }
            if (string.Equals(loadedPath, path, StringComparison.OrdinalIgnoreCase) && meters != null)
                return true;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                error = "The Earth heightmap could not be read: " + e.Message;
                return false;
            }
            if (bytes.Length != ExpectedBytes)
            {
                error = $"The Earth heightmap is {bytes.Length} bytes. Expected {ExpectedBytes} (720 by 1440 int16).";
                return false;
            }

            var values = new short[Rows * Columns];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            meters = values;
            loadedPath = path;
            return true;
        }

        public static float SampleMeters(float longitudeDeg, float latitudeDeg)
        {
            float lon = longitudeDeg;
            if (lon >= 180f)
                lon -= 360f;
            else if (lon < -180f)
                lon += 360f;
            float lat = Mathf.Clamp(latitudeDeg, -90f, 90f);

            float rowF = (90f - lat) / 0.25f - 0.5f;
            float colF = (lon + 180f) / 0.25f - 0.5f;
            int row0 = Mathf.FloorToInt(rowF);
            int col0 = Mathf.FloorToInt(colF);
            float rowT = rowF - row0;
            float colT = colF - col0;
            int row1 = row0 + 1;
            int col1 = col0 + 1;
            if (row0 < 0)
            {
                row0 = 0;
                row1 = 0;
                rowT = 0f;
            }
            else if (row1 >= Rows)
            {
                row0 = Rows - 1;
                row1 = Rows - 1;
                rowT = 0f;
            }
            col0 = FloorMod(col0, Columns);
            col1 = FloorMod(col1, Columns);

            float v00 = meters[row0 * Columns + col0];
            float v01 = meters[row0 * Columns + col1];
            float v10 = meters[row1 * Columns + col0];
            float v11 = meters[row1 * Columns + col1];
            float north = v00 + (v01 - v00) * colT;
            float south = v10 + (v11 - v10) * colT;
            return north + (south - north) * rowT;
        }

        private static int FloorMod(int value, int modulus)
        {
            int m = value % modulus;
            return m < 0 ? m + modulus : m;
        }
    }
}
