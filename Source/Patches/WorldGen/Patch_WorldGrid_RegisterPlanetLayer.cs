using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.WorldGen.Constants;

namespace Planets.Patches.WorldGen
{
    /// <summary>
    /// Applies the custom planet scale (subdivision count) to the world grid generation.
    /// Replaces the need for My Little Planet by natively overriding the subdivisions.
    /// Also updates EarthScaleConstants.TILES_CIRCUMFERENCE for accurate distance calculations.
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid))]
    public static class Patch_WorldGrid_RegisterPlanetLayer
    {
        [HarmonyPatch("RegisterPlanetLayer")]
        [HarmonyPatch(new[] { typeof(PlanetLayerDef), typeof(PlanetLayerSettings), typeof(float?), typeof(Vector3?), typeof(Vector3?), typeof(float?) })]
        [HarmonyPrefix]
        public static void RegisterPlanetLayer_Settings_Prefix(PlanetLayerDef layer, ref PlanetLayerSettings settings)
        {
            if (layer != PlanetLayerDefOf.Surface || settings == null)
                return;

            if (!Planets_GameComponent.generatingWithRP)
            {
                EarthScaleConstants.SetForSubdivisions(settings.subdivisions);
                return;
            }

            int subcount = Planets_GameComponent.subcount;

            var modifiedSettings = new PlanetLayerSettings
            {
                radius = settings.radius,
                origin = settings.origin,
                viewAngle = settings.viewAngle,
                useSurfaceViewAngle = settings.useSurfaceViewAngle,
                extraCameraAltitude = settings.extraCameraAltitude,
                backgroundWorldCameraOffset = settings.backgroundWorldCameraOffset,
                backgroundWorldCameraParallaxDistancePer100Cells = settings.backgroundWorldCameraParallaxDistancePer100Cells,
                subdivisions = subcount
            };

            settings = modifiedSettings;

            UpdateEarthScaleConstantsForSubdivision(subcount);
        }

        [HarmonyPatch("RegisterPlanetLayer")]
        [HarmonyPatch(new[] { typeof(PlanetLayerDef), typeof(Vector3?), typeof(float), typeof(float), typeof(float), typeof(int), typeof(float), typeof(float), typeof(Vector3?) })]
        [HarmonyPrefix]
        public static void RegisterPlanetLayer_Direct_Prefix(PlanetLayerDef def, ref int subdivisions)
        {
            if (def != PlanetLayerDefOf.Surface)
                return;

            if (!Planets_GameComponent.generatingWithRP)
            {
                EarthScaleConstants.SetForSubdivisions(subdivisions);
                return;
            }

            int subcount = Planets_GameComponent.subcount;
            subdivisions = subcount;

            UpdateEarthScaleConstantsForSubdivision(subcount);
        }

        private static void UpdateEarthScaleConstantsForSubdivision(int subdivisionCount)
        {
            EarthScaleConstants.SetForSubdivisions(subdivisionCount);
            if (subdivisionCount < 5 || subdivisionCount > 11)
                RPLog.W($"[WorldGrid] Unknown subdivision count: {subdivisionCount}. Using default.");

            float scale = (float)EarthScaleConstants.TILES_CIRCUMFERENCE / EarthScaleConstants.STANDARD_TILES_CIRCUMFERENCE;

            RPLog.D($"[WorldGrid] Updated TILES_CIRCUMFERENCE to {EarthScaleConstants.TILES_CIRCUMFERENCE} (subdivision {subdivisionCount})");
            RPLog.D($"[WorldGrid] Resolution Scale: {scale:F4} (1.0 = Earth Standard)");
            RPLog.D($"[WorldGrid] KM_PER_TILE = {EarthScaleConstants.KM_PER_TILE:F2} km");
        }
    }
}
