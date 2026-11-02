using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Patches.WorldGen;
using Planets.WorldGen.Climate;
using Planets.WorldGen.Constants;
using Planets.Utilities.Logging;

namespace Planets.Patches.WorldGen
{
    /// <summary>
    /// Draws rivers from RP2 drainage. Vanilla still runs when RP terrain did not, or when the flow map is missing.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenStep_Rivers), "GenerateRivers")]
    public static class Patch_DrainageRivers
    {
        private static List<SavedThresholds> saved;

        public static bool Prefix(WorldGenStep_Rivers __instance, PlanetLayer layer)
        {
            saved = null;
            if (layer == null || Find.WorldGrid?.Surface == null || layer != Find.WorldGrid.Surface)
                return true;
            if (!Patch_WorldGenStep_Terrain.rpTerrainSucceeded)
                return true;
            if (Controller.Settings?.hydrologyConfig?.CustomRiversEnabled?.CurrentValue == false)
                return true;

            ClimateData climate = Find.World?.GetComponent<ClimateData>();
            if (climate?.FlowRain == null)
                return true;

            float density = RiverDensity();
            if (density <= 0.01f)
                return false;

            try
            {
                saved = ScaleThresholds(density);
                Find.WorldPathGrid.RecalculateLayerPerceivedPathCosts(layer);
                DrawRivers(__instance, layer, climate);
                return false;
            }
            catch (Exception e)
            {
                RPLog.E($"[Rivers] RP2 drainage rivers failed: {e}");
                return false;
            }
        }

        public static void Finalizer()
        {
            if (saved == null)
                return;
            for (int i = 0; i < saved.Count; i++)
                saved[i].Restore();
            saved = null;
        }

        private static void DrawRivers(WorldGenStep_Rivers step, PlanetLayer layer, ClimateData climate)
        {
            int count = Mathf.Min(layer.Tiles.Count, climate.FlowRain.Length);
            var downstream = new int[count];
            var upstream = new List<PlanetTile>[count];
            var buffer = new List<PlanetTile>();
            bool lakes = Controller.Settings?.hydrologyConfig?.riversIntoLakes?.CurrentValue != false;

            for (int i = 0; i < count; i++)
            {
                downstream[i] = Next(i, layer, climate, buffer);
                if (downstream[i] < 0)
                    continue;
                if (upstream[downstream[i]] == null)
                    upstream[downstream[i]] = new List<PlanetTile>();
                upstream[downstream[i]].Add(new PlanetTile(i, layer));
            }

            var mouths = new List<(int mouth, int water, float flow)>();
            for (int i = 0; i < count; i++)
            {
                int water = downstream[i];
                if (water < 0 || water >= count)
                    continue;
                Tile land = layer.Tiles[i];
                if (land == null || land.biome == BiomeDefOf.Ocean || land.WaterCovered)
                    continue;
                bool ocean = climate.TrueOceanMask != null && water < climate.TrueOceanMask.Length && climate.TrueOceanMask[water];
                bool lake = lakes && layer.Tiles[water]?.biome == BiomeDefOf.Lake;
                if (!ocean && !lake)
                    continue;
                mouths.Add((i, water, climate.FlowRain[i]));
            }
            mouths.Sort((a, b) => b.flow.CompareTo(a.flow));

            for (int m = 0; m < mouths.Count; m++)
            {
                RiverDef def = RiverForFlow(mouths[m].flow);
                if (def == null)
                    continue;
                PlanetTile waterTile = new PlanetTile(mouths[m].water, layer);
                PlanetTile mouthTile = new PlanetTile(mouths[m].mouth, layer);
                Find.WorldGrid.OverlayRiver(waterTile, mouthTile, def);
                step.ExtendRiver(climate.FlowRain, upstream, mouthTile, def);
            }
        }

