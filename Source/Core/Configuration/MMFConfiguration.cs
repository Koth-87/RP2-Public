using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using MapModeFramework;

namespace Planets.Core.Configuration
{
    /// <summary>
    /// MAP MODE FRAMEWORK CONFIGURATION
    /// ================================
    /// Manages caching settings for Map Mode Framework (MMF) integration.
    /// MMF is an integrated subsystem that provides visualization modes for the map.
    /// This configuration handles which map modes are cached and when caching occurs.
    /// </summary>
    public class MMFConfiguration
    {
        public Dictionary<string, bool> enabledCaching;
        private Vector2 scrollPos = Vector2.zero;
        private float mmfTabHeight = 600f;

        /// <summary>Render MMF caching settings UI</summary>
        public void RenderUI(Rect canvas)
        {
            float viewWidth = canvas.width - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, mmfTabHeight);

            Widgets.BeginScrollView(canvas, ref scrollPos, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.ColumnWidth = viewWidth - 8f;
            list.Begin(viewRect);

            GUI.color = Color.cyan;
            list.Label("Map Mode Framework Settings");
            GUI.color = Color.white;
            list.Gap(6);

            // Initialize MMF settings if needed
            if (MapModeFramework.Core.settings != null)
            {
                var mmfSettings = MapModeFramework.Core.settings;
                if (mmfSettings.enabledCaching != null && mmfSettings.enabledCaching.Any())
                {
                    list.Label("MMF.Settings.CacheData".Translate());
                    list.Gap(6);

                    foreach (var entry in mmfSettings.enabledCaching.ToList())
                    {
                        var def = entry.Key;
                        var setting = entry.Value;

                        bool enableCache = setting.EnableCache;
                        string cacheLabel = "MMF.Settings.EnableCaching".Translate() + ": " + def.LabelCap;
                        list.CheckboxLabeled(cacheLabel, ref enableCache);

                        if (enableCache)
                        {
                            bool cacheOnStart = setting.CacheOnStart;
                            string startLabel = "    " + "MMF.Settings.CacheOnStart".Translate();
                            list.CheckboxLabeled(startLabel, ref cacheOnStart);
                            setting.CacheOnStart = cacheOnStart;
                        }
                        else
                        {
                            setting.CacheOnStart = false;
                        }
                        setting.EnableCache = enableCache;
                        
                        list.Gap(6);
                    }
                }
                else
                {
                    list.Label("No cacheable map modes found.");
                }
            }
            else
            {
                list.Label("Map Mode Framework not initialized.");
            }

            list.End();
            mmfTabHeight = list.CurHeight + 12f;
            Widgets.EndScrollView();
        }

        /// <summary>Save which map modes keep a cache. Keyed by MapModeDef defName.</summary>
        public void ExposeData()
        {
            Scribe_Collections.Look(ref enabledCaching, "mmfEnabledCaching", LookMode.Value, LookMode.Value);
        }

        /// <summary>Copy the live cache checkboxes into the saved dictionary.</summary>
        public void CaptureFromRuntime()
        {
            MapModeFramework.Settings settings = MapModeFramework.Core.settings;
            if (settings?.enabledCaching == null)
                return;
            enabledCaching = new Dictionary<string, bool>();
            foreach (var entry in settings.enabledCaching)
            {
                if (entry.Key?.defName != null)
                    enabledCaching[entry.Key.defName] = entry.Value.EnableCache;
            }
        }

        /// <summary>Apply the saved cache checkboxes after map mode defs exist.</summary>
        public void ApplyToRuntime()
        {
            MapModeFramework.Settings settings = MapModeFramework.Core.settings;
            if (settings == null || enabledCaching == null)
                return;
            foreach (var entry in enabledCaching)
            {
                MapModeDef def = DefDatabase<MapModeDef>.GetNamedSilentFail(entry.Key);
                if (def == null)
                    continue;
                if (!settings.enabledCaching.TryGetValue(def, out CacheSetting setting))
                {
                    bool cacheOnStart = def != MapModeFrameworkDefOf.GrowingPeriod;
                    settings.enabledCaching[def] = new CacheSetting(entry.Value, cacheOnStart);
                }
                else
                {
                    setting.EnableCache = entry.Value;
                }
            }
        }

        /// <summary>Reset the cache checkboxes on the mod settings MMF tab. Caching starts on. Growing period does not cache at startup.</summary>
        public void ResetPageDefaults()
        {
            MapModeFramework.Settings settings = MapModeFramework.Core.settings;
            if (settings?.enabledCaching == null)
                return;
            foreach (var entry in settings.enabledCaching.ToList())
            {
                bool cacheOnStart = entry.Key != MapModeFrameworkDefOf.GrowingPeriod;
                entry.Value.SetCacheSettings(true, cacheOnStart);
            }
            CaptureFromRuntime();
        }
    }
}
