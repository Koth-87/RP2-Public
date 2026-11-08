using UnityEngine;
using Verse;
using RimWorld;

namespace Planets.Core.Configuration
{
    /// <summary>
    /// FEATURE TOGGLES
    /// Manages visual and gameplay feature flags.
    /// Tier 2 (advanced hydrology) is gated by Tier 1 and Odyssey DLC.
    /// </summary>
    public class FeatureConfiguration
    {
        // RP2 feature toggles (non-lighting).
        public ConfigValue<bool> earthScaleHydrology = new ConfigValue<bool>(true, "earthScaleHydrology");
        public ConfigValue<bool> advancedHydrology = new ConfigValue<bool>(true, "advancedHydrology");
        public ConfigValue<bool> compensateSettlementDensity = new ConfigValue<bool>(true, "compensateSettlementDensity");
        public ConfigValue<bool> enableDiurnalModel = new ConfigValue<bool>(true, "enableDiurnalModel");
        public ConfigValue<bool> enableWeatherDiurnal = new ConfigValue<bool>(true, "enableWeatherDiurnal");
        public ConfigValue<bool> enableSeasonalAmplitudeModel = new ConfigValue<bool>(true, "enableSeasonalAmplitudeModel");
        public ConfigValue<bool> enableMinMaxTempModel = new ConfigValue<bool>(true, "enableMinMaxTempModel");
        public ConfigValue<bool> enableTemperatureCurves = new ConfigValue<bool>(true, "enableTemperatureCurves");
        public ConfigValue<bool> enablePlantDormancy = new ConfigValue<bool>(true, "enablePlantDormancy");
        public ConfigValue<bool> enableShortPlantRest = new ConfigValue<bool>(true, "enableShortPlantRest");

        private Vector2 featuresScrollPos = Vector2.zero;
        private float featuresTabHeight = 900f;

        /// <summary>True when Tier 1 hydrology is enabled (drainage-based swampiness, elevation bonuses, river filtering).</summary>
        public bool IsTier1HydrologyEnabled => earthScaleHydrology.CurrentValue;

        /// <summary>True when Tier 2 hydrology should run: Tier 1 on, advanced on, Odyssey DLC active, and Tier 2 available.</summary>
        public bool IsTier2HydrologyEnabled =>
            Controller.Tier2HydrologyAvailable
            && earthScaleHydrology.CurrentValue && advancedHydrology.CurrentValue && ModsConfig.OdysseyActive;

        /// <summary>Save/load settings</summary>
        public void ExposeData()
        {
            earthScaleHydrology.Look();
            advancedHydrology.Look();
            compensateSettlementDensity.Look();
            enableDiurnalModel.Look();
            enableWeatherDiurnal.Look();
            enableSeasonalAmplitudeModel.Look();
            enableMinMaxTempModel.Look();
            enableTemperatureCurves.Look();
            enablePlantDormancy.Look();
            enableShortPlantRest.Look();
        }

