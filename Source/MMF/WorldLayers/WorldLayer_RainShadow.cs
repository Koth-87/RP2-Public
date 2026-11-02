using System.Collections;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.WorldGen.Climate;

namespace MapModeFramework
{
    /// <summary>
    /// World layer that draws leeward (L) rain shadow only.
    /// Renders when drawRainShadow is enabled in DrawSettings.
    /// Uses Climate.Leeward(tile) with display-only clamps; keep in sync with climate pipeline.
    /// </summary>
    public class WorldLayer_RainShadow : WorldDrawLayer
    {
        public static WorldLayer_RainShadow Instance { get; private set; }
        public override bool VisibleWhenLayerNotSelected => false;

        public WorldLayer_RainShadow()
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
                float leeward = Climate.Leeward(i);
                Material mat = Materials.MatForRainShadowOverlay(leeward);
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
    }
}
