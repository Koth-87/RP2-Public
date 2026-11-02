using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Biomes.Core;
using Planets.Presets.Biomes;
using Planets.Utilities;

namespace Planets.UI.Dialogs
{
    /// <summary>
    /// Single-biome editor dialog with two tabs:
    /// - Tab 1 (<see cref="EditorTab.BiomeWorker"/>): Scoring variables, ideals, tolerances, and secondary factors.
    /// - Tab 2 (<see cref="EditorTab.BiomeDef"/>): Runtime <see cref="BiomeDef"/> stat overrides (animal/plant density, disease interval, settlement weight).
    /// Supports per-biome preset save/load and default assignment.
    /// </summary>
    public partial class Dialog_BiomeEditor : Window
    {
        private readonly BiomeDef biome;
        private readonly BiomeConfiguration config;
        private BiomeMetadata meta;

        private enum EditorTab { BiomeWorker, BiomeDef }
        private EditorTab currentTab = EditorTab.BiomeWorker;

        private bool useTemperature;
        private bool useRainfall;
        private bool useAridity;
        private bool useElevation;

        // Text field buffers for numeric input (cleared on Reset or external profile load)
        private string bufUserIdealTemp, bufUserTempTolerance, bufUserIdealRain, bufUserRainTolerance;
        private string bufIdealAridity, bufAridityTolerance, bufUserIdealElevation, bufUserElevationTolerance, bufUserIdealElevation2, bufUserElevationTolerance2, bufUserElevationWeight1, bufUserElevationWeight2;
        private string bufAnimalDensity, bufPlantDensity, bufDiseaseMtb, bufSettlementWeight;
        private string bufCampWeight, bufPollutionOffset, bufForageability, bufWildPlantRegrowDays;
        private string bufMovementDifficulty, bufGeyserCountFactor, bufWildAnimalScariaChance, bufMaxFishPopulation;
        private string bufSwampinessIdeal, bufSwampinessTol, bufContinentalityIdeal, bufContinentalityTol;
        private string bufSeasonalityIdeal, bufSeasonalityTol, bufWindwardThr, bufWindwardScale, bufLeewardThr, bufLeewardScale;
        private string bufPreferredHilliness, bufFlowThr, bufFlowScale, bufSummerMaxIdeal, bufSummerMaxTol, bufWinterMinIdeal, bufWinterMinTol;
        private string bufMonsoonThr, bufMonsoonScale, bufClumpingThr, bufClumpingBonus, bufClumpingPenalty, bufNoisePassProb;

        private static readonly HashSet<string> CompatNoiseWorkerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DesertOasis", "IceFloes", "Sandbar"
        };

