using System;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Random;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Core
{
    /// <summary>
    /// Base class for Realistic Planets 2 fit-energy biome workers.
    /// Evaluates candidate biomes on a 3D primary climate space (Temperature, Aridity/Precipitation, Elevation)
    /// plus discrete hard gates, placement gates, host restrictions, and bounded secondary modifiers.
    /// Inside two tolerances on every axis, <c>100 * exp(-Energy / 2)</c> equals the legacy Gaussian affinity score.
    /// </summary>
    public abstract class BiomeWorkerBase : BiomeWorker
    {
        // ========== PRIMARY AXES ==========

        protected abstract float IdealAnnualTemp { get; }
        protected abstract float TempTolerance { get; }
        protected abstract float IdealAridityIndex { get; }
        protected abstract float AridityTolerance { get; }

        // ========== PRECIPITATION MOISTURE AXIS ==========

        /// <summary>
        /// Ideal annual precipitation (mm/year) when scored on the precipitation axis (<c>annualTemp &lt; 0</c> or <see cref="UseRainfallMoistureAxis"/>).
        /// Negative if unused.
        /// </summary>
        protected virtual float IdealPrecipitation => -1f;
        protected virtual float PrecipitationTolerance => 100f;

        /// <summary>
        /// When true, this biome always uses the precipitation Gaussian on the moisture axis regardless of whether temperature is above 0°C.
        /// </summary>
        protected virtual bool UseRainfallMoistureAxis => false;

        public bool UsesRainfallMoistureAxis => UseRainfallMoistureAxis;

        // ========== ELEVATION AXIS ==========

        protected virtual float IdealElevation => 500f;
        protected virtual float ElevationTolerance => 1000f;

        protected virtual bool UseDualElevation => false;
        protected virtual float IdealElevation2 => 500f;
        protected virtual float ElevationTolerance2 => 1000f;
        protected virtual float ElevationWeight1 => 1.0f;
        protected virtual float ElevationWeight2 => 1.0f;

        // ========== GATES & PLACEMENT NOISE DEFAULTS ==========

        protected virtual bool PassesHardGates(Tile tile, int tileID) => true;

        protected virtual bool PassesHardGates(Tile tile, in ClimateSample s) => PassesHardGates(tile, s.TileID);

        protected virtual bool PassesPlacementGate(Tile tile, int tileID) => true;

        protected virtual bool PassesPlacementGate(Tile tile, in ClimateSample s) => PassesPlacementGate(tile, s.TileID);

        /// <summary>Human-readable summary of this biome's hard gates for the Biome Editor Worker tab (WS18-4).</summary>
        public virtual string DescribeHardGates() => "None";

        /// <summary>Built-in placement noise threshold for workers that use <see cref="PlacementNoiseHelper"/>.</summary>
        public virtual float DefaultNoiseThreshold => 0.9f;

        /// <summary>Built-in secondary bonus when placement noise passes.</summary>
        public virtual float DefaultNoiseBonus => 0.8f;

        /// <summary>Built-in secondary penalty when placement noise fails.</summary>
        public virtual float DefaultNoisePenalty => 1.5f;

        /// <summary>Built-in pass probability (0–1) for workers that use <see cref="DeterministicRng"/> rarity rolls.</summary>
        public virtual float DefaultNoisePassProbability => 0.01f;

        protected float EffectiveNoiseThreshold(float builtIn)
        {
            BiomeMetadata meta = Controller.Settings?.biomeConfig?.GetMetadata(CurrentBiomeDefName);
            return meta != null && meta.UseVarNoise ? meta.NoiseThreshold : builtIn;
        }

        protected bool PassesNoiseThreshold(string presetKey, int tileID, float builtInThreshold, bool passIfAbove)
        {
            float noise = PlacementNoiseHelper.GetNoiseValue(presetKey, tileID);
            float threshold = EffectiveNoiseThreshold(builtInThreshold);
            return passIfAbove ? noise >= threshold : noise < threshold;
        }

        protected virtual bool AllowWaterTiles => false;

        // ========== HOST-RESTRICTED BIOMES ==========

        protected virtual bool IsHostRestricted => false;
        protected virtual string[] AllowedHostBiomes => Array.Empty<string>();
        protected virtual float MinHostScore => 50f;

        // ========== SECONDARY BONUSES ==========

        protected virtual float SecondaryBonusMaxCap => 0.50f;
        protected virtual float SecondaryBonusMinCap => -0.50f;

        protected virtual float CalculateSecondaryPercentage(Tile tile, int tileID) => 0f;

        protected virtual float CalculateSecondaryPercentage(Tile tile, in ClimateSample s) => CalculateSecondaryPercentage(tile, s.TileID);

        /// <summary>
        /// True when this worker overrides <see cref="GetScore"/> with its own non-energy number scale.
        /// Selection normalizes positive scores against that worker's peak on the world.
        /// </summary>
        public virtual bool HasCustomScore => false;

        public bool PlacesOnWater => AllowWaterTiles;
        public bool RequiresHost => IsHostRestricted;

        /// <summary>
        /// Wet biomes set this so moisture distance is 0 whenever a tile is wetter than the ideal.
        /// </summary>
        protected virtual bool OneSidedWetMoisture => false;

        public void FillCalibrationDefaults(BiomeMetadata meta, string biomeDefName = null)
        {
            if (meta == null)
                return;

            meta.IdealTemp = IdealAnnualTemp;
            meta.TempTolerance = TempTolerance;
            meta.IdealElevation = IdealElevation;
            meta.ElevationTolerance = ElevationTolerance;
            if (UseDualElevation)
            {
                meta.UseVarDualElevation = true;
                meta.IdealElevation2 = IdealElevation2;
                meta.ElevationTolerance2 = ElevationTolerance2;
                meta.UserIdealElevation2 = IdealElevation2;
                meta.UserElevationTolerance2 = ElevationTolerance2;
                meta.UserElevationWeight1 = ElevationWeight1;
                meta.UserElevationWeight2 = ElevationWeight2;
            }
            meta.IdealAridity = IdealAridityIndex;
            meta.AridityTolerance = AridityTolerance;
            if (IdealPrecipitation >= 0f)
            {
                meta.IdealRain = IdealPrecipitation;
                meta.RainTolerance = PrecipitationTolerance;
            }
            else
            {
                meta.IdealRain = IdealAridityIndex * 400f;
                meta.RainTolerance = AridityTolerance * 200f;
            }
            meta.IsCalibrated = true;

            meta.NoiseThreshold = DefaultNoiseThreshold;
            meta.NoiseBonus = DefaultNoiseBonus;
            meta.NoisePenalty = DefaultNoisePenalty;
            meta.NoisePassProbability = DefaultNoisePassProbability;
        }

        protected virtual float MoistureSquaredDistance(float annualTemp, float aridityIndex, float rainfall)
        {
            bool useRainfall = (annualTemp < 0f || UseRainfallMoistureAxis) && IdealPrecipitation >= 0f;
            if (useRainfall)
            {
                if (OneSidedWetMoisture && rainfall >= IdealPrecipitation)
                    return 0f;
                return SquaredToleranceDistance(rainfall, IdealPrecipitation, PrecipitationTolerance);
            }

            if (OneSidedWetMoisture && aridityIndex >= IdealAridityIndex)
                return 0f;
            return SquaredToleranceDistance(aridityIndex, IdealAridityIndex, AridityTolerance);
        }

        protected virtual float ElevationSquaredDistance(float elevation)
        {
            if (UseDualElevation)
            {
                return DualSquaredDistance(
                    elevation,
                    IdealElevation, ElevationTolerance, ElevationWeight1,
                    IdealElevation2, ElevationTolerance2, ElevationWeight2);
            }

            return SquaredToleranceDistance(elevation, IdealElevation, ElevationTolerance);
        }

        protected float CalculateSecondaryFromMetadata(BiomeMetadata meta, Tile tile, in ClimateSample s, string biomeDefName)
        {
            if (meta == null)
                return 0f;
            float pct = 0f;
            int tileID = s.TileID;

            if (meta.UseVarSwampiness)
                pct += GaussianToPercentage(s.Swampiness, meta.SwampinessIdeal, meta.SwampinessTolerance, 0.35f);
            if (meta.UseVarHilliness)
                pct += ConditionalBonus((int)s.Hilliness == meta.PreferredHilliness, 0.2f);
            if (meta.UseVarContinentality)
                pct += GaussianToPercentage(s.Continentality, meta.ContinentalityIdeal, meta.ContinentalityTolerance, 0.2f);
            if (meta.UseVarSeasonality)
                pct += GaussianToPercentage(s.SeasonalAmp, meta.SeasonalityIdeal, meta.SeasonalityTolerance, 0.12f);
            if (meta.UseVarWindward)
                pct += ThresholdScaleToPercentage(s.Windward, meta.WindwardThreshold, meta.WindwardScale);
            if (meta.UseVarLeeward)
                pct += ThresholdScaleToPercentage(s.Leeward, meta.LeewardThreshold, meta.LeewardScale);
            if (meta.UseVarFlowAccumulation)
                pct += ThresholdScaleToPercentage(s.FlowAccumulation, meta.FlowAccumulationThreshold, meta.FlowAccumulationScale);
            if (meta.UseVarSummerMax)
                pct += GaussianToPercentage(s.SummerMax, meta.SummerMaxIdeal, meta.SummerMaxTolerance, 0.1f);
            if (meta.UseVarWinterMin)
                pct += GaussianToPercentage(s.WinterMin, meta.WinterMinIdeal, meta.WinterMinTolerance, 0.1f);
            if (meta.UseVarMonsoonBoost)
                pct += ThresholdScaleToPercentage(s.MonsoonBoost, meta.MonsoonBoostThreshold, meta.MonsoonBoostScale);

            if (meta.UseVarNoise && BiomeConditionRegistry.UsesNoise(biomeDefName))
                pct += EvaluateNoiseForMetadata(biomeDefName, tileID, meta);

            return pct;
        }

        private float EvaluateNoiseForMetadata(string biomeDefName, int tileID, BiomeMetadata meta)
        {
            int worldSeed = Find.World?.info?.Seed ?? 0;
            Vector3 tileCenter = Find.WorldGrid.GetTileCenter(tileID);

            if (biomeDefName == "GlacialPlain")
            {
                bool passes = PlacementNoiseHelper.PassesVanillaOdysseyThreshold(
                    tileID, worldSeed, PlacementNoiseHelper.VanillaGlacialPlainSeedPart,
                    0.3, 0.4, 0.5, 5, meta.NoiseThreshold);
                return passes ? meta.NoiseBonus : -meta.NoisePenalty;
            }
            if (biomeDefName == "LavaField")
            {
                bool passes = PlacementNoiseHelper.PassesVanillaOdysseyThreshold(
                    tileID, worldSeed, PlacementNoiseHelper.VanillaLavaFieldSeedPart,
                    0.3, 0.5, 0.5, 4, meta.NoiseThreshold);
                return passes ? meta.NoiseBonus : -meta.NoisePenalty;
            }
            if (biomeDefName == "Scarlands")
            {
                bool passes = PlacementNoiseHelper.PassesVanillaScarlandsWithNeighbor(tileID, worldSeed);
                return passes ? meta.NoiseBonus : -meta.NoisePenalty;
            }
            if (biomeDefName == "Oasis" || biomeDefName == "RP_Oasis")
            {
                bool passes = DeterministicRng.PassesWithPosition(tileID, tileCenter, meta.NoisePassProbability, DeterministicRng.Salts.GenericBiomeNoise);
                return passes ? meta.NoiseBonus : -meta.NoisePenalty;
            }

            return PlacementNoiseHelper.EvaluateThreshold(
                PlacementNoiseHelper.PresetWeirdness, tileID, meta.NoiseThreshold, true, meta.NoiseBonus, meta.NoisePenalty);
        }

        private static float GaussianToPercentage(float value, float ideal, float tolerance, float scale)
        {
            if (tolerance <= 0f)
                return value >= ideal - 0.001f && value <= ideal + 0.001f ? scale : 0f;
            float norm = Mathf.Abs(value - ideal) / tolerance;
            float affinity = norm <= 2f ? Mathf.Exp(-(norm * norm) / 2f) : 0f;
            return (affinity - 0.5f) * 2f * scale;
        }

        private static float ThresholdScaleToPercentage(float value, float threshold, float scale)
        {
            if (value < threshold)
                return 0f;
            return Mathf.Min((value - threshold) * scale, 0.15f);
        }

        // ========== MAIN SCORING METHOD ==========

        [ThreadStatic]
        private static string currentBiomeDefName;

        protected string CurrentBiomeDefName
        {
            get => currentBiomeDefName;
            set => currentBiomeDefName = value;
        }

        public BiomeFit EvaluateFit(BiomeDef biome, Tile tile, PlanetTile planetTile, bool ignoreGates = false)
        {
            ClimateSample sample = ClimateSample.FromTile(tile, planetTile.tileId);
            return EvaluateFit(biome, tile, planetTile, in sample, ignoreGates);
        }

        public BiomeFit EvaluateFit(BiomeDef biome, Tile tile, PlanetTile planetTile, in ClimateSample sample, bool ignoreGates = false)
        {
            CurrentBiomeDefName = biome?.defName;

            if (!AllowWaterTiles && sample.WaterCovered)
                return BiomeFit.Reject("water");

            if (!ignoreGates)
            {
                if (!PassesHardGates(tile, in sample))
                    return BiomeFit.Reject("gate");

                if (!PassesPlacementGate(tile, in sample))
                    return BiomeFit.Reject("placement");

                if (IsHostRestricted && AllowedHostBiomes.Length > 0 && !CheckHostViability(tile, planetTile))
                    return BiomeFit.Reject("host");
            }

            float annualTemp = sample.AnnualTemp;
            float aridityIndex = sample.Aridity;
            float rainfall = sample.Rainfall;
            float elevation = sample.Elevation;

            BiomeMetadata meta = Controller.Settings?.biomeConfig?.GetMetadata(biome?.defName);
            bool useOverrides = meta != null && (meta.UseWorkerOverrides || this is GenericRP2Worker);

            float wT = (useOverrides && meta.UseVarTemperature) ? 0.40f : (useOverrides ? 0f : 0.40f);
            float wM = (useOverrides && (meta.UseVarRainfall || meta.UseVarAridity)) ? 0.40f : (useOverrides ? 0f : 0.40f);
            float wE = (useOverrides && meta.UseVarElevation) ? 0.20f : (useOverrides ? 0f : 0.20f);
            float weightSum = wT + wM + wE;
            if (weightSum < 0.01f)
                return BiomeFit.Reject("axes");

            float eT = 0f;
            if (wT > 0f)
                eT = SquaredToleranceDistance(annualTemp, useOverrides ? meta.EffectiveIdealTemp : IdealAnnualTemp, useOverrides ? meta.EffectiveTempTolerance : TempTolerance);

            float eM = 0f;
            if (wM > 0f)
            {
                if (useOverrides)
                {
                    bool useRain = annualTemp < 0f || (meta.UseVarRainfall && !meta.UseVarAridity) || UseRainfallMoistureAxis;
                    if (useRain)
                    {
                        eM = SquaredToleranceDistance(rainfall, meta.EffectiveIdealRain, meta.EffectiveRainTolerance);
                        if (OneSidedWetMoisture && rainfall >= meta.EffectiveIdealRain)
                            eM = 0f;
                    }
                    else
                    {
                        eM = SquaredToleranceDistance(aridityIndex, meta.IdealAridity, meta.AridityTolerance);
                        if (OneSidedWetMoisture && aridityIndex >= meta.IdealAridity)
                            eM = 0f;
                    }
                }
                else
                {
                    eM = MoistureSquaredDistance(annualTemp, aridityIndex, rainfall);
                }
            }

            float eE = 0f;
            if (wE > 0f)
            {
                if (useOverrides && meta.UseVarDualElevation)
                {
                    eE = DualSquaredDistance(
                        elevation,
                        meta.EffectiveIdealElevation, meta.EffectiveElevationTolerance, meta.UserElevationWeight1,
                        meta.EffectiveIdealElevation2, meta.EffectiveElevationTolerance2, meta.UserElevationWeight2);
                }
                else if (useOverrides)
                {
                    eE = SquaredToleranceDistance(elevation, meta.EffectiveIdealElevation, meta.EffectiveElevationTolerance);
                }
                else
                {
                    eE = ElevationSquaredDistance(elevation);
                }
            }

            float primaryEnergy = (wT * eT + wM * eM + wE * eE) / weightSum;

            float secondary = (meta != null && meta.UseWorkerOverrides)
                ? CalculateSecondaryFromMetadata(meta, tile, in sample, biome.defName)
                : CalculateSecondaryPercentage(tile, in sample);
            secondary = Mathf.Clamp(secondary, SecondaryBonusMinCap, SecondaryBonusMaxCap);
            if (secondary <= -0.95f)
                return BiomeFit.Reject("secondary");

            float energy = primaryEnergy - 2f * Mathf.Log(1f + secondary);
            return BiomeFit.Accept(energy, primaryEnergy);
        }

        public override float GetScore(BiomeDef biome, Tile tile, PlanetTile planetTile)
        {
            BiomeFit fit = EvaluateFit(biome, tile, planetTile);
            return fit.Viable ? 100f * Mathf.Exp(-fit.Energy / 2f) : -100f;
        }

        private static float SquaredToleranceDistance(float value, float ideal, float tolerance)
        {
            if (tolerance <= 0f)
                return 0f;
            float normalized = (value - ideal) / tolerance;
            return normalized * normalized;
        }

        private static float DualSquaredDistance(
            float value,
            float ideal1, float tolerance1, float weight1,
            float ideal2, float tolerance2, float weight2)
        {
            float e1 = SquaredToleranceDistance(value, ideal1, tolerance1) - 2f * Mathf.Log(Mathf.Max(weight1, 0.01f));
            float e2 = SquaredToleranceDistance(value, ideal2, tolerance2) - 2f * Mathf.Log(Mathf.Max(weight2, 0.01f));
            return Mathf.Min(e1, e2);
        }

        protected float GetEffectiveIdealTemp(string defName)
        {
            BiomeMetadata meta = Controller.Settings?.biomeConfig?.GetMetadata(defName);
            return meta != null && meta.UseWorkerOverrides ? meta.EffectiveIdealTemp : IdealAnnualTemp;
        }

        protected float GetEffectiveTempTolerance(string defName)
        {
            BiomeMetadata meta = Controller.Settings?.biomeConfig?.GetMetadata(defName);
            return meta != null && meta.UseWorkerOverrides ? meta.EffectiveTempTolerance : TempTolerance;
        }

        /// <summary>
        /// Checks if any enabled, positive-frequency host biome scores at least <see cref="MinHostScore"/> at this tile.
        /// </summary>
        protected virtual bool CheckHostViability(Tile tile, PlanetTile planetTile)
        {
            BiomeConfiguration biomeConfig = Controller.Settings?.biomeConfig;
            foreach (string hostDefName in AllowedHostBiomes)
            {
                if (biomeConfig != null && (!biomeConfig.IsBiomeEnabled(hostDefName) || biomeConfig.GetCommonalityMultiplier(hostDefName) <= 0f))
                    continue;

                BiomeDef hostBiome = DefDatabase<BiomeDef>.GetNamedSilentFail(hostDefName);
                if (hostBiome?.Worker == null)
                    continue;

                float hostScore = hostBiome.Worker.GetScore(hostBiome, tile, planetTile);
                if (hostScore >= MinHostScore)
                    return true;
            }

            return false;
        }

        protected bool InRange(float value, float min, float max) => value >= min && value <= max;

        protected float ConditionalBonus(bool condition, float bonus) => condition ? bonus : 0f;
    }

    public struct BiomeFit
    {
        public bool Viable;
        public float Energy;
        public float PrimaryEnergy;
        public string RejectReason;

        public static BiomeFit Reject(string reason)
        {
            return new BiomeFit { Viable = false, Energy = float.MaxValue, PrimaryEnergy = float.MaxValue, RejectReason = reason };
        }

        public static BiomeFit Accept(float energy, float primaryEnergy)
        {
            return new BiomeFit { Viable = true, Energy = energy, PrimaryEnergy = primaryEnergy, RejectReason = null };
        }
    }
}
