using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Temperate Swamp (Vanilla) - Warm temperate wetland.
    /// 
    /// Primary: Warm temperate (8°C to 18°C) + very humid (ideal 1.8 AI). Wetter than the ideal is not a penalty.
    /// Elevation: Very Tight - 100±200m (coastal lowlands).
    /// Hard Gates: swampiness OR wetlandIntensity ≥ 0.05.
    /// Secondary: +45% swampiness, +35% wetland, -40% non-wetland penalty.
    /// Secondary Caps: +0.50 / -5.0 (catastrophic non-wetland penalty).
    /// Target Coverage: 0.5-1% land.
    /// </summary>
    public class TemperateSwamp : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 13f;
        protected override float TempTolerance => 5.0f;      // 8°C to 18°C
        protected override float IdealAridityIndex => 1.8f; // UNEP very humid band
        protected override float AridityTolerance => 0.50f; // 1.30 AI and up; wetter than 1.8 is not a penalty
        protected override bool OneSidedWetMoisture => true;
        
        // Very tight elevation: coastal lowlands
        protected override float IdealElevation => 100f;
        protected override float ElevationTolerance => 200f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "Swampiness >= 0.05";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            // Swamp minimum gate: requires SOME wetland character
            if (s.Swampiness < 0.05f)
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUS CAPS ==========
        
        // Swamps get catastrophic non-wetland penalty
        protected override float SecondaryBonusMinCap => -5.0f;
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float percentage = 0f;
            
            float swampiness = s.Swampiness;
            float flowAccumulation = s.FlowAccumulation;
            
            // CRITICAL orthogonal: swampiness (KEY differentiator from Temperate Forest)
            // Merged weight: 0.45 + 0.35 = 0.50× (rounded to 0.50 for consistency)
            percentage += swampiness * 0.50f; // Up to +50%
            
            // Moderate orthogonal: Flow accumulation (river-fed swamps)
            // Temperate swamps often form in river valleys and floodplains
            if (flowAccumulation > 10f)
                percentage += Mathf.Min(flowAccumulation / 100f, 0.10f);  // Up to +10%
            
            // CATASTROPHIC non-wetland penalty
            if (swampiness < 0.05f)
                percentage -= 0.40f;  // -40% (but cap allows down to -500%)
            
            return percentage;
        }
    }
}
