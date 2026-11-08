using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using Planets.Presets.Biomes;
using Planets.Presets.WorldGen;
using Planets.Presets.Factions;

namespace Planets.Core.Configuration
{
    public class ProfileManagerConfiguration
    {
        private Vector2 scrollPos = Vector2.zero;
        private float profileTabHeight = 800f;

        public void RenderUI(Rect canvas)
        {
            float viewWidth = canvas.width - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, profileTabHeight);

            Widgets.BeginScrollView(canvas, ref scrollPos, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.ColumnWidth = viewWidth;
            list.Begin(viewRect);

            GUI.color = Color.cyan;
            Text.Font = GameFont.Medium;
            list.Label("=== Profile & Preset Manager ===");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            list.GapLine();
            list.Gap();

            // 1. Biome Profiles
            DrawBiomeProfilesSection(list);
            list.GapLine();
            list.Gap();

            // 2. Per-Biome Presets
            DrawPerBiomePresetsSection(list);
            list.GapLine();
            list.Gap();

            // 3. World Gen Presets
            DrawWorldGenPresetsSection(list);
            list.GapLine();
            list.Gap();

            // 4. Faction Presets
            DrawFactionPresetsSection(list);
            list.GapLine();
            list.Gap();

            // 5. Legacy Biome Presets Conversion
            DrawLegacyPresetsSection(list);

            list.End();
            profileTabHeight = list.CurHeight + 12f;
            Widgets.EndScrollView();
        }

