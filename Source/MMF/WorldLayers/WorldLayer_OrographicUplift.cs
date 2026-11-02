using System.Collections;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.WorldGen.Climate;

namespace MapModeFramework
{
    /// <summary>
    /// World layer that draws windward (U) orographic uplift only.
    /// Renders when drawOrographicUplift is enabled in DrawSettings.
    /// Calibrated to show uplift only where terrain matters: ridges or tiles with significant
    /// local relief/elevation change. Filters out flat terrain that accumulates minor climb.
    /// </summary>
    public class WorldLayer_OrographicUplift : WorldDrawLayer
    {
        public static WorldLayer_OrographicUplift Instance { get; private set; }
        public override bool VisibleWhenLayerNotSelected => false;

        public WorldLayer_OrographicUplift()
        {
            Instance = this;
        }

        public override IEnumerable Regenerate()
        {
            foreach (object item in base.Regenerate())
                yield return item;
            if (WorldDrawLayerBase_Render_Patch.OverlayDrawDisabled(GetType()))
                yield break;

            var climate = Find.World?.GetComponent<ClimateData>();
            if (climate == null)
                yield break;

            WorldGrid grid = Find.WorldGrid;
            var tileMaterials = new Dictionary<Material, List<int>>();

            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (TileUtilities.IsWaterForOverlay(grid[i]) && !WorldDrawLayerBase_Render_Patch.OverlayDrawsOnWater(GetType()))
                    continue;

                float windward = Climate.Windward(i);
                if (!PassesUpliftTerrainFilter(climate, i, windward))
                    continue;

                Material mat = Materials.MatForOrographicUpliftOverlay(windward);
                if (mat == BaseContent.ClearMat)
                    continue;

                if (!tileMaterials.TryGetValue(mat, out var list))
                {
                    list = new List<int>();
                    tileMaterials[mat] = list;
                }
                list.Add(i);
            }

            foreach (var kv in tileMaterials)
            {
                foreach (int tile in kv.Value)
                    TileUtilities.DrawTile(GetSubMesh(kv.Key), tile);
            }
            FinalizeMesh(MeshParts.All);
        }

        /// <summary>
        /// Show uplift only on ridges or within 150 km upwind of ridges (ridge-as-start zone).
        /// With ridge-as-start orography, windward is non-zero only in that zone; 0.35 threshold
        /// filters moderate uplift. Excludes flat terrain that would have qualified under path accumulation.
        /// </summary>
        private static bool PassesUpliftTerrainFilter(ClimateData climate, int tileId, float windward)
        {
            if (climate.IsRidgeTile != null && tileId < climate.IsRidgeTile.Length && climate.IsRidgeTile[tileId])
                return true;

            return windward >= 0.35f;
        }
    }
}
