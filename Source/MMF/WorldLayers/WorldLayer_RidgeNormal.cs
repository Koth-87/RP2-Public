using System.Collections;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.WorldGen.Climate;

namespace MapModeFramework
{
    /// <summary>
    /// World layer that draws procedural ridge-normal arrows on ridge tiles.
    /// Direction from ClimateData.RidgeNormal (zero vector skipped).
    /// Renders when drawRidgeNormal is enabled in DrawSettings.
    /// </summary>
    /// <summary>
    /// Draws the across-ridge orientation (RidgeNormal) as a symmetric bar rather than an arrow,
    /// because the vector has a 180° sign ambiguity — neither end is "forward."
    /// Bar half-length = 0.36 × tileSize, half-width = 0.08 × tileSize.
    /// </summary>
    public class WorldLayer_RidgeNormal : WorldDrawLayer
    {
        public static WorldLayer_RidgeNormal Instance { get; private set; }
        public override bool VisibleWhenLayerNotSelected => false;
        private const float MinMagnitudeSq = 0.01f;

        public WorldLayer_RidgeNormal()
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
            if (climate?.RidgeNormal == null || climate?.IsRidgeTile == null)
                yield break;

            WorldGrid grid = Find.WorldGrid;
            float halfLength = grid.AverageTileSize * 0.36f;
            float halfWidth  = grid.AverageTileSize * 0.08f;

            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (i >= climate.IsRidgeTile.Length || i >= climate.RidgeNormal.Length)
                    break;
                if (TileUtilities.IsWaterForOverlay(grid[i]) && !WorldDrawLayerBase_Render_Patch.OverlayDrawsOnWater(GetType()))
                    continue;
                if (!climate.IsRidgeTile[i])
                    continue;

                Vector3 normal = climate.RidgeNormal[i];
                if (normal.sqrMagnitude < MinMagnitudeSq)
                    continue;

                TileUtilities.DrawOrientationBar(GetSubMesh(Materials.matRidgeNormalArrow), i, normal, halfLength, halfWidth);
            }
            FinalizeMesh(MeshParts.All);
        }
    }
}
