using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Cold Bog (Vanilla) - Cold wetland (boreal muskeg, fens).
    /// 
    /// Primary: Cold boreal (-5°C to +9°C) + very humid (ideal 1.5 AI). Wetter than the ideal is not a penalty.
    /// Elevation: Tight - 600±500m (valleys and lowlands).
    /// Hard Gates: winterMin < 5°C, swampiness OR wetlandIntensity ≥ 0.05.
    /// Secondary: +30% swampiness, +25% wetland, -40% non-wetland penalty.
    /// Secondary Caps: +0.50 / -5.0 (catastrophic non-wetland penalty).
    /// Target Coverage: 1-2% land.
    /// </summary>
    public class ColdBog : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 2f;
        protected override float TempTolerance => 7f;      // -5°C to +9°C
        protected override float IdealAridityIndex => 1.5f; // UNEP humid/very humid band
        protected override float AridityTolerance => 0.5f; // 1.0 AI and up; wetter than 1.5 is not a penalty
        protected override bool OneSidedWetMoisture => true;
        
        // Tight elevation: valleys and lowlands
        protected override float IdealElevation => 600f;
        protected override float ElevationTolerance => 500f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "WinterMin < 3°C, Swampiness >= 0.05";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            // Cold climate requirement
            if (s.WinterMin >= 3f)
                return false;
            
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
            
            // CRITICAL orthogonal: swampiness (KEY differentiator from Boreal Forest)
            percentage += swampiness * 0.42f; // Up to +42%
            
            // Moderate orthogonal: Flow accumulation (river-fed bogs)
            // Cold bogs often form in poorly drained valleys with river systems
            if (flowAccumulation > 10f)
                percentage += Mathf.Min(flowAccumulation / 100f, 0.07f);  // Up to +7%
            
            // CATASTROPHIC non-wetland penalty
            if (swampiness < 0.05f)
                percentage -= 0.40f;  // -40% (but cap allows down to -500%)
            
            return percentage;
        }
    }
}
