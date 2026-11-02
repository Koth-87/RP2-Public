using System;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;

namespace Planets.UI.Dialogs
{
    public partial class Dialog_BiomeEditor
    {
        private void DrawBiomeDefTab(Rect rect)
        {
            GUI.BeginGroup(rect);
            Rect inner = rect.AtZero();

            float checkboxSectionHeight = CalculateBiomeDefCheckboxHeight();
            Rect checkboxSection = new Rect(0f, 0f, inner.width, checkboxSectionHeight);
            Widgets.DrawBox(checkboxSection);
            float colWidth = (inner.width - 24f) / 3f;
            const float colGap = 8f;
            const float rowStep = CHECKBOX_HEIGHT + GAP;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(8f, 4f, checkboxSection.width - 16f, HEADER_HEIGHT), "Biome Stats Variables");
            Text.Font = GameFont.Small;

            float c1 = 8f;
            float c2 = 8f + colWidth + colGap;
            float c3 = 8f + (colWidth + colGap) * 2f;
            float rowY = HEADER_HEIGHT + GAP;

            // Row 1
            DrawDefFloatToggle(new Rect(c1, rowY, colWidth, CHECKBOX_HEIGHT), "Animal Density", ref meta.AnimalDensity, BiomeDefOriginals.AnimalDensity(biome), ref bufAnimalDensity);
            DrawDefFloatToggle(new Rect(c2, rowY, colWidth, CHECKBOX_HEIGHT), "Plant Density", ref meta.PlantDensity, BiomeDefOriginals.PlantDensity(biome), ref bufPlantDensity);
            DrawDefFloatToggle(new Rect(c3, rowY, colWidth, CHECKBOX_HEIGHT), "Disease interval (days)", ref meta.DiseaseMtbDays, BiomeDefOriginals.DiseaseMtbDays(biome), ref bufDiseaseMtb);

            // Row 2
            rowY += rowStep;
            DrawDefFloatToggle(new Rect(c1, rowY, colWidth, CHECKBOX_HEIGHT), "Settlement Weight", ref meta.SettlementSelectionWeight, BiomeDefOriginals.SettlementSelectionWeight(biome), ref bufSettlementWeight);
            DrawDefFloatToggle(new Rect(c2, rowY, colWidth, CHECKBOX_HEIGHT), "Camp Weight", ref meta.CampSelectionWeight, BiomeDefOriginals.CampSelectionWeight(biome), ref bufCampWeight);
            DrawDefFloatToggle(new Rect(c3, rowY, colWidth, CHECKBOX_HEIGHT), "Pollution Offset", ref meta.PollutionOffset, BiomeDefOriginals.PollutionOffset(biome), ref bufPollutionOffset);

            // Row 3
            rowY += rowStep;
            DrawDefFloatToggle(new Rect(c1, rowY, colWidth, CHECKBOX_HEIGHT), "Forageability", ref meta.Forageability, BiomeDefOriginals.Forageability(biome), ref bufForageability);
            DrawDefFloatToggle(new Rect(c2, rowY, colWidth, CHECKBOX_HEIGHT), "Wild Plant Regrow Days", ref meta.WildPlantRegrowDays, BiomeDefOriginals.WildPlantRegrowDays(biome), ref bufWildPlantRegrowDays);
            DrawDefFloatToggle(new Rect(c3, rowY, colWidth, CHECKBOX_HEIGHT), "Movement Difficulty", ref meta.MovementDifficulty, BiomeDefOriginals.MovementDifficulty(biome), ref bufMovementDifficulty);

            // Row 4
            rowY += rowStep;
            DrawDefFloatToggle(new Rect(c1, rowY, colWidth, CHECKBOX_HEIGHT), "Geyser Count Factor", ref meta.GeyserCountFactor, BiomeDefOriginals.GeyserCountFactor(biome), ref bufGeyserCountFactor);
            DrawDefFloatToggle(new Rect(c2, rowY, colWidth, CHECKBOX_HEIGHT), "Wild Animal Scaria Chance", ref meta.WildAnimalScariaChance, BiomeDefOriginals.WildAnimalScariaChance(biome), ref bufWildAnimalScariaChance);
            DrawDefFloatToggle(new Rect(c3, rowY, colWidth, CHECKBOX_HEIGHT), "Max Fish Population", ref meta.MaxFishPopulation, BiomeDefOriginals.MaxFishPopulation(biome), ref bufMaxFishPopulation);

