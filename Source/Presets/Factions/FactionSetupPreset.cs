using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Planets.Presets.Factions
{
    /// <summary>
    /// Stores a named faction setup configuration for world creation, validating faction defs against active mods.
    /// </summary>
    public class FactionSetupPreset : IExposable
    {
        public string name = "Unnamed";
        public string description = "";
        public long savedDateTicks = DateTime.Now.Ticks;
        public List<string> factionDefNames = new List<string>();

        private bool? cachedIsValid;
        private List<FactionDef> cachedFactionDefs;

        public DateTime SavedDate => new DateTime(savedDateTicks);

        public FactionSetupPreset() { }

        public FactionSetupPreset(string name, List<FactionDef> factions, string description = "")
        {
            this.name = name;
            this.description = description;
            savedDateTicks = DateTime.Now.Ticks;
            factionDefNames = factions?.Select(f => f.defName).ToList() ?? new List<string>();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "Unnamed");
            Scribe_Values.Look(ref description, "description", "");
            Scribe_Values.Look(ref savedDateTicks, "savedDateTicks", DateTime.Now.Ticks);
            Scribe_Collections.Look(ref factionDefNames, "factionDefNames", LookMode.Value);

            if (factionDefNames == null)
                factionDefNames = new List<string>();
        }

        public bool IsValid()
        {
            if (cachedIsValid.HasValue)
                return cachedIsValid.Value;

            if (factionDefNames == null || factionDefNames.Count == 0)
            {
                cachedIsValid = false;
                return false;
            }

            foreach (string defName in factionDefNames)
            {
                if (DefDatabase<FactionDef>.GetNamedSilentFail(defName) == null)
                {
                    cachedIsValid = false;
                    return false;
                }
            }

            cachedIsValid = true;
            return true;
        }

        public List<FactionDef> GetFactionDefs()
        {
            if (cachedFactionDefs != null)
                return cachedFactionDefs;

            cachedFactionDefs = new List<FactionDef>();
            if (factionDefNames == null)
                return cachedFactionDefs;

            foreach (string defName in factionDefNames)
            {
                FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(defName);
                if (def != null)
                    cachedFactionDefs.Add(def);
            }

            return cachedFactionDefs;
        }

        public List<string> GetMissingFactionNames()
        {
            var missing = new List<string>();
            if (factionDefNames == null)
                return missing;

            foreach (string defName in factionDefNames)
            {
                if (DefDatabase<FactionDef>.GetNamedSilentFail(defName) == null)
                    missing.Add(defName);
            }

            return missing;
        }

        public string GetSummary()
        {
            List<FactionDef> defs = GetFactionDefs();
            int visibleCount = defs.Count(f => f.displayInFactionSelection);
            int totalInPreset = factionDefNames?.Count ?? 0;
            int missingCount = totalInPreset - defs.Count;

            if (missingCount <= 0)
                return $"{visibleCount} faction{(visibleCount != 1 ? "s" : "")}";
            return $"{visibleCount} faction{(visibleCount != 1 ? "s" : "")} ({missingCount} missing)";
        }

        public void ClearCache()
        {
            cachedIsValid = null;
            cachedFactionDefs = null;
        }
    }
}
