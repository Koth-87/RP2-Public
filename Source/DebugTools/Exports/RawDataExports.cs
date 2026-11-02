using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Utilities.Logging;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Climate;
using Planets.DebugTools;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.WorldGen.Constants;

namespace Planets.DebugTools.Exports
{
    /// <summary>
    /// Raw data export actions for external Python analysis
    /// Exports complete tile data and biome scoring without in-game analysis
    /// </summary>
    public static class RawDataExports
    {
        [DebugAction(DebugActionBase.CATEGORY_EXPORTS, "Export Raw Tile Data", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void ExportRawTileData()
        {
            if (!Climate.IsInitialized())
            {
                Messages.Message("Climate system not initialized - generate a world first", MessageTypeDefOf.RejectInput);
                return;
            }
            
            LongEventHandler.QueueLongEvent(ExportRawTileDataWork(), "Exporting tile data", exception => RPLog.E($"Tile export failed: {exception}"));
        }

        private static IEnumerable ExportRawTileDataWork()
        {
            int count = Find.WorldGrid.TilesCount;
            var order = new List<(int tileId, float lat, float lon)>(count);
            for (int i = 0; i < count; i++)
            {
                if (i < 0 || i >= Find.WorldGrid.TilesCount)
                    continue;
                var coords = Find.WorldGrid.LongLatOf(i);
                order.Add((i, coords.y, coords.x));
                if (i % 4000 == 0)
                {
                    LongEventHandler.SetCurrentEventText($"Reading tiles {i}/{count}");
                    yield return null;
                }
            }
            order.Sort((a, b) =>
            {
                int latComp = b.lat.CompareTo(a.lat);
                return latComp != 0 ? latComp : a.lon.CompareTo(b.lon);
            });

            int writeCount = order.Count;
            string filename = ExportWriter.GetFilename("TileData", "csv");
            StreamWriter writer = ExportWriter.OpenTemp(filename, out string tempPath, out string finalPath);
            var climateData = Find.World?.GetComponent<ClimateData>();
            List<BiomeDef> biomes = GetSurfaceBiomes();
            try
            {
                float tiltDegrees = AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt);
                writer.WriteLine("# Raw Tile Data Export - Complete world tile dump sorted by Lat/Long");
                writer.WriteLine($"# AxialTilt: {Planets_GameComponent.ActiveAxialTilt} ({tiltDegrees}°)");
                writer.WriteLine($"# WorldSeed: {Find.World.info.seedString}");
                writer.WriteLine($"# Coverage: {Find.World.info.planetCoverage}");
                writer.WriteLine($"# OverallRainfall: {Find.World.info.overallRainfall}");
                writer.WriteLine($"# OverallTemperature: {Find.World.info.overallTemperature}");
                writer.WriteLine($"# ClimateModel: {climateData?.bakeRecord?.modelVersion ?? 0}");
                writer.WriteLine("# EarthHeightmap: " + (climateData?.bakeRecord?.earthHeightmap == true ? "yes" : "no"));
                writer.WriteLine("# OceanDistKm is distance to the true ocean. Lakes are not ocean.");
                writer.WriteLine("# Rainfall is the climate result. MonsoonBoost is not added to it.");
                writer.WriteLine("# Elevation is the rounded tile meter. ExactElevationM is the height used for drainage.");
                writer.WriteLine("# BiomeScore is 100*exp(-energy/2). Higher is a better fit.");
                writer.WriteLine("# SummerWind and WinterWind are east and north components of the solstice wind.");
                writer.WriteLine("# Summer and winter condensation, incoming, and deleted moisture are the two seasonal passes. Deleted is calm tiles plus wind off the map.");
                writer.WriteLine("# ElevationBandEdgesM: " + BandEdges(climateData));
                writer.WriteLine($"# TileAreaKm2: {EarthScaleConstants.TILE_AREA_KM2:F1}");
                writer.WriteLine("# FlowDir and SpillDir are indexes into NeighborIDs. RiverSystemID is the topographic fill-and-spill, run with no evaporation.");
                writer.WriteLine("# IsMajorPeak tiles are forced onto the ridge list. RidgeEast and RidgeNorth point across the ridge. The sign is arbitrary.");
                writer.WriteLine("TileID,Lat,Long,IsWater,Elevation,ExactElevationM,Hilliness," +
                    "AnnualTemp,SummerMax,WinterMin,SeasonalAmp," +
                    "Rainfall,Aridity,Swampiness," +
                    "Continentality," +
                    "FlowAccumulation,FlowRain,InitialBasinID,MergedBasinID,RiverSystemID,DrainageType,MergedLakeID,FlowDir,SpillDir," +
                    "FlowAccHydro,MergedLakeIDHydro,MergedBasinIDHydro,FlowDirHydro,SpillDirHydro,EndorheicMaskHydro,WaterBodyType,RiverMagnitude," +
                    "WaterBodyID,TrueOceanMask,EndorheicMask," +
                    "OceanDistKm,InlandWaterDistKm,InlandWaterInfluence,ElevationBand,IsRidgeTile,RidgeTier,RidgeNeighborSystemID,IsMajorPeak,RidgeEast,RidgeNorth," +
                    "Prominence,Relief,Ruggedness,Steepness," +
                    "UpwindReliefIndex,LeewardShadowIndex,MonsoonBoost,MonsoonMoistureSource,MonsoonLiftIndex," +
                    "AdvectedCondensationMm,SummerCondensationMm,WinterCondensationMm,SummerIncomingMm,WinterIncomingMm,SummerDeletedMm,WinterDeletedMm," +
                    "LatSeaLevelTemp,SST,CurrentsAnomaly,CoastalModStrength,PET,DailyRangeC," +
                    "AdvectedIncomingMm,AdvectedRemainingMm,AdvectedDryingRatio,WindSpeed," +
                    "SummerWindEast,SummerWindNorth,WinterWindEast,WinterWindNorth," +
                    "ConvergenceIndex,TemperatureCapacity," +
                    "MaritimeJune,MaritimeDec,MaritimeEq,MaritimeAnnual,UpwindSST," +
                    "Biome,BiomeScore," +
                    "NeighborCount,NeighborIDs");
                for (int n = 0; n < writeCount; n++)
                {
                    int tileId = order[n].tileId;
                    if (tileId < 0 || tileId >= Find.WorldGrid.TilesCount)
                        continue;
                    writer.WriteLine(BuildTileDataRow(tileId, Find.WorldGrid[tileId], climateData, biomes));
                    if (n % 25 == 0)
                    {
                        LongEventHandler.SetCurrentEventText($"Writing tiles {n}/{writeCount}");
                        yield return null;
                    }
                }
            }
            finally
            {
                writer.Dispose();
            }
            ExportWriter.FinishTemp(tempPath, finalPath);
            Messages.Message($"Raw tile data exported to {filename} ({writeCount} tiles)", MessageTypeDefOf.NeutralEvent);
        }
        
        [DebugAction(DebugActionBase.CATEGORY_EXPORTS, "Export Biome Scoring", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void ExportBiomeScoring()
        {
            if (!Climate.IsInitialized())
            {
                Messages.Message("Climate system not initialized - generate a world first", MessageTypeDefOf.RejectInput);
                return;
            }
            
            LongEventHandler.QueueLongEvent(ExportBiomeScoringWork(), "Exporting biome scoring", exception => RPLog.E($"Biome scoring export failed: {exception}"));
        }

        private static IEnumerable ExportBiomeScoringWork()
        {
            List<BiomeDef> biomeList = GetSurfaceBiomes();
            int count = Find.WorldGrid.TilesCount;
            var order = new List<(int tileId, float lat, float lon)>();
            for (int i = 0; i < count; i++)
            {
                if (i < 0 || i >= Find.WorldGrid.TilesCount || Find.WorldGrid[i].WaterCovered)
                    continue;
                var coords = Find.WorldGrid.LongLatOf(i);
                order.Add((i, coords.y, coords.x));
                if (i % 4000 == 0)
                {
                    LongEventHandler.SetCurrentEventText($"Reading land tiles {i}/{count}");
                    yield return null;
                }
            }
            order.Sort((a, b) =>
            {
                int latComp = b.lat.CompareTo(a.lat);
                return latComp != 0 ? latComp : a.lon.CompareTo(b.lon);
            });

            int writeCount = order.Count;
            string filename = ExportWriter.GetFilename("BiomeScoring", "csv");
            StreamWriter writer = ExportWriter.OpenTemp(filename, out string tempPath, out string finalPath);
            try
            {
                writer.WriteLine("# Biome Scoring Export");
                writer.WriteLine("# Score is 100*exp(-energy/2). Higher is a better fit. A biome that failed its gates is -999.");
                writer.WriteLine("TileID,Lat,Long,WinningBiome,WinningScore,SecondBiome,SecondScore," +
                    string.Join(",", biomeList.Select(b => CleanBiomeName(b.defName))));
                for (int n = 0; n < writeCount; n++)
                {
                    writer.WriteLine(BuildBiomeScoringRow(order[n].tileId, biomeList));
                    if (n % 10 == 0)
                    {
                        LongEventHandler.SetCurrentEventText($"Scoring tiles {n}/{writeCount}");
                        yield return null;
                    }
                }
            }
            finally
            {
                writer.Dispose();
            }
            ExportWriter.FinishTemp(tempPath, finalPath);
            Messages.Message($"Biome scoring exported to {filename} ({writeCount} land tiles)", MessageTypeDefOf.NeutralEvent);
        }
        
        // ===== HELPER METHODS =====
        
        private static List<BiomeDef> GetSurfaceBiomes()
        {
            var validBiomes = new List<BiomeDef>();
            
            foreach (var biome in DefDatabase<BiomeDef>.AllDefs)
            {
                if (biome == null || !biome.implemented || !biome.canAutoChoose)
                    continue;
                
                try
                {
                    if (biome.Worker != null)
                    {
                        validBiomes.Add(biome);
                    }
                }
                catch
                {
                    // Skip biomes with invalid or unresolvable worker classes
                }
            }
            
            return validBiomes;
        }
        
        private static List<(BiomeDef biome, float score)> GetBiomeScores(Tile tile, int tileID, List<BiomeDef> biomes)
        {
            var scores = new List<(BiomeDef, float)>();
            if (biomes == null || tile == null || tileID < 0 || Find.WorldGrid?.Surface == null || tileID >= Find.WorldGrid.TilesCount)
                return scores;

            var candidates = Biomes.Core.BiomeSelector.EvaluateCandidates(tile, new PlanetTile(tileID, Find.WorldGrid.Surface), Find.WorldGrid.Surface);
            for (int i = 0; i < candidates.Count; i++)
                scores.Add((candidates[i].Biome, candidates[i].Score));
            return scores;
        }
        
        private static string BuildTileDataRow(int tileId, Tile tile, ClimateData climateData, List<BiomeDef> biomes)
        {
            if (tile == null || tileId < 0 || Find.WorldGrid == null || tileId >= Find.WorldGrid.TilesCount)
                return "";
            var coords = Find.WorldGrid.LongLatOf(tileId);
            
            // Basic tile info
            string isWater = tile.WaterCovered ? "1" : "0";
            string elevation = tile.elevation.ToString("F1");
            string exactElevation = Num(climateData?.ExactElevationMeters, tileId, "F1");
            string hilliness = ((int)tile.hilliness).ToString();
            
            // Climate variables
            string annualTemp = Climate.AnnualTemp(tileId).ToString("F2");
            string summerMax = Climate.SummerMax(tileId).ToString("F2");
            string winterMin = Climate.WinterMin(tileId).ToString("F2");
            string seasonalAmp = Climate.SeasonalAmp(tileId).ToString("F2");
            
            string rainfall = Climate.Rainfall(tileId).ToString("F1");
            string aridity = Climate.Aridity(tileId).ToString("F4");
            string swampiness = tile.swampiness.ToString("F4");
            
            string continentality = Climate.Continentality(tileId).ToString("F3");
            // Hydrology variables
            string flowAcc = Climate.FlowAccumulation(tileId).ToString("F1");
            string flowRain = Num(climateData?.FlowRain, tileId, "F1");
            
            string initialBasinID = (climateData?.InitialBasinID != null && tileId < climateData.InitialBasinID.Length)
                ? climateData.InitialBasinID[tileId].ToString() : "-1";
            
            string mergedBasinID = Climate.MergedBasinID(tileId).ToString();
            
            string riverSystemID = (climateData?.RiverSystemIDTopology != null && tileId < climateData.RiverSystemIDTopology.Length) 
                ? climateData.RiverSystemIDTopology[tileId].ToString() : "-1";
                
            string drainageType = (climateData?.DrainageTypeClassification != null && tileId < climateData.DrainageTypeClassification.Length)
                ? climateData.DrainageTypeClassification[tileId].ToString() : "None";
                
            string lakeID = (climateData?.MergedLakeIDTopology != null && tileId < climateData.MergedLakeIDTopology.Length)
                ? climateData.MergedLakeIDTopology[tileId].ToString() : "-1";
                
            string flowDir = (climateData?.FlowDirectionTopology != null && tileId < climateData.FlowDirectionTopology.Length)
                ? climateData.FlowDirectionTopology[tileId].ToString() : "-1";
                
            string spillDir = (climateData?.SpillDirectionTopology != null && tileId < climateData.SpillDirectionTopology.Length)
                ? climateData.SpillDirectionTopology[tileId].ToString() : "-1";

            // Tier 2 Hydrology variables
            string flowAccHydro = (climateData?.FlowAccumulationHydrology != null && tileId < climateData.FlowAccumulationHydrology.Length)
                ? climateData.FlowAccumulationHydrology[tileId].ToString("F1") : "0.0";
            string lakeIDHydro = (climateData?.MergedLakeIDHydrology != null && tileId < climateData.MergedLakeIDHydrology.Length)
                ? climateData.MergedLakeIDHydrology[tileId].ToString() : "-1";
            string mergedBasinHydro = (climateData?.MergedBasinIDHydrology != null && tileId < climateData.MergedBasinIDHydrology.Length)
                ? climateData.MergedBasinIDHydrology[tileId].ToString() : "-1";
            string flowDirHydro = (climateData?.FlowDirectionHydrology != null && tileId < climateData.FlowDirectionHydrology.Length)
                ? climateData.FlowDirectionHydrology[tileId].ToString() : "-1";
            string spillDirHydro = (climateData?.SpillDirectionHydrology != null && tileId < climateData.SpillDirectionHydrology.Length)
                ? climateData.SpillDirectionHydrology[tileId].ToString() : "-1";
            string endorheicHydro = (climateData?.EndorheicMaskHydrology != null && tileId < climateData.EndorheicMaskHydrology.Length)
                ? (climateData.EndorheicMaskHydrology[tileId] ? "1" : "0") : "0";
            string waterBodyType = (climateData?.WaterBodyType != null && tileId < climateData.WaterBodyType.Length)
                ? climateData.WaterBodyType[tileId].ToString() : "0";
            string riverMag = (climateData?.RiverMagnitude != null && tileId < climateData.RiverMagnitude.Length)
                ? climateData.RiverMagnitude[tileId].ToString() : "0";
            
            // Water body and ocean classification
            string waterBodyID = (climateData?.WaterBodyID != null && tileId < climateData.WaterBodyID.Length)
                ? climateData.WaterBodyID[tileId].ToString() : "-1";
            
            string trueOceanMask = (climateData?.TrueOceanMask != null && tileId < climateData.TrueOceanMask.Length)
                ? (climateData.TrueOceanMask[tileId] ? "1" : "0") : "0";
            
            string endorheicMask = (climateData?.EndorheicMask != null && tileId < climateData.EndorheicMask.Length)
                ? (climateData.EndorheicMask[tileId] ? "1" : "0") : "0";
            
            // Geographic and terrain metrics
            string oceanDistKm = (climateData?.OceanDistKm != null && tileId < climateData.OceanDistKm.Length)
                ? climateData.OceanDistKm[tileId].ToString("F1") : "-1";
            string inlandDist = (climateData?.InlandWaterDistKm != null && tileId < climateData.InlandWaterDistKm.Length && climateData.InlandWaterDistKm[tileId] >= 0f)
                ? climateData.InlandWaterDistKm[tileId].ToString("F1") : "";
            string inlandInfluence = Num(climateData?.InlandWaterInfluence, tileId, "F3");
            
            string elevationBand = (climateData?.ElevationBand != null && tileId < climateData.ElevationBand.Length)
                ? climateData.ElevationBand[tileId].ToString() : "-1";
            
            string isRidgeTile = (climateData?.IsRidgeTile != null && tileId < climateData.IsRidgeTile.Length)
                ? (climateData.IsRidgeTile[tileId] ? "1" : "0") : "0";
            
            string ridgeTier = (climateData?.RidgeTier != null && tileId < climateData.RidgeTier.Length)
                ? climateData.RidgeTier[tileId].ToString() : "0";
            
            string ridgeNeighborSystemID = (climateData?.RidgeNeighborSystemID != null && tileId < climateData.RidgeNeighborSystemID.Length)
                ? climateData.RidgeNeighborSystemID[tileId].ToString() : "-1";
            string isMajorPeak = (climateData?.IsMajorPeak != null && tileId < climateData.IsMajorPeak.Length && climateData.IsMajorPeak[tileId]) ? "1" : "0";
            WindParts(climateData, tileId, climateData?.RidgeNormal, out string ridgeEast, out string ridgeNorth);
            if (string.IsNullOrEmpty(ridgeEast))
            {
                ridgeEast = "0";
                ridgeNorth = "0";
            }
            
            // Terrain metrics
            string prominence = (climateData?.Prominence != null && tileId < climateData.Prominence.Length)
                ? climateData.Prominence[tileId].ToString("F1") : "-1";
            
            string relief = (climateData?.Relief != null && tileId < climateData.Relief.Length)
                ? climateData.Relief[tileId].ToString("F1") : "-1";
            
            string ruggedness = (climateData?.Ruggedness != null && tileId < climateData.Ruggedness.Length)
                ? climateData.Ruggedness[tileId].ToString("F3") : "-1";
            
            string steepness = (climateData?.Steepness != null && tileId < climateData.Steepness.Length)
                ? climateData.Steepness[tileId].ToString("F3") : "-1";
            
            // Orographic details
            string upwindReliefIndex = (climateData?.UpwindReliefIndex != null && tileId < climateData.UpwindReliefIndex.Length)
                ? climateData.UpwindReliefIndex[tileId].ToString("F3") : "-1";
            
            string leewardShadowIndex = (climateData?.LeewardShadowIndex != null && tileId < climateData.LeewardShadowIndex.Length)
                ? climateData.LeewardShadowIndex[tileId].ToString("F3") : "-1";
            
            // Moisture sources
            string monsoonBoost = (climateData?.MonsoonBoost != null && tileId < climateData.MonsoonBoost.Length)
                ? climateData.MonsoonBoost[tileId].ToString("F1") : "0";
            string monsoonMoistureSource = (climateData?.MonsoonMoistureSource != null && tileId < climateData.MonsoonMoistureSource.Length)
                ? climateData.MonsoonMoistureSource[tileId].ToString("F3") : "0";
            string monsoonLiftIndex = (climateData?.MonsoonLiftIndex != null && tileId < climateData.MonsoonLiftIndex.Length)
                ? climateData.MonsoonLiftIndex[tileId].ToString("F3") : "0";

            // Rainfall pipeline diagnostics (analysis-only)
            float advectedCondMm = (climateData?.AdvectedCondensationMm != null && tileId < climateData.AdvectedCondensationMm.Length)
                ? climateData.AdvectedCondensationMm[tileId] : 0f;

            // Extra Climate Variables
            string latSeaLevelTemp = (climateData?.LatitudinalSeaLevelTemp != null && tileId < climateData.LatitudinalSeaLevelTemp.Length)
                ? climateData.LatitudinalSeaLevelTemp[tileId].ToString("F2") : "0";
            string sst = (climateData?.SST != null && tileId < climateData.SST.Length)
                ? climateData.SST[tileId].ToString("F2") : "0";
            string currentsAnomaly = (climateData?.CurrentsAnomalySST != null && tileId < climateData.CurrentsAnomalySST.Length)
                ? climateData.CurrentsAnomalySST[tileId].ToString("F2") : "0";
            string coastalModStrength = (climateData?.CoastalModerationStrength != null && tileId < climateData.CoastalModerationStrength.Length)
                ? climateData.CoastalModerationStrength[tileId].ToString("F3") : "0";
            string pet = (climateData?.PotentialEvapotranspiration != null && tileId < climateData.PotentialEvapotranspiration.Length)
                ? climateData.PotentialEvapotranspiration[tileId].ToString("F1") : "0";
            string dailyRange = Num(climateData?.DailyRangeC, tileId, "F1");
            
            string advectedIncomingMm = (climateData?.AdvectedIncomingMoistureMm != null && tileId < climateData.AdvectedIncomingMoistureMm.Length)
                ? climateData.AdvectedIncomingMoistureMm[tileId].ToString("F1") : "0";
            string advectedRemainingMm = (climateData?.AdvectedRemainingMoistureMm != null && tileId < climateData.AdvectedRemainingMoistureMm.Length)
                ? climateData.AdvectedRemainingMoistureMm[tileId].ToString("F1") : "0";
            string advectedDryingRatio = (climateData?.AdvectedDryingRatio != null && tileId < climateData.AdvectedDryingRatio.Length)
                ? climateData.AdvectedDryingRatio[tileId].ToString("F3") : "0";
            
            string windSpeed = (climateData?.WindSpeed != null && tileId < climateData.WindSpeed.Length)
                ? climateData.WindSpeed[tileId].ToString("F2") : "0";
            WindParts(climateData, tileId, climateData?.SummerWind, out string summerEast, out string summerNorth);
            WindParts(climateData, tileId, climateData?.WinterWind, out string winterEast, out string winterNorth);
            string convergenceIndex = (climateData?.ConvergenceIndex != null && tileId < climateData.ConvergenceIndex.Length)
                ? climateData.ConvergenceIndex[tileId].ToString("F3") : "0";
            string temperatureCapacity = (climateData?.TemperatureCapacity != null && tileId < climateData.TemperatureCapacity.Length)
                ? climateData.TemperatureCapacity[tileId].ToString("F3") : "0";

            string maritimeJune = Num(climateData?.MaritimeJune, tileId, "F3");
            string maritimeDec = Num(climateData?.MaritimeDecember, tileId, "F3");
            string maritimeEq = Num(climateData?.MaritimeEquinox, tileId, "F3");
            string maritimeAnnual = Num(climateData?.MaritimeAnnual, tileId, "F3");
            string upwindSst = Num(climateData?.UpwindSST, tileId, "F2");

            string advectedCondStr = advectedCondMm.ToString("F1");
            string summerCond = Num(climateData?.SummerCondensationMm, tileId, "F1");
            string winterCond = Num(climateData?.WinterCondensationMm, tileId, "F1");
            string summerIn = Num(climateData?.SummerIncomingMm, tileId, "F1");
            string winterIn = Num(climateData?.WinterIncomingMm, tileId, "F1");
            string summerDeleted = Num(climateData?.SummerDeletedMm, tileId, "F1");
            string winterDeleted = Num(climateData?.WinterDeletedMm, tileId, "F1");

            // Biome
            string biome = tile.biome?.defName ?? "unknown";
            
            // Calculate biome score (winning score only)
            string biomeScore = "0.0";
            try
            {
                if (!tile.WaterCovered && tile.biome != null)
                {
                    var scores = GetBiomeScores(tile, tileId, biomes);
                    var winningScore = scores.FirstOrDefault(s => s.biome == tile.biome);
                    biomeScore = winningScore.score.ToString("F2");
                }
            }
            catch (Exception e)
            {
                RPLog.W($"[TileExport] Failed to get biome score for tile {tileId}: {e.Message}");
            }
            
            // NEW: Neighbor connectivity data (for external mountain range analysis)
            var neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(tileId, neighbors);
            string neighborCount = neighbors.Count.ToString();
            string neighborIDs = string.Join("|", neighbors.Select(n => n.tileId));
            
            return $"{tileId},{coords.y:F4},{coords.x:F4},{isWater},{elevation},{exactElevation},{hilliness}," +
                   $"{annualTemp},{summerMax},{winterMin},{seasonalAmp}," +
                   $"{rainfall},{aridity},{swampiness}," +
                   $"{continentality}," +
                   $"{flowAcc},{flowRain},{initialBasinID},{mergedBasinID},{riverSystemID},{drainageType},{lakeID},{flowDir},{spillDir}," +
                   $"{flowAccHydro},{lakeIDHydro},{mergedBasinHydro},{flowDirHydro},{spillDirHydro},{endorheicHydro},{waterBodyType},{riverMag}," +
                   $"{waterBodyID},{trueOceanMask},{endorheicMask}," +
                   $"{oceanDistKm},{inlandDist},{inlandInfluence},{elevationBand},{isRidgeTile},{ridgeTier},{ridgeNeighborSystemID},{isMajorPeak},{ridgeEast},{ridgeNorth}," +
                   $"{prominence},{relief},{ruggedness},{steepness}," +
                   $"{upwindReliefIndex},{leewardShadowIndex},{monsoonBoost},{monsoonMoistureSource},{monsoonLiftIndex}," +
                   $"{advectedCondStr},{summerCond},{winterCond},{summerIn},{winterIn},{summerDeleted},{winterDeleted}," +
                   $"{latSeaLevelTemp},{sst},{currentsAnomaly},{coastalModStrength},{pet},{dailyRange}," +
                   $"{advectedIncomingMm},{advectedRemainingMm},{advectedDryingRatio},{windSpeed}," +
                   $"{summerEast},{summerNorth},{winterEast},{winterNorth}," +
                   $"{convergenceIndex},{temperatureCapacity}," +
                   $"{maritimeJune},{maritimeDec},{maritimeEq},{maritimeAnnual},{upwindSst}," +
                   $"{biome},{biomeScore}," +
                   $"{neighborCount},{neighborIDs}";
        }

        private static string BandEdges(ClimateData climateData)
        {
            if (climateData?.BandThresholds == null || climateData.BandThresholds.Length == 0)
                return "unknown";
            return string.Join(", ", climateData.BandThresholds.Select(edge => edge.ToString("F0")));
        }

        private static string Num(float[] values, int tileId, string format)
        {
            if (values == null || tileId < 0 || tileId >= values.Length)
                return "";
            return values[tileId].ToString(format);
        }

        private static void WindParts(ClimateData climate, int tileId, Vector3[] field, out string east, out string north)
        {
            east = "";
            north = "";
            if (field == null || tileId < 0 || tileId >= field.Length)
                return;

            Vector3 wind = field[tileId];
            Vector3 normal = climate?.TileCenters != null && tileId < climate.TileCenters.Length
                ? climate.TileCenters[tileId]
                : Find.WorldGrid.Surface.GetTileCenter(tileId);
            if (normal.sqrMagnitude < 1e-8f)
                return;
            normal.Normalize();
            Vector3 eastAxis = Vector3.Cross(normal, Vector3.up);
            if (eastAxis.sqrMagnitude < 1e-6f)
                eastAxis = Vector3.right;
            eastAxis.Normalize();
            Vector3 northAxis = Vector3.Cross(eastAxis, normal).normalized;
            east = Vector3.Dot(wind, eastAxis).ToString("F3");
            north = Vector3.Dot(wind, northAxis).ToString("F3");
        }
        
        private static string BuildBiomeScoringRow(int tileId, List<BiomeDef> biomeList)
        {
            var coords = Find.WorldGrid.LongLatOf(tileId);
            var tile = Find.WorldGrid[tileId];
            
            // Get all biome scores
            var allScores = new Dictionary<string, float>();
            BiomeDef winningBiome = tile.biome;
            BiomeDef secondBiome = null;
            float winningScore = -999f;
            float secondScore = -999f;
            
            try
            {
                var scores = GetBiomeScores(tile, tileId, biomeList);
                var sortedScores = scores.OrderByDescending(s => s.score).ToList();
                
                // Get winning and second place
                if (sortedScores.Count > 0)
                {
                    winningScore = sortedScores[0].score;
                    if (sortedScores.Count > 1)
                    {
                        secondBiome = sortedScores[1].biome;
                        secondScore = sortedScores[1].score;
                    }
                }
                
                // Build score dictionary for all biomes
                foreach (var scoreData in scores)
                {
                    string cleanName = CleanBiomeName(scoreData.biome.defName);
                    allScores[cleanName] = scoreData.score;
                }
            }
            catch (Exception e)
            {
                RPLog.W($"[BiomeExport] Failed to get biome scores for tile {tileId}: {e.Message}");
            }
            
            // Build base data
            var row = new StringBuilder();
            row.Append($"{tileId},{coords.y:F4},{coords.x:F4},");
            row.Append($"{winningBiome?.defName ?? "unknown"},{winningScore:F2},");
            row.Append($"{secondBiome?.defName ?? "none"},{secondScore:F2}");
            
            // Add all biome scores in order
            foreach (var biome in biomeList)
            {
                string cleanName = CleanBiomeName(biome.defName);
                float score = allScores.ContainsKey(cleanName) ? allScores[cleanName] : -999f;
                row.Append($",{score:F2}");
            }
            
            return row.ToString();
        }
        
        private static string CleanBiomeName(string biomeName)
        {
            if (string.IsNullOrEmpty(biomeName)) return "unknown";
            
            return biomeName.Replace(",", "_")
                           .Replace(" ", "_")
                           .Replace("-", "_")
                           .Replace("(", "")
                           .Replace(")", "")
                           .Replace("'", "")
                           .Replace('"', '_');
        }
    }
}
