using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.WorldGen.Constants;
using Planets.WorldGen.Climate;
using Planets.Utilities.Logging;

namespace Planets.Patches.WorldGen
{
    /// <summary>
    /// When Tier 2 Hydrology and Odyssey are enabled, adds RP2-driven mutators to non-water land tiles:
    /// - Marshy/Wetland: at most one tile per connected wet patch (swampiness >= WETLAND_MIN_INTENSITY)
    /// - Single-tile lake mutators (Pond, Oasis, HotSprings) for filled single-tile basins (WaterBodyType == 3)
    /// Coastal/lakeshore mutators (Coast, Lakeshore) and river mutators (RiverDelta, RiverConfluence, Headwater)
    /// are handled by vanilla WorldGenStep_Mutators.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenStep_Mutators), "AddMutatorsFromTile")]
    public static class Patch_WorldGenStep_Mutators_AddMutatorsFromTile
    {
        public static void Postfix(PlanetLayer layer)
        {
            if (!ModsConfig.OdysseyActive) return;
            if (Controller.Settings?.featureConfig?.IsTier2HydrologyEnabled != true) return;

            var hydroConfig = Controller.Settings?.hydrologyConfig;
            if (hydroConfig == null) return;

            var world = Find.World;
            var climate = world?.GetComponent<ClimateData>();
            if (climate?.WaterBodyType == null || climate.Swampiness == null) return;
            if (climate.OceanMask == null) return;

            int tileCount = Mathf.Min(Find.WorldGrid.TilesCount, climate.WaterBodyType.Length);

            // Wetland and Marshy mutators: at most one tile per connected wet patch (the wettest tile whose biome accepts it)
            if (hydroConfig.WetlandMutatorsEnabled.CurrentValue)
            {
                AddWetlandMutatorsByConnectedPatch(layer, climate, tileCount);
            }
        }

        private static void AddWetlandMutatorsByConnectedPatch(PlanetLayer layer, ClimateData climate, int tileCount)
        {
            var wetlandDef = DefDatabase<TileMutatorDef>.GetNamedSilentFail("Wetland");
            var marshyDef = DefDatabase<TileMutatorDef>.GetNamedSilentFail("Marshy");
            if (wetlandDef == null && marshyDef == null)
                return;

            var visited = new bool[tileCount];
            var queue = new Queue<int>();
            var patch = new List<int>();
            var neighbors = new List<PlanetTile>(8);

            for (int start = 0; start < tileCount; start++)
            {
                if (visited[start] || climate.OceanMask[start])
                    continue;
                if (climate.Swampiness[start] < EarthScaleConstants.WETLAND_MIN_INTENSITY)
                    continue;
                Tile startTile = layer.Tiles[start];
                if (startTile == null || startTile.WaterCovered)
                    continue;

                patch.Clear();
                visited[start] = true;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    patch.Add(cur);
                    neighbors.Clear();
                    layer.GetTileNeighbors(cur, neighbors);
                    for (int k = 0; k < neighbors.Count; k++)
                    {
                        int nid = neighbors[k].tileId;
                        if (nid < 0 || nid >= tileCount || visited[nid] || climate.OceanMask[nid])
                            continue;
                        if (climate.Swampiness[nid] < EarthScaleConstants.WETLAND_MIN_INTENSITY)
                            continue;
                        Tile nt = layer.Tiles[nid];
                        if (nt == null || nt.WaterCovered)
                            continue;
                        visited[nid] = true;
                        queue.Enqueue(nid);
                    }
                }

                patch.Sort((a, b) => climate.Swampiness[b].CompareTo(climate.Swampiness[a]));
                for (int idx = 0; idx < patch.Count; idx++)
                {
                    int tid = patch[idx];
                    Tile t = layer.Tiles[tid];
                    float sw = climate.Swampiness[tid];
                    TileMutatorDef chosen = (sw >= EarthScaleConstants.WETLAND_CLUSTER_INTENSITY && wetlandDef != null)
                        ? wetlandDef
                        : marshyDef;
                    if (chosen != null && TryAddMutator(t, layer, chosen))
                        break;
                }
            }
        }

        /// <summary>Replicate vanilla TryAddMutator logic (method is private), ensuring water tiles never receive mutators.</summary>
        private static bool TryAddMutator(Tile tile, PlanetLayer layer, TileMutatorDef mutator)
        {
            if (tile == null || tile.WaterCovered || tile.biome == BiomeDefOf.Ocean || tile.biome == BiomeDefOf.Lake || tile.biome == BiomeDefOf.SeaIce)
                return false;
            if (tile.Mutators.Contains(mutator)) return false;
            if (!mutator.IsValidTile(tile.tile, layer)) return false;
            try
            {
                tile.AddMutator(mutator);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"RP2 mutator: Error adding {mutator?.defName} to tile {tile?.tile.tileId}: {ex.Message}");
                return false;
            }
        }
    }
}
