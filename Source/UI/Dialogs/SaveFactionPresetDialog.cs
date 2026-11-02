using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using Planets.Presets.Factions;
using Planets.Core;
using Planets.Utilities;

namespace Planets.UI.Dialogs
{
    /// <summary>
    /// Modal dialog for saving faction setup presets.
    /// </summary>
    public class SaveFactionPresetDialog : Window
    {
        private string presetName = "";
        private string description = "";
        private bool setAsDefault = false;
        private string existsCacheName;
        private bool existsCache;
        private List<FactionDef> factions;
        private Action onSaved;
        
        public override Vector2 InitialSize => new Vector2(500f, 320f);
        
        public SaveFactionPresetDialog(List<FactionDef> factions, Action onSaved = null)
        {
            this.factions = factions;
            this.onSaved = onSaved;
            this.forcePause = true;
            this.doCloseX = true;
            this.doCloseButton = false;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = true;
            
            this.presetName = PresetFileNames.NextUnique("New Preset", FactionPresetManager.PresetExists);
            this.description = BuildSuggestedDescription(factions);
        }

        private static string BuildSuggestedDescription(List<FactionDef> factions)
        {
            if (factions == null) return "";

            // List of non-official (mod-added) factions that actually appear in the faction selection list.
            var lines = factions
                .Where(f => f != null && f.displayInFactionSelection)
                .Distinct()
                .Where(f => f.modContentPack != null && !f.modContentPack.IsOfficialMod)
                .OrderBy(f => f.modContentPack.Name)
                .ThenBy(f => f.label)
                .Select(f => $"{f.LabelCap} ({f.modContentPack.Name})")
                .ToList();

            return lines.Count == 0 ? "" : string.Join("\n", lines);
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Planets.SaveFactionPreset".Translate());
            
            Text.Font = GameFont.Small;
            float y = 45f;
            
            // Name field
            Widgets.Label(new Rect(0f, y, 100f, 30f), "Planets.PresetName".Translate());
            presetName = Widgets.TextField(new Rect(110f, y, inRect.width - 110f, 30f), presetName);
            y += 35f;

            // Description field
            Widgets.Label(new Rect(0f, y, 100f, 30f), "Planets.Description".Translate());
            description = Widgets.TextArea(new Rect(110f, y, inRect.width - 110f, 60f), description);
            y += 65f;

            // Default toggle (matches Advanced Biomes / Biome Editor save dialogs)
            Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 28f), "Set as default (auto-load on world creation)", ref setAsDefault);
            y += 35f;
            
            // Show faction count for those that appear in the list (exclude hidden / non-displayed)
            int totalFactions = factions.Count(f => f.displayInFactionSelection);
            int uniqueFactionTypes = factions.Where(f => f.displayInFactionSelection).Select(f => f.defName).Distinct().Count();
            
            GUI.color = Color.gray;
            string infoText = (totalFactions == uniqueFactionTypes)
                ? "Planets.FactionPresetInfo".Translate(totalFactions)
                : "Planets.FactionPresetInfoDetailed".Translate(totalFactions, uniqueFactionTypes);
            Widgets.Label(new Rect(0f, y, inRect.width, 30f), infoText);
            GUI.color = Color.white;
            y += 35f;
            
            // Warning if name already exists
            if (NameAlreadyExists())
            {
                GUI.color = Color.yellow;
                Widgets.Label(new Rect(0f, y, inRect.width, 30f), "Planets.PresetNameExists".Translate());
                GUI.color = Color.white;
                y += 35f;
            }
            
            // Bottom buttons
            float buttonY = inRect.height - 35f;
            float buttonWidth = 120f;
            
            // Cancel
            if (Widgets.ButtonText(new Rect(0f, buttonY, buttonWidth, 35f), "Cancel".Translate()))
            {
                Close();
            }
            
            // Save
            bool canSave = !string.IsNullOrWhiteSpace(presetName);
            if (!canSave) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(inRect.width - buttonWidth, buttonY, buttonWidth, 35f), "Save".Translate()))
            {
                if (canSave) Save();
            }
            GUI.color = Color.white;
            
            // Enter key to save
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && canSave)
            {
                Save();
                Event.current.Use();
            }
        }
        
        private bool NameAlreadyExists()
        {
            if (string.IsNullOrEmpty(presetName))
                return false;
            if (existsCacheName != presetName)
            {
                existsCacheName = presetName;
                existsCache = FactionPresetManager.PresetExists(presetName);
            }
            return existsCache;
        }

        private void Save()
        {
            if (!PresetFileNames.TryValidate(presetName, out string error))
            {
                Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                return;
            }
            
            if (FactionPresetManager.PresetExists(presetName))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Planets.ConfirmOverwritePreset".Translate(presetName),
                    () => DoSave(),
                    true
                ));
            }
            else
            {
                DoSave();
            }
        }
        
        private void DoSave()
        {
            var preset = new FactionSetupPreset(presetName, factions, description);
            
            if (FactionPresetManager.SavePreset(preset))
            {
                Messages.Message("Planets.PresetSaved".Translate(presetName), MessageTypeDefOf.TaskCompletion);
                if (setAsDefault)
                {
                    Controller.Settings.defaultFactionProfile.CurrentValue = presetName;
                    Controller.Settings.Write();
                    Messages.Message("Planets.PresetSetAsDefault".Translate(presetName), MessageTypeDefOf.TaskCompletion, false);
                }
                onSaved?.Invoke();
                Close();
            }
            else
            {
                Messages.Message("Planets.PresetSaveFailed".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
    }
}
