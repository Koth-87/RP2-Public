using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MapModeFramework
{
    public class MapModeUI : Window
    {
        public MapModeComponent parent;
        private List<MapMode> MapModes => parent.mapModes;
        public MapMode CurrentMapMode => parent.currentMapMode;

        public override Vector2 InitialSize => new Vector2(curBaseX + Margin * 2f, curBaseY + Margin * 2f);
        private const float barWidth = 720f;  // Wide enough for Base+Geography+Climate+Hydrology+Ecology+Debug+Mods+Options
        private const float categoryBarHeight = 32f;
        private const float categoryButtonWidth = 86f;
        private const float categoryButtonMargin = 4f;
        private static readonly string[] CategoryOrder = { "Base", "Geography", "Climate", "Hydrology", "Ecology", "Debug", "Mods", "Options" };
        private const string ModsCategoryId = "Mods";

        /// <summary>Package ID of this mod (RP2). Modes from other mods go in the Mods dropdown.</summary>
        private static readonly string OurPackageId = "koth.RealisticPlanets2";

        private float curBaseX;
        private float curBaseY;

        float expandedHeight;
        bool windowExpanded;

        public MapModeUI(MapModeComponent parent)
        {
            this.parent = parent;
            doCloseX = false;
            doCloseButton = false;
            closeOnAccept = false;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            preventCameraMotion = false;
            doWindowBackground = false;
            draggable = true;
            drawShadow = false;
        }

        public override void SetInitialSizeAndPosition()
        {
            curBaseX = barWidth;
            curBaseY = Text.LineHeight + expandedHeight;
            Vector2 initialSize = InitialSize;
            windowRect = new Rect(0f, 0f, initialSize.x, initialSize.y);
            windowRect = windowRect.Rounded();
        }

        private void UpdateWindowSize()
        {
            curBaseX = barWidth;
            curBaseY = Text.LineHeight + expandedHeight;
            windowRect.width = curBaseX + Margin * 2f;
            windowRect.height = curBaseY + Margin * 2f;
            windowRect = windowRect.Rounded();
        }

        private void BindWindowPosition()
        {
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Verse.UI.screenWidth - windowRect.width);
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Verse.UI.screenHeight - windowRect.height);
        }

        public override void WindowUpdate()
        {
            if (!WorldRendererUtility.WorldRendered || !Planets.Core.Controller.Settings.showMMFUI.CurrentValue)
            {
                Close();
                return;
            }
            base.WindowUpdate();
        }

        public override void WindowOnGUI()
        {
            BindWindowPosition();
            base.WindowOnGUI();
        }

        /// <summary>True if this mode was added by a mod other than RP2 (compat for FTV, etc.).</summary>
        private static bool IsModAddedMode(MapMode mode)
        {
            if (mode?.def == null) return false;
            var pack = mode.def.modContentPack;
            if (pack == null) return false;
            return !string.Equals(pack.PackageId, OurPackageId, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Categories that have at least one map mode, in display order.</summary>
        private List<string> GetCategoriesInOrder()
        {
            var ordered = new List<string>();
            foreach (string cat in CategoryOrder)
            {
                if (cat == "Options")
                {
                    ordered.Add(cat);
                    continue;
                }
                if (cat == ModsCategoryId)
                {
                    if (MapModes.Any(IsModAddedMode))
                        ordered.Add(cat);
                    continue;
                }
                if (MapModes.Any(m => !IsModAddedMode(m) && ((m.def?.category.NullOrEmpty() ?? true) ? cat == "Base" : m.def.category == cat)))
                {
                    ordered.Add(cat);
                }
            }
            return ordered;
        }

        private void DoCategoryBar(Rect barRect)
        {
            List<string> categories = GetCategoriesInOrder();
            if (categories.Count == 0) return;

            float x = barRect.x;
            for (int i = 0; i < categories.Count; i++)
            {
                string category = categories[i];
                Rect btnRect = new Rect(x, barRect.y, categoryButtonWidth, categoryBarHeight - categoryButtonMargin * 2f);
                btnRect = btnRect.CenteredOnYIn(barRect);
                string buttonLabel = category == "Options" ? "MMF.UI.Category.Options".Translate()
                    : category == ModsCategoryId ? "MMF.UI.Category.Mods".Translate()
                    : category;
                bool isActiveCategory = category == ModsCategoryId ? IsModAddedMode(CurrentMapMode)
                    : CurrentMapMode?.def?.category == category
                    || (category == "Base" && (CurrentMapMode?.def?.category.NullOrEmpty() ?? true) && !IsModAddedMode(CurrentMapMode));
                if (Widgets.ButtonText(btnRect, buttonLabel))
                {
                    if (category == "Options")
                        OpenOptionsMenu(btnRect);
                    else
                        OpenCategoryMenu(category, btnRect);
                }
                if (isActiveCategory)
                {
                    Widgets.DrawHighlight(btnRect);
                }
                TooltipHandler.TipRegion(btnRect, string.Format("MMF.UI.Tooltip.Category".Translate(), buttonLabel));
                x += categoryButtonWidth + categoryButtonMargin;
            }
        }

        private void OpenCategoryMenu(string category, Rect buttonRect)
        {
            var options = new List<FloatMenuOption>();
            foreach (MapMode mode in MapModes)
            {
                if (category == ModsCategoryId)
                {
                    if (!IsModAddedMode(mode)) continue;
                }
                else
                {
                    string modeCat = mode.def?.category;
                    if (modeCat.NullOrEmpty()) modeCat = "Base";
                    if (modeCat != category || IsModAddedMode(mode)) continue;
                }

                MapMode captured = mode;
                string label = captured.def?.LabelCap ?? captured.Name;
                options.Add(new FloatMenuOption(label, () =>
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    captured.OnButtonClick();
                }));
            }
            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void OpenOptionsMenu(Rect buttonRect)
        {
            DrawSettings ds = parent.drawSettings;
            var options = new List<FloatMenuOption>();

            // Map content
            if (CurrentMapMode != null && CurrentMapMode.CanToggleWater && CurrentMapMode.def.allowWaterTiles)
            {
                AddOptionToggle(options, "MMF.DrawSettings.IncludeWater".Translate(), () => ds.includeWater, v => ds.includeWater = v, () => parent.RegenerateNow());
            }
            AddOptionToggle(options, "MMF.DrawSettings.ShowLakesAsWater".Translate(), () => ds.showLakesAsWater, v => ds.showLakesAsWater = v, () =>
            {
                parent.RegenerateNow();
                WorldLayer_Ridges.Instance?.SetDirty();
                WorldLayer_OrographicUplift.Instance?.SetDirty();
                WorldLayer_RainShadow.Instance?.SetDirty();
                WorldLayer_WindDirection.Instance?.SetDirty();
                WorldLayer_RidgeNormal.Instance?.SetDirty();
                WorldLayer_MonsoonWind.Instance?.SetDirty();
            }, "MMF.DrawSettings.ShowLakesAsWater.Tooltip".Translate());

            // UI toggles
            bool? displayLabels = CurrentMapMode?.def?.displayLabels;
            if (displayLabels.HasValue && displayLabels.Value)
            {
                AddOptionToggle(options, "MMF.DrawSettings.ShowLabel".Translate(), () => ds.displayLabels, v => ds.displayLabels = v, null);
            }
            bool? doTooltip = CurrentMapMode?.def?.doTooltip;
            if (doTooltip.HasValue && doTooltip.Value)
            {
                AddOptionToggle(options, "MMF.DrawSettings.ShowTooltip".Translate(), () => ds.doTooltip, v => ds.doTooltip = v, null);
            }
            AddOptionToggle(options, "MMF.DrawSettings.ShowFeaturesText".Translate(), () => !ds.disableFeaturesText, v => ds.disableFeaturesText = !v, null);

            // Vanilla world layers
            AddOptionToggle(options, "MMF.DrawSettings.DrawWorldObjects".Translate(), () => ds.drawWorldObjects, v => ds.drawWorldObjects = v, null);
            AddOptionToggle(options, "MMF.DrawSettings.DrawHills".Translate(), () => ds.drawHills, v => ds.drawHills = v, null);
            AddOptionToggle(options, "MMF.DrawSettings.DrawRivers".Translate(), () => ds.drawRivers, v => ds.drawRivers = v, null);
            AddOptionToggle(options, "MMF.DrawSettings.DrawRoads".Translate(), () => ds.drawRoads, v => ds.drawRoads = v, null);
            AddOptionToggle(options, "MMF.DrawSettings.DrawPollution".Translate(), () => ds.drawPollution, v => ds.drawPollution = v, null);

            // RP overlays
            AddOptionToggle(options, "MMF.DrawSettings.DrawRidges".Translate(), () => ds.drawRidges, v => ds.drawRidges = v, () => WorldLayer_Ridges.Instance?.SetDirty());
            AddOptionToggle(options, "MMF.DrawSettings.DrawOrographicUplift".Translate(), () => ds.drawOrographicUplift, v => ds.drawOrographicUplift = v, () => WorldLayer_OrographicUplift.Instance?.SetDirty());
            AddOptionToggle(options, "MMF.DrawSettings.DrawRainShadow".Translate(), () => ds.drawRainShadow, v => ds.drawRainShadow = v, () => WorldLayer_RainShadow.Instance?.SetDirty());
            AddOptionToggle(options, "MMF.DrawSettings.DrawWindDirection".Translate(), () => ds.drawWindDirection, v => ds.drawWindDirection = v, () => WorldLayer_WindDirection.Instance?.SetDirty(), "MMF.DrawSettings.WindDirection.Tooltip".Translate());
            AddOptionToggle(options, "MMF.DrawSettings.DrawRidgeNormal".Translate(), () => ds.drawRidgeNormal, v => ds.drawRidgeNormal = v, () => WorldLayer_RidgeNormal.Instance?.SetDirty());
            AddOptionToggle(options, "MMF.DrawSettings.DrawMonsoonWind".Translate(), () => ds.drawMonsoonWind, v => ds.drawMonsoonWind = v, () => WorldLayer_MonsoonWind.Instance?.SetDirty(), "MMF.DrawSettings.WindDirection.Tooltip".Translate());

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void AddOptionToggle(List<FloatMenuOption> options, string label, System.Func<bool> getSetting, System.Action<bool> setSetting, System.Action onDirty, TaggedString? tooltip = null)
        {
            bool current = getSetting();
            string menuLabel = current ? label + ": " + "MMF.Overlay.On".Translate() : label + ": " + "MMF.Overlay.Off".Translate();
            var opt = new FloatMenuOption(menuLabel, () =>
            {
                setSetting(!getSetting());
                onDirty?.Invoke();
            });
            if (tooltip.HasValue)
                opt.tooltip = new TipSignal(tooltip.Value);
            options.Add(opt);
        }

        public override void DoWindowContents(Rect inRect)
        {
            expandedHeight = 0f;
            Text.Font = GameFont.Small;
            string mapModeText = string.Format("{0}: {1}", "MMF.CurrentMapMode".Translate(), CurrentMapMode?.Name);
            Rect rectCurrentMapMode = new Rect(0f, 0f, Text.CalcSize(mapModeText).x + Widgets.CheckboxSize, Text.LineHeight);
            DoExpandButton(ref rectCurrentMapMode, mapModeText, ref windowExpanded);
            string banner = Planets.WorldGen.Climate.ClimateDiagnostics.Building
                ? "Building climate overlays…"
                : Planets.WorldGen.Climate.ClimateDiagnostics.StalenessBanner;
            if (!banner.NullOrEmpty())
            {
                Rect bannerRect = new Rect(rectCurrentMapMode.xMax + 12f, 0f, 640f, Text.LineHeight);
                GUI.color = Color.yellow;
                Widgets.Label(bannerRect, banner);
                GUI.color = Color.white;
            }
            if (windowExpanded)
            {
                Rect rectMapModeBar = new Rect(0f, rectCurrentMapMode.yMax, barWidth, categoryBarHeight);
                Widgets.DrawShadowAround(new Rect(rectMapModeBar) { width = rectMapModeBar.width, height = rectMapModeBar.height });
                Widgets.DrawWindowBackground(rectMapModeBar);
                DoCategoryBar(rectMapModeBar.ContractedBy(categoryButtonMargin));
            }
        }

        private void DoExpandButton(ref Rect inRect, string label, ref bool expanded)
        {
            if (expanded)
            {
                expandedHeight += categoryBarHeight;
            }
            UpdateWindowSize();
            Widgets.CheckboxLabeled(inRect, label, ref expanded, placeCheckboxNearText: true, texChecked: Resources.dropdownExpanded, texUnchecked: Resources.dropdown);
        }
    }
}
