using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Profile;
using Verse.Sound;
using Planets.Presets.Factions;
using Planets.Presets.Biomes;
using Planets.Presets.WorldGen;
using Planets.WorldGen;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Geography;
using Planets.WorldGen.Climate;
using Planets.Utilities.Logging;
using Planets.WorldGen.Constants;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Compatibility;
using Planets.Compatibility.FernyMods;
using Planets.Utilities.UI;
using Planets.UI.Dialogs;
using HarmonyLib;

namespace Planets.UI.Pages
{
    /// <summary>
    /// Realistic Planets world creation page.
    /// Extends vanilla Page_CreateWorldParams with additional climate and geography controls.
    /// </summary>
    public class Page_CreateWorldParamsRP : Page_CreateWorldParams
    {
        // ==================== Static Data ====================
        private readonly static new float[] PlanetCoverages;
        private readonly static WorldPreset[] WorldPresets;
        private readonly static string[] WorldPresetStrings;
        
        // ==================== Core RP Fields ====================
        private new bool initialized;
        private Vector2 leftColumnScrollPos = Vector2.zero;
        
        // ==================== Faction Preset Fields ====================
        private static string currentPresetName = null;
        
        // ==================== World Gen Preset Fields ====================
        private static string currentWorldGenPresetName = null;
        
        // ==================== Mod Integration Fields ====================
        // Geological Landforms integration
        private float mountains = 1f;      // GL default: 1.0 = 100% (midpoint of 0-200% range)
        private float caveSystems = 1f;    // GL default: 1.0 = 100% (midpoint of 0-200% range)
        
        // VFE Insectoids 2 integration
        private float insectTerritoryScale = 1f;
        private static Type gameComponentInsectoidsType;
        
        // ==================== Parent Field Accessors ====================
        // Direct wrappers over publicized Page_CreateWorldParams fields (zero reflection).
        
        public new string seedString
        {
            get => base.seedString;
            set => base.seedString = value;
        }
        
        public new float planetCoverage
        {
            get => base.planetCoverage;
            set => base.planetCoverage = value;
        }
        
        public new OverallTemperature temperature
        {
            get => base.temperature;
            set => base.temperature = value;
        }
        
        public new OverallPopulation population
        {
            get => base.population;
            set => base.population = value;
        }
        
        public new float pollution
        {
            get => base.pollution;
            set => base.pollution = value;
        }
        
        public new LandmarkDensity landmarkDensity
        {
            get => base.landmarkDensity;
            set => base.landmarkDensity = value;
        }
        
        public new List<FactionDef> factions
        {
            get => base.factions;
            set => base.factions = value;
        }
        
        public new OverallRainfall rainfall
        {
            get => base.rainfall;
            set => base.rainfall = value;
        }
        
        // ==================== RP Public Properties ====================
        
        private string CurrentWorldPreset
        {
            get => Planets_GameComponent.worldPreset;
            set => Planets_GameComponent.worldPreset = value;
        }
        
        // Mod integration properties
        public float Mountains
        {
            get => mountains;
            set => mountains = value;
        }
        
        public float CaveSystems
        {
            get => caveSystems;
            set => caveSystems = value;
        }
        
        public float InsectTerritoryScale
        {
            get => insectTerritoryScale;
            set => insectTerritoryScale = value;
        }
        
        // Compatibility aliases (used by external mods like Worldbuilder)
        public List<FactionDef> factionCounts
        {
            get => factions;
            set => factions = value;
        }
        
        public string SeedString => seedString;
        public float PlanetCoverage => planetCoverage;
        public OverallTemperature Temperature => temperature;
        public OverallPopulation Population => population;
        public float Pollution => pollution;
        public LandmarkDensity LandmarkDensity => landmarkDensity;
        
        // ==================== Static Constructor ====================
        static Page_CreateWorldParamsRP()
        {
            PlanetCoverages = new float[] { 
                0.05f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.35f, 0.4f, 0.45f, 0.5f, 
                0.55f, 0.6f, 0.65f, 0.7f, 0.75f, 0.8f, 0.85f, 0.9f, 0.95f, 1f
            };
            
            WorldPresets = WorldPresetUtility.WorldPresets.ToArray();
            WorldPresetStrings = new string[WorldPresets.Length];
            for (int i = 0; i < WorldPresets.Length; i++)
            {
                WorldPresetStrings[i] = WorldPresets[i].Name;
            }
        }
        
        // ==================== Instance Constructor ====================
        
        public Page_CreateWorldParamsRP()
        {
        }
        
        // ==================== Lifecycle Methods ====================
        
