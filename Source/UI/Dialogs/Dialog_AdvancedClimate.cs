using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.WorldGen.Constants;

namespace Planets.UI.Dialogs
{
    /// <summary>
    /// ADVANCED CLIMATE SETTINGS DIALOG (WS24)
    /// ========================================
    /// Outcome-oriented tuning interface for climate generation parameters, with a collapsible
    /// DevMode fold exposing the underlying physical model constants.
    /// Accessed via: World Creation > Advanced Climate.
    /// </summary>
    public class Dialog_AdvancedClimate : Window
    {
        private enum ClimateTab
        {
            Temperature,
            Rainfall,
            Moisture,
            Hydrology
        }

        private ClimateTab currentTab = ClimateTab.Temperature;

        private readonly ClimateConfiguration climateConfig;
        private readonly HydrologyConfiguration hydroConfig;

        private Vector2 temperatureScrollPos = Vector2.zero;
        private Vector2 rainfallScrollPos = Vector2.zero;
        private Vector2 moistureScrollPos = Vector2.zero;
        private Vector2 hydrologyScrollPos = Vector2.zero;

        private float temperatureHeight = 900f;
        private float rainfallHeight = 700f;
        private float moistureHeight = 900f;
        private float hydrologyHeight = 600f;

        private bool showDevConstantsTemp;
        private bool showDevConstantsMoisture;

        public override Vector2 InitialSize => new Vector2(900f, 700f);
        public override float Margin => 18f;

        public Dialog_AdvancedClimate(ClimateConfiguration climate, HydrologyConfiguration hydro)
        {
            this.doCloseButton = false;
            this.doCloseX = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = true;

            climateConfig = climate ?? throw new System.ArgumentNullException(nameof(climate));
            hydroConfig = hydro ?? throw new System.ArgumentNullException(nameof(hydro));
        }

        private static string FormatKm(float km)
        {
            float tiles = km / Mathf.Max(1f, EarthScaleConstants.KM_PER_TILE);
            return $"{km:F0} km (~{tiles:F1} tiles)";
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            string titleText = "Planets.AdvancedClimateTitle".Translate();
            float titleHeight = Text.CalcHeight(titleText, rect.width);
            Widgets.Label(new Rect(0f, 0f, rect.width, titleHeight), titleText);
            Text.Font = GameFont.Small;

            float currentY = titleHeight + 6f;

            GUI.color = Color.yellow;
            string warningText = "Planets.AdvancedClimateWarning".Translate();
            float warningHeight = Text.CalcHeight(warningText, rect.width);
            Widgets.Label(new Rect(0f, currentY, rect.width, warningHeight), warningText);
            GUI.color = Color.white;
            currentY += warningHeight + 40f;

            bool tier2Hydrology = Controller.Settings?.featureConfig?.IsTier2HydrologyEnabled == true;
            if (currentTab == ClimateTab.Hydrology && !tier2Hydrology)
                currentTab = ClimateTab.Temperature;

            Rect tabRect = new Rect(0f, currentY, rect.width, 30f);
            List<TabRecord> tabs = new List<TabRecord>
            {
                new TabRecord("Temperature", () => currentTab = ClimateTab.Temperature, currentTab == ClimateTab.Temperature),
                new TabRecord("Planets.ClimateTab.RainfallTerrain".Translate(), () => currentTab = ClimateTab.Rainfall, currentTab == ClimateTab.Rainfall),
                new TabRecord("Planets.ClimateTab.RainfallMoisture".Translate(), () => currentTab = ClimateTab.Moisture, currentTab == ClimateTab.Moisture),
            };
            if (tier2Hydrology)
                tabs.Add(new TabRecord("Hydrology", () => currentTab = ClimateTab.Hydrology, currentTab == ClimateTab.Hydrology));
            TabDrawer.DrawTabs(tabRect, tabs);
            currentY += 30f;

            Rect contentRect = new Rect(10f, currentY, rect.width - 20f, rect.height - currentY - 50f);

            switch (currentTab)
            {
                case ClimateTab.Temperature:
                    DoTemperatureTab(contentRect);
                    break;
                case ClimateTab.Rainfall:
                    DoRainfallTab(contentRect);
                    break;
                case ClimateTab.Moisture:
                    DoMoistureTab(contentRect);
                    break;
                case ClimateTab.Hydrology:
                    DoHydrologyTab(contentRect);
                    break;
            }

            float bottomY = rect.height - 40f;
            float buttonWidth = 120f;

            if (Widgets.ButtonText(new Rect(0f, bottomY, buttonWidth, 30f), "Planets.ResetAll".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Planets.ResetAllClimateConfirm".Translate(),
                    () =>
                    {
                        climateConfig.ResetToDefaults();
                        hydroConfig.ResetToDefaults();
                    }
                ));
            }

