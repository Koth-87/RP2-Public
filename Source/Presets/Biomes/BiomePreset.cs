using System;
using System.Collections.Generic;
using Verse;
using Planets.Core.Configuration;

namespace Planets.Presets.Biomes
{
    /// <summary>
    /// Legacy whole-config biome preset model retained for migrating older presets into
    /// <see cref="BiomeProfile"/> and <see cref="PerBiomePreset"/> files.
    /// </summary>
    public class BiomePreset : IExposable
    {
        public string name = "Unnamed";
        public string description = "";
        public long savedDateTicks = DateTime.Now.Ticks;

        public Dictionary<string, BiomeMetadata> biomeMetadata = new Dictionary<string, BiomeMetadata>();
        private List<string> metadataKeys;
        private List<BiomeMetadata> metadataValues;

        private Dictionary<string, BiomeConfiguration.BiomeModifier> biomeModifiers;
        private List<string> modifierKeys;
        private List<BiomeConfiguration.BiomeModifier> modifierValues;

        public DateTime SavedDate => new DateTime(savedDateTicks);

        public BiomePreset() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "Unnamed");
            Scribe_Values.Look(ref description, "description", "");
            Scribe_Values.Look(ref savedDateTicks, "savedDateTicks", DateTime.Now.Ticks);
            Scribe_Collections.Look(ref biomeMetadata, "biomeMetadata", LookMode.Value, LookMode.Deep, ref metadataKeys, ref metadataValues);
            Scribe_Collections.Look(ref biomeModifiers, "biomeModifiers", LookMode.Value, LookMode.Deep, ref modifierKeys, ref modifierValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (biomeMetadata == null)
                    biomeMetadata = new Dictionary<string, BiomeMetadata>();
                if (biomeModifiers != null && biomeModifiers.Count > 0)
                {
                    foreach (var kvp in biomeModifiers)
                    {
                        if (!biomeMetadata.ContainsKey(kvp.Key))
                            biomeMetadata[kvp.Key] = BiomeMetadata.FromModifier(kvp.Value);
                    }
                    biomeModifiers = null;
                }
            }
        }

        public bool IsValid() => biomeMetadata != null && biomeMetadata.Count > 0;

        public string GetSummary()
        {
            int count = biomeMetadata?.Count ?? 0;
            return $"{count} configured biome{(count != 1 ? "s" : "")}";
        }

        public void CaptureFrom(BiomeConfiguration config)
        {
            if (config?.BiomeMetadataMap == null)
                return;
            biomeMetadata.Clear();
            foreach (var kvp in config.BiomeMetadataMap)
                biomeMetadata[kvp.Key] = kvp.Value.Clone();
            savedDateTicks = DateTime.Now.Ticks;
        }

        public void ApplyTo(BiomeConfiguration config)
        {
            if (config == null)
                return;
            foreach (var kvp in biomeMetadata)
                config.BiomeMetadataMap[kvp.Key] = kvp.Value.Clone();
        }
    }
}
