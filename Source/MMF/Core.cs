using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Planets.Utilities;
using Planets.Utilities.Logging;

namespace MapModeFramework
{
    [StaticConstructorOnStartup]
    public static class Core
    {
        public static List<BiomeDef> allNaturalBiomes;

        public static Settings settings;
        public static readonly Color MMF_Color = new Color(1f, 0.647f, 0f);
        public static readonly string prefix = "[Realistic Planets - Map Modes]".Colorize(MMF_Color);
        private static readonly System.Collections.Generic.HashSet<int> warningOnceKeys = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.HashSet<int> errorOnceKeys = new System.Collections.Generic.HashSet<int>();

        static Core()
        {
            // MMF initialization is now handled by the main Controller
            // This static constructor just ensures the class is loaded
            // The actual initialization happens in Initialize() method
        }
        
        /// <summary>
        /// Initialize MMF components. Called by the main Controller.
        /// </summary>
        public static void Initialize()
        {
            // Initialize MMF's own settings (not the main settings)
            // Since we're now a static class, we don't have a ModContentPack to get settings from
            // The settings will be managed through the main controller's integration settings
            settings = new Settings();
            
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                settings.InitializeSettings();
                Planets.Core.Controller.Settings?.mmfConfig?.ApplyToRuntime();
                CacheRelevantLists();
                ModCompatibility.GetAssemblies();
            });
        }

        public static void CacheRelevantLists()
        {
            allNaturalBiomes = DefDatabase<BiomeDef>.AllDefsListForReading.Where(biome => biome.generatesNaturally).ToList();
        }

        public static void KillAllProcessesSynchronously()
        {
            WorldRegenHandler.Interrupt();
            TaskHandler.KillQueue();
            MapModeComponent.Instance?.ClearMainThreadCache(false);

            if (!WorldRegenHandler.IsBusy && !TaskHandler.IsBusy)
            {
                ClearStaleWorldCaches();
                WorldRegenHandler.Reset(true);
                MapModeComponent.Instance?.Reset(true);
                return;
            }

            if (Find.World == null)
            {
                ClearStaleWorldCaches();
                WorldRegenHandler.Reset(true);
                MapModeComponent.Instance?.Reset(true);
                return;
            }

            Task cacheWorker = TaskHandler.currentWorkerTask;
            if (cacheWorker != null && !cacheWorker.IsCompleted)
            {
                try { cacheWorker.Wait(2000); }
                catch { }
            }

            Task regenWorker = WorldRegenHandler.currentWorkerTask;
            if (regenWorker != null && !regenWorker.IsCompleted && Find.World != null)
            {
                try { regenWorker.Wait(1000); }
                catch { }
            }

            ClearStaleWorldCaches();
            WorldRegenHandler.Reset(true);
            MapModeComponent.Instance?.Reset(true);
        }

        /// <summary>Drop region caches that belong to the world being left.</summary>
        public static void ClearStaleWorldCaches()
        {
            RegionCache<WorldFeature>.cachedRegions?.Clear();
            EdgesCache.cachedEdges?.Clear();
            if (WorldLayer_SelectedRegion.Instance != null)
                WorldLayer_SelectedRegion.Instance.selectedRegion = null;
        }

        public static void Message(string text)
        {
            RPLog.D(prefix + " " + text);
        }

        public static void Warning(string text)
        {
            RPLog.W(prefix + " " + text);
        }

        public static void WarningOnce(string text, int key)
        {
            if (warningOnceKeys.Add(key))
                RPLog.W(prefix + " " + text);
        }

        public static void Error(string text)
        {
            RPLog.E(prefix + " " + text);
        }

        public static void ErrorOnce(string text, int key)
        {
            if (errorOnceKeys.Add(key))
                RPLog.E(prefix + " " + text);
        }

        /// <summary>
        /// Clear cached map modes when settings are written. Called by main Controller.
        /// </summary>
        public static void OnSettingsWrite()
        {
            List<MapMode> mapModes = MapModeComponent.Instance?.mapModes;
            if (mapModes == null)
            {
                return;
            }
            foreach (MapMode_Cached mapMode in mapModes.OfType<MapMode_Cached>())
            {
                mapMode.ClearCache();
            }
        }

        // Settings are now integrated into main Realistic Planets settings
        // Comment out or remove these methods to disable separate settings window
        /*
        public override void DoSettingsWindowContents(Rect inRect)
        {
            settings.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return base.Content.Name;
        }
        */
    }

    [DefOf]
    public static class MapModeFrameworkDefOf
    {
        public static MapModeDef Default;
        public static MapModeDef Biome;
        public static MapModeDef Temperature;
        public static MapModeDef Elevation;
        public static MapModeDef Rainfall;
        public static MapModeDef GrowingPeriod;
        public static MapModeDef Features;
    }
}
