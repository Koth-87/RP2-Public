using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Planets.WorldGen.Climate;
using Planets.Utilities.WorldGen;
using Planets.WorldGen.Constants;

namespace Planets.Utilities.WorldGen
{
    /// <summary>
    /// Reusable spatial index for efficient proximity queries on spherical tile grids.
    /// Uses lat/lon bucketing for O(1) region lookup instead of O(n) full scans.
    /// 
    /// Typical usage:
    /// 1. Create index with bucket size = 2× your query radius
    /// 2. Call BuildIndex() with tiles to index
    /// 3. Call QueryRadius() repeatedly to find nearby tiles
    /// 
    /// Performance:
    /// - Build: O(m) where m = number of indexed tiles
    /// - Query: O(k) where k = average tiles per bucket (~10-30)
    /// - Speedup: 60× reduction for 600km radius queries (1500 ridges → 25 nearby)
    /// </summary>
    public class SpatialTileIndex
    {
        // Bucket grid: Flattened array (latBand * lonBandCount + lonBand) -> List of tile IDs
        // Changed from Dictionary for O(1) access and no hashing overhead
        private readonly List<int>[] buckets;
        
        // Configuration
        private readonly float bucketSizeKm;
        private readonly Func<int, Vector3> positionProvider;
        
        // Grid dimensions
        private readonly int latBandCount;
        private readonly int lonBandCount;
        
        // Reusable buffer for query results
        private readonly List<int> queryResults = new List<int>();
        
        /// <summary>
        /// Create spatial index with specified bucket size and custom position provider.
        /// Bucket size should typically be 2× your query radius.
        /// </summary>
        /// <param name="bucketSizeKm">Size of each bucket in kilometers</param>
        /// <param name="positionProvider">Function that returns Vector3 position for a given tile ID</param>
        public SpatialTileIndex(float bucketSizeKm, Func<int, Vector3> positionProvider)
        {
            this.bucketSizeKm = bucketSizeKm;
            this.positionProvider = positionProvider;
            
            // Calculate grid dimensions based on bucket size
            // Latitude: -90° to +90° = 180° total
            float latDegreesPerBucket = bucketSizeKm / EarthScaleConstants.KM_PER_DEGREE;
            latBandCount = Mathf.CeilToInt(180f / latDegreesPerBucket);
            
            // Longitude: -180° to +180° = 360° total
            float lonDegreesPerBucket = bucketSizeKm / EarthScaleConstants.KM_PER_DEGREE;
            lonBandCount = Mathf.CeilToInt(360f / lonDegreesPerBucket);

            // Initialize flat array
            this.buckets = new List<int>[latBandCount * lonBandCount];
        }
        
        /// <summary>
        /// Compatibility constructor for ClimateData-based usage.
        /// Use the delegate constructor for non-climate scenarios.
        /// </summary>
        /// <param name="bucketSizeKm">Size of each bucket in kilometers</param>
        /// <param name="climate">Climate data containing tile centers</param>
        public SpatialTileIndex(float bucketSizeKm, ClimateData climate)
            : this(bucketSizeKm, id => climate.TileCenters[id])
        {
        }
        
        /// <summary>
        /// Build index from a collection of tiles.
        /// Call once after construction, before any queries.
        /// </summary>
        /// <param name="tilesToIndex">Tile IDs to add to the index</param>
        public void BuildIndex(IEnumerable<int> tilesToIndex)
        {
            // Clear existing buckets
            Array.Clear(buckets, 0, buckets.Length);
            
            foreach (int tileID in tilesToIndex)
            {
                Vector3 tileCenter = positionProvider(tileID);
                var coords = GetBucketCoords(tileCenter);
                int bucketIdx = GetBucketIndex(coords.latBand, coords.lonBand);
                
                if (buckets[bucketIdx] == null)
                {
                    buckets[bucketIdx] = new List<int>();
                }
                buckets[bucketIdx].Add(tileID);
            }
        }
        
        /// <summary>
        /// Query all indexed tiles within radius of target tile.
        /// Returns only tiles that were added via BuildIndex().
        /// </summary>
        /// <param name="tileID">Center tile for query</param>
        /// <param name="radiusKm">Search radius in kilometers</param>
        /// <returns>List of tile IDs within radius</returns>
        public List<int> QueryRadius(int tileID, float radiusKm)
        {
            return QueryRadius(tileID, radiusKm, null);
        }
        
