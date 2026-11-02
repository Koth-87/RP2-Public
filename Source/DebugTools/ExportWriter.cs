using System;
using System.IO;
using System.Text;
using RimWorld;
using Verse;
using Planets.Utilities.Logging;

namespace Planets.DebugTools
{
    /// <summary>
    /// Centralized export framework for Realistic Planets debug data.
    /// Ensures consistent formatting and atomic writes. Complete-world dumps are not row-capped.
    /// </summary>
    public static class ExportWriter
    {
        /// <summary>
        /// Optional cap for short summary exports. Complete-world dumps (raw tile data and biome scoring) write every tile.
        /// </summary>
        public const int MaxLines = 1000;
        
        /// <summary>
        /// Export directory path.
        /// </summary>
        public static string ExportPath => Path.Combine(
            GenFilePaths.SaveDataFolderPath,
            "RealisticPlanets_Exports"
        );

        /// <summary>
        /// Write all content to file with header and atomic write.
        /// </summary>
        public static void WriteAll(string filename, StringBuilder sb, bool addHeader = true)
        {
            try
            {
                Directory.CreateDirectory(ExportPath);
                
                var fullContent = new StringBuilder();
                
                bool isCSV = filename.Contains(".csv");
                if (addHeader && !isCSV)
                {
                    AppendHeader(fullContent);
                    fullContent.AppendLine();
                }
                
                fullContent.Append(sb);
                
                string fullPath = Path.Combine(ExportPath, filename);
                string tempPath = fullPath + ".tmp";
                
                File.WriteAllText(tempPath, fullContent.ToString());
                
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
                File.Move(tempPath, fullPath);
                
                RPLog.Export($"Exported {filename} ({fullContent.Length} bytes)");
            }
            catch (Exception e)
            {
                RPLog.E($"Failed to export {filename}: {e.Message}");
            }
        }

        /// <summary>
        /// Add standard header to exports.
        /// </summary>
        private static void AppendHeader(StringBuilder sb)
        {
            var world = Find.World;
            string seed = world?.info?.seedString ?? "unknown";
            
            var mapSize = world?.info?.initialMapSize;
            string size = ((mapSize?.x ?? 0) > 0 && (mapSize?.z ?? 0) > 0)
                ? $"{mapSize.Value.x}x{mapSize.Value.z}"
                : "unknown";
            
            string version = typeof(ExportWriter).Assembly.GetName().Version?.ToString() ?? "unknown";
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            
            sb.AppendLine($"# Realistic Planets v{version} Export");
            sb.AppendLine($"# Seed: {seed} | World: {size} | Generated: {timestamp}");
            sb.AppendLine($"# =================================================================");
        }

        /// <summary>
        /// Add section header.
        /// </summary>
        public static void AppendSection(StringBuilder sb, string title)
        {
            sb.AppendLine();
            sb.AppendLine($"[{title}]");
            sb.AppendLine(new string('-', Math.Min(title.Length + 2, 80)));
        }

        /// <summary>
        /// Generate deterministic filename based on seed.
        /// Extension can be compound like "csv.txt" for CSV data in text files.
        /// </summary>
        public static string GetFilename(string prefix, string extension = "txt")
        {
            string seed = Find.World?.info?.seedString ?? "noseed";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                seed = seed.Replace(c, '_');
            }
            return $"{prefix}_{seed}.{extension}";
        }

        /// <summary>
        /// Enforce row cap on output with truncation message.
        /// </summary>
        public static void EnforceRowCap(StringBuilder sb, int currentLines)
        {
            if (currentLines >= MaxLines)
            {
                sb.AppendLine($"... truncated at {MaxLines} rows (actual: {currentLines})");
            }
        }

        /// <summary>
        /// Check if we should continue adding rows.
        /// </summary>
        public static bool ShouldContinue(int currentLines)
        {
            return currentLines < MaxLines;
        }

        /// <summary>Open a temporary CSV. Call Finish after the writer is closed.</summary>
        public static StreamWriter OpenTemp(string filename, out string tempPath, out string finalPath)
        {
            Directory.CreateDirectory(ExportPath);
            finalPath = Path.Combine(ExportPath, filename);
            tempPath = finalPath + ".tmp";
            return new StreamWriter(tempPath, false);
        }

        /// <summary>Replace the export file with the finished temporary file.</summary>
        public static void FinishTemp(string tempPath, string finalPath)
        {
            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(tempPath, finalPath);
            RPLog.Export($"Exported {Path.GetFileName(finalPath)}");
        }

        /// <summary>
        /// Open export directory in file explorer (Windows).
        /// </summary>
        public static void OpenExportDirectory()
        {
            try
            {
                Directory.CreateDirectory(ExportPath);
                System.Diagnostics.Process.Start("explorer.exe", ExportPath);
            }
            catch (Exception e)
            {
                RPLog.W($"Could not open export directory: {e.Message}");
            }
        }
    }
}
