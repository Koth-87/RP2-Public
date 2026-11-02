using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Planets.Utilities.Logging;

namespace Planets.Core
{
    /// <summary>
    /// Main mod entry point for Realistic Planets.
    /// Responsibilities:
    /// - Apply Harmony patches at startup
    /// - Load and store ModSettings instance for global access
    /// - Provide debug mode property for other systems
    /// - Delegate Settings UI rendering to ModSettings class
    /// </summary>
    public class Controller : Mod
    {
        /// <summary>
        /// Global reference to this mod instance. Set in constructor.
        /// Used by patches (e.g. hotkey) to call WriteSettings().
        /// </summary>
        public static Controller Instance { get; private set; }

        /// <summary>
        /// Global reference to mod settings. Set in constructor.
        /// Other systems read from this to check user configuration.
        /// </summary>
        public static ModSettings Settings;

        // Source switches. Change them here. They are not player settings.
        // DebugEnabled, below, is the mod's Debug Mode setting. It is not RimWorld Dev Mode.

        /// <summary>
        /// Edit button on each biome row in Advanced Biomes.
        /// Off: the row keeps the frequency slider and hides Edit.
        /// </summary>
        public static bool AdvancedBiomeEditingAvailable = true;

        /// <summary>
        /// Tier 2 hydrology. Off hides the Tier 2 checkbox.
        /// Tier 2 also stays off unless Tier 1 is on, the Tier 2 checkbox is on, and Odyssey is active.
        /// </summary>
        public static bool Tier2HydrologyAvailable = true;

        /// <summary>
        /// Earth heightmap debug option in Mod Settings > Main (under Debug Mode).
        /// Off hides the toggle and prevents loading the external ETOPO heightmap until a revised heightmap is bundled.
        /// </summary>
        public static bool EarthHeightmapAvailable = false;
        
        /// <summary>
        /// Harmony instance for patch management
        /// </summary>
        private static Harmony _harmony;
        public static Harmony harmony => _harmony;
        
        /// <summary>
        /// Mod Debug Mode. True only when RimWorld Dev Mode is on and the Debug Mode checkbox is checked.
        /// Dev Mode off clears the checkbox, so turning Dev Mode back on shows it unchecked.
        /// This does not depend on a DEBUG or release build.
        /// On: diagnostic logs run. Rainfall, temperature, and hydrology also run their comparison pass when Run Comparison Tests is on.
        /// Off: those branches are skipped. RPLog.E still logs errors. Debug-only map modes stay hidden.
        /// </summary>
        public static bool DebugEnabled
        {
            get
            {
                if (!Prefs.DevMode)
                {
                    if (Settings?.debugMode != null && Settings.debugMode.CurrentValue)
                        Settings.debugMode.CurrentValue = false;
                    return false;
                }
                return Settings?.debugMode?.CurrentValue ?? false;
            }
        }
        
        /// <summary>
        /// Constructor called when mod loads.
        /// Applies Harmony patches and loads ModSettings instance.
        /// </summary>
        public Controller(ModContentPack content) : base(content)
        {
            Instance = this;
            const string Id = "rimworld.mod.koth.realisticplanets";

            // Load mod settings first so patches can read configuration in Prepare()
            Settings = GetSettings<ModSettings>();
            
            _harmony = new Harmony(Id);
            
            // Apply all Harmony patches (auto-discovery via [HarmonyPatch] attributes)
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                try
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Any())
                    {
                        _harmony.CreateClassProcessor(type).Patch();
                    }
                }
                catch (Exception e)
                {
                    RPLog.E($"Realistic Planets: Failed to patch {type.Name}: {e.Message}");
                }
            }
            
            if (DebugEnabled)
                RPLog.D("[Core] Controller initialized, patches applied");
        }
        
        /// <summary>
        /// RimWorld calls this to get the mod settings category name.
        /// </summary>
        public override string SettingsCategory()
        {
            return "Planets.ModName".Translate();
        }
        
        /// <summary>
        /// RimWorld calls this to render the Settings dialog.
        /// Delegates to ModSettings.DoSettingsWindowContents().
        /// </summary>
        public override void DoSettingsWindowContents(Rect canvas)
        {
            Settings.DoSettingsWindowContents(canvas);
        }
        
        /// <summary>
        /// RimWorld calls this when player closes the Settings dialog.
        /// Calls base to handle serialization.
        /// </summary>
        public override void WriteSettings()
        {
            Settings?.mmfConfig?.CaptureFromRuntime();
            base.WriteSettings();
            MapModeFramework.Core.OnSettingsWrite();
        }
    }
}
