using System.Collections.Generic;
using System.IO;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.Utilities.Serialization;

namespace Planets.Presets.Biomes
{
    /// <summary>
    /// Manages per-biome presets stored in <c>RealisticPlanets_BiomePresets/{defName}/{presetName}.xml</c>.
    /// Uses <see cref="XmlPresetStore{T}"/> per biome subfolder.
    /// </summary>
    public static class PerBiomePresetManager
    {
        private static readonly string BasePath = Path.Combine(GenFilePaths.ConfigFolderPath, "RealisticPlanets_BiomePresets");
        private static readonly Dictionary<string, XmlPresetStore<PerBiomePreset>> Stores = new Dictionary<string, XmlPresetStore<PerBiomePreset>>();

        private static XmlPresetStore<PerBiomePreset> GetStore(string defName)
        {
            string safeDef = XmlPresetStore<PerBiomePreset>.SanitizeFileName(defName);
            if (!Stores.TryGetValue(safeDef, out XmlPresetStore<PerBiomePreset> store))
            {
                string folder = Path.Combine(BasePath, safeDef);
                store = new XmlPresetStore<PerBiomePreset>(
                    folder,
                    "PerBiomePreset",
                    p => p.name,
                    p => p.savedDateTicks,
                    null,
                    isFullPath: true);
                Stores[safeDef] = store;
            }
            return store;
        }

        public static List<PerBiomePreset> GetPresetsForBiome(string defName, bool forceRefresh = false)
        {
            if (string.IsNullOrEmpty(defName))
                return new List<PerBiomePreset>();
            return GetStore(defName).GetAll(forceRefresh);
        }

        public static bool SavePreset(string defName, string presetName, BiomeMetadata metadata, string description = "")
        {
            if (string.IsNullOrEmpty(defName) || string.IsNullOrEmpty(presetName) || metadata == null)
            {
                RPLog.Error("Cannot save per-biome preset: missing defName, presetName, or metadata");
                return false;
            }

            PerBiomePreset preset = new PerBiomePreset(defName, presetName, metadata, description);
            return GetStore(defName).Save(preset);
        }

        public static PerBiomePreset LoadPreset(string defName, string presetName)
        {
            if (string.IsNullOrEmpty(defName) || string.IsNullOrEmpty(presetName))
                return null;
            return GetStore(defName).Load(presetName);
        }

        public static bool DeletePreset(string defName, string presetName)
        {
            if (string.IsNullOrEmpty(defName) || string.IsNullOrEmpty(presetName))
                return false;

            return GetStore(defName).Delete(presetName, () =>
            {
                if (Controller.Settings?.defaultPresetPerBiome != null
                    && Controller.Settings.defaultPresetPerBiome.TryGetValue(defName, out string current)
                    && current == presetName)
                {
                    Controller.Settings.defaultPresetPerBiome.Remove(defName);
                    Controller.Instance?.WriteSettings();
                }
            });
        }

        public static bool PresetExists(string defName, string presetName)
        {
            if (string.IsNullOrEmpty(defName) || string.IsNullOrEmpty(presetName))
                return false;
            string safeDef = XmlPresetStore<PerBiomePreset>.SanitizeFileName(defName);
            string folder = Path.Combine(BasePath, safeDef);
            if (!Directory.Exists(folder))
                return false;
            return GetStore(defName).Exists(presetName);
        }
    }
}