        /// <summary>
        /// Query indexed tiles within radius, with optional filter predicate.
        /// </summary>
        /// <param name="tileID">Center tile for query</param>
        /// <param name="radiusKm">Search radius in kilometers</param>
        /// <param name="filter">Optional predicate to filter results</param>
        /// <returns>List of tile IDs within radius that pass filter</returns>
        public List<int> QueryRadius(int tileID, float radiusKm, Func<int, bool> filter)
        {
            queryResults.Clear();
            
            Vector3 tileCenter = positionProvider(tileID);
            var centerCoords = GetBucketCoords(tileCenter);
            int centerLat = centerCoords.latBand;
            int centerLon = centerCoords.lonBand;
            int latSpan = LatitudeSpan(radiusKm);
            int lonSpan = LongitudeSpan(centerLat, radiusKm);
            
            for (int latOffset = -latSpan; latOffset <= latSpan; latOffset++)
            {
                int latBand = centerLat + latOffset;
                
                // Clamp latitude (no wraparound at poles)
                if (latBand < 0 || latBand >= latBandCount)
                    continue;
                
                for (int lonOffset = -lonSpan; lonOffset <= lonSpan; lonOffset++)
                {
                    int lonBand = centerLon + lonOffset;
                    
                    // Wrap longitude at dateline (±180°)
                    if (lonBand < 0)
                        lonBand += lonBandCount;
                    else if (lonBand >= lonBandCount)
                        lonBand -= lonBandCount;

                    int bucketIdx = GetBucketIndex(latBand, lonBand);
                    List<int> bucketTiles = buckets[bucketIdx];

                    if (bucketTiles == null) continue;
                
                    // Check each tile in bucket
                    int count = bucketTiles.Count;
                    for (int i = 0; i < count; i++)
                    {
                        int candidateTileID = bucketTiles[i];
                        
                        // Apply filter if provided
                        if (filter != null && !filter(candidateTileID))
                            continue;
                        
                        // Check actual distance (buckets are rectangular, we want circular query)
                        Vector3 candidateCenter = positionProvider(candidateTileID);
                        float distKm = ClimateGeometryHelpers.ArcDistanceKm(tileCenter, candidateCenter);
                        
                        if (distKm <= radiusKm)
                        {
                            queryResults.Add(candidateTileID);
                        }
                    }
                }
            }
            
            return new List<int>(queryResults); // Return copy to allow safe iteration
        }
        
        /// <summary>
        /// Find the closest indexed tile to an arbitrary position.
        /// Used by GL integration to convert noise sample points to tile IDs.
        /// Optimized for high-frequency calls.
        /// </summary>
        /// <param name="targetPos">World position (Vector3 on sphere)</param>
        /// <returns>Tile ID of closest tile, or -1 if no tiles indexed</returns>
        public int GetClosestTile(Vector3 targetPos)
        {
            int closestTile = -1;
            float closestDistSqr = float.MaxValue;
            
            var centerCoords = GetBucketCoords(targetPos);
            int centerLat = centerCoords.latBand;
            int centerLon = centerCoords.lonBand;
            int latSpan = LatitudeSpan(bucketSizeKm);
            int lonSpan = LongitudeSpan(centerLat, bucketSizeKm);
            
            for (int latOffset = -latSpan; latOffset <= latSpan; latOffset++)
            {
                int latBand = centerLat + latOffset;
                
                // Clamp latitude
                if (latBand < 0 || latBand >= latBandCount)
                    continue;
                
                for (int lonOffset = -lonSpan; lonOffset <= lonSpan; lonOffset++)
                {
                    int lonBand = centerLon + lonOffset;
                    
                    // Wrap longitude
                    if (lonBand < 0)
                        lonBand += lonBandCount;
                    else if (lonBand >= lonBandCount)
                        lonBand -= lonBandCount;

                    int bucketIdx = GetBucketIndex(latBand, lonBand);
                    List<int> bucketTiles = buckets[bucketIdx];

                    if (bucketTiles == null) continue;
                
                    // Find closest tile in this bucket
                    int count = bucketTiles.Count;
                    for (int i = 0; i < count; i++)
                    {
                        int candidateTileID = bucketTiles[i];
                        Vector3 candidateCenter = positionProvider(candidateTileID);
                        float distSqr = (candidateCenter - targetPos).sqrMagnitude;
                        
                        if (distSqr < closestDistSqr)
                        {
                            closestDistSqr = distSqr;
                            closestTile = candidateTileID;
                        }
                    }
                }
            }
            
            return closestTile;
        }
        