            // Row 5 (Booleans)
            rowY += rowStep;
            DrawDefBoolToggle(new Rect(c1, rowY, colWidth, CHECKBOX_HEIGHT), "Allow Roads", ref meta.AllowRoads, BiomeDefOriginals.AllowRoads(biome));
            DrawDefBoolToggle(new Rect(c2, rowY, colWidth, CHECKBOX_HEIGHT), "Allow Rivers", ref meta.AllowRivers, BiomeDefOriginals.AllowRivers(biome));
            DrawDefBoolToggle(new Rect(c3, rowY, colWidth, CHECKBOX_HEIGHT), "Allow Farming Camps", ref meta.AllowFarmingCamps, BiomeDefOriginals.AllowFarmingCamps(biome));

            // Row 6
            rowY += rowStep;
            DrawDefBoolToggle(new Rect(c1, rowY, colWidth, CHECKBOX_HEIGHT), "Extreme Biome", ref meta.IsExtremeBiome, BiomeDefOriginals.IsExtremeBiome(biome));

            float controlY = checkboxSectionHeight + GAP;
            Rect controlSection = new Rect(0f, controlY, inner.width, inner.height - controlY);
            Widgets.DrawBox(controlSection);

            float controlContentHeight = CalculateBiomeDefControlHeight();
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

            bool wasDefOverride = meta.UseDefOverrides;
            Widgets.CheckboxLabeled(new Rect(0f, controlYPos, viewRect.width, CHECKBOX_HEIGHT),
                "Override biome stats (density, disease, movement, flags)", ref meta.UseDefOverrides);
            if (wasDefOverride != meta.UseDefOverrides)
            {
                ClearAllBuffers();
                BiomeWorkerManager.ApplyDefOverrides(config);
            }
            controlYPos += CHECKBOX_HEIGHT + GAP * 2;

            bool prevEnabled = GUI.enabled;
            GUI.enabled = meta.UseDefOverrides;

            if (meta.AnimalDensity.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.AnimalDensity, BiomeDefOriginals.AnimalDensity(biome), ref bufAnimalDensity, "Animal Density", 0f, 100f);
            if (meta.PlantDensity.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.PlantDensity, BiomeDefOriginals.PlantDensity(biome), ref bufPlantDensity, "Plant Density", 0f, 100f);
            if (meta.DiseaseMtbDays.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.DiseaseMtbDays, BiomeDefOriginals.DiseaseMtbDays(biome), ref bufDiseaseMtb, "Disease interval (days)", 1f, 10000f);
            if (meta.SettlementSelectionWeight.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.SettlementSelectionWeight, BiomeDefOriginals.SettlementSelectionWeight(biome), ref bufSettlementWeight, "Settlement Selection Weight", 0f, 100f);
            if (meta.CampSelectionWeight.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.CampSelectionWeight, BiomeDefOriginals.CampSelectionWeight(biome), ref bufCampWeight, "Camp Selection Weight", 0f, 100f);
            if (meta.PollutionOffset.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.PollutionOffset, BiomeDefOriginals.PollutionOffset(biome), ref bufPollutionOffset, "Pollution Offset", -10f, 10f);
            if (meta.Forageability.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.Forageability, BiomeDefOriginals.Forageability(biome), ref bufForageability, "Forageability (0-1)", 0f, 1f);
            if (meta.WildPlantRegrowDays.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.WildPlantRegrowDays, BiomeDefOriginals.WildPlantRegrowDays(biome), ref bufWildPlantRegrowDays, "Wild Plant Regrow Days", 1f, 1000f);
            if (meta.MovementDifficulty.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.MovementDifficulty, BiomeDefOriginals.MovementDifficulty(biome), ref bufMovementDifficulty, "Movement Difficulty", 0.1f, 50f);
            if (meta.GeyserCountFactor.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.GeyserCountFactor, BiomeDefOriginals.GeyserCountFactor(biome), ref bufGeyserCountFactor, "Geyser Count Factor", 0f, 20f);
            if (meta.WildAnimalScariaChance.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.WildAnimalScariaChance, BiomeDefOriginals.WildAnimalScariaChance(biome), ref bufWildAnimalScariaChance, "Wild Animal Scaria Chance (0-1)", 0f, 1f);
            if (meta.MaxFishPopulation.HasValue)
                DrawDefStatField(ref controlYPos, viewRect, ref meta.MaxFishPopulation, BiomeDefOriginals.MaxFishPopulation(biome), ref bufMaxFishPopulation, "Max Fish Population", 0f, 100000f);

