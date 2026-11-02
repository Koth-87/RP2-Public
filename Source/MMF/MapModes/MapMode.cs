using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using UI = Verse.UI;

namespace MapModeFramework
{
    public class MapModeDef : Def
    {
        public Type mapModeClass;
        public Type worldLayerClass;
        public bool canCache;

        /// <summary>If true, this mode can show water tiles and the Include Water checkbox is available. If false, water is always hidden for this mode.</summary>
        public bool allowWaterTiles = false;
        public bool includeWater = false;
        public bool? drawWorldObjects;
        public bool? drawHills;
        public bool? drawRivers;
        public bool? drawRoads;
        public bool? drawPollution;
        public bool? disableFeaturesText;
        public bool? displayLabels;
        public bool? doTooltip;

        public string iconPath;

        /// <summary>Category for UI grouping (e.g. Base, Geography, Climate, Hydrology, Ecology, Debug).</summary>
        public string category = "Base";
        /// <summary>If true, this map mode appears only when RimWorld Dev Mode is on and the mod Debug Mode checkbox is checked.</summary>
        public bool debugOnly;

        [Unsaved(false)]
        private Texture2D icon;
        public Texture2D Icon
        {
            get
            {
                if (icon == null && iconPath != null)
                {
                    icon = ContentFinder<Texture2D>.Get(iconPath);
                }
                return icon ?? BaseContent.BadTex;
            }
        }

        public RegionProperties RegionProperties;
    }

    public class RegionProperties
    {
        public bool overrideSelector;
        public bool doBorders;
        public float borderWidth;
    }

    public abstract class MapMode
    {
        public MapModeComponent Parent => MapModeComponent.Instance;
        public bool IsRegenerating => WorldRegenHandler.regeneratingMapMode == this;
        public virtual string Name => def.LabelCap;
        public MapModeDef def;

        public Type WorldLayerClass => def.worldLayerClass;
        public abstract WorldLayer_MapMode WorldLayer { get; }
        public virtual bool CanToggleWater => false;
        
        public virtual bool Active => true;

        public MapMode() { }
        public MapMode(MapModeDef def)
        {
            this.def = def;
            Initialize();
        }

        public virtual void Initialize()
        {
        }

        public virtual void OnButtonClick()
        {
            MapModeComponent mapModeComponent = MapModeComponent.Instance;
            if (mapModeComponent.currentMapMode == this)
            {
                return;
            }
            mapModeComponent.RequestMapModeSwitch(this);
        }

        public virtual void DoPreRegenerate()
        {
        }

        public virtual void MapModeUpdate()
        {
        }

        public  virtual void MapModeOnGUI()
        {
            if (!TileUtilities.IsSurfaceLayerSelected())
                return;
            if (Active)
            {
                if (IsRegenerating)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Medium;
                    int tilesPrepared = WorldRegenHandler.tilesPrepared;
                    int tilesToPrepare = WorldRegenHandler.tilesToPrepare;
                    string progressText = "MMF.UI.LoadProgress".Translate(tilesPrepared, tilesToPrepare);
                    float statusWidth = Verse.UI.screenWidth;
                    float statusHeight = Text.LineHeight;
                    Rect rectStatus = new Rect(Verse.UI.screenWidth / 2f - statusWidth / 2f, Verse.UI.screenHeight * 3f / 4f, statusWidth, statusHeight);
                    Widgets.Label(rectStatus, progressText);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft;
                }
                else
                {
                    WorldLayer?.OnGUI();
                }
            }
        }

        public virtual void Notify_TileChanged(int tile)
        {
        }

