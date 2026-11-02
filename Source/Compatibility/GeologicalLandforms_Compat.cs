using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.Utilities.WorldGen;

namespace Planets.Compatibility
{
    /// <summary>
    /// Geological Landforms compatibility module.
    /// Integrates GL's cave system generation with RP2's terrain using translation layer adapters.
    /// </summary>
    [StaticConstructorOnStartup]
    public class GeologicalLandformsCompat : ICompatibilityModule
    {
        private static GeologicalLandformsCompat _instance;
        public static GeologicalLandformsCompat Instance => _instance ?? (_instance = new GeologicalLandformsCompat());

        /// <summary>
        /// GL disables vanilla river mutators when its own river landforms are enabled.
        /// Every RP biome allowlist must include these, or a world-map river has nothing to draw on the colony map.
        /// </summary>
        private static readonly string[] GLRiverLandforms =
        {
            "River", "RiverSource", "RiverDelta", "RiverConfluence", "RiverIsland"
        };

        public static bool IsEnabled => Instance.IsActive;

        static GeologicalLandformsCompat()
        {
            ModCompatibilityRegistry.Register(Instance);
        }

        // Interface Implementation
        public string ModuleID => "m00nl1ght.GeologicalLandforms";
        public string ModName => "Geological Landforms";
        public bool IsActive { get; private set; }
        public bool HasWorldCreationButton => false;
        public string ButtonLabel => null;

        public void OnWorldCreationButtonClicked(List<FactionDef> factions) { }
        public void RenderIntegrationUI(Listing_Standard list) { } // GL manages its own settings
        public void ExposeData() { }
        public void ResetToDefaults() { }

        // Cached reflection members
        private Type patchTerrainType;
        private FieldInfo hillinessNoiseOffsetField;
        private FieldInfo caveSystemNoiseThresholdField;
        private MethodInfo calcCaveSystemsMethod;
        
        /// <summary>
        /// Spatial index for fast Vector3 -> Tile ID conversion.
        /// </summary>
        private SpatialTileIndex tileIndex;
        
        // Caching for high-frequency queries
        private Vector3 lastQueryPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        private int lastQueryTileID = -1;
        