            if (meta.AllowRoads.HasValue)
                DrawDefBoolField(ref controlYPos, viewRect, ref meta.AllowRoads, BiomeDefOriginals.AllowRoads(biome), "Allow Roads");
            if (meta.AllowRivers.HasValue)
                DrawDefBoolField(ref controlYPos, viewRect, ref meta.AllowRivers, BiomeDefOriginals.AllowRivers(biome), "Allow Rivers");
            if (meta.AllowFarmingCamps.HasValue)
                DrawDefBoolField(ref controlYPos, viewRect, ref meta.AllowFarmingCamps, BiomeDefOriginals.AllowFarmingCamps(biome), "Allow Farming Camps");
            if (meta.IsExtremeBiome.HasValue)
                DrawDefBoolField(ref controlYPos, viewRect, ref meta.IsExtremeBiome, BiomeDefOriginals.IsExtremeBiome(biome), "Is Extreme Biome");

            GUI.enabled = prevEnabled;

            Widgets.EndScrollView();
            GUI.EndGroup();
        }

        private static string DefStatLabel(string name, float xmlValue)
        {
            return name + " (default " + xmlValue.ToString("0.###") + "):";
        }

        private static void DrawDefStatField(ref float y, Rect viewRect, ref float? stored, float xmlDefault, ref string buffer, string label, float min, float max)
        {
            float value = stored ?? xmlDefault;
            Widgets.TextFieldNumericLabeled(new Rect(0f, y, viewRect.width, CONTROL_HEIGHT),
                DefStatLabel(label, xmlDefault), ref value, ref buffer, min, max);
            stored = value;
            y += CONTROL_HEIGHT + GAP;
        }

        private static void DrawDefBoolField(ref float y, Rect viewRect, ref bool? stored, bool xmlDefault, string label)
        {
            bool value = stored ?? xmlDefault;
            Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width, CHECKBOX_HEIGHT),
                $"{label} (default {(xmlDefault ? "on" : "off")})", ref value);
            stored = value;
            y += CHECKBOX_HEIGHT + GAP;
        }

        private static void DrawDefFloatToggle(Rect rect, string label, ref float? stored, float xmlDefault, ref string buffer)
        {
            bool isOn = stored.HasValue;
            bool wasOn = isOn;
            Widgets.CheckboxLabeled(rect, label, ref isOn);
            if (wasOn != isOn)
            {
                buffer = null;
                stored = isOn ? xmlDefault : (float?)null;
            }
        }

        private static void DrawDefBoolToggle(Rect rect, string label, ref bool? stored, bool xmlDefault)
        {
            bool isOn = stored.HasValue;
            bool wasOn = isOn;
            Widgets.CheckboxLabeled(rect, label, ref isOn);
            if (wasOn != isOn)
                stored = isOn ? xmlDefault : (bool?)null;
        }

        private static float CalculateBiomeDefCheckboxHeight()
        {
            return HEADER_HEIGHT + GAP + (CHECKBOX_HEIGHT + GAP) * 6f + 8f;
        }

        private float CalculateBiomeDefControlHeight()
        {
            float height = HEADER_HEIGHT + GAP * 2 + CHECKBOX_HEIGHT + GAP * 2;
            if (meta.AnimalDensity.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.PlantDensity.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.DiseaseMtbDays.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.SettlementSelectionWeight.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.CampSelectionWeight.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.PollutionOffset.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.Forageability.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.WildPlantRegrowDays.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.MovementDifficulty.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.GeyserCountFactor.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.WildAnimalScariaChance.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.MaxFishPopulation.HasValue) height += CONTROL_HEIGHT + GAP;
            if (meta.AllowRoads.HasValue) height += CHECKBOX_HEIGHT + GAP;
            if (meta.AllowRivers.HasValue) height += CHECKBOX_HEIGHT + GAP;
            if (meta.AllowFarmingCamps.HasValue) height += CHECKBOX_HEIGHT + GAP;
            if (meta.IsExtremeBiome.HasValue) height += CHECKBOX_HEIGHT + GAP;
            return Mathf.Max(height + 24f, 200f);
        }
    }
}
