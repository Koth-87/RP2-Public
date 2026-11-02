using RimWorld.Planet;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Tundra (Vanilla) - Cold polar/subpolar with seasonal thaw.
    /// 
    /// Primary: Subpolar cold (-15°C to +5°C) + Low precipitation (200-400mm).
    /// Elevation: Equal Dual - Polar 200±400m OR Alpine 2200±1000m (equal weight).
    /// Hard Gates: summerMax 0-15°C (must thaw, but not too warm).
    /// Secondary: +15% summer thaw, +12% continentality.
    /// Target Coverage: 6-8% land.
    /// 
    /// Note: Uses absolute precipitation for moisture scoring (not AI) because polar regions
    /// have high AI (2-3) due to very low PET, but are still deserts by absolute precipitation.
    /// </summary>
    public class Tundra : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => -8f;
        protected override float TempTolerance => 8f;      // -16°C to 0°C
        protected override float IdealAridityIndex => 0.35f; // Not used (cold biome uses precipitation)
        protected override float AridityTolerance => 0.25f; // Not used
        
        // Cold biome: use absolute precipitation instead of AI
        protected override bool UseRainfallMoistureAxis => true;
        protected override bool OneSidedWetMoisture => true;
        protected override float IdealPrecipitation => 300f; // Low-to-moderate polar/subpolar precipitation
        protected override float PrecipitationTolerance => 350f;
        
        // Equal dual elevation: Arctic lowlands OR alpine tundra
        protected override bool UseDualElevation => true;
        protected override float IdealElevation => 200f;       // Polar: Arctic lowlands
        protected override float ElevationTolerance => 500f;
        protected override float IdealElevation2 => 2200f;     // Alpine: Alpine tundra
        protected override float ElevationTolerance2 => 1200f;
        protected override float ElevationWeight1 => 1.0f;     // Equal weight
        protected override float ElevationWeight2 => 1.0f;     // Equal weight
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "0°C < SummerMax < 11.5°C (thaws in summer, below tree line)";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            float summerMax = s.SummerMax;
            
            // Must thaw (vs Ice Sheet)
            if (summerMax <= 0f)
                return false;
            
            // Below the Boreal tree line (vs Boreal Forest)
            if (summerMax >= 11.5f)
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float percentage = 0f;
            
            float summerMax = s.SummerMax;
            float continentality = s.Continentality;
            
            // Climate derivative: summer thaw indicator (differentiates from Ice Sheet and Boreal)
            if (summerMax > 0f && summerMax < 10f)
                percentage += 0.18f;  // +18%
            
            // Weak orthogonal: continentality (interior tundra vs coastal)
            percentage += continentality * 0.12f;  // Up to +12%
            
            return percentage;
        }
    }
}
