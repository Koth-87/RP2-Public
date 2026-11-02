using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Planets.Core;
using Planets.Utilities.Serialization;

namespace Planets.Presets.Factions
{
    /// <summary>
    /// Manages saving, loading, deleting, and applying <see cref="FactionSetupPreset"/> files.
    /// Backed by <see cref="XmlPresetStore{T}"/> in <c>RealisticPlanets_FactionPresets</c>.
    /// </summary>
    public static class FactionPresetManager
    {
        private static readonly XmlPresetStore<FactionSetupPreset> Store = new XmlPresetStore<FactionSetupPreset>(
            "RealisticPlanets_FactionPresets",
            "FactionSetupPreset",
            p => p.name,
            p => p.savedDateTicks,
            p => p.ClearCache());

        public static List<FactionSetupPreset> GetAllPresets(bool forceRefresh = false) => Store.GetAll(forceRefresh);

        public static bool SavePreset(FactionSetupPreset preset) => Store.Save(preset);

        public static FactionSetupPreset LoadPreset(string name) => Store.Load(name);

        public static bool DeletePreset(string name)
        {
            return Store.Delete(name, () =>
            {
                if (Controller.Settings?.defaultFactionProfile?.CurrentValue == name)
                {
                    Controller.Settings.defaultFactionProfile.CurrentValue = null;
                    Controller.Instance?.WriteSettings();
                }
            });
        }

        public static bool PresetExists(string name) => Store.Exists(name);

        /// <summary>
        /// Applies a faction preset to the target faction list, enforcing <c>maxConfigurableAtWorldCreation</c>
        /// and <c>replacesFaction</c> constraints while respecting mod-expanded total faction caps.
        /// </summary>
        public static bool ApplyPreset(FactionSetupPreset preset, List<FactionDef> targetList)
        {
            if (preset == null || targetList == null)
                return false;

            List<FactionDef> factionDefs = preset.GetFactionDefs();
            if (factionDefs.Count == 0)
            {
                ReportDropped(preset.GetMissingFactionNames(), "Faction preset was not applied.");
                return false;
            }

            var replaced = new HashSet<FactionDef>();
            for (int i = 0; i < factionDefs.Count; i++)
            {
                if (factionDefs[i]?.replacesFaction != null)
                    replaced.Add(factionDefs[i].replacesFaction);
            }

            var accepted = new List<FactionDef>();
            var dropped = new List<string>();
            for (int i = 0; i < factionDefs.Count; i++)
            {
                FactionDef def = factionDefs[i];
                if (def == null)
                    continue;

                if (replaced.Contains(def))
                {
                    dropped.Add(def.LabelCap + " (replaced by another faction)");
                    continue;
                }

                int copies = 0;
                for (int j = 0; j < accepted.Count; j++)
                {
                    if (accepted[j] == def)
                        copies++;
                }

                if (copies >= def.maxConfigurableAtWorldCreation)
                {
                    dropped.Add(def.LabelCap + " (max " + def.maxConfigurableAtWorldCreation + ")");
                    continue;
                }

                accepted.Add(def);
            }

            foreach (string missing in preset.GetMissingFactionNames())
                dropped.Add(missing + " (missing)");

            if (accepted.Count == 0)
            {
                ReportDropped(dropped, "Faction preset was not applied.");
                return false;
            }

            targetList.Clear();
            targetList.AddRange(accepted);
            ReportDropped(dropped, "Dropped from faction preset:");
            return true;
        }

        private static void ReportDropped(List<string> dropped, string prefix)
        {
            if (dropped == null || dropped.Count == 0)
                return;

            const int shown = 8;
            string names = string.Join(", ", dropped.Take(shown));
            if (dropped.Count > shown)
                names += ", and " + (dropped.Count - shown) + " more";
            Messages.Message(prefix + " " + names + ".", MessageTypeDefOf.CautionInput, false);
        }
    }
}
