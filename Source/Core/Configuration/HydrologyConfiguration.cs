using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Planets.Core.Configuration
{
    /// <summary>
    /// User-configurable hydrology parameters and feature toggles.
    /// All fields register into <see cref="entries"/> on construction for uniform Scribe persistence, reset, modification checks, and copying.
    /// </summary>
    public class HydrologyConfiguration : IExposable
    {
        private readonly List<IConfigValue> entries = new List<IConfigValue>();

        /// <summary>When true, Scribe writes every entry including values equal to their default (used by <see cref="WorldGen.Climate.ClimateBakeRecord"/>).</summary>
        public bool ForceSaveAll;

        /// <summary>Reference rainfall (mm/year) for flow-to-drainage conversion in Tier 2 hydrology.</summary>
        public ConfigValue<float> referenceRainfallMm;

        /// <summary>Evaporation model settings for Tier 2 hydrology.</summary>
        public ConfigValue<float> evaporationMultiplier;
        public ConfigValue<float> maxEvaporationMmDay;
        public ConfigValue<float> minEvaporationMmDay;

        // ========== FEATURE TOGGLES ==========
        public ConfigValue<bool> LakeConversionEnabled;
        public ConfigValue<bool> LakeBiomeConversionEnabled;
        public ConfigValue<bool> LakeMutatorsEnabled;
        public ConfigValue<bool> CustomRiversEnabled;

        /// <summary>Allow land draining into a Lake biome tile to start a river, in addition to true-ocean mouths.</summary>
        public ConfigValue<bool> riversIntoLakes;
        /// <summary>Minimum number of land tiles a river system must span to be drawn (removes short 1–2 tile coastal rivers).</summary>
        public ConfigValue<int> minRiverLengthTiles;
        public ConfigValue<bool> WetlandMutatorsEnabled;

        public HydrologyConfiguration()
        {
            referenceRainfallMm = new ConfigValue<float>(1000f, "referenceRainfallMm", entries);
            evaporationMultiplier = new ConfigValue<float>(1.0f, "evaporationMultiplier", entries);
            maxEvaporationMmDay = new ConfigValue<float>(12f, "maxEvaporationMmDay", entries);
            minEvaporationMmDay = new ConfigValue<float>(1f, "minEvaporationMmDay", entries);

            LakeConversionEnabled = new ConfigValue<bool>(true, "LakeConversionEnabled", entries, isFeatureToggle: true);
            LakeBiomeConversionEnabled = new ConfigValue<bool>(false, "LakeBiomeConversionEnabled", entries, isFeatureToggle: true);
            LakeMutatorsEnabled = new ConfigValue<bool>(true, "LakeMutatorsEnabled", entries, isFeatureToggle: true);
            CustomRiversEnabled = new ConfigValue<bool>(true, "CustomRiversEnabled", entries, isFeatureToggle: true);
            riversIntoLakes = new ConfigValue<bool>(true, "riversIntoLakes", entries, isFeatureToggle: true);
            minRiverLengthTiles = new ConfigValue<int>(3, "minRiverLengthTiles", entries, isFeatureToggle: true);
            WetlandMutatorsEnabled = new ConfigValue<bool>(true, "WetlandMutatorsEnabled", entries, isFeatureToggle: true);
        }

        /// <summary>Reset numeric hydrology parameters edited in Advanced Climate. Feature checkboxes on the Features tab are preserved.</summary>
        public void ResetToDefaults()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].IsFeatureToggle)
                    entries[i].Reset();
            }
        }

        /// <summary>Reset feature checkboxes edited on the Mod Settings Features tab.</summary>
        public void ResetFeatureToggles()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsFeatureToggle)
                    entries[i].Reset();
            }
        }

        public void ExposeData()
        {
            for (int i = 0; i < entries.Count; i++)
                entries[i].Look(ForceSaveAll);
        }

        public bool HasModifications()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].IsFeatureToggle && entries[i].IsModified)
                    return true;
            }
            return false;
        }

        public void CopyFrom(HydrologyConfiguration source)
        {
            if (source == null)
                return;
            int count = Mathf.Min(entries.Count, source.entries.Count);
            for (int i = 0; i < count; i++)
                entries[i].CopyValueFrom(source.entries[i]);
        }

        public void CopyFeatureTogglesFrom(HydrologyConfiguration source)
        {
            if (source == null)
                return;
            int count = Mathf.Min(entries.Count, source.entries.Count);
            for (int i = 0; i < count; i++)
            {
                if (entries[i].IsFeatureToggle)
                    entries[i].CopyValueFrom(source.entries[i]);
            }
        }
    }
}
