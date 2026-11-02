using System;
using Verse;
using Planets.Core.Configuration;

namespace Planets.Presets.Biomes
{
    /// <summary>
    /// Per-biome preset storing calibration and worker/def override metadata for a single biome.
    /// Stored in <c>RealisticPlanets_BiomePresets/{defName}/{presetName}.xml</c>.
    /// </summary>
    public class PerBiomePreset : IExposable
    {
        public string defName = "";
        public string name = "Unnamed";
        public string description = "";
        public long savedDateTicks = DateTime.Now.Ticks;

        public BiomeMetadata metadata = new BiomeMetadata();

        public DateTime SavedDate => new DateTime(savedDateTicks);

        public PerBiomePreset() { }

        public PerBiomePreset(string biomeDefName, string presetName, BiomeMetadata meta, string description = "")
        {
            defName = biomeDefName ?? "";
            name = presetName ?? "Unnamed";
            this.description = description ?? "";
            metadata = meta?.Clone() ?? new BiomeMetadata();
            savedDateTicks = DateTime.Now.Ticks;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName", "");
            Scribe_Values.Look(ref name, "name", "Unnamed");
            Scribe_Values.Look(ref description, "description", "");
            Scribe_Values.Look(ref savedDateTicks, "savedDateTicks", DateTime.Now.Ticks);
            Scribe_Deep.Look(ref metadata, "metadata");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && metadata == null)
                metadata = new BiomeMetadata();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && description == null)
                description = "";
        }
    }
}