        public override Vector2 InitialSize
        {
            get
            {
                Vector2 size = base.InitialSize;
                // Widen the window if Worldbuilder preview is available to fit the 3-column layout
                if (WorldbuilderCompat.Instance.HasPreview)
                {
                    size.x = 1350f;
                }
                return size;
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
    
            if (!this.initialized)
                this.ResetRPState();
            
            currentPresetName = null;
            currentWorldGenPresetName = null;
            
            // Cache VFE Insectoids GameComponent type for later use
            if (Compatibility.VFEInsectoidsCompat.IsEnabled)
            {
                gameComponentInsectoidsType = Compatibility.VFEInsectoidsCompat.GetGameComponentType();
            }
        }
        
        public override void PostOpen()
        {
            base.PostOpen();

            // First open of this page only. Back from select-starting-site reopens the same page,
            // and initialized is already true, so the generated settings stay.
            // A default world preset applied here does not copy the saved seed or planet name.
            if (!this.initialized)
            {
                WorldbuilderCompat.ClearPreview();
                ApplyCreationDefaults();
                this.initialized = true;
            }

            TutorSystem.Notify_Event("PageStart-CreateWorldParams");
        }
        
        // ==================== Reset Methods ====================
        
        /// <summary>
        /// Resets RP-specific world parameters. Vanilla Reset already ran; this does not call it again.
        /// A postfix on Page_CreateWorldParams.Reset invokes this for the RP page, including when another mod
        /// holds the page as the vanilla type.
        /// </summary>
        public void ResetRPState()
        {
            // Reset RP world preset
            this.CurrentWorldPreset = "Planets.Vanilla";
            currentPresetName = null;
            Planets_GameComponent.axialTilt = AxialTilt.Normal;
            Planets_GameComponent.seaLevel = SeaLevel.Normal;
            Planets_GameComponent.planetName = "";
            Planets_GameComponent.riverDensity = 1f;
            Planets_GameComponent.ancientRoadDensity = 1f;
            Planets_GameComponent.factionRoadDensity = 1f;
            Planets_GameComponent.subcount = 10;
            PlanetLayerSettingsDefOf.Surface.settings.subdivisions = 10;
            
            // Reset mod integration values - Geological Landforms defaults: 1f = 100% (midpoint of 0-200% range)
            this.mountains = 1f;
            this.caveSystems = 1f;
            this.insectTerritoryScale = 1f;
            Compatibility.PollutionTweaksCompat.Reset();

            // Reset RP mountains slider (Hilliness Bias)
            Controller.Settings.climateConfig.hillinessBias.Reset();
            Controller.Settings.climateConfig.mountainHeight.Reset();
            Controller.Settings.climateConfig.mountainousness.Reset();
            
            // Initialize temperature curve with default axial tilt
            AxialTiltCurves.SetSeasonalCurve();

            if (ModsConfig.BiotechActive)
                this.pollution = 0.05f;
            
            // Apply Geological Landforms values if active
            if (Compatibility.GeologicalLandformsCompat.IsEnabled)
            {
                // Mountains slider removed - pass 0f (ignored by GL compat)
                Compatibility.GeologicalLandformsCompat.ApplyMountainCaveValues(0f, this.caveSystems);
            }
            
            if (Controller.DebugEnabled)
            {
                RPLog.D("[UI] Reset world parameters to defaults");
            }
        }
        
        /// <summary>
        /// Load the player's default world preset, biome profile, and faction preset.
        /// Where a default is not set, use the built-in values. A biome profile's linked presets
        /// are not overwritten by the per-biome default list.
        /// </summary>
        public void ApplyCreationDefaults()
        {
            ApplyWorldDefaults();
            BiomeProfileManager.ApplyBiomeDefaults(Controller.Settings?.biomeConfig);
            ApplyFactionDefaults();
        }

        private void ApplyWorldDefaults()
        {
            string presetName = Controller.Settings?.defaultWorldGenPreset?.CurrentValue;
            if (!string.IsNullOrEmpty(presetName))
            {
                var preset = WorldGenPresetManager.LoadPreset(presetName);
                if (preset != null)
                {
                    WorldGenPresetManager.ApplyTo(preset, this, true);
                    this.CurrentWorldPreset = "Planets.Custom";
                    if (Controller.DebugEnabled)
                        RPLog.D($"[UI] Applied default world preset: {presetName}");
                    return;
                }

                Messages.Message($"Default world preset '{presetName}' was not found.", MessageTypeDefOf.CautionInput, false);
            }

            base.Reset();
            Controller.Settings?.climateConfig?.ResetToDefaults();
            Controller.Settings?.hydrologyConfig?.ResetToDefaults();
        }

        private void ApplyFactionDefaults()
        {
            string presetName = Controller.Settings?.defaultFactionProfile?.CurrentValue;
            if (!string.IsNullOrEmpty(presetName))
            {
                var preset = FactionPresetManager.LoadPreset(presetName);
                if (preset == null)
                {
                    Messages.Message($"Default faction preset '{presetName}' was not found.", MessageTypeDefOf.CautionInput, false);
                }
                else if (this.factions != null && FactionPresetManager.ApplyPreset(preset, this.factions))
                {
                    currentPresetName = preset.name;
                    if (Controller.DebugEnabled)
                        RPLog.D($"[UI] Applied default faction preset: {presetName}");
                    return;
                }
            }

            ResetFactionCounts();
            currentPresetName = null;
        }

        /// <summary>
        /// Resets only the faction list to defaults using vanilla logic.
        /// Called by the "Reset Factions" button in the faction preset UI.
        /// </summary>
        public new void ResetFactionCounts()
        {
            base.ResetFactionCounts();
            
            if (Controller.DebugEnabled)
            {
                RPLog.D($"[UI] Reset faction counts to defaults");
            }
        }
        
        // ==================== Main UI Rendering ====================
        
        /// <summary>
        /// Main window rendering method. Draws layout with parameter sliders, faction selection, and optional Worldbuilder preview.
        /// </summary>
        public override void DoWindowContents(Rect rect)
        {
            DrawPageTitle(rect);
            Rect mainRect = GetMainRect(rect, 0f, false);

            Rect leftColumn, rightColumn;
            Rect? centerColumn = null;

            // Check if Worldbuilder preview is available for 3-column layout
            bool hasPreview = WorldbuilderCompat.Instance.HasPreview;

            if (hasPreview)
            {
                float spacing = 17f;
                // Symmetrical side columns.
                float sideColWidth = 410f;
                float centerColWidth = mainRect.width - (sideColWidth * 2) - (spacing * 2);
                
                leftColumn = new Rect(mainRect.x, mainRect.y, sideColWidth, mainRect.height);
                centerColumn = new Rect(leftColumn.xMax + spacing, mainRect.y, centerColWidth, mainRect.height);
                rightColumn = new Rect(centerColumn.Value.xMax + spacing, mainRect.y, sideColWidth, mainRect.height);
            }
            else
            {
                // Standard 2-column layout with 20f spacing
                float columnWidth = (mainRect.width - 20f) / 2f;
                leftColumn = new Rect(mainRect.x, mainRect.y, columnWidth, mainRect.height);
                rightColumn = new Rect(mainRect.x + columnWidth + 20f, mainRect.y, columnWidth, mainRect.height);
            }

            // LEFT COLUMN - Parameter sliders with scrolling support
            float contentHeight = CalculateLeftColumnHeight();
            bool showScrollbar = contentHeight > leftColumn.height;
            float contentPadding = 24f;
            float viewWidth = showScrollbar ? 
                leftColumn.width - 16f - contentPadding : leftColumn.width - contentPadding;

            Rect viewRect = new Rect(0f, 0f, viewWidth, contentHeight);
            Widgets.BeginScrollView(leftColumn, ref leftColumnScrollPos, viewRect, true);
            DrawLeftColumnContent(viewWidth);
            Widgets.EndScrollView();

            // CENTER COLUMN - Worldbuilder Preview (if active)
            if (centerColumn.HasValue)
            {
                WorldbuilderCompat.Instance.DrawPreview(this, centerColumn.Value);
            }

            // RIGHT COLUMN - Faction selection UI
            Rect adjustedRightColumn = new Rect(rightColumn.x, rightColumn.y, rightColumn.width - 24f, rightColumn.height);
            
            // Pass false for isDefaultFactionCounts since preset system handles this
            bool isDefaultFactionCounts = false;
            WorldFactionsUIUtility.DoWindowContents(adjustedRightColumn, this.factions, isDefaultFactionCounts);
            DrawFactionPresetButtons(rightColumn);

            // Bottom button bar
            DoCustomBottomButtons(rect);
        }

        /// <summary>
        /// Draws all world parameter sliders in the left column.
        /// Includes seed, coverage, climate parameters, and conditional mod integrations.
        /// </summary>
        /// <param name="width">Available width for slider controls</param>
        private void DrawLeftColumnContent(float width)
        {
            Text.Font = GameFont.Small;
            float yPos = 0f;
            
            // Planet name
            Widgets.Label(new Rect(0f, yPos, 150f, 30f), "Planets.PlanetName".Translate());
            Rect nameRect = new Rect(150f, yPos, width - 150f, 30f);
            Planets_GameComponent.planetName = Widgets.TextField(nameRect, Planets_GameComponent.planetName);
            yPos += 40f;

            // Planet seed
            Widgets.Label(new Rect(0f, yPos, 150f, 30f), "WorldSeed".Translate());
            Rect seedRect = new Rect(150f, yPos, width - 150f, 30f);
            this.seedString = Widgets.TextField(seedRect, this.seedString);
            yPos += 40f;
            
            // Randomize row
            Widgets.Label(new Rect(0f, yPos, 150f, 30f), "Planets.Randomize".Translate());
            float buttonWidth = (width - 150f - 10f) / 2f;
            
            Rect randomNameRect = new Rect(150f, yPos, buttonWidth, 30f);
            if (Widgets.ButtonText(randomNameRect, "Planets.PlanetName".Translate(), true, false, true))
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(null);
                Planets_GameComponent.planetName = NameGenerator.GenerateName(RulePackDefOf.NamerWorld);
            }

            Rect randomSeedRect = new Rect(150f + buttonWidth + 10f, yPos, buttonWidth, 30f);
            if (Widgets.ButtonText(randomSeedRect, "WorldSeed".Translate(), true, false, true))
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(null);
                this.seedString = GenText.RandomSeedString();
            }
            
            // Native Planet Scale
            yPos += 40f;
            Widgets.Label(new Rect(0f, yPos, 150f, 30f), "Planets.PlanetScale".Translate());
            Rect scaleRect = new Rect(150f, yPos, width - 150f, 30f);
            
            // Dynamic center label shows current subdivision count
            string scaleLabel = Planets_GameComponent.subcount.ToString();
            
            int newSubcount = Mathf.RoundToInt(
                Widgets.HorizontalSlider(scaleRect, Planets_GameComponent.subcount, 5f, 11f, true, 
                    scaleLabel, null, null, 1f));
            if (newSubcount != Planets_GameComponent.subcount)
            {
                Planets_GameComponent.subcount = newSubcount;
                PlanetLayerSettingsDefOf.Surface.settings.subdivisions = newSubcount;
            }
            TooltipHandler.TipRegion(new Rect(0f, yPos, scaleRect.xMax, scaleRect.height), "Planets.PlanetScaleTip".Translate());
            
            // Core world parameters
            yPos += 40f;
            DoPlanetCoverage(yPos, width);
            
            yPos += 40f;
            DoWorldGenPresetButtons(yPos, width);
            
            yPos += 40f;
            DoSeaLevelSlider(yPos, width);
            
            yPos += 40f;
            DoVanillaRainfallSlider(yPos, width);
            
