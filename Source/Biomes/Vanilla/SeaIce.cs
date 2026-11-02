using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.Utilities.Random;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Vanilla
{
    /// <summary>
    /// Sea Ice (Vanilla) - Permanent polar ocean ice.
    /// 
    /// Primary: Extreme cold (-37°C to -13°C) + AI/Precipitation IGNORED (temperature-only scoring).
    /// Elevation: N/A (water biome, elevation-agnostic).
    /// Hard Gates: summerMax < 0°C (never thaws), waterCovered (ocean only).
    /// Secondary: +12% extreme winter cold, smooth scoring transition near freezing point.
    /// Secondary Caps: +1.0 / -2.0.
    /// Target Coverage: ~6% of polar ocean.
    /// </summary>
    public class SeaIce : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => -35f;
        protected override float TempTolerance => 20f;      // -55°C to -15°C
        
        // Sea Ice relies on extreme temperature and ocean tiles; 
        // it ignores rainfall/aridity because polar oceans receive massive advected moisture.
        protected override float IdealAridityIndex => 0.1f; // IGNORED via MoistureSquaredDistance
        protected override float AridityTolerance => 10.0f; // IGNORED

        protected override float MoistureSquaredDistance(float annualTemp, float aridityIndex, float rainfall)
        {
            return 0f;
        }
        
        // Elevation: Sea level (water biome - elevation-agnostic)
        protected override float IdealElevation => 0f;
        protected override float ElevationTolerance => 0f;  // Ignored
        
        protected override bool AllowWaterTiles => true;    // WATER ONLY
        
        // ========== SECONDARY BONUS CAPS ==========
        
        // Sea Ice gets enhanced caps for sharp geographic edges
        protected override float SecondaryBonusMaxCap => 1.0f;
        protected override float SecondaryBonusMinCap => -2.0f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "WaterCovered, SummerMax < 0°C";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            float summerMax = s.SummerMax;
            
            // CRITICAL: Must never thaw
            if (summerMax >= 0f)
                return false;
            
            // Must be water tile
            if (!s.WaterCovered)
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float percentage = 0f;
            
            float winterMin = s.WinterMin;
            float summerMax = s.SummerMax;
            
            // Climate derivative: extreme winter cold
            if (winterMin < -35f)
                percentage += 0.12f;  // +12%
            
            // The isotherms are already naturally wavy due to planetary wave perturbations in the climate model.
            // We just need a smooth score falloff based on how deeply frozen the ocean is to ensure a clean ice cap.
            // (Note: summerMax >= 0 is a hard gate, so any tile here is already below freezing in summer)
            if (summerMax >= -1f)
            {
                // Borderline melting
                percentage -= 0.50f;
            }
            else if (summerMax <= -6f)
            {
                // Deep permanent ice
                percentage += 0.80f;
            }
            else
            {
                // Smooth transition between -1°C and -6°C
                float t = (summerMax - (-1f)) / (-5f); // 0 at -1, 1 at -6
                percentage += Mathf.Lerp(-0.50f, 0.80f, t);
            }
            
            return percentage;
        }
    }
}
