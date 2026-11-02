using System;
using System.Collections.Generic;
using System.Threading;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.WorldGen.Climate;

namespace MapModeFramework
{
    /// <summary>
    /// Map mode showing outdoor growing period in twelfths (0–12).
    /// Incrementally caches on the main thread via <see cref="RunCacheBatch"/> because <see cref="GenTemperature"/> is not thread-safe.
    /// </summary>
    public class MapMode_GrowingPeriod : MapMode_Cached
    {
        private byte[] cache;
        private int _nextTileToCache;
        private const float minGrowingTemp = 6f;
        private const float maxGrowingTemp = 42f;

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => true;
        public override bool CacheOnMainThread => true;
        public override bool HasCacheData => cache != null;

        public MapMode_GrowingPeriod() { }
        public MapMode_GrowingPeriod(MapModeDef def) : base(def) { }

        public override (int cachedThisBatch, bool done) RunCacheBatch(int batchSize, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return (0, true);
            World world = Find.World;
            WorldGrid grid = world?.grid;
            if (grid == null)
                return (0, true);
            int tileCount = grid.TilesCount;
            if (tileCount == 0)
                return (0, true);

            if (cache == null)
            {
                cache = new byte[tileCount];
                tilesToCache = tileCount;
                _nextTileToCache = 0;
            }

            int start = _nextTileToCache;
            int end = Math.Min(start + batchSize, tileCount);
            int cachedThisBatch = 0;

            for (int tile = start; tile < end; tile++)
            {
                if (token.IsCancellationRequested || Find.World == null || Find.WorldGrid == null)
                    return (cachedThisBatch, true);

                byte? result = SafeCalculateGrowingPeriod(tile, token);
                if (result == null)
                    return (cachedThisBatch, true);
                cache[tile] = result.Value;
                tilesCached++;
                cachedThisBatch++;
                _nextTileToCache = tile + 1;
            }

            bool done = _nextTileToCache >= tileCount;
            return (cachedThisBatch, done);
        }

        private static byte? SafeCalculateGrowingPeriod(int tile, CancellationToken token)
        {
            if (token.IsCancellationRequested || Find.World == null || Find.WorldGrid == null)
                return null;

            try
            {
                PlanetTile planetTile = new PlanetTile(tile);
                if (!planetTile.Valid)
                    return 0;
                if (Find.World == null || Find.WorldGrid == null || token.IsCancellationRequested)
                    return null;

                List<Twelfth> twelfths;
                try
                {
                    twelfths = GenTemperature.TwelfthsInAverageTemperatureRange(planetTile, minGrowingTemp, maxGrowingTemp);
                }
                catch (ThreadAbortException) { return null; }
                catch (NullReferenceException) { return null; }
                catch (Exception)
                {
                    if (Find.World == null || Find.WorldGrid == null)
                        return null;
                    throw;
                }

                return (byte)(twelfths?.Count ?? 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (ThreadAbortException) { return null; }
            catch (NullReferenceException) { return null; }
            catch (Exception)
            {
                if (Find.World == null || Find.WorldGrid == null)
                    return null;
                return 0;
            }
        }

        protected override void StartCaching(CancellationToken token)
        {
            World world = Find.World;
            WorldGrid grid = world?.grid;
            if (grid == null)
                return;
            int tileCount = grid.TilesCount;
            if (tileCount == 0)
                return;

            cache = new byte[tileCount];
            tilesToCache = tileCount;

            for (int tile = 0; tile < tileCount; tile++)
            {
                token.ThrowIfCancellationRequested();
                if (Find.World == null || Find.WorldGrid == null)
                    return;

                byte? result = SafeCalculateGrowingPeriod(tile, token);
                if (result == null)
                    return;

                cache[tile] = result.Value;
                tilesCached++;
            }
        }

        protected override void DoCacheClearing()
        {
            cache = null;
            _nextTileToCache = 0;
        }

        public override Material GetMaterial(int tile)
        {
            if (cache != null && tile >= 0 && tile < cache.Length && tilesCached > tile)
                return Materials.MatForGrowingPeriodOverlay(cache[tile]);

            MapModeComponent.Instance?.EnsureMainThreadCache(this);
            return BaseContent.ClearMat;
        }

        public override string GetTooltip(int tile)
        {
            return string.Format("{0}:\n{1}", "OutdoorGrowingPeriod".Translate(), ClimateUtility.FormatGrowingPeriodDescription(tile));
        }
    }
}
