using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate;

namespace Planets.WorldGen
{
    /// <summary>
    /// Encapsulates all world generation data in a validated, immutable structure.
    /// Prevents partial initialization and provides clear phase tracking.
    /// 
    /// **Design Pattern**: Data container with defensive validation.
    /// - Constructor validates all inputs and guarantees valid state or throws
    /// - Readonly fields prevent mutation after construction
    /// - No business logic, pure data encapsulation
    /// - Phase tracking for debugging and state management
    /// </summary>
    public class WorldGenerationState
    {
        // ========== CORE REFERENCES (NEVER NULL) ==========
        
        /// <summary>RimWorld's planet layer containing tile geometry</summary>
        public readonly PlanetLayer Layer;
        
        /// <summary>World grid reference (from Find.WorldGrid)</summary>
        public readonly WorldGrid WorldGrid;
        
        /// <summary>Total number of tiles in the world</summary>
        public readonly int TileCount;
        
        /// <summary>Vanilla elevation noise generator (preserved for continent shapes)</summary>
        public readonly ModuleBase VanillaElevationNoise;
        
        // ========== WORKING DATA ARRAYS ==========
        
        /// <summary>
        /// Extracted elevation data in meters.
        /// Written by Phase 1 (terrain generation), consumed by climate layers.
        /// </summary>
        public readonly float[] ElevationData;
        
        // ========== CLIMATE SYSTEM ==========
        
        /// <summary>
        /// Climate computation results container.
        /// Initialized with empty arrays, populated by ClimateOrchestrator in Phase 2.
        /// </summary>
        public readonly ClimateData ClimateData;
        
        /// <summary>User-tunable climate parameters from Advanced Climate Dialog</summary>
        public readonly ClimateConfiguration ClimateConfig;
        
        // ========== HYDROLOGY SYSTEM ==========
        
        /// <summary>User-tunable hydrology parameters (lake classification, evaporation)</summary>
        public readonly HydrologyConfiguration HydroConfig;
        
        // ========== FEATURE TOGGLES ==========
        
        /// <summary>
        /// Feature toggle configuration.
        /// Currently only hydrology is wired into generation; other toggles are placeholders.
        /// </summary>
        public readonly FeatureConfiguration Features;
        
        // ========== INTER-PHASE COMMUNICATION ==========
        
        /// <summary>
        /// Dictionary for passing computed data between phases.
        /// 
        /// NOTE: Currently unused. Originally intended for sharing complex data structures
        /// between phases (e.g., TerrainAnalysisOrchestrator, HillinessAnalyzer), but these
        /// are now created directly within GeographyLayer instead.
        /// 
        /// Kept for potential future use if inter-phase data sharing is needed.
        /// </summary>
        public readonly Dictionary<string, object> SystemData;
        
        // ========== PHASE TRACKING ==========
        
        /// <summary>
        /// Current generation phase.
        /// Tracks progress through the 3-phase pipeline for debugging and validation.
        /// </summary>
        public GenerationPhase CurrentPhase { get; private set; }
        
        // ========== CONSTRUCTOR ==========
        
        /// <summary>
        /// Creates a new world generation state with full validation.
        /// Throws ArgumentNullException or InvalidOperationException on invalid inputs.
        /// 
        /// This constructor guarantees:
        /// - All reference parameters are non-null
        /// - WorldGrid is accessible and has valid tile count
        /// - ClimateData arrays are initialized
        /// - Configuration objects have valid defaults
        /// - CurrentPhase is set to Initializing
        /// </summary>
        /// <param name="layer">RimWorld's planet layer (must not be null)</param>
        /// <param name="elevationNoise">Vanilla elevation noise generator (must not be null)</param>
        /// <param name="climateConfig">Climate configuration (null = use defaults)</param>
        /// <param name="hydroConfig">Hydrology configuration (null = use defaults)</param>
        /// <param name="features">Feature toggles (null = use defaults)</param>
        public WorldGenerationState(
            PlanetLayer layer,
            ModuleBase elevationNoise,
            ClimateConfiguration climateConfig,
            HydrologyConfiguration hydroConfig,
            FeatureConfiguration features)
        {
            // Validate required parameters
            Layer = layer ?? throw new ArgumentNullException(nameof(layer), 
                "PlanetLayer cannot be null during world generation");
            
            VanillaElevationNoise = elevationNoise ?? throw new ArgumentNullException(nameof(elevationNoise), 
                "Vanilla elevation noise generator cannot be null");
            
            // Validate WorldGrid accessibility
            WorldGrid = Find.WorldGrid;
            if (WorldGrid == null)
            {
                throw new InvalidOperationException(
                    "Find.WorldGrid is null - world generation cannot proceed");
            }
            
            TileCount = WorldGrid.TilesCount;
            if (TileCount <= 0)
            {
                throw new InvalidOperationException(
                    $"Invalid tile count: {TileCount} - world grid may not be initialized");
            }
            
            // Initialize working arrays
            ElevationData = new float[TileCount];
            SystemData = new Dictionary<string, object>();
            
            // Initialize climate system and register as WorldComponent immediately
            // This ensures ClimateData is available via Find.World.GetComponent<ClimateData>() during worldgen
            // (Required for biome workers that call Climate.* facade methods)
            var world = Find.World;
            if (world == null)
                throw new InvalidOperationException("World not available during generation");
            
            ClimateData = world.GetComponent<ClimateData>();
            if (ClimateData == null)
            {
                ClimateData = new ClimateData(world);
                world.components.Add(ClimateData);
            }
            
            ClimateConfig = climateConfig ?? new ClimateConfiguration();
            HydroConfig = hydroConfig ?? new HydrologyConfiguration();
            Features = features ?? new FeatureConfiguration();
            ClimateData.InitializeArrays(TileCount, Features.IsTier2HydrologyEnabled);
            
            // Set initial phase
            CurrentPhase = GenerationPhase.Initializing;
            
            if (Controller.DebugEnabled)
            {
                RPLog.D($"[WorldGen] State initialized - {TileCount} tiles, Phase: {CurrentPhase}");
            }
        }
        
        // ========== PHASE MANAGEMENT ==========
        
        /// <summary>
        /// Advances to the next generation phase.
        /// Logs phase transitions when debug is enabled.
        /// 
        /// Called by WorldGenerationOrchestrator at phase boundaries.
        /// </summary>
        /// <param name="phase">New phase to transition to</param>
        public void AdvanceToPhase(GenerationPhase phase)
        {
            CurrentPhase = phase;
            
            if (Controller.DebugEnabled)
            {
                RPLog.D($"[WorldGen] Phase transition: {phase}");
            }
        }
    }
    
    /// <summary>
    /// World generation phases.
    /// Tracks progress through the 3-phase pipeline.
    /// </summary>
    public enum GenerationPhase
    {
        /// <summary>Initial state - WorldGenerationState is being constructed</summary>
        Initializing,
        
        /// <summary>Phase 1 - Generating terrain (elevation, tiles, lakes)</summary>
        GeneratingTerrain,
        
        /// <summary>Phase 2 - Computing climate (all 11 layers via ClimateOrchestrator)</summary>
        ComputingClimate,
        
        /// <summary>Phase 3 - Applying data to tiles (temperature, rainfall, hilliness, biomes)</summary>
        ApplyingData,
        
        /// <summary>Generation complete - world is ready</summary>
        Complete
    }
}
