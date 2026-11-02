using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace Planets.Core.Configuration
{
    /// <summary>
    /// Unified metadata for a biome: placement (enabled, multiplier, offset), calibration (probe results),
    /// and user overrides (worker ideals, def stats). Replaces BiomeModifier and BiomeCalibrationData.
    /// </summary>
    public class BiomeMetadata : IExposable
    {
        // ---------- Placement (from legacy BiomeModifier) ----------
        public bool Enabled = true;
        public float CommonalityMultiplier = 1.0f;
        public float ScoreOffset = 0.0f;

        /// <summary>Name of per-biome preset loaded for this biome. Stored in profiles; used when saving/loading.</summary>
        public string PresetName;

        // ---------- Calibration (from BAACS probe) ----------
        public bool IsCalibrated;
        public float IdealTemp;
        public float IdealRain = 1000f;
        public float IdealElevation = 500f;
        public float IdealAridity = 1f;
        public float PeakScore;
        public float TempMin = 999f, TempMax = -999f;
        public float RainMin = 9999f, RainMax = -999f;

        // ---------- Tolerances (derived or user-set for GenericRP2Worker) ----------
        public float TempTolerance = 10f;
        public float RainTolerance = 500f;
        public float AridityTolerance = 0.4f;
        public float ElevationTolerance = 1000f;

        // ---------- User overrides ----------
        /// <summary>If true, use user ideals/tolerances for scoring (worker override or generic worker).</summary>
        public bool UseWorkerOverrides;

        /// <summary>For foreign (third-party) biomes: opt in to RP2 fit-energy scoring via <see cref="Biomes.Core.GenericRP2Worker"/>.</summary>
        public bool UseRP2ClimateFit;

        /// <summary>When <see cref="UseRP2ClimateFit"/> is enabled, ignore the foreign worker's own <c>GetScore &gt; 0</c> gate.</summary>
        public bool IgnoreForeignPlacementRules;

        public float UserIdealTemp = 15f;
        public float UserIdealRain = 1000f;
        public float UserIdealElevation = 500f;
        public float UserTempTolerance = 10f;
        public float UserRainTolerance = 500f;
        public float UserElevationTolerance = 1000f;

        // ---------- Dual elevation (when UseVarElevation and UseVarDualElevation) ----------
        public bool UseVarDualElevation = false;
        public float IdealElevation2 = 500f;
        public float ElevationTolerance2 = 1000f;
        public float UserIdealElevation2 = 500f;
        public float UserElevationTolerance2 = 1000f;
        public float UserElevationWeight1 = 1f;
        public float UserElevationWeight2 = 1f;

        /// <summary>If true, apply def overrides (density, etc.) at runtime.</summary>
        public bool UseDefOverrides;

        public float? AnimalDensity;
        public float? PlantDensity;
        public float? DiseaseMtbDays;
        public float? SettlementSelectionWeight;
        public float? CampSelectionWeight;
        public float? PollutionOffset;
        public float? Forageability;
        public float? WildPlantRegrowDays;
        public float? MovementDifficulty;
        public float? GeyserCountFactor;
        public float? WildAnimalScariaChance;
        public float? MaxFishPopulation;
        public bool? AllowRoads;
        public bool? AllowRivers;
        public bool? AllowFarmingCamps;
        public bool? IsExtremeBiome;

        public bool HasAnyDefOverride =>
            AnimalDensity.HasValue || PlantDensity.HasValue || DiseaseMtbDays.HasValue || SettlementSelectionWeight.HasValue
            || CampSelectionWeight.HasValue || PollutionOffset.HasValue || Forageability.HasValue || WildPlantRegrowDays.HasValue
            || MovementDifficulty.HasValue || GeyserCountFactor.HasValue || WildAnimalScariaChance.HasValue || MaxFishPopulation.HasValue
            || AllowRoads.HasValue || AllowRivers.HasValue || AllowFarmingCamps.HasValue || IsExtremeBiome.HasValue;

        public void ClearDefOverrides()
        {
            UseDefOverrides = false;
            AnimalDensity = null;
            PlantDensity = null;
            DiseaseMtbDays = null;
            SettlementSelectionWeight = null;
            CampSelectionWeight = null;
            PollutionOffset = null;
            Forageability = null;
            WildPlantRegrowDays = null;
            MovementDifficulty = null;
            GeyserCountFactor = null;
            WildAnimalScariaChance = null;
            MaxFishPopulation = null;
            AllowRoads = null;
            AllowRivers = null;
            AllowFarmingCamps = null;
            IsExtremeBiome = null;
        }

        // ---------- Per-variable "in use" flags (Option A: toggle off = not used in scoring) ----------
        public bool UseVarTemperature = true;
        public bool UseVarRainfall = false;
        public bool UseVarAridity = true;
        public bool UseVarElevation = true;
        public bool UseVarSwampiness = false;
        public bool UseVarWetlandIntensity = false;
        public bool UseVarHilliness = false;
        public bool UseVarContinentality = false;
        public bool UseVarSeasonality = false;
        public bool UseVarWindward = false;
        public bool UseVarLeeward = false;
        public bool UseVarFlowAccumulation = false;
        public bool UseVarSummerMax = false;
        public bool UseVarWinterMin = false;
        public bool UseVarMonsoonBoost = false;
        public bool UseVarNoise = false;

        // ---------- Secondary variable values (used when UseWorkerOverrides and corresponding UseVar* is true) ----------
        public float SwampinessIdeal = 0.5f;
        public float SwampinessTolerance = 0.3f;
        public float WetlandIntensityIdeal = 0.5f;
        public float WetlandIntensityTolerance = 0.3f;
        public int PreferredHilliness = 0; // 0=Flat, 1=SmallHills, 2=LargeHills, 3=Mountainous, 4=Impassable
        public float ContinentalityIdeal = 0.5f;
        public float ContinentalityTolerance = 0.3f;
        public float SeasonalityIdeal = 20f;
        public float SeasonalityTolerance = 10f;
        public float WindwardThreshold = 0.3f;
        public float WindwardScale = 0.12f;
        public float LeewardThreshold = 0.3f;
        public float LeewardScale = 0.12f;
        public float FlowAccumulationThreshold = 10f;
        public float FlowAccumulationScale = 0.001f;
        public float SummerMaxIdeal = 25f;
        public float SummerMaxTolerance = 10f;
        public float WinterMinIdeal = -5f;
        public float WinterMinTolerance = 10f;
        public float MonsoonBoostThreshold = 200f;
        public float MonsoonBoostScale = 0.1f;
        // Noise: host-restricted biomes (Oasis, GlacialPlain, etc.) and compat placement (threshold/bonus/penalty)
        public float NoiseThreshold = 0.9f;
        public float NoiseBonus = 0.8f;
        public float NoisePenalty = 1.5f;
        /// <summary>Pass probability (0-1) for percent-pass compat biomes (Desert Oasis, Ice Floes, Sandbar). Used when UseVarNoise and worker is percent-pass.</summary>
        public float NoisePassProbability = 0.01f;

        // ---------- Effective values (for scoring) ----------
        /// <summary>
        /// Fold a saved score offset into frequency, then clear the offset.
        /// +50 becomes about 4x frequency and -50 about a quarter. Calling this with an offset of 0 changes nothing,
        /// so a second load of an already-converted file does not compound.
        /// </summary>
        public void FoldScoreOffsetIntoFrequency()
        {
            if (Mathf.Abs(ScoreOffset) < 0.001f)
            {
                ScoreOffset = 0f;
                return;
            }

            float factor = Mathf.Exp(ScoreOffset / 36f);
            CommonalityMultiplier = Mathf.Clamp(CommonalityMultiplier * factor, 0f, 5f);
            ScoreOffset = 0f;
        }

        public float EffectiveIdealTemp => UseWorkerOverrides ? UserIdealTemp : (IsCalibrated ? IdealTemp : UserIdealTemp);
        public float EffectiveIdealRain => UseWorkerOverrides ? UserIdealRain : (IsCalibrated ? IdealRain : UserIdealRain);
        public float EffectiveIdealElevation => UseWorkerOverrides ? UserIdealElevation : (IsCalibrated ? IdealElevation : UserIdealElevation);
        public float EffectiveTempTolerance => UseWorkerOverrides ? UserTempTolerance : TempTolerance;
        public float EffectiveRainTolerance => UseWorkerOverrides ? UserRainTolerance : RainTolerance;
        public float EffectiveElevationTolerance => UseWorkerOverrides ? UserElevationTolerance : ElevationTolerance;
        public float EffectiveIdealElevation2 => UseWorkerOverrides ? UserIdealElevation2 : (IsCalibrated ? IdealElevation2 : UserIdealElevation2);
        public float EffectiveElevationTolerance2 => UseWorkerOverrides ? UserElevationTolerance2 : ElevationTolerance2;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "Enabled", true);
            Scribe_Values.Look(ref CommonalityMultiplier, "CommonalityMultiplier", 1.0f);
            Scribe_Values.Look(ref ScoreOffset, "ScoreOffset", 0.0f);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                FoldScoreOffsetIntoFrequency();
            Scribe_Values.Look(ref PresetName, "PresetName", null);

            Scribe_Values.Look(ref IsCalibrated, "IsCalibrated", false);
            Scribe_Values.Look(ref IdealTemp, "IdealTemp", 0f);
            Scribe_Values.Look(ref IdealRain, "IdealRain", 1000f);
            Scribe_Values.Look(ref IdealElevation, "IdealElevation", 500f);
            Scribe_Values.Look(ref IdealAridity, "IdealAridity", 1f);
            Scribe_Values.Look(ref PeakScore, "PeakScore", 0f);
            Scribe_Values.Look(ref TempMin, "TempMin", 999f);
            Scribe_Values.Look(ref TempMax, "TempMax", -999f);
            Scribe_Values.Look(ref RainMin, "RainMin", 9999f);
            Scribe_Values.Look(ref RainMax, "RainMax", -999f);

            Scribe_Values.Look(ref TempTolerance, "TempTolerance", 10f);
            Scribe_Values.Look(ref RainTolerance, "RainTolerance", 500f);
            Scribe_Values.Look(ref AridityTolerance, "AridityTolerance", 0.4f);
            Scribe_Values.Look(ref ElevationTolerance, "ElevationTolerance", 1000f);

            Scribe_Values.Look(ref UseWorkerOverrides, "UseWorkerOverrides", false);
            Scribe_Values.Look(ref UseRP2ClimateFit, "UseRP2ClimateFit", false);
            Scribe_Values.Look(ref IgnoreForeignPlacementRules, "IgnoreForeignPlacementRules", false);
            Scribe_Values.Look(ref UserIdealTemp, "UserIdealTemp", 15f);
            Scribe_Values.Look(ref UserIdealRain, "UserIdealRain", 1000f);
            Scribe_Values.Look(ref UserIdealElevation, "UserIdealElevation", 500f);
            Scribe_Values.Look(ref UserTempTolerance, "UserTempTolerance", 10f);
            Scribe_Values.Look(ref UserRainTolerance, "UserRainTolerance", 500f);
            Scribe_Values.Look(ref UserElevationTolerance, "UserElevationTolerance", 1000f);
            Scribe_Values.Look(ref UseVarDualElevation, "UseVarDualElevation", false);
            Scribe_Values.Look(ref IdealElevation2, "IdealElevation2", 500f);
            Scribe_Values.Look(ref ElevationTolerance2, "ElevationTolerance2", 1000f);
            Scribe_Values.Look(ref UserIdealElevation2, "UserIdealElevation2", 500f);
            Scribe_Values.Look(ref UserElevationTolerance2, "UserElevationTolerance2", 1000f);
            Scribe_Values.Look(ref UserElevationWeight1, "UserElevationWeight1", 1f);
            Scribe_Values.Look(ref UserElevationWeight2, "UserElevationWeight2", 1f);

            Scribe_Values.Look(ref UseDefOverrides, "UseDefOverrides", false);
            Scribe_Values.Look(ref AnimalDensity, "AnimalDensity");
            Scribe_Values.Look(ref PlantDensity, "PlantDensity");
            Scribe_Values.Look(ref DiseaseMtbDays, "DiseaseMtbDays");
            Scribe_Values.Look(ref SettlementSelectionWeight, "SettlementSelectionWeight");
            Scribe_Values.Look(ref CampSelectionWeight, "CampSelectionWeight");
            Scribe_Values.Look(ref PollutionOffset, "PollutionOffset");
            Scribe_Values.Look(ref Forageability, "Forageability");
            Scribe_Values.Look(ref WildPlantRegrowDays, "WildPlantRegrowDays");
            Scribe_Values.Look(ref MovementDifficulty, "MovementDifficulty");
            Scribe_Values.Look(ref GeyserCountFactor, "GeyserCountFactor");
            Scribe_Values.Look(ref WildAnimalScariaChance, "WildAnimalScariaChance");
            Scribe_Values.Look(ref MaxFishPopulation, "MaxFishPopulation");
            Scribe_Values.Look(ref AllowRoads, "AllowRoads");
            Scribe_Values.Look(ref AllowRivers, "AllowRivers");
            Scribe_Values.Look(ref AllowFarmingCamps, "AllowFarmingCamps");
            Scribe_Values.Look(ref IsExtremeBiome, "IsExtremeBiome");

            Scribe_Values.Look(ref UseVarTemperature, "UseVarTemperature", true);
            Scribe_Values.Look(ref UseVarRainfall, "UseVarRainfall", false);
            Scribe_Values.Look(ref UseVarAridity, "UseVarAridity", true);
            Scribe_Values.Look(ref UseVarElevation, "UseVarElevation", true);
            Scribe_Values.Look(ref UseVarSwampiness, "UseVarSwampiness", false);
            Scribe_Values.Look(ref UseVarWetlandIntensity, "UseVarWetlandIntensity", false);
            Scribe_Values.Look(ref UseVarHilliness, "UseVarHilliness", false);
            Scribe_Values.Look(ref UseVarContinentality, "UseVarContinentality", false);
            Scribe_Values.Look(ref UseVarSeasonality, "UseVarSeasonality", false);
            Scribe_Values.Look(ref UseVarWindward, "UseVarWindward", false);
            Scribe_Values.Look(ref UseVarLeeward, "UseVarLeeward", false);
            Scribe_Values.Look(ref UseVarFlowAccumulation, "UseVarFlowAccumulation", false);
            Scribe_Values.Look(ref UseVarSummerMax, "UseVarSummerMax", false);
            Scribe_Values.Look(ref UseVarWinterMin, "UseVarWinterMin", false);
            Scribe_Values.Look(ref UseVarMonsoonBoost, "UseVarMonsoonBoost", false);
            Scribe_Values.Look(ref UseVarNoise, "UseVarNoise", false);

            Scribe_Values.Look(ref SwampinessIdeal, "SwampinessIdeal", 0.5f);
            Scribe_Values.Look(ref SwampinessTolerance, "SwampinessTolerance", 0.3f);
            Scribe_Values.Look(ref WetlandIntensityIdeal, "WetlandIntensityIdeal", 0.5f);
            Scribe_Values.Look(ref WetlandIntensityTolerance, "WetlandIntensityTolerance", 0.3f);
            Scribe_Values.Look(ref PreferredHilliness, "PreferredHilliness", 0);
            Scribe_Values.Look(ref ContinentalityIdeal, "ContinentalityIdeal", 0.5f);
            Scribe_Values.Look(ref ContinentalityTolerance, "ContinentalityTolerance", 0.3f);
            Scribe_Values.Look(ref SeasonalityIdeal, "SeasonalityIdeal", 20f);
            Scribe_Values.Look(ref SeasonalityTolerance, "SeasonalityTolerance", 10f);
            Scribe_Values.Look(ref WindwardThreshold, "WindwardThreshold", 0.3f);
            Scribe_Values.Look(ref WindwardScale, "WindwardScale", 0.12f);
            Scribe_Values.Look(ref LeewardThreshold, "LeewardThreshold", 0.3f);
            Scribe_Values.Look(ref LeewardScale, "LeewardScale", 0.12f);
            Scribe_Values.Look(ref FlowAccumulationThreshold, "FlowAccumulationThreshold", 10f);
            Scribe_Values.Look(ref FlowAccumulationScale, "FlowAccumulationScale", 0.001f);
            Scribe_Values.Look(ref SummerMaxIdeal, "SummerMaxIdeal", 25f);
            Scribe_Values.Look(ref SummerMaxTolerance, "SummerMaxTolerance", 10f);
            Scribe_Values.Look(ref WinterMinIdeal, "WinterMinIdeal", -5f);
            Scribe_Values.Look(ref WinterMinTolerance, "WinterMinTolerance", 10f);
            Scribe_Values.Look(ref MonsoonBoostThreshold, "MonsoonBoostThreshold", 200f);
            Scribe_Values.Look(ref MonsoonBoostScale, "MonsoonBoostScale", 0.1f);
            Scribe_Values.Look(ref NoiseThreshold, "NoiseThreshold", 0.9f);
            Scribe_Values.Look(ref NoiseBonus, "NoiseBonus", 0.8f);
            Scribe_Values.Look(ref NoisePenalty, "NoisePenalty", 1.5f);
            Scribe_Values.Look(ref NoisePassProbability, "NoisePassProbability", 0.01f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                MigrateWetlandIntensity();
        }

        /// <summary>
        /// Older configs used wetland intensity as the name of this axis. Swampiness replaced it.
        /// This runs when the metadata is loaded. Scoring only reads the result.
        /// </summary>
        public void MigrateWetlandIntensity()
        {
            if (!UseVarWetlandIntensity || UseVarSwampiness)
                return;

            UseVarSwampiness = true;
            if (SwampinessIdeal == 0.5f && SwampinessTolerance == 0.3f)
            {
                SwampinessIdeal = WetlandIntensityIdeal;
                SwampinessTolerance = WetlandIntensityTolerance;
            }
            UseVarWetlandIntensity = false;
        }

        /// <summary>Ideal used when wetland is on and swampiness is not. Matches the old load conversion.</summary>
        public float WetlandScoreIdeal =>
            SwampinessIdeal == 0.5f && SwampinessTolerance == 0.3f ? WetlandIntensityIdeal : SwampinessIdeal;

        /// <summary>Tolerance used when wetland is on and swampiness is not. Matches the old load conversion.</summary>
        public float WetlandScoreTolerance =>
            SwampinessIdeal == 0.5f && SwampinessTolerance == 0.3f ? WetlandIntensityTolerance : SwampinessTolerance;

        public BiomeMetadata Clone()
        {
            var m = new BiomeMetadata
            {
                Enabled = this.Enabled,
                CommonalityMultiplier = this.CommonalityMultiplier,
                ScoreOffset = this.ScoreOffset,
                PresetName = this.PresetName,
                IsCalibrated = this.IsCalibrated,
                IdealTemp = this.IdealTemp,
                IdealRain = this.IdealRain,
                IdealElevation = this.IdealElevation,
                IdealAridity = this.IdealAridity,
                PeakScore = this.PeakScore,
                TempMin = this.TempMin,
                TempMax = this.TempMax,
                RainMin = this.RainMin,
                RainMax = this.RainMax,
                TempTolerance = this.TempTolerance,
                RainTolerance = this.RainTolerance,
                AridityTolerance = this.AridityTolerance,
                ElevationTolerance = this.ElevationTolerance,
                UseWorkerOverrides = this.UseWorkerOverrides,
                UseRP2ClimateFit = this.UseRP2ClimateFit,
                IgnoreForeignPlacementRules = this.IgnoreForeignPlacementRules,
                UserIdealTemp = this.UserIdealTemp,
                UserIdealRain = this.UserIdealRain,
                UserIdealElevation = this.UserIdealElevation,
                UserTempTolerance = this.UserTempTolerance,
                UserRainTolerance = this.UserRainTolerance,
                UserElevationTolerance = this.UserElevationTolerance,
                UseVarDualElevation = this.UseVarDualElevation,
                IdealElevation2 = this.IdealElevation2,
                ElevationTolerance2 = this.ElevationTolerance2,
                UserIdealElevation2 = this.UserIdealElevation2,
                UserElevationTolerance2 = this.UserElevationTolerance2,
                UserElevationWeight1 = this.UserElevationWeight1,
                UserElevationWeight2 = this.UserElevationWeight2,
                UseDefOverrides = this.UseDefOverrides,
                AnimalDensity = this.AnimalDensity,
                PlantDensity = this.PlantDensity,
                DiseaseMtbDays = this.DiseaseMtbDays,
                SettlementSelectionWeight = this.SettlementSelectionWeight,
                CampSelectionWeight = this.CampSelectionWeight,
                PollutionOffset = this.PollutionOffset,
                Forageability = this.Forageability,
                WildPlantRegrowDays = this.WildPlantRegrowDays,
                MovementDifficulty = this.MovementDifficulty,
                GeyserCountFactor = this.GeyserCountFactor,
                WildAnimalScariaChance = this.WildAnimalScariaChance,
                MaxFishPopulation = this.MaxFishPopulation,
                AllowRoads = this.AllowRoads,
                AllowRivers = this.AllowRivers,
                AllowFarmingCamps = this.AllowFarmingCamps,
                IsExtremeBiome = this.IsExtremeBiome,
                UseVarTemperature = this.UseVarTemperature,
                UseVarRainfall = this.UseVarRainfall,
                UseVarAridity = this.UseVarAridity,
                UseVarElevation = this.UseVarElevation,
                UseVarSwampiness = this.UseVarSwampiness,
                UseVarWetlandIntensity = this.UseVarWetlandIntensity,
                UseVarHilliness = this.UseVarHilliness,
                UseVarContinentality = this.UseVarContinentality,
                UseVarSeasonality = this.UseVarSeasonality,
                UseVarWindward = this.UseVarWindward,
                UseVarLeeward = this.UseVarLeeward,
                UseVarFlowAccumulation = this.UseVarFlowAccumulation,
                UseVarSummerMax = this.UseVarSummerMax,
                UseVarWinterMin = this.UseVarWinterMin,
                UseVarMonsoonBoost = this.UseVarMonsoonBoost,
                UseVarNoise = this.UseVarNoise,
                SwampinessIdeal = this.SwampinessIdeal,
                SwampinessTolerance = this.SwampinessTolerance,
                WetlandIntensityIdeal = this.WetlandIntensityIdeal,
                WetlandIntensityTolerance = this.WetlandIntensityTolerance,
                PreferredHilliness = this.PreferredHilliness,
                ContinentalityIdeal = this.ContinentalityIdeal,
                ContinentalityTolerance = this.ContinentalityTolerance,
                SeasonalityIdeal = this.SeasonalityIdeal,
                SeasonalityTolerance = this.SeasonalityTolerance,
                WindwardThreshold = this.WindwardThreshold,
                WindwardScale = this.WindwardScale,
                LeewardThreshold = this.LeewardThreshold,
                LeewardScale = this.LeewardScale,
                FlowAccumulationThreshold = this.FlowAccumulationThreshold,
                FlowAccumulationScale = this.FlowAccumulationScale,
                SummerMaxIdeal = this.SummerMaxIdeal,
                SummerMaxTolerance = this.SummerMaxTolerance,
                WinterMinIdeal = this.WinterMinIdeal,
                WinterMinTolerance = this.WinterMinTolerance,
                MonsoonBoostThreshold = this.MonsoonBoostThreshold,
                MonsoonBoostScale = this.MonsoonBoostScale,
                NoiseThreshold = this.NoiseThreshold,
                NoiseBonus = this.NoiseBonus,
                NoisePenalty = this.NoisePenalty,
                NoisePassProbability = this.NoisePassProbability
            };
            return m;
        }

        /// <summary>Copy all fields from another BiomeMetadata (in-place). Used when loading per-biome presets.</summary>
        public void CopyFrom(BiomeMetadata other)
        {
            if (other == null) return;
            Enabled = other.Enabled;
            CommonalityMultiplier = other.CommonalityMultiplier;
            ScoreOffset = other.ScoreOffset;
            PresetName = other.PresetName;
            IsCalibrated = other.IsCalibrated;
            IdealTemp = other.IdealTemp;
            IdealRain = other.IdealRain;
            IdealElevation = other.IdealElevation;
            IdealAridity = other.IdealAridity;
            PeakScore = other.PeakScore;
            TempMin = other.TempMin;
            TempMax = other.TempMax;
            RainMin = other.RainMin;
            RainMax = other.RainMax;
            TempTolerance = other.TempTolerance;
            RainTolerance = other.RainTolerance;
            AridityTolerance = other.AridityTolerance;
            ElevationTolerance = other.ElevationTolerance;
            UseWorkerOverrides = other.UseWorkerOverrides;
            UseRP2ClimateFit = other.UseRP2ClimateFit;
            IgnoreForeignPlacementRules = other.IgnoreForeignPlacementRules;
            UserIdealTemp = other.UserIdealTemp;
            UserIdealRain = other.UserIdealRain;
            UserIdealElevation = other.UserIdealElevation;
            UserTempTolerance = other.UserTempTolerance;
            UserRainTolerance = other.UserRainTolerance;
            UserElevationTolerance = other.UserElevationTolerance;
            UseVarDualElevation = other.UseVarDualElevation;
            IdealElevation2 = other.IdealElevation2;
            ElevationTolerance2 = other.ElevationTolerance2;
            UserIdealElevation2 = other.UserIdealElevation2;
            UserElevationTolerance2 = other.UserElevationTolerance2;
            UserElevationWeight1 = other.UserElevationWeight1;
            UserElevationWeight2 = other.UserElevationWeight2;
            UseDefOverrides = other.UseDefOverrides;
            AnimalDensity = other.AnimalDensity;
            PlantDensity = other.PlantDensity;
            DiseaseMtbDays = other.DiseaseMtbDays;
            SettlementSelectionWeight = other.SettlementSelectionWeight;
            CampSelectionWeight = other.CampSelectionWeight;
            PollutionOffset = other.PollutionOffset;
            Forageability = other.Forageability;
            WildPlantRegrowDays = other.WildPlantRegrowDays;
            MovementDifficulty = other.MovementDifficulty;
            GeyserCountFactor = other.GeyserCountFactor;
            WildAnimalScariaChance = other.WildAnimalScariaChance;
            MaxFishPopulation = other.MaxFishPopulation;
            AllowRoads = other.AllowRoads;
            AllowRivers = other.AllowRivers;
            AllowFarmingCamps = other.AllowFarmingCamps;
            IsExtremeBiome = other.IsExtremeBiome;
            UseVarTemperature = other.UseVarTemperature;
            UseVarRainfall = other.UseVarRainfall;
            UseVarAridity = other.UseVarAridity;
            UseVarElevation = other.UseVarElevation;
            UseVarSwampiness = other.UseVarSwampiness;
            UseVarWetlandIntensity = other.UseVarWetlandIntensity;
            UseVarHilliness = other.UseVarHilliness;
            UseVarContinentality = other.UseVarContinentality;
            UseVarSeasonality = other.UseVarSeasonality;
            UseVarWindward = other.UseVarWindward;
            UseVarLeeward = other.UseVarLeeward;
            UseVarFlowAccumulation = other.UseVarFlowAccumulation;
            UseVarSummerMax = other.UseVarSummerMax;
            UseVarWinterMin = other.UseVarWinterMin;
            UseVarMonsoonBoost = other.UseVarMonsoonBoost;
            UseVarNoise = other.UseVarNoise;
            SwampinessIdeal = other.SwampinessIdeal;
            SwampinessTolerance = other.SwampinessTolerance;
            WetlandIntensityIdeal = other.WetlandIntensityIdeal;
            WetlandIntensityTolerance = other.WetlandIntensityTolerance;
            PreferredHilliness = other.PreferredHilliness;
            ContinentalityIdeal = other.ContinentalityIdeal;
            ContinentalityTolerance = other.ContinentalityTolerance;
            SeasonalityIdeal = other.SeasonalityIdeal;
            SeasonalityTolerance = other.SeasonalityTolerance;
            WindwardThreshold = other.WindwardThreshold;
            WindwardScale = other.WindwardScale;
            LeewardThreshold = other.LeewardThreshold;
            LeewardScale = other.LeewardScale;
            FlowAccumulationThreshold = other.FlowAccumulationThreshold;
            FlowAccumulationScale = other.FlowAccumulationScale;
            SummerMaxIdeal = other.SummerMaxIdeal;
            SummerMaxTolerance = other.SummerMaxTolerance;
            WinterMinIdeal = other.WinterMinIdeal;
            WinterMinTolerance = other.WinterMinTolerance;
            MonsoonBoostThreshold = other.MonsoonBoostThreshold;
            MonsoonBoostScale = other.MonsoonBoostScale;
            NoiseThreshold = other.NoiseThreshold;
            NoiseBonus = other.NoiseBonus;
            NoisePenalty = other.NoisePenalty;
            NoisePassProbability = other.NoisePassProbability;
            FoldScoreOffsetIntoFrequency();
        }

        /// <summary>
        /// Copy editor tuning from another metadata and leave this biome's checkbox, frequency, and preset name alone.
        /// Used when an Advanced Biomes profile loads the per-biome preset it links to.
        /// </summary>
        public void CopyCalibrationFrom(BiomeMetadata other)
        {
            if (other == null) return;
            bool enabled = Enabled;
            float frequency = CommonalityMultiplier;
            float offset = ScoreOffset;
            string presetName = PresetName;
            CopyFrom(other);
            Enabled = enabled;
            CommonalityMultiplier = frequency;
            ScoreOffset = offset;
            PresetName = presetName;
        }

        /// <summary>Create from legacy BiomeModifier for migration.</summary>
        public static BiomeMetadata FromModifier(BiomeConfiguration.BiomeModifier mod)
        {
            if (mod == null) return new BiomeMetadata();
            var meta = new BiomeMetadata
            {
                Enabled = mod.Enabled,
                CommonalityMultiplier = mod.CommonalityMultiplier,
                ScoreOffset = mod.ScoreOffset
            };
            meta.FoldScoreOffsetIntoFrequency();
            return meta;
        }
    }
}
