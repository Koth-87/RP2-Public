using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.Core.Configuration;
using Planets.WorldGen.Climate;

namespace Planets.UI.Dialogs
{
    public partial class Dialog_BiomeEditor
    {
        private void DrawBiomeWorkerTab(Rect rect)
        {
            GUI.BeginGroup(rect);
            Rect inner = rect.AtZero();

            float checkboxSectionHeight = CalculateBiomeWorkerCheckboxHeight();
            Rect checkboxSection = new Rect(0f, 0f, inner.width, checkboxSectionHeight);
            Widgets.DrawBox(checkboxSection);
            float colWidth = (inner.width - 24f) / 3f;
            const float colGap = 8f;
            const float rowHeight = CHECKBOX_HEIGHT + GAP;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(8f, 4f, checkboxSection.width - 16f, HEADER_HEIGHT), "Scoring Variables");
            Text.Font = GameFont.Small;
            float rowY = HEADER_HEIGHT + GAP;
            bool primaryMoistureIsRainfall = meta.UserIdealTemp < 0f
                || (biome?.Worker is BiomeWorkerBase rpw && rpw.UsesRainfallMoistureAxis);
            string rainfallLabel = primaryMoistureIsRainfall ? "Rainfall*" : "Rainfall";
            string aridityLabel = primaryMoistureIsRainfall ? "Aridity" : "Aridity*";