            yPos += 40f;
            DoTemperatureSlider(yPos, width);
            
            yPos += 40f;
            DoAxialTiltSlider(yPos, width);
            
            yPos += 40f;
            Rect populationRow = new Rect(0f, yPos, width, 30f);
            bool populationLocked = Compatibility.FactionControlCompat.IsEnabled;
            if (populationLocked)
                TooltipHandler.TipRegion(populationRow, "Planets.PopulationFactionControl".Translate());
            bool populationEnabled = GUI.enabled;
            if (populationLocked)
                GUI.enabled = false;
            DoPopulationSlider(yPos, width);
            GUI.enabled = populationEnabled;
            
            // Landmarks (Odyssey DLC)
            if (ModsConfig.OdysseyActive)
            {
                yPos += 40f;
                DoLandmarksSlider(yPos, width);
            }
            
            yPos += 40f;
            DoMountainHeightSlider(yPos, width);
            yPos += 40f;
            DoMountainFrequencySlider(yPos, width);

            // Densities
            yPos += 40f;
            bool worldbuilderControlsDensity = ModsConfig.IsActive("ferny.Worldbuilder");
            string riverTip = worldbuilderControlsDensity ? "Controlled by Worldbuilder" : "Planets.RiverDensityTip".Translate();
            string ancientTip = worldbuilderControlsDensity ? "Controlled by Worldbuilder" : "Planets.AncientRoadDensityTip".Translate();
            string factionTip = worldbuilderControlsDensity ? "Controlled by Worldbuilder" : "Planets.FactionRoadDensityTip".Translate();
            bool densityEnabled = GUI.enabled;
            if (worldbuilderControlsDensity)
                GUI.enabled = false;
            DoDensitySlider(yPos, width, "Planets.RiverDensity".Translate(), riverTip, ref Planets_GameComponent.riverDensity);

            yPos += 40f;
            DoDensitySlider(yPos, width, "Planets.AncientRoadDensity".Translate(), ancientTip, ref Planets_GameComponent.ancientRoadDensity);

            yPos += 40f;
            DoDensitySlider(yPos, width, "Planets.FactionRoadDensity".Translate(), factionTip, ref Planets_GameComponent.factionRoadDensity);
            GUI.enabled = densityEnabled;
            
            // Geological Landforms integration
            // GL's cave system generation uses RP2's actual terrain data via translation layer
            if (Compatibility.GeologicalLandformsCompat.IsEnabled)
            {
                yPos += 40f;
                DoCaveSystemsSlider(yPos, width);
            }
            
            // VFE Insectoids 2 integration
            if (Compatibility.VFEInsectoidsCompat.IsEnabled)
            {
                yPos += 40f;
                DoInsectTerritoryScaleSlider(yPos, width);
            }

            // Pollution (Biotech DLC)
            if (ModsConfig.BiotechActive)
            {
                // DoPollutionSlider handles its own yPos increment and adds Pollution Tweaks controls
                yPos = DoPollutionSlider(yPos, width);
            }

            // World Tech Level integration (WTL mod draws its own slider)
            if (Compatibility.WorldTechLevelCompat.IsEnabled)
            {
                var tempFactions = this.factions;
                var tempPollution = this.pollution;
                yPos = Compatibility.WorldTechLevelCompat.DoTechLevelSlider(ref tempFactions, ref tempPollution, yPos, width);
                this.factions = tempFactions;
                this.pollution = tempPollution;
            }

            if (TryGetSparseSettlementWarning(out string settlementWarning))
            {
                yPos += 40f;
                GUI.color = Color.yellow;
                float warningHeight = Text.CalcHeight(settlementWarning, width);
                Widgets.Label(new Rect(0f, yPos, width, warningHeight), settlementWarning);
                GUI.color = Color.white;
                yPos += warningHeight - 30f;
            }

            // Advanced settings button
            if (!TutorSystem.TutorialMode)
            {
                yPos += 40f;
                Widgets.Label(new Rect(0f, yPos, 150f, 30f), "Planets.MiscSettings".Translate());
                if (Widgets.ButtonText(new Rect(150f, yPos, width - 150f, 30f), "Edit".Translate() + "...", true, true, true, null))
                {
                    Find.WindowStack.Add(new Dialog_AdvancedGameConfig());
                }
            }
        }

        // ==================== Faction Preset UI ====================
        