        public virtual Material GetMaterial(int tile) => BaseContent.ClearMat;
        public virtual string GetTileLabel(int tile) => string.Empty;
        public virtual string GetTooltip(int tile) => string.Empty;
    }

    public interface IMapMode_Window
    {
        public abstract bool HasWindow(int tile);
        public abstract void OpenWindow();
    }

    public abstract class MapMode_Cached : MapMode
    {
        public bool EnabledCaching => def.canCache && Core.settings.enabledCaching[def]?.EnableCache == true;
        public bool CacheOnStart => EnabledCaching && Core.settings.enabledCaching[def].CacheOnStart;

        public override bool Active => !cachingNow;
        public bool cachingNow;
        public bool cached;

        /// <summary>False until this mode has a cache array. Regeneration must not mark an empty cache as finished.</summary>
        public virtual bool HasCacheData => true;

        public int tilesCached;
        public int tilesToCache;

        /// <summary>If true, cache is filled incrementally on the main thread (no background Task.Run).
        /// Prevents crashes when quitting while vanilla/Burst code (e.g. GenTemperature) is running.</summary>
        public virtual bool CacheOnMainThread => false;

        public MapMode_Cached() { }
        public MapMode_Cached(MapModeDef def) : base(def) { }

        /// <summary>Run one batch of cache work on the main thread. Returns (tiles cached this batch, true if fully done).
        /// Default returns (0, true) meaning "no batching, use background". Override only when CacheOnMainThread is true.</summary>
        public virtual (int cachedThisBatch, bool done) RunCacheBatch(int batchSize, CancellationToken token)
        {
            return (0, true);
        }

        public async Task PopulateCache(CancellationToken token)
        {
            if (IsRegenerating || cachingNow)
                return;
            cachingNow = true;
            bool succeeded = false;
            try
            {
                Task worker = Task.Run(() => StartCaching(token), token);
                TaskHandler.currentWorkerTask = worker;
                await worker;
                succeeded = !token.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (Find.World != null)
                    Core.Error($"Error during asynchronous caching of {this}: {ex.Message}");
            }
            finally
            {
                cachingNow = false;
                if (succeeded)
                {
                    cached = true;
                    if (Find.World != null && Parent != null)
                    {
                        try { Parent.Notify_CachingComplete(this); }
                        catch { /* ignore during shutdown */ }
                    }
                }
            }
        }

        protected abstract void StartCaching(CancellationToken token);

        public void ClearCache()
        {
            DoCacheClearing();
            tilesCached = 0;
            tilesToCache = 0;
            cached = false;
        }

        protected abstract void DoCacheClearing();

        public override void MapModeOnGUI()
        {
            base.MapModeOnGUI();
            bool regeneratingOrCaching = IsRegenerating || cachingNow;
            if (regeneratingOrCaching)
            {
                TimeSpan elapsed = DateTime.Now - WorldRegenHandler.startTime;
                int tilesPrepared = IsRegenerating ? WorldRegenHandler.tilesPrepared : tilesCached;
                int tilesToPrepare = IsRegenerating ? WorldRegenHandler.tilesToPrepare : tilesToCache;
                string progressText = "MMF.UI.LoadProgress".Translate(tilesPrepared, tilesToPrepare);
                float statusWidth = Verse.UI.screenWidth;
                float statusHeight = Text.LineHeight;
                Rect rectStatus = new Rect(Verse.UI.screenWidth / 2f - statusWidth / 2f, Verse.UI.screenHeight * 3f / 4f, statusWidth, statusHeight);
                if (!Active)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Medium;
                    Widgets.Label(rectStatus, progressText);
                    Text.Font = GameFont.Small;
                }
                if (elapsed.TotalSeconds >= 10 && !EnabledCaching)
                {
                    string tryCacheText = "MMF.UI.TryCache".Translate();
                    Vector2 tryCacheTextSize = Text.CalcSize(tryCacheText);
                    Widgets.Label(new Rect(Verse.UI.screenWidth / 2f - tryCacheTextSize.x / 2f, rectStatus.yMax, tryCacheTextSize.x, tryCacheTextSize.y), tryCacheText);
                }
                else if (EnabledCaching && !cached)
                {
                    string cachingText = "MMF.UI.Caching".Translate() + GenText.MarchingEllipsis();
                    Vector2 cachingTextSize = Text.CalcSize(cachingText);
                    Widgets.Label(new Rect(Verse.UI.screenWidth / 2f - cachingTextSize.x / 2f, rectStatus.yMax, cachingTextSize.x, cachingTextSize.y), cachingText);
                }
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }
    }

    public class MapMode_Default : MapMode
    {
        public override WorldLayer_MapMode WorldLayer => null;

        public MapMode_Default() { }
        public MapMode_Default(MapModeDef def) : base(def) { }
    }
}