        /// <summary>Render Feature UI. Optionally pass hydrologyConfig to show river and Tier 2 sub-toggles.</summary>
        public void RenderUI(Rect canvas, HydrologyConfiguration hydrologyConfig = null)
        {
            float viewWidth = canvas.width - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, featuresTabHeight);
            Widgets.BeginScrollView(canvas, ref featuresScrollPos, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.ColumnWidth = viewWidth - 8f; // Leave margin so checkboxes aren't cut off on the right
            list.Begin(viewRect);

            GUI.color = Color.cyan;
            list.Label("World Generation");
            GUI.color = Color.white;
            list.Gap(4);
            list.CheckboxLabeled("Compensate settlement density", ref compensateSettlementDensity.CurrentValue,
                "Smaller planets have fewer tiles, so vanilla gives each faction only one settlement. This raises settlement density on planet scales below 10 so factions still spread out. Scale 10 is unchanged, and the result stays under 8% of land tiles.");
            list.Gap(6);

            GUI.color = Color.cyan;
            list.Label("Hydrology & Rivers");
            GUI.color = Color.white;
            list.Gap(4);
            list.CheckboxLabeled("Drainage-based swampiness", ref earthScaleHydrology.CurrentValue,
                "Swampiness follows how much land drains through a flat, wet, low tile, and hilliness adds elevation resistance for river pathfinding. Off uses a climate-only swampiness estimate.");
            if (hydrologyConfig != null)
            {
                list.CheckboxLabeled("Custom rivers (RP2)", ref hydrologyConfig.CustomRiversEnabled.CurrentValue,
                    "Places rivers along RP2's drainage. Where a lake spills into a river that gets drawn, that river continues up the streams that fed the lake. Off lets vanilla generate rivers.");
                if (hydrologyConfig.CustomRiversEnabled.CurrentValue)
                {
                    list.CheckboxLabeled("    Rivers can start at lakes", ref hydrologyConfig.riversIntoLakes.CurrentValue,
                        "Also draws rivers that end in a lake. A lake that already spills into a drawn river is followed from downstream instead of starting another one.");
                    list.Label($"    Minimum river length: {hydrologyConfig.minRiverLengthTiles.CurrentValue} tiles", -1f,
                        new TipSignal("Rivers spanning fewer than this many land tiles are not drawn. Default 3 removes 1–2 tile coastal rivers."));
                    hydrologyConfig.minRiverLengthTiles.CurrentValue = Mathf.RoundToInt(
                        list.Slider(hydrologyConfig.minRiverLengthTiles.CurrentValue, 1f, 10f));
                }
            }

            if (earthScaleHydrology.CurrentValue && Controller.Tier2HydrologyAvailable)
            {
                list.Gap(4);
                bool odysseyActive = ModsConfig.OdysseyActive;
                bool prevEnabled = GUI.enabled;
                Color prevColor = GUI.color;

                if (!odysseyActive)
                {
                    GUI.enabled = false;
                    GUI.color = Color.gray;
                }

                list.CheckboxLabeled("Closed-basin fill & spill hydrology (Tier 2)", ref advancedHydrology.CurrentValue,
                    "Simulates closed basins using surface runoff (rainfall minus land evaporation) and open-water lake evaporation. Wet basins fill into multi-tile lakes or single-tile pools and spill into downstream watersheds; dry endorheic basins terminate inland. Rivers and swampiness follow the resulting flow network. Requires the Odyssey DLC.");

                GUI.enabled = prevEnabled;
                GUI.color = prevColor;

                if (!odysseyActive)
                {
                    GUI.color = Color.gray;
                    list.Label("    Requires Odyssey DLC.");
                    GUI.color = Color.white;
                }

                if (IsTier2HydrologyEnabled && hydrologyConfig != null)
                {
                    list.CheckboxLabeled("    Lake conversion (filled depressions)", ref hydrologyConfig.LakeConversionEnabled.CurrentValue,
                        "Converts multi-tile basins (2 or more tiles) that flood in the fill-and-spill water balance into Lake biomes.");
                    list.CheckboxLabeled("    Wetland & Marshy mutators", ref hydrologyConfig.WetlandMutatorsEnabled.CurrentValue,
                        "Places an Odyssey Wetland or Marshy mutator on the wettest tile of each connected high-swampiness patch.");
                }
            }
            list.Gap(6);

            GUI.color = Color.cyan;
            list.Label("Temperature");
            GUI.color = Color.white;
            list.Gap(4);
            list.CheckboxLabeled("Seasonal temperature amplitude", ref enableSeasonalAmplitudeModel.CurrentValue,
                "How far temperature swings between summer and winter. RP2 builds that swing from axial tilt, oceans, distance inland, and elevation, and growing seasons use it. Surface tiles only. Off uses vanilla's latitude curve.");
            list.CheckboxLabeled("Summer and winter temperatures", ref enableMinMaxTempModel.CurrentValue,
                "The hottest and coldest seasonal temperatures on a tile. Tile tips and growing-period checks read these. RP2 stores them from its climate. Surface tiles only. Off makes vanilla estimate them from the tile's average temperature.");
            list.CheckboxLabeled("Day and night temperature swing", ref enableDiurnalModel.CurrentValue,
                "How much outdoor temperature rises and falls through the day. The size of the swing is the daily range: dry inland tiles swing more than wet coasts, and land above 2 km swings a little more. The sun's height through the day sets the shape. A day that never rises stays flat. Surface tiles only. Off uses vanilla's fixed 7°C swing.");
            list.CheckboxLabeled("Weather shrinks the day-night swing", ref enableWeatherDiurnal.CurrentValue,
                "On a colony map, rain, snow, fog, and a dark sky pull that swing toward the daily average. A clear day keeps the full swing. A downpour nearly flattens it. Days run cooler and nights run warmer. A world tile with no colony map is unchanged. Requires the day-night swing above. Off keeps the clear-day swing.");
            list.CheckboxLabeled("Temperature preset curves", ref enableTemperatureCurves.CurrentValue,
                "How the world-creation temperature setting, from Very Cold to Very Hot, turns latitude into a temperature offset. These curves match RP2's climate baselines. Normal always keeps the vanilla curve. Off uses vanilla's curves for every setting.");
            list.Gap(6);

            GUI.color = Color.cyan;
            list.Label("Plants");
            GUI.color = Color.white;
            list.Gap(4);
            list.CheckboxLabeled("Winter dormancy", ref enablePlantDormancy.CurrentValue,
                "Outdoor plants that can drop their leaves survive a long night instead of dying after about a week without sun. Plants under a roof still die in the dark. Follows the sun in the sky.");
            list.CheckboxLabeled("Rest only in the deep night", ref enableShortPlantRest.CurrentValue,
                "Plants that need light rest for four hours around midnight, and only while the sun is down. A midnight sun does not count as night. Off uses vanilla's longer rest.");

            list.End();
            featuresTabHeight = list.CurHeight + 12f;
            Widgets.EndScrollView();
        }

        /// <summary>Reset all settings to defaults</summary>
        public void ResetToDefaults()
        {
            earthScaleHydrology.Reset();
            advancedHydrology.Reset();
            compensateSettlementDensity.Reset();
            enableDiurnalModel.Reset();
            enableWeatherDiurnal.Reset();
            enableSeasonalAmplitudeModel.Reset();
            enableMinMaxTempModel.Reset();
            enableTemperatureCurves.Reset();
            enablePlantDormancy.Reset();
            enableShortPlantRest.Reset();
        }
    }
}
