using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.WorldGen.Climate;
using Planets.WorldGen.Constants;

namespace MapModeFramework
{
    /// <summary>
    /// Terrain, climate, and hydrology map mode using a table-driven descriptor lookup per <see cref="MapModeDef.defName"/>.
    /// </summary>
    public class MapMode_Terrain : MapMode
    {
        private readonly struct ModeDescriptor
        {
            public readonly Func<int, Material> Material;
            public readonly Func<int, string> Label;
            public readonly Func<int, string> Tooltip;

            public ModeDescriptor(Func<int, Material> material, Func<int, string> label, Func<int, string> tooltip)
            {
                Material = material;
                Label = label;
                Tooltip = tooltip;
            }
        }

        private static readonly Dictionary<string, ModeDescriptor> Descriptors = BuildDescriptors();

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => true;

        public MapMode_Terrain() { }
        public MapMode_Terrain(MapModeDef def) : base(def) { }

        public override Material GetMaterial(int tile)
        {
            string key = ResolveModeKey();
            if (key != null && Descriptors.TryGetValue(key, out ModeDescriptor desc) && desc.Material != null)
                return desc.Material(tile);
            return base.GetMaterial(tile);
        }

        public override string GetTileLabel(int tile)
        {
            string key = ResolveModeKey();
            if (key != null && Descriptors.TryGetValue(key, out ModeDescriptor desc) && desc.Label != null)
                return desc.Label(tile);
            return base.GetTileLabel(tile);
        }

        public override string GetTooltip(int tile)
        {
            string key = ResolveModeKey();
            if (key != null && Descriptors.TryGetValue(key, out ModeDescriptor desc) && desc.Tooltip != null)
                return desc.Tooltip(tile);
            return base.GetTooltip(tile);
        }

        private string ResolveModeKey()
        {
            if (def == null)
                return null;
            if (def == MapModeFrameworkDefOf.Temperature)
                return "Temperature";
            if (def == MapModeFrameworkDefOf.Elevation)
                return "Elevation";
            if (def == MapModeFrameworkDefOf.Rainfall)
                return "Rainfall";
            return def.defName;
        }

