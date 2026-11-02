using RimWorld.Planet;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Tropical Rainforest (Vanilla) - Hot equatorial rainforest (Amazon, Congo, Southeast Asia).
    /// 
    /// Primary: Tropical (22.5°C to 29.5°C) + very humid (ideal 2.3 AI). Wetter than the ideal is not a penalty.
    /// Elevation: Weighted Dual - Lowlands 250±350m (weight 1.0) OR Foothills 600±600m (weight 0.5).
    /// Hard Gate: winterMin ≥ 18°C (Köppen A climate standard).
    /// Secondary: +8% high rainfall, -30% anti-wetland (gives space to swamps).
    /// Target Coverage: 5-7% land.
    /// </summary>
    public class TropicalRainforest : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 26.5f;
        protected override float TempTolerance => 3.2f;    // 23.3°C to 29.7°C
        protected override float IdealAridityIndex => 1.85f;
        protected override float AridityTolerance => 0.60f;
        protected override bool OneSidedWetMoisture => true;
        
        // Weighted dual elevation: Strongly prefers lowlands, accepts foothills
        protected override bool UseDualElevation => true;
        protected override float IdealElevation => 250f;       // Lowlands (river valleys)
        protected override float ElevationTolerance => 350f;
        protected override float ElevationWeight1 => 1.0f;     // Full strength
        protected override float IdealElevation2 => 600f;      // Foothills fallback
        protected override float ElevationTolerance2 => 600f;
        protected override float ElevationWeight2 => 0.5f;     // Half strength
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "WinterMin >= 18°C, Aridity >= 0.85";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            // Tropical warmth gate: winterMin ≥ 18°C (Köppen A standard) and humid climate
            if (s.WinterMin < 18f)
                return false;
            if (s.Aridity < 0.85f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float percentage = 0f;
            
            float rainfall = s.Rainfall;
            float windward = s.Windward;
            float aridity = s.Aridity;
            float driest = s.DriestSeasonRain;
            
            // Climate derivative: High rainfall
            if (rainfall > 2000f)
                percentage += 0.10f;
            
            // Strong orthogonal: Windward slopes (orographic rainfall)
            if (windward > 0.3f)
                percentage += UnityEngine.Mathf.Min(windward * 0.12f, 0.15f);
            
            // Year-round wet vs dry-season differentiation (Köppen Af/Am vs Aw Savanna)
            if (driest >= 500f && aridity > 1.2f)
                percentage += 0.16f;
            else if (driest < 420f)
                percentage -= 0.35f * UnityEngine.Mathf.Clamp01((420f - driest) / 300f);
            
            // Anti-wetland penalty (give space to Tropical Swamp without full exclusion)
            if (s.Swampiness > 0.5f)
                percentage -= 0.24f;  // -24%
            
            return UnityEngine.Mathf.Clamp(percentage, -0.5f, 0.5f);
        }
    }
}