        private void DrawBiomeProfilesSection(Listing_Standard list)
        {
            Text.Font = GameFont.Medium;
            list.Label("Biome Profiles (Global)");
            Text.Font = GameFont.Small;
            list.Gap(4);

            string defaultProfile = Controller.Settings.defaultBiomeProfile.CurrentValue;
            var profiles = BiomeProfileManager.GetAllProfiles(false);

            if (profiles.Count == 0)
            {
                GUI.color = Color.gray;
                list.Label("No biome profiles found.");
                GUI.color = Color.white;
            }
            else
            {
                foreach (var profile in profiles)
                {
                    var p = profile;
                    bool isDefault = (p.name == defaultProfile);

                    Rect rowRect = list.GetRect(24f);
                    Widgets.DrawHighlightIfMouseover(rowRect);

                    Rect labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width - 160f, rowRect.height);
                    string label = p.name;
                    if (isDefault)
                    {
                        GUI.color = Color.green;
                        label += " [Default]";
                    }
                    Widgets.Label(labelRect, label);
                    GUI.color = Color.white;

                    Rect btnDefault = new Rect(rowRect.xMax - 150f, rowRect.y, 70f, 24f);
                    if (Widgets.ButtonText(btnDefault, isDefault ? "Clear" : "Set Def"))
                    {
                        if (isDefault)
                            Controller.Settings.defaultBiomeProfile.CurrentValue = null;
                        else
                            Controller.Settings.defaultBiomeProfile.CurrentValue = p.name;
                        Controller.Settings.Write();
                    }

                    Rect btnDelete = new Rect(rowRect.xMax - 70f, rowRect.y, 70f, 24f);
                    if (Widgets.ButtonText(btnDelete, "Delete"))
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation($"Delete profile '{p.name}'?", () =>
                        {
                            if (BiomeProfileManager.DeleteProfile(p.name))
                            {
                                if (isDefault)
                                {
                                    Controller.Settings.defaultBiomeProfile.CurrentValue = null;
                                    Controller.Settings.Write();
                                }
                                Messages.Message($"Deleted profile {p.name}.", MessageTypeDefOf.TaskCompletion, false);
                            }
                        }));
                    }
                }
            }
        }

        private void DrawPerBiomePresetsSection(Listing_Standard list)
        {
            Text.Font = GameFont.Medium;
            list.Label("Per-Biome Presets");
            Text.Font = GameFont.Small;
            list.Gap(4);

            // Group by biome
            // We need to look into the folders to see what exists.
            string basePath = Path.Combine(GenFilePaths.ConfigFolderPath, "RealisticPlanets_BiomePresets");
            var dir = new DirectoryInfo(basePath);
            bool foundAny = false;

            if (dir.Exists)
            {
                var defaultPresets = Controller.Settings.defaultPresetPerBiome ?? new Dictionary<string, string>();
                
                foreach (var subDir in dir.GetDirectories())
                {
                    string defName = subDir.Name;
                    var presets = PerBiomePresetManager.GetPresetsForBiome(defName, false);
                    if (presets.Count > 0)
                    {
                        foundAny = true;
                        list.Label($"Biome: {defName}");
                        foreach (var preset in presets)
                        {
                            var p = preset;
                            bool isDefault = defaultPresets.TryGetValue(defName, out string defPreset) && defPreset == p.name;

                            Rect rowRect = list.GetRect(24f);
                            Widgets.DrawHighlightIfMouseover(rowRect);

                            Rect labelRect = new Rect(rowRect.x + 20f, rowRect.y, rowRect.width - 180f, rowRect.height);
                            string label = p.name;
                            if (isDefault)
                            {
                                GUI.color = Color.green;
                                label += " [Default]";
                            }
                            Widgets.Label(labelRect, label);
                            GUI.color = Color.white;

                            Rect btnDefault = new Rect(rowRect.xMax - 150f, rowRect.y, 70f, 24f);
                            if (Widgets.ButtonText(btnDefault, isDefault ? "Clear" : "Set Def"))
                            {
                                if (Controller.Settings.defaultPresetPerBiome == null)
                                    Controller.Settings.defaultPresetPerBiome = new Dictionary<string, string>();
                                    
                                if (isDefault)
                                    Controller.Settings.defaultPresetPerBiome.Remove(defName);
                                else
                                    Controller.Settings.defaultPresetPerBiome[defName] = p.name;
                                Controller.Settings.Write();
                            }

                            Rect btnDelete = new Rect(rowRect.xMax - 70f, rowRect.y, 70f, 24f);
                            if (Widgets.ButtonText(btnDelete, "Delete"))
                            {
                                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation($"Delete preset '{p.name}' for {defName}?", () =>
                                {
                                    if (PerBiomePresetManager.DeletePreset(defName, p.name))
                                    {
                                        if (isDefault)
                                        {
                                            Controller.Settings.defaultPresetPerBiome.Remove(defName);
                                            Controller.Settings.Write();
                                        }
                                        Messages.Message($"Deleted preset {p.name}.", MessageTypeDefOf.TaskCompletion, false);
                                    }
                                }));
                            }
                        }
                    }
                }
            }

            if (!foundAny)
            {
                GUI.color = Color.gray;
                list.Label("No per-biome presets found.");
                GUI.color = Color.white;
            }
        }

        private void DrawWorldGenPresetsSection(Listing_Standard list)
        {
            Text.Font = GameFont.Medium;
            list.Label("World Generation Presets");
            Text.Font = GameFont.Small;
            list.Gap(4);

            string defaultPreset = Controller.Settings.defaultWorldGenPreset.CurrentValue;
            var presets = WorldGenPresetManager.GetAllPresets(false);

            if (presets.Count == 0)
            {
                GUI.color = Color.gray;
                list.Label("No world gen presets found.");
                GUI.color = Color.white;
            }
            else
            {
                foreach (var preset in presets)
                {
                    var p = preset;
                    bool isDefault = (p.name == defaultPreset);

                    Rect rowRect = list.GetRect(24f);
                    Widgets.DrawHighlightIfMouseover(rowRect);

                    Rect labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width - 160f, rowRect.height);
                    string label = p.name;
                    if (isDefault)
                    {
                        GUI.color = Color.green;
                        label += " [Default]";
                    }
                    Widgets.Label(labelRect, label);
                    GUI.color = Color.white;

                    Rect btnDefault = new Rect(rowRect.xMax - 150f, rowRect.y, 70f, 24f);
                    if (Widgets.ButtonText(btnDefault, isDefault ? "Clear" : "Set Def"))
                    {
                        if (isDefault)
                            Controller.Settings.defaultWorldGenPreset.CurrentValue = null;
                        else
                            Controller.Settings.defaultWorldGenPreset.CurrentValue = p.name;
                        Controller.Settings.Write();
                    }

                    Rect btnDelete = new Rect(rowRect.xMax - 70f, rowRect.y, 70f, 24f);
                    if (Widgets.ButtonText(btnDelete, "Delete"))
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation($"Delete world gen preset '{p.name}'?", () =>
                        {
                            if (WorldGenPresetManager.DeletePreset(p.name))
                            {
                                if (isDefault)
                                {
                                    Controller.Settings.defaultWorldGenPreset.CurrentValue = null;
                                    Controller.Settings.Write();
                                }
                                Messages.Message($"Deleted preset {p.name}.", MessageTypeDefOf.TaskCompletion, false);
                            }
                        }));
                    }
                }
            }
        }

        private void DrawFactionPresetsSection(Listing_Standard list)
        {
            Text.Font = GameFont.Medium;
            list.Label("Faction Presets");
            Text.Font = GameFont.Small;
            list.Gap(4);

            string defaultPreset = Controller.Settings.defaultFactionProfile.CurrentValue;
            var presets = FactionPresetManager.GetAllPresets(false);

            if (presets.Count == 0)
            {
                GUI.color = Color.gray;
                list.Label("No faction presets found.");
                GUI.color = Color.white;
            }
            else
            {
                foreach (var preset in presets)
                {
                    var p = preset;
                    bool isDefault = (p.name == defaultPreset);

                    Rect rowRect = list.GetRect(24f);
                    Widgets.DrawHighlightIfMouseover(rowRect);

                    Rect labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width - 160f, rowRect.height);
                    string label = p.name;
                    if (isDefault)
                    {
                        GUI.color = Color.green;
                        label += " [Default]";
                    }
                    Widgets.Label(labelRect, label);
                    GUI.color = Color.white;

                    Rect btnDefault = new Rect(rowRect.xMax - 150f, rowRect.y, 70f, 24f);
                    if (Widgets.ButtonText(btnDefault, isDefault ? "Clear" : "Set Def"))
                    {
                        if (isDefault)
                            Controller.Settings.defaultFactionProfile.CurrentValue = null;
                        else
                            Controller.Settings.defaultFactionProfile.CurrentValue = p.name;
                        Controller.Settings.Write();
                    }

                    Rect btnDelete = new Rect(rowRect.xMax - 70f, rowRect.y, 70f, 24f);
                    if (Widgets.ButtonText(btnDelete, "Delete"))
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation($"Delete faction preset '{p.name}'?", () =>
                        {
                            if (FactionPresetManager.DeletePreset(p.name))
                            {
                                if (isDefault)
                                {
                                    Controller.Settings.defaultFactionProfile.CurrentValue = null;
                                    Controller.Settings.Write();
                                }
                                Messages.Message($"Deleted preset {p.name}.", MessageTypeDefOf.TaskCompletion, false);
                            }
                        }));
                    }
                }
            }
        }

        private void DrawLegacyPresetsSection(Listing_Standard list)
        {
            Text.Font = GameFont.Medium;
            list.Label("Legacy Biome Presets");
            Text.Font = GameFont.Small;
            list.Gap(4);

            var legacyPresets = BiomePresetManager.GetAllPresets(false);
            if (legacyPresets.Count > 0)
            {
                GUI.color = Color.yellow;
                list.Label($"Found {legacyPresets.Count} legacy biome presets from an older version.");
                GUI.color = Color.white;

                Rect btnRect = list.GetRect(30f);
                if (Widgets.ButtonText(new Rect(btnRect.x, btnRect.y, 200f, 30f), "Convert Legacy Presets"))
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Convert all legacy presets to the new Profile & Per-Biome system? This will migrate their data and delete the old files.", () =>
                    {
                        int converted = 0;
                        foreach (var oldPreset in legacyPresets)
                        {
                            // Create a new profile
                            BiomeProfile newProfile = new BiomeProfile();
                            newProfile.name = oldPreset.name;
                            newProfile.description = oldPreset.description;
                            newProfile.savedDateTicks = oldPreset.savedDateTicks;

                            foreach (var kvp in oldPreset.biomeMetadata)
                            {
                                string defName = kvp.Key;
                                var meta = kvp.Value;

                                // Save per-biome preset
                                PerBiomePresetManager.SavePreset(defName, oldPreset.name, meta, oldPreset.description);

                                // Add to profile
                                newProfile.entries[defName] = new BiomeProfileEntry
                                {
                                    Enabled = meta.Enabled,
                                    CommonalityMultiplier = meta.CommonalityMultiplier,
                                    ScoreOffset = meta.ScoreOffset,
                                    PresetName = oldPreset.name // Link to the per-biome preset we just saved
                                };
                            }

                            if (BiomeProfileManager.SaveProfile(newProfile))
                            {
                                BiomePresetManager.DeletePreset(oldPreset.name);
                                converted++;
                            }
                        }
                        Messages.Message($"Successfully converted {converted} legacy presets.", MessageTypeDefOf.PositiveEvent, false);
                    }));
                }
            }
            else
            {
                GUI.color = Color.gray;
                list.Label("No legacy presets found.");
                GUI.color = Color.white;
            }
        }
    }
}