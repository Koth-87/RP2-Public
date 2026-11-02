using Verse;
using UnityEngine;
using System.Collections.Generic;
using RimWorld;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Presets.Biomes;
using Planets.Presets.Factions;
using Planets.Presets.WorldGen;
using Planets.Utilities.Logging;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Geography;

namespace Planets.Core
{
    /// <summary>
    /// SETTINGS UI ORCHESTRATOR
    /// ========================
    /// Provides the mod settings interface in Mod Settings menu.
    /// Tab order: Main, Features, Integrations, Profiles/Presets, MMF.
    /// Each tab delegates to its configuration object via RenderUI(Rect); Main tab keeps display settings inline.
    /// Biome configuration is handled per-world via Dialog_AdvancedBiomes (World Creation > Advanced Climate > Biomes).
    /// </summary>
    public class ModSettings : Verse.ModSettings
    {
        // ========== CONFIGURATION OBJECTS ==========

        public FeatureConfiguration featureConfig = new FeatureConfiguration();
        public BiomeConfiguration biomeConfig = new BiomeConfiguration();
        public IntegrationConfiguration integrationConfig = new IntegrationConfiguration();
        public MMFConfiguration mmfConfig = new MMFConfiguration();
        public ClimateConfiguration climateConfig = new ClimateConfiguration();
        public HydrologyConfiguration hydrologyConfig = new HydrologyConfiguration();
        public ProfileManagerConfiguration profileManagerConfig = new ProfileManagerConfiguration();

        // ========== DISPLAY SETTINGS (inline, small) ==========

        /// <summary>Show detailed stone type information in biome tooltip</summary>
        public ConfigValue<bool> showStoneTypes = new ConfigValue<bool>(true, "showStoneTypes");

        /// <summary>Show growing period information in biome tooltip</summary>
        public ConfigValue<bool> showGrowingPeriod = new ConfigValue<bool>(true, "showGrowingPeriod");

        /// <summary>Show disease frequency information in biome tooltip</summary>
        public ConfigValue<bool> showDiseaseFrequency = new ConfigValue<bool>(true, "showDiseaseFrequency");

        /// <summary>Show Map Mode Framework (MMF) UI on the world map</summary>
        public ConfigValue<bool> showMMFUI = new ConfigValue<bool>(true, "showMMFUI");

        /// <summary>Mod Debug Mode checkbox. Takes effect only while RimWorld Dev Mode is on, and is cleared when Dev Mode is off.</summary>
        public ConfigValue<bool> debugMode = new ConfigValue<bool>(false, "debugMode");

        /// <summary>Default biome profile to load on world creation (optional). Applied first; per-biome defaults then override for each biome.</summary>
        public ConfigValue<string> defaultBiomeProfile = new ConfigValue<string>(null, "defaultBiomeProfile");

        /// <summary>Default faction preset to load on world creation (optional).</summary>
        public ConfigValue<string> defaultFactionProfile = new ConfigValue<string>(null, "defaultFactionProfile");

        /// <summary>Default world generation preset to load on world creation (optional).</summary>
        public ConfigValue<string> defaultWorldGenPreset = new ConfigValue<string>(null, "defaultWorldGenPreset");

        /// <summary>Per-biome default preset names. Key = biome defName, Value = preset name. Applied after default biome profile, overriding it for those biomes.</summary>
        public Dictionary<string, string> defaultPresetPerBiome = new Dictionary<string, string>();
        private List<string> defaultPresetPerBiomeKeys;
        private List<string> defaultPresetPerBiomeValues;

        // ========== PERFORMANCE SETTINGS ==========

        public ConfigValue<bool> perfParallelHydrology = new ConfigValue<bool>(true, "perfParallelHydrology");
        public ConfigValue<bool> perfParallelTemperature = new ConfigValue<bool>(true, "perfParallelTemperature");
        public ConfigValue<bool> perfOptimizeGL = new ConfigValue<bool>(true, "perfOptimizeGL");

        // ========== UI STATE ==========

        private enum SettingsTab { Main, Features, Integrations, ProfileManager, MMF }
        private static SettingsTab currentTab = SettingsTab.Main;
        private Vector2 mainTabScroll;
        private float mainTabHeight = 1200f;

        // ========== TAB RENDERING ==========

        public void DoSettingsWindowContents(Rect canvas)
        {
            // Vanilla draws the mod title in the 40px above this rect and leaves 150px on the right for a button.
            Rect resetRect = new Rect(canvas.xMax - 150f - 17f, canvas.y - 40f, 150f, 35f);
            if (Widgets.ButtonText(resetRect, "Reset to defaults"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Reset every control on this screen to its default? Saved profiles stay. World creation and Advanced Climate are not changed.",
                    ResetPageToDefaults,
                    true));
            }

