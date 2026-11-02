using RimWorld.Planet;
using UnityEngine;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Boreal Forest (Vanilla) - Cold temperate humid forest (taiga).
    /// 
    /// Primary: Boreal (-6.5°C to +4.5°C) + Humid (0.40-1.20 AI, UNEP 0.65-2.0 band).
    /// Elevation: Single - 600±800m (hills and plateaus).
    /// Hard Gates: None (negative penalties handle extreme temperature mismatches).
    /// Secondary: Moderate cold/continental depth, with warm-side penalties to reduce bleed into temperate zones.
    /// Target Coverage: 8-10% land.
    /// </summary>
    public class BorealForest : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => -2f;
        protected override float TempTolerance => 6.0f;      // -8°C to +4°C
        protected override float IdealAridityIndex => 0.85f; // UNEP humid band center
        protected override float AridityTolerance => 0.38f;
        protected override bool OneSidedWetMoisture => true;
        
        // Single elevation: hills and plateaus
        protected override float IdealElevation => 500f;
        protected override float ElevationTolerance => 900f;

        // ========== HARD GATES ==========

        public override string DescribeHardGates() => "SummerMax >= 9.5°C (tree line), AnnualTemp <= 8°C, Aridity >= 0.35";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            // Köppen tree-line requirement (warmest month >= ~10°C) and cold-temperate ceiling
            if (s.SummerMax < 9.5f || s.AnnualTemp > 8f)
                return false;
            if (s.Aridity < 0.35f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float winterMin = s.WinterMin;
            float seasonalAmp = s.SeasonalAmp;
            float windward = s.Windward;
            float wetlandIntensity = s.Swampiness;
            float annualTemp = s.AnnualTemp;
            
            // Climate derivative: cold winters (KEY differentiator from Temperate Forest)
            if (winterMin < -8f)
                totalBonus += 0.15f;  // +15%
            
            // Moderate orthogonal: high seasonal amplitude (continental preference)
            if (seasonalAmp > 25f)
                totalBonus += 0.12f;  // +12%
            
            // Strong orthogonal: Windward slopes (orographic precipitation)
            if (windward > 0.3f)
                totalBonus += Mathf.Min(windward * 0.10f, 0.15f);
            
            // CRITICAL: Anti-wetland penalty (give space to Cold Bog)
            if (wetlandIntensity > 0.25f)
                totalBonus -= 0.22f;

            // Reduce Boreal dominance on warm edge transitions.
            if (annualTemp > 5f)
                totalBonus -= 0.20f;
            
            return Mathf.Clamp(totalBonus, -0.5f, 0.5f);
        }
    }
}
