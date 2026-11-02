using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;
using Planets.Utilities.Random;

namespace Planets.Biomes.Odyssey
{
    /// <summary>
    /// Glacial Plain (Odyssey DLC - HOST-RESTRICTED) - Antarctic/Greenland-style ice plateaus.
    /// 
    /// Primary: Cold polar (-16°C to 0°C) + Moderate precipitation (250-750mm, enough for glaciation).
    /// Elevation: Equal Dual - Polar lowlands 500±400m OR Alpine peaks 4000±1500m (equal weight).
    /// Hard Gates: Host=Tundra/IceSheet, vanilla Odyssey noise ≥ 0.9.
    /// Secondary: +30-50% extreme cold, +40% seasonality (>35°C amplitude), +100% vanilla noise bonus.
    /// Secondary Caps: +3.0 / -5.0 (host-restricted biome).
    /// Target Coverage: 0.1-0.3% land (ultra-rare).
    /// Noise: Vanilla Odyssey Perlin(freq=0.3, lac=0.4, pers=0.5, oct=5, seed=Hash(worldSeed, 44319114)), threshold 0.9.
    /// 
    /// Note: Uses absolute precipitation for moisture scoring (not AI) because polar regions
    /// have high AI (2-3) due to very low PET. Vanilla requires rainfall > 600; RP2 targets
    /// moderate precipitation (500mm ideal) to align with Tundra/IceSheet hosts while favoring
    /// the wetter end of polar zones where glaciation can occur.
    /// </summary>
    public class GlacialPlain : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => -8f;
        protected override float TempTolerance => 8f;      // -16°C to 0°C
        protected override float IdealAridityIndex => 0.20f; // Not used (cold biome uses precipitation)
        protected override float AridityTolerance => 0.20f; // Not used
        
        // Cold biome: use absolute precipitation instead of AI
        // Vanilla requires rainfall > 600; target wetter end of polar zones for glaciation
        protected override bool UseRainfallMoistureAxis => true;
        protected override float IdealPrecipitation => 500f; // Moderate precipitation (glaciation)
        protected override float PrecipitationTolerance => 250f; // 250-750mm range
        
        // Equal dual elevation: Polar lowlands OR Alpine peaks
        protected override bool UseDualElevation => true;
        protected override float IdealElevation => 500f;       // Polar lowlands (ice age highlands)
        protected override float ElevationTolerance => 400f;
        protected override float IdealElevation2 => 4000f;     // Alpine peaks
        protected override float ElevationTolerance2 => 1500f;
        protected override float ElevationWeight1 => 1.0f;     // Equal weight
        protected override float ElevationWeight2 => 1.0f;     // Equal weight
        
        // ========== HOST RESTRICTION ==========
        
        protected override bool IsHostRestricted => true;
        protected override string[] AllowedHostBiomes => new[] { "Tundra", "IceSheet" };
        protected override float MinHostScore => 50f;
        
        // ========== SECONDARY BONUS CAPS ==========
        
        // Host-restricted biomes get extreme caps to compete with hosts
        protected override float SecondaryBonusMaxCap => 3.0f;
        protected override float SecondaryBonusMinCap => -5.0f;
        public override float DefaultNoiseThreshold => 0.9f;
        public override float DefaultNoiseBonus => 1.0f;
        public override float DefaultNoisePenalty => 1.5f;
        
        // ========== SECONDARY BONUSES ==========
        
        /// <summary>
        /// Vanilla Odyssey noise gate: Perlin(0.3, 0.4, 0.5, 5) >= 0.9.
        /// </summary>
        public override string DescribeHardGates() => "Host: Tundra or IceSheet (score >= 50), Vanilla Odyssey Perlin >= 0.9";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            int worldSeed = Find.World?.info?.Seed ?? 0;
            if (!PlacementNoiseHelper.PassesVanillaOdysseyThreshold(
                    s.TileID, worldSeed, PlacementNoiseHelper.VanillaGlacialPlainSeedPart,
                    0.3, 0.4, 0.5, 5, 0.9f))
                return false;
            return true;
        }
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float winterMin = s.WinterMin;
            float seasonalAmp = s.SeasonalAmp;
            
            // Climate derivative: extreme winter cold (KEY differentiator from Tundra)
            if (winterMin < -15f)
                totalBonus += 0.30f;  // +30%
            if (winterMin < -20f)
                totalBonus += 0.20f;  // +20% additional
            
            // Moderate orthogonal: high seasonal amplitude (continental)
            if (seasonalAmp > 35f)
                totalBonus += 0.40f;  // +40%
            
            // Vanilla Odyssey noise already enforced in PassesHardGates (threshold 0.9)
            // Tiles reaching here have passed the noise gate
            totalBonus += 1.0f;  // +100% bonus for tiles in valid noise clumps
            
            return totalBonus;
        }
    }
}