        private static Dictionary<string, ModeDescriptor> BuildDescriptors()
        {
            var map = new Dictionary<string, ModeDescriptor>(StringComparer.Ordinal)
            {
                ["Temperature"] = new ModeDescriptor(
                    tile => Materials.MatForTemperature(Climate.AnnualTemp(tile)),
                    tile => Climate.AnnualTemp(tile).ToStringTemperature(),
                    tile => string.Format("{0}:\n{1}", "AvgTemp".Translate(), GenTemperature.GetAverageTemperatureLabel(tile))),

                ["Elevation"] = new ModeDescriptor(
                    tile => Materials.MatForElevation(Find.WorldGrid[tile].elevation),
                    tile => Find.WorldGrid[tile].elevation.ToString("F0") + "m",
                    tile => string.Format("{0}: {1}m\nColors run from sea level to 9000 m. Higher ground uses the top color.", "Elevation".Translate(), Find.WorldGrid[tile].elevation.ToString("F0"))),

                ["Rainfall"] = new ModeDescriptor(
                    tile => Materials.MatForRainfallOverlay(Climate.Rainfall(tile)),
                    tile => Climate.Rainfall(tile).ToString("F0") + "mm",
                    TooltipRainfall),

                ["RP_AridityIndex"] = new ModeDescriptor(
                    tile => Materials.MatForAridityOverlay(Climate.Aridity(tile)),
                    tile => Climate.Aridity(tile).ToString("F2"),
                    TooltipAridity),

                ["RP_Continentality"] = new ModeDescriptor(
                    tile => Materials.MatForContinentalityOverlay(Climate.Continentality(tile)),
                    tile => (Climate.Continentality(tile) * 100f).ToString("F0") + "%",
                    TooltipContinentality),

                ["RP_SeasonalAmplitude"] = new ModeDescriptor(
                    tile => Materials.MatForSeasonalAmplitudeOverlay(Climate.SeasonalAmp(tile)),
                    tile => Climate.SeasonalAmp(tile).ToString("F1") + "°C",
                    TooltipSeasonalAmplitude),

                ["RP_Monsoon"] = new ModeDescriptor(
                    tile => Materials.MatForMonsoonOverlay(Climate.MonsoonBoost(tile)),
                    tile => Climate.MonsoonBoost(tile).ToString("F0") + "mm",
                    TooltipMonsoon),

                ["RP_Hilliness"] = new ModeDescriptor(
                    tile => Materials.MatForHillinessOverlay((int)Find.WorldGrid[tile].hilliness),
                    tile => Find.WorldGrid[tile].hilliness.GetLabel(),
                    TooltipHilliness),

                ["RP_RiverFlow"] = new ModeDescriptor(
                    tile => TryClimateValue(tile, c => c.FlowRain, out float flow) ? Materials.MatForFlowAccumulationOverlay(flow) : BaseContent.ClearMat,
                    tile => TryClimateValue(tile, c => c.FlowRain, out float flow) ? flow.ToString("F0") : "-",
                    null),

                ["RP_IncomingMoisture"] = MoistureDescriptor(c => c.AdvectedIncomingMoistureMm),
                ["RP_Condensation"] = MoistureDescriptor(c => c.AdvectedCondensationMm),
                ["RP_RemainingMoisture"] = MoistureDescriptor(c => c.AdvectedRemainingMoistureMm),

                ["RP_DryingRatio"] = new ModeDescriptor(
                    tile => UnitMaterial(tile, c => c.AdvectedDryingRatio),
                    tile => TryClimateValue(tile, c => c.AdvectedDryingRatio, out float r) ? r.ToString("F2") : "-",
                    tile => TryClimateValue(tile, c => c.AdvectedDryingRatio, out float r) ? "Remaining / incoming: " + r.ToString("F2") : "No drying data"),

                ["RP_CurrentAnomaly"] = new ModeDescriptor(
                    tile => SignedMaterial(tile, c => c.CurrentsAnomalySST, 8f),
                    tile => TryClimateValue(tile, c => c.CurrentsAnomalySST, out float a) ? a.ToString("F1") + "°C" : "-",
                    tile => TryClimateValue(tile, c => c.CurrentsAnomalySST, out float a) ? "Current anomaly: " + a.ToString("F1") + "°C" : "No current anomaly"),

                ["RP_Convergence"] = new ModeDescriptor(
                    tile => SignedMaterial(tile, c => c.ConvergenceIndex, 1f),
                    tile => TryClimateValue(tile, c => c.ConvergenceIndex, out float c) ? c.ToString("F2") : "-",
                    tile => TryClimateValue(tile, c => c.ConvergenceIndex, out float c) ? "Convergence: " + c.ToString("F2") : "No convergence data"),

                ["RP_DailyRange"] = new ModeDescriptor(
                    tile => TryClimateValue(tile, c => c.DailyRangeC, out float r) ? Materials.MatForDailyRange(r) : BaseContent.ClearMat,
                    tile => TryClimateValue(tile, c => c.DailyRangeC, out float r) ? r.ToString("F1") + "°C" : "-",
                    tile => TryClimateValue(tile, c => c.DailyRangeC, out float r) ? "Daily range: " + r.ToString("F1") + "°C" : "No daily range"),

                ["RP_FlowAccumulation"] = new ModeDescriptor(
                    MaterialFlowAccumulationTopology,
                    LabelFlowAccumulationTopology,
                    TooltipFlowAccumulationTopology),

                ["RP_FlowAccumulationHydrology"] = new ModeDescriptor(
                    MaterialFlowAccumulationHydrology,
                    LabelFlowAccumulationHydrology,
                    TooltipFlowAccumulationHydrology),

                ["RP_Wetlands"] = new ModeDescriptor(
                    tile => TryClimateValue(tile, c => c.Swampiness, out float s) ? Materials.MatForWetlandsOverlay(s) : BaseContent.ClearMat,
                    tile => TryClimateValue(tile, c => c.Swampiness, out float s) ? (s * 100f).ToString("F0") + "%" : "0%",
                    TooltipWetlands),

                ["RP_InitialBasins"] = new ModeDescriptor(
                    MaterialInitialBasins,
                    tile => TryClimateInt(tile, c => c.InitialBasinID, out int id) ? id.ToString() : "-",
                    null),

                ["RP_MergedBasins"] = new ModeDescriptor(
                    MaterialMergedBasins,
                    LabelMergedBasins,
                    null),

                ["RP_DrainageType"] = new ModeDescriptor(
                    MaterialDrainageType,
                    LabelDrainageType,
                    null),

                ["RP_RiverSystems"] = new ModeDescriptor(
                    MaterialRiverSystems,
                    tile => TryClimateInt(tile, c => c.RiverSystemIDTopology, out int id) ? id.ToString() : "-",
                    TooltipRiverSystems),

                ["RP_MaritimeShare"] = new ModeDescriptor(
                    tile => UnitMaterial(tile, c => c.MaritimeAnnual),
                    tile => TryClimateValue(tile, c => c.MaritimeAnnual, out float m) ? (m * 100f).ToString("F0") + "%" : "-",
                    tile => TryClimateValue(tile, c => c.MaritimeAnnual, out float m) ? "Maritime air share: " + (m * 100f).ToString("F1") + "%" : "No maritime data"),

                ["RP_PrecipitableWater"] = MoistureDescriptor(c => c.PrecipitableWaterMm),
                ["RP_LandEvaporation"] = MoistureDescriptor(c => c.LandEvaporationMm),

                ["RP_OceanWaterShare"] = new ModeDescriptor(
                    tile => UnitMaterial(tile, c => c.OceanWaterShare),
                    tile => TryClimateValue(tile, c => c.OceanWaterShare, out float s) ? (s * 100f).ToString("F0") + "%" : "-",
                    tile => TryClimateValue(tile, c => c.OceanWaterShare, out float s) ? "Ocean-evaporated share: " + (s * 100f).ToString("F1") + "%" : "No moisture data")
            };

            return map;
        }