        public void Initialize()
        {
            try
            {
                // Check if Geological Landforms is active
                IsActive = ModsConfig.IsActive(ModuleID);
                
                if (!IsActive)
                {
                    if (Controller.DebugEnabled)
                        RPLog.D("[Compat] Geological Landforms not detected");
                    return;
                }
                
                RPLog.Message("[Compat] Geological Landforms detected, initializing compatibility");
                
                // Inject Biome Properties (Landforms)
                InjectBiomeProperties();

                // Get GL's patch type that holds the static configuration fields
                patchTerrainType = AccessTools.TypeByName("GeologicalLandforms.Patches.Patch_RimWorld_WorldGenStep_Terrain");
                
                if (patchTerrainType == null)
                {
                    RPLog.Warning("[Compat] Geological Landforms detected but Patch_RimWorld_WorldGenStep_Terrain type not found");
                    IsActive = false;
                    return;
                }
                
                // Cache the static fields
                hillinessNoiseOffsetField = patchTerrainType.GetField("HillinessNoiseOffset",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                caveSystemNoiseThresholdField = patchTerrainType.GetField("CaveSystemNoiseThreshold",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                calcCaveSystemsMethod = AccessTools.Method(patchTerrainType, "CalcCaveSystems",
                    new[] { typeof(ModuleBase), typeof(ModuleBase) });
                
                if (hillinessNoiseOffsetField == null || caveSystemNoiseThresholdField == null)
                {
                    RPLog.Warning("[Compat] Geological Landforms: Required fields not found");
                    IsActive = false;
                    return;
                }

                if (calcCaveSystemsMethod == null)
                {
                    RPLog.Warning("[Compat] Geological Landforms: CalcCaveSystems method not found");
                }
                
                RPLog.Message("[Compat] Geological Landforms compatibility initialized successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to initialize Geological Landforms compatibility: {e}");
                IsActive = false;
            }
        }
        
        /// <summary>
        /// Applies the cave system threshold value to GL.
        /// Uses GL's own formula to convert slider value (0-2) to threshold.
        /// </summary>
        /// <param name="caveSystems">Cave system slider value (0-2, default 1.0)</param>
        public static void ApplyCaveSystemValue(float caveSystems)
        {
            Instance.ApplyCaveSystemValueInternal(caveSystems);
        }

        private void ApplyCaveSystemValueInternal(float caveSystems)
        {
            if (!IsActive || caveSystemNoiseThresholdField == null)
                return;
            
            try
            {
                // GL's formula: threshold = caves <= 0 ? 1 : 0.4 - caves * 0.7
                // caves=0 → threshold=1 (no caves)
                // caves=1 → threshold=-0.3 (default)
                // caves=2 → threshold=-1.0 (maximum caves)
                float threshold = caveSystems <= 0 ? 1f : 0.4f - caveSystems * 0.7f;
                
                caveSystemNoiseThresholdField.SetValue(null, threshold);
                
                if (Controller.DebugEnabled)
                    RPLog.D($"[Compat] Set GL cave system threshold to {threshold:F2} (slider value: {caveSystems:F2})");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to set GL cave system value: {e}");
            }
        }
        
        /// <summary>
        /// Disables GL's Mountains slider by setting offset to 0.
        /// Users should use RP2's Hilliness Bias in the main page instead.
        /// </summary>
        public static void DisableMountainSlider()
        {
            Instance.DisableMountainSliderInternal();
        }

        private void DisableMountainSliderInternal()
        {
            if (!IsActive || hillinessNoiseOffsetField == null)
                return;
            
            try
            {
                // Set hilliness offset to 0 (disabled)
                hillinessNoiseOffsetField.SetValue(null, 0f);
                
                if (Controller.DebugEnabled)
                    RPLog.D("[Compat] Disabled GL mountains slider (offset = 0)");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to disable GL mountains slider: {e}");
            }
        }
        
        /// <summary>
        /// Initialize the spatial index for Vector3 -> Tile ID lookups.
        /// Must be called after world terrain generation completes.
        /// </summary>
        public static void BuildSpatialIndex()
        {
            Instance.BuildSpatialIndexInternal();
        }

        private void BuildSpatialIndexInternal()
        {
            if (!IsActive)
                return;
            
            try
            {
                // Use small bucket size (~50km) for precise lookups
                // GL queries can be anywhere on the planet, so we need fine granularity
                tileIndex = new SpatialTileIndex(50f, id => Find.WorldGrid.GetTileCenter(id));
                
                // Index all tiles
                var allTiles = Enumerable.Range(0, Find.WorldGrid.TilesCount);
                tileIndex.BuildIndex(allTiles);
                
                if (Controller.DebugEnabled)
                    RPLog.D("[Compat] GL spatial index built successfully");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to build GL spatial index: {e}");
            }
        }
        
        /// <summary>
        /// Get the tile ID for an arbitrary world position.
        /// Used by GL noise adapters to translate noise sample points.
        /// </summary>
        /// <param name="pos">Position on the planet sphere</param>
        /// <returns>Tile ID of closest tile, or -1 if lookup fails</returns>
        public static int GetTileIDAt(Vector3 pos)
        {
            return Instance.GetTileIDAtInternal(pos);
        }

        private int GetTileIDAtInternal(Vector3 pos)
        {
            // Fast path: check last query cache
            // GL often queries the same position multiple times in sequence (elevation, hilliness, etc.)
            if ((pos - lastQueryPos).sqrMagnitude < 0.000001f)
            {
                return lastQueryTileID;
            }

            if (tileIndex == null)
            {
                RPLog.Warning("[Compat] GL spatial index not initialized - building now");
                BuildSpatialIndexInternal();
            }
            
            int result = tileIndex?.GetClosestTile(pos) ?? -1;
            
            // Update cache
            lastQueryPos = pos;
            lastQueryTileID = result;
            
            return result;
        }
        
        /// <summary>
        /// Legacy method name for backward compatibility.
        /// Now calls ApplyCaveSystemValue and ignores mountains parameter.
        /// </summary>
        public static void ApplyMountainCaveValues(float mountains, float caveSystems)
        {
            // Mountains slider is disabled - ignore mountains parameter
            ApplyCaveSystemValue(caveSystems);
            DisableMountainSlider();
        }

        /// <summary>
        /// Invoke GL's cave system calculation with RP2 noise adapters.
        /// </summary>
        public static void TryCalculateCaveSystems(ModuleBase hillinessModule, ModuleBase elevationModule)
        {
            Instance.TryCalculateCaveSystemsInternal(hillinessModule, elevationModule);
        }

        private void TryCalculateCaveSystemsInternal(ModuleBase hillinessModule, ModuleBase elevationModule)
        {
            if (!IsActive || calcCaveSystemsMethod == null || hillinessModule == null || elevationModule == null)
                return;

            try
            {
                calcCaveSystemsMethod.Invoke(null, new object[] { hillinessModule, elevationModule });

                if (Controller.DebugEnabled)
                    RPLog.D("[Compat] GL cave systems calculated via RP2 noise adapters");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to calculate GL cave systems: {e}");
            }
        }

        private void InjectBiomeProperties()
        {
            try
            {
                Type biomePropsType = AccessTools.TypeByName("GeologicalLandforms.BiomeProperties");
                if (biomePropsType == null)
                {
                    RPLog.Error("[Compat] Could not find GeologicalLandforms.BiomeProperties type");
                    return;
                }

                InjectBiomePropsFor("RP_Oasis", biomePropsType,
                    allowed: WithRivers("Oasis", "Lake", "LakeWithIsland", "Valley"));

                InjectBiomePropsFor("RP_Prairie", biomePropsType,
                    disallowed: new List<string> { "Oasis", "IceOasis", "Island", "CoastalIsland", "Archipelago", "DryLake" });

                InjectBiomePropsFor("RP_Savanna", biomePropsType,
                    disallowed: new List<string> { "Glacier", "IceOasis", "Fjord" });

                // GL's own temperature and rainfall requirements already keep most landforms off
                // the wrong climate. These lists only remove landforms that clash with the biome.
                InjectBiomePropsFor("RP_Permafrost", biomePropsType,
                    disallowed: new List<string> { "Oasis", "DesertPlateau", "Badlands", "DryLake", "Atoll" });

                InjectBiomePropsFor("RP_Steppe", biomePropsType,
                    disallowed: new List<string> { "Oasis", "IceOasis", "Glacier", "Fjord", "SwampHill", "Atoll" });

                InjectBiomePropsFor("RP_ColdDesert", biomePropsType,
                    disallowed: new List<string> { "Oasis", "IceOasis", "Glacier", "Fjord", "SwampHill", "Atoll" });

                try
                {
                    AccessTools.Method("GeologicalLandforms.BiomeProperties:RebuildCache")?.Invoke(null, null);
                }
                catch (Exception e)
                {
                    RPLog.W($"[Compat] GL BiomeProperties.RebuildCache failed: {e.Message}");
                }

                if (Controller.DebugEnabled)
                    RPLog.Message("[Compat] Injected Geological Landforms biome properties");
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Failed to inject biome properties: {e}");
            }
        }

        private static List<string> WithRivers(params string[] landforms)
        {
            var list = new List<string>(landforms);
            foreach (string river in GLRiverLandforms)
            {
                if (!list.Contains(river))
                    list.Add(river);
            }
            return list;
        }

        private void InjectBiomePropsFor(string biomeDefName, Type propsType, List<string> allowed = null, List<string> disallowed = null)
        {
            BiomeDef biome = DefDatabase<BiomeDef>.GetNamedSilentFail(biomeDefName);
            if (biome == null) return;

            // Create instance of GeologicalLandforms.BiomeProperties
            DefModExtension extension = (DefModExtension)Activator.CreateInstance(propsType);
            
            // Set fields using reflection
            Traverse t = Traverse.Create(extension);
            t.Field("allowLandforms").SetValue(true);
            t.Field("allowBiomeTransitions").SetValue(true);
            
            if (allowed != null)
                t.Field("allowedLandforms").SetValue(allowed);
            
            if (disallowed != null)
                t.Field("disallowedLandforms").SetValue(disallowed);

            // Add to biome
            if (biome.modExtensions == null)
                biome.modExtensions = new List<DefModExtension>();
            
            biome.modExtensions.Add(extension);
        }
        
    }
}
