using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Tropical Swamp (Vanilla) - Hot tropical wetlands (Everglades, Pantanal, Sundarbans).
    /// 
    /// Primary: Tropical (21°C to 31°C) + very humid (ideal 3.0 AI). Wetter than the ideal is not a penalty.
    /// Elevation: Very Tight - 100±200m (coastal deltas, lowest of all biomes).
    /// Hard Gates: winterMin ≥ 18°C, swampiness OR wetlandIntensity ≥ 0.05.
    /// Secondary: +40% swampiness, +30% wetland, -50% non-wetland catastrophic penalty.
    /// Secondary Caps: +0.50 / -5.0 (catastrophic non-wetland penalty).
    /// Target Coverage: 0.5-1% land.
    /// </summary>
    public class TropicalSwamp : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 26f;
        protected override float TempTolerance => 5f;      // 21°C to 31°C
        protected override float IdealAridityIndex => 3.0f; // Wetter than rainforest
        protected override float AridityTolerance => 1.0f; // 2.0 AI and up; wetter than 3.0 is not a penalty
        protected override bool OneSidedWetMoisture => true;
        
        // Very tight elevation - coastal deltas
        protected override float IdealElevation => 100f;
        protected override float ElevationTolerance => 200f; // 0-300m range
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "WinterMin >= 18°C, Swampiness >= 0.05";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            // Tropical warmth gate: winterMin ≥ 18°C (Köppen A standard)
            if (s.WinterMin < 18f)
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
            
            // CRITICAL orthogonal: swampiness (KEY differentiator from rainforest)
            // Slightly reduced so rainforest can reclaim part of humid tropical space.
            percentage += swampiness * 0.44f; // Up to +44%
            
            // Moderate orthogonal: Flow accumulation (river-fed swamps)
            // Tropical swamps often form in river deltas and floodplains
            if (flowAccumulation > 10f)
                percentage += Mathf.Min(flowAccumulation / 100f, 0.08f);  // Up to +8%
            
            // CATASTROPHIC non-wetland penalty
            if (swampiness < 0.05f)
                percentage -= 0.50f;  // -50% (but cap allows down to -500%)
            
            return percentage;
        }
    }
}
