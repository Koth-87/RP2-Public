using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;

namespace Planets.Patches.WorldGen
{
    /// <summary>
    /// Smaller planet scales have fewer tiles, so vanilla's per-100k settlement roll drops to zero.
    /// Scale the surface layer's range for this call, then put the original range back.
    /// </summary>
    [HarmonyPatch(typeof(FactionGenerator), nameof(FactionGenerator.GenerateFactionsIntoWorldLayer))]
    [HarmonyPriority(Priority.Low)]
    public static class Patch_FactionGenerator_SettlementDensity
    {
        private static bool applied;
        private static FloatRange saved;

        [HarmonyPrefix]
        public static void Prefix(PlanetLayer layer)
        {
            applied = false;
            if (layer?.Def == null || layer.Def != PlanetLayerDefOf.Surface)
                return;
            if (Controller.Settings?.featureConfig?.compensateSettlementDensity?.CurrentValue == false)
                return;

            float factor = Mathf.Clamp(Mathf.Pow(3f, 10f - Planets_GameComponent.ActiveSubcount), 1f, 27f);
            FloatRange original = layer.Def.settlementsPer100kTiles;
            FloatRange scaled = new FloatRange(original.min * factor, original.max * factor);
            scaled = CapToLand(layer, scaled);

            saved = original;
            layer.Def.settlementsPer100kTiles = scaled;
            applied = true;
        }

        [HarmonyFinalizer]
        public static void Finalizer(PlanetLayer layer)
        {
            if (!applied || layer?.Def == null)
                return;
            layer.Def.settlementsPer100kTiles = saved;
            applied = false;
        }

        private static FloatRange CapToLand(PlanetLayer layer, FloatRange scaled)
        {
            int land = 0;
            var tiles = layer.Tiles;
            if (tiles != null)
            {
                for (int i = 0; i < tiles.Count; i++)
                {
                    if (tiles[i] != null && !tiles[i].WaterCovered)
                        land++;
                }
            }
            if (land <= 0 || Find.World?.info == null || scaled.max <= 0f)
                return scaled;

            float view = 1f;
            if (layer.Def.viewAngleSettlementsFactorCurve != null)
                view = layer.Def.viewAngleSettlementsFactorCurve.Evaluate(Mathf.Clamp01(layer.ViewAngle / 180f));
            float population = Find.World.info.overallPopulation.GetScaleFactor();
            float expectedMax = layer.TilesCount / 100000f * scaled.max * population * view;
            float cap = 0.08f * land;
            if (expectedMax <= cap)
                return scaled;

            float shrink = cap / expectedMax;
            return new FloatRange(scaled.min * shrink, scaled.max * shrink);
        }
    }
}
