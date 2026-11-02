using System;
using System.IO;

namespace Planets.Utilities
{
    /// <summary>
    /// Names that can be stored as preset files on Windows.
    /// </summary>
    public static class PresetFileNames
    {
        public static bool TryValidate(string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Name is required.";
                return false;
            }

            string trimmed = name.Trim();
            if (trimmed.EndsWith(".") || char.IsWhiteSpace(trimmed[trimmed.Length - 1]))
            {
                error = "Name cannot end with a space or a dot.";
                return false;
            }

            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "Name contains a character Windows does not allow in a file name.";
                return false;
            }

            if (IsReserved(trimmed))
            {
                error = "That name is reserved by Windows.";
                return false;
            }

            error = null;
            return true;
        }

        public static string NextUnique(string stem, Func<string, bool> exists)
        {
            if (exists == null || !exists(stem))
                return stem;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = stem + " " + i;
                if (!exists(candidate))
                    return candidate;
            }

            return stem + " " + DateTime.Now.Ticks;
        }

        private static bool IsReserved(string name)
        {
            string stem = name;
            int dot = stem.IndexOf('.');
            if (dot >= 0)
                stem = stem.Substring(0, dot);
            stem = stem.Trim().ToUpperInvariant();

            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL")
                return true;

            if (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")))
            {
                char last = stem[3];
                return last >= '1' && last <= '9';
            }

            return false;
        }
    }
}
