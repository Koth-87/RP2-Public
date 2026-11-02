using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Pollution Tweaks compatibility module.
    /// Adds "Pollution Range" slider and "Adjust pollution variance" toggle to world creation page.
    /// </summary>
    [StaticConstructorOnStartup]
    public class PollutionTweaksCompat : ICompatibilityModule
    {
        private static PollutionTweaksCompat _instance;
        public static PollutionTweaksCompat Instance => _instance ?? (_instance = new PollutionTweaksCompat());

        public static bool IsEnabled => Instance.IsActive;

        static PollutionTweaksCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "kathanon.PollutionTweaks";
        public string ModName => "Pollution Tweaks";
        public bool IsActive { get; private set; }
        
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => "";

        // Reflection fields
        private Type patchType;
        private FieldInfo rangeField;
        private FieldInfo adjustField;
        
        // Cached translations
        private string labelPollutionRange;
        private string tipPollutionRange;
        private string labelPollutionSpread;
        private string tipPollutionSpread;

        public void Initialize()
        {
            try
            {
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive) return;
                
                // Get the assembly and type
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "PollutionTweaks");
                
                if (assembly == null)
                {
                    RPLog.Warning($"[Compat] {ModName} detected but assembly not found");
                    IsActive = false;
                    return;
                }
                
                patchType = assembly.GetType("PollutionTweaks.Patch_Page_CreateWorldParams");
                
                if (patchType == null)
                {
                    RPLog.Warning($"[Compat] {ModName}: Patch_Page_CreateWorldParams type not found");
                    IsActive = false;
                    return;
                }

                rangeField = patchType.GetField("range", BindingFlags.Public | BindingFlags.Static);
                adjustField = patchType.GetField("adjust", BindingFlags.Public | BindingFlags.Static);

                if (rangeField == null || adjustField == null)
                {
                    RPLog.Warning($"[Compat] {ModName}: Required fields not found");
                    IsActive = false;
                    return;
                }

                // Cache translations
                labelPollutionRange = "kathanon.PollutionTweaks.PollutionRange".Translate();
                tipPollutionRange = "kathanon.PollutionTweaks.PollutionRangeTip".Translate();
                labelPollutionSpread = "kathanon.PollutionTweaks.PollutionSpread".Translate();
                tipPollutionSpread = "kathanon.PollutionTweaks.PollutionSpreadTip".Translate();
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to initialize {ModName} compatibility: {e}");
                IsActive = false;
            }
        }

        public void RenderIntegrationUI(Listing_Standard list)
        {
            // No global settings needed
        }

        public void ExposeData()
        {
            // No persistent data needed
        }

        public void ResetToDefaults()
        {
            Reset();
        }

        public void OnWorldCreationButtonClicked(List<FactionDef> factions)
        {
            // No separate window
        }

        /// <summary>
        /// Resets Pollution Tweaks settings to defaults.
        /// </summary>
        public static void Reset()
        {
            if (!IsEnabled) return;
            
            try
            {
                Instance.rangeField.SetValue(null, new FloatRange(0.25f, 1f));
                Instance.adjustField.SetValue(null, false);
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Error resetting {Instance.ModName}: {e}");
            }
        }

        /// <summary>
        /// Draws the Pollution Tweaks controls (Range slider + Spread checkbox).
        /// Returns the new Y position.
        /// </summary>
        public static float DrawControls(float yPos, float width)
        {
            if (!IsEnabled) return yPos;

            try
            {
                // Get current values
                var range = (FloatRange)Instance.rangeField.GetValue(null);
                bool adjust = (bool)Instance.adjustField.GetValue(null);

                // 1. Pollution Range Slider
                Rect rangeRect = new Rect(0f, yPos, width, 30f);
                TooltipHandler.TipRegion(rangeRect, Instance.tipPollutionRange);
                
                Widgets.Label(rangeRect.LeftPartPixels(200f), Instance.labelPollutionRange);
                
                Rect sliderRect = rangeRect;
                sliderRect.xMin += 200f;
                Widgets.FloatRange(sliderRect, 56419169, ref range, valueStyle: ToStringStyle.PercentZero);
                
                yPos += 40f;

                // 2. Pollution Spread Checkbox
                Rect spreadRect = new Rect(0f, yPos, width, 30f);
                TooltipHandler.TipRegion(spreadRect, Instance.tipPollutionSpread);
                
                Widgets.Label(spreadRect.LeftPartPixels(200f), Instance.labelPollutionSpread);
                Widgets.Checkbox(new Vector2(spreadRect.x + 200f, spreadRect.y), ref adjust);

                // Update values
                Instance.rangeField.SetValue(null, range);
                Instance.adjustField.SetValue(null, adjust);
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Error drawing {Instance.ModName} controls: {e}");
            }

            return yPos;
        }
    }
}
