using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace MapModeFramework
{
    /// <summary>RP2: Conditional visibility for map modes. Debug-only modes show when RimWorld Dev Mode is on and the mod Debug Mode checkbox is checked.</summary>
    public static class MapModeVisibility
    {
        public static bool IsVisible(MapModeDef def)
        {
            if (def == null) return true;
            if (def.debugOnly && !Planets.Core.Controller.DebugEnabled)
                return false;
            if (def.defName != null && def.defName.StartsWith("RP_") && !Planets.WorldGen.Climate.Climate.IsInitialized())
                return false;
            return true;
        }
    }

    public class DrawSettings
    {
        public bool includeWater;
        public bool showLakesAsWater;
        public bool drawWorldObjects;
        public bool drawHills;
        public bool drawRivers;
        public bool drawRoads;
        public bool drawPollution;
        public bool drawRidges;
        public bool drawOrographicUplift;
        public bool drawRainShadow;
        public bool drawWindDirection;
        public bool drawRidgeNormal;
        public bool drawMonsoonWind;
        public bool disableFeaturesText;
        public bool displayLabels;
        public bool doTooltip;

        public DrawSettings()
        {
            Reset();
        }

        public void Reset()
        {
            includeWater = true;
            showLakesAsWater = true;
            drawWorldObjects = true;
            drawHills = true;
            drawRivers = true;
            drawRoads = true;
            drawPollution = true;
            drawRidges = false; // Off by default
            drawOrographicUplift = false;
            drawRainShadow = false;
            drawWindDirection = false;
            drawRidgeNormal = false;
            drawMonsoonWind = false;
            disableFeaturesText = false;
            displayLabels = true;
            doTooltip = true;
        }

        public void Update(MapModeDef def)
        {
            if (def.allowWaterTiles)
                includeWater = def.includeWater;
            else
                includeWater = false;
            drawWorldObjects = def.drawWorldObjects ?? drawWorldObjects;
            drawHills = def.drawHills ?? drawHills;
            drawRivers = def.drawRivers ?? drawRivers;
            drawRoads = def.drawRoads ?? drawRoads;
            drawPollution = def.drawPollution ?? drawPollution;
            disableFeaturesText = def.disableFeaturesText ?? disableFeaturesText;
            displayLabels = def.displayLabels ?? displayLabels;
            doTooltip = def.doTooltip ?? doTooltip;
        }
    }

    public class MapModeComponent : GameComponent
    {
        public static MapModeComponent Instance;

        public List<MapModeDef> mapModeDefs = new List<MapModeDef>();
        public List<MapMode> mapModes = new List<MapMode>();
        public bool mapModesInitialized;

        public MapMode currentMapMode;
        public DrawSettings drawSettings;

        public bool regenerateNow;

        /// <summary>Mode currently caching on the main thread (e.g. Growing Period). Null when none.</summary>
        private MapMode_Cached mainThreadCacheMode;
        private CancellationToken? mainThreadCacheToken;
        /// <summary>Tiles per frame for main-thread cache. GenTemperature is ~50-200ms per tile; keep low to avoid frame hitches.</summary>
        private const int MainThreadCacheBatchSize = 10;

        public MapModeComponent(Game game)
        {
            Instance = this;
            drawSettings = new DrawSettings();
        }

        public override void FinalizeInit()
        {
            if (!mapModesInitialized)
            {
                InitializeMapModes();
            }
        }

        public override void LoadedGame()
        {
            // Ensure map modes are initialized when loading a save
            // This handles cases where FinalizeInit might not run or runs before component is fully loaded
            if (!mapModesInitialized || mapModes.Count == 0)
            {
                InitializeMapModes();
            }
        }

        public void InitializeMapModes()
        {
            // Clear existing map modes to ensure clean initialization
            mapModes.Clear();
            
            // First, try to restore map modes from saved defs
            foreach (MapModeDef def in mapModeDefs.ToList())
            {
                if (def == null || !MapModeVisibility.IsVisible(def)) continue;
                
                try
                {
                    Type mapModeType = def.mapModeClass ?? typeof(MapMode_Default);
                    if (mapModeType == null)
                    {
                        Core.Warning($"MapModeDef {def.defName} has null mapModeClass, skipping");
                        continue;
                    }
                    
                    MapMode mapMode = Activator.CreateInstance(mapModeType, def) as MapMode;
                    if (mapMode == null)
                    {
                        Core.Warning($"Failed to create MapMode instance for {def.defName} (type: {mapModeType.FullName})");
                        continue;
                    }
                    mapModes.Add(mapMode);
                }
                catch (Exception ex)
                {
                    Core.Error($"Exception creating MapMode for {def?.defName ?? "null"}: {ex}");
                }
            }
            
            // Then add any new defs that weren't in the save
            List<MapModeDef> defs = DefDatabase<MapModeDef>.AllDefsListForReading;
            foreach (MapModeDef def in defs.Where(x => !mapModes.Any(y => y.def == x) && MapModeVisibility.IsVisible(x)))
            {
                try
                {
                    Type mapModeType = def.mapModeClass ?? typeof(MapMode_Default);
                    if (mapModeType == null)
                    {
                        Core.Warning($"MapModeDef {def.defName} has null mapModeClass, skipping");
                        continue;
                    }
                    
                    MapMode mapMode = Activator.CreateInstance(mapModeType, def) as MapMode;
                    if (mapMode == null)
                    {
                        Core.Warning($"Failed to create MapMode instance for {def.defName} (type: {mapModeType.FullName})");
                        continue;
                    }
                    mapModes.Add(mapMode);
                    mapModeDefs.Add(def);
                }
                catch (Exception ex)
                {
                    Core.Error($"Exception creating MapMode for {def?.defName ?? "null"}: {ex}");
                }
            }
            
            Reset();
            CacheMapModes();
            mapModesInitialized = true;
        }

        private void CacheMapModes()
        {
            for (int i = 0; i < mapModes.Count; i++)
            {
                if (mapModes[i] is not MapMode_Cached mapModeCached || !mapModeCached.CacheOnStart)
                    continue;
                TaskHandler.StartQueue((token) =>
                {
                    if (mapModeCached.CacheOnMainThread)
                    {
                        SetMainThreadCache(mapModeCached, token);
                        return Task.CompletedTask;
                    }
                    return mapModeCached.PopulateCache(token);
                });
            }
        }

        /// <summary>Start main-thread incremental caching. Work is done in GameComponentUpdate via RunCacheBatch.</summary>
        private void SetMainThreadCache(MapMode_Cached mode, CancellationToken token)
        {
            mainThreadCacheMode = mode;
            mainThreadCacheToken = token;
            mode.cachingNow = true;
        }

        /// <summary>Stop main-thread cache and optionally mark as complete. Call with markComplete=false on quit.</summary>
        public void ClearMainThreadCache(bool markComplete)
        {
            if (mainThreadCacheMode == null)
                return;
            mainThreadCacheMode.cachingNow = false;
            if (markComplete)
            {
                mainThreadCacheMode.cached = true;
                try
                {
                    if (Find.World != null && mainThreadCacheMode.Parent != null)
                        mainThreadCacheMode.Parent.Notify_CachingComplete(mainThreadCacheMode);
                }
                catch { /* ignore during shutdown */ }
            }
            mainThreadCacheMode = null;
            mainThreadCacheToken = null;
        }

        public override void GameComponentUpdate()
        {
            if (Find.World == null) return;

            try
            {
                if (mainThreadCacheMode != null && mainThreadCacheToken.HasValue)
                {
                    var token = mainThreadCacheToken.Value;
                    (_, bool done) = mainThreadCacheMode.RunCacheBatch(MainThreadCacheBatchSize, token);
                    if (done || token.IsCancellationRequested)
                        ClearMainThreadCache(done && !token.IsCancellationRequested);
                }

                bool showUI = Planets.Core.Controller.Settings.showMMFUI.CurrentValue;
                bool isRendered = WorldRendererUtility.WorldRendered;
                var currentUI = Find.WindowStack.WindowOfType<MapModeUI>();

                if (isRendered && showUI && currentUI == null)
                {
                    Find.WindowStack.Add(new MapModeUI(this));
                }
                else if ((!isRendered || !showUI) && currentUI != null)
                {
                    currentUI.Close();
                }

                currentMapMode?.MapModeUpdate();
            }
            catch (Exception ex)
            {
                Core.ErrorOnce($"Map mode update failed: {ex.Message}", "MapModeUpdate".GetHashCode());
            }
        }

        public override void GameComponentOnGUI()
        {
            if (Find.World == null || !TileUtilities.IsSurfaceLayerSelected()) return;
            try
            {
                if (!WorldRendererUtility.WorldRendered) return;
                currentMapMode?.MapModeOnGUI();
            }
            catch (Exception ex)
            {
                Core.ErrorOnce($"Map mode GUI failed: {ex.Message}", "MapModeOnGUI".GetHashCode());
            }
        }

        public void RequestMapModeSwitch(MapMode mapMode)
        {
            MapModeDef def = mapMode.def;
            if (mapMode.WorldLayerClass == null)
            {
                if (def != MapModeFrameworkDefOf.Default)
                {
                    Core.Warning($"Loaded MapModeDef {def.defName} that has no WorldLayerClass. This is likely unintended.");
                }
            }
            _ = WorldRegenHandler.RequestMapModeSwitch(mapMode);
        }

        public void SwitchMapMode(MapMode mapMode)
        {
            currentMapMode = mapMode;
            UpdateMapMode(mapMode.def);
            RegenerateNow();
        }

        public void UpdateMapMode(MapModeDef def)
        {
            drawSettings.Update(def);
            if (Find.World == null) return;
            try
            {
                Find.WorldFeatures.UpdateFeatures();
            }
            catch { /* ignore during shutdown */ }
        }

        public void Reset(bool fullReset = false)
        {
            try
            {
                currentMapMode = mapModes.FirstOrDefault(x => x.def == MapModeFrameworkDefOf.Default) ?? (mapModes.Count > 0 ? mapModes[0] : currentMapMode);
                drawSettings.Reset();
                regenerateNow = false;
                if (fullReset)
                {
                    mapModeDefs.Clear();
                    mapModes.Clear();
                    mapModesInitialized = false;
                    Core.ClearStaleWorldCaches();
                }
            }
            catch
            {
                regenerateNow = false;
            }
        }

        private int pendingRegens;

        public void RegenerateNow()
        {
            if (Find.World == null || currentMapMode == null) return;
            try
            {
                pendingRegens++;
                currentMapMode.DoPreRegenerate();
                regenerateNow = true;
            }
            catch (Exception ex)
            {
                Core.ErrorOnce($"Map mode regenerate failed: {ex.Message}", "RegenerateNow".GetHashCode());
            }
        }

        public void Notify_RegenerationComplete(MapMode mapMode)
        {
            if (mapMode == null)
            {
                if (pendingRegens == 0)
                    regenerateNow = false;
                return;
            }
            if (pendingRegens > 0)
                pendingRegens--;
            if (pendingRegens > 0)
                return;
            regenerateNow = false;
            if (mapMode is MapMode_Cached mapModeCached && mapModeCached.EnabledCaching && mapModeCached.HasCacheData)
                mapModeCached.cached = true;
        }

        /// <summary>Queue growing-period work on the main thread. Mesh building must not call GenTemperature itself.</summary>
        public void EnsureMainThreadCache(MapMode_Cached mode)
        {
            if (mode == null || mode.cachingNow || mode.cached)
                return;
            SetMainThreadCache(mode, CancellationToken.None);
        }

        public void Notify_CachingComplete(MapMode mapMode)
        {
            if (Find.World == null) return;
            if (currentMapMode == mapMode)
                RegenerateNow();
        }

        public void Notify_RegionChanged()
        {
            RegenerateNow();
        }

        public void Notify_TileChanged(int tile)
        {
            int mapModesCount = mapModes.Count;
            for (int i = 0; i < mapModesCount; i++)
            {
                MapMode mapMode = mapModes[i];
                mapMode.Notify_TileChanged(tile);
            }
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                mapModeDefs.Clear();
                foreach (MapMode mapMode in mapModes)
                {
                    if (mapMode?.def != null)
                        mapModeDefs.Add(mapMode.def);
                }
            }
            Scribe_Collections.Look(ref mapModeDefs, "MMF.MapModeDefs", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                mapModeDefs ??= new List<MapModeDef>();
                mapModes ??= new List<MapMode>();
                // Remove null entries - must use RemoveAll to avoid modifying collection while iterating
                mapModeDefs.RemoveAll(mapModeDef => mapModeDef == null);
                // Force re-initialization when loading from save to ensure all map modes are properly restored
                mapModesInitialized = false;
            }
        }
    }
}
