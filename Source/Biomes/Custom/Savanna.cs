using RimWorld.Planet;
using UnityEngine;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Custom
{
    /// <summary>
    /// Savanna (Realistic Planets) - Tropical/subtropical grasslands with seasonal rainfall.
    /// 
    /// Primary: Tropical (22.8°C to 28.2°C) + Dry sub-humid (0.43-0.67 AI, UNEP 0.50-0.65 band).
    /// Elevation: Single - 500±800m (plateaus and plains).
    /// Hard Gate: winterMin ≥ 18°C (tropical warmth requirement).
    /// Secondary: +15% continentality, +30% seasonality (12-25°C amplitude).
    /// Target Coverage: 12-20% land.
    /// </summary>
    public class Savanna : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 26.0f;
        protected override float TempTolerance => 3.2f;      // 22.8°C to 29.2°C
        protected override float IdealAridityIndex => 0.90f; // Covers sub-humid to seasonal monsoon-humid Aw savanna
        protected override float AridityTolerance => 0.38f;
        
        // Single elevation: plateaus and plains
        protected override float IdealElevation => 450f;
        protected override float ElevationTolerance => 800f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "WinterMin >= 17.5°C, 0.42 <= Aridity <= 1.75";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (s.WinterMin < 17.5f)
                return false;
            float ai = s.Aridity;
            if (ai < 0.42f || ai > 1.75f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float continentality = s.Continentality;
            float monsoonBoost = s.MonsoonBoost;
            float driest = s.DriestSeasonRain;
            
            // Strong orthogonal: continentality (interior vs coastal)
            totalBonus += continentality * 0.12f;
            
            // KEY differentiator (Köppen Aw vs Af): pronounced dry season
            if (driest < 450f)
                totalBonus += 0.26f * Mathf.Clamp01((450f - driest) / 300f);
            else
                totalBonus -= 0.28f;
            
            // Monsoon boost (seasonal rainfall enhancement)
            if (monsoonBoost > 150f)
                totalBonus += 0.14f;

            if (s.Swampiness > 0.5f)
                totalBonus -= 0.25f;
            
            return Mathf.Clamp(totalBonus, -0.5f, 0.5f);
        }
    }
}
