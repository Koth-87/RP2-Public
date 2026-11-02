using System;
using System.Collections.Generic;
using System.IO;
using Verse;
using Planets.Utilities.Logging;
using Planets.Utilities.Serialization;

namespace Planets.Presets.Biomes
{
    /// <summary>
    /// Manages legacy whole-config biome presets stored in <c>RealisticPlanets_BiomePresets_Legacy</c>.
    /// Automatically migrates flat XML files out of <c>RealisticPlanets_BiomePresets</c> at startup.
    /// </summary>
    public static class BiomePresetManager
    {
        private static readonly string PresetFolderPath = Path.Combine(GenFilePaths.ConfigFolderPath, "RealisticPlanets_BiomePresets_Legacy");
        private static readonly string SharedFolderPath = Path.Combine(GenFilePaths.ConfigFolderPath, "RealisticPlanets_BiomePresets");

        private static readonly XmlPresetStore<BiomePreset> Store;

        static BiomePresetManager()
        {
            MigrateSharedFolder();
            Store = new XmlPresetStore<BiomePreset>(
                "RealisticPlanets_BiomePresets_Legacy",
                "BiomePreset",
                p => p.name,
                p => p.savedDateTicks);
        }

        /// <summary>
        /// Moves flat legacy XML files out of the per-biome preset root folder into <c>RealisticPlanets_BiomePresets_Legacy</c>.
        /// </summary>
        public static void MigrateSharedFolder()
        {
            try
            {
                DirectoryInfo source = new DirectoryInfo(SharedFolderPath);
                if (!source.Exists)
                    return;

                FileInfo[] files = source.GetFiles("*.xml");
                if (files.Length == 0)
                    return;

                Directory.CreateDirectory(PresetFolderPath);
                foreach (FileInfo file in files)
                {
                    string originalName = file.Name;
                    string dest = Path.Combine(PresetFolderPath, originalName);
                    int suffix = 2;
                    while (File.Exists(dest))
                    {
                        dest = Path.Combine(PresetFolderPath, Path.GetFileNameWithoutExtension(originalName) + "_" + suffix + file.Extension);
                        suffix++;
                    }

                    try
                    {
                        file.MoveTo(dest);
                        RPLog.W($"[Presets] Moved legacy biome preset '{originalName}' to RealisticPlanets_BiomePresets_Legacy.");
                    }
                    catch (Exception e)
                    {
                        RPLog.Error($"Failed to move legacy biome preset '{originalName}': {e}");
                    }
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"Failed to migrate legacy biome presets: {e}");
            }
        }

        public static List<BiomePreset> GetAllPresets(bool forceRefresh = false) => Store.GetAll(forceRefresh);

        public static bool SavePreset(BiomePreset preset) => Store.Save(preset);

        public static BiomePreset LoadPreset(string name) => Store.Load(name);

        public static bool DeletePreset(string name) => Store.Delete(name);

        public static bool PresetExists(string name) => Store.Exists(name);
    }
}
