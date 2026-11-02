using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.WorldGen.Climate;
using Planets.Utilities.Random;

namespace Planets.Biomes.Odyssey
{
    /// <summary>
    /// Scarlands (Odyssey DLC - HOST-RESTRICTED) - Volcanic/geothermal badlands.
    /// 
    /// Primary: Same as Temperate Forest (4°C to 20°C) + Humid-ish (0.5-1.3 AI).
    /// Elevation: WEIGHTED DUAL - Lowlands (600±500m, weight 1.0) + General (900±900m, weight 0.6).
    /// Hard Gates: Host=TemperateForest (min 60% score); vanilla Odyssey noise ≥ 0.9 + neighbor check.
    /// Secondary: +30% moderate seasonality (18-28°C amplitude), vanilla noise clumping.
    /// Secondary Caps: +3.0 / -5.0 (host-restricted biome).
    /// Target Coverage: 0.1-0.3% land (ultra-rare - volcanic/geothermal badlands).
    /// Noise: Vanilla Odyssey Perlin(freq=0.2, lac=0.5, pers=0.1, oct=4, seed=Hash(worldSeed, 417235213)), threshold 0.9 + neighbor.
    /// </summary>
    public class Scarlands : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 12f;
        protected override float TempTolerance => 8f;      // 4°C to 20°C (same as Temperate Forest)
        protected override float IdealAridityIndex => 0.9f;
        protected override float AridityTolerance => 0.4f; // 0.5-1.3 AI (similar to Temperate Forest)
        
        // Elevation: WEIGHTED DUAL - Lowlands (600m ± 500m, weight 1.0) + General (900m ± 900m, weight 0.6)
        protected override bool UseDualElevation => true;
        protected override float IdealElevation => 600f;   // Lowlands (volcanic valleys)
        protected override float ElevationTolerance => 500f;
        protected override float ElevationWeight1 => 1.0f;  // Primary preference
        protected override float IdealElevation2 => 900f;  // General
        protected override float ElevationTolerance2 => 900f;
        protected override float ElevationWeight2 => 0.6f; // Moderate fallback
        
        // ========== HARD GATES ==========
        
        /// <summary>
        /// Reject tiles not in vanilla Odyssey noise clump, and reject isolated single tiles.
        /// Vanilla Scarlands uses Perlin(0.2, 0.5, 0.1, 4) with threshold 0.9 + neighbor check.
        /// </summary>
        public override string DescribeHardGates() => "Host: TemperateForest (score >= 60), Vanilla Scarlands noise + neighbor clump";

        protected override bool PassesHardGates(Tile tile, in ClimateSample s)
        {
            int worldSeed = Find.World?.info?.Seed ?? 0;
            // Must pass vanilla noise threshold AND have at least one neighbor that also passes
            if (!PlacementNoiseHelper.PassesVanillaScarlandsWithNeighbor(s.TileID, worldSeed))
                return false;
            return true;
        }
        
        // ========== HOST RESTRICTION ==========
        
        protected override bool IsHostRestricted => true;
        protected override string[] AllowedHostBiomes => new[] { "TemperateForest" };
        protected override float MinHostScore => 60f;
        
        // ========== SECONDARY BONUS CAPS ==========
        
        // Host-restricted biomes get extreme caps to compete with hosts
        protected override float SecondaryBonusMaxCap => 3.0f;
        protected override float SecondaryBonusMinCap => -5.0f;
        public override float DefaultNoiseThreshold => 0.9f;
        public override float DefaultNoiseBonus => 1.2f;
        public override float DefaultNoisePenalty => 1.5f;
        
        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            float totalBonus = 0f;
            
            float seasonalAmp = s.SeasonalAmp;
            
            // Moderate orthogonal: moderate seasonal amplitude (KEY differentiator)
            if (seasonalAmp > 18f && seasonalAmp < 28f)
                totalBonus += 0.30f;  // +30%
            
            // Vanilla Odyssey noise already enforced in PassesHardGates (threshold 0.9 + neighbor)
            // Tiles reaching here have passed the noise gate, so give a moderate bonus
            totalBonus += 1.2f;  // +120% bonus for tiles in valid noise clumps
            
            return totalBonus;
        }
    }
}
