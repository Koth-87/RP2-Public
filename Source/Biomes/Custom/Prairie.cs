using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Custom
{
    /// <summary>
    /// Prairie (Realistic Planets) - Grasslands clone when Odyssey NOT installed.
    /// 
    /// Identical parameters to Odyssey Grasslands.
    /// Only appears when !ModsConfig.OdysseyActive.
    /// 
    /// Primary: Temperate (5.5°C to 20.5°C) + Dry sub-humid to humid (0.38-0.82 AI).
    /// Elevation: Weighted Dual - Lowlands 600±800m (weight 1.0) + General 1000±1200m (weight 0.5).
    /// Hard Gates: !Odyssey, Aridity <= 1.0 (humid maximum - prevents dominance).
    /// Secondary: +25% flat terrain, +10% continentality.
    /// Target Coverage: Replaces Grasslands when Odyssey inactive.
    /// </summary>
    public class Prairie : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 13f;
        protected override float TempTolerance => 7.5f;     // 5.5°C to 20.5°C
        protected override float IdealAridityIndex => 0.60f;
        protected override float AridityTolerance => 0.22f; // 0.38-0.82 AI
        
        // Weighted dual elevation: Lowlands (preferred) + General fallback
        protected override bool UseDualElevation => true;
        protected override float IdealElevation => 600f;       // Lowlands
        protected override float ElevationTolerance => 800f;
        protected override float ElevationWeight1 => 1.0f;     // Primary preference
        protected override float IdealElevation2 => 1000f;     // General
        protected override float ElevationTolerance2 => 1200f;
        protected override float ElevationWeight2 => 0.5f;     // Fallback
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "Odyssey DLC inactive, Aridity <= 1.0";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            // Only appear when Odyssey is NOT active
            if (ModsConfig.OdysseyActive)
                return false;
            
            if (s.Aridity > 1.0f)
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float continentality = s.Continentality;
            
            // Strong orthogonal: flat terrain preference
            if (s.Hilliness == Hilliness.Flat)
                totalBonus += 0.25f;  // +25%
            
            // Weak orthogonal: continentality
            totalBonus += continentality * 0.10f; // Up to +10%
            
            return Mathf.Clamp(totalBonus, -0.5f, 0.5f);
        }
    }
}
