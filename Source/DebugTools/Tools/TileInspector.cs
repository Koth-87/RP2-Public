using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate;
using Planets.UI.Dialogs;
using Planets.Core;
using Planets.WorldGen.Constants;

namespace Planets.DebugTools.Tools
{
    /// <summary>
    /// Interactive tile inspection tools for in-game climate and biome analysis
    /// </summary>
    public static class TileInspector
    {
        [DebugAction(DebugActionBase.CATEGORY_TOOLS, "Inspect Tile (Click)", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void InspectTileClick()
        {
            Messages.Message("Click on a tile to inspect it", MessageTypeDefOf.CautionInput);
            
            Find.WorldTargeter.BeginTargeting(new Func<GlobalTargetInfo, bool>(InspectTile),
                canTargetTiles: true,
                mouseAttachment: null,
                closeWorldTabWhenFinished: false,
                onUpdate: null,
                extraLabelGetter: (target) => GetTilePreviewLabel(target.Tile));
        }
        
        [DebugAction(DebugActionBase.CATEGORY_TOOLS, "Inspect Tile (by ID)", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void InspectTileById()
        {
            Find.WindowStack.Add(new IntegerInputDialog(
                (int selectedTile) => {
                    InspectTileByID(selectedTile);
                },
                0,
                "Enter tile ID to inspect:"));
        }
        
        private static bool InspectTile(GlobalTargetInfo target)
        {
            if (!target.HasWorldObject && target.Tile >= 0)
            {
                InspectTileByID(target.Tile);
                return true;
            }
            return false;
        }
        
        private static void InspectTileByID(int tileID)
        {
            if (tileID < 0 || tileID >= Find.WorldGrid.TilesCount)
            {
                Messages.Message("Invalid tile ID", MessageTypeDefOf.RejectInput);
                return;
            }
            
            var sb = new StringBuilder();
            AppendFullTileReport(sb, tileID);
            
            RPLog.Climate(sb.ToString());
            ShowTileSummary(tileID);
        }
        
        private static string GetTilePreviewLabel(int tileID)
        {
            if (tileID < 0 || tileID >= Find.WorldGrid.TilesCount)
                return "Invalid tile";
            
            var tile = Find.WorldGrid[tileID];
            var coords = Find.WorldGrid.LongLatOf(tileID);
            
            return $"Tile {tileID}: {tile.biome?.label ?? "Ocean"}\n" +
                   $"{coords.y:F1}°{(coords.y >= 0 ? "N" : "S")}, {coords.x:F1}°{(coords.x >= 0 ? "E" : "W")}\n" +
                   $"Temp: {Climate.AnnualTemp(tileID):F1}°C, Rain: {Climate.Rainfall(tileID):F0}mm";
        }
        
        private static void ShowTileSummary(int tileID)
        {
            var tile = Find.WorldGrid[tileID];
            var coords = Find.WorldGrid.LongLatOf(tileID);
            
            string summary = $"Tile {tileID}: {tile.biome?.label ?? "Ocean"} at " +
                           $"{coords.y:F1}°{(coords.y >= 0 ? "N" : "S")}, {coords.x:F1}°{(coords.x >= 0 ? "E" : "W")}\n" +
                           $"Temp: {Climate.AnnualTemp(tileID):F1}°C, Rain: {Climate.Rainfall(tileID):F0}mm, " +
                           $"Elev: {tile.elevation:F0}m";
            
            Messages.Message(summary, MessageTypeDefOf.NeutralEvent);
        }
        
        private static void AppendFullTileReport(StringBuilder sb, int tileID)
        {
            var tile = Find.WorldGrid[tileID];
            var coords = Find.WorldGrid.LongLatOf(tileID);
            var climateData = Find.World?.GetComponent<ClimateData>();
            
            sb.AppendLine($"=== TILE {tileID} COMPREHENSIVE ANALYSIS ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            
            // === LOCATION ===
            sb.AppendLine("LOCATION:");
            sb.AppendLine($"  Coordinates: {coords.y:F2}°{(coords.y >= 0 ? "N" : "S")}, {coords.x:F2}°{(coords.x >= 0 ? "E" : "W")}");
            sb.AppendLine($"  Biome: {tile.biome?.label ?? "None"}");
            sb.AppendLine($"  Water Covered: {(tile.WaterCovered ? "Yes" : "No")}");
            sb.AppendLine();
            
            // === TERRAIN ===
            sb.AppendLine("TERRAIN:");
            sb.AppendLine($"  Elevation: {tile.elevation:F0}m (rounded tile)");
            if (climateData?.ExactElevationMeters != null && tileID < climateData.ExactElevationMeters.Length)
                sb.AppendLine($"  Exact elevation: {climateData.ExactElevationMeters[tileID]:F1}m");
            sb.AppendLine($"  Hilliness: {tile.hilliness}");
            sb.AppendLine($"  Swampiness: {tile.swampiness:F2}");
            sb.AppendLine($"  Beside water: {(IsCoastal(tileID) ? "Yes" : "No")}");
            sb.AppendLine();
            
            // === TEMPERATURE ===
            sb.AppendLine("TEMPERATURE:");
            sb.AppendLine($"  Annual Mean: {Climate.AnnualTemp(tileID):F1}°C");
            sb.AppendLine($"  Summer Max: {Climate.SummerMax(tileID):F1}°C");
            sb.AppendLine($"  Winter Min: {Climate.WinterMin(tileID):F1}°C");
            sb.AppendLine($"  Seasonal Amplitude: {Climate.SeasonalAmp(tileID):F1}°C");
            sb.AppendLine();
            
            // === MOISTURE ===
            sb.AppendLine("MOISTURE:");
            sb.AppendLine($"  Annual Rainfall: {Climate.Rainfall(tileID):F0}mm");
            sb.AppendLine($"  Aridity Index: {Climate.Aridity(tileID):F2} ({GetAridityCategory(Climate.Aridity(tileID))})");
            if (climateData?.PotentialEvapotranspiration != null && tileID < climateData.PotentialEvapotranspiration.Length)
                sb.AppendLine($"  Evaporation: {climateData.PotentialEvapotranspiration[tileID]:F0}mm");
            if (climateData?.DailyRangeC != null && tileID < climateData.DailyRangeC.Length)
                sb.AppendLine($"  Daily range: {climateData.DailyRangeC[tileID]:F1}°C");
            sb.AppendLine($"  Monsoon boost (biome scoring, not added to rainfall): {Climate.MonsoonBoost(tileID):F0}mm");
            sb.AppendLine();
            
            // === CLIMATE EFFECTS ===
            sb.AppendLine("CLIMATE EFFECTS:");
            sb.AppendLine($"  Continentality: {Climate.Continentality(tileID):F2}");
            sb.AppendLine($"  Ocean distance (true ocean): {Climate.OceanDistKm(tileID):F0}km");
            if (climateData?.InlandWaterInfluence != null && tileID < climateData.InlandWaterInfluence.Length
                && climateData.InlandWaterInfluence[tileID] > 0.001f)
            {
                float inlandKm = climateData.InlandWaterDistKm != null && tileID < climateData.InlandWaterDistKm.Length
                    ? climateData.InlandWaterDistKm[tileID] : -1f;
                sb.AppendLine($"  Inland water: influence {climateData.InlandWaterInfluence[tileID]:F2}, distance {(inlandKm >= 0f ? inlandKm.ToString("F0") + "km" : "none")}");
            }
            sb.AppendLine($"  Windward Effect: {Climate.Windward(tileID):F2}");
            sb.AppendLine($"  Leeward Shadow: {Climate.Leeward(tileID):F2}");
            sb.AppendLine($"  Orographic Display (map): {Climate.OrographicDisplayValue(tileID):F2}");
            if (climateData?.CurrentsAnomalySST != null && tileID < climateData.CurrentsAnomalySST.Length)
                sb.AppendLine($"  Current anomaly: {climateData.CurrentsAnomalySST[tileID]:F1}°C");
            sb.AppendLine();
            
            // === HYDROLOGY ===
            if (Climate.IsInitialized())
            {
                sb.AppendLine("HYDROLOGY:");
                float drainageTiles = Climate.FlowAccumulation(tileID);
                float drainageKm2 = drainageTiles * EarthScaleConstants.TILE_AREA_KM2;
                sb.AppendLine($"  Flow Accumulation: {drainageTiles:F1} tiles ({drainageKm2:F0} km²)");
                if (climateData?.FlowRain != null && tileID < climateData.FlowRain.Length)
                    sb.AppendLine($"  Rain-weighted flow: {climateData.FlowRain[tileID]:F1}");
                sb.AppendLine($"  Endorheic Basin: {(Climate.IsEndorheic(tileID) ? "Yes" : "No")}");
                sb.AppendLine($"  Drainage Basin ID: {Climate.MergedBasinID(tileID)}");
                
                if (climateData != null)
                {
                    // River System ID
                    if (climateData.RiverSystemIDTopology != null && tileID < climateData.RiverSystemIDTopology.Length)
                    {
                        int riverSystemID = climateData.RiverSystemIDTopology[tileID];
                        sb.AppendLine($"  River System ID: {riverSystemID}");
                    }
                    
                    // Drainage Type
                    if (climateData.DrainageTypeClassification != null && tileID < climateData.DrainageTypeClassification.Length)
                    {
                        var drainageType = climateData.DrainageTypeClassification[tileID];
                        sb.AppendLine($"  Drainage Type: {drainageType}");
                    }
                    
                    // Merged Lake ID
                    if (climateData.MergedLakeIDTopology != null && tileID < climateData.MergedLakeIDTopology.Length)
                    {
                        int mergedLakeID = climateData.MergedLakeIDTopology[tileID];
                        if (mergedLakeID >= 0)
                            sb.AppendLine($"  Merged Lake ID: {mergedLakeID}");
                    }
                    
                    // Flow direction details
                    if (climateData.FlowDirectionTopology != null && tileID < climateData.FlowDirectionTopology.Length)
                    {
                        int flowDirIdx = climateData.FlowDirectionTopology[tileID];
                        sb.AppendLine($"  Flow Direction Index: {flowDirIdx}");
                        
                        if (flowDirIdx >= 0)
                        {
                            var neighborTiles = new List<PlanetTile>();
                            Find.WorldGrid.GetTileNeighbors(tileID, neighborTiles);
                            
                            if (flowDirIdx < neighborTiles.Count)
                            {
                                int downstreamID = neighborTiles[flowDirIdx].tileId;
                                var downstreamTile = Find.WorldGrid[downstreamID];
                                sb.AppendLine($"  Downstream Tile: {downstreamID} at {downstreamTile.elevation:F0}m");
                                sb.AppendLine($"  Downstream Basin ID: {Climate.MergedBasinID(downstreamID)}");
                            }
                            else
                            {
                                sb.AppendLine($"  ERROR: Flow direction index {flowDirIdx} out of range (only {neighborTiles.Count} neighbors)");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"  Flow Direction: SINK (no outflow)");
                        }
                    }
                    
                    // Spill direction details
                    if (climateData.SpillDirectionTopology != null && tileID < climateData.SpillDirectionTopology.Length)
                    {
                        int spillDirIdx = climateData.SpillDirectionTopology[tileID];
                        if (spillDirIdx >= 0)
                        {
                            sb.AppendLine($"  Spill Direction Index: {spillDirIdx}");
                            
                            var neighborTiles = new List<PlanetTile>();
                            Find.WorldGrid.GetTileNeighbors(tileID, neighborTiles);
                            
                            if (spillDirIdx < neighborTiles.Count)
                            {
                                int spillTargetID = neighborTiles[spillDirIdx].tileId;
                                var spillTargetTile = Find.WorldGrid[spillTargetID];
                                sb.AppendLine($"  Spill Target Tile: {spillTargetID} at {spillTargetTile.elevation:F0}m");
                                sb.AppendLine($"  Spill Target Basin ID: {Climate.MergedBasinID(spillTargetID)}");
                            }
                        }
                    }
                }
                
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("HYDROLOGY:");
                sb.AppendLine("  [Climate data not available - world not generated with Realistic Planets]");
                sb.AppendLine();
            }
            
            // === BIOME COMPETITION ===
            sb.AppendLine("BIOME COMPETITION:");
            sb.AppendLine("  Score is 100*exp(-energy/2), including frequency multiplier and foreign peak normalization. Higher is a better fit.");
            var biomeScores = GetBiomeScores(tile, tileID);
            var topScores = biomeScores.Take(10).ToList();
            
            for (int i = 0; i < topScores.Count; i++)
            {
                var (biome, score) = topScores[i];
                bool isWinner = biome == tile.biome;
                string marker = isWinner ? " <-- WINNER" : "";
                sb.AppendLine($"  #{i + 1}: {biome.label,-25} Score: {score:F1}{marker}");
            }
            
            if (topScores.Count >= 2)
            {
                float margin = topScores[0].score - topScores[1].score;
                sb.AppendLine($"  Victory Margin: {margin:F1} ({(margin <= 5f ? "CLOSE COMPETITION" : margin <= 10f ? "Moderate" : "Clear winner")})");
            }
            sb.AppendLine();
            
            // === RIVERS & ROADS ===
            sb.AppendLine("INFRASTRUCTURE:");
            if (tile.Rivers?.Any() == true)
            {
                var rivers = tile.Rivers.Select(r => r.river.label).ToList();
                sb.AppendLine($"  Rivers: {string.Join(", ", rivers)}");
            }
            else
            {
                sb.AppendLine("  Rivers: None");
            }
            
            if (tile.Roads?.Any() == true)
            {
                var roads = tile.Roads.Select(r => r.road.label).ToList();
                sb.AppendLine($"  Roads: {string.Join(", ", roads)}");
            }
            else
            {
                sb.AppendLine("  Roads: None");
            }
            sb.AppendLine();
            
            // === NEIGHBORS ===
            sb.AppendLine("NEIGHBORING TILES:");
            var neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(tileID, neighbors);
            
            int flowDirIndex = -1;
            int spillDirIndex = -1;
            
            if (climateData != null)
            {
                if (climateData.FlowDirectionTopology != null && tileID < climateData.FlowDirectionTopology.Length)
                    flowDirIndex = climateData.FlowDirectionTopology[tileID];
                    
                if (climateData.SpillDirectionTopology != null && tileID < climateData.SpillDirectionTopology.Length)
                    spillDirIndex = climateData.SpillDirectionTopology[tileID];
            }
            
            for (int i = 0; i < neighbors.Count && i < 6; i++)
            {
                var neighbor = neighbors[i];
                int neighborID = neighbor.tileId;
                var nTile = Find.WorldGrid[neighborID];
                var nCoords = Find.WorldGrid.LongLatOf(neighborID);
                
                // Hydrology info for neighbor
                string hydroInfo = "";
                if (Climate.IsInitialized())
                {
                    float nFlow = Climate.FlowAccumulation(neighborID);
                    hydroInfo = $", Flow: {nFlow:F0}";
                }
                
                // Direction indicators
                string dirIndicator = "";
                if (i == flowDirIndex) dirIndicator += " [DOWNSTREAM]";
                if (i == spillDirIndex) dirIndicator += " [SPILL TARGET]";
                
                sb.AppendLine($"  {neighborID}: {nTile.biome?.label ?? "Ocean"} " +
                            $"[Elev: {nTile.elevation:F0}m{hydroInfo}]{dirIndicator} " +
                            $"({nCoords.y:F1}°{(nCoords.y >= 0 ? "N" : "S")}, " +
                            $"{nCoords.x:F1}°{(nCoords.x >= 0 ? "E" : "W")})");
            }
        }
        
        private static string GetAridityCategory(float aridity)
        {
            if (aridity < 0.05f) return "Hyper-Arid";
            if (aridity < 0.2f) return "Arid";
            if (aridity < 0.5f) return "Semi-Arid";
            if (aridity < 1.0f) return "Sub-Humid";
            if (aridity < 2.0f) return "Humid";
            if (aridity < 4.0f) return "Very Humid";
            return "Extremely Humid";
        }
        
        private static bool IsCoastal(int tileID)
        {
            var tile = Find.WorldGrid[tileID];
            if (tile.WaterCovered) return false;
            
            var neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(tileID, neighbors);
            
            foreach (var neighbor in neighbors)
            {
                if (Find.WorldGrid[neighbor.tileId].WaterCovered)
                    return true;
            }
            return false;
        }
        
        private static List<(BiomeDef biome, float score)> GetBiomeScores(Tile tile, int tileID)
        {
            var candidates = Biomes.Core.BiomeSelector.EvaluateCandidates(tile, new PlanetTile(tileID, Find.WorldGrid.Surface), Find.WorldGrid.Surface);
            var scores = new List<(BiomeDef, float)>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                scores.Add((candidates[i].Biome, candidates[i].Score));
            return scores;
        }
    }
}