            float startY = 40f;

            // Draw tab buttons (order: Main, Features, Integrations, Profiles/Presets, MMF)
            Rect tabRect = new Rect(canvas.x, canvas.y + startY, canvas.width, 30f);
            List<TabRecord> tabs = new List<TabRecord>
            {
                new TabRecord("Main", () => currentTab = SettingsTab.Main, currentTab == SettingsTab.Main),
                new TabRecord("Features", () => currentTab = SettingsTab.Features, currentTab == SettingsTab.Features),
                new TabRecord("Integrations", () => currentTab = SettingsTab.Integrations, currentTab == SettingsTab.Integrations),
                new TabRecord("Profiles/Presets", () => currentTab = SettingsTab.ProfileManager, currentTab == SettingsTab.ProfileManager),
                new TabRecord("MMF", () => currentTab = SettingsTab.MMF, currentTab == SettingsTab.MMF),
            };

            TabDrawer.DrawTabs(tabRect, tabs);

            // Content area (below tabs)
            Rect contentRect = new Rect(canvas.x, canvas.y + startY + 35f, canvas.width, canvas.height - startY - 35f);

            switch (currentTab)
            {
                case SettingsTab.Main:
                    RenderMainTab(contentRect);
                    break;
                case SettingsTab.Features:
                    featureConfig.RenderUI(contentRect, hydrologyConfig);
                    break;
                case SettingsTab.Integrations:
                    integrationConfig.RenderUI(contentRect);
                    break;
                case SettingsTab.ProfileManager:
                    profileManagerConfig.RenderUI(contentRect);
                    break;
                case SettingsTab.MMF:
                    mmfConfig.RenderUI(contentRect);
                    break;
            }
        }

        /// <summary>Main tab: Display settings</summary>
        private void RenderMainTab(Rect canvas)
        {
            float viewWidth = canvas.width - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, mainTabHeight);
            Widgets.BeginScrollView(canvas, ref mainTabScroll, viewRect);
            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.ColumnWidth = viewWidth;
            list.Begin(viewRect);

            GUI.color = Color.cyan;
            list.Label("Display Settings");
            GUI.color = Color.white;
            list.Gap(6);
            list.CheckboxLabeled("Show Stone Types", ref showStoneTypes.CurrentValue);
            list.CheckboxLabeled("Show Growing Period", ref showGrowingPeriod.CurrentValue);
            list.CheckboxLabeled("Show Disease Frequency", ref showDiseaseFrequency.CurrentValue);
            list.CheckboxLabeled("Show Map Mode Framework (MMF) UI", ref showMMFUI.CurrentValue);

            list.Gap(12);

            if (!Prefs.DevMode)
            {
                debugMode.CurrentValue = false;
            }
            else
            {
                list.CheckboxLabeled("Debug Mode", ref debugMode.CurrentValue,
                    "Extra RP2 diagnostics. Shown only while RimWorld Dev Mode is on, and it starts unchecked. Dev Mode alone does not turn these on.");

                if (debugMode.CurrentValue)
                {
                    list.Gap();
                    GUI.color = Color.yellow;
                    list.Label("Performance Optimizations");
                    GUI.color = Color.white;
                    list.CheckboxLabeled("Parallel Hydrology", ref perfParallelHydrology.CurrentValue);
                    list.CheckboxLabeled("Parallel Temperature", ref perfParallelTemperature.CurrentValue);
                    list.CheckboxLabeled("GL Compatibility Opt", ref perfOptimizeGL.CurrentValue);

                    if (Controller.EarthHeightmapAvailable)
                    {
                        list.Gap();
                        list.CheckboxLabeled("Earth heightmap", ref climateConfig.useEarthHeightmap.CurrentValue,
                            "Off by default. On a 100% world, elevation comes from the ETOPO file below instead of the noise. Sea level 0 is Earth's coastline. Mountain height and the polar landmasses are skipped. Any other coverage generates the usual world.");
                        if (climateConfig.useEarthHeightmap.CurrentValue)
                        {
                            Rect pathRect = list.GetRect(30f);
                            string path = climateConfig.earthHeightmapPath.CurrentValue ?? "";
                            climateConfig.earthHeightmapPath.CurrentValue = Widgets.TextField(pathRect, path);
                        }
                    }
                }
            }

