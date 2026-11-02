using System;
using UnityEngine;
using Verse;
using RimWorld;
using Planets.UI.Pages;
using Planets.Presets.WorldGen;
using Planets.Core;
using Planets.Compatibility;
using Planets.Utilities;

namespace Planets.UI.Dialogs
{
    /// <summary>
    /// Modal dialog for saving world generation presets (CreateWorldParams + Advanced Climate).
    /// </summary>
    public class Dialog_SaveWorldGenPreset : Window
    {
        private string presetName = "";
        private string description = "";
        private bool setAsDefault = false;
        private bool includeSeedAndName = false;
        private readonly Page_CreateWorldParamsRP page;

        public override Vector2 InitialSize => new Vector2(500f, 360f);

        public Dialog_SaveWorldGenPreset(Page_CreateWorldParamsRP page)
        {
            this.page = page;
            this.forcePause = true;
            this.doCloseX = true;
            this.doCloseButton = false;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = true;
            presetName = PresetFileNames.NextUnique("New Preset", WorldGenPresetManager.PresetExists);
            description = BuildSuggestedDescription();
        }

        private string BuildSuggestedDescription()
        {
            // List sliders/variables that come from external mod integrations (not vanilla).
            // Format: "[Variable] ([Mod])"
            var lines = new System.Collections.Generic.List<string>();

            if (MyLittlePlanetCompat.IsEnabled)
                lines.Add("Planet size (My Little Planet)");

            if (GeologicalLandformsCompat.IsEnabled)
                lines.Add("Cave systems (Geological Landforms)");

            if (VFEInsectoidsCompat.IsEnabled)
                lines.Add("Insect territory scale (VFE Insectoids 2)");

            if (ModsConfig.BiotechActive && PollutionTweaksCompat.IsEnabled)
                lines.Add("Pollution tweaks (Pollution Tweaks)");

            if (WorldTechLevelCompat.IsEnabled)
                lines.Add("World tech level (World Tech Level)");

            return lines.Count == 0 ? "" : string.Join("\n", lines);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Planets.SaveWorldPreset".Translate());
            Text.Font = GameFont.Small;
            float y = 45f;

            Widgets.Label(new Rect(0f, y, 100f, 30f), "Planets.PresetName".Translate());
            string previousName = presetName;
            presetName = Widgets.TextField(new Rect(110f, y, inRect.width - 110f, 30f), presetName);
            y += 35f;
            
            Widgets.Label(new Rect(0f, y, 100f, 30f), "Planets.Description".Translate());
            description = Widgets.TextArea(new Rect(110f, y, inRect.width - 110f, 60f), description);
            y += 65f;

            Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 28f), "Planets.IncludeSeedAndName".Translate(), ref includeSeedAndName);
            y += 32f;

            Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 28f), "Planets.SetAsDefaultAutoLoad".Translate(), ref setAsDefault);
            y += 35f;

            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, inRect.width, 40f), "Planets.SaveWorldPresetHelp".Translate());
            GUI.color = Color.white;

            float buttonY = inRect.height - 35f;
            float buttonWidth = 120f;
            if (Widgets.ButtonText(new Rect(0f, buttonY, buttonWidth, 35f), "Cancel".Translate()))
                Close();

            bool canSave = !string.IsNullOrWhiteSpace(presetName);
            if (!canSave) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(inRect.width - buttonWidth, buttonY, buttonWidth, 35f), "Save".Translate()))
            {
                if (canSave) Save();
            }
            GUI.color = Color.white;

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && canSave)
            {
                Save();
                Event.current.Use();
            }
        }

        private void Save()
        {
            if (!PresetFileNames.TryValidate(presetName, out string error))
            {
                Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                return;
            }

            var preset = WorldGenPresetManager.CaptureFrom(page);
            if (preset == null) return;
            preset.name = presetName.Trim();
            preset.description = description.Trim();
            preset.includeSeedAndName = includeSeedAndName;

            if (WorldGenPresetManager.PresetExists(preset.name))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Planets.ConfirmOverwritePreset".Translate(preset.name),
                    () => DoSave(preset),
                    true));
            }
            else
            {
                DoSave(preset);
            }
        }

        private void DoSave(WorldGenPreset preset)
        {
            if (WorldGenPresetManager.SavePreset(preset))
            {
                Messages.Message("Planets.WorldGenPresetSaved".Translate(preset.name), MessageTypeDefOf.TaskCompletion);
                if (setAsDefault)
                {
                    Controller.Settings.defaultWorldGenPreset.CurrentValue = preset.name;
                    Controller.Settings.Write();
                    Messages.Message("Planets.WorldGenPresetSetAsDefault".Translate(preset.name), MessageTypeDefOf.TaskCompletion, false);
                }
                Close();
            }
            else
            {
                Messages.Message("Planets.WorldGenPresetSaveFailed".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
    }
}
