using RimWorld.Planet;
using Verse;
using UnityEngine;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Odyssey
{
    /// <summary>
    /// Grasslands (Odyssey DLC) - Endless flat continental temperate grasslands.
    /// 
    /// Primary: Cool temperate (5.5°C to 20.5°C) + Dry sub-humid (0.38-0.82 AI).
    /// Elevation: Weighted Dual - Plains 600±800m (weight 1.0) OR General 1000±1200m (weight 0.5).
    /// Hard Gates: Aridity <= 1.0 (humid maximum - prevents dominance).
    /// Secondary: +25% flat terrain (KEY differentiator), +10% continentality.
    /// Target Coverage: 8-12% land.
    /// </summary>
    public class Grasslands : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 13f;
        protected override float TempTolerance => 7.5f;     // 5.5°C to 20.5°C (kept synchronized with Prairie)
        protected override float IdealAridityIndex => 0.60f;
        protected override float AridityTolerance => 0.22f; // 0.38-0.82 AI (dry sub-humid to humid)
        
        // Weighted dual elevation: Plains (preferred) + General fallback
        protected override bool UseDualElevation => true;
        protected override float IdealElevation => 600f;       // Plains
        protected override float ElevationTolerance => 800f;
        protected override float ElevationWeight1 => 1.0f;     // Primary preference
        protected override float IdealElevation2 => 1000f;     // General
        protected override float ElevationTolerance2 => 1200f;
        protected override float ElevationWeight2 => 0.5f;     // Fallback
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "Aridity <= 1.0";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (s.Aridity > 1.0f)
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float continentality = s.Continentality;
            
            // CRITICAL: Flat terrain bonus (HUGE - defining feature)
            if (s.Hilliness == Hilliness.Flat)
                totalBonus += 0.25f;  // +25%
            
            // Weak orthogonal: continentality
            totalBonus += continentality * 0.10f; // Up to +10%
            
            return Mathf.Clamp(totalBonus, -0.5f, 0.5f);
        }
    }
}
