using HarmonyLib;
using RimWorld.Planet;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse.Sound;
using Verse.Profile;
using System.Threading;
using System.Linq;
using Verse;

namespace MapModeFramework
{
    [HarmonyPatch(typeof(MemoryUtility), nameof(MemoryUtility.ClearAllMapsAndWorld))]
    public static class MemoryUtility_ClearAllMapsAndWorld_Patch
    {
        public static void Prefix()
        {
            Planets.WorldGen.Climate.Climate.ClearCache();
            Planets.WorldGen.Climate.ClimateDiagnostics.Clear();
            if (Find.World != null)
                Core.KillAllProcessesSynchronously();
        }
    }

    [HarmonyPatch(typeof(Page), "DoBack")]
    public static class Page_DoBack_Patch
    {
        public static void Prefix(Page __instance)
        {
            if (__instance is not Page_SelectStartingSite)
            {
                return;
            }
            Core.KillAllProcessesSynchronously();
        }
    }

    [HarmonyPatch(typeof(WorldSelector), "SelectUnderMouse")]
    public static class WorldSelector_SelectUnderMouse_Patch
    {
        public static bool Prefix(WorldSelector __instance, bool canSelectTile = true)
        {
            MapModeComponent mapModeComponent = MapModeComponent.Instance;
            if (mapModeComponent == null)
            {
                return true;
            }
            if (mapModeComponent.currentMapMode is not MapMode_Region mapModeRegion)
            {
                return true;
            }
            if (mapModeRegion.def.RegionProperties?.overrideSelector != true)
            {
                return true;
            }
            if (Current.ProgramState == ProgramState.Playing)
            {
                Thing colonistBarTarget = Find.ColonistBar?.ColonistOrCorpseAt(Verse.UI.MousePositionOnUIInverted);
                Pawn colonist = colonistBarTarget as Pawn;
                if (colonistBarTarget != null && (colonist == null || !colonist.IsCaravanMember()))
                    return true;
            }
            List<WorldObject> underMouse = GenWorldUI.WorldObjectsUnderMouse(Verse.UI.MousePositionOnUI);
            if (underMouse != null && underMouse.Count > 0)
                return true;
            if (!canSelectTile || !TileUtilities.TryGetSurfaceMouseTile(out int tile))
                return true;
            Region region = ModCompatibility.DrawTile(tile) ? mapModeRegion.GetRegion(tile) : null;
            if (region != null)
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            WorldLayer_SelectedRegion.Instance.selectedRegion = region;
            __instance.SelectedTile = -1;
            return false;
        }

        public static void Postfix(bool canSelectTile = true)
        {
            if (!canSelectTile || !TileUtilities.TryGetSurfaceMouseTile(out int tile))
                return;
            MapModeComponent mapModeComponent = MapModeComponent.Instance;
            if (mapModeComponent == null)
            {
                return;
            }
            if (mapModeComponent.currentMapMode is IMapMode_Window mapModeWindow && mapModeWindow.HasWindow(tile))
            {
                mapModeWindow.OpenWindow();
            }
        }
    }

    [HarmonyPatch(typeof(WorldFeatures), "UpdateFeatures")]
    public static class WorldFeatures_UpdateFeatures_Patch
    {
        public static bool Prefix(List<WorldFeatureTextMesh> ___texts)
        {
            MapModeComponent mapModeComponent = MapModeComponent.Instance;
            if (mapModeComponent == null)
                return true;
            bool wantActive = !mapModeComponent.drawSettings.disableFeaturesText;
            SetTextsActive(___texts, wantActive);
            return wantActive;
        }

        public static void SetTextsActive(List<WorldFeatureTextMesh> ___texts, bool active)
        {
            for (int i = 0; i < ___texts.Count; i++)
            {
                ___texts[i].SetActive(active);
            }
        }
    }