            if (Widgets.ButtonText(new Rect(rect.width - buttonWidth, bottomY, buttonWidth, 30f), "Close".Translate()))
            {
                this.Close(true);
            }
        }

        private void DoTemperatureTab(Rect rect)
        {
            float viewWidth = rect.width - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, temperatureHeight);

            Widgets.BeginScrollView(rect, ref temperatureScrollPos, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.ColumnWidth = viewWidth;
            list.Begin(viewRect);

            // Manual Temperature Targets
            DrawSectionHeader(list, "Planets.ClimateSection.ManualTempTargets".Translate());
            list.CheckboxLabeled("Use manual polar and equatorial targets",
                ref climateConfig.manualTemperatureEnabled.CurrentValue,
                "When enabled, the temperature slider on the world creation page is disabled. Instead, set polar and equatorial mean temperatures here.");
            list.Gap(6f);
            if (climateConfig.manualTemperatureEnabled.CurrentValue)
            {
                list.Label($"Polar mean temperature: {climateConfig.manualPolarTemp.CurrentValue:F0}°C");
                climateConfig.manualPolarTemp.CurrentValue = list.Slider(climateConfig.manualPolarTemp.CurrentValue, -90f, 20f);
                DrawDescription(list, "Target annual mean temperature at the poles. Earth-like: -29.5°C. Range: -90 (ice world) to 20°C.");
                list.Gap(6f);
                list.Label($"Equatorial mean temperature: {climateConfig.manualEquatorialTemp.CurrentValue:F0}°C");
                climateConfig.manualEquatorialTemp.CurrentValue = list.Slider(climateConfig.manualEquatorialTemp.CurrentValue, 0f, 60f);
                DrawDescription(list, "Target annual mean temperature at the equator. Earth-like: 31°C. Range: 0 to 60°C.");
                list.Gap(12f);
            }

            // Outcome Multipliers (Temperature & Ocean Moderation)
            DrawSectionHeader(list, "Planets.ClimateSection.CoastalModeration".Translate());
            list.Label($"Ocean moderation reach: {climateConfig.grpOceanReach.CurrentValue:F2}x (effective fade {FormatKm(climateConfig.EffectiveMaritimeFadeKm)})");
            climateConfig.grpOceanReach.CurrentValue = list.Slider(climateConfig.grpOceanReach.CurrentValue, 0f, 2f);
            DrawDescription(list, "Higher: coasts stay milder farther inland and seasonal swings stay smaller downwind of oceans. Map modes: Continental vs Maritime, Maritime Air Share, Temperature.");
            list.Gap(12f);

            DrawSectionHeader(list, "Planets.ClimateSection.OceanCurrents".Translate());
            list.Label($"Ocean currents strength: {climateConfig.grpCurrents.CurrentValue:F2}x");
            climateConfig.grpCurrents.CurrentValue = list.Slider(climateConfig.grpCurrents.CurrentValue, 0f, 2f);
            DrawDescription(list, "Higher: stronger warm western-boundary currents (Gulf Stream), cool eastern-boundary upwelling, and mid-latitude storm-track warming. Map modes: Ocean Currents (SST Anomaly), Temperature.");
            list.Gap(12f);

            // Latitude & Interior Curve
            DrawSectionHeader(list, "Planets.ClimateSection.LatTempCurve".Translate());
            list.Label($"Latitude curve exponent: {climateConfig.latTempExponent.CurrentValue:F2}");
            climateConfig.latTempExponent.CurrentValue = list.Slider(climateConfig.latTempExponent.CurrentValue, 0.8f, 2.0f);
            DrawDescription(list, "Controls how temperature changes from equator to poles. Lower = gentler gradient, Higher = sharper (more extreme poles). Map mode: Temperature.");
            list.Gap(12f);

            list.Label($"Polar cold penalty: {climateConfig.polarColdExtra.CurrentValue:F1}°C");
            climateConfig.polarColdExtra.CurrentValue = list.Slider(climateConfig.polarColdExtra.CurrentValue, -12f, 0f);
            DrawDescription(list, "Extra cooling applied above 70° latitude, reaching the full value at the pole. Map mode: Temperature.");
            list.Gap(12f);

            list.Label($"Inland temperature penalty: {climateConfig.inlandMeanPenalty.CurrentValue:F2}°C");
            climateConfig.inlandMeanPenalty.CurrentValue = list.Slider(climateConfig.inlandMeanPenalty.CurrentValue, 0f, 2f);
            DrawDescription(list, "Additional annual cooling for deep continental interiors. Map modes: Temperature, Continental vs Maritime.");
            list.Gap(12f);

            if (Prefs.DevMode)
            {
                list.GapLine();
                list.CheckboxLabeled("Model constants (developer)", ref showDevConstantsTemp,
                    "Raw maritime and ocean-current model constants. Scaled by the outcome multipliers above.");
                if (showDevConstantsTemp)
                {
                    list.Gap(6f);
                    if (list.ButtonText("Reset developer constants"))
                        climateConfig.ResetModelConstants();
                    list.Gap(6f);

                    list.Label($"Maritime inland fade distance: {FormatKm(climateConfig.maritimeFadeKm.CurrentValue)}");
                    climateConfig.maritimeFadeKm.CurrentValue = list.Slider(climateConfig.maritimeFadeKm.CurrentValue, 500f, 6000f);

                    list.Label($"Maritime storm-track drift gain: {climateConfig.maritimeStormDrift.CurrentValue:F2}");
                    climateConfig.maritimeStormDrift.CurrentValue = list.Slider(climateConfig.maritimeStormDrift.CurrentValue, 0f, 2.5f);

                    list.Label($"Continental air over ocean fade: {FormatKm(climateConfig.maritimeOceanFadeKm.CurrentValue)}");
                    climateConfig.maritimeOceanFadeKm.CurrentValue = list.Slider(climateConfig.maritimeOceanFadeKm.CurrentValue, 500f, 4000f);

                    list.Label($"Coastal sea-breeze fringe: {FormatKm(climateConfig.maritimeBreezeKm.CurrentValue)}");
                    climateConfig.maritimeBreezeKm.CurrentValue = list.Slider(climateConfig.maritimeBreezeKm.CurrentValue, 10f, 150f);

                    list.Label($"Winter weight in continentality: {climateConfig.maritimeWinterWeight.CurrentValue:F2}");
                    climateConfig.maritimeWinterWeight.CurrentValue = list.Slider(climateConfig.maritimeWinterWeight.CurrentValue, 0f, 1f);

                    list.Label($"Coastal SST blend weight: {climateConfig.maritimeBlendWeight.CurrentValue:F2}");
                    climateConfig.maritimeBlendWeight.CurrentValue = list.Slider(climateConfig.maritimeBlendWeight.CurrentValue, 0f, 1f);

                    list.Label($"Open-ocean seasonal swing share: {climateConfig.oceanSwingOpenShare.CurrentValue:F2}");
                    climateConfig.oceanSwingOpenShare.CurrentValue = list.Slider(climateConfig.oceanSwingOpenShare.CurrentValue, 0.1f, 1f);

                    list.Label($"Continental-air ocean swing gain: {climateConfig.oceanSwingContinentalGain.CurrentValue:F2}");
                    climateConfig.oceanSwingContinentalGain.CurrentValue = list.Slider(climateConfig.oceanSwingContinentalGain.CurrentValue, 0f, 1.5f);

                    list.Label($"Initial nearest-coast e-fold: {FormatKm(climateConfig.oceanEfoldKm.CurrentValue)}");
                    climateConfig.oceanEfoldKm.CurrentValue = list.Slider(climateConfig.oceanEfoldKm.CurrentValue, 100f, 600f);

                    list.Label($"Fallback coastal blend distance: {FormatKm(climateConfig.coastBlendKm.CurrentValue)}");
                    climateConfig.coastBlendKm.CurrentValue = list.Slider(climateConfig.coastBlendKm.CurrentValue, 20f, 150f);

                    list.Label($"Western boundary current warming: {climateConfig.warmWBCurrent.CurrentValue:F1}°C");
                    climateConfig.warmWBCurrent.CurrentValue = list.Slider(climateConfig.warmWBCurrent.CurrentValue, 0f, 5f);

                    list.Label($"Eastern boundary current cooling: {climateConfig.coolEBCurrent.CurrentValue:F1}°C");
                    climateConfig.coolEBCurrent.CurrentValue = list.Slider(climateConfig.coolEBCurrent.CurrentValue, -5f, 0f);

                    list.Label($"Mid-latitude storm-track ocean warming: {climateConfig.stormWarmCurrent.CurrentValue:F1}°C");
                    climateConfig.stormWarmCurrent.CurrentValue = list.Slider(climateConfig.stormWarmCurrent.CurrentValue, 0f, 2f);
                    list.Gap(12f);
                }
            }

            list.End();
            temperatureHeight = list.CurHeight + 24f;
            Widgets.EndScrollView();
        }

