using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;
using Planets.Core;
using Planets.WorldGen.Constants;

namespace Planets.Biomes.Custom
{
    /// <summary>
    /// Steppe (Realistic Planets) - Eurasian semi-arid temperate grasslands.
    /// 
    /// Primary: Temperate (-2°C to +15°C) + Semi-arid (0.25-0.55 AI, UNEP 0.20-0.50 band).
    /// Elevation: High - 1500±1000m (plateaus and highlands).
    /// Hard Gates: Aridity < 0.80 (semi-arid requirement).
    /// Secondary: +20% upland preference, -30% lowland penalty, +20% aridity > 0.35, -15% aridity < 0.25.
    /// Target Coverage: 5-7% land.
    /// </summary>
    public class Steppe : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 8f;
        protected override float TempTolerance => 6.5f;      // 1.5°C to 14.5°C
        protected override float IdealAridityIndex => 0.36f; // UNEP semi-arid band center
        protected override float AridityTolerance => 0.13f; // 0.23-0.49 AI
        
        // Plains, plateaus, and highlands
        protected override float IdealElevation => 800f;
        protected override float ElevationTolerance => 900f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "-3°C <= AnnualTemp <= 17.5°C, 0.20 <= Aridity < 0.56";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            float ai = s.Aridity;
            float temp = s.AnnualTemp;
            if (ai >= 0.56f || ai < 0.20f)
                return false;
            if (temp < -3f || temp > 17.5f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float aridity = s.Aridity;
            float continentality = s.Continentality;

            totalBonus += continentality * 0.18f;
            
            if (s.Elevation > 700f)
                totalBonus += 0.12f;
            
            if (aridity >= 0.25f && aridity <= 0.48f)
                totalBonus += 0.15f;
            
            return Mathf.Clamp(totalBonus, -0.5f, 0.5f);
        }
    }
}
