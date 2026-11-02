using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;
using Planets.Utilities.Random;

namespace Planets.Biomes.Odyssey
{
    /// <summary>
    /// Lava Field (Odyssey DLC - HOST-RESTRICTED) - Active volcanic zones.
    /// 
    /// Primary: BROAD subtropical-hot (10°C to 34°C) + Hyper-arid to arid (0.0-0.23 AI).
    /// Elevation: LOOSE SINGLE - Highlands (2000±2500m, extremely broad, elevation-agnostic).
    /// Hard Gates: Host=Desert/ExtremeDesert/AridShrubland, Aridity < 0.40, vanilla Odyssey noise ≥ 0.9.
    /// Secondary: +20% extreme heat (>30°C summer), +90% vanilla noise clump bonus.
    /// Secondary Caps: +3.0 / -5.0 (host-restricted biome).
    /// Target Coverage: 0.05-0.1% land (ultra-rare - active volcanic zones).
    /// Noise: Vanilla Odyssey Perlin(freq=0.3, lac=0.5, pers=0.5, oct=4, seed=Hash(worldSeed, 967525656)), threshold 0.9.
    /// </summary>
    public class LavaField : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 22f;
        protected override float TempTolerance => 12f;     // 10°C to 34°C (BROAD - volcanic heat)
        protected override float IdealAridityIndex => 0.08f;
        protected override float AridityTolerance => 0.15f; // 0.0-0.23 AI (hyper-arid to arid)
        
        // Elevation: LOOSE SINGLE - Highlands (2000m ± 2500m, extremely broad, elevation-agnostic)
        protected override float IdealElevation => 2000f;
        protected override float ElevationTolerance => 2500f; // Very broad tolerance
        
        // ========== HOST RESTRICTION ==========
        
        protected override bool IsHostRestricted => true;
        protected override string[] AllowedHostBiomes => new[] { "Desert", "ExtremeDesert", "AridShrubland" };
        protected override float MinHostScore => 50f;
        
        // ========== SECONDARY BONUS CAPS ==========
        
        // Host-restricted biomes get extreme caps to compete with hosts
        protected override float SecondaryBonusMaxCap => 3.0f;
        protected override float SecondaryBonusMinCap => -5.0f;
        public override float DefaultNoiseThreshold => 0.9f;
        public override float DefaultNoiseBonus => 0.90f;
        public override float DefaultNoisePenalty => 1.5f;
        
        // ========== HARD GATES ==========
        
        public override string DescribeHardGates() => "Host: Desert/ExtremeDesert/AridShrubland (score >= 50), Aridity < 0.40, Vanilla Odyssey Perlin >= 0.9";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            // CRITICAL: Hyper-arid to arid requirement (physical impossibility)
            if (s.Aridity >= 0.40f)
                return false;
            
            // Vanilla Odyssey noise gate: Perlin(0.3, 0.5, 0.5, 4) >= 0.9
            int worldSeed = Find.World?.info?.Seed ?? 0;
            if (!PlacementNoiseHelper.PassesVanillaOdysseyThreshold(
                    s.TileID, worldSeed, PlacementNoiseHelper.VanillaLavaFieldSeedPart,
                    0.3, 0.5, 0.5, 4, 0.9f))
                return false;
            
            return true;
        }
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float summerMax = s.SummerMax;
            
            // Climate derivative: extreme heat (moderate bonus - KEY differentiator)
            if (summerMax > 30f)
                totalBonus += 0.20f;  // +20%
            
            // Vanilla Odyssey noise already enforced in PassesHardGates (threshold 0.9)
            // Tiles reaching here have passed the noise gate
            totalBonus += 0.90f;  // +90% bonus for tiles in valid noise clumps
            
            return totalBonus;
        }
    }
}