        private static readonly HashSet<string> CompatPercentPassWorkerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DesertOasis", "IceFloes", "Sandbar"
        };

        private Vector2 scrollPos = Vector2.zero;
        private bool hasPresets;
        private const float CHECKBOX_HEIGHT = 24f;
        private const float CONTROL_HEIGHT = 28f;
        private const float HEADER_HEIGHT = 30f;
        private const float GAP = 6f;
        private const float TAB_HEIGHT = 30f;

        public override Vector2 InitialSize => new Vector2(1050f, 850f);
        public override float Margin => 18f;

        public Dialog_BiomeEditor(BiomeDef biome, BiomeConfiguration config)
        {
            this.biome = biome;
            this.config = config;
            meta = config?.GetOrCreateMetadata(biome.defName);

            InitializeVariableStates();

            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            RefreshHasPresets();
        }

        private void ClearAllBuffers()
        {
            bufUserIdealTemp = bufUserTempTolerance = bufUserIdealRain = bufUserRainTolerance = null;
            bufIdealAridity = bufAridityTolerance = bufUserIdealElevation = bufUserElevationTolerance = bufUserIdealElevation2 = bufUserElevationTolerance2 = bufUserElevationWeight1 = bufUserElevationWeight2 = null;
            bufAnimalDensity = bufPlantDensity = bufDiseaseMtb = bufSettlementWeight = null;
            bufCampWeight = bufPollutionOffset = bufForageability = bufWildPlantRegrowDays = null;
            bufMovementDifficulty = bufGeyserCountFactor = bufWildAnimalScariaChance = bufMaxFishPopulation = null;
            bufSwampinessIdeal = bufSwampinessTol = bufContinentalityIdeal = bufContinentalityTol = null;
            bufSeasonalityIdeal = bufSeasonalityTol = bufWindwardThr = bufWindwardScale = bufLeewardThr = bufLeewardScale = null;
            bufPreferredHilliness = bufFlowThr = bufFlowScale = bufSummerMaxIdeal = bufSummerMaxTol = bufWinterMinIdeal = bufWinterMinTol = null;
            bufMonsoonThr = bufMonsoonScale = bufClumpingThr = bufClumpingBonus = bufClumpingPenalty = bufNoisePassProb = null;
        }

        private void InitializeVariableStates()
        {
            if (meta == null)
                return;

            useTemperature = meta.UseVarTemperature;
            useRainfall = meta.UseVarRainfall;
            useAridity = meta.UseVarAridity;
            useElevation = meta.UseVarElevation;
        }

        private static bool IsNativeRP2Biome(BiomeDef b) => b?.workerClass?.Namespace?.StartsWith("Planets") ?? false;

        private static bool IsCompatBiome(BiomeDef b) => b?.workerClass?.Namespace != null && b.workerClass.Namespace.StartsWith("Planets.Biomes.Compat");

        private static bool UsesNoise(BiomeDef b)
        {
            if (BiomeConditionRegistry.UsesNoise(b.defName))
                return true;
            BiomeWorker worker = b.Worker;
            return worker != null && CompatNoiseWorkerNames.Contains(worker.GetType().Name);
        }

        private bool IsPercentPassNoiseCompat()
        {
            BiomeWorker worker = biome?.Worker;
            return worker != null && CompatPercentPassWorkerNames.Contains(worker.GetType().Name);
        }

        public override void DoWindowContents(Rect inRect)
        {
            BiomeMetadata live = config?.GetOrCreateMetadata(biome.defName);
            if (live == null)
                return;
            if (!ReferenceEquals(live, meta))
            {
                meta = live;
                ClearAllBuffers();
                InitializeVariableStates();
            }

            float y = 0f;

            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(biome.LabelCap, inRect.width);
            Widgets.Label(new Rect(0f, y, inRect.width, titleHeight), biome.LabelCap);
            Text.Font = GameFont.Small;
            y += titleHeight + 6f;

            if (IsCompatBiome(biome))
            {
                GUI.color = new Color(0.4f, 0.8f, 1f);
                const string statusText = "Calibrated";
                float statusHeight = Text.CalcHeight(statusText, inRect.width);
                Widgets.Label(new Rect(0f, y, inRect.width, statusHeight), statusText);
                GUI.color = Color.white;
                y += statusHeight + 6f;
            }
            else if (BiomeConditionRegistry.IsInRegistry(biome.defName) || IsNativeRP2Biome(biome))
            {
                GUI.color = Color.green;
                const string statusText = "Native/Mapped";
                float statusHeight = Text.CalcHeight(statusText, inRect.width);
                Widgets.Label(new Rect(0f, y, inRect.width, statusHeight), statusText);
                GUI.color = Color.white;
                y += statusHeight + 6f;
            }
            else
            {
                GUI.color = Color.red;
                const string statusText = "UNCALIBRATED";
                float statusHeight = Text.CalcHeight(statusText, inRect.width);
                Widgets.Label(new Rect(0f, y, inRect.width, statusHeight), statusText);
                GUI.color = Color.white;
                y += statusHeight + 6f;
            }

            y += 36f;

            Rect tabRect = new Rect(0f, y, inRect.width, TAB_HEIGHT);
            var tabs = new List<TabRecord>
            {
                new TabRecord("BiomeWorker", () => currentTab = EditorTab.BiomeWorker, currentTab == EditorTab.BiomeWorker),
                new TabRecord("BiomeDef", () => currentTab = EditorTab.BiomeDef, currentTab == EditorTab.BiomeDef)
            };
            TabDrawer.DrawTabs(tabRect, tabs);
            y += TabDrawer.TabHeight + 8f;

            const float bottomReserve = 50f;
            Rect contentRect = new Rect(0f, y, inRect.width, inRect.height - y - bottomReserve);

            switch (currentTab)
            {
                case EditorTab.BiomeWorker:
                    DrawBiomeWorkerTab(contentRect);
                    break;
                case EditorTab.BiomeDef:
                    DrawBiomeDefTab(contentRect);
                    break;
            }

            float buttonY = inRect.height - 38f;
            const float buttonWidth = 100f;
            const float buttonGap = 8f;
            float x = 0f;

            if (Widgets.ButtonText(new Rect(x, buttonY, buttonWidth, 32f), "Planets.SavePresetBtn".Translate()))
                DoSavePresetFloatMenu();
            x += buttonWidth + buttonGap;

            Rect loadRect = new Rect(x, buttonY, buttonWidth, 32f);
            if (Widgets.ButtonText(loadRect, "Planets.LoadPresetBtn".Translate()))
                DoLoadPresetFloatMenu();
            TooltipHandler.TipRegion(loadRect, "Load a preset. Use Reset/Clear, Set as Default, to make one load when no Advanced Biomes profile is the default.");
            x += buttonWidth + buttonGap;

            if (!hasPresets)
                GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(x, buttonY, buttonWidth, 32f), "Planets.DeletePresetBtn".Translate()))
            {
                if (hasPresets)
                    DoDeletePresetFloatMenu();
            }
            GUI.color = Color.white;
            x += buttonWidth + buttonGap;

            Rect resetClearRect = new Rect(x, buttonY, buttonWidth, 32f);
            if (Widgets.ButtonText(resetClearRect, "Planets.ResetClear".Translate()))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Planets.ResetAll".Translate(), ResetToHardcodedDefaults),
                    new FloatMenuOption("Planets.SetAsDefaultMenu".Translate(), DoSetDefaultPresetFloatMenu),
                    new FloatMenuOption("Planets.ClearDefault".Translate(), () =>
                    {
                        if (Controller.Settings.defaultPresetPerBiome != null && Controller.Settings.defaultPresetPerBiome.Remove(biome.defName))
                        {
                            Controller.Settings.Write();
                            Messages.Message("Default preset cleared for this biome.", MessageTypeDefOf.TaskCompletion, false);
                        }
                    })
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            TooltipHandler.TipRegion(resetClearRect, "Reset All: restore this biome's live values to the built-in defaults. Set as Default: choose which saved preset loads when no Advanced Biomes profile is the default. Clear Default: forget that preset.");
            x += buttonWidth + buttonGap;

            if (Widgets.ButtonText(new Rect(x, buttonY, buttonWidth, 32f), "Close".Translate()))
                Close();
        }

        private void RefreshHasPresets()
        {
            hasPresets = PerBiomePresetManager.GetPresetsForBiome(biome.defName, false).Count > 0;
        }

        private static void DrawLockedCheckbox(Rect rect, string label, string tooltip)
        {
            TooltipHandler.TipRegion(rect, tooltip);
            GUI.color = Color.gray;
            GUI.enabled = false;
            bool shown = true;
            Widgets.CheckboxLabeled(rect, label, ref shown);
            GUI.enabled = true;
            GUI.color = Color.white;
        }

        private void ResetToHardcodedDefaults()
        {
            config?.ResetBiomeToDefault(biome.defName);
            meta = config?.GetOrCreateMetadata(biome.defName);
            if (meta == null)
                return;

            BiomeConfiguration.FillHardcodedDefaults(meta, biome);
            meta.PresetName = null;

            meta.ClearDefOverrides();
            BiomeDefOriginals.Restore(biome);

            ClearAllBuffers();
            InitializeVariableStates();
        }

        private void DoLoadPresetFloatMenu()
        {
            List<PerBiomePreset> presets = PerBiomePresetManager.GetPresetsForBiome(biome.defName, false);
            string defaultPreset = Controller.Settings.defaultPresetPerBiome != null && Controller.Settings.defaultPresetPerBiome.TryGetValue(biome.defName, out string dp) ? dp : null;
            var options = new List<FloatMenuOption>();
            foreach (PerBiomePreset preset in presets)
            {
                PerBiomePreset localPreset = preset;
                string label = localPreset.name;
                if (localPreset.name == defaultPreset)
                    label += " (Default)";
                options.Add(new FloatMenuOption(label, () =>
                {
                    if (localPreset.metadata != null)
                    {
                        meta.CopyFrom(localPreset.metadata);
                        meta.PresetName = localPreset.name;
                    }
                    ClearAllBuffers();
                    InitializeVariableStates();
                    Messages.Message($"Loaded preset '{localPreset.name}' for {biome.LabelCap}.", MessageTypeDefOf.TaskCompletion, false);
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption($"No presets found for {biome.LabelCap}. Save a preset first.", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DoSetDefaultPresetFloatMenu()
        {
            List<PerBiomePreset> presets = PerBiomePresetManager.GetPresetsForBiome(biome.defName, false);
            string current = Controller.Settings.defaultPresetPerBiome != null
                && Controller.Settings.defaultPresetPerBiome.TryGetValue(biome.defName, out string dp) ? dp : null;
            var options = new List<FloatMenuOption>();
            foreach (PerBiomePreset preset in presets)
            {
                PerBiomePreset localPreset = preset;
                string label = localPreset.name == current ? localPreset.name + " (Default)" : localPreset.name;
                options.Add(new FloatMenuOption(label, () =>
                {
                    if (Controller.Settings.defaultPresetPerBiome == null)
                        Controller.Settings.defaultPresetPerBiome = new Dictionary<string, string>();
                    Controller.Settings.defaultPresetPerBiome[biome.defName] = localPreset.name;
                    Controller.Settings.Write();
                    Messages.Message($"Set '{localPreset.name}' as the default preset for {biome.LabelCap}.", MessageTypeDefOf.TaskCompletion, false);
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption($"No presets found for {biome.LabelCap}. Save a preset first.", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DoDeletePresetFloatMenu()
        {
            List<PerBiomePreset> presets = PerBiomePresetManager.GetPresetsForBiome(biome.defName, false);
            string defaultPreset = Controller.Settings.defaultPresetPerBiome != null && Controller.Settings.defaultPresetPerBiome.TryGetValue(biome.defName, out string dp) ? dp : null;
            var options = new List<FloatMenuOption>();
            foreach (PerBiomePreset preset in presets)
            {
                PerBiomePreset localPreset = preset;
                string label = localPreset.name;
                if (localPreset.name == defaultPreset)
                    label += " (Default)";
                options.Add(new FloatMenuOption(label, () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Delete preset '{localPreset.name}' for {biome.LabelCap}?",
                        () =>
                        {
                            bool wasDefault = Controller.Settings.defaultPresetPerBiome != null
                                && Controller.Settings.defaultPresetPerBiome.TryGetValue(biome.defName, out string currentDefault)
                                && currentDefault == localPreset.name;

                            if (PerBiomePresetManager.DeletePreset(biome.defName, localPreset.name))
                            {
                                RefreshHasPresets();
                                if (wasDefault)
                                    Messages.Message($"Deleted preset '{localPreset.name}' and cleared default for {biome.LabelCap}.", MessageTypeDefOf.TaskCompletion, false);
                                else
                                    Messages.Message($"Deleted preset '{localPreset.name}' for {biome.LabelCap}.", MessageTypeDefOf.TaskCompletion, false);

                                if (meta.PresetName == localPreset.name)
                                    meta.PresetName = null;
                            }
                            else
                            {
                                Messages.Message($"Failed to delete preset '{localPreset.name}'.", MessageTypeDefOf.RejectInput, false);
                            }
                        }));
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DoSavePresetFloatMenu()
        {
            List<PerBiomePreset> presets = PerBiomePresetManager.GetPresetsForBiome(biome.defName, false);
            string defaultPreset = Controller.Settings.defaultPresetPerBiome != null
                && Controller.Settings.defaultPresetPerBiome.TryGetValue(biome.defName, out string dp) ? dp : null;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("(New Save)", DoSavePresetDialog)
            };

            foreach (PerBiomePreset preset in presets)
            {
                PerBiomePreset localPreset = preset;
                string label = localPreset.name;
                if (localPreset.name == defaultPreset)
                    label += " (Default)";

                options.Add(new FloatMenuOption(label, () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Overwrite preset '{localPreset.name}' for {biome.LabelCap}?",
                        () => OverwritePreset(localPreset.name),
                        true));
                })
                {
                    tooltip = new TipSignal($"{localPreset.name.AsTipTitle()}\n{localPreset.description}\n\nSaved: {localPreset.SavedDate:yyyy-MM-dd HH:mm}")
                });
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OverwritePreset(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            string existingDesc = PerBiomePresetManager.LoadPreset(biome.defName, name)?.description ?? "";
            if (PerBiomePresetManager.SavePreset(biome.defName, name, meta, existingDesc))
            {
                meta.PresetName = name;
                RefreshHasPresets();
                Messages.Message($"Saved preset '{name}' for {biome.LabelCap}.", MessageTypeDefOf.TaskCompletion, false);
            }
            else
            {
                Messages.Message("Failed to save preset.", MessageTypeDefOf.RejectInput, false);
            }
        }

        private void DoSavePresetDialog()
        {
            Func<string, bool> nameExists = n => PerBiomePresetManager.PresetExists(biome.defName, n);
            string suggested = PresetFileNames.NextUnique("New Preset", nameExists);
            Find.WindowStack.Add(new Dialog_RenamePreset(ConfirmSavePreset, nameExists, suggested));
        }

        private void ConfirmSavePreset(string name, string description, bool setAsDefault)
        {
            if (PerBiomePresetManager.SavePreset(biome.defName, name, meta, description))
            {
                meta.PresetName = name;
                RefreshHasPresets();
                if (setAsDefault)
                {
                    if (Controller.Settings.defaultPresetPerBiome == null)
                        Controller.Settings.defaultPresetPerBiome = new Dictionary<string, string>();
                    Controller.Settings.defaultPresetPerBiome[biome.defName] = name;
                    Controller.Settings.Write();
                    Messages.Message($"Saved preset '{name}' and set as default for {biome.LabelCap}.", MessageTypeDefOf.TaskCompletion, false);
                }
                else
                {
                    Messages.Message($"Saved preset '{name}' for {biome.LabelCap}.", MessageTypeDefOf.TaskCompletion, false);
                }
            }
            else
            {
                Messages.Message("Failed to save preset.", MessageTypeDefOf.RejectInput, false);
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            Controller.Settings?.Write();
        }
    }
}
