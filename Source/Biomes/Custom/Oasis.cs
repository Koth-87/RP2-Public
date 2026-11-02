using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.Core;
using Planets.WorldGen.Climate;
using Planets.Utilities.Random;

namespace Planets.Biomes.Custom
{
    /// <summary>
    /// Oasis (Realistic Planets - HOST-RESTRICTED) - Desert oases with water features.
    /// 
    /// Primary: Same as Desert (18°C to 34°C) + Arid (0.0-0.25 AI).
    /// Elevation: Tight - 300±400m (valley oases and basin springs).
    /// Hard Gates: Host=Desert (no hydrology hard gate - works with hydrology off).
    /// Secondary: DeterministicRng-based scattered singles (~0.5% base probability, up to ~2.5% in moist areas),
    ///             +30% wetland, +20% flow accumulation, +25% swampiness (modest bonuses - RNG gate controls rarity).
    /// Secondary Caps: +3.0 / -5.0 (host-restricted biome needs extreme bonuses).
    /// Target Coverage: ~1% of desert tiles (scattered single tiles, not clusters).
    /// </summary>
    public class Oasis : BiomeWorkerBase
    {
        // ========== PRIMARY AXES ==========
        
        protected override float IdealAnnualTemp => 26f;
        protected override float TempTolerance => 8f;      // 18°C to 34°C
        protected override float IdealAridityIndex => 0.1f;
        protected override float AridityTolerance => 0.15f; // 0.0-0.25 AI (slightly broader than Desert)
        
        // Tight elevation: valley oases and basin springs
        protected override float IdealElevation => 300f;
        protected override float ElevationTolerance => 400f;
        
        // ========== HOST RESTRICTION ==========
        
        protected override bool IsHostRestricted => true;
        protected override string[] AllowedHostBiomes => new[] { "Desert" };
        protected override float MinHostScore => 50f;
        
        // ========== SECONDARY BONUS CAPS ==========
        
        // Host-restricted biomes get extreme caps to compete with hosts
        protected override float SecondaryBonusMaxCap => 3.0f;
        protected override float SecondaryBonusMinCap => -5.0f;
        public override float DefaultNoisePassProbability => 0.005f;
        public override float DefaultNoiseBonus => 0.8f;
        public override float DefaultNoisePenalty => 1.2f;
        
        // ========== PLACEMENT ==========

        public override string DescribeHardGates() => "Host: Desert (score >= 50), 0.5%-2.5% deterministic rarity roll";

        /// <summary>Scattered singles: 0.5% base, up to about 2.5% where moisture or flow is already high.</summary>
        protected override bool PassesPlacementGate(Tile tile, in ClimateSample s)
        {
            float probability = BuiltInOasisProbability(in s);
            var meta = Controller.Settings?.biomeConfig?.GetMetadata(CurrentBiomeDefName);
            if (meta != null && meta.UseWorkerOverrides && meta.UseVarNoise)
                probability = meta.NoisePassProbability;

            Vector3 tileCenter = Find.WorldGrid.GetTileCenter(s.TileID);
            return DeterministicRng.PassesWithPosition(s.TileID, tileCenter, probability, DeterministicRng.Salts.OasisPlacement);
        }

        private static float BuiltInOasisProbability(in ClimateSample s)
        {
            float baseProbability = 0.005f;
            if (s.Swampiness > 0.05f) baseProbability = 0.02f;
            if (s.FlowAccumulation > 10f) baseProbability += 0.005f;
            return baseProbability;
        }

        // ========== SECONDARY BONUSES ==========
        
        protected override float CalculateSecondaryPercentage(Tile tile, in ClimateSample s)
        {
            // The rarity roll is PassesPlacementGate. Tiles that reach here get a modest moisture bonus.
            float wetlandIntensity = s.Swampiness;
            float flowAccumulation = s.FlowAccumulation;
            float totalBonus = wetlandIntensity * 0.3f;
            if (flowAccumulation > 5f) totalBonus += 0.2f;
            return totalBonus;
        }
    }
}