            list.End();
            mainTabHeight = list.CurHeight + 12f;
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Defaults for the controls on this screen. Advanced Climate, the biome editor, and saved profile files stay.
        /// </summary>
        private void ResetPageToDefaults()
        {
            showStoneTypes.Reset();
            showGrowingPeriod.Reset();
            showDiseaseFrequency.Reset();
            showMMFUI.Reset();
            debugMode.Reset();
            perfParallelHydrology.Reset();
            perfParallelTemperature.Reset();
            perfOptimizeGL.Reset();
            climateConfig.useEarthHeightmap.Reset();
            climateConfig.earthHeightmapPath.Reset();
            featureConfig.ResetToDefaults();
            hydrologyConfig.ResetFeatureToggles();
            integrationConfig.ResetPageDefaults();
            defaultBiomeProfile.Reset();
            defaultFactionProfile.Reset();
            defaultWorldGenPreset.Reset();
            defaultPresetPerBiome = new Dictionary<string, string>();
            mmfConfig.ResetPageDefaults();
            Controller.Instance?.WriteSettings();
            Messages.Message("Realistic Planets mod settings reset to defaults.", MessageTypeDefOf.TaskCompletion, false);
        }

        // ========== SERIALIZATION ==========

        public override void ExposeData()
        {
            // Display settings
            showStoneTypes.Look();
            showGrowingPeriod.Look();
            showDiseaseFrequency.Look();
            showMMFUI.Look();
            debugMode.Look();
            defaultBiomeProfile.Look();
            defaultFactionProfile.Look();
            defaultWorldGenPreset.Look();
            Scribe_Collections.Look(ref defaultPresetPerBiome, "defaultPresetPerBiome", LookMode.Value, LookMode.Value, ref defaultPresetPerBiomeKeys, ref defaultPresetPerBiomeValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && defaultPresetPerBiome == null)
                defaultPresetPerBiome = new Dictionary<string, string>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ClearDanglingDefaults();

            // Performance settings
            perfParallelHydrology.Look();
            perfParallelTemperature.Look();
            perfOptimizeGL.Look();

            // Configuration objects
            featureConfig.ExposeData();
            biomeConfig.ExposeData();
            integrationConfig.ExposeData();
            mmfConfig.ExposeData();
            climateConfig.ExposeData();
            hydrologyConfig.ExposeData();
            // Calibration data lives in biomeConfig.BiomeMetadataMap

            base.ExposeData();
        }

        /// <summary>
        /// Drop default names whose files are gone. Called after settings load, and the cleared names are written back.
        /// </summary>
        private void ClearDanglingDefaults()
        {
            var cleared = new List<string>();

            if (!string.IsNullOrEmpty(defaultBiomeProfile.CurrentValue) && !BiomeProfileManager.ProfileExists(defaultBiomeProfile.CurrentValue))
            {
                cleared.Add($"biome profile '{defaultBiomeProfile.CurrentValue}'");
                defaultBiomeProfile.CurrentValue = null;
            }

            if (!string.IsNullOrEmpty(defaultWorldGenPreset.CurrentValue) && !WorldGenPresetManager.PresetExists(defaultWorldGenPreset.CurrentValue))
            {
                cleared.Add($"world preset '{defaultWorldGenPreset.CurrentValue}'");
                defaultWorldGenPreset.CurrentValue = null;
            }

            if (!string.IsNullOrEmpty(defaultFactionProfile.CurrentValue) && !FactionPresetManager.PresetExists(defaultFactionProfile.CurrentValue))
            {
                cleared.Add($"faction preset '{defaultFactionProfile.CurrentValue}'");
                defaultFactionProfile.CurrentValue = null;
            }

            if (defaultPresetPerBiome != null)
            {
                var missing = new List<string>();
                foreach (var kvp in defaultPresetPerBiome)
                {
                    if (string.IsNullOrEmpty(kvp.Value) || !PerBiomePresetManager.PresetExists(kvp.Key, kvp.Value))
                        missing.Add(kvp.Key);
                }
                foreach (string key in missing)
                {
                    cleared.Add($"preset '{defaultPresetPerBiome[key]}' for {key}");
                    defaultPresetPerBiome.Remove(key);
                }
            }

            if (cleared.Count == 0)
                return;

            string text = "Cleared missing defaults: " + string.Join(", ", cleared);
            RPLog.W("[Settings] " + text);
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Messages.Message(text + ".", MessageTypeDefOf.CautionInput, false);
                Controller.Instance?.WriteSettings();
            });
        }
    }
}