            // Column 1: Primary climate and wetland
            float cx = 8f;
            if (meta.UseWorkerOverrides)
            {
                meta.UseVarTemperature = true;
                DrawLockedCheckbox(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Temperature",
                    "Temperature stays on while custom values are used. Turning it off removes temperature from the score.");
            }
            else
            {
                Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Temperature", ref meta.UseVarTemperature);
            }
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), rainfallLabel, ref meta.UseVarRainfall);
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), aridityLabel, ref meta.UseVarAridity);
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Elevation", ref meta.UseVarElevation);
            rowY += rowHeight;
            if (meta.UseVarElevation)
            {
                Widgets.CheckboxLabeled(new Rect(cx + 16f, rowY, colWidth - 16f, CHECKBOX_HEIGHT), "Dual Elevation", ref meta.UseVarDualElevation);
                rowY += rowHeight;
            }
            else
            {
                meta.UseVarDualElevation = false;
            }
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Swampiness", ref meta.UseVarSwampiness);

            // Column 2: Hilliness, continentality, seasonality, windward/leeward, summer max
            rowY = HEADER_HEIGHT + GAP;
            cx = 8f + colWidth + colGap;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Hilliness", ref meta.UseVarHilliness);
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Continentality", ref meta.UseVarContinentality);
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Seasonality (amplitude)", ref meta.UseVarSeasonality);
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Windward", ref meta.UseVarWindward);
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Leeward", ref meta.UseVarLeeward);
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Summer Max Temp", ref meta.UseVarSummerMax);

            // Column 3: Winter min, monsoon, flow, noise
            rowY = HEADER_HEIGHT + GAP;
            cx = 8f + (colWidth + colGap) * 2f;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Winter Min Temp", ref meta.UseVarWinterMin);
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Monsoon Boost", ref meta.UseVarMonsoonBoost);
            rowY += rowHeight;
            Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Flow Accumulation", ref meta.UseVarFlowAccumulation);
            rowY += rowHeight;
            if (UsesNoise(biome))
            {
                bool wasNoise = meta.UseVarNoise;
                Widgets.CheckboxLabeled(new Rect(cx, rowY, colWidth, CHECKBOX_HEIGHT), "Noise", ref meta.UseVarNoise);
                if (!wasNoise && meta.UseVarNoise && biome?.Worker is BiomeWorkerBase noiseWorker)
                {
                    meta.NoiseThreshold = noiseWorker.DefaultNoiseThreshold;
                    meta.NoiseBonus = noiseWorker.DefaultNoiseBonus;
                    meta.NoisePenalty = noiseWorker.DefaultNoisePenalty;
                    meta.NoisePassProbability = noiseWorker.DefaultNoisePassProbability;
                    bufClumpingThr = bufClumpingBonus = bufClumpingPenalty = bufNoisePassProb = null;
                }
            }

            float controlY = checkboxSectionHeight + GAP;
            Rect controlSection = new Rect(0f, controlY, inner.width, inner.height - controlY);
            Widgets.DrawBox(controlSection);

            float controlContentHeight = CalculateBiomeWorkerControlHeight();
            float scrollOutWidth = controlSection.width - 8f;
            float viewWidth = scrollOutWidth - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, controlContentHeight);

            Widgets.BeginScrollView(new Rect(4f, controlSection.y + 4f, scrollOutWidth, controlSection.height - 8f),
                ref scrollPos, viewRect);

            float controlYPos = 0f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, controlYPos, viewRect.width, HEADER_HEIGHT), "Variable Controls");
            Text.Font = GameFont.Small;
            controlYPos += HEADER_HEIGHT + GAP * 2;

            bool isForeignBiome = !BiomeWorkerManager.IsNative(biome);
            if (isForeignBiome)
            {
                BiomeWorker orig = BiomeWorkerManager.GetOriginalWorker(biome) ?? biome.Worker;
                if (ForeignPeaks.TryGet(biome.defName, out ForeignPeaks.Record probeInfo) && probeInfo.ProbePeak > 0f)
                {
                    GUI.color = new Color(0.6f, 0.9f, 1f);
                    string probeLine = $"Probed peak: {probeInfo.ProbePeak:F1} at {probeInfo.PeakTemperature:F0}°C, {probeInfo.PeakRainfall:F0} mm, {probeInfo.PeakElevation:F0} m"
                        + (probeInfo.ObservedPeak > 0f ? $" (world p95: {probeInfo.ObservedPeak:F1})" : "");
                    float probeH = Text.CalcHeight(probeLine, viewRect.width);
                    Widgets.Label(new Rect(0f, controlYPos, viewRect.width, probeH), probeLine);
                    GUI.color = Color.white;
                    controlYPos += probeH + GAP;
                }

                bool wasClimateFit = meta.UseRP2ClimateFit;
                Widgets.CheckboxLabeled(new Rect(0f, controlYPos, viewRect.width, CHECKBOX_HEIGHT),
                    "Use RP2 climate fit (probed ideals & tolerances)", ref meta.UseRP2ClimateFit);
                controlYPos += CHECKBOX_HEIGHT + GAP;

                if (!wasClimateFit && meta.UseRP2ClimateFit)
                {
                    meta.UseWorkerOverrides = true;
                    meta.UseVarTemperature = true;
                    meta.UseVarRainfall = true;
                    meta.UseVarAridity = false;
                    meta.UseVarElevation = true;
                    ForeignPeaks.Record rec = ForeignPeaks.ProbeOrGet(biome, orig);
                    if (rec.ProbePeak > 0f)
                    {
                        meta.UserIdealTemp = rec.PeakTemperature;
                        meta.UserTempTolerance = Mathf.Max(3f, (rec.HalfTempMax - rec.HalfTempMin) * 0.5f);
                        meta.UserIdealRain = rec.PeakRainfall;
                        meta.UserRainTolerance = Mathf.Max(200f, (rec.HalfRainMax - rec.HalfRainMin) * 0.5f);
                        meta.UserIdealElevation = rec.PeakElevation;
                        meta.UserElevationTolerance = Mathf.Max(400f, (rec.HalfElevMax - rec.HalfElevMin) * 0.5f);
                        ClearAllBuffers();
                    }
                }
                if (wasClimateFit != meta.UseRP2ClimateFit)
                    BiomeWorkerManager.ApplyWorkers(config);

                if (meta.UseRP2ClimateFit)
                {
                    Widgets.CheckboxLabeled(new Rect(16f, controlYPos, viewRect.width - 16f, CHECKBOX_HEIGHT),
                        "Ignore the mod's own placement rules", ref meta.IgnoreForeignPlacementRules);
                    controlYPos += CHECKBOX_HEIGHT + GAP;
                }
            }

            // Read-only Hard Gates display (WS18-4)
            string gatesDesc = biome?.Worker is BiomeWorkerBase rpwGates
                ? rpwGates.DescribeHardGates()
                : "Mod's own BiomeWorker.GetScore > 0";
            GUI.color = new Color(0.8f, 0.85f, 0.9f);
            string gatesLine = "Hard gates (read-only): " + gatesDesc;
            float gatesHeight = Text.CalcHeight(gatesLine, viewRect.width);
            Widgets.Label(new Rect(0f, controlYPos, viewRect.width, gatesHeight), gatesLine);
            GUI.color = Color.white;
            controlYPos += gatesHeight + GAP;

            // Live tile energy preview when a world tile is selected (WS18-4)
            string previewLine = BuildSelectedTilePreview();
            if (!string.IsNullOrEmpty(previewLine))
            {
                GUI.color = new Color(0.55f, 1f, 0.65f);
                float prevH = Text.CalcHeight(previewLine, viewRect.width);
                Widgets.Label(new Rect(0f, controlYPos, viewRect.width, prevH), previewLine);
                GUI.color = Color.white;
                controlYPos += prevH + GAP;
            }

            bool wasOverride = meta.UseWorkerOverrides;
            Widgets.CheckboxLabeled(new Rect(0f, controlYPos, viewRect.width, CHECKBOX_HEIGHT),
                "Use custom values (override native/calibrated)", ref meta.UseWorkerOverrides);
            if (!wasOverride && meta.UseWorkerOverrides)
            {
                meta.UseVarTemperature = true;
                meta.UseVarRainfall = true;
                meta.UseVarAridity = true;
                meta.UseVarElevation = true;
            }
            controlYPos += CHECKBOX_HEIGHT + GAP;

            GUI.color = meta.UseWorkerOverrides ? Color.gray : new Color(1f, 0.85f, 0.4f);
            const string customValuesHint = "These numbers are used only while custom values are on. Otherwise this biome keeps its built-in worker values.";
            float hintHeight = Text.CalcHeight(customValuesHint, viewRect.width);
            Widgets.Label(new Rect(0f, controlYPos, viewRect.width, hintHeight), customValuesHint);
            GUI.color = Color.white;
            controlYPos += hintHeight + GAP;

            bool valuesEnabled = GUI.enabled;
            GUI.enabled = meta.UseWorkerOverrides;

            BiomeMetadata defaultMeta = null;
            bool showGreenDefaults = (BiomeConditionRegistry.IsInRegistry(biome.defName) || IsNativeRP2Biome(biome)) && !IsCompatBiome(biome);
            bool showCyanDefaults = IsCompatBiome(biome);
            if (showGreenDefaults || showCyanDefaults)
            {
                defaultMeta = new BiomeMetadata();
                BiomeConfiguration.FillHardcodedDefaults(defaultMeta, biome);
            }

            DrawBiomeWorkerControls(viewRect, controlYPos, defaultMeta, showGreenDefaults, showCyanDefaults);
            GUI.enabled = valuesEnabled;

            Widgets.EndScrollView();
            GUI.EndGroup();
        }

        private float DrawBiomeWorkerControls(Rect viewRect, float y, BiomeMetadata defaultMeta, bool showGreenDefaults, bool showCyanDefaults = false)
        {
            const float maxFloat = 1E+09f;
            bool showDefaultLabels = (showGreenDefaults || showCyanDefaults) && defaultMeta != null;
            Color defaultColor = showCyanDefaults ? new Color(0.4f, 0.8f, 1f) : Color.green;

            void DrawRow(string mainLabel, string bracketText, ref float val, ref string buf, float min, float max)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, CONTROL_HEIGHT);
                float cursorX = 0f;
                GUI.color = Color.white;
                Widgets.Label(new Rect(cursorX, y, Text.CalcSize(mainLabel).x, CONTROL_HEIGHT), mainLabel);
                cursorX += Text.CalcSize(mainLabel).x;
                if (showDefaultLabels && !string.IsNullOrEmpty(bracketText))
                {
                    GUI.color = defaultColor;
                    float bw = Text.CalcSize(bracketText).x;
                    Widgets.Label(new Rect(cursorX, y, bw, CONTROL_HEIGHT), bracketText);
                    GUI.color = Color.white;
                    cursorX += bw;
                }
                float fieldWidth = Mathf.Max(80f, rowRect.width - cursorX - 4f);
                Widgets.TextFieldNumeric(new Rect(cursorX + 4f, y, fieldWidth - 4f, CONTROL_HEIGHT), ref val, ref buf, min, max);
                y += CONTROL_HEIGHT + GAP;
            }

            void DrawRowInt(string mainLabel, string bracketText, ref int val, ref string buf, int min, int max)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, CONTROL_HEIGHT);
                float cursorX = 0f;
                GUI.color = Color.white;
                Widgets.Label(new Rect(cursorX, y, Text.CalcSize(mainLabel).x, CONTROL_HEIGHT), mainLabel);
                cursorX += Text.CalcSize(mainLabel).x;
                if (showDefaultLabels && !string.IsNullOrEmpty(bracketText))
                {
                    GUI.color = defaultColor;
                    float bw = Text.CalcSize(bracketText).x;
                    Widgets.Label(new Rect(cursorX, y, bw, CONTROL_HEIGHT), bracketText);
                    GUI.color = Color.white;
                    cursorX += bw;
                }
                float fieldWidth = Mathf.Max(80f, rowRect.width - cursorX - 4f);
                Widgets.TextFieldNumeric(new Rect(cursorX + 4f, y, fieldWidth - 4f, CONTROL_HEIGHT), ref val, ref buf, min, max);
                y += CONTROL_HEIGHT + GAP;
            }

            if (meta.UseVarTemperature)
            {
                DrawRow("Ideal Temperature (°C):", showDefaultLabels ? " [" + defaultMeta.UserIdealTemp.ToString("F1") + "]" : "", ref meta.UserIdealTemp, ref bufUserIdealTemp, -100f, 100f);
                DrawRow("Temperature Tolerance (°C):", showDefaultLabels ? " [" + defaultMeta.UserTempTolerance.ToString("F1") + "]" : "", ref meta.UserTempTolerance, ref bufUserTempTolerance, 0f, maxFloat);
                y += GAP;
            }
            if (meta.UseVarRainfall)
            {
                DrawRow("Ideal Rainfall (mm/year):", showDefaultLabels ? " [" + defaultMeta.UserIdealRain.ToString("F0") + "]" : "", ref meta.UserIdealRain, ref bufUserIdealRain, 0f, maxFloat);
                DrawRow("Rainfall Tolerance (mm):", showDefaultLabels ? " [" + defaultMeta.UserRainTolerance.ToString("F0") + "]" : "", ref meta.UserRainTolerance, ref bufUserRainTolerance, 0f, maxFloat);
                y += GAP;
            }
            if (meta.UseVarAridity)
            {
                DrawRow("Ideal Aridity Index:", showDefaultLabels ? " [" + defaultMeta.IdealAridity.ToString("F2") + "]" : "", ref meta.IdealAridity, ref bufIdealAridity, 0f, maxFloat);
                DrawRow("Aridity Tolerance:", showDefaultLabels ? " [" + defaultMeta.AridityTolerance.ToString("F2") + "]" : "", ref meta.AridityTolerance, ref bufAridityTolerance, 0f, maxFloat);
                y += GAP;
            }
            if (meta.UseVarElevation)
            {
                DrawRow("Ideal Elevation (m):", showDefaultLabels ? " [" + defaultMeta.UserIdealElevation.ToString("F0") + "]" : "", ref meta.UserIdealElevation, ref bufUserIdealElevation, 0f, maxFloat);
                DrawRow("Elevation Tolerance (m):", showDefaultLabels ? " [" + defaultMeta.UserElevationTolerance.ToString("F0") + "]" : "", ref meta.UserElevationTolerance, ref bufUserElevationTolerance, 0f, maxFloat);
                if (meta.UseVarDualElevation)
                {
                    DrawRow("Ideal Elevation 2 (m):", showDefaultLabels ? " [" + defaultMeta.UserIdealElevation2.ToString("F0") + "]" : "", ref meta.UserIdealElevation2, ref bufUserIdealElevation2, 0f, maxFloat);
                    DrawRow("Elevation Tolerance 2 (m):", showDefaultLabels ? " [" + defaultMeta.UserElevationTolerance2.ToString("F0") + "]" : "", ref meta.UserElevationTolerance2, ref bufUserElevationTolerance2, 0f, maxFloat);
                    DrawRow("Elevation Weight 1:", showDefaultLabels ? " [" + defaultMeta.UserElevationWeight1.ToString("F2") + "]" : "", ref meta.UserElevationWeight1, ref bufUserElevationWeight1, 0f, 2f);
                    DrawRow("Elevation Weight 2:", showDefaultLabels ? " [" + defaultMeta.UserElevationWeight2.ToString("F2") + "]" : "", ref meta.UserElevationWeight2, ref bufUserElevationWeight2, 0f, 2f);
                }
                y += GAP;
            }
            if (meta.UseVarSwampiness)
            {
                DrawRow("Swampiness Ideal (0-1):", showDefaultLabels ? " [" + defaultMeta.SwampinessIdeal.ToString("F2") + "]" : "", ref meta.SwampinessIdeal, ref bufSwampinessIdeal, 0f, 1f);
                DrawRow("Swampiness Tolerance:", showDefaultLabels ? " [" + defaultMeta.SwampinessTolerance.ToString("F2") + "]" : "", ref meta.SwampinessTolerance, ref bufSwampinessTol, 0f, 1f);
                y += GAP;
            }
            if (meta.UseVarHilliness)
            {
                DrawRowInt("Preferred Hilliness (0=Flat,1=SmallHills,2=LargeHills,3=Mountainous,4=Impassable):", showDefaultLabels ? " [" + defaultMeta.PreferredHilliness + "]" : "", ref meta.PreferredHilliness, ref bufPreferredHilliness, 0, 4);
                y += GAP;
            }
            if (meta.UseVarContinentality)
            {
                DrawRow("Continentality Ideal (0-1):", showDefaultLabels ? " [" + defaultMeta.ContinentalityIdeal.ToString("F2") + "]" : "", ref meta.ContinentalityIdeal, ref bufContinentalityIdeal, 0f, 1f);
                DrawRow("Continentality Tolerance:", showDefaultLabels ? " [" + defaultMeta.ContinentalityTolerance.ToString("F2") + "]" : "", ref meta.ContinentalityTolerance, ref bufContinentalityTol, 0f, 1f);
                y += GAP;
            }
            if (meta.UseVarSeasonality)
            {
                DrawRow("Seasonality Ideal (°C amplitude):", showDefaultLabels ? " [" + defaultMeta.SeasonalityIdeal.ToString("F1") + "]" : "", ref meta.SeasonalityIdeal, ref bufSeasonalityIdeal, 0f, 100f);
                DrawRow("Seasonality Tolerance:", showDefaultLabels ? " [" + defaultMeta.SeasonalityTolerance.ToString("F1") + "]" : "", ref meta.SeasonalityTolerance, ref bufSeasonalityTol, 0f, 50f);
                y += GAP;
            }
            if (meta.UseVarWindward)
            {
                DrawRow("Windward Threshold:", showDefaultLabels ? " [" + defaultMeta.WindwardThreshold.ToString("F2") + "]" : "", ref meta.WindwardThreshold, ref bufWindwardThr, 0f, 2f);
                DrawRow("Windward Scale:", showDefaultLabels ? " [" + defaultMeta.WindwardScale.ToString("F2") + "]" : "", ref meta.WindwardScale, ref bufWindwardScale, 0f, 1f);
                y += GAP;
            }
            if (meta.UseVarLeeward)
            {
                DrawRow("Leeward Threshold:", showDefaultLabels ? " [" + defaultMeta.LeewardThreshold.ToString("F2") + "]" : "", ref meta.LeewardThreshold, ref bufLeewardThr, 0f, 2f);
                DrawRow("Leeward Scale:", showDefaultLabels ? " [" + defaultMeta.LeewardScale.ToString("F2") + "]" : "", ref meta.LeewardScale, ref bufLeewardScale, 0f, 1f);
                y += GAP;
            }
            if (meta.UseVarFlowAccumulation)
            {
                DrawRow("Flow Accumulation Threshold:", showDefaultLabels ? " [" + defaultMeta.FlowAccumulationThreshold.ToString("F0") + "]" : "", ref meta.FlowAccumulationThreshold, ref bufFlowThr, 0f, maxFloat);
                DrawRow("Flow Accumulation Scale:", showDefaultLabels ? " [" + defaultMeta.FlowAccumulationScale.ToString("F3") + "]" : "", ref meta.FlowAccumulationScale, ref bufFlowScale, 0f, 0.1f);
                y += GAP;
            }
            if (meta.UseVarSummerMax)
            {
                DrawRow("Summer Max Ideal (°C):", showDefaultLabels ? " [" + defaultMeta.SummerMaxIdeal.ToString("F1") + "]" : "", ref meta.SummerMaxIdeal, ref bufSummerMaxIdeal, -50f, 80f);
                DrawRow("Summer Max Tolerance:", showDefaultLabels ? " [" + defaultMeta.SummerMaxTolerance.ToString("F1") + "]" : "", ref meta.SummerMaxTolerance, ref bufSummerMaxTol, 0f, 50f);
                y += GAP;
            }
            if (meta.UseVarWinterMin)
            {
                DrawRow("Winter Min Ideal (°C):", showDefaultLabels ? " [" + defaultMeta.WinterMinIdeal.ToString("F1") + "]" : "", ref meta.WinterMinIdeal, ref bufWinterMinIdeal, -80f, 50f);
                DrawRow("Winter Min Tolerance:", showDefaultLabels ? " [" + defaultMeta.WinterMinTolerance.ToString("F1") + "]" : "", ref meta.WinterMinTolerance, ref bufWinterMinTol, 0f, 50f);
                y += GAP;
            }
            if (meta.UseVarMonsoonBoost)
            {
                DrawRow("Monsoon Boost Threshold (mm):", showDefaultLabels ? " [" + defaultMeta.MonsoonBoostThreshold.ToString("F0") + "]" : "", ref meta.MonsoonBoostThreshold, ref bufMonsoonThr, 0f, 2000f);
                DrawRow("Monsoon Boost Scale:", showDefaultLabels ? " [" + defaultMeta.MonsoonBoostScale.ToString("F2") + "]" : "", ref meta.MonsoonBoostScale, ref bufMonsoonScale, 0f, 1f);
                y += GAP;
            }
            if (meta.UseVarNoise && UsesNoise(biome))
            {
                if (IsPercentPassNoiseCompat())
                {
                    float defaultPassProb = (biome.Worker as BiomeWorkerBase)?.DefaultNoisePassProbability ?? defaultMeta?.NoisePassProbability ?? 0.01f;
                    DrawRow("Noise pass probability (0-1):", showDefaultLabels ? " [" + defaultPassProb.ToString("F3") + "]" : "", ref meta.NoisePassProbability, ref bufNoisePassProb, 0f, 1f);
                }
                else
                {
                    DrawRow("Noise threshold (0-1):", showDefaultLabels && defaultMeta != null ? " [" + defaultMeta.NoiseThreshold.ToString("F2") + "]" : "", ref meta.NoiseThreshold, ref bufClumpingThr, 0f, 1f);
                    DrawRow("Noise bonus:", showDefaultLabels && defaultMeta != null ? " [" + defaultMeta.NoiseBonus.ToString("F2") + "]" : "", ref meta.NoiseBonus, ref bufClumpingBonus, 0f, 3f);
                    DrawRow("Noise penalty:", showDefaultLabels && defaultMeta != null ? " [" + defaultMeta.NoisePenalty.ToString("F2") + "]" : "", ref meta.NoisePenalty, ref bufClumpingPenalty, 0f, 5f);
                }
                y += GAP;
            }

            return y;
        }

        private float CalculateBiomeWorkerCheckboxHeight()
        {
            const float rowHeight = CHECKBOX_HEIGHT + GAP;
            float rows = 6f; // Column 2 has 6 rows; Column 1 has 5 rows (or 6 when Dual Elevation is visible).
            return HEADER_HEIGHT + GAP + rows * rowHeight + 8f;
        }

        private string BuildSelectedTilePreview()
        {
            if (Current.ProgramState != ProgramState.Playing && Find.World == null)
                return null;
            if (!Climate.IsInitialized() || Find.WorldSelector == null)
                return null;
            PlanetTile sel = Find.WorldSelector.SelectedTile;
            if (!sel.Valid || Find.WorldGrid?.Surface == null || sel.Layer != Find.WorldGrid.Surface)
                return null;
            int tileID = sel.tileId;
            if (tileID < 0 || tileID >= Find.WorldGrid.TilesCount)
                return null;

            Tile tile = Find.WorldGrid[tileID];
            if (tile == null)
                return null;

            List<BiomeSelector.CandidateScore> candidates = BiomeSelector.EvaluateCandidates(tile, sel, Find.WorldGrid.Surface);
            string winText = candidates.Count > 0
                ? $"winner {candidates[0].Biome.LabelCap} with energy {candidates[0].Energy:F2} (score {candidates[0].Score:F1})"
                : "no viable candidate";

            float freq = config != null ? config.GetCommonalityMultiplier(biome.defName) : 1f;
            if (!meta.Enabled || freq <= 0f)
                return $"At the selected tile (#{tileID}): disabled; {winText}";

            float freqShift = -2f * Mathf.Log(freq);
            if (biome?.Worker is BiomeWorkerBase rpw && !rpw.HasCustomScore)
            {
                ClimateSample sample = ClimateSample.FromTile(tile, tileID);
                BiomeFit fit = rpw.EvaluateFit(biome, tile, sel, in sample);
                if (!fit.Viable)
                    return $"At the selected tile (#{tileID}): rejected ({fit.RejectReason}); {winText}";
                float energy = fit.Energy + freqShift;
                float score = 100f * Mathf.Exp(-energy / 2f);
                return $"At the selected tile (#{tileID}): energy {energy:F2} (score {score:F1}); {winText}";
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Biome == biome)
                    return $"At the selected tile (#{tileID}): energy {candidates[i].Energy:F2} (score {candidates[i].Score:F1}); {winText}";
            }
            return $"At the selected tile (#{tileID}): score <= 0 (not viable); {winText}";
        }

        private float CalculateBiomeWorkerControlHeight()
        {
            float height = HEADER_HEIGHT + GAP * 2 + CHECKBOX_HEIGHT + GAP * 2 + 88f;
            if (!BiomeWorkerManager.IsNative(biome))
                height += (CHECKBOX_HEIGHT + GAP) * 3f;
            if (meta.UseVarTemperature) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarRainfall) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarAridity) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarElevation)
            {
                height += CONTROL_HEIGHT * 2 + GAP * 2;
                if (meta.UseVarDualElevation) height += CONTROL_HEIGHT * 4 + GAP * 2;
            }
            if (meta.UseVarSwampiness) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarHilliness) height += CONTROL_HEIGHT + GAP * 2;
            if (meta.UseVarContinentality) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarSeasonality) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarWindward) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarLeeward) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarFlowAccumulation) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarSummerMax) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarWinterMin) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarMonsoonBoost) height += CONTROL_HEIGHT * 2 + GAP * 2;
            if (meta.UseVarNoise && UsesNoise(biome))
                height += IsPercentPassNoiseCompat() ? CONTROL_HEIGHT + GAP : CONTROL_HEIGHT * 3 + GAP * 2;

            height *= 1.15f;
            return Mathf.Max(height, 200f);
        }
    }
}