        private void DoRainfallTab(Rect rect)
        {
            float viewWidth = rect.width - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, rainfallHeight);

            Widgets.BeginScrollView(rect, ref rainfallScrollPos, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.ColumnWidth = viewWidth;
            list.Begin(viewRect);

            // Base Rainfall Override
            DrawSectionHeader(list, "Planets.ClimateSection.BaseRainfallOverride".Translate());
            list.CheckboxLabeled("Use manual base rainfall",
                ref climateConfig.manualRainfallEnabled.CurrentValue,
                "When enabled, the rainfall slider on the world creation page is disabled. Instead, set base rainfall (mm/year) here.");
            list.Gap(6f);
            if (climateConfig.manualRainfallEnabled.CurrentValue)
            {
                list.Label($"Base rainfall: {climateConfig.manualBaseRain.CurrentValue:F0} mm/year");
                climateConfig.manualBaseRain.CurrentValue = list.Slider(climateConfig.manualBaseRain.CurrentValue, 200f, 2500f);
                DrawDescription(list, "Global moisture budget. Earth-like Normal: 1000 mm/year. Range: 200 (arid) to 2500 (wet). Map mode: Rainfall.");
                list.Gap(12f);
            }

            // Outcome Multipliers (Mountains & Deserts)
            DrawSectionHeader(list, "Planets.OrographicSettings".Translate());
            list.Label($"Rain shadow & mountain uplift strength: {climateConfig.grpRainShadow.CurrentValue:F2}x");
            climateConfig.grpRainShadow.CurrentValue = list.Slider(climateConfig.grpRainShadow.CurrentValue, 0f, 2f);
            DrawDescription(list, "Higher: wetter windward mountain slopes and drier leeward rain shadows. Map modes: Rainfall, Orography.");
            list.Gap(12f);

            list.Label($"Desert strength (subtropical sinking air): {climateConfig.grpSubsidence.CurrentValue:F2}x");
            climateConfig.grpSubsidence.CurrentValue = list.Slider(climateConfig.grpSubsidence.CurrentValue, 0f, 2f);
            DrawDescription(list, "Higher: sinking air in subtropical high-pressure belts suppresses rainfall more strongly, widening dry belts. Map modes: Rainfall, Convergence.");
            list.Gap(12f);

            list.End();
            rainfallHeight = list.CurHeight + 24f;
            Widgets.EndScrollView();
        }

