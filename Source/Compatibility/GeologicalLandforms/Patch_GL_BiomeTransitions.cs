using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Biomes.Core;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate;
using Unity.Collections;

namespace Planets.Compatibility.GeologicalLandforms
{
    /// <summary>
    /// Optimization and Correctness patch for Geological Landforms' Biome Transitions.
    /// 
    /// Problem:
    /// 1. GL's CalcTransitions runs sequentially (30s+ lag).
    /// 2. GL checks midpoints by re-generating vanilla noise, ignoring RP2's simulated climate data.
    ///    This results in biome transitions being calculated based on wrong data.
    /// 
    /// Solution:
    /// 1. Disable GL's CalcTransitions call.
    /// 2. Replace it with our own parallelized implementation.
    /// 3. Instead of noise lookups, interpolate RP2's actual tile data for the midpoint checks.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_GL_BiomeTransitions
    {
        // Lunar loads GeologicalLandforms.dll from its own mod constructor, which can run after ours.
        // Prepare must fail closed until that assembly exists, or Harmony reports a null target.
        private static bool IsActive =>
            ModsConfig.IsActive("m00nl1ght.GeologicalLandforms") &&
            Controller.Settings.perfOptimizeGL.CurrentValue &&
            AccessTools.TypeByName("GeologicalLandforms.Patches.Patch_RimWorld_WorldGenStep_Terrain") != null;

        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            return IsActive;
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            // Target the Postfix where CalcTransitions is called
            // In RW 1.6+ GL uses GenerateFresh
            return AccessTools.Method("GeologicalLandforms.Patches.Patch_RimWorld_WorldGenStep_Terrain:GenerateFresh_Postfix");
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var calcTransitions = AccessTools.Method("GeologicalLandforms.Patches.Patch_RimWorld_WorldGenStep_Terrain:CalcTransitions");
            var replacement = AccessTools.Method(typeof(Patch_GL_BiomeTransitions), nameof(CalcTransitionsOptimized));

            if (calcTransitions == null)
            {
                RPLog.Error("[Compat] Patch_GL_BiomeTransitions: Could not find CalcTransitions method.");
                return instructions;
            }

            bool found = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(calcTransitions))
                {
                    codes[i].operand = replacement;
                    found = true;
                    // CalcTransitions takes (WorldGenStep_Terrain instance) as arg, ours does too, so signature matches
                }
            }

            if (!found)
            {
                RPLog.Error("[Compat] Patch_GL_BiomeTransitions: Could not find call to CalcTransitions to replace.");
                return instructions;
            }
            
            if (Controller.DebugEnabled)
                RPLog.Message("[Compat] Patch_GL_BiomeTransitions applied. Replaced sequential GL transitions with RP2 parallel version.");

            return codes;
        }

        /// <summary>
        /// Optimized replacement for GL's CalcTransitions.
        /// Runs in parallel and uses RP2 data interpolation.
        /// </summary>
        public static void CalcTransitionsOptimized(WorldGenStep_Terrain instance)
        {
            RPLog.D("[Compat] Starting optimized parallel biome transition calculation...");
            var stopwatch = Stopwatch.StartNew();

            var world = Find.World;
            var grid = world.grid;
            int tilesCount = grid.TilesCount;
            
            // GL stores transitions in a bool array of size [tilesCount * 6] (for up to 6 neighbors)
            var data = new bool[tilesCount * 6];

            // Pre-fetch neighbor data to avoid repeated lookups
            // Use 1.6 compatible native arrays if available
            var nbDataNative = grid.ExtNbValuesNative();
            var nbOffsetsNative = grid.ExtNbOffsetsNative();
            
            bool useNative = nbDataNative.HasValue && nbOffsetsNative.HasValue;
            
            // Fallback for older versions or if reflection fails
            int[] nbDataLegacy = null;
            int[] nbOffsetsLegacy = null;

            if (!useNative)
            {
                nbDataLegacy = grid.ExtNbValuesLegacy();
                nbOffsetsLegacy = grid.ExtNbOffsetsLegacy();
            }

            if (!useNative && (nbDataLegacy == null || nbOffsetsLegacy == null))
            {
                RPLog.Error("[Compat] Could not access WorldGrid neighbor arrays via reflection (Native or Legacy). Dumping WorldGrid members for diagnosis...");
                LogWorldGridReflections();
                
                RPLog.Message("[Compat] Falling back to standard GetTileNeighbors (slower but safe).");
                CalcTransitionsFallback(grid, tilesCount, data);
                return;
            }

            // Reflection to get the SetBiomeTransitions method (it's an extension method in GL)
            // world.LandformData()?.SetBiomeTransitions(data);
            // We need to find the LandformData extension method and the SetBiomeTransitions method
            var landformDataMethod = AccessTools.Method("GeologicalLandforms.ExtensionUtils:LandformData");
            var setTransitionsMethod = AccessTools.Method("GeologicalLandforms.LandformData:SetBiomeTransitions");

            if (landformDataMethod == null || setTransitionsMethod == null)
            {
                RPLog.Error("[Compat] Could not find GL LandformData methods via reflection. Aborting transition calc.");
                return;
            }

            // Parallel Loop
            Parallel.For(0, tilesCount, tileIdx =>
            {
                var tile = grid[tileIdx];
                var biome = tile.biome;
                
                // Fast fail if invalid
                if (biome == null) return;

                int nbOffset;
                int nbBound;
                
                if (useNative)
                {
                    nbOffset = nbOffsetsNative.Value[tileIdx];
                    nbBound = grid.IdxBoundForNative(nbOffsetsNative.Value, nbDataNative.Value, tileIdx);
                }
                else
                {
                    nbOffset = nbOffsetsLegacy[tileIdx];
                    nbBound = grid.IdxBoundForLegacy(nbOffsetsLegacy, nbDataLegacy, tileIdx);
                }

                for (var i = nbOffset; i < nbBound; i++)
                {
                    int nIdx;
                    if (useNative)
                        nIdx = nbDataNative.Value[i].tileId;
                    else
                        nIdx = nbDataLegacy[i];

                    var nTile = grid[nIdx];
                    var nBiome = nTile.biome;

                    if (nBiome != null && biome != nBiome)
                    {
                        // Calculate transition using RP2 data interpolation
                        data[tileIdx * 6 + i - nbOffset] = CheckIsTransitionInterpolated(tileIdx, nIdx, biome, nBiome, grid, tile, nTile);
                    }
                }
            });

            // Apply results
            var landformData = landformDataMethod.Invoke(null, new object[] { world });
            if (landformData != null)
            {
                setTransitionsMethod.Invoke(landformData, new object[] { data });
            }

            stopwatch.Stop();
            RPLog.Message($"[Compat] Calculation of extended biome data took {stopwatch.ElapsedMilliseconds} ms (Parallel + Interpolated).");
        }

        private static void LogWorldGridReflections()
        {
            try
            {
                var fields = AccessTools.GetDeclaredFields(typeof(WorldGrid));
                RPLog.D("[Compat] WorldGrid fields:");
                foreach (var f in fields)
                {
                    RPLog.D($" - {f.FieldType} {f.Name}");
                }
                
                var props = AccessTools.GetDeclaredProperties(typeof(WorldGrid));
                RPLog.D("[Compat] WorldGrid properties:");
                foreach (var p in props)
                {
                    RPLog.D($" - {p.PropertyType} {p.Name}");
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Error dumping WorldGrid reflections: {e}");
            }
        }

        private static void CalcTransitionsFallback(WorldGrid grid, int tilesCount, bool[] data)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Parallel Loop with thread-local buffers
            Parallel.For(0, tilesCount, 
                () => new List<PlanetTile>(), // Local init
                (tileIdx, loopState, neighbors) => // Body
                {
                    var tile = grid[tileIdx];
                    var biome = tile.biome;
                    
                    if (biome != null)
                    {
                        neighbors.Clear();
                        grid.GetTileNeighbors(tileIdx, neighbors);
                        
                        // We need the index of the neighbor in the neighbor list to set the correct bit in 'data'
                        // The 'data' array structure depends on the order of neighbors.
                        // Standard 'GetTileNeighbors' returns them in the same order as stored internally.
                        
                        for (int i = 0; i < neighbors.Count; i++)
                        {
                            var nTile = neighbors[i];
                            var nBiome = grid[nTile.tileId].biome;
                            
                            // Note: GL's data array expects 6 entries per tile. 
                            // i corresponds to the neighbor index (0..N, where N <= 6 usually).
                            // This assumes GetTileNeighbors returns them in the exact order GL expects (which is the storage order).
                            
                            if (nBiome != null && biome != nBiome)
                            {
                                // PlanetTile does not have an indexer or get method, we need to access the WorldGrid to get the neighbor Tile
                                data[tileIdx * 6 + i] = CheckIsTransitionInterpolated(tileIdx, nTile.tileId, biome, nBiome, grid, tile, grid[nTile.tileId]);
                            }
                        }
                    }
                    return neighbors;
                },
                (finalNeighbors) => { } // Local finally
            );

            // Apply results
            var landformDataMethod = AccessTools.Method("GeologicalLandforms.ExtensionUtils:LandformData");
            var setTransitionsMethod = AccessTools.Method("GeologicalLandforms.LandformData:SetBiomeTransitions");
            var world = Find.World;

            if (landformDataMethod != null && setTransitionsMethod != null)
            {
                var landformData = landformDataMethod.Invoke(null, new object[] { world });
                if (landformData != null)
                {
                    setTransitionsMethod.Invoke(landformData, new object[] { data });
                }
            }

            stopwatch.Stop();
            RPLog.Message($"[Compat] Calculation of extended biome data took {stopwatch.ElapsedMilliseconds} ms (Parallel Fallback + Interpolated).");
        }

        /// <summary>
        /// Determines if a transition exists between two tiles by interpolating RP2 climate data
        /// at the midpoint, rather than re-generating vanilla noise.
        /// </summary>
        private static bool CheckIsTransitionInterpolated(int tileA_Idx, int tileB_Idx, BiomeDef biomeA, BiomeDef biomeB, WorldGrid grid, Tile tileA, Tile tileB)
        {
            // Logic mirrored from GL:
            // "bool rev = tile > nTile;"
            bool rev = tileA_Idx > tileB_Idx;
            int minIdx = rev ? tileB_Idx : tileA_Idx;
            int maxIdx = rev ? tileA_Idx : tileB_Idx;

            // Deterministic RNG state based on tile pair
            int seed = Gen.HashCombineInt(minIdx, maxIdx);
            
            // THREAD-SAFE RANDOMNESS
            // Verse.Rand is NOT thread-safe. We use a simple hash function.
            // Rand.ValueSeeded returns 0..1. >0.5 is true.
            // We can replicate this using the seed directly.
            
            // Hash function (MurmurHash3 mixer style or similar simple hash)
            uint hash = (uint)seed;
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;
            
            // Normalize to 0..1 float
            float randValue = (hash & 0xFFFFFF) / (float)0xFFFFFF;
            
            bool flag = randValue > 0.5f ^ rev;
            
            // "var rTile = flag ? nTile : tile;" (This is the 'root' tile for score calculation, likely purely for distance checks if any)
            // In vanilla GetScore, the tile index is used for some random seeds or localized checks.
            int rTileIdx = flag ? tileB_Idx : tileA_Idx;

            // Create an interpolated fake tile representing the midpoint
            var interpTile = new SurfaceTile(new PlanetTile(rTileIdx, grid.Surface))
            {
                elevation = (tileA.elevation + tileB.elevation) * 0.5f,
                temperature = (tileA.temperature + tileB.temperature) * 0.5f,
                rainfall = (tileA.rainfall + tileB.rainfall) * 0.5f,
                swampiness = (tileA.swampiness + tileB.swampiness) * 0.5f,
                hilliness = (Hilliness)Math.Max((int)tileA.hilliness, (int)tileB.hilliness)
            };

            ClimateSample midpoint = ClimateSample.Midpoint(tileA, tileA_Idx, tileB, tileB_Idx, rTileIdx);
            PlanetTile rootPlanetTile = new PlanetTile(rTileIdx, grid.Surface);

            float scoreA = EvaluateMidpointScore(biomeA, interpTile, rootPlanetTile, in midpoint);
            float scoreB = EvaluateMidpointScore(biomeB, interpTile, rootPlanetTile, in midpoint);

            float diff = scoreB - scoreA;

            // "if (diff > 0) return true;"
            if (diff > 0) return true;
            // "if (diff < 0) return false;"
            if (diff < 0) return false;

            // "return flag;"
            return flag;
        }

        private static float EvaluateMidpointScore(BiomeDef biome, Tile interpTile, PlanetTile planetTile, in ClimateSample midpoint)
        {
            if (biome?.Worker == null)
                return -100f;
            float freq = Controller.Settings?.biomeConfig?.GetCommonalityMultiplier(biome.defName) ?? 1f;
            if (freq <= 0f)
                return -100f;
            float freqShift = -2f * Mathf.Log(freq);

            if (biome.Worker is BiomeWorkerBase rpWorker && !rpWorker.HasCustomScore)
            {
                BiomeFit fit = rpWorker.EvaluateFit(biome, interpTile, planetTile, in midpoint);
                if (!fit.Viable)
                    return -100f;
                return 100f * Mathf.Exp(-(fit.Energy + freqShift) / 2f);
            }

            float raw = biome.Worker.GetScore(biome, interpTile, planetTile);
            if (raw <= 0f)
                return -100f;
            float peak = ForeignPeaks.Peak(biome.defName);
            if (peak <= 0f)
                return raw * freq;
            float ratio = Mathf.Min(raw / peak, 1f);
            float energy = -2f * Mathf.Log(ratio) + freqShift;
            return 100f * Mathf.Exp(-energy / 2f);
        }
    }

    public static class ExtensionUtils
    {
        // Cache members
        private static PropertyInfo _valuesPropNative;
        private static PropertyInfo _offsetsPropNative;
        private static FieldInfo _valuesFieldLegacy;
        private static FieldInfo _offsetsFieldLegacy;

        public static NativeArray<PlanetTile>? ExtNbValuesNative(this WorldGrid grid)
        {
            if (_valuesPropNative == null)
                _valuesPropNative = AccessTools.Property(typeof(WorldGrid), "UnsafeTileIDToNeighbors_values");
            
            if (_valuesPropNative == null) return null;
            
            var val = _valuesPropNative.GetValue(grid);
            if (val == null) return null;
            return (NativeArray<PlanetTile>)val;
        }

        public static NativeArray<int>? ExtNbOffsetsNative(this WorldGrid grid)
        {
            if (_offsetsPropNative == null)
                _offsetsPropNative = AccessTools.Property(typeof(WorldGrid), "UnsafeTileIDToNeighbors_offsets");

            if (_offsetsPropNative == null) return null;

            var val = _offsetsPropNative.GetValue(grid);
            if (val == null) return null;
            return (NativeArray<int>)val;
        }

        public static int[] ExtNbValuesLegacy(this WorldGrid grid)
        {
            if (_valuesFieldLegacy == null)
                _valuesFieldLegacy = AccessTools.Field(typeof(WorldGrid), "tileIDToNeighbors_values");
            
            if (_valuesFieldLegacy == null) return null;
            return _valuesFieldLegacy.GetValue(grid) as int[];
        }

        public static int[] ExtNbOffsetsLegacy(this WorldGrid grid)
        {
            if (_offsetsFieldLegacy == null)
                _offsetsFieldLegacy = AccessTools.Field(typeof(WorldGrid), "tileIDToNeighbors_offsets");

            if (_offsetsFieldLegacy == null) return null;
            return _offsetsFieldLegacy.GetValue(grid) as int[];
        }

        public static int IdxBoundForNative(this WorldGrid grid, NativeArray<int> offsets, NativeArray<PlanetTile> values, int tileIdx)
        {
            if (tileIdx + 1 < offsets.Length)
                return offsets[tileIdx + 1];
            return values.Length;
        }

        public static int IdxBoundForLegacy(this WorldGrid grid, int[] offsets, int[] values, int tileIdx)
        {
            if (offsets == null || values == null) return 0;
            if (tileIdx + 1 < offsets.Length)
                return offsets[tileIdx + 1];
            return values.Length;
        }
    }
}
