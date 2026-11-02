using RimWorld.Planet;
using UnityEngine;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Custom
{
    /// <summary>
    /// Cold Desert (Realistic Planets) - Cold arid deserts (Gobi, Patagonian, Great Basin).
    /// 
    /// Primary: Cold temperate (-2.5°C to +12.5°C) + Arid (0.0-0.16 AI, UNEP <0.20 band).
    /// Elevation: Single - 1000±1500m (highlands/plateaus preferred, but flexible).
    /// Hard Gates: Aridity < 0.20 (arid requirement).
    /// Secondary: +15% continentality, +10% high elevation (>1500m).
    /// Target Coverage: 3-5% land.
    /// 
    /// Note: Represents BWk Köppen classification (Cold Desert). Distinguished from hot deserts
    /// (BWh) by lower temperatures and from semi-arid (BSh/BSk) by lower precipitation.
    /// </summary>
    public class ColdDesert : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 8.5f;
        protected override float TempTolerance => 6.5f;      // 2°C to 15°C
        protected override float IdealAridityIndex => 0.11f; // UNEP arid band center
        protected override float AridityTolerance => 0.08f; // 0.03-0.19 AI
        
        // Single elevation: highlands/plateaus preferred, but flexible
        protected override float IdealElevation => 1000f;
        protected override float ElevationTolerance => 1400f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "-8°C <= AnnualTemp <= 17°C, SummerMax > 2°C, Aridity < 0.24";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            float ai = s.Aridity;
            float temp = s.AnnualTemp;
            if (ai >= 0.24f)
                return false;
            // Must thaw in summer and sit in the cold-temperate/subpolar belt (not on polar ice caps or hot deserts)
            if (s.SummerMax <= 2f || temp < -8f || temp > 17f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float continentality = s.Continentality;
            float leeward = s.Leeward;
            
            // Strong orthogonal: continentality (interior cold deserts)
            totalBonus += continentality * 0.15f;
            
            // Moderate orthogonal: high elevation preference
            if (s.Elevation > 1000f)
                totalBonus += 0.10f;  // +10%
            
            // Strong orthogonal: Leeward rain shadow
            if (leeward > 0.2f)
                totalBonus += Mathf.Min(leeward * 0.12f, 0.12f);
            
            return Mathf.Clamp(totalBonus, -0.5f, 0.5f);
        }
    }
}
