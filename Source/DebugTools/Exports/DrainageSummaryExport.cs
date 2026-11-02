using System.Collections;
using System.Collections.Generic;
using System.IO;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate;
using Planets.WorldGen.Constants;

namespace Planets.DebugTools.Exports
{
    /// <summary>
    /// One row per topographic river system and one row per connected ridge.
    /// The river systems are the fill-and-spill run with no evaporation.
    /// </summary>
    public static class DrainageSummaryExport
    {
        [DebugAction(DebugActionBase.CATEGORY_EXPORTS, "Export Drainage Summary", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void Export()
        {
            if (!Climate.IsInitialized())
            {
                Messages.Message("Climate system not initialized - generate a world first", MessageTypeDefOf.RejectInput);
                return;
            }

            LongEventHandler.QueueLongEvent(Write(), "Exporting drainage", exception => RPLog.E($"Drainage export failed: {exception}"));
        }

        private static IEnumerable Write()
        {
            ClimateData climate = Find.World?.GetComponent<ClimateData>();
            int count = Find.WorldGrid.TilesCount;
            if (climate?.RiverSystemIDTopology == null || climate.FlowDirectionTopology == null)
            {
                Messages.Message("Drainage has not been computed for this world", MessageTypeDefOf.RejectInput);
                yield break;
            }

            var systems = new Dictionary<int, SystemRow>();
            var hydroSystems = new Dictionary<int, SystemRow>();
            bool hasHydro = climate.RiverSystemIDHydrology != null && climate.RiverSystemIDHydrology.Length >= count
                && climate.FlowDirectionHydrology != null && climate.FlowDirectionHydrology.Length >= count;
            var neighbors = new List<PlanetTile>();
            for (int i = 0; i < count; i++)
            {
                if (climate.OceanMask != null && i < climate.OceanMask.Length && climate.OceanMask[i])
                    continue;
                float elev = Elevation(climate, i);
                int system = climate.RiverSystemIDTopology[i];
                if (system >= 0)
                {
                    if (!systems.TryGetValue(system, out SystemRow row))
                    {
                        row = new SystemRow();
                        systems[system] = row;
                    }
                    row.Count++;
                    if (elev < row.MinElev)
                        row.MinElev = elev;
                    if (elev > row.MaxElev)
                        row.MaxElev = elev;

                    int down = Downstream(climate, i, neighbors, tier2: false);
                    string kind = OutletKind(climate, system, down, tier2: false);
                    if (kind != null)
                    {
                        row.OutletCount++;
                        float flow = Flow(climate, i, tier2: false);
                        if (flow > row.OutletFlow)
                        {
                            row.OutletFlow = flow;
                            row.OutletTile = i;
                            row.OutletKind = kind;
                        }
                    }
                }

                if (hasHydro)
                {
                    int hSystem = climate.RiverSystemIDHydrology[i];
                    if (hSystem >= 0)
                    {
                        if (!hydroSystems.TryGetValue(hSystem, out SystemRow hRow))
                        {
                            hRow = new SystemRow();
                            hydroSystems[hSystem] = hRow;
                        }
                        hRow.Count++;
                        if (elev < hRow.MinElev)
                            hRow.MinElev = elev;
                        if (elev > hRow.MaxElev)
                            hRow.MaxElev = elev;

                        int hDown = Downstream(climate, i, neighbors, tier2: true);
                        string hKind = OutletKind(climate, hSystem, hDown, tier2: true);
                        if (hKind != null)
                        {
                            hRow.OutletCount++;
                            float hFlow = Flow(climate, i, tier2: true);
                            if (hFlow > hRow.OutletFlow)
                            {
                                hRow.OutletFlow = hFlow;
                                hRow.OutletTile = i;
                                hRow.OutletKind = hKind;
                            }
                        }
                    }
                }

                if (i % 4000 == 0)
                {
                    LongEventHandler.SetCurrentEventText($"Reading drainage {i}/{count}");
                    yield return null;
                }
            }

            var component = new int[count];
            for (int i = 0; i < count; i++)
                component[i] = -1;
            var ridges = new List<RidgeRow>();
            var queue = new Queue<int>();
            int nextComponent = 0;
            for (int i = 0; i < count; i++)
            {
                if (!IsRidge(climate, i) || component[i] >= 0)
                    continue;
                var ridge = new RidgeRow { Id = nextComponent };
                ridges.Add(ridge);
                component[i] = nextComponent;
                queue.Enqueue(i);
                bool haveFacing = false;
                float refEast = 0f;
                float refNorth = 0f;
                while (queue.Count > 0)
                {
                    int tile = queue.Dequeue();
                    ridge.Count++;
                    ridge.ElevSum += Elevation(climate, tile);
                    Facing(climate, tile, out float east, out float north);
                    if (!haveFacing && east * east + north * north > 1e-6f)
                    {
                        refEast = east;
                        refNorth = north;
                        haveFacing = true;
                    }
                    if (haveFacing && east * refEast + north * refNorth < 0f)
                    {
                        east = -east;
                        north = -north;
                    }
                    ridge.EastSum += east;
                    ridge.NorthSum += north;

                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                    for (int n = 0; n < neighbors.Count; n++)
                    {
                        int next = neighbors[n].tileId;
                        if (next < 0 || next >= count || component[next] >= 0 || !IsRidge(climate, next))
                            continue;
                        component[next] = nextComponent;
                        queue.Enqueue(next);
                    }
                }
                nextComponent++;
                if (i % 4000 == 0)
                {
                    LongEventHandler.SetCurrentEventText($"Reading ridges {i}/{count}");
                    yield return null;
                }
            }

            string filename = ExportWriter.GetFilename("DrainageSummary", "csv");
            StreamWriter writer = ExportWriter.OpenTemp(filename, out string tempPath, out string finalPath);
            try
            {
                writer.WriteLine("# Drainage summary. One row per topographic river system, then one row per connected ridge.");
                writer.WriteLine($"# WorldSeed: {Find.World.info.seedString}");
                writer.WriteLine($"# TileAreaKm2: {EarthScaleConstants.TILE_AREA_KM2:F1}");
                writer.WriteLine("# River systems are the fill-and-spill with no evaporation. Closed basins are forced over their lowest sill.");
                writer.WriteLine("# OutletKind: ocean (true ocean only), lake (inland water, including a lake biome or a below-sea body that is not the world ocean), spill (into another system), or terminal (no downhill neighbor).");
                writer.WriteLine("# The outlet row is the exiting tile with the most upstream area. OutletCount is every exiting tile.");
                writer.WriteLine("# RidgeEast and RidgeNorth are the mean across-ridge direction. Opposing normals were flipped before the average. The sign is arbitrary.");
                writer.WriteLine("Kind,Id,TileCount,AreaKm2,MinElevM,MaxElevM,MeanElevM,OutletLat,OutletLon,OutletKind,OutletCount,RidgeEast,RidgeNorth");

                var systemIds = new List<int>(systems.Keys);
                systemIds.Sort();
                float area = EarthScaleConstants.TILE_AREA_KM2;
                for (int s = 0; s < systemIds.Count; s++)
                {
                    int id = systemIds[s];
                    SystemRow row = systems[id];
                    string lat = "";
                    string lon = "";
                    if (row.OutletTile >= 0)
                    {
                        Vector3 coords = Find.WorldGrid.LongLatOf(row.OutletTile);
                        lat = coords.y.ToString("F4");
                        lon = coords.x.ToString("F4");
                    }
                    writer.WriteLine(string.Join(",",
                        "system",
                        id.ToString(),
                        row.Count.ToString(),
                        (row.Count * area).ToString("F0"),
                        row.MinElev.ToString("F0"),
                        row.MaxElev.ToString("F0"),
                        "",
                        lat,
                        lon,
                        row.OutletKind,
                        row.OutletCount.ToString(),
                        "",
                        ""));
                }

                if (hydroSystems.Count > 0)
                {
                    var hydroIds = new List<int>(hydroSystems.Keys);
                    hydroIds.Sort();
                    for (int s = 0; s < hydroIds.Count; s++)
                    {
                        int id = hydroIds[s];
                        SystemRow row = hydroSystems[id];
                        string lat = "";
                        string lon = "";
                        if (row.OutletTile >= 0)
                        {
                            Vector3 coords = Find.WorldGrid.LongLatOf(row.OutletTile);
                            lat = coords.y.ToString("F4");
                            lon = coords.x.ToString("F4");
                        }
                        writer.WriteLine(string.Join(",",
                            "hydro_system",
                            id.ToString(),
                            row.Count.ToString(),
                            (row.Count * area).ToString("F0"),
                            row.MinElev.ToString("F0"),
                            row.MaxElev.ToString("F0"),
                            "",
                            lat,
                            lon,
                            row.OutletKind,
                            row.OutletCount.ToString(),
                            "",
                            ""));
                    }
                }

                for (int r = 0; r < ridges.Count; r++)
                {
                    RidgeRow ridge = ridges[r];
                    float meanElev = ridge.Count > 0 ? ridge.ElevSum / ridge.Count : 0f;
                    float meanEast = ridge.Count > 0 ? ridge.EastSum / ridge.Count : 0f;
                    float meanNorth = ridge.Count > 0 ? ridge.NorthSum / ridge.Count : 0f;
                    writer.WriteLine(string.Join(",",
                        "ridge",
                        ridge.Id.ToString(),
                        ridge.Count.ToString(),
                        (ridge.Count * area).ToString("F0"),
                        "",
                        "",
                        meanElev.ToString("F0"),
                        "",
                        "",
                        "",
                        "",
                        meanEast.ToString("F3"),
                        meanNorth.ToString("F3")));
                }
            }
            finally
            {
                writer.Dispose();
            }
            ExportWriter.FinishTemp(tempPath, finalPath);
            Messages.Message($"Drainage summary exported to {filename} ({systems.Count} systems, {ridges.Count} ridges)", MessageTypeDefOf.NeutralEvent);
        }

        private static int Downstream(ClimateData climate, int tileId, List<PlanetTile> neighbors, bool tier2 = false)
        {
            int[] flowDirs = tier2 ? climate.FlowDirectionHydrology : climate.FlowDirectionTopology;
            int[] spillDirs = tier2 ? climate.SpillDirectionHydrology : climate.SpillDirectionTopology;
            int slot = -1;
            if (flowDirs != null && tileId < flowDirs.Length && flowDirs[tileId] >= 0)
                slot = flowDirs[tileId];
            else if (spillDirs != null && tileId < spillDirs.Length && spillDirs[tileId] >= 0)
                slot = spillDirs[tileId];
            if (slot < 0)
                return -1;
            neighbors.Clear();
            Find.WorldGrid.GetTileNeighbors(tileId, neighbors);
            if (slot >= neighbors.Count)
                return -1;
            return neighbors[slot].tileId;
        }

        private static string OutletKind(ClimateData climate, int system, int downstream, bool tier2 = false)
        {
            if (downstream < 0)
                return "terminal";
            if (IsTrueOcean(climate, downstream))
                return "ocean";
            if (IsInlandWater(climate, downstream, tier2))
                return "lake";
            int[] sysArr = tier2 ? climate.RiverSystemIDHydrology : climate.RiverSystemIDTopology;
            int other = sysArr != null && downstream < sysArr.Length ? sysArr[downstream] : -1;
            if (other != system)
                return "spill";
            return null;
        }

        private static bool IsTrueOcean(ClimateData climate, int tileId)
        {
            if (climate.TrueOceanMask != null && tileId < climate.TrueOceanMask.Length && climate.TrueOceanMask[tileId])
                return true;
            return climate.WaterBodyID != null && tileId < climate.WaterBodyID.Length && climate.WaterBodyID[tileId] == 0
                && climate.OceanMask != null && tileId < climate.OceanMask.Length && climate.OceanMask[tileId];
        }

        private static bool IsInlandWater(ClimateData climate, int tileId, bool tier2 = false)
        {
            if (tier2 && climate.WaterBodyType != null && tileId < climate.WaterBodyType.Length && climate.WaterBodyType[tileId] == 2)
                return true;
            if (climate.WaterBodyID != null && tileId < climate.WaterBodyID.Length && climate.WaterBodyID[tileId] > 0)
                return true;
            return climate.OceanMask != null && tileId < climate.OceanMask.Length && climate.OceanMask[tileId];
        }

        private static float Flow(ClimateData climate, int tileId, bool tier2 = false)
        {
            float[] arr = tier2 ? climate.FlowAccumulationHydrology : climate.FlowAccumulationTopology;
            if (arr == null || tileId >= arr.Length)
                return 0f;
            return arr[tileId];
        }

        private static float Elevation(ClimateData climate, int tileId)
        {
            if (climate.ExactElevationMeters != null && tileId < climate.ExactElevationMeters.Length)
                return climate.ExactElevationMeters[tileId];
            return Find.WorldGrid[tileId].elevation;
        }

        private static bool IsRidge(ClimateData climate, int tileId)
        {
            return climate.IsRidgeTile != null && tileId < climate.IsRidgeTile.Length && climate.IsRidgeTile[tileId];
        }

        private static void Facing(ClimateData climate, int tileId, out float east, out float north)
        {
            east = 0f;
            north = 0f;
            if (climate.RidgeNormal == null || tileId >= climate.RidgeNormal.Length)
                return;
            Vector3 facing = climate.RidgeNormal[tileId];
            Vector3 radial = climate.TileCenters != null && tileId < climate.TileCenters.Length
                ? climate.TileCenters[tileId]
                : Find.WorldGrid.GetTileCenter(tileId);
            if (radial.sqrMagnitude < 1e-8f)
                return;
            radial.Normalize();
            Vector3 eastAxis = Vector3.Cross(radial, Vector3.up);
            if (eastAxis.sqrMagnitude < 1e-6f)
                eastAxis = Vector3.right;
            eastAxis.Normalize();
            Vector3 northAxis = Vector3.Cross(eastAxis, radial).normalized;
            east = Vector3.Dot(facing, eastAxis);
            north = Vector3.Dot(facing, northAxis);
        }

        private sealed class SystemRow
        {
            public int Count;
            public float MinElev = float.MaxValue;
            public float MaxElev = float.MinValue;
            public int OutletTile = -1;
            public float OutletFlow = float.MinValue;
            public string OutletKind = "terminal";
            public int OutletCount;
        }

        private sealed class RidgeRow
        {
            public int Id;
            public int Count;
            public float ElevSum;
            public float EastSum;
            public float NorthSum;
        }
    }
}