    [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandableWorldObjectsOnGUI))]
    public static class ExpandableWorldObjectsUtility_ExpandableWorldObjectsOnGUI_Patch
    {
        public static bool Prefix()
        {
            MapModeComponent mapModeComponent = MapModeComponent.Instance;
            if (mapModeComponent == null)
            {
                return true;
            }
            if (mapModeComponent.drawSettings.drawWorldObjects)
            {
                return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldDrawLayerBase), nameof(WorldDrawLayerBase.Render))]
    public static class WorldDrawLayerBase_Render_Patch
    {
        public static Dictionary<Type, Func<DrawSettings, bool>> disableRendering = new Dictionary<Type, Func<DrawSettings, bool>>()
        {
            { typeof(WorldDrawLayer_Hills), settings => settings.drawHills },
            { typeof(WorldDrawLayer_Rivers), settings => settings.drawRivers },
            { typeof(WorldDrawLayer_Roads), settings => settings.drawRoads },
            { typeof(WorldDrawLayer_Pollution), settings => settings.drawPollution },
            { typeof(WorldLayer_Ridges), settings => settings.drawRidges },
            { typeof(WorldLayer_OrographicUplift), settings => settings.drawOrographicUplift },
            { typeof(WorldLayer_RainShadow), settings => settings.drawRainShadow },
            { typeof(WorldLayer_WindDirection), settings => settings.drawWindDirection },
            { typeof(WorldLayer_RidgeNormal), settings => settings.drawRidgeNormal },
            { typeof(WorldLayer_MonsoonWind), settings => settings.drawMonsoonWind }
        };

        /// <summary>Overlay layer types that draw on water tiles. If not present or false, the overlay skips water tiles.</summary>
        public static Dictionary<Type, bool> OverlayDrawOnWater = new Dictionary<Type, bool>()
        {
            { typeof(WorldLayer_WindDirection), true },
            { typeof(WorldLayer_MonsoonWind), false },
            { typeof(WorldLayer_RidgeNormal), false },
            { typeof(WorldLayer_OrographicUplift), false },
            { typeof(WorldLayer_RainShadow), false },
            { typeof(WorldLayer_Ridges), false }
        };

        public static bool OverlayDrawsOnWater(Type layerType)
        {
            return OverlayDrawOnWater.TryGetValue(layerType, out bool drawOnWater) && drawOnWater;
        }

        public static bool OverlayDrawDisabled(Type layerType)
        {
            if (!disableRendering.TryGetValue(layerType, out Func<DrawSettings, bool> drawSetting))
                return false;
            DrawSettings settings = MapModeComponent.Instance?.drawSettings;
            return settings != null && !drawSetting(settings);
        }

        public static bool Prefix(WorldDrawLayerBase __instance)
        {
            MapModeComponent mapModeComponent = MapModeComponent.Instance;
            if (mapModeComponent == null)
            {
                return true;
            }
            bool overrideSelector = mapModeComponent.currentMapMode is MapMode_Region mapModeRegion && mapModeRegion.def.RegionProperties?.overrideSelector == true;
            bool isSelectorLayer = __instance is WorldDrawLayer_MouseTile || __instance is WorldDrawLayer_SelectedTile;
            if (overrideSelector && isSelectorLayer)
            {
                return false;
            }
            Type worldLayerType = __instance.GetType();
            if (disableRendering.TryGetValue(__instance.GetType(), out Func<DrawSettings, bool> drawSetting))
            {
                DrawSettings drawSettings = mapModeComponent.drawSettings;
                if (!drawSetting(drawSettings))
                {
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Page_SelectStartingSite), nameof(Page_SelectStartingSite.PostOpen))]
    public static class Page_SelectStartingSite_PostOpen_Patch
    {
        public static void Postfix()
        {
            MapModeComponent mapModeComponent = MapModeComponent.Instance;
            if (mapModeComponent != null && !mapModeComponent.mapModesInitialized)
            {
                mapModeComponent.InitializeMapModes();
            }
        }
    }
}
