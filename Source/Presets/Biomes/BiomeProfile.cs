using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Planets.Core.Configuration;

namespace Planets.Presets.Biomes
{
    /// <summary>
    /// Global biome profile storing enabled state, frequency multiplier, and linked per-biome preset name per biome.
    /// Does not duplicate calibration/override values directly; those live in the linked <see cref="PerBiomePreset"/>.
    /// </summary>
    public class BiomeProfile : IExposable
    {
        public string name = "Unnamed";
        public string description = "";
        public long savedDateTicks = DateTime.Now.Ticks;

        public Dictionary<string, BiomeProfileEntry> entries = new Dictionary<string, BiomeProfileEntry>();
        private List<string> entryKeys;
        private List<BiomeProfileEntry> entryValues;

        public DateTime SavedDate => new DateTime(savedDateTicks);

        public BiomeProfile() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "Unnamed");
            Scribe_Values.Look(ref description, "description", "");
            Scribe_Values.Look(ref savedDateTicks, "savedDateTicks", DateTime.Now.Ticks);
            Scribe_Collections.Look(ref entries, "entries", LookMode.Value, LookMode.Deep, ref entryKeys, ref entryValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && entries == null)
                entries = new Dictionary<string, BiomeProfileEntry>();
        }

        public bool IsValid() => entries != null && entries.Count > 0;

        public string GetSummary()
        {
            int count = 0;
            if (entries != null)
            {
                foreach (var kvp in entries)
                {
                    if (!kvp.Value.Enabled)
                        continue;
                    BiomeDef def = DefDatabase<BiomeDef>.GetNamedSilentFail(kvp.Key);
                    if (def != null && UI.Dialogs.Dialog_AdvancedBiomes.ShouldShowBiome(def))
                        count++;
                }
            }
            return $"{count} enabled biome{(count != 1 ? "s" : "")}";
        }
    }

    /// <summary>
    /// Per-biome entry in a <see cref="BiomeProfile"/>: enabled toggle, frequency multiplier, legacy score offset, and linked preset name.
    /// </summary>
    public class BiomeProfileEntry : IExposable
    {
        public bool Enabled = true;
        public float CommonalityMultiplier = 1.0f;
        public float ScoreOffset = 0.0f;
        public string PresetName;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "Enabled", true);
            Scribe_Values.Look(ref CommonalityMultiplier, "CommonalityMultiplier", 1.0f);
            Scribe_Values.Look(ref ScoreOffset, "ScoreOffset", 0.0f);
            Scribe_Values.Look(ref PresetName, "PresetName", null);
        }
    }
}