        /// <summary>
        /// Convert tile center to bucket coordinates.
        /// </summary>
        /// <param name="tileCenter">Tile center. Normalized internally; callers may pass a radius-100 position.</param>
        /// <returns>Bucket coordinates (latBand, lonBand)</returns>
        private (int latBand, int lonBand) GetBucketCoords(Vector3 tileCenter)
        {
            float magnitude = tileCenter.magnitude;
            if (magnitude > 0.0001f)
                tileCenter /= magnitude;

            // Convert to lat/lon (spherical coordinates)
            // Latitude: -90° (south pole) to +90° (north pole)
            float latRad = Mathf.Asin(Mathf.Clamp(tileCenter.y, -1f, 1f));
            float latDeg = latRad * Mathf.Rad2Deg;
            
            // Longitude: -180° to +180° (wrap at dateline)
            float lonRad = Mathf.Atan2(tileCenter.z, tileCenter.x);
            float lonDeg = lonRad * Mathf.Rad2Deg;
            
            // Convert to bucket indices
            // Latitude: Map [-90, +90] to [0, latBandCount)
            float latDegreesPerBucket = 180f / latBandCount;
            int latBand = Mathf.FloorToInt((latDeg + 90f) / latDegreesPerBucket);
            latBand = Mathf.Clamp(latBand, 0, latBandCount - 1);
            
            // Longitude: Map [-180, +180] to [0, lonBandCount)
            float lonDegreesPerBucket = 360f / lonBandCount;
            int lonBand = Mathf.FloorToInt((lonDeg + 180f) / lonDegreesPerBucket);
            lonBand = Mathf.Clamp(lonBand, 0, lonBandCount - 1);
            
            return (latBand, lonBand);
        }
        
        private int LatitudeSpan(float radiusKm)
        {
            float bucketWidthKm = (180f / latBandCount) * EarthScaleConstants.KM_PER_DEGREE;
            return ClampSpan(Mathf.CeilToInt(radiusKm / Mathf.Max(bucketWidthKm, 1f)), latBandCount);
        }

        /// <summary>Longitude buckets shrink by cos(latitude), so the same kilometer radius covers more of them near the poles.</summary>
        private int LongitudeSpan(int latBand, float radiusKm)
        {
            float latDeg = ((latBand + 0.5f) * (180f / latBandCount)) - 90f;
            float cosLat = Mathf.Cos(latDeg * Mathf.Deg2Rad);
            if (cosLat < 0.02f)
                cosLat = 0.02f;
            float bucketWidthKm = (360f / lonBandCount) * EarthScaleConstants.KM_PER_DEGREE * cosLat;
            return ClampSpan(Mathf.CeilToInt(radiusKm / Mathf.Max(bucketWidthKm, 1f)), lonBandCount);
        }

        private static int ClampSpan(int span, int bandCount)
        {
            if (span < 1)
                span = 1;
            int maxSpan = Mathf.Max(1, bandCount / 2);
            return span > maxSpan ? maxSpan : span;
        }

        private int GetBucketIndex(int latBand, int lonBand)
        {
            return latBand * lonBandCount + lonBand;
        }
        
        /// <summary>
        /// Get all populated buckets for iteration.
        /// Useful for operations that need to process regions independently (e.g. finding regional maxima).
        /// </summary>
        public IEnumerable<List<int>> GetAllBuckets()
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] != null && buckets[i].Count > 0)
                    yield return buckets[i];
            }
        }
        
        /// <summary>
        /// Get statistics about the spatial index for debugging.
        /// </summary>
        /// <returns>String with bucket count, tile count, and distribution stats</returns>
        public string GetDiagnosticInfo()
        {
            int totalTiles = 0;
            int nonEmptyBuckets = 0;
            int maxTilesInBucket = 0;
            int minTilesInBucket = int.MaxValue;
            
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] != null && buckets[i].Count > 0)
                {
                    int count = buckets[i].Count;
                    totalTiles += count;
                    nonEmptyBuckets++;
                    if (count > maxTilesInBucket) maxTilesInBucket = count;
                    if (count < minTilesInBucket) minTilesInBucket = count;
                }
            }
            
            if (nonEmptyBuckets == 0) minTilesInBucket = 0;
            
            float avgTilesPerBucket = nonEmptyBuckets > 0 ? (float)totalTiles / nonEmptyBuckets : 0f;
            
            return $"SpatialTileIndex: {totalTiles} tiles in {nonEmptyBuckets} buckets " +
                   $"(avg {avgTilesPerBucket:F1}, min {minTilesInBucket}, max {maxTilesInBucket}) " +
                   $"Grid: {latBandCount}×{lonBandCount}, Bucket size: {bucketSizeKm:F0}km";
        }
    }
}
