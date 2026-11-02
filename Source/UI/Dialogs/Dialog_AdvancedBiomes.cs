using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Presets.Biomes;
using Planets.Utilities;

namespace Planets.UI.Dialogs
{
    /// <summary>
    /// ADVANCED BIOME CONFIGURATION DIALOG
    /// ===================================
    /// Per-world biome configuration interface with support for:
    /// - Individual biome toggles (Enabled/Disabled)
    /// - Frequency multipliers (0x to 5x). 0 removes the biome.
    /// - Search and filtering by mod source
    /// - Preset save/load system
    /// 
    /// Accessed via: World Creation > Advanced Climate > Biomes > "Open Biome Configuration..."
    /// </summary>
    public class Dialog_AdvancedBiomes : Window
    {
        private BiomeConfiguration config;
        private Vector2 scrollPos;
        private string searchFilter = "";
        private Dictionary<string, bool> sectionCollapsed = new Dictionary<string, bool>();
        public override Vector2 InitialSize => new Vector2(900f, 700f);

        public Dialog_AdvancedBiomes()
        {
            this.doCloseButton = true;
            this.doCloseX = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = true;
            this.config = Controller.Settings.biomeConfig;
            
            // Ensure config is initialized and up-to-date
            // We always call Init() to catch any biomes that might be missing from the dictionary
            // (e.g. if new mods were added, or if initialization was partial)
            config.Init();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Planets.AdvancedBiomeConfigTitle".Translate());
            Text.Font = GameFont.Small;

            // Header Row: Search and Global Buttons
            float topY = 40f;
            float searchWidth = 300f;
            searchFilter = Widgets.TextField(new Rect(0f, topY, searchWidth, 30f), searchFilter);
            if (string.IsNullOrEmpty(searchFilter))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(5f, topY + 4f, searchWidth, 25f), "Planets.SearchBiomes".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            // Preset Buttons
            float buttonWidth = 120f;
            float buttonX = inRect.width - buttonWidth;
            
            Rect resetClearRect = new Rect(buttonX, topY, buttonWidth, 30f);
            if (Widgets.ButtonText(resetClearRect, "Planets.ResetClear".Translate()))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                opts.Add(new FloatMenuOption("Planets.ResetAll".Translate(), () => BiomeProfileManager.ApplyBiomeDefaults(config)));
                opts.Add(new FloatMenuOption("Planets.SetAsDefaultMenu".Translate(), () => DoSetDefaultProfileFloatMenu()));
                opts.Add(new FloatMenuOption("Planets.ClearDefault".Translate(), () => {
                    Controller.Settings.defaultBiomeProfile.CurrentValue = null;
                    Controller.Settings.Write();
                    Messages.Message("Default biome profile cleared.", MessageTypeDefOf.TaskCompletion, false);
                }));
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            TooltipHandler.TipRegion(resetClearRect, "Reset All: reload the default profile, or the built-in biome values when no default profile is set. Clear Default: forget the auto-load profile. The biomes on screen stay until Reset All.");
            buttonX -= (buttonWidth + 10f);

            bool hasProfiles = BiomeProfileManager.GetAllProfiles(false).Any();
            if (!hasProfiles) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(buttonX, topY, buttonWidth, 30f), "Planets.DeleteProfile".Translate()))
            {
                if (hasProfiles) DoDeleteProfileFloatMenu();
            }
            GUI.color = Color.white;
            buttonX -= (buttonWidth + 10f);

            Rect loadRect = new Rect(buttonX, topY, buttonWidth, 30f);
            if (Widgets.ButtonText(loadRect, "Planets.LoadProfile".Translate()))
            {
                DoLoadProfileFloatMenu();
            }
            TooltipHandler.TipRegion(loadRect, "Load a profile. Use Reset/Clear, Set as Default, to make one load automatically.");
            buttonX -= (buttonWidth + 10f);
            
            if (Widgets.ButtonText(new Rect(buttonX, topY, buttonWidth, 30f), "Planets.SaveProfile".Translate()))
            {
                DoSaveProfileFloatMenu();
            }

