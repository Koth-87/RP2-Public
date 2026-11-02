using RimWorld.Planet;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Temperate Forest (Vanilla) - Cool temperate humid forest.
    /// 
    /// Primary: Broad temperate (4°C to 20°C) + Humid/sub-humid (0.35-1.45 AI).
    /// Elevation: Single - 600±800m (hills and plateaus).
    /// Hard Gates: None.
    /// Secondary: -20% anti-wetland (swampiness>0.5), +12% seasonality (15-30°C amplitude), -15% very cold winters (<-8°C).
    /// Target Coverage: 20-25% land.
    /// </summary>
    public class TemperateForest : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 13f;
        protected override float TempTolerance => 6.5f;     // 6.5°C to 19.5°C
        protected override float IdealAridityIndex => 0.85f;
        protected override float AridityTolerance => 0.35f; // 0.50 AI and up; wetter than 0.85 is not a penalty
        protected override bool OneSidedWetMoisture => true;
        
        // Single elevation: hills and plateaus
        protected override float IdealElevation => 500f;
        protected override float ElevationTolerance => 800f;

        // ========== HARD GATES ==========

        public override string DescribeHardGates() => "AnnualTemp >= 1.5°C, WinterMin >= -16°C, Aridity >= 0.42";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            // Keep Temperate Forest out of subarctic/polar zones and semi-arid steppes/deserts
            if (s.AnnualTemp < 1.5f || s.WinterMin < -16f)
                return false;
            if (s.Aridity < 0.42f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float percentage = 0f;
            
            float winterMin = s.WinterMin;
            float seasonalAmp = s.SeasonalAmp;
            float windward = s.Windward;
            float monsoonBoost = s.MonsoonBoost;
            float annualTemp = s.AnnualTemp;
            float aridity = s.Aridity;
            
            // CRITICAL: Anti-wetland penalty (give space to Temperate Swamp)
            if (s.Swampiness > 0.5f)
                percentage -= 0.20f;  // -20%
            
            // Moderate orthogonal: Seasonality sweet spot
            if (seasonalAmp > 15f && seasonalAmp < 30f)
                percentage += 0.14f;  // +14%
            
            // Strong orthogonal: Windward slopes (orographic rainfall)
            // Temperate forests benefit from orographic precipitation on windward slopes
            if (windward > 0.3f)
                percentage += UnityEngine.Mathf.Min(windward * 0.12f, 0.15f);
            
            // Weak orthogonal: Moderate monsoon boost (if applicable)
            if (monsoonBoost > 150f && monsoonBoost < 800f)
                percentage += 0.05f;  // +5%

            // Stage 2 depth shaping: help temperate forests hold humid warm-mid transitions.
            if (annualTemp >= 7f && annualTemp <= 19f && aridity >= 0.55f)
                percentage += 0.12f;
            
            // CRITICAL: Anti-cold winter penalty (give space to Boreal Forest - per resizing report)
            if (winterMin < -8f)
                percentage -= 0.12f;  // -12%
            
            return percentage;
        }
    }
}
