using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.WorldGen.Constants;

namespace Planets.Compatibility
{
    /// <summary>
    /// Handles My Little Planet mod compatibility for Realistic Planets.
    /// Manages custom subdivision counts for planet generation and updates
    /// EarthScaleConstants.TILES_CIRCUMFERENCE for accurate distance calculations.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MyLittlePlanetCompat : ICompatibilityModule
    {
        private static MyLittlePlanetCompat _instance;
        public static MyLittlePlanetCompat Instance => _instance ?? (_instance = new MyLittlePlanetCompat());

        public static bool IsEnabled => Instance.IsActive;

        static MyLittlePlanetCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "oblitus.mylittleplanet";
        public string ModName => "My Little Planet";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }
        public void RenderIntegrationUI(Listing_Standard list) { }
        public void ExposeData() { }
        public void ResetToDefaults() { }

        // ========== INITIALIZATION ==========
        
        public void Initialize()
        {
            try
            {
                // Check if My Little Planet is active
                // Using manual check because PackageId might vary or be partial match
                IsActive = ModsConfig.ActiveModsInLoadOrder.Any(mod => 
                    mod.PackageId?.ToLower() == "oblitus.mylittleplanet" ||
                    mod.Name?.Contains("My Little Planet") == true);
                
                if (!IsActive)
                {
                    RPLog.D("[Compat] My Little Planet not detected");
                    return;
                }
                
                RPLog.D("[Compat] My Little Planet detected, initializing compatibility");
                RPLog.D("[Compat] My Little Planet compatibility initialized");
            }
            catch (Exception e)
            {
                RPLog.E($"[Compat] Error initializing My Little Planet compatibility: {e}");
                IsActive = false;
            }
        }
        
        /// <summary>
        /// Keeps MLP's planet size synced with RP2's subcount.
        /// Call this before WorldGen so MLP doesn't overwrite our subdivisions.
        /// </summary>
        public static void SyncSubcountToMLP()
        {
            if (!IsEnabled) return;
            try
            {
                var rulesType = AccessTools.TypeByName("WorldGenRules.WorldGenRules");
                var subcountField = rulesType != null ? AccessTools.Field(rulesType, "subcount") : null;
                if (subcountField != null)
                {
                    subcountField.SetValue(null, Core.Planets_GameComponent.subcount);
                    RPLog.D($"[Compat] Synced RP2 subcount ({Core.Planets_GameComponent.subcount}) to WorldGenRules.subcount");
                }
            }
            catch (Exception e)
            {
                RPLog.W($"[Compat] Failed to sync subcount to MLP: {e.Message}");
            }
        }
    }
}