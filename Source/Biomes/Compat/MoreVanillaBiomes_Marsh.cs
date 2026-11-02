using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;

namespace Planets.Biomes.Compat.MoreVanillaBiomes
{
    /// <summary>
    /// RP2 compatibility worker for More Vanilla Biomes Marsh.
    /// Cool, wet, very swampy. Original: 11 + swampiness*5 + temp.
    /// Gates: temp [-10,14], rain>=600, swampiness>=0.5. Swampiness is dominant (5x multiplier).
    /// </summary>
    public class Marsh : BiomeWorkerBase
    {
        private static readonly FieldInfo SpawnField = AccessTools.Field("VanillaBiomes.BiomeSettings:spawnMarsh");

        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 4.0f;
        protected override float TempTolerance => 7.5f;
        protected override float IdealAridityIndex => 1.40f;
        protected override float AridityTolerance => 0.50f;
        protected override bool UseRainfallMoistureAxis => true;
        protected override float IdealPrecipitation => 1100f;
        protected override float PrecipitationTolerance => 500f;
        protected override bool OneSidedWetMoisture => true;

        protected override float IdealElevation => 300f;
        protected override float ElevationTolerance => 600f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "-10°C <= AnnualTemp <= 14°C, Rainfall >= 600mm, Swampiness >= 0.50";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            if (SpawnField != null && SpawnField.GetValue(null) is bool spawn && !spawn)
                return false;
            if (s.AnnualTemp < -10.0f)
                return false;
            if (s.AnnualTemp > 14.0f)
                return false;
            if (s.Rainfall < 600f)
                return false;
            if (s.Swampiness < 0.5f)
                return false;
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            // Very strong swampiness (original: swampiness*5, dominant factor)
            float swampiness = s.Swampiness;
            totalBonus += swampiness * 0.40f; // Up to +40%
            
            // Penalty for marginal swampiness (gate is 0.5, prefer well above)
            if (swampiness < 0.6f)
                totalBonus -= 0.15f; // -15%
            
            // Warm bias within range (original: +temp linear term)
            float winterMin = s.WinterMin;
            if (winterMin > -5f)
                totalBonus += 0.10f; // +10% for milder winters
            
            return totalBonus;
        }
    }
}
