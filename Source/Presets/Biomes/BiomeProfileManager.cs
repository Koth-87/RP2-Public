using System.Collections.Generic;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.Utilities.Serialization;

namespace Planets.Presets.Biomes
{
    /// <summary>
    /// Manages saving, loading, deleting, and applying global <see cref="BiomeProfile"/> files.
    /// Backed by <see cref="XmlPresetStore{T}"/> in <c>RealisticPlanets_BiomeProfiles</c>.
    /// </summary>
    public static class BiomeProfileManager
    {
        private static readonly XmlPresetStore<BiomeProfile> Store = new XmlPresetStore<BiomeProfile>(
            "RealisticPlanets_BiomeProfiles",
            "BiomeProfile",
            p => p.name,
            p => p.savedDateTicks);

        public static List<BiomeProfile> GetAllProfiles(bool forceRefresh = false) => Store.GetAll(forceRefresh);

        public static bool SaveProfile(BiomeProfile profile) => Store.Save(profile);

        public static BiomeProfile LoadProfile(string name) => Store.Load(name);

        public static bool DeleteProfile(string name)
        {
            return Store.Delete(name, () =>
            {
                if (Controller.Settings?.defaultBiomeProfile?.CurrentValue == name)
                {
                    Controller.Settings.defaultBiomeProfile.CurrentValue = null;
                    Controller.Instance?.WriteSettings();
                }
            });
        }

        public static bool ProfileExists(string name) => Store.Exists(name);

        /// <summary>
        /// Applies a profile to a biome configuration: loads each linked per-biome preset via <see cref="BiomeMetadata.CopyCalibrationFrom"/>,
        /// then applies the profile's enabled checkbox, frequency multiplier, and preset link.
        /// </summary>
        public static void ApplyToConfig(BiomeProfile profile, BiomeConfiguration config)
        {
            if (profile?.entries == null || config == null)
                return;

            foreach (var kvp in profile.entries)
            {
                string defName = kvp.Key;
                BiomeProfileEntry entry = kvp.Value;
                if (string.IsNullOrEmpty(defName) || entry == null)
                    continue;

                BiomeMetadata meta = config.GetOrCreateMetadata(defName);
                if (!string.IsNullOrEmpty(entry.PresetName))
                {
                    PerBiomePreset preset = PerBiomePresetManager.LoadPreset(defName, entry.PresetName);
                    if (preset?.metadata != null)
                    {
                        meta.CopyCalibrationFrom(preset.metadata);
                    }
                    else
                    {
                        RPLog.W($"[Biomes] Profile preset '{entry.PresetName}' for {defName} was not found. Keeping built-in tuning.");
                        Messages.Message($"Per-biome preset '{entry.PresetName}' for {defName} was not found. Using built-in tuning.", MessageTypeDefOf.CautionInput, false);
                    }
                }

                meta.Enabled = entry.Enabled;
                meta.CommonalityMultiplier = entry.CommonalityMultiplier;
                meta.ScoreOffset = entry.ScoreOffset;
                meta.PresetName = entry.PresetName;
                meta.FoldScoreOffsetIntoFrequency();
            }
        }

        /// <summary>
        /// Resets biomes to built-in defaults, then applies either the default <see cref="BiomeProfile"/>
        /// or the per-biome default presets (<c>defaultPresetPerBiome</c>).
        /// </summary>
        public static void ApplyBiomeDefaults(BiomeConfiguration config)
        {
            if (config == null)
                return;
            config.ResetToDefaults();

            string profileName = Controller.Settings?.defaultBiomeProfile?.CurrentValue;
            if (!string.IsNullOrEmpty(profileName))
            {
                BiomeProfile profile = LoadProfile(profileName);
                if (profile != null)
                {
                    ApplyToConfig(profile, config);
                    if (Controller.DebugEnabled)
                        RPLog.D($"[Biomes] Applied default profile '{profileName}'.");
                    return;
                }

                RPLog.W($"[Biomes] Default profile '{profileName}' was not found. Using built-in values and per-biome defaults.");
                Messages.Message($"Default biome profile '{profileName}' was not found.", MessageTypeDefOf.CautionInput, false);
            }

            Dictionary<string, string> perBiome = Controller.Settings?.defaultPresetPerBiome;
            if (perBiome == null)
                return;

            foreach (var kvp in perBiome)
            {
                if (string.IsNullOrEmpty(kvp.Key) || string.IsNullOrEmpty(kvp.Value))
                    continue;

                PerBiomePreset preset = PerBiomePresetManager.LoadPreset(kvp.Key, kvp.Value);
                if (preset?.metadata == null)
                {
                    RPLog.W($"[Biomes] Default preset '{kvp.Value}' for {kvp.Key} was not found.");
                    continue;
                }

                BiomeMetadata meta = config.GetOrCreateMetadata(kvp.Key);
                meta.CopyFrom(preset.metadata);
                meta.PresetName = kvp.Value;
            }
        }
    }
}
