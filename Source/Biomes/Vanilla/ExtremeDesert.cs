using RimWorld.Planet;
using UnityEngine;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Extreme Desert (Vanilla) - Hyper-arid core deserts (core Sahara, Atacama, Death Valley).
    /// 
    /// Primary: Dual elevation - Hot lowland (24-36°C) OR Cold highland (-5 to 15°C) + Hyper-arid (0.0-0.09 AI).
    /// Elevation: Equal Dual - Lowland 200±600m (Death Valley) OR Highland 3000±2000m (Atacama/Tibet).
    /// Hard Gates: Aridity < 0.10 (hyper-arid requirement).
    /// Secondary: +25% extreme heat (>40°C lowland), +40% continentality (>0.6), temperature coupling by elevation.
    /// Secondary Caps: +0.50 / -1.5 (rainfall penalty).
    /// Target Coverage: 1-2% land (rare).
    /// </summary>
    public class ExtremeDesert : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        // Dual elevation: Lowland (hot) OR Highland (cold)
        // Temperature coupling handled in secondary bonuses
        protected override float IdealAnnualTemp => 30f; // Base for lowland variant
        protected override float TempTolerance => 6f;    // Lowland: 24-36°C
        protected override float IdealAridityIndex => 0.04f; // UNEP hyper-arid band center
        protected override float AridityTolerance => 0.055f; // Slightly broader hyper-arid envelope
        
        // Equal dual elevation: Lowland (Death Valley) OR Highland (Atacama/Tibet)
        protected override bool UseDualElevation => true;
        protected override float IdealElevation => 200f;        // Lowland (Death Valley style)
        protected override float ElevationTolerance => 600f;
        protected override float ElevationWeight1 => 1.0f;     // Equal weight
        protected override float IdealElevation2 => 3000f;     // Highland (Atacama/Tibet style)
        protected override float ElevationTolerance2 => 2000f;
        protected override float ElevationWeight2 => 1.0f;     // Equal weight
        
        // ========== SECONDARY BONUS CAPS ==========
        
        // Extreme Desert gets enhanced rainfall penalty
        protected override float SecondaryBonusMinCap => -1.5f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "Aridity < 0.16";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (s.Aridity >= 0.16f)
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float annualTemp = s.AnnualTemp;
            float summerMax = s.SummerMax;
            float continentality = s.Continentality;
            float leeward = s.Leeward;
            float elevation = s.Elevation;
            
            // Temperature coupling by elevation:
            // Lowland variant (elevation < 1000m): Hot hyper-arid (25-35°C ideal)
            // Highland variant (elevation > 2000m): Cold hyper-arid (-5 to 15°C ideal)
            if (elevation < 1000f)
            {
                // Lowland: Hot hyper-arid (Death Valley style)
                // Temperature bonus for hot conditions
                if (annualTemp > 24f && annualTemp < 36f)
                    totalBonus += 0.20f;  // +20% for ideal lowland temp
                
                // Climate derivative: extreme heat (KEY differentiator from regular Desert)
                if (summerMax > 40f)
                    totalBonus += 0.25f;  // +25%
            }
            else if (elevation > 2000f)
            {
                // Highland: Cold hyper-arid (Atacama/Tibet style)
                // Temperature bonus for cold conditions
                if (annualTemp > -5f && annualTemp < 15f)
                    totalBonus += 0.20f;  // +20% for ideal highland temp
            }
            
            // Strong orthogonal: continentality (deep interior deserts)
            if (continentality > 0.6f)
                totalBonus += 0.50f;  // +50%
            
            // Strong orthogonal: Leeward rain shadow (KEY differentiator)
            // Extreme deserts are often in deep rain shadows
            if (leeward > 0.3f)
                totalBonus += leeward * 0.20f;  // Up to +20% (leeward 0-1)
            
            return totalBonus;
        }
    }
}