        private static int Next(int tileId, PlanetLayer layer, ClimateData climate, List<PlanetTile> buffer)
        {
            Tile tile = layer.Tiles[tileId];
            if (tile == null || tile.biome == BiomeDefOf.Ocean)
                return -1;
            bool tier2 = Controller.Settings?.featureConfig?.IsTier2HydrologyEnabled == true
                && climate.FlowDirectionHydrology != null && climate.SpillDirectionHydrology != null;
            int[] flowDirs = tier2 ? climate.FlowDirectionHydrology : climate.FlowDirectionTopology;
            int[] spillDirs = tier2 ? climate.SpillDirectionHydrology : climate.SpillDirectionTopology;
            int slot = -1;
            if (flowDirs != null && tileId < flowDirs.Length && flowDirs[tileId] >= 0)
                slot = flowDirs[tileId];
            else if (spillDirs != null && tileId < spillDirs.Length && spillDirs[tileId] >= 0)
                slot = spillDirs[tileId];
            if (slot < 0)
                return -1;
            buffer.Clear();
            Find.WorldGrid.GetTileNeighbors(new PlanetTile(tileId, layer), buffer);
            if (slot >= buffer.Count)
                return -1;
            return buffer[slot].tileId;
        }

        private static RiverDef RiverForFlow(float flow)
        {
            RiverDef best = null;
            foreach (RiverDef def in DefDatabase<RiverDef>.AllDefsListForReading)
            {
                if (def.spawnFlowThreshold <= 0 || def.spawnFlowThreshold > flow)
                    continue;
                if (best == null || def.spawnFlowThreshold > best.spawnFlowThreshold)
                    best = def;
            }
            return best;
        }

        private static float RiverDensity()
        {
            if (ModsConfig.IsActive("ferny.Worldbuilder"))
            {
                var dataType = AccessTools.TypeByName("Worldbuilder.World_ExposeData_Patch");
                var dataField = AccessTools.Field(dataType, "worldGenerationData");
                object data = dataField?.GetValue(null);
                var densityField = AccessTools.Field(data?.GetType(), "riverDensity");
                if (densityField?.GetValue(data) is float density)
                    return density;
            }
            return Planets_GameComponent.riverDensity;
        }

        private static List<SavedThresholds> ScaleThresholds(float density)
        {
            var list = new List<SavedThresholds>();
            foreach (RiverDef def in DefDatabase<RiverDef>.AllDefsListForReading)
            {
                var copy = new SavedThresholds(def);
                list.Add(copy);
                if (def.spawnFlowThreshold > 0)
                    def.spawnFlowThreshold = Mathf.Max(1, Mathf.RoundToInt(def.spawnFlowThreshold / density));
                def.degradeThreshold = Mathf.Max(0, Mathf.RoundToInt(def.degradeThreshold / density));
                if (def.branches == null)
                    continue;
                for (int i = 0; i < def.branches.Count; i++)
                    def.branches[i].minFlow = Mathf.Max(0, Mathf.RoundToInt(def.branches[i].minFlow / density));
            }
            return list;
        }

        private sealed class SavedThresholds
        {
            private readonly RiverDef def;
            private readonly int spawn;
            private readonly int degrade;
            private readonly int[] branchMin;

            public SavedThresholds(RiverDef def)
            {
                this.def = def;
                spawn = def.spawnFlowThreshold;
                degrade = def.degradeThreshold;
                if (def.branches == null)
                    return;
                branchMin = new int[def.branches.Count];
                for (int i = 0; i < def.branches.Count; i++)
                    branchMin[i] = def.branches[i].minFlow;
            }

            public void Restore()
            {
                def.spawnFlowThreshold = spawn;
                def.degradeThreshold = degrade;
                if (branchMin == null || def.branches == null)
                    return;
                int n = Mathf.Min(branchMin.Length, def.branches.Count);
                for (int i = 0; i < n; i++)
                    def.branches[i].minFlow = branchMin[i];
            }
        }
    }

    /// <summary>
    /// Fallback elevation bonus for vanilla river pathfinding if RP2 terrain generation failed and fell back to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenStep_Rivers), "GetImpliedElevation")]
    public static class Patch_GetImpliedElevation
    {
        public static bool Prefix(Tile tile, ref float __result)
        {
            var features = Controller.Settings?.featureConfig;
            if (features == null || !features.earthScaleHydrology.CurrentValue)
                return true;

            float bonus = tile.hilliness switch
            {
                Hilliness.SmallHills => EarthScaleConstants.SMALL_HILLS_ELEV_BONUS,
                Hilliness.LargeHills => EarthScaleConstants.LARGE_HILLS_ELEV_BONUS,
                Hilliness.Mountainous => EarthScaleConstants.MOUNTAINOUS_ELEV_BONUS,
                Hilliness.Impassable => EarthScaleConstants.IMPASSABLE_ELEV_BONUS,
                _ => 0f
            };

            __result = tile.elevation + bonus;
            return false;
        }
    }
}