        private void DoMoistureTab(Rect rect)
        {
            float viewWidth = rect.width - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, moistureHeight);

            Widgets.BeginScrollView(rect, ref moistureScrollPos, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.ColumnWidth = viewWidth;
            list.Begin(viewRect);

            // Outcome Multipliers (Moisture Reach & Monsoon)
            DrawSectionHeader(list, "Planets.ClimateSection.MoistureTransport".Translate());
            list.Label($"Moisture reach inland: {climateConfig.grpMoistureReach.CurrentValue:F2}x");
            climateConfig.grpMoistureReach.CurrentValue = list.Slider(climateConfig.grpMoistureReach.CurrentValue, 0f, 2f);
            DrawDescription(list, "Higher: winds and westerly storm tracks carry ocean moisture farther into continental interiors before it rains out. Map modes: Rainfall, Precipitable Water.");
            list.Gap(12f);

            DrawSectionHeader(list, "Planets.ClimateSection.Monsoon".Translate());
            list.Label($"Monsoon strength: {climateConfig.grpMonsoon.CurrentValue:F2}x");
            climateConfig.grpMonsoon.CurrentValue = list.Slider(climateConfig.grpMonsoon.CurrentValue, 0f, 2f);
            DrawDescription(list, "Higher: summer continental heat lows and seasonal pressure shifts draw stronger monsoon winds and rain onto land. Map modes: Monsoon, Wind, Convergence.");
            list.Gap(12f);

            if (Prefs.DevMode)
            {
                list.GapLine();
                list.CheckboxLabeled("Model constants (developer)", ref showDevConstantsMoisture,
                    "Raw diagnostic circulation and two-layer physical moisture budget constants. Scaled by the outcome multipliers above.");
                if (showDevConstantsMoisture)
                {
                    list.Gap(6f);
                    if (list.ButtonText("Reset developer constants"))
                        climateConfig.ResetModelConstants();
                    list.Gap(6f);

                    DrawSectionHeader(list, "Circulation Model Constants (WS28)");
                    list.Label($"Warm-land pressure response: {climateConfig.circHeatWarmHpaPerC.CurrentValue:F2} hPa/°C");
                    climateConfig.circHeatWarmHpaPerC.CurrentValue = list.Slider(climateConfig.circHeatWarmHpaPerC.CurrentValue, 0.5f, 5f);

                    list.Label($"Cold-air / warm-ocean pressure response: {climateConfig.circHeatColdHpaPerC.CurrentValue:F2} hPa/°C");
                    climateConfig.circHeatColdHpaPerC.CurrentValue = list.Slider(climateConfig.circHeatColdHpaPerC.CurrentValue, 0.5f, 5f);

                    list.Label($"Cold-high gate ceiling: {climateConfig.circColdGateC.CurrentValue:F1}°C");
                    climateConfig.circColdGateC.CurrentValue = list.Slider(climateConfig.circColdGateC.CurrentValue, -15f, 25f);

                    list.Label($"Cold-high gate ramp span: {climateConfig.circColdGateSpanC.CurrentValue:F1}°C");
                    climateConfig.circColdGateSpanC.CurrentValue = list.Slider(climateConfig.circColdGateSpanC.CurrentValue, 2f, 40f);

                    list.Label($"Local anomaly smoothing scale: {FormatKm(climateConfig.circSmoothingKm.CurrentValue)}");
                    climateConfig.circSmoothingKm.CurrentValue = list.Slider(climateConfig.circSmoothingKm.CurrentValue, 100f, 1200f);

                    list.Label($"Broad anomaly smoothing scale: {FormatKm(climateConfig.circBroadSmoothingKm.CurrentValue)}");
                    climateConfig.circBroadSmoothingKm.CurrentValue = list.Slider(climateConfig.circBroadSmoothingKm.CurrentValue, 800f, 4000f);

                    list.Label($"Local residual response (inland): {climateConfig.circHeatLocalHpaPerC.CurrentValue:F2} hPa/°C");
                    climateConfig.circHeatLocalHpaPerC.CurrentValue = list.Slider(climateConfig.circHeatLocalHpaPerC.CurrentValue, 0f, 3f);

                    list.Label($"Local residual response (coast): {climateConfig.circHeatLocalCoastHpaPerC.CurrentValue:F2} hPa/°C");
                    climateConfig.circHeatLocalCoastHpaPerC.CurrentValue = list.Slider(climateConfig.circHeatLocalCoastHpaPerC.CurrentValue, 0f, 3f);

                    list.Label($"Coastal residual blend distance: {FormatKm(climateConfig.circLocalCoastKm.CurrentValue)}");
                    climateConfig.circLocalCoastKm.CurrentValue = list.Slider(climateConfig.circLocalCoastKm.CurrentValue, 100f, 2000f);

                    list.Label($"Latitude pressure profile scale: {climateConfig.circPressureProfileScale.CurrentValue:F2}");
                    climateConfig.circPressureProfileScale.CurrentValue = list.Slider(climateConfig.circPressureProfileScale.CurrentValue, 0.2f, 2f);

                    list.Label($"Seasonal pressure delta scale: {climateConfig.circSeasonalProfileScale.CurrentValue:F2}");
                    climateConfig.circSeasonalProfileScale.CurrentValue = list.Slider(climateConfig.circSeasonalProfileScale.CurrentValue, 0f, 2f);

                    list.Label($"Ocean friction (10⁻⁵ s⁻¹): {climateConfig.circFrictionOcean.CurrentValue * 1e5f:F2}");
                    climateConfig.circFrictionOcean.CurrentValue = list.Slider(climateConfig.circFrictionOcean.CurrentValue * 1e5f, 0.2f, 5f) * 1e-5f;

                    list.Label($"Land friction (10⁻⁵ s⁻¹): {climateConfig.circFrictionLand.CurrentValue * 1e5f:F2}");
                    climateConfig.circFrictionLand.CurrentValue = list.Slider(climateConfig.circFrictionLand.CurrentValue * 1e5f, 1f, 15f) * 1e-5f;

                    list.Label($"Surface-to-layer wind ratio (ocean): {climateConfig.circSurfaceOcean.CurrentValue:F2}");
                    climateConfig.circSurfaceOcean.CurrentValue = list.Slider(climateConfig.circSurfaceOcean.CurrentValue, 0.2f, 1f);

                    list.Label($"Surface-to-layer wind ratio (land): {climateConfig.circSurfaceLand.CurrentValue:F2}");
                    climateConfig.circSurfaceLand.CurrentValue = list.Slider(climateConfig.circSurfaceLand.CurrentValue, 0.2f, 1f);
                    list.Gap(12f);

                    DrawSectionHeader(list, "Two-Layer Moisture Budget Constants (WS30/WS31)");
                    list.CheckboxLabeled("Enable upper-troposphere moisture layer (WS31)", ref climateConfig.twoLayerMoisture.CurrentValue);

                    list.Label($"Baseline trade-wind eddy diffusivity K (10⁵ m²/s): {climateConfig.moistEddyK.CurrentValue * 1e-5f:F2}");
                    climateConfig.moistEddyK.CurrentValue = list.Slider(climateConfig.moistEddyK.CurrentValue * 1e-5f, 0.5f, 10f) * 1e5f;

                    list.Label($"Rising-air local rain efficiency ε: {climateConfig.rainEfficiency.CurrentValue:F2}");
                    climateConfig.rainEfficiency.CurrentValue = list.Slider(climateConfig.rainEfficiency.CurrentValue, 0.2f, 1f);

                    list.Label($"Base condensation humidity threshold r_c0: {climateConfig.rainThreshold.CurrentValue:F2}");
                    climateConfig.rainThreshold.CurrentValue = list.Slider(climateConfig.rainThreshold.CurrentValue, 0.5f, 0.95f);

                    list.Label($"Subsidence threshold shift β: {climateConfig.rainThresholdSink.CurrentValue:F2}");
                    climateConfig.rainThresholdSink.CurrentValue = list.Slider(climateConfig.rainThresholdSink.CurrentValue, 0f, 0.4f);

                    list.Label($"Humid-air relaxation timescale τ: {climateConfig.rainTimescaleDays.CurrentValue:F2} days");
                    climateConfig.rainTimescaleDays.CurrentValue = list.Slider(climateConfig.rainTimescaleDays.CurrentValue, 0.5f, 6f);

                    list.Label($"Slope threshold shift γ: {climateConfig.slopeThreshold.CurrentValue:F2}");
                    climateConfig.slopeThreshold.CurrentValue = list.Slider(climateConfig.slopeThreshold.CurrentValue, 0.05f, 0.6f);

                    list.Label($"Slope reference velocity w_ref: {climateConfig.slopeRefVelocity.CurrentValue:F3} m/s");
                    climateConfig.slopeRefVelocity.CurrentValue = list.Slider(climateConfig.slopeRefVelocity.CurrentValue, 0.01f, 0.15f);

                    list.Label($"Ocean bulk evaporation coefficient C_E (10⁻³): {climateConfig.oceanEvapCoeff.CurrentValue * 1e3f:F2}");
                    climateConfig.oceanEvapCoeff.CurrentValue = list.Slider(climateConfig.oceanEvapCoeff.CurrentValue * 1e3f, 0.5f, 4f) * 1e-3f;

                    list.Label($"Ocean evaporation gust floor: {climateConfig.oceanEvapGustMs.CurrentValue:F1} m/s");
                    climateConfig.oceanEvapGustMs.CurrentValue = list.Slider(climateConfig.oceanEvapGustMs.CurrentValue, 0.5f, 6f);

                    list.Label($"Fu-Budyko land recycling parameter w: {climateConfig.budykoW.CurrentValue:F2}");
                    climateConfig.budykoW.CurrentValue = list.Slider(climateConfig.budykoW.CurrentValue, 1.5f, 4.5f);

                    list.Label($"Retained carrier divergence share: {climateConfig.moistDivKeep.CurrentValue:F2}");
                    climateConfig.moistDivKeep.CurrentValue = list.Slider(climateConfig.moistDivKeep.CurrentValue, 0.1f, 1f);

                    list.Label($"Rising-air divergence D95 cap (10⁻⁶ s⁻¹): {climateConfig.moistTriggerD95.CurrentValue * 1e6f:F2}");
                    climateConfig.moistTriggerD95.CurrentValue = list.Slider(climateConfig.moistTriggerD95.CurrentValue * 1e6f, 0.5f, 8f) * 1e-6f;

                    list.CheckboxLabeled("PET continental stress factor", ref climateConfig.petContinentalStress.CurrentValue);
                    list.CheckboxLabeled("PET polar factor (>60°)", ref climateConfig.petPolarFactor.CurrentValue);
                    list.Gap(12f);
                }
            }

            list.End();
            moistureHeight = list.CurHeight + 24f;
            Widgets.EndScrollView();
        }

