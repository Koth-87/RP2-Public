using RimWorld.Planet;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Desert (Vanilla) - Hot subtropical deserts (Sahara, Arabian, Australian).
    /// 
    /// Primary: Subtropical-hot (18°C to 36°C) + Arid (0.03-0.17 AI, UNEP band).
    /// Elevation: Weighted Dual - Lowlands 200±600m (weight 1.0) + General 1500±1500m (weight 0.5).
    /// Hard Gates: Aridity < 0.30 (arid requirement), AnnualTemp >= 5°C (exclude cold deserts/polar dry zones).
    /// Secondary: Heat- and rain-shadow-weighted depth with cold penalties to prevent high-latitude leakage.
    /// Target Coverage: 10-12% land.
    /// </summary>
    public class Desert : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 27f;
        protected override float TempTolerance => 9f;      // 18°C to 36°C
        protected override float IdealAridityIndex => 0.1f; // UNEP arid band center
        protected override float AridityTolerance => 0.07f; // 0.03-0.17 AI
        
        // Weighted dual elevation: Lowlands (Sahara, Arabian style) + General fallback
        protected override bool UseDualElevation => true;
        protected override float IdealElevation => 200f;       // Lowlands
        protected override float ElevationTolerance => 600f;
        protected override float ElevationWeight1 => 1.0f;     // Primary preference
        protected override float IdealElevation2 => 1500f;     // General fallback
        protected override float ElevationTolerance2 => 1500f;
        protected override float ElevationWeight2 => 0.5f;     // Fallback
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "AnnualTemp >= 5°C, Aridity < 0.30";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (s.Aridity >= 0.30f)
                return false;

            if (s.AnnualTemp < 5f)
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float summerMax = s.SummerMax;
            float continentality = s.Continentality;
            float leeward = s.Leeward;
            float aridity = s.Aridity;
            float annualTemp = s.AnnualTemp;
            float winterMin = s.WinterMin;
            
            // Climate derivative: extreme heat (moderate bonus - KEY differentiator)
            if (summerMax > 35f)
                totalBonus += 0.16f;  // +16%
            
            // Strong orthogonal: continentality (interior deserts)
            // NOTE: NO latitude bonus (removed per design doc)
            totalBonus += continentality * 0.08f; // Up to +8%
            
            // Strong orthogonal: Leeward rain shadow (KEY differentiator)
            // Deserts are often in rain shadows behind mountain ranges
            if (leeward > 0.3f)
                totalBonus += leeward * 0.08f;  // Up to +8% (leeward 0-1)

            // Give the hyper-arid tail more room for ExtremeDesert.
            if (aridity < 0.08f)
                totalBonus -= 0.08f;

            // Keep hot-desert anchor out of cool/cold transition bins.
            if (annualTemp < 12f)
                totalBonus -= 0.15f;
            if (winterMin < -8f)
                totalBonus -= 0.15f;
            
            return totalBonus;
        }
    }
}