        private static ModeDescriptor MoistureDescriptor(Func<ClimateData, float[]> field)
        {
            return new ModeDescriptor(
                tile => TryClimateValue(tile, field, out float mm) ? Materials.MatForMoistureMm(mm) : BaseContent.ClearMat,
                tile => TryClimateValue(tile, field, out float mm) ? mm.ToString("F0") + "mm" : "-",
                tile => TryClimateValue(tile, field, out float mm) ? mm.ToString("F0") + " mm" : "No moisture data");
        }

        private static bool TryClimateValue(int tile, Func<ClimateData, float[]> field, out float value)
        {
            value = 0f;
            ClimateData cache = Find.World?.GetComponent<ClimateData>();
            float[] values = cache == null ? null : field(cache);
            if (values == null || tile < 0 || tile >= values.Length)
                return false;
            value = values[tile];
            return true;
        }

        private static bool TryClimateInt(int tile, Func<ClimateData, int[]> field, out int value)
        {
            value = -1;
            ClimateData cache = Find.World?.GetComponent<ClimateData>();
            int[] values = cache == null ? null : field(cache);
            if (values == null || tile < 0 || tile >= values.Length)
                return false;
            value = values[tile];
            return true;
        }

        private static Material UnitMaterial(int tile, Func<ClimateData, float[]> field)
        {
            return TryClimateValue(tile, field, out float value) ? Materials.MatForZeroToOne(value) : BaseContent.ClearMat;
        }

        private static Material SignedMaterial(int tile, Func<ClimateData, float[]> field, float halfRange)
        {
            return TryClimateValue(tile, field, out float value) ? Materials.MatForSigned(value, halfRange) : BaseContent.ClearMat;
        }

        private static Material MaterialFlowAccumulationTopology(int tile)
        {
            if (TryClimateValue(tile, c => c.FlowAccumulationTopology, out float tiles))
                return Materials.MatForFlowAccumulationOverlay(tiles * EarthScaleConstants.TILE_AREA_KM2);
            return BaseContent.ClearMat;
        }

        private static string LabelFlowAccumulationTopology(int tile)
        {
            if (TryClimateValue(tile, c => c.FlowAccumulationTopology, out float tiles))
                return (tiles * EarthScaleConstants.TILE_AREA_KM2).ToString("F0") + " km²";
            return "-";
        }

