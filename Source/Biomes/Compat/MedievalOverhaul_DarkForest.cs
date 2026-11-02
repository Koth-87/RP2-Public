using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Compat.MedievalOverhaul
{
    /// <summary>
    /// RP2 compatibility worker for Medieval Overhaul Dark Forest.
    /// Cold boreal forest variant. Original: flat score 40 (no variable scoring).
    /// Gates: temp [-10,10], rain>=1100. Claims all qualifying tiles equally.
    /// </summary>
    public class DarkForest : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 1.0f;
        protected override float TempTolerance => 6.5f;
        protected override float IdealAridityIndex => 1.50f;
        protected override float AridityTolerance => 0.50f;
        protected override bool UseRainfallMoistureAxis => true;
        protected override float IdealPrecipitation => 1400f;
        protected override float PrecipitationTolerance => 500f;
        protected override bool OneSidedWetMoisture => true;

        protected override float IdealElevation => 500f;
        protected override float ElevationTolerance => 900f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "-10°C <= AnnualTemp <= 10°C, Rainfall >= 1100mm";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (s.AnnualTemp < -10.0f)
                return false;
            if (s.AnnualTemp > 10.0f)
                return false;
            if (s.Rainfall < 1100.0f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            // Original has no variable scoring (flat 40) - minimal secondaries
            // Mild swampiness preference (dark gloomy forests)
            totalBonus += s.Swampiness * 0.10f; // Up to +10%
            
            return totalBonus;
        }
    }
}
