using System.Collections;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.WorldGen.Climate;

namespace MapModeFramework
{
    /// <summary>
    /// Draws wind direction arrows only on tiles where Climate.HasMonsoon(tile) is true.
    /// Arrow points toward where wind is blowing; hue = compass direction, brightness = speed.
    /// Renders when drawMonsoonWind is enabled in DrawSettings.
    /// </summary>
    public class WorldLayer_MonsoonWind : WorldDrawLayer
    {
        public static WorldLayer_MonsoonWind Instance { get; private set; }
        public override bool VisibleWhenLayerNotSelected => false;
        private const float MinWindMagnitude = 0.1f;

        public WorldLayer_MonsoonWind()
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
            if (climate?.WindDirection == null || climate.WindSpeed == null)
                yield break;

            WorldGrid grid = Find.WorldGrid;
            float arrowSize = grid.AverageTileSize * 0.18f;
            var byMaterial = new Dictionary<Material, List<(int tile, Vector3 dir)>>();

            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (i >= climate.WindDirection.Length)
                    break;
                if (TileUtilities.IsWaterForOverlay(grid[i]) && !WorldDrawLayerBase_Render_Patch.OverlayDrawsOnWater(GetType()))
                    continue;
                if (!Climate.HasMonsoon(i))
                    continue;

                Vector3 wind = climate.WindDirection[i];
                if (wind.sqrMagnitude < MinWindMagnitude * MinWindMagnitude)
                    continue;

                float speed = climate.WindSpeed != null && i < climate.WindSpeed.Length ? climate.WindSpeed[i] : 5f;
                Vector3 center = planetLayer.GetTileCenter(i);
                Material mat = Materials.MatForWindArrowDirSpd(wind, center, speed);
                if (!byMaterial.TryGetValue(mat, out var list))
                {
                    list = new List<(int, Vector3)>();
                    byMaterial[mat] = list;
                }
                list.Add((i, wind));
            }

            foreach (var kv in byMaterial)
            {
                foreach (var (tile, dir) in kv.Value)
                    TileUtilities.DrawDirectionalArrow(GetSubMesh(kv.Key), tile, dir, arrowSize);
            }
            FinalizeMesh(MeshParts.All);
        }
    }
}