        private static string TooltipFlowAccumulationTopology(int tile)
        {
            if (TryClimateValue(tile, c => c.FlowAccumulationTopology, out float tiles))
            {
                float drainageKm2 = tiles * EarthScaleConstants.TILE_AREA_KM2;
                string classification = drainageKm2 < EarthScaleConstants.STREAM_MIN_DRAINAGE_KM2 ? "Headwater" :
                                        drainageKm2 < EarthScaleConstants.RIVER_MIN_DRAINAGE_KM2 ? "Stream" :
                                        drainageKm2 < EarthScaleConstants.MAJOR_RIVER_MIN_DRAINAGE_KM2 ? "River" :
                                        drainageKm2 < EarthScaleConstants.HUGE_RIVER_MIN_DRAINAGE_KM2 ? "Major River" : "Huge River";
                return string.Format("Flow Accumulation: {0:F1} tiles\nDrainage Area: {1:F0} km²\nClassification: {2}",
                    tiles, drainageKm2, classification);
            }
            return "No hydrology data available";
        }

        private static Material MaterialFlowAccumulationHydrology(int tile)
        {
            if (TryClimateValue(tile, c => c.FlowAccumulationHydrology, out float flowVolume))
                return Materials.MatForFlowAccumulationOverlay(flowVolume / 1000f);
            return BaseContent.ClearMat;
        }

        private static string LabelFlowAccumulationHydrology(int tile)
        {
            if (TryClimateValue(tile, c => c.FlowAccumulationHydrology, out float flowVolume))
                return (flowVolume / 1000f).ToString("F0") + " km² eq.";
            return "-";
        }

        private static string TooltipFlowAccumulationHydrology(int tile)
        {
            if (TryClimateValue(tile, c => c.FlowAccumulationHydrology, out float flowVolume))
            {
                float eq = flowVolume / 1000f;
                string classification = eq < EarthScaleConstants.STREAM_MIN_DRAINAGE_KM2 ? "Headwater" :
                    eq < EarthScaleConstants.RIVER_MIN_DRAINAGE_KM2 ? "Stream" :
                    eq < EarthScaleConstants.MAJOR_RIVER_MIN_DRAINAGE_KM2 ? "River" :
                    eq < EarthScaleConstants.HUGE_RIVER_MIN_DRAINAGE_KM2 ? "Major River" : "Huge River";
                return string.Format("Flow (Climate): {0:F0} km² eq.\nClassification: {1}\nTier 2 hydrology.", eq, classification);
            }
            return "No Tier 2 hydrology data (enable Advanced Hydrology)";
        }

        private static Material MaterialInitialBasins(int tile)
        {
            if (TryClimateInt(tile, c => c.InitialBasinID, out int basinId))
            {
                if (basinId == tile)
                    return Materials.matDrainageFilledTerminal;
                if (basinId >= 0)
                    return Materials.MatForDrainageBasin(basinId);
            }
            return BaseContent.ClearMat;
        }

        private static Material MaterialMergedBasins(int tile)
        {
            ClimateData cache = Find.World?.GetComponent<ClimateData>();
            if (cache?.MergedBasinIDTopology != null && tile >= 0 && tile < cache.MergedBasinIDTopology.Length)
            {
                int lakeId = cache.MergedLakeIDTopology != null && tile < cache.MergedLakeIDTopology.Length
                    ? cache.MergedLakeIDTopology[tile]
                    : -1;
                if (lakeId >= 0 && cache.DrainageTypeClassification != null && tile < cache.DrainageTypeClassification.Length)
                {
                    DrainageType type = cache.DrainageTypeClassification[tile];
                    if (type == DrainageType.FilledSpilling) return Materials.matDrainageFilledSpilling;
                    if (type == DrainageType.FilledTerminal) return Materials.matDrainageFilledTerminal;
                }

                int basinId = cache.MergedBasinIDTopology[tile];
                if (basinId >= 0)
                    return Materials.MatForDrainageBasin(basinId);
            }
            return BaseContent.ClearMat;
        }

        private static string LabelMergedBasins(int tile)
        {
            ClimateData cache = Find.World?.GetComponent<ClimateData>();
            if (cache?.MergedBasinIDTopology != null && tile >= 0 && tile < cache.MergedBasinIDTopology.Length)
            {
                string label = cache.MergedBasinIDTopology[tile].ToString();
                if (cache.MergedLakeIDTopology != null && tile < cache.MergedLakeIDTopology.Length && cache.MergedLakeIDTopology[tile] >= 0)
                    label += " (L)";
                return label;
            }
            return "-";
        }

