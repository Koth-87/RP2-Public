using RimWorld.Planet;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Ice Sheet (Vanilla) - Permanent polar ice (never thaws).
    /// 
    /// Primary: Extreme cold (-46°C to -14°C) + Low precipitation (<250mm, polar desert).
    /// Elevation: Equal Dual - Polar 500±1000m OR Alpine 5500±2000m (equal weight).
    /// Hard Gate: summerMax ≤ 0°C (NEVER THAWS - physical impossibility).
    /// Secondary: +12% extreme cold (<-40°C).
    /// Target Coverage: 0.5-1% land.
    /// 
    /// Note: Uses absolute precipitation for moisture scoring (not AI) because polar regions
    /// have high AI (2-3) due to very low PET, but are still deserts by absolute precipitation.
    /// </summary>
    public class IceSheet : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => -30f;
        protected override float TempTolerance => 16f;      // -46°C to -14°C
        protected override float IdealAridityIndex => 0.15f; // Used only when annual temperature is 0°C or above. Below 0°C, scoring uses IdealPrecipitation.
        protected override float AridityTolerance => 0.10f;
        
        // Cold biome: use absolute precipitation instead of AI
        protected override float IdealPrecipitation => 200f;
        protected override float PrecipitationTolerance => 350f;
        
        // Equal dual elevation: Polar lowlands OR alpine peaks
        protected override bool UseDualElevation => true;
        protected override float IdealElevation => 500f;        // Polar lowlands (Greenland/Antarctica)
        protected override float ElevationTolerance => 1200f;
        protected override float IdealElevation2 => 5500f;      // Alpine peaks (Kilimanjaro)
        protected override float ElevationTolerance2 => 2200f;
        protected override float ElevationWeight1 => 1.0f;      // Equal weight
        protected override float ElevationWeight2 => 1.0f;      // Equal weight
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "SummerMax <= 0°C (never thaws)";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            float summerMax = s.SummerMax;
            
            // CRITICAL: Must NEVER thaw - physical impossibility
            // Ice cannot persist if summer temps go above freezing
            if (summerMax > 0f)
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float percentage = 0f;
            
            float winterMin = s.WinterMin;
            
            // Climate derivative: extreme cold bonus
            if (winterMin < -40f)
                percentage += 0.12f;  // +12%
            
            return percentage;
        }
    }
}