        private void DrawFactionPresetButtons(Rect rightColumn)
        {
            float buttonHeight = 28f;
            float buttonSpacing = 5f;
            float buttonY = rightColumn.y - 40f;
            
            // Four buttons: Save - Load - Delete - Reset/Clear (same order as Dialog_AdvancedBiomes)
            float totalWidth = rightColumn.width;
            int buttonCount = 4;
            float buttonWidth = (totalWidth - (buttonSpacing * (buttonCount - 1))) / buttonCount;
            
            Text.Font = GameFont.Small;
            
            // Save preset button
            float xPos = rightColumn.x;
            if (Widgets.ButtonText(new Rect(xPos, buttonY, buttonWidth, buttonHeight), 
                "Planets.SaveFactions".Translate()))
            {
                ShowSavePresetsMenu();
            }
            
            // Load preset button
            xPos += buttonWidth + buttonSpacing;
            Rect loadRect = new Rect(xPos, buttonY, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(loadRect, "Planets.LoadFactions".Translate()))
            {
                ShowLoadPresetsMenu();
            }
            TooltipHandler.TipRegion(loadRect, "Load a faction preset. Use Reset/Clear, Set as Default, to make one load automatically.");
            xPos += buttonWidth + buttonSpacing;
            
            // Delete preset button
            bool hasPresets = FactionPresetManager.GetAllPresets().Count > 0;
            if (!hasPresets)
                GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(xPos, buttonY, buttonWidth, buttonHeight), 
                "Planets.DeletePreset".Translate()))
            {
                if (hasPresets)
                    ShowDeletePresetsMenu();
            }
            GUI.color = Color.white;
            xPos += buttonWidth + buttonSpacing;
            
            // Reset/Clear button (FloatMenu: Reset All | Set as Default... | Clear Default)
            Rect resetClearRect = new Rect(xPos, buttonY, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(resetClearRect, "Planets.ResetClear".Translate()))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                opts.Add(new FloatMenuOption("Planets.ResetAll".Translate(), () => {
                    ApplyFactionDefaults();
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }));
                opts.Add(new FloatMenuOption("Planets.SetAsDefaultMenu".Translate(), ShowSetDefaultFactionPresetMenu));
                opts.Add(new FloatMenuOption("Planets.ClearDefault".Translate(), () => {
                    Controller.Settings.defaultFactionProfile.CurrentValue = null;
                    Controller.Settings.Write();
                    Messages.Message("Default faction profile cleared.", MessageTypeDefOf.TaskCompletion, false);
                }));
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            TooltipHandler.TipRegion(resetClearRect, "Reset All: reload the default faction preset, or the built-in factions when no default is set. Set as Default: choose which preset loads automatically. Clear Default: forget the auto-load preset.");
            
            // Show current preset name if one is loaded
            if (!string.IsNullOrEmpty(currentPresetName))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(rightColumn.x, buttonY - 18f, rightColumn.width, 18f), 
                    "Planets.CurrentPreset".Translate(currentPresetName));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
        }
        
        private void ShowSavePresetsMenu()
        {
            var presets = FactionPresetManager.GetAllPresets(true);
            string defaultProfile = Controller.Settings.defaultFactionProfile.CurrentValue;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Top option: create a new save (opens the existing save dialog)
            options.Add(new FloatMenuOption("Planets.NewSave".Translate(), () =>
            {
                Find.WindowStack.Add(new SaveFactionPresetDialog(this.factions, () =>
                {
                    // Callback for UI refresh if needed
                }));
            }));

            // Existing saves: overwrite with confirmation
            foreach (var preset in presets)
            {
                var localPreset = preset;
                string label = localPreset.name;
                if (localPreset.name == defaultProfile)
                    label += " (Default)";

                if (!localPreset.IsValid())
                    label += " " + "Planets.PresetHasMissingMods".Translate();
                else
                    label += $" ({localPreset.GetSummary()})";

                options.Add(new FloatMenuOption(label, () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Planets.ConfirmOverwritePreset".Translate(localPreset.name),
                        () => OverwriteFactionPreset(localPreset.name),
                        true
                    ));
                })
                {
                    tooltip = new TipSignal($"{localPreset.name.AsTipTitle()}\n{localPreset.description}\n\nSaved: {localPreset.SavedDate:yyyy-MM-dd HH:mm}")
                });
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowLoadPresetsMenu()
        {
            var presets = FactionPresetManager.GetAllPresets(true);
            string defaultProfile = Controller.Settings.defaultFactionProfile.CurrentValue;
            
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            
            foreach (var preset in presets)
            {
                var localPreset = preset; // Capture for closure
                
                string label = preset.name;
                bool isDefault = preset.name == defaultProfile;
                if (isDefault)
                    label += " (Default)";
                
                string tooltip = preset.description ?? "";
                
                if (!preset.IsValid())
                {
                    label += " " + "Planets.PresetHasMissingMods".Translate();
                    var missing = preset.GetMissingFactionNames();
                    if (missing.Count > 0)
                    {
                        tooltip += "\n\n" + "Planets.MissingFactions".Translate() + "\n" + string.Join("\n", missing.Select(m => "  - " + m));
                    }
                }
                else
                {
                    label += $" ({preset.GetSummary()})";
                }
                
                tooltip += "\n\n" + "Planets.SavedOn".Translate(preset.SavedDate.ToString("yyyy-MM-dd HH:mm"));
                
                options.Add(new FloatMenuOption(label, () => LoadPreset(localPreset))
                {
                    tooltip = new TipSignal(tooltip)
                });
            }
            
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("Planets.NoPresetsFound".Translate(), null));
            }
            
            Find.WindowStack.Add(new FloatMenu(options));
        }
        
        private void ShowDeletePresetsMenu()
        {
            var presets = FactionPresetManager.GetAllPresets(true);
            
            if (presets.Count == 0)
            {
                Messages.Message("Planets.NoPresetsFound".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            
            foreach (var preset in presets)
            {
                var localPreset = preset; // Capture for closure
                
                string label = preset.name;
                if (!string.IsNullOrEmpty(preset.description))
                {
                    label += " - " + preset.description.Truncate(50);
                }
                
                var option = new FloatMenuOption(label, () => 
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Planets.ConfirmDeletePreset".Translate(localPreset.name),
                        () => DeletePreset(localPreset.name),
                        true
                    ));
                });
                
                options.Add(option);
            }
            
            Find.WindowStack.Add(new FloatMenu(options));
        }
        
        private void LoadPreset(FactionSetupPreset preset)
        {
            if (!FactionPresetManager.ApplyPreset(preset, this.factions))
                return;

            currentPresetName = preset.name;
            int visibleCount = this.factions.Count(f => f.displayInFactionSelection);
            Messages.Message("Planets.PresetLoaded".Translate(preset.name, visibleCount), MessageTypeDefOf.TaskCompletion, false);
        }

        private void ShowSetDefaultFactionPresetMenu()
        {
            var presets = FactionPresetManager.GetAllPresets(true);
            string current = Controller.Settings.defaultFactionProfile.CurrentValue;
            var options = new List<FloatMenuOption>();
            foreach (var preset in presets)
            {
                var localPreset = preset;
                string label = localPreset.name == current ? localPreset.name + " (Default)" : localPreset.name;
                options.Add(new FloatMenuOption(label, () =>
                {
                    Controller.Settings.defaultFactionProfile.CurrentValue = localPreset.name;
                    Controller.Settings.Write();
                    Messages.Message("Planets.PresetSetAsDefault".Translate(localPreset.name), MessageTypeDefOf.TaskCompletion, false);
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("Planets.NoPresetsFound".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OverwriteFactionPreset(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
                return;

            // Preserve existing description when overwriting
            string existingDesc = FactionPresetManager.LoadPreset(presetName)?.description ?? "";
            var preset = new FactionSetupPreset(presetName, this.factions, existingDesc);

            if (FactionPresetManager.SavePreset(preset))
            {
                Messages.Message("Planets.PresetSaved".Translate(presetName), MessageTypeDefOf.TaskCompletion);
            }
            else
            {
                Messages.Message("Planets.PresetSaveFailed".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
        
        private void DeletePreset(string presetName)
        {
            bool wasDefault = Controller.Settings.defaultFactionProfile.CurrentValue == presetName;
            if (FactionPresetManager.DeletePreset(presetName))
            {
                if (wasDefault)
                    Messages.Message("Planets.PresetDeletedAndDefaultCleared".Translate(presetName), MessageTypeDefOf.TaskCompletion, false);
                else
                    Messages.Message("Planets.PresetDeleted".Translate(presetName), MessageTypeDefOf.TaskCompletion, false);

                if (currentPresetName == presetName)
                    currentPresetName = null;
            }
            else
            {
                Messages.Message("Planets.PresetDeleteFailed".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
        
        // ==================== UI Helper Methods ====================
        
        private float CalculateLeftColumnHeight()
        {
            float height = 0f;
            
            // Base controls
            height += 40f; // Planet name
            height += 40f; // Seed
            height += 40f; // Scale
            height += 40f; // Coverage
            height += 40f; // World Presets
            height += 40f; // World Type
            height += 40f; // Axial Tilt
            height += 40f; // Rainfall
            height += 40f; // Temperature
            height += 40f; // Population, including the read-only row when Faction Control is active
            
            // Conditional elements
            if (ModsConfig.OdysseyActive)
            {
                height += 40f; // Landmarks
            }

            height += 80f; // Mountain height and frequency
            height += 120f; // River, Ancient Road, Faction Road densities
            
            if (Compatibility.GeologicalLandformsCompat.IsEnabled)
            {
                height += 40f; // Cave systems
            }
            if (TryGetSparseSettlementWarning(out string settlementWarning))
                height += Text.CalcHeight(settlementWarning, 400f);
            
            if (Compatibility.VFEInsectoidsCompat.IsEnabled)
            {
                height += 40f; // Insect Territory Scale
            }
            
            if (ModsConfig.BiotechActive)
            {
                height += 40f; // Pollution
                
                if (Compatibility.PollutionTweaksCompat.IsEnabled)
                {
                    height += 80f; // Range slider + Spread checkbox
                }
            }
            
            if (Compatibility.WorldTechLevelCompat.IsEnabled)
            {
                height += 40f; // World Tech Level
            }
            
            if (!TutorSystem.TutorialMode)
            {
                height += 40f; // Advanced Settings
            }
            
            // Add padding
            height += 75f;
            
            return height;
        }
        
        public void Randomize()
        {
            this.seedString = GenText.RandomSeedString();
            
            // Randomly select a preset (excluding Custom) using utility
            var randomPreset = RandomWorldParameters.GetRandomPreset(WorldPresets);
            if (randomPreset != null)
            {
                ApplyWorldPreset(randomPreset);
            }
            
            float[] coverageChoices = PlanetCoverages.Where(coverage => coverage >= 0.2f).ToArray();
            this.planetCoverage = coverageChoices[Rand.Range(0, coverageChoices.Length)];

            if (this.population < OverallPopulation.LittleBitLess)
                this.population = OverallPopulation.LittleBitLess;
            
            // Randomize mod-specific parameters using utilities
            if (Compatibility.GeologicalLandformsCompat.IsEnabled)
            {
                this.caveSystems = RandomWorldParameters.RandomizeCaveSystems();
            }
            
            if (Compatibility.VFEInsectoidsCompat.IsEnabled)
            {
                this.insectTerritoryScale = RandomWorldParameters.RandomizeInsectScale();
            }
            
            if (ModsConfig.BiotechActive)
            {
                this.pollution = RandomWorldParameters.RandomizePollution();
            }

            if (Controller.Settings?.climateConfig != null)
            {
                Controller.Settings.climateConfig.mountainHeight.CurrentValue = Rand.Range(0.75f, 1.25f);
                Controller.Settings.climateConfig.mountainousness.CurrentValue = Rand.Range(0.6f, 1.4f);
            }
        }
        
        // ==================== Slider Drawing Methods ====================
        
        // Core world parameters
        
        private void DoPlanetCoverage(float single, float width)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), "PlanetCoverage".Translate());
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            
            if (Widgets.ButtonText(rect, this.planetCoverage.ToStringPercent(), true, true, true, null))
            {
                List<FloatMenuOption> floatMenuOptions = new List<FloatMenuOption>();
                float[] coverages = PlanetCoverages;
                for (int k = 0; k < coverages.Length; k++)
                {
                    float coverage = coverages[k];
                    FloatMenuOption floatMenuOption = new FloatMenuOption(coverage.ToStringPercent(), delegate ()
                    {
                        if (this.planetCoverage != coverage)
                        {
                            this.planetCoverage = coverage;
                            this.CurrentWorldPreset = "Planets.Custom";
                            
                            // Performance warning for very high coverage
                            if (this.planetCoverage >= 0.95f)
                            {
                                Messages.Message("MessageMaxPlanetCoveragePerformanceWarning".Translate(), 
                                    MessageTypeDefOf.CautionInput, false);
                            }
                            
                            // Warning for very low coverage
                            if (this.planetCoverage <= 0.10f)
                            {
                                Messages.Message("Planets.MessageLowPlanetCoverageWarning".Translate(), 
                                    MessageTypeDefOf.CautionInput, false);
                            }
                        }
                    }, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0);
                    floatMenuOptions.Add(floatMenuOption);
                }
                Find.WindowStack.Add(new FloatMenu(floatMenuOptions));
            }
            
            TooltipHandler.TipRegion(new Rect(0f, single, rect.xMax, rect.height), 
                "PlanetCoverageTip".Translate());
        }
        
        private void DoWorldGenPresetButtons(float single, float width)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), "Planets.WorldPresets".Translate());
            
            // Calculate button width to fill available space (matching other buttons like "RandomizeSeed")
            float availableWidth = width - 150f; // Label takes 150f
            float buttonSpacing = 5f;
            int buttonCount = 3; // Save, Load, Delete
            float buttonWidth = (availableWidth - (buttonSpacing * (buttonCount - 1))) / buttonCount;
            
            float x = 150f;
            
            if (Widgets.ButtonText(new Rect(x, single, buttonWidth, 28f), "Planets.SaveFactions".Translate()))
            {
                ShowSaveWorldGenPresetMenu();
            }
            x += buttonWidth + buttonSpacing;
            if (Widgets.ButtonText(new Rect(x, single, buttonWidth, 28f), "Planets.LoadFactions".Translate()))
            {
                ShowLoadWorldGenPresetMenu();
            }
            x += buttonWidth + buttonSpacing;
            bool hasWgPresets = WorldGenPresetManager.GetAllPresets().Count > 0;
            if (!hasWgPresets) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(x, single, buttonWidth, 28f), "Planets.DeletePreset".Translate()))
            {
                if (hasWgPresets)
                    ShowDeleteWorldGenPresetMenu();
            }
            GUI.color = Color.white;
        }
        
        private void ShowSaveWorldGenPresetMenu()
        {
            var presets = WorldGenPresetManager.GetAllPresets(true);
            string defaultPreset = Controller.Settings.defaultWorldGenPreset.CurrentValue;
            var options = new List<FloatMenuOption>();

            options.Add(new FloatMenuOption("Planets.NewSave".Translate(), () => ShowSaveWorldGenPresetDialog()));

            foreach (var p in presets)
            {
                var preset = p;
                string label = preset.name;
                if (preset.name == defaultPreset)
                    label += " (Default)";

                options.Add(new FloatMenuOption(label, () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Planets.ConfirmOverwritePreset".Translate(preset.name),
                        () => OverwriteWorldGenPreset(preset.name),
                        true
                    ));
                })
                {
                    tooltip = new TipSignal($"{preset.name.AsTipTitle()}\n{preset.description}\n\nSeed: {preset.seedString}\n\nSaved: {preset.SavedDate:yyyy-MM-dd HH:mm}")
                });
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowSaveWorldGenPresetDialog()
        {
            Find.WindowStack.Add(new Dialog_SaveWorldGenPreset(this));
        }
        
        private void ShowLoadWorldGenPresetMenu()
        {
            var presets = WorldGenPresetManager.GetAllPresets(true);
            var options = new List<FloatMenuOption>();
            foreach (var p in presets)
            {
                var preset = p;
                string label = preset.name;
                bool isDefault = preset.name == Controller.Settings.defaultWorldGenPreset.CurrentValue;
                if (isDefault) label += " (Default)";
                options.Add(new FloatMenuOption(label, () =>
                {
                    WorldGenPresetManager.ApplyTo(preset, this);
                    this.CurrentWorldPreset = "Planets.Custom";
                    currentWorldGenPresetName = preset.name;
                    Messages.Message("Planets.WorldGenPresetLoaded".Translate(preset.name), MessageTypeDefOf.TaskCompletion, false);
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("Planets.NoWorldGenPresetsFound".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowSetDefaultWorldGenPresetMenu()
        {
            var presets = WorldGenPresetManager.GetAllPresets(true);
            string current = Controller.Settings.defaultWorldGenPreset.CurrentValue;
            var options = new List<FloatMenuOption>();
            foreach (var p in presets)
            {
                var preset = p;
                string label = preset.name == current ? preset.name + " (Default)" : preset.name;
                options.Add(new FloatMenuOption(label, () =>
                {
                    Controller.Settings.defaultWorldGenPreset.CurrentValue = preset.name;
                    Controller.Settings.Write();
                    Messages.Message("Planets.WorldGenPresetSetAsDefault".Translate(preset.name), MessageTypeDefOf.TaskCompletion, false);
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("Planets.NoWorldGenPresetsFound".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        
        private void OverwriteWorldGenPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var preset = WorldGenPresetManager.CaptureFrom(this);
            if (preset == null) return;
            preset.name = name.Trim();
            preset.description = WorldGenPresetManager.LoadPreset(preset.name)?.description ?? "";

            if (WorldGenPresetManager.SavePreset(preset))
            {
                Messages.Message("Planets.WorldGenPresetSaved".Translate(preset.name), MessageTypeDefOf.TaskCompletion, false);
            }
            else
            {
                Messages.Message("Planets.WorldGenPresetSaveFailed".Translate(), MessageTypeDefOf.RejectInput, false);
            }
        }

        private void ShowDeleteWorldGenPresetMenu()
        {
            var presets = WorldGenPresetManager.GetAllPresets(true);
            if (presets.Count == 0) return;
            var options = new List<FloatMenuOption>();
            foreach (var p in presets)
            {
                var preset = p;
                options.Add(new FloatMenuOption(preset.name, () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Planets.ConfirmDeleteWorldGenPreset".Translate(preset.name),
                        () =>
                        {
                            bool wasDefault = Controller.Settings.defaultWorldGenPreset.CurrentValue == preset.name;
                            if (WorldGenPresetManager.DeletePreset(preset.name))
                            {
                                if (wasDefault)
                                    Messages.Message("Planets.WorldGenPresetDeletedAndDefaultCleared".Translate(preset.name), MessageTypeDefOf.TaskCompletion, false);
                                else
                                    Messages.Message("Planets.WorldGenPresetDeleted".Translate(preset.name), MessageTypeDefOf.TaskCompletion, false);

                                if (currentWorldGenPresetName == preset.name)
                                    currentWorldGenPresetName = null;
                            }
                        },
                        true));
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        
        // Climate parameters
        
        private void DoSeaLevelSlider(float single, float width)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), "Planets.SeaLevel".Translate());
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            SeaLevel seaLevelCheck = Planets_GameComponent.seaLevel;

            string currentLabel = SeaLevelUtility.GetLabel(Planets_GameComponent.seaLevel);
            
            float slider = Widgets.HorizontalSlider(rect, (float)Planets_GameComponent.seaLevel, 0f, 
                SeaLevelUtility.EnumValuesCount - 1, true, currentLabel, null, null, 1f);
            Planets_GameComponent.seaLevel = (SeaLevel)Mathf.RoundToInt(slider);
            
            if (Planets_GameComponent.seaLevel != seaLevelCheck)
            {
                this.CurrentWorldPreset = "Planets.Custom";
            }
            
            TooltipHandler.TipRegion(new Rect(0f, single, rect.xMax, rect.height), 
                "Planets.SeaLevelTip".Translate());
        }

        private void DoVanillaRainfallSlider(float single, float width)
        {
            bool manualRain = Controller.Settings?.climateConfig?.manualRainfallEnabled?.CurrentValue == true;
            Widgets.Label(new Rect(0f, single, 150f, 30f), "PlanetRainfall".Translate());
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            
            if (manualRain)
            {
                GUI.color = Color.gray;
                Widgets.Label(rect, "Planets.RainfallControlledByAdvanced".Translate());
                TooltipHandler.TipRegion(new Rect(0f, single, rect.xMax, rect.height), 
                    "Planets.RainfallManualModeTip".Translate());
                GUI.color = Color.white;
                return;
            }
            
            OverallRainfall rainfallCheck = this.rainfall;
            string currentLabel = GetRainfallLabel(this.rainfall);

            this.rainfall = (OverallRainfall)Mathf.RoundToInt(
                Widgets.HorizontalSlider(rect, (float)this.rainfall, 0f, 
                (float)(Enum.GetNames(typeof(OverallRainfall)).Length - 1), true, 
                currentLabel, null, null, 1f));
                
            if (this.rainfall != rainfallCheck) 
            { 
                this.CurrentWorldPreset = "Planets.Custom"; 
            }
        }

        private void DoTemperatureSlider(float single, float width)
        {
            bool manualTemp = Controller.Settings?.climateConfig?.manualTemperatureEnabled?.CurrentValue == true;
            Widgets.Label(new Rect(0f, single, 150f, 30f), "PlanetTemperature".Translate());
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            
            if (manualTemp)
            {
                GUI.color = Color.gray;
                Widgets.Label(rect, "Planets.TempControlledByAdvanced".Translate());
                TooltipHandler.TipRegion(new Rect(0f, single, rect.xMax, rect.height), 
                    "Planets.TempManualModeTip".Translate());
                GUI.color = Color.white;
                return;
            }
            
            OverallTemperature temperatureCheck = this.temperature;
            string currentLabel = GetTemperatureLabel(this.temperature);
            
            this.temperature = (OverallTemperature)Mathf.RoundToInt(
                Widgets.HorizontalSlider(rect, (float)this.temperature, 0f, 
                (float)(OverallTemperatureUtility.EnumValuesCount - 1), true, 
                currentLabel, null, null, 1f));
                
            if (this.temperature != temperatureCheck) 
            { 
                this.CurrentWorldPreset = "Planets.Custom"; 
            }
        }

        private void DoAxialTiltSlider(float single, float width)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), "Planets.AxialTilt".Translate());
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            AxialTilt axialTiltCheck = Planets_GameComponent.axialTilt;
            
            string currentLabel = GetAxialTiltLabel(Planets_GameComponent.axialTilt);
            
            Planets_GameComponent.axialTilt = (AxialTilt)Mathf.RoundToInt(
                Widgets.HorizontalSlider(rect, (float)Planets_GameComponent.axialTilt, 0f, 
                (float)(AxialTiltUtility.EnumValuesCount - 1), true, 
                currentLabel, null, null, 1f));
            
            if (Planets_GameComponent.axialTilt != axialTiltCheck) 
            { 
                this.CurrentWorldPreset = "Planets.Custom";
                
                // Update temperature curve when axial tilt changes
                AxialTiltCurves.SetSeasonalCurve();
                
                if (Controller.DebugEnabled)
                {
                    RPLog.D($"[UI] Axial tilt changed to {Planets_GameComponent.axialTilt}, updated temperature curve");
                }
            }
        }
        
        // Vanilla parameters
        
        private void DoPopulationSlider(float single, float width)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), "PlanetPopulation".Translate());
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            OverallPopulation populationCheck = this.population;
            
            string currentLabel = GetPopulationLabel(this.population);
            
            this.population = (OverallPopulation)Mathf.RoundToInt(
                Widgets.HorizontalSlider(rect, (float)this.population, 0f, 
                (float)(OverallPopulationUtility.EnumValuesCount - 1), true, 
                currentLabel, null, null, 1f));
                
            if (this.population != populationCheck) 
            { 
                this.CurrentWorldPreset = "Planets.Custom"; 
            }
        }

        private void DoLandmarksSlider(float single, float width)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), "PlanetLandmarkDensity".Translate());
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            LandmarkDensity landmarkCheck = this.landmarkDensity;
            
            string currentLabel = GetLandmarkDensityLabel(this.landmarkDensity);
            
            this.landmarkDensity = (LandmarkDensity)Mathf.RoundToInt(
                Widgets.HorizontalSlider(rect, (float)this.landmarkDensity, 0f, 
                (float)(LandmarkDensityUtility.EnumValuesCount - 1), true, 
                currentLabel, null, null, 1f));
                
            if (this.landmarkDensity != landmarkCheck) 
            { 
                this.CurrentWorldPreset = "Planets.Custom"; 
            }
        }

        private float DoPollutionSlider(float yPos, float width)
        {
            yPos += 40f;
            
            Widgets.Label(new Rect(0f, yPos, 150f, 30f), "PlanetPollution".Translate());
            Rect rect = new Rect(150f, yPos, width - 150f, 30f);
            float pollutionCheck = this.pollution;
            
            this.pollution = Widgets.HorizontalSlider(rect, this.pollution, 0f, 1f, true, 
                this.pollution.ToStringPercent(), null, null, 0.05f);
                
            if (this.pollution != pollutionCheck) 
            { 
                this.CurrentWorldPreset = "Planets.Custom"; 
            }

            // Pollution Tweaks integration
            if (Compatibility.PollutionTweaksCompat.IsEnabled)
            {
                yPos += 40f;
                yPos = Compatibility.PollutionTweaksCompat.DrawControls(yPos, width);
            }

            return yPos;
        }
        
        // Mod integration sliders
        
        private void DoDensitySlider(float single, float width, string label, string tip, ref float value)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), label);
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            
            string currentLabel = value.ToStringPercent();
            float newValue = Widgets.HorizontalSlider(rect, value, 0f, 2f, true, currentLabel, null, null, 0.05f);
            
            if (newValue != value)
            {
                value = newValue;
                this.CurrentWorldPreset = "Planets.Custom";
            }
            
            TooltipHandler.TipRegion(new Rect(0f, single, rect.xMax, rect.height), tip);
        }

        private void DoMountainHeightSlider(float single, float width)
        {
            DrawMountainSlider(single, width, "Planets.MountainHeight".Translate(),
                Controller.Settings.climateConfig.mountainHeight, 0.5f, 1.5f,
                "Planets.MountainHeightTip".Translate());
        }

        private void DoMountainFrequencySlider(float single, float width)
        {
            DrawMountainSlider(single, width, "Planets.MountainFrequency".Translate(),
                Controller.Settings.climateConfig.mountainousness, 0f, 2f,
                "Planets.MountainFrequencyTip".Translate());
        }

        private void DrawMountainSlider(float single, float width, string label, ConfigValue<float> setting, float min, float max, string tip)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), label);
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            float previous = setting.CurrentValue;
            setting.CurrentValue = Widgets.HorizontalSlider(rect, previous, min, max, true, previous.ToStringPercent(), null, null, 0.05f);
            if (setting.CurrentValue != previous)
                this.CurrentWorldPreset = "Planets.Custom";
            TooltipHandler.TipRegion(new Rect(0f, single, rect.xMax, 30f), tip);
        }

        private void DoCaveSystemsSlider(float single, float width)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), "GeologicalLandforms.WorldParams.CaveSystems".Translate());
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            var cavesCheck = this.caveSystems;
            
            // Range 0-2 matches GL's expected range (0-200%)
            this.caveSystems = Widgets.HorizontalSlider(rect, this.caveSystems, 0f, 2f, true,
                this.caveSystems.ToStringPercent(), null, null, 0.05f);
                
            if (this.caveSystems != cavesCheck)
            {
                this.CurrentWorldPreset = "Planets.Custom";
                
                // Apply immediately for GL feedback
                if (Compatibility.GeologicalLandformsCompat.IsEnabled)
                {
                    Compatibility.GeologicalLandformsCompat.ApplyMountainCaveValues(this.mountains, this.caveSystems);
                }
            }
        }

        private void DoInsectTerritoryScaleSlider(float single, float width)
        {
            Widgets.Label(new Rect(0f, single, 150f, 30f), "VFEI_InsectTerritoryScale".Translate());
            Rect rect = new Rect(150f, single, width - 150f, 30f);
            var scaleCheck = this.insectTerritoryScale;
            
            this.insectTerritoryScale = Widgets.HorizontalSlider(rect, this.insectTerritoryScale, 0f, 2f, true, 
                this.insectTerritoryScale.ToStringPercent(), null, null, 0.05f);
                
            if (this.insectTerritoryScale != scaleCheck)
            {
                this.CurrentWorldPreset = "Planets.Custom";
            }
            
            TooltipHandler.TipRegion(new Rect(0f, single, rect.xMax, rect.height), "VFEI_InsectTerritoryScaleTip".Translate());
        }
        
        // ==================== Label Helper Methods ====================


        private string GetRainfallLabel(OverallRainfall rainfall)
        {
            // OverallRainfall has 7 values: AlmostNone, Little, LittleBitLess, Normal, LittleBitMore, High, VeryHigh
            switch (rainfall)
            {
                case OverallRainfall.AlmostNone:
                    return "Planets.Rainfall_VeryLow".Translate();
                case OverallRainfall.Little:
                    return "Planets.Rainfall_Low".Translate();
                case OverallRainfall.LittleBitLess:
                    return "Planets.Rainfall_SlightlyLow".Translate();
                case OverallRainfall.Normal:
                    return "Planets.Rainfall_Normal".Translate();
                case OverallRainfall.LittleBitMore:
                    return "Planets.Rainfall_SlightlyHigh".Translate();
                case OverallRainfall.High:
                    return "Planets.Rainfall_High".Translate();
                case OverallRainfall.VeryHigh:
                    return "Planets.Rainfall_VeryHigh".Translate();
                default:
                    return "Planets.Rainfall_Normal".Translate();
            }
        }

        private string GetTemperatureLabel(OverallTemperature temp)
        {
            // OverallTemperature has 7 values: VeryCold, Cold, LittleBitColder, Normal, LittleBitWarmer, Hot, VeryHot
            switch (temp)
            {
                case OverallTemperature.VeryCold:
                    return "Planets.Temperature_VeryLow".Translate();
                case OverallTemperature.Cold:
                    return "Planets.Temperature_Low".Translate();  // Use RP key for consistency
                case OverallTemperature.LittleBitColder:
                    return "Planets.Temperature_SlightlyLow".Translate();
                case OverallTemperature.Normal:
                    return "Planets.Temperature_Normal".Translate();  // Use RP key for consistency
                case OverallTemperature.LittleBitWarmer:
                    return "Planets.Temperature_SlightlyHigh".Translate();
                case OverallTemperature.Hot:
                    return "Planets.Temperature_High".Translate();  // Use RP key for consistency
                case OverallTemperature.VeryHot:
                    return "Planets.Temperature_VeryHigh".Translate();
                default:
                    return "Planets.Temperature_Normal".Translate();
            }
        }

        private string GetPopulationLabel(OverallPopulation pop)
        {
            // OverallPopulation has 7 values: AlmostNone, Little, LittleBitLess, Normal, LittleBitMore, High, VeryHigh
            switch (pop)
            {
                case OverallPopulation.AlmostNone:
                    return "Planets.Population_VeryLow".Translate();
                case OverallPopulation.Little:
                    return "Planets.Population_Low".Translate();  // Use RP key for consistency
                case OverallPopulation.LittleBitLess:
                    return "Planets.Population_SlightlyLow".Translate();
                case OverallPopulation.Normal:
                    return "Planets.Population_Normal".Translate();  // Use RP key for consistency
                case OverallPopulation.LittleBitMore:
                    return "Planets.Population_SlightlyHigh".Translate();
                case OverallPopulation.High:
                    return "Planets.Population_High".Translate();  // Use RP key for consistency
                case OverallPopulation.VeryHigh:
                    return "Planets.Population_VeryHigh".Translate();
                default:
                    return "Planets.Population_Normal".Translate();
            }
        }

        private string GetLandmarkDensityLabel(LandmarkDensity density)
        {
            switch (density)
            {
                case LandmarkDensity.Sparse:
                    return "PlanetLandmarkDensity_Sparse".Translate();
                case LandmarkDensity.SlightlyMoreSparse:
                    return "PlanetLandmarkDensity_SlightlyMoreSparse".Translate();
                case LandmarkDensity.SlightlySparse:
                    return "PlanetLandmarkDensity_SlightlySparse".Translate();
                case LandmarkDensity.Normal:
                    return "PlanetLandmarkDensity_Normal".Translate();
                case LandmarkDensity.SlightlyCrowded:
                    return "PlanetLandmarkDensity_SlightlyCrowded".Translate();
                case LandmarkDensity.SlightlyMoreCrowded:
                    return "PlanetLandmarkDensity_SlightlyMoreCrowded".Translate();
                case LandmarkDensity.Crowded:
                    return "PlanetLandmarkDensity_Crowded".Translate();
                default:
                    return "PlanetLandmarkDensity_Normal".Translate();
            }
        }

        private string GetAxialTiltLabel(AxialTilt tilt)
        {
            switch (tilt)
            {
                case AxialTilt.VeryLow:
                    return "Planets.AxialTilt_VeryLow".Translate();
                case AxialTilt.Low:
                    return "Planets.AxialTilt_Low".Translate();
                case AxialTilt.Normal:
                    return "Planets.AxialTilt_Normal".Translate();
                case AxialTilt.High:
                    return "Planets.AxialTilt_High".Translate();
                case AxialTilt.VeryHigh:
                    return "Planets.AxialTilt_VeryHigh".Translate();
                default:
                    return "Planets.AxialTilt_Normal".Translate();
            }
        }
        
        // ==================== Bottom Button Bar ====================
        
        private void DoCustomBottomButtons(Rect rect)
        {
            // Use local variable since Page.BottomButSize is protected
            Vector2 bottomButSize = new Vector2(150f, 38f);
            
            float y = rect.yMax - 38f;
            
            // We're treating the bottom as having 6 equal button slots:
            // [Back] [AdvClimate] [ResetAll] | [Random] [FC/empty] [Generate]
            // Back and Generate are locked to corners by base.DoBottomButtons
            
            // Calculate available space between Back and Generate buttons
            float availableStart = rect.x + bottomButSize.x;
            float availableEnd = rect.xMax - bottomButSize.x;
            float availableWidth = availableEnd - availableStart;
            
            // Calculate gap size based on available space
            // We need 5 gaps: [Back]-gap-[AdvClimate]-gap-[ResetAll]-gap-[Random]-gap-[FC]-gap-[Generate]
            float middleButtonsWidth = bottomButSize.x * 4;
            float buttonGap = (availableWidth - middleButtonsWidth) / 5f;
            
            // Start position for first middle button (Adv Climate slot)
            float middleStartX = availableStart + buttonGap;
            
            // Advanced dropdown (slot 1): Climate | Biomes - with visual feedback for non-default climate settings
            bool hasAdvancedSettings = HasNonDefaultClimateSettings();
            GUI.color = hasAdvancedSettings ? Color.green : Color.white;
            
            Rect advRect = new Rect(middleStartX, y, bottomButSize.x, bottomButSize.y);
            if (Widgets.ButtonText(advRect, "Planets.Advanced".Translate(), true, true, true, null))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                opts.Add(new FloatMenuOption("Planets.Climate".Translate(), () => Find.WindowStack.Add(new Dialog_AdvancedClimate(
                    Controller.Settings.climateConfig,
                    Controller.Settings.hydrologyConfig))));
                opts.Add(new FloatMenuOption("Planets.Biomes".Translate(), () => Find.WindowStack.Add(new Dialog_AdvancedBiomes())));
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            
            GUI.color = Color.white; // Reset color
            
            // Reset/Clear button (slot 2) - same pattern as faction preset row
            float resetX = middleStartX + bottomButSize.x + buttonGap;
            Rect resetClearRect = new Rect(resetX, y, bottomButSize.x, bottomButSize.y);
            if (Widgets.ButtonText(resetClearRect, "Planets.ResetClear".Translate(), true, true, true, null))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                opts.Add(new FloatMenuOption("Planets.ResetAll".Translate(), () =>
                {
                    ApplyCreationDefaults();
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }));
                opts.Add(new FloatMenuOption("Planets.SetAsDefaultWorldPreset".Translate(), ShowSetDefaultWorldGenPresetMenu));
                opts.Add(new FloatMenuOption("Planets.ClearDefaultWorldPreset".Translate(), () =>
                {
                    Controller.Settings.defaultWorldGenPreset.CurrentValue = null;
                    Controller.Settings.Write();
                    Messages.Message("Default world preset cleared.", MessageTypeDefOf.TaskCompletion, false);
                }));

                // Add Worldbuilder preview clear option if active
                if (WorldbuilderCompat.Instance.HasPreview)
                {
                    opts.Add(new FloatMenuOption("Planets.ClearMapPreview".Translate(), () =>
                    {
                        WorldbuilderCompat.ClearPreview();
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        Messages.Message("Map preview cleared.", MessageTypeDefOf.TaskCompletion, false);
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(opts));
            }
            TooltipHandler.TipRegion(resetClearRect, "Reset All: reload the default world preset, biome profile, and faction preset. Where a default has been cleared, use the built-in values. Clear Default: removes the auto-load world preset." + (WorldbuilderCompat.Instance.HasPreview ? " Clear Map Preview: resets the Worldbuilder map preview." : ""));
            
            // Random button (slot 3)
            float randomX = middleStartX + (bottomButSize.x * 2) + (buttonGap * 2);
            Rect randomRect = new Rect(randomX, y, bottomButSize.x, bottomButSize.y);
            if (Widgets.ButtonText(randomRect, "Planets.Random".Translate(), true, true, true, null))
            {
                this.Randomize();
            }
            
            // Mod Integration button (slot 4)
            // Dynamically populated from registered compatibility modules
            
            List<FloatMenuOption> modOptions = new List<FloatMenuOption>();
            
            // Get modules that have world creation buttons from registry
            var buttonModules = ModCompatibilityRegistry.GetWorldCreationButtonModules();
            
            foreach (var module in buttonModules)
            {
                modOptions.Add(new FloatMenuOption(module.ButtonLabel, () => 
                {
                    module.OnWorldCreationButtonClicked(this.factions);
                }));
            }
            
            if (modOptions.Count > 0)
            {
                float modBtnX = middleStartX + (bottomButSize.x * 3) + (buttonGap * 3);
                Rect modBtnRect = new Rect(modBtnX, y, bottomButSize.x, bottomButSize.y);
                
                string label = modOptions.Count == 1 ? modOptions[0].Label : "Planets.Mods".Translate();
                
                if (Widgets.ButtonText(modBtnRect, label, true, true, true, null))
                {
                    if (modOptions.Count == 1)
                    {
                        modOptions[0].action();
                    }
                    else
                    {
                        Find.WindowStack.Add(new FloatMenu(modOptions));
                    }
                }
            }
            
            // Generate button is at slot 5 (handled by base.DoBottomButtons)
            
            // Call the base DoBottomButtons for Back and Generate buttons
            base.DoBottomButtons(rect, "WorldGenerate".Translate(), null, null, true, true);
        }
        
        // ==================== World Preset Application ====================
        
        private void ApplyWorldPreset(WorldPreset worldPreset)
        {
            this.CurrentWorldPreset = worldPreset.Name;

            if (this.CurrentWorldPreset != "Planets.Custom")
            {
                Planets_GameComponent.seaLevel = worldPreset.SeaLevel;
                Planets_GameComponent.axialTilt = worldPreset.AxialTilt;
                this.rainfall = worldPreset.Rainfall;
                this.temperature = worldPreset.Temperature;
                this.population = worldPreset.Population;
                if (this.population < OverallPopulation.LittleBitLess)
                    this.population = OverallPopulation.LittleBitLess;
            }
        }

        private bool TryGetSparseSettlementWarning(out string warning)
        {
            warning = null;
            int factionsVisible = 0;
            if (this.factions != null)
            {
                for (int i = 0; i < this.factions.Count; i++)
                {
                    if (this.factions[i] != null && !this.factions[i].hidden)
                        factionsVisible++;
                }
            }
            if (factionsVisible <= 0)
                return false;

            double tiles = (10.0 * Math.Pow(3.0, Planets_GameComponent.subcount) + 2.0)
                * (1.0 - Math.Cos(this.planetCoverage * Math.PI)) / 2.0;
            float populationScale = PopulationScale(this.population);
            float compensation = 1f;
            if (Controller.Settings?.featureConfig?.compensateSettlementDensity?.CurrentValue != false)
                compensation = Mathf.Clamp(Mathf.Pow(3f, 10f - Planets_GameComponent.subcount), 1f, 27f);
            double expected = tiles / 100000.0 * 80.0 * populationScale * compensation;
            double extras = expected - factionsVisible;
            if (extras >= factionsVisible)
                return false;

            warning = "Planets.SparseSettlements".Translate(Mathf.RoundToInt((float)expected));
            return true;
        }

        private static float PopulationScale(OverallPopulation population)
        {
            switch (population)
            {
                case OverallPopulation.AlmostNone: return 0.1f;
                case OverallPopulation.Little: return 0.4f;
                case OverallPopulation.LittleBitLess: return 0.7f;
                case OverallPopulation.LittleBitMore: return 1.5f;
                case OverallPopulation.High: return 2f;
                case OverallPopulation.VeryHigh: return 2.75f;
                default: return 1f;
            }
        }
        
        /// <summary>
        /// Check if climate settings have been modified from defaults
        /// Used for visual feedback on the Advanced Climate button
        /// </summary>
        private bool HasNonDefaultClimateSettings()
        {
            var climate = Controller.Settings?.climateConfig;
            var hydrology = Controller.Settings?.hydrologyConfig;
            
            if (climate == null || hydrology == null)
                return false;
            
            return climate.HasModifications() || hydrology.HasModifications();
        }
        
        // ==================== World Generation ====================
        
        /// <summary>
        /// Runs before vanilla queues world generation, so mods that patch the vanilla method still see the final values.
        /// </summary>
        private void PrepareForGeneration()
        {
            Planets_GameComponent.generatingWithRP = true;
            Planets_GameComponent.worldPreset = this.CurrentWorldPreset;

            PlanetLayerSettingsDefOf.Surface.settings.subdivisions = Planets_GameComponent.subcount;
            if (WorldbuilderCompat.Instance.IsActive)
                WorldbuilderCompat.SyncPageToWorldbuilderData(this);

            if (!Planets_Initializer.VerifyReadyForWorldGen())
                RPLog.E("[UI] System verification failed, but attempting to continue");

            if (MyLittlePlanetCompat.IsEnabled)
                MyLittlePlanetCompat.SyncSubcountToMLP();

            AxialTiltCurves.SetSeasonalCurve();

            if (GeologicalLandformsCompat.IsEnabled)
                GeologicalLandformsCompat.ApplyMountainCaveValues(this.mountains, this.caveSystems);

            if (VFEInsectoidsCompat.IsEnabled)
                VFEInsectoidsCompat.SetInsectTerritoryScale(insectTerritoryScale);

            if (Controller.DebugEnabled)
            {
                RPLog.D("[UI] Pre-generation values:");
                RPLog.D($"  - Sea Level: {Planets_GameComponent.seaLevel}");
                RPLog.D($"  - Axial Tilt: {Planets_GameComponent.axialTilt}");
                RPLog.D($"  - Rainfall: {this.rainfall}");
                RPLog.D($"  - Temperature: {this.temperature}");
                RPLog.D($"  - Caves: {this.caveSystems:P0}");
            }
        }

        public override bool CanDoNext()
        {
            PrepareForGeneration();
            if (!ModsConfig.BiotechActive)
                this.pollution = 0f;
            return base.CanDoNext();
        }
    }
}