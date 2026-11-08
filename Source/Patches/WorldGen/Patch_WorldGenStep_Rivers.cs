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
                DrawRivers(layer, climate);
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

        private static void DrawRivers(PlanetLayer layer, ClimateData climate)
        {
            new DrainageDraw(layer, climate).Run();
        }

        /// <summary>
        /// Walks upstream from ocean mouths, and from lakes that never join a drawn river.
        /// A lake tile's stored flow is the whole inflow, so a segment is never sized from it.
        /// The tile below a spill keeps the leftover that actually came over the outlet.
        /// </summary>
        private sealed class DrainageDraw
        {
            private readonly PlanetLayer layer;
            private readonly ClimateData climate;
            private readonly int count;
            private readonly bool tier2;
            private readonly bool lakeMouths;
            private readonly int minRiverLengthTiles;
            private readonly int[] lakeIds;
            private readonly int[] flowDirs;
            private readonly int[] spillDirs;
            private readonly int[] downstream;
            private readonly List<int>[] upstream;
            private readonly bool[] visited;
            private readonly Dictionary<int, Lake> lakes = new Dictionary<int, Lake>();
            private readonly Dictionary<int, List<Lake>> spillsAt = new Dictionary<int, List<Lake>>();
            private readonly HashSet<int> joined = new HashSet<int>();
            private readonly List<PlanetTile> buffer = new List<PlanetTile>();
            private readonly List<(int from, int to, RiverDef def)> pendingSegments = new List<(int, int, RiverDef)>();
            private readonly HashSet<int> pendingLandTiles = new HashSet<int>();

            public DrainageDraw(PlanetLayer layer, ClimateData climate)
            {
                this.layer = layer;
                this.climate = climate;
                count = Mathf.Min(layer.Tiles.Count, climate.FlowRain.Length);
                tier2 = Controller.Settings?.featureConfig?.IsTier2HydrologyEnabled == true
                    && climate.FlowDirectionHydrology != null && climate.SpillDirectionHydrology != null;
                lakeMouths = Controller.Settings?.hydrologyConfig?.riversIntoLakes?.CurrentValue != false;
                minRiverLengthTiles = Mathf.Clamp(Controller.Settings?.hydrologyConfig?.minRiverLengthTiles?.CurrentValue ?? 3, 1, 10);
                if (tier2)
                {
                    lakeIds = climate.MergedLakeIDHydrology;
                    flowDirs = climate.FlowDirectionHydrology;
                    spillDirs = climate.SpillDirectionHydrology;
                }
                else
                {
                    lakeIds = climate.MergedLakeIDTopology;
                    flowDirs = climate.FlowDirectionTopology;
                    spillDirs = climate.SpillDirectionTopology;
                }
                downstream = new int[count];
                upstream = new List<int>[count];
                visited = new bool[count];
            }

            public void Run()
            {
                BuildGraph();
                MeasureSpills();
                var ocean = new List<(int mouth, int water, float flow)>();
                var shores = new List<(int mouth, int water, float flow)>();
                CollectMouths(ocean, shores);
                DrawMouths(ocean, false);
                DrawSpillsIntoStillLakes();
                DrawMouths(shores, true);
            }

            private void BuildGraph()
            {
                for (int i = 0; i < count; i++)
                    downstream[i] = Next(i);

                for (int i = 0; i < count; i++)
                {
                    int spill = SpillSlot(i);
                    int lake = LakeAt(i);
                    if (spill >= 0 && lake >= 0)
                    {
                        int target = Neighbor(i, spill);
                        if (target >= 0 && target != i && LakeAt(target) != lake)
                        {
                            Lake course = Course(lake);
                            course.exit = i;
                            course.target = target;
                        }
                    }

                    if (lake >= 0)
                        continue;
                    int next = downstream[i];
                    if (next < 0)
                        continue;
                    int host = LakeAt(next);
                    if (host >= 0)
                    {
                        Course(host).inlets.Add(i);
                        continue;
                    }
                    if (upstream[next] == null)
                        upstream[next] = new List<int>();
                    upstream[next].Add(i);
                }
            }

            private void MeasureSpills()
            {
                var landGroups = new Dictionary<int, List<Lake>>();
                foreach (Lake course in lakes.Values)
                {
                    if (course.exit < 0 || course.target < 0)
                        continue;
                    int host = LakeAt(course.target);
                    if (host >= 0)
                        Course(host).feeds.Add(course);
                    else
                    {
                        if (!landGroups.TryGetValue(course.target, out List<Lake> group))
                        {
                            group = new List<Lake>();
                            landGroups[course.target] = group;
                        }
                        group.Add(course);
                    }
                }

                var stack = new HashSet<int>();
                foreach (Lake course in lakes.Values)
                    InletSum(course, stack);

                foreach (Lake course in lakes.Values)
                    course.feeds.Sort((a, b) => a.id.CompareTo(b.id));

                foreach (KeyValuePair<int, List<Lake>> pair in landGroups)
                {
                    List<Lake> group = pair.Value;
                    group.Sort((a, b) => a.id.CompareTo(b.id));
                    float share = tier2 ? LandSpill(pair.Key) : 0f;
                    float total = 0f;
                    for (int i = 0; i < group.Count; i++)
                        total += group[i].inletSum;
                    for (int i = 0; i < group.Count; i++)
                    {
                        Lake course = group[i];
                        course.delivered = tier2
                            ? (total > 0f ? share * (course.inletSum / total) : 0f)
                            : course.inletSum;
                        if (course.delivered < 1f)
                            course.delivered = 0f;
                    }
                    spillsAt[pair.Key] = group;
                }

                stack.Clear();
                foreach (Lake course in lakes.Values)
                {
                    if (course.target < 0 || LakeAt(course.target) < 0)
                        continue;
                    // No land between these two lakes, so the continuing stream is the
                    // largest feeder. The sum would let a fan of creeks outrank a real river.
                    course.delivered = MaxFeeder(course, stack);
                    if (course.delivered < 1f)
                        course.delivered = 0f;
                }
            }

            private float LandSpill(int tile)
            {
                float children = 0f;
                List<int> kids = upstream[tile];
                if (kids != null)
                {
                    for (int i = 0; i < kids.Count; i++)
                        children += Flow(kids[i]);
                }
                float residual = Flow(tile) - children - LocalRunoff(tile);
                return residual > 0f ? residual : 0f;
            }

            private float LocalRunoff(int tile)
            {
                float rain = 0f;
                if (climate.AdvectedCondensationMm != null && tile < climate.AdvectedCondensationMm.Length)
                    rain = climate.AdvectedCondensationMm[tile];
                else if (climate.AnnualRainfall != null && tile < climate.AnnualRainfall.Length)
                    rain = climate.AnnualRainfall[tile];
                float evap = climate.LandEvaporationMm != null && tile < climate.LandEvaporationMm.Length
                    ? climate.LandEvaporationMm[tile] : 0f;
                float runoff = Mathf.Max(0f, rain - evap);
                float scale = EarthScaleConstants.TILE_AREA_KM2 / Mathf.Max(1f, EarthScaleConstants.TILE_AREA_KM2_AT_SCALE_10);
                return runoff * scale;
            }

            private float InletSum(Lake course, HashSet<int> stack)
            {
                if (course.sumReady)
                    return course.inletSum;
                if (!stack.Add(course.id))
                    return 0f;
                float sum = 0f;
                for (int i = 0; i < course.inlets.Count; i++)
                    sum += Flow(course.inlets[i]);
                for (int i = 0; i < course.feeds.Count; i++)
                    sum += InletSum(course.feeds[i], stack);
                stack.Remove(course.id);
                course.inletSum = sum;
                course.sumReady = true;
                return sum;
            }

            private float MaxFeeder(Lake course, HashSet<int> stack)
            {
                if (course.maxReady)
                    return course.maxFeeder;
                if (!stack.Add(course.id))
                    return 0f;
                float max = 0f;
                for (int i = 0; i < course.inlets.Count; i++)
                {
                    float flow = Flow(course.inlets[i]);
                    if (flow > max)
                        max = flow;
                }
                for (int i = 0; i < course.feeds.Count; i++)
                {
                    float flow = MaxFeeder(course.feeds[i], stack);
                    if (flow > max)
                        max = flow;
                }
                stack.Remove(course.id);
                course.maxFeeder = max;
                course.maxReady = true;
                return max;
            }

            private void CollectMouths(List<(int mouth, int water, float flow)> ocean, List<(int mouth, int water, float flow)> shores)
            {
                for (int i = 0; i < count; i++)
                {
                    int water = downstream[i];
                    if (water < 0 || water >= count || LakeAt(i) >= 0)
                        continue;
                    Tile land = layer.Tiles[i];
                    if (land == null || land.biome == BiomeDefOf.Ocean || land.WaterCovered)
                        continue;
                    float flow = Flow(i);
                    bool sea = climate.TrueOceanMask != null && water < climate.TrueOceanMask.Length && climate.TrueOceanMask[water];
                    if (sea)
                    {
                        ocean.Add((i, water, flow));
                        continue;
                    }
                    if (lakeMouths && layer.Tiles[water]?.biome == BiomeDefOf.Lake)
                        shores.Add((i, water, flow));
                }
                ocean.Sort(ByFlow);
                shores.Sort(ByFlow);
            }

            private void DrawMouths(List<(int mouth, int water, float flow)> mouths, bool shores)
            {
                for (int m = 0; m < mouths.Count; m++)
                {
                    int mouth = mouths[m].mouth;
                    int water = mouths[m].water;
                    if (visited[mouth])
                        continue;
                    if (shores)
                    {
                        int host = LakeAt(water);
                        if (host >= 0 && joined.Contains(host))
                            continue;
                    }
                    RiverDef def = RiverForFlow(mouths[m].flow);
                    if (def == null)
                        continue;
                    pendingSegments.Clear();
                    pendingLandTiles.Clear();
                    RecordSegment(water, mouth, def);
                    Extend(mouth, def);
                    CommitOrDiscardPending();
                }
            }

            private void DrawSpillsIntoStillLakes()
            {
                if (!lakeMouths)
                    return;
                var pending = new List<Lake>();
                foreach (Lake course in lakes.Values)
                {
                    if (course.exit < 0 || course.delivered <= 0f || joined.Contains(course.id))
                        continue;
                    int host = LakeAt(course.target);
                    if (host < 0 || joined.Contains(host))
                        continue;
                    if (layer.Tiles[course.target]?.biome != BiomeDefOf.Lake)
                        continue;
                    pending.Add(course);
                }
                pending.Sort((a, b) =>
                {
                    int byFlow = b.delivered.CompareTo(a.delivered);
                    return byFlow != 0 ? byFlow : a.id.CompareTo(b.id);
                });
                for (int i = 0; i < pending.Count; i++)
                {
                    Lake course = pending[i];
                    if (joined.Contains(course.id))
                        continue;
                    RiverDef def = RiverForFlow(course.delivered);
                    if (def == null)
                        continue;
                    joined.Add(course.id);
                    pendingSegments.Clear();
                    pendingLandTiles.Clear();
                    FollowLake(course, def);
                    CommitOrDiscardPending();
                }
            }

            private void Extend(int tile, RiverDef incoming)
            {
                if (tile < 0 || tile >= count || visited[tile] || incoming == null)
                    return;
                visited[tile] = true;

                int bestTile = -1;
                float bestFlow = -1f;
                List<int> children = upstream[tile];
                if (children != null)
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        int child = children[i];
                        if (visited[child])
                            continue;
                        float flow = Flow(child);
                        if (flow > bestFlow || (flow == bestFlow && (bestTile < 0 || child < bestTile)))
                        {
                            bestFlow = flow;
                            bestTile = child;
                        }
                    }
                }

                Lake bestLake = null;
                float bestLakeFlow = -1f;
                spillsAt.TryGetValue(tile, out List<Lake> here);
                if (here != null)
                {
                    for (int i = 0; i < here.Count; i++)
                    {
                        Lake lake = here[i];
                        if (lake.delivered <= 0f || joined.Contains(lake.id))
                            continue;
                        if (lake.delivered > bestLakeFlow || (lake.delivered == bestLakeFlow && (bestLake == null || lake.id < bestLake.id)))
                        {
                            bestLakeFlow = lake.delivered;
                            bestLake = lake;
                        }
                    }
                }

                bool lakeWins = bestLake != null && bestLakeFlow > bestFlow;
                if (lakeWins)
                {
                    RiverDef stem = Degrade(incoming, bestLake.delivered);
                    if (stem != null)
                    {
                        joined.Add(bestLake.id);
                        DrawNeighbor(tile, bestLake.exit, stem);
                        FollowLake(bestLake, stem);
                    }
                    else
                        lakeWins = false;
                }
                if (!lakeWins && bestTile >= 0)
                {
                    RiverDef stem = Degrade(incoming, bestFlow);
                    if (stem != null)
                    {
                        DrawNeighbor(tile, bestTile, stem);
                        Extend(bestTile, stem);
                    }
                }

                if (incoming.branches == null)
                    return;
                if (children != null)
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        int child = children[i];
                        if (child == bestTile && !lakeWins)
                            continue;
                        if (visited[child])
                            continue;
                        RiverDef.Branch branch = PickBranch(incoming, Flow(child));
                        if (branch == null || Rand.Value >= branch.chance)
                            continue;
                        DrawNeighbor(tile, child, branch.child);
                        Extend(child, branch.child);
                    }
                }
                if (here != null)
                {
                    for (int i = 0; i < here.Count; i++)
                    {
                        Lake lake = here[i];
                        if (lakeWins && lake == bestLake)
                            continue;
                        if (lake.delivered <= 0f || joined.Contains(lake.id))
                            continue;
                        RiverDef.Branch branch = PickBranch(incoming, lake.delivered);
                        if (branch == null || Rand.Value >= branch.chance)
                            continue;
                        joined.Add(lake.id);
                        DrawNeighbor(tile, lake.exit, branch.child);
                        FollowLake(lake, branch.child);
                    }
                }
            }

            private void FollowLake(Lake lake, RiverDef incoming)
            {
                if (incoming == null)
                    return;

                bool drawAcrossLandBasin = lake.exit >= 0 && !IsLakeBiome(lake.exit);

                int bestInlet = -1;
                float bestFlow = -1f;
                for (int i = 0; i < lake.inlets.Count; i++)
                {
                    int inlet = lake.inlets[i];
                    if (inlet < 0 || inlet >= count || visited[inlet])
                        continue;
                    float flow = Flow(inlet);
                    if (flow > bestFlow || (flow == bestFlow && (bestInlet < 0 || inlet < bestInlet)))
                    {
                        bestFlow = flow;
                        bestInlet = inlet;
                    }
                }

                Lake bestUpper = null;
                float bestUpperFlow = -1f;
                for (int i = 0; i < lake.feeds.Count; i++)
                {
                    Lake upper = lake.feeds[i];
                    if (upper.delivered <= 0f || joined.Contains(upper.id))
                        continue;
                    if (upper.delivered > bestUpperFlow || (upper.delivered == bestUpperFlow && (bestUpper == null || upper.id < bestUpper.id)))
                    {
                        bestUpperFlow = upper.delivered;
                        bestUpper = upper;
                    }
                }

                bool upperWins = bestUpper != null && bestUpperFlow > bestFlow;
                if (upperWins)
                {
                    RiverDef stem = Degrade(incoming, bestUpper.delivered);
                    if (stem != null)
                    {
                        joined.Add(bestUpper.id);
                        if (drawAcrossLandBasin && bestUpper.target >= 0 && bestUpper.exit >= 0)
                        {
                            DrawBasinPath(lake.id, lake.exit, bestUpper.target, stem);
                            DrawNeighbor(bestUpper.target, bestUpper.exit, stem);
                        }
                        FollowLake(bestUpper, stem);
                    }
                    else
                        upperWins = false;
                }
                if (!upperWins && bestInlet >= 0)
                {
                    RiverDef stem = Degrade(incoming, bestFlow);
                    if (stem != null)
                    {
                        if (drawAcrossLandBasin)
                            DrawBasinPath(lake.id, lake.exit, downstream[bestInlet], stem);
                        DrawNeighbor(downstream[bestInlet], bestInlet, stem);
                        Extend(bestInlet, stem);
                    }
                }

                if (incoming.branches == null)
                    return;
                for (int i = 0; i < lake.inlets.Count; i++)
                {
                    int inlet = lake.inlets[i];
                    if (inlet == bestInlet && !upperWins)
                        continue;
                    if (inlet < 0 || inlet >= count || visited[inlet])
                        continue;
                    RiverDef.Branch branch = PickBranch(incoming, Flow(inlet));
                    if (branch == null || Rand.Value >= branch.chance)
                        continue;
                    if (drawAcrossLandBasin)
                        DrawBasinPath(lake.id, lake.exit, downstream[inlet], branch.child);
                    DrawNeighbor(downstream[inlet], inlet, branch.child);
                    Extend(inlet, branch.child);
                }
                for (int i = 0; i < lake.feeds.Count; i++)
                {
                    Lake upper = lake.feeds[i];
                    if (upperWins && upper == bestUpper)
                        continue;
                    if (upper.delivered <= 0f || joined.Contains(upper.id))
                        continue;
                    RiverDef.Branch branch = PickBranch(incoming, upper.delivered);
                    if (branch == null || Rand.Value >= branch.chance)
                        continue;
                    joined.Add(upper.id);
                    if (drawAcrossLandBasin && upper.target >= 0 && upper.exit >= 0)
                    {
                        DrawBasinPath(lake.id, lake.exit, upper.target, branch.child);
                        DrawNeighbor(upper.target, upper.exit, branch.child);
                    }
                    FollowLake(upper, branch.child);
                }
            }

            private bool IsLakeBiome(int tileId)
            {
                if (tileId < 0 || tileId >= count)
                    return false;
                Tile t = layer.Tiles[tileId];
                return t != null && t.biome == BiomeDefOf.Lake;
            }

            /// <summary>
            /// When lake conversion is switched off so a flooded basin remains land, connect the river
            /// continuously across the basin's land tiles from <paramref name="startTile"/> to <paramref name="endTile"/>.
            /// </summary>
            private void DrawBasinPath(int lakeId, int startTile, int endTile, RiverDef def)
            {
                if (def == null || startTile < 0 || endTile < 0 || startTile == endTile)
                    return;

                var q = new Queue<int>();
                var parent = new Dictionary<int, int>();
                q.Enqueue(startTile);
                parent[startTile] = -1;

                bool found = false;
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    if (cur == endTile)
                    {
                        found = true;
                        break;
                    }
                    buffer.Clear();
                    Find.WorldGrid.GetTileNeighbors(new PlanetTile(cur, layer), buffer);
                    for (int k = 0; k < buffer.Count; k++)
                    {
                        int nId = buffer[k].tileId;
                        if (nId < 0 || nId >= count || parent.ContainsKey(nId))
                            continue;
                        if (LakeAt(nId) != lakeId || IsLakeBiome(nId))
                            continue;
                        parent[nId] = cur;
                        q.Enqueue(nId);
                    }
                }

                if (!found)
                    return;

                var path = new List<int>();
                for (int step = endTile; step >= 0; step = parent.TryGetValue(step, out int p) ? p : -1)
                    path.Add(step);

                for (int idx = path.Count - 1; idx > 0; idx--)
                    DrawNeighbor(path[idx], path[idx - 1], def);
            }

            private void DrawNeighbor(int from, int to, RiverDef def)
            {
                if (def == null || from < 0 || to < 0 || from >= count || to >= count)
                    return;
                PlanetTile a = new PlanetTile(from, layer);
                PlanetTile b = new PlanetTile(to, layer);
                if (!Find.WorldGrid.IsNeighbor(a, b))
                    return;
                RecordSegment(from, to, def);
            }

            private void RecordSegment(int from, int to, RiverDef def)
            {
                pendingSegments.Add((from, to, def));
                if (IsCountableLandTile(from))
                    pendingLandTiles.Add(from);
                if (IsCountableLandTile(to))
                    pendingLandTiles.Add(to);
            }

            private bool IsCountableLandTile(int tileId)
            {
                if (tileId < 0 || tileId >= count)
                    return false;
                Tile t = layer.Tiles[tileId];
                if (t == null || t.WaterCovered || t.biome == BiomeDefOf.Ocean || t.biome == BiomeDefOf.Lake)
                    return false;
                return true;
            }

            private void CommitOrDiscardPending()
            {
                if (pendingLandTiles.Count >= minRiverLengthTiles)
                {
                    for (int i = 0; i < pendingSegments.Count; i++)
                    {
                        var seg = pendingSegments[i];
                        Find.WorldGrid.OverlayRiver(new PlanetTile(seg.from, layer), new PlanetTile(seg.to, layer), seg.def);
                    }
                }
                pendingSegments.Clear();
                pendingLandTiles.Clear();
            }

            private int Next(int tileId)
            {
                Tile tile = layer.Tiles[tileId];
                if (tile == null || tile.biome == BiomeDefOf.Ocean)
                    return -1;
                int slot = -1;
                if (flowDirs != null && tileId < flowDirs.Length && flowDirs[tileId] >= 0)
                    slot = flowDirs[tileId];
                else
                    slot = SpillSlot(tileId);
                if (slot < 0)
                    return -1;
                return Neighbor(tileId, slot);
            }

            private int SpillSlot(int tileId)
            {
                if (spillDirs == null || tileId < 0 || tileId >= spillDirs.Length)
                    return -1;
                return spillDirs[tileId];
            }

            private int Neighbor(int tileId, int slot)
            {
                buffer.Clear();
                Find.WorldGrid.GetTileNeighbors(new PlanetTile(tileId, layer), buffer);
                if (slot < 0 || slot >= buffer.Count)
                    return -1;
                return buffer[slot].tileId;
            }

            private int LakeAt(int tile)
            {
                if (lakeIds == null || tile < 0 || tile >= lakeIds.Length)
                    return -1;
                return lakeIds[tile];
            }

            private float Flow(int tile)
            {
                if (tile < 0 || tile >= climate.FlowRain.Length)
                    return 0f;
                return climate.FlowRain[tile];
            }

            private Lake Course(int id)
            {
                if (!lakes.TryGetValue(id, out Lake course))
                {
                    course = new Lake(id);
                    lakes[id] = course;
                }
                return course;
            }

            private static int ByFlow((int mouth, int water, float flow) a, (int mouth, int water, float flow) b)
            {
                int byFlow = b.flow.CompareTo(a.flow);
                return byFlow != 0 ? byFlow : a.mouth.CompareTo(b.mouth);
            }

            private static RiverDef Degrade(RiverDef def, float flow)
            {
                while (def != null && def.degradeThreshold > flow)
                    def = def.degradeChild;
                return def;
            }

            private static RiverDef.Branch PickBranch(RiverDef river, float flow)
            {
                if (river?.branches == null)
                    return null;
                RiverDef.Branch best = null;
                for (int i = 0; i < river.branches.Count; i++)
                {
                    RiverDef.Branch branch = river.branches[i];
                    if (branch?.child == null || branch.minFlow > flow)
                        continue;
                    if (best == null || branch.minFlow > best.minFlow)
                        best = branch;
                }
                return best;
            }

            private sealed class Lake
            {
                public readonly int id;
                public int exit = -1;
                public int target = -1;
                public readonly List<int> inlets = new List<int>();
                public readonly List<Lake> feeds = new List<Lake>();
                public float inletSum;
                public float maxFeeder;
                public float delivered;
                public bool sumReady;
                public bool maxReady;

                public Lake(int id)
                {
                    this.id = id;
                }
            }
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

        // Vanilla's lines were written for a sum of each tile's full rainfall. RP2 mouths
        // are leftover runoff after evaporation, stitched across spilling basins, so a 3.5x
        // divisor aligns multi-basin runoff with vanilla's river size thresholds.
        private const float FlowUnitDivisor = 3.5f;

        private static List<SavedThresholds> ScaleThresholds(float density)
        {
            float scale = Mathf.Max(density, 0.01f) * FlowUnitDivisor;
            var list = new List<SavedThresholds>();
            foreach (RiverDef def in DefDatabase<RiverDef>.AllDefsListForReading)
            {
                var copy = new SavedThresholds(def);
                list.Add(copy);
                if (def.spawnFlowThreshold > 0)
                    def.spawnFlowThreshold = Mathf.Max(1, Mathf.RoundToInt(def.spawnFlowThreshold / scale));
                def.degradeThreshold = Mathf.Max(0, Mathf.RoundToInt(def.degradeThreshold / scale));
                if (def.branches == null)
                    continue;
                for (int i = 0; i < def.branches.Count; i++)
                    def.branches[i].minFlow = Mathf.Max(0, Mathf.RoundToInt(def.branches[i].minFlow / scale));
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
