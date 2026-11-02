using RimWorld.Planet;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Arid Shrubland (Vanilla) - Mediterranean scrublands, chaparral, foothills.
    /// 
    /// Primary: Warm temperate-subtropical (15.5°C to 26.5°C) + Semi-arid (0.25-0.45 AI, UNEP band).
    /// Elevation: Single - 500±1000m (foothills and plateaus).
    /// Hard Gates: Aridity < 0.70 (semi-arid requirement).
    /// Secondary: +20% leeward, +10% continentality.
    /// Target Coverage: 8-10% land.
    /// 
    /// Note: Focuses on semi-arid climates (BSh/BSk Köppen). Cold desert (BWk) is now
    /// handled by the dedicated Cold Desert biome.
    /// </summary>
    public class AridShrubland : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 22f;
        protected override float TempTolerance => 5.5f;      // 16.5°C to 27.5°C
        protected override float IdealAridityIndex => 0.34f; // UNEP semi-arid band center
        protected override float AridityTolerance => 0.13f; // 0.21-0.47 AI
        
        // Single elevation: foothills and plateaus
        protected override float IdealElevation => 500f;
        protected override float ElevationTolerance => 1000f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "AnnualTemp >= 13°C, 0.16 <= Aridity < 0.58";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            float ai = s.Aridity;
            if (ai >= 0.58f || ai < 0.16f)
                return false;
            if (s.AnnualTemp < 13f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float percentage = 0f;
            
            float leeward = s.Leeward;
            float continentality = s.Continentality;
            float aridity = s.Aridity;
            
            if (leeward > 0.2f)
                percentage += 0.14f;
            
            percentage += continentality * 0.08f;

            if (aridity >= 0.20f && aridity <= 0.48f)
                percentage += 0.16f;
            
            return percentage;
        }
    }
}
