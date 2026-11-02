using RimWorld.Planet;
using UnityEngine;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Custom
{
    /// <summary>
    /// Permafrost (Realistic Planets) - Extreme continental cold (Siberian plains + Tibetan Plateau).
    /// 
    /// Primary: Extreme subpolar (-16°C to -8°C) + Moderate precipitation (400-600mm).
    /// Elevation: Very Tight - 400±400m (valleys and basins, flat/low hilliness only).
    /// Hard Gates: annualTemp < -8°C (extreme cold distinction from Boreal Forest).
    /// Secondary: +20% continentality, +12% high amplitude (>35°C), +15% flat terrain, +10% poor drainage.
    /// Target Coverage: 2-3% land.
    /// 
    /// Note: Uses absolute precipitation for moisture scoring (not AI) because cold regions
    /// have high AI (2-3) due to very low PET, but Permafrost is still relatively dry
    /// by absolute precipitation standards (400-600mm, not truly humid).
    /// </summary>
    public class Permafrost : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => -12f;
        protected override float TempTolerance => 4f;      // -16°C to -8°C
        protected override float IdealAridityIndex => 1.0f; // Used only when annual temperature is 0°C or above. Below 0°C, scoring uses IdealPrecipitation.
        protected override float AridityTolerance => 0.4f;
        
        // Cold biome: use absolute precipitation instead of AI
        protected override float IdealPrecipitation => 500f; // Moderate precipitation (cold but not desert)
        protected override float PrecipitationTolerance => 120f; // Slightly broader cold-dry uptake
        
        // Very tight elevation: valleys and basins
        protected override float IdealElevation => 400f;
        protected override float ElevationTolerance => 400f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "AnnualTemp < -7.5°C, Flat or SmallHills, Swampiness >= 0.08 or Elevation < 550m";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (s.AnnualTemp >= -7.5f)
                return false;
            
            if (s.Hilliness != Hilliness.Flat && s.Hilliness != Hilliness.SmallHills)
                return false;
            
            float swampiness = s.Swampiness;
            if (swampiness < 0.08f && s.Elevation >= 550f)
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float percentage = 0f;
            
            float seasonalAmp = s.SeasonalAmp;
            float continentality = s.Continentality;
            float swampiness = s.Swampiness;
            
            // Strong orthogonal: Extreme continentality (Siberian interior - KEY differentiator)
            percentage += continentality * 0.22f; // Up to +22%
            
            // Moderate orthogonal: High seasonal amplitude (continental extremes)
            if (seasonalAmp > 35f)
                percentage += 0.12f;  // +12%
            
            // Strong orthogonal: Flat terrain preference (valleys, basins)
            if (s.Hilliness == Hilliness.Flat)
                percentage += 0.15f;  // +15%
            
            // Moderate orthogonal: Poor drainage (swampiness or low elevation)
            // Merged weight: 0.10 + 0.10 = 0.20×
            if (swampiness > 0.1f || s.Elevation < 500f)
                percentage += 0.20f;  // +20%
            
            return Mathf.Clamp(percentage, -0.5f, 0.5f);
        }
    }
}