        private static Material MaterialDrainageType(int tile)
        {
            ClimateData cache = Find.World?.GetComponent<ClimateData>();
            if (cache?.DrainageTypeClassification != null && tile >= 0 && tile < cache.DrainageTypeClassification.Length)
            {
                return cache.DrainageTypeClassification[tile] switch
                {
                    DrainageType.ExorheicOcean => Materials.matDrainageExorheic,
                    DrainageType.EndorheicLake => Materials.matDrainageEndorheicLake,
                    DrainageType.FilledTerminal => Materials.matDrainageFilledTerminal,
                    DrainageType.FilledSpilling => Materials.matDrainageFilledSpilling,
                    _ => BaseContent.ClearMat
                };
            }
            return BaseContent.ClearMat;
        }

        private static string LabelDrainageType(int tile)
        {
            ClimateData cache = Find.World?.GetComponent<ClimateData>();
            if (cache?.DrainageTypeClassification != null && tile >= 0 && tile < cache.DrainageTypeClassification.Length)
                return cache.DrainageTypeClassification[tile].ToString();
            return "-";
        }

        private static Material MaterialRiverSystems(int tile)
        {
            if (TryClimateInt(tile, c => c.RiverSystemIDTopology, out int systemId) && systemId >= 0)
                return Materials.MatForDrainageBasin(systemId);
            return BaseContent.ClearMat;
        }

        private static string TooltipRainfall(int tile)
        {
            float rain = Climate.Rainfall(tile);
            string classification =
                rain < 100f   ? "Hyper-arid desert (0–100mm/year)" :
                rain < 250f   ? "Arid desert (100–250mm/year)" :
                rain < 500f   ? "Semi-arid steppe (250–500mm/year)" :
                rain < 1000f  ? "Dry sub-humid / grassland (500–1000mm/year)" :
                rain < 2000f  ? "Humid temperate (1000–2000mm/year)" :
                rain < 3000f  ? "Wet temperate / monsoon fringe (2000–3000mm/year)" :
                rain < 5000f  ? "Rainforest / very wet (3000–5000mm/year)" :
                rain < 8000f  ? "Mountain rainforest / orographic wet (5000–8000mm/year)" :
                rain < 10000f ? "Extreme orographic (8000–10000mm/year)" :
                                "Ultra-extreme orographic (10000–12000mm+/year)";

            return string.Format(
                "{0}: {1}mm/year\n{2}\nEarth-scale precipitation (0–12000mm visualization range)",
                "Rainfall".Translate(), rain.ToString("F0"), classification);
        }

        private static string TooltipAridity(int tile)
        {
            float aridityIndex = Climate.Aridity(tile);
            string classification = aridityIndex < 0.2f ? "Hyper-arid" :
                                    aridityIndex < 0.5f ? "Arid" :
                                    aridityIndex < 0.65f ? "Semi-arid" :
                                    aridityIndex < 1.0f ? "Sub-humid" :
                                    aridityIndex < 2.0f ? "Humid" :
                                    aridityIndex < 4.0f ? "Very humid" : "Extremely humid";
            return string.Format("Aridity Index: {0:F2}\n{1}\nRainfall: {2:F0}mm",
                aridityIndex, classification, Climate.Rainfall(tile));
        }

        private static string TooltipContinentality(int tile)
        {
            float oceanDist = Climate.OceanDistKm(tile);
            string classification = oceanDist < 200f ? "Maritime" :
                                    oceanDist < 500f ? "Sub-maritime" :
                                    oceanDist < 1000f ? "Sub-continental" :
                                    oceanDist < 1500f ? "Continental" : "Extreme continental";
            return string.Format("Distance from Ocean: {0:F0}km\n{1}", oceanDist, classification);
        }

        private static string TooltipSeasonalAmplitude(int tile)
        {
            float amplitude = Climate.SeasonalAmp(tile);
            string severity = amplitude < 5f ? "Minimal" :
                              amplitude < 15f ? "Low" :
                              amplitude < 35f ? "Moderate" :
                              amplitude < 70f ? "High" :
                              amplitude < 120f ? "Very high" : "Extreme";
            return string.Format(
                "Seasonal temperature variation (full annual range): {0:F1}°C\n{1} seasonal variation (0–160°C scale)",
                amplitude, severity);
        }

