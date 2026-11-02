using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace MapModeFramework
{
    public abstract class MapMode_Region : MapMode_Cached
    {
        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Region.Instance;
        public WorldLayer_MapMode_Region WorldLayerRegion => (WorldLayer_MapMode_Region)WorldLayer;

        public List<Region> regions = new List<Region>();
        public abstract Material RegionMaterial { get; }
        public virtual bool DoBorders => def.RegionProperties?.doBorders == true;
        public virtual Material BorderMaterial { get; }
        public virtual float BorderWidth => def.RegionProperties?.borderWidth ?? 0.2f;

        public MapMode_Region() { }
        public MapMode_Region(MapModeDef def) : base(def) { }

        public override void Initialize()
        {
            SetRegions();
        }

        protected override void StartCaching(CancellationToken token)
        {
            regions.ForEach(region =>
            {
                if (region.doBorders && region.borderMaterial != BaseContent.ClearMat)
                {
                    tilesToCache += region.GetBorders().Count;
                }
            });
            int regionsCount = regions.Count;
            for (int i = 0; i < regionsCount; i++)
            {
                token.ThrowIfCancellationRequested();
                Region region = regions[i];
                WorldLayerRegion.DoRegionBorders(region, null, this);
            }
        }

        protected override void DoCacheClearing()
        {
            Type genericType = GetType();
            while (genericType != null && genericType.GetGenericArguments().Length == 0)
                genericType = genericType.BaseType;
            Type[] typeArgs = genericType?.GetGenericArguments() ?? Type.EmptyTypes;
            if (typeArgs.Length > 0)
            {
                Type cacheType = typeof(RegionCache<>).MakeGenericType(typeArgs[0]);
                MethodInfo removeMethod = cacheType.GetMethod("RemoveRegion", BindingFlags.Public | BindingFlags.Static);

                PropertyInfo listProperty = genericType.GetProperty("RegionList");
                if (listProperty?.GetValue(this) is IEnumerable list && removeMethod != null)
                {
                    foreach (var item in list)
                    {
                        if (Planets.Core.Controller.DebugEnabled)
                            Core.Message($"Removed {item}");
                        removeMethod.Invoke(null, new[] { item });
                    }
                }
            }

            List<Region> regions = this.regions;
            int regionsCount = regions.Count;
            for (int i = 0; i < regionsCount; i++)
            {
                Region region = regions[i];
                EdgesCache.ClearRegionCache(region);
            }
        }

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            SetRegions();
        }

        private int[] tileToRegion;

        public Region GetRegion(int tile)
        {
            if (tileToRegion != null && tile >= 0 && tile < tileToRegion.Length)
            {
                int index = tileToRegion[tile];
                if (index >= 0 && index < regions.Count)
                    return regions[index];
                return null;
            }
            return regions.FirstOrDefault(x => x.tiles.Contains(tile));
        }

        protected void RebuildTileIndex()
        {
            int count = Find.WorldGrid?.TilesCount ?? 0;
            tileToRegion = new int[count];
            for (int i = 0; i < count; i++)
                tileToRegion[i] = -1;
            for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
            {
                List<int> tiles = regions[regionIndex].tiles;
                if (tiles == null)
                    continue;
                for (int i = 0; i < tiles.Count; i++)
                {
                    int tile = tiles[i];
                    if (tile >= 0 && tile < tileToRegion.Length)
                        tileToRegion[tile] = regionIndex;
                }
            }
        }

        public abstract void SetRegions();

        public override Material GetMaterial(int tile)
        {
            Region region = GetRegion(tile);
            if (region != null)
            {
                return region.material;
            }
            return base.GetMaterial(tile);
        }

        public override string GetTooltip(int tile)
        {
            Region region = GetRegion(tile);
            if (region == null)
            {
                return base.GetTooltip(tile);
            }
            if (!ModCompatibility.OverrideTooltip(tile, out string tooltip))
            {
                tooltip = region.GetTooltip();
            }
            return tooltip;
        }
    }

    public abstract class MapMode_GenericRegion<T> : MapMode_Region
    {
        public abstract List<T> RegionList { get; }

        public MapMode_GenericRegion() { }
        public MapMode_GenericRegion(MapModeDef def) : base(def) { }

        public override void SetRegions()
        {
            if (!EnabledCaching)
                regions.Clear();
            foreach (T regionType in RegionList)
            {
                Region region = GetOrGenerateRegion(regionType);
                if (!regions.Contains(region))
                {
                    regions.Add(region);
                }
            }

            Region GetOrGenerateRegion(T regionType)
            {
                Region region = RegionCache<T>.GetRegion(regionType);
                if (region == null)
                {
                    region = GenerateRegion(regionType);
                    if (EnabledCaching)
                    {
                        RegionCache<T>.AddRegion(regionType, region);
                    }
                }
                return region;
            }
            RebuildTileIndex();
        }

        public abstract Region GenerateRegion(T regionType);

        public virtual string GetRegionTooltip(T regionType)
        {
            return string.Empty;
        }
    }
}
