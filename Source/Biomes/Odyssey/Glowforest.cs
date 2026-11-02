using RimWorld.Planet;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Odyssey
{
    /// <summary>
    /// Glowforest (Odyssey DLC) - Bioluminescent fantasy forest.
    /// 
    /// Vanilla behavior: climate-competitive (NOT host-restricted, NO noise).
    /// Vanilla gates: temp 10-55, rainfall >= 1000, swampiness >= 0.6.
    /// Vanilla score: 23 + (temp-7) + (rainfall-600)/220 + swampiness*3.
    /// 
    /// RP2 Translation:
    /// Primary: Warm temperate to subtropical (16°C to 30°C) + Humid (1.0-2.0 AI).
    /// Elevation: Single - 600±1000m (valleys and foothills).
    /// Hard Gates: swampiness >= 0.5 (relaxed from vanilla's 0.6 for RP2 climate model).
    /// Secondary: +35% low seasonality, strong swampiness bonus (up to +50%).
    /// NOT host-restricted - competes on climate + swampiness like vanilla.
    /// </summary>
    public class Glowforest : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 23f;
        protected override float TempTolerance => 7.0f;      // 16°C to 30°C
        protected override float IdealAridityIndex => 1.5f;
        protected override float AridityTolerance => 0.5f; // 1.0-2.0 AI (humid to very humid)
        
        // Single elevation: valleys and foothills
        protected override float IdealElevation => 600f;
        protected override float ElevationTolerance => 1000f;
        
        // NOT host-restricted - vanilla Glowforest is climate-competitive
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "Swampiness >= 0.58, Aridity >= 1.25";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (s.Swampiness < 0.58f || s.Aridity < 1.25f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float seasonalAmp = s.SeasonalAmp;
            
            // Low seasonality bonus favors stable humid zones
            if (seasonalAmp < 20f)
                totalBonus += 0.35f;  // +35%
            
            // Strong swampiness bonus - vanilla uses swampiness*3 as a major scoring term
            float swampiness = s.Swampiness;
            totalBonus += swampiness * 0.50f; // Up to +50%
            
            // Penalty for marginal swampiness (strong preference for very wet areas)
            if (swampiness < 0.6f)
                totalBonus -= 0.25f;  // -25%
            
            return totalBonus;
        }
    }
}