        private static string TooltipMonsoon(int tile)
        {
            float boost = Climate.MonsoonBoost(tile);
            bool hasMonsoon = Climate.HasMonsoon(tile);
            string classification = boost < 50f ? "No monsoon" :
                                    boost < 250f ? "Weak monsoon" :
                                    boost < 600f ? "Moderate monsoon" :
                                    boost < 1200f ? "Strong monsoon" :
                                    boost < 1800f ? "Very strong monsoon" : "Extreme monsoon";
            return string.Format(
                "Summer monsoon excess: {0:F0}mm/year\n{1}\n{2} (0–2000mm scale)",
                boost, classification, hasMonsoon ? "Monsoon zone" : "Non-monsoon");
        }

        private static string TooltipHilliness(int tile)
        {
            Hilliness hilliness = Find.WorldGrid[tile].hilliness;
            string description = hilliness switch
            {
                Hilliness.Flat => "Flat terrain - easy movement, good for farming",
                Hilliness.SmallHills => "Small hills - minor movement penalties",
                Hilliness.LargeHills => "Large hills - moderate movement penalties, mining opportunities",
                Hilliness.Mountainous => "Mountainous - significant movement penalties, rich deposits",
                Hilliness.Impassable => "Impassable mountains - cannot be settled",
                _ => "Unknown terrain"
            };
            return string.Format("Hilliness: {0}\n{1}", hilliness.GetLabel(), description);
        }

        private static string TooltipWetlands(int tile)
        {
            if (TryClimateValue(tile, c => c.Swampiness, out float intensity))
            {
                string classification = intensity < 0.2f ? "Dry" :
                                        intensity < EarthScaleConstants.WETLAND_MIN_INTENSITY ? "Slightly Moist" :
                                        intensity < EarthScaleConstants.WETLAND_HIGH_INTENSITY ? "Wetland" :
                                        intensity < EarthScaleConstants.WETLAND_CLUSTER_INTENSITY ? "Strong Wetland" :
                                        "Swamp/Marsh";
                return string.Format("Wetland Intensity: {0:F2}\nClassification: {1}\nBased on flow accumulation and climate",
                    intensity, classification);
            }
            return "No wetland data available";
        }

        private static string TooltipRiverSystems(int tile)
        {
            ClimateData cache = Find.World?.GetComponent<ClimateData>();
            if (cache?.RiverSystemIDTopology != null && tile >= 0 && tile < cache.RiverSystemIDTopology.Length)
            {
                int systemId = cache.RiverSystemIDTopology[tile];
                if (systemId >= 0)
                {
                    if (cache.DrainageTypeClassification != null && tile < cache.DrainageTypeClassification.Length)
                    {
                        DrainageType drainageType = cache.DrainageTypeClassification[tile];
                        string typeLabel = drainageType switch
                        {
                            DrainageType.ExorheicOcean => "Exorheic (Ocean-draining)",
                            DrainageType.EndorheicLake => "Endorheic (Lake-draining)",
                            DrainageType.FilledSpilling => "Endorheic (Filled depression, spills)",
                            DrainageType.FilledTerminal => "Endorheic (Terminal sink)",
                            _ => "Unknown"
                        };
                        string description = drainageType switch
                        {
                            DrainageType.ExorheicOcean => "Water ultimately flows to the ocean",
                            DrainageType.EndorheicLake => "Water flows to an inland lake surrounded by land",
                            DrainageType.FilledSpilling => "Water fills depression and spills to adjacent basin (playa lake)",
                            DrainageType.FilledTerminal => "Water collects in closed basin with no outlet (salt flat/dry lake)",
                            _ => ""
                        };
                        return string.Format("River System #{0} (Aggregated Basin)\nType: {1}\n{2}\nMultiple elementary basins merged into major watershed",
                            systemId, typeLabel, description);
                    }

                    bool isEndorheic = cache.EndorheicMask != null && tile < cache.EndorheicMask.Length && cache.EndorheicMask[tile];
                    string basinType = isEndorheic ? "Endorheic (Closed)" : "Exorheic (Ocean-draining)";
                    string fallbackDesc = isEndorheic ? "Water flows to internal lakes or salt flats" : "Water ultimately flows to the ocean";
                    return string.Format("River System #{0} (Aggregated Basin)\nType: {1}\n{2}\nMultiple elementary basins merged into major watershed",
                        systemId, basinType, fallbackDesc);
                }
            }
            return "No river system data available";
        }
    }
}