            // Main List
            float listY = topY + 40f;
            float listHeight = inRect.height - listY - 50f;
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, CalculateContentHeight(inRect.width));
            
            Widgets.BeginScrollView(new Rect(0f, listY, inRect.width, listHeight), ref scrollPos, viewRect);
            
            Listing_Standard list = new Listing_Standard();
            list.Begin(viewRect);

            DrawBiomeList(list);

            list.End();
            Widgets.EndScrollView();
        }

        private static readonly string[] OdysseyBiomeDefNames = { "GlacialPlain", "Glowforest", "Grasslands", "LavaField", "Scarlands" };

        private static string GetSectionSortKey(string source)
        {
            if (source == "Core") return " A_Core";
            if (source == "Odyssey") return " B_Odyssey";
            return " C_" + source;
        }

        private void DrawBiomeList(Listing_Standard list)
        {
            // Group biomes by source: Core, Odyssey, then other mods.
            var biomes = GetFilteredBiomes()
                .GroupBy(b => GetBiomeSource(b))
                .OrderBy(g => GetSectionSortKey(g.Key));

            foreach (var group in biomes)
            {
                // Section Header
                if (!sectionCollapsed.ContainsKey(group.Key))
                    sectionCollapsed[group.Key] = false;

                bool collapsed = sectionCollapsed[group.Key];
                
                Rect headerRect = list.GetRect(30f);
                Widgets.DrawHighlightIfMouseover(headerRect);
                if (Widgets.ButtonInvisible(headerRect))
                {
                    sectionCollapsed[group.Key] = !collapsed;
                }
                
                // Draw arrow and label
                string arrow = collapsed ? "▶" : "▼";
                int totalCount = group.Count();
                int enabledCount = group.Count(b => config.GetMetadata(b.defName)?.Enabled ?? false);
                Widgets.Label(headerRect, $"  {arrow} {group.Key} ({enabledCount}/{totalCount})");
                
                if (!collapsed)
                {
                    list.Gap(4f);
                    foreach (var biome in group.OrderBy(b => b.label))
                    {
                        DrawBiomeRow(list, biome);
                    }
                    list.Gap(10f);
                }
            }
        }

        private void DrawBiomeRow(Listing_Standard list, BiomeDef biome)
        {
            var meta = config.GetMetadata(biome.defName);
            if (meta == null) return;

            Rect rowRect = list.GetRect(50f);
            Widgets.DrawHighlightIfMouseover(rowRect);
            
            Rect toggleRect = new Rect(rowRect.x, rowRect.y + 13f, 24f, 24f);
            bool isLocked = config.IsBiomeLocked(biome.defName, out string lockReason);
            if (isLocked)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                Widgets.ButtonImage(toggleRect, Widgets.CheckboxOffTex);
                GUI.color = Color.white;
                if (Mouse.IsOver(toggleRect)) TooltipHandler.TipRegion(toggleRect, lockReason);
                if (meta.Enabled) meta.Enabled = false;
            }
            else
            {
                Widgets.Checkbox(toggleRect.x, toggleRect.y, ref meta.Enabled);
            }

            Rect labelRect = new Rect(rowRect.x + 35f, rowRect.y, 150f, rowRect.height);
            bool isPerBiomeDefault = Controller.Settings?.defaultPresetPerBiome != null
                && Controller.Settings.defaultPresetPerBiome.TryGetValue(biome.defName, out string defaultPresetName)
                && !string.IsNullOrEmpty(defaultPresetName);
            bool hasLinkedPreset = !string.IsNullOrEmpty(meta.PresetName);
            string badges = (isPerBiomeDefault ? " [D]" : "") + (hasLinkedPreset ? " [P]" : "");
            Widgets.Label(labelRect, biome.LabelCap + badges);
            string labelTip = biome.description;
            if (isPerBiomeDefault)
                labelTip += "\n\n[D] Default per-biome preset. Used when no Advanced Biomes profile is the default.";
            if (hasLinkedPreset)
                labelTip += $"\n\n[P] Linked preset '{meta.PresetName}'. Saved with an Advanced Biomes profile.";
            TooltipHandler.TipRegion(labelRect, labelTip);

            float sliderX = labelRect.xMax + 10f;
            float reserved = Controller.AdvancedBiomeEditingAvailable ? 170f : 90f;
            float sliderWidth = Mathf.Max(160f, rowRect.xMax - sliderX - reserved);
            if (isLocked) GUI.color = Color.gray;
            
            Rect freqRect = new Rect(sliderX, rowRect.y + 5f, sliderWidth, 20f);
            meta.CommonalityMultiplier = Widgets.HorizontalSlider(freqRect, meta.CommonalityMultiplier, 0f, 5f, true, 
                $"Freq: {meta.CommonalityMultiplier:F2}x", "0x", "5x");
            TooltipHandler.TipRegion(freqRect, "Makes this biome more or less likely where its climate already fits. 0 removes it. It never places a biome outside its climate gates.");
            if (isLocked) GUI.color = Color.white;

            if (Controller.AdvancedBiomeEditingAvailable)
            {
                Rect editRect = new Rect(rowRect.xMax - 160f, rowRect.y + 10f, 70f, 30f);
                if (Widgets.ButtonText(editRect, "Edit"))
                    Find.WindowStack.Add(new Dialog_BiomeEditor(biome, config));
            }

            Rect resetRect = new Rect(rowRect.xMax - 80f, rowRect.y + 10f, 70f, 30f);
            if (Widgets.ButtonText(resetRect, "Reset"))
                config.ResetBiomeToDefault(biome.defName);
        }

        private IEnumerable<BiomeDef> GetFilteredBiomes()
        {
            return DefDatabase<BiomeDef>.AllDefsListForReading
                .Where(b => ShouldShowBiome(b))
                .Where(b => string.IsNullOrEmpty(searchFilter) || 
                    b.label.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        
        public static bool ShouldShowBiome(BiomeDef biome)
        {
            // Vanilla filtering: implemented, generatesNaturally
            if (!biome.implemented || !biome.generatesNaturally || biome.workerClass == null)
                return false;
            
            // Explicitly exclude Ocean and Lake (user requested)
            if (biome.defName == "Ocean" || biome.defName == "Lake")
                return false;
            
            // Check if biome can place on Surface layer using layerWhitelist/layerBlacklist
            // This works without needing a world loaded!
            PlanetLayerDef surfaceDef = PlanetLayerDefOf.Surface;
            if (surfaceDef == null)
            {
                // Fallback if Surface def not found (shouldn't happen)
                return true;
            }
            
            // Check whitelist: if whitelist exists and doesn't contain Surface, exclude
            if (!biome.layerWhitelist.NullOrEmpty() && !biome.layerWhitelist.Contains(surfaceDef))
                return false;
            
            // Check blacklist: if blacklist contains Surface, exclude
            if (!biome.layerBlacklist.NullOrEmpty() && biome.layerBlacklist.Contains(surfaceDef))
                return false;
            
            // Check if Surface layer requires whitelisted biomes only
            // If it does, and biome has no whitelist, exclude
            if (surfaceDef.onlyAllowWhitelistedBiomes && biome.layerWhitelist.NullOrEmpty())
                return false;
            
            // If we have a world loaded, also check CanPlaceOnLayer for overrides (e.g., Space biome)
            PlanetLayer surfaceLayer = GetSurfaceLayer();
            if (surfaceLayer != null)
            {
                BiomeWorker worker = biome.Worker;
                if (worker == null || !worker.CanPlaceOnLayer(biome, surfaceLayer))
                    return false;
            }
            
            return true;
        }
        
        private static PlanetLayer GetSurfaceLayer()
        {
            // Check if world is loaded (required for WorldGrid access)
            if (Find.World == null)
                return null;
            
            try
            {
                if (Find.WorldGrid?.PlanetLayers == null)
                    return null;
                
                foreach (var kvp in Find.WorldGrid.PlanetLayers)
                {
                    if (kvp.Value?.Def?.defName == "Surface")
                        return kvp.Value;
                }
            }
            catch
            {
                // WorldGrid may throw if world isn't fully initialized
                return null;
            }
            
            return null;
        }
        
        private string GetBiomeSource(BiomeDef biome)
        {
            // DLC biomes: Odyssey (GlacialPlain, Glowforest, etc.) under "Odyssey"
            if (ModsConfig.OdysseyActive && OdysseyBiomeDefNames.Contains(biome.defName))
                return "Odyssey";

            // Mod compat: Planets.Biomes.Compat.X - group by mod
            string ns = biome.workerClass?.Namespace ?? "";
            if (ns.Contains("Planets.Biomes.Compat.AlphaBiomes"))
                return "Alpha Biomes";
            if (ns.Contains("Planets.Biomes.Compat.MoreVanillaBiomes"))
                return "More Vanilla Biomes";
            if (ns.Contains("Planets.Biomes.Compat.MedievalOverhaul"))
                return "Medieval Overhaul";
            if (ns.Contains("Planets.Biomes.Compat.ReGrowth2"))
                return "ReGrowth 2";

            // Other mods: use modContentPack name
            if (biome.modContentPack != null)
            {
                string modName = biome.modContentPack.Name;
                if (modName.Contains("Alpha Biomes")) return "Alpha Biomes";
                if (modName.Contains("More Vanilla Biomes") || modName.Contains("Vanilla Biomes")) return "More Vanilla Biomes";
                if (modName.Contains("Medieval Overhaul")) return "Medieval Overhaul";
                if (modName.Contains("ReGrowth")) return "ReGrowth 2";
                return modName;
            }

            // Core: vanilla biomes + RP2 custom biomes (RP_Permafrost, RP_Oasis, etc.)
            return "Core";
        }
        
        private float CalculateContentHeight(float width)
        {
            float height = 0f;
            
            var biomes = GetFilteredBiomes()
                .GroupBy(b => GetBiomeSource(b));

            foreach (var group in biomes)
            {
                height += 30f; // Header
                if (!sectionCollapsed.TryGetValue(group.Key, out bool collapsed) || !collapsed)
                {
                    height += 4f; // Gap
                    height += group.Count() * 50f; // Rows
                    height += 10f; // Bottom gap
                }
            }
            
            // Add 15% buffer so all sections fit when expanded.
            height *= 1.15f;
            return height;
        }
        
        private void DoDeleteProfileFloatMenu()
        {
            var profiles = BiomeProfileManager.GetAllProfiles(true);
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            
            foreach (var profile in profiles)
            {
                var localProfile = profile;
                options.Add(new FloatMenuOption(localProfile.name, () => {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Delete biome profile '{localProfile.name}'?",
                        () => {
                        bool wasDefault = Controller.Settings.defaultBiomeProfile.CurrentValue == localProfile.name;
                        if (BiomeProfileManager.DeleteProfile(localProfile.name))
                        {
                            if (wasDefault)
                                Messages.Message($"Deleted profile '{localProfile.name}' and cleared default.", MessageTypeDefOf.TaskCompletion, false);
                            else
                                Messages.Message($"Deleted biome profile: {localProfile.name}", MessageTypeDefOf.TaskCompletion, false);
                        }
                            else
                            {
                                Messages.Message($"Failed to delete biome profile: {localProfile.name}", MessageTypeDefOf.RejectInput, false);
                            }
                        }
                    ));
                }));
            }
            
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("No profiles found", null));
            }
            
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DoLoadProfileFloatMenu()
        {
            var profiles = BiomeProfileManager.GetAllProfiles(true);
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            
            string defaultProfile = Controller.Settings.defaultBiomeProfile.CurrentValue;

            foreach (var profile in profiles)
            {
                var localProfile = profile;
                string label = localProfile.name;
                bool isDefault = localProfile.name == defaultProfile;
                
                if (isDefault)
                {
                    label += " (Default)";
                }
                
                options.Add(new FloatMenuOption(label, () => {
                    BiomeProfileManager.ApplyToConfig(localProfile, config);
                    Messages.Message($"Loaded biome profile: {localProfile.name}", MessageTypeDefOf.TaskCompletion, false);
                }));
            }
            
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("No profiles found", null));
            }
            
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DoSetDefaultProfileFloatMenu()
        {
            var profiles = BiomeProfileManager.GetAllProfiles(true);
            var options = new List<FloatMenuOption>();
            string current = Controller.Settings.defaultBiomeProfile.CurrentValue;
            foreach (var profile in profiles)
            {
                var localProfile = profile;
                string label = localProfile.name == current ? localProfile.name + " (Default)" : localProfile.name;
                options.Add(new FloatMenuOption(label, () =>
                {
                    Controller.Settings.defaultBiomeProfile.CurrentValue = localProfile.name;
                    Controller.Settings.Write();
                    Messages.Message($"Set '{localProfile.name}' as the default biome profile.", MessageTypeDefOf.TaskCompletion, false);
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No profiles found", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DoSaveProfileFloatMenu()
        {
            var profiles = BiomeProfileManager.GetAllProfiles(true);
            string defaultProfile = Controller.Settings.defaultBiomeProfile.CurrentValue;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Top option: create a new save (opens the existing save dialog)
            options.Add(new FloatMenuOption("(New Save)", () => DoSaveProfileDialog()));

            // Existing saves: overwrite with confirmation
            foreach (var profile in profiles)
            {
                var localProfile = profile;
                string label = localProfile.name;
                if (localProfile.name == defaultProfile)
                    label += " (Default)";

                options.Add(new FloatMenuOption(label, () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Overwrite biome profile '{localProfile.name}'?",
                        () => OverwriteProfile(localProfile.name),
                        true
                    ));
                })
                {
                    tooltip = new TipSignal($"{localProfile.name.AsTipTitle()}\n{localProfile.description}\n\n{localProfile.GetSummary()}\n\nSaved: {localProfile.SavedDate:yyyy-MM-dd HH:mm}")
                });
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OverwriteProfile(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            string existingDesc = BiomeProfileManager.LoadProfile(name)?.description ?? "";
            var profile = new BiomeProfile();
            profile.name = name;
            profile.description = existingDesc;
            profile.entries = new Dictionary<string, BiomeProfileEntry>();
            foreach (var kvp in config.BiomeMetadataMap)
            {
                var meta = kvp.Value;
                profile.entries[kvp.Key] = new BiomeProfileEntry
                {
                    Enabled = meta.Enabled,
                    CommonalityMultiplier = meta.CommonalityMultiplier,
                    ScoreOffset = meta.ScoreOffset,
                    PresetName = meta.PresetName
                };
            }

            if (BiomeProfileManager.SaveProfile(profile))
                Messages.Message($"Saved biome profile: {name}", MessageTypeDefOf.TaskCompletion, false);
            else
                Messages.Message($"Failed to save biome profile: {name}", MessageTypeDefOf.RejectInput, false);
        }
        
        private void DoSaveProfileDialog()
        {
            Find.WindowStack.Add(new Dialog_RenamePreset((name, description, setAsDefault) => {
                var profile = new BiomeProfile();
                profile.name = name;
                profile.description = description ?? "";
                profile.entries = new Dictionary<string, BiomeProfileEntry>();
                foreach (var kvp in config.BiomeMetadataMap)
                {
                    var meta = kvp.Value;
                    profile.entries[kvp.Key] = new BiomeProfileEntry
                    {
                        Enabled = meta.Enabled,
                        CommonalityMultiplier = meta.CommonalityMultiplier,
                        ScoreOffset = meta.ScoreOffset,
                        PresetName = meta.PresetName
                    };
                }
                if (!BiomeProfileManager.SaveProfile(profile))
                {
                    Messages.Message($"Failed to save biome profile: {name}", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (setAsDefault)
                {
                    Controller.Settings.defaultBiomeProfile.CurrentValue = name;
                    Controller.Settings.Write();
                    Messages.Message($"Saved profile '{name}' and set as default.", MessageTypeDefOf.TaskCompletion, false);
                }
                else
                {
                    Messages.Message($"Saved biome profile: {name}", MessageTypeDefOf.TaskCompletion, false);
                }
            }, BiomeProfileManager.ProfileExists, PresetFileNames.NextUnique("New Profile", BiomeProfileManager.ProfileExists), BuildEnabledBiomesDescription()));
        }

        private string BuildEnabledBiomesDescription()
        {
            // Full list of enabled biomes and their source, e.g. "Desert (Core)"
            var enabled = DefDatabase<BiomeDef>.AllDefsListForReading
                .Where(b => ShouldShowBiome(b))
                .Where(b => config.GetOrCreateMetadata(b.defName).Enabled)
                .Select(b => new { biome = b, source = GetBiomeSource(b) })
                .OrderBy(x => GetSectionSortKey(x.source))
                .ThenBy(x => x.biome.label)
                .Select(x => $"{x.biome.LabelCap} ({x.source})")
                .ToList();

            return enabled.Count == 0 ? "" : string.Join("\n", enabled);
        }

        public override void PostClose()
        {
            base.PostClose();
            Controller.Settings?.Write();
        }
    }
}