        private void DoHydrologyTab(Rect rect)
        {
            float viewWidth = rect.width - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, hydrologyHeight);

            Widgets.BeginScrollView(rect, ref hydrologyScrollPos, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.ColumnWidth = viewWidth;
            list.Begin(viewRect);

            GUI.color = new Color(0.7f, 0.9f, 1f);
            list.Label("These settings apply when Tier 2 Advanced Hydrology is enabled (Features menu).");
            list.Label("Planets.ClimateSection.HydrologyTier2Note".Translate());
            GUI.color = Color.white;
            list.Gap(12f);

            DrawSectionHeader(list, "Planets.ClimateSection.HydrologyScale".Translate());
            list.Label($"Hydrology scale: {hydroConfig.referenceRainfallMm.CurrentValue:F0} mm/year");
            hydroConfig.referenceRainfallMm.CurrentValue = list.Slider(hydroConfig.referenceRainfallMm.CurrentValue, 400f, 2000f);
            DrawDescription(list, "Reference rainfall for flow-to-drainage conversion. Affects river formation and wetland intensity. Higher = fewer rivers and wetlands, lower = more. 1000 = Earth baseline.");
            list.Gap(12f);

            DrawSectionHeader(list, "Planets.ClimateSection.EvaporationModel".Translate());
            list.Label($"Evaporation multiplier: {hydroConfig.evaporationMultiplier.CurrentValue:F2}x");
            hydroConfig.evaporationMultiplier.CurrentValue = list.Slider(hydroConfig.evaporationMultiplier.CurrentValue, 0.5f, 2.0f);
            DrawDescription(list, "Scales base evaporation rate globally.");
            list.Gap(12f);

            list.Label($"Max evaporation: {hydroConfig.maxEvaporationMmDay.CurrentValue:F1} mm/day");
            hydroConfig.maxEvaporationMmDay.CurrentValue = list.Slider(hydroConfig.maxEvaporationMmDay.CurrentValue, 5f, 20f);
            DrawDescription(list, "Maximum daily evaporation rate (caps arid extremes).");
            list.Gap(12f);

            list.Label($"Min evaporation: {hydroConfig.minEvaporationMmDay.CurrentValue:F1} mm/day");
            hydroConfig.minEvaporationMmDay.CurrentValue = list.Slider(hydroConfig.minEvaporationMmDay.CurrentValue, 0.5f, 3f);
            DrawDescription(list, "Minimum daily evaporation rate (floor for humid climates).");
            list.Gap(12f);

            list.End();
            hydrologyHeight = list.CurHeight + 24f;
            Widgets.EndScrollView();
        }

        private void DrawSectionHeader(Listing_Standard listing, string text)
        {
            GUI.color = new Color(0.8f, 0.8f, 1f);
            Text.Font = GameFont.Medium;
            listing.Label($"=== {text} ===");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            listing.Gap(6f);
        }

        private void DrawDescription(Listing_Standard listing, string text)
        {
            GUI.color = Color.gray;
            listing.Label(text);
            GUI.color = Color.white;
        }

        public override void PostClose()
        {
            base.PostClose();
            Controller.Settings?.Write();
        }
    }
}
