using UnityEngine;
using Verse;
using RimWorld.Planet;
using System.Collections.Generic;
using Planets.WorldGen.Constants;

namespace Planets.Utilities.WorldGen
{
    /// <summary>
    /// ClimateGeometryHelpers - Shared geometric calculation utilities for climate layers
    /// 
    /// Consolidates frequently-used geometric methods to avoid duplication and ensure
    /// consistent use of constants (e.g., PLANET_RADIUS_KM from EarthScaleConstants).
    /// 
    /// **Methods:**
    /// - ArcDistanceKm: Great-circle distance between two points on sphere
    /// - GetTilesWithinRings: BFS-based tile neighborhood search
    /// 
    /// **Usage:**
    /// - Called by: GeographyLayer, OrographyLayer, and other layers needing spatial calculations
    /// - Ensures consistency: all use EarthScaleConstants.PLANET_RADIUS_KM
    /// </summary>
    public static class ClimateGeometryHelpers
    {
        /// <summary>
        /// Calculate great-circle arc distance between two points on sphere
        /// 
        /// Uses normalized vectors and the dot product method (numerically stable).
        /// Returns distance in kilometers using the configured planet radius.
        /// 
        /// **Parameters:**
        /// - a, b: Position vectors (typically from ClimateData.TileCenters, already unit vectors)
        /// 
        /// **Returns:**
        /// Distance in kilometers
        /// 
        /// **Physics:**
        /// - Great-circle distance = R * arccos(a·b) where R = planet radius
        /// - Normalized vectors ensure the dot product is in [-1, 1]
        /// - Clamping prevents numerical errors causing arccos domain errors
        /// </summary>
        public static float ArcDistanceKm(Vector3 a, Vector3 b)
        {
            // Normalize and compute dot product
            float dot = Vector3.Dot(a.normalized, b.normalized);
            
            // Clamp to [-1, 1] to prevent numerical errors in arccos
            dot = Mathf.Clamp(dot, -1f, 1f);
            
            // Great-circle distance formula: R * arccos(dot product of unit vectors)
            float angle = Mathf.Acos(dot);
            return angle * EarthScaleConstants.PLANET_RADIUS_KM;
        }

        /// <summary>
        /// Applies planetary wave perturbations (Rossby waves) to latitude to break up perfectly linear climate bands.
        /// Includes deterministic noise tied to the world seed for variance between planets.
        /// Also applies a compression/expansion scaling to vary the width of climate bands dynamically.
        /// Returns the signed perturbed latitude in degrees, clamped to [-90, 90].
        /// </summary>
        public static float CalculatePerturbedLatitude(float latDegSigned, float lonRad, float latRadSigned, int seed)
        {
            // Create a deterministic hash from the seed
            // RimWorld seeds are often large ints. Use bitwise operations to extract varied bytes
            int hash1 = unchecked((seed ^ 0x55555555) * 16777619);
            int hash2 = unchecked((seed ^ unchecked((int)0xAAAAAAAA)) * 16777619);
            
            // Phase offsets in radians (0 to 2PI)
            float phaseOffset1 = (Mathf.Abs(hash1) % 1000) / 1000f * Mathf.PI * 2f;
            float phaseOffset2 = (Mathf.Abs(hash2) % 1000) / 1000f * Mathf.PI * 2f;
            float phaseOffset3 = ((Mathf.Abs(hash1) + Mathf.Abs(hash2)) % 1000) / 1000f * Mathf.PI * 2f;
            
            // Deterministic high-frequency noise for organic variation
            // We use simple deterministic pseudo-randomness based on seed and position
            float posHash = Mathf.Sin(lonRad * 12.345f + latRadSigned * 67.89f + (seed % 1000) * 0.01f);
            float noise = posHash * 0.5f; // +/- 0.5 degrees of organic noise
            
            // Wave 1: 3 large waves around the globe (typical planetary wave number)
            float wave1 = Mathf.Sin(lonRad * 3f + phaseOffset1); 
            // Wave 2: Secondary higher-frequency wave, tilted by latitude
            float wave2 = Mathf.Cos(lonRad * 5f + latRadSigned * 2f + phaseOffset2); 
            
            // Compression wave: Modulates the *position* of the bands to stretch/compress them locally.
            // By adding a sine wave based on latitude, the distance between isotherms naturally varies.
            float compressionWave = Mathf.Sin(lonRad * 2f + latRadSigned * 5f + phaseOffset3);
            
            // Perturbation amplitude: ~2.5 degrees of latitude shift max from waves, +1.0 from compression, +0.5 from noise
            float latPerturbation = (wave1 * 1.5f) + (wave2 * 1.0f) + (compressionWave * 1.0f) + noise;
            
            // CRITICAL: Taper the perturbation near the poles to keep the ice caps relatively circular!
            // A 4-degree shift at the equator is fine, but at 85 degrees it creates massive distortion.
            // Mathf.Cos(latRadSigned) goes from 1 at equator to 0 at poles.
            float polarTaper = Mathf.Cos(latRadSigned);
            latPerturbation *= polarTaper;
            
            // Apply additive translation perturbation (not a multiplier!)
            float modifiedLat = latDegSigned + latPerturbation;
            
            // Return clamped signed latitude
            return Mathf.Clamp(modifiedLat, -90f, 90f);
        }

        /// <summary>
        /// Get all tiles within specified ring distance via BFS (Non-allocating)
        /// 
        /// Requires caller to provide reusable buffers to avoid allocations.
        /// 
        /// **Parameters:**
        /// - centerTile: Starting tile ID
        /// - maxRings: Maximum ring distance to search
        /// - result: Buffer to store all unique tiles found (cleared at start)
        /// - currentRing: Buffer for current expansion frontier
        /// - nextRing: Buffer for next expansion frontier
        /// - neighborBuffer: Buffer for neighbor lookup
        /// </summary>
        public static void GetTilesWithinRings(
            int centerTile, 
            int maxRings, 
            HashSet<int> result, 
            List<int> currentRing, 
            List<int> nextRing, 
            List<PlanetTile> neighborBuffer)
        {
            result.Clear();
            currentRing.Clear();
            nextRing.Clear();

            // Ring 0
            result.Add(centerTile);
            currentRing.Add(centerTile);

            for (int ring = 1; ring <= maxRings; ring++)
            {
                // We strictly use nextRing to collect new neighbors
                // currentRing holds the tiles we are expanding from
                // At end of loop, we swap them

                // Note: nextRing is already cleared at start or from previous swap/clear
                // Actually, if we swap, the "new" nextRing (old currentRing) has data.
                // So we must clear nextRing inside loop.
                nextRing.Clear();

                for (int i = 0; i < currentRing.Count; i++)
                {
                    int tile = currentRing[i];
                    neighborBuffer.Clear();
                    Find.WorldGrid.GetTileNeighbors(tile, neighborBuffer);

                    for (int j = 0; j < neighborBuffer.Count; j++)
                    {
                        int nID = neighborBuffer[j].tileId;
                        // HashSet.Add returns true if element was added (not present)
                        if (result.Add(nID))
                        {
                            nextRing.Add(nID);
                        }
                    }
                }

                if (nextRing.Count == 0) break;

                // Swap buffers for next iteration
                var temp = currentRing;
                currentRing = nextRing;
                nextRing = temp;
            }
        }
    }
}
