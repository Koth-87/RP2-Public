using System.Collections;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate;

namespace MapModeFramework
{
    /// <summary>
    /// World layer that highlights ridge tiles with a golden glow overlay.
    /// Only renders when drawRidges is enabled in DrawSettings.
    /// 
    /// This layer follows the vanilla pattern (like WorldDrawLayer_Hills):
    /// - Only overrides Regenerate() to build the mesh
    /// - Relies on base class dirty flag for initial regeneration
    /// - Harmony patch on WorldDrawLayerBase.Render handles enable/disable
    /// </summary>
    public class WorldLayer_Ridges : WorldDrawLayer
    {
        public static WorldLayer_Ridges Instance { get; private set; }
        public override bool VisibleWhenLayerNotSelected => false;

        public WorldLayer_Ridges()
        {
            Instance = this;
        }

        public override IEnumerable Regenerate()
        {
            // Call base to clear submeshes and set dirty=false
            foreach (object item in base.Regenerate())
            {
                yield return item;
            }
            if (WorldDrawLayerBase_Render_Patch.OverlayDrawDisabled(GetType()))
                yield break;

            // Get climate data
            var climate = Find.World?.GetComponent<ClimateData>();
            if (climate?.IsRidgeTile == null)
            {
                RPLog.D("[RP] WorldLayer_Ridges: ClimateData.IsRidgeTile is null");
                yield break;
            }

            // Build meshes for ridge tiles (split by intensity).
            // GetSubMesh called per-tile to avoid vertex overflow when many ridges share same material.
            WorldGrid grid = Find.WorldGrid;
            int ridgeCount = 0;

            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (TileUtilities.IsWaterForOverlay(grid[i]) && !WorldDrawLayerBase_Render_Patch.OverlayDrawsOnWater(GetType()))
                    continue;
                // Add ridge tiles to mesh
                if (climate.IsRidgeTile != null && i < climate.IsRidgeTile.Length && climate.IsRidgeTile[i])
                {
                    // Check elevation band if available
                    int band = (climate.ElevationBand != null && i < climate.ElevationBand.Length) 
                        ? climate.ElevationBand[i] 
                        : 0;

                    // Band 5 (Peaks) gets high intensity, others get low
                    if (band >= 5)
                        TileUtilities.DrawTile(GetSubMesh(Materials.matRidgeGlowHigh), i);
                    else
                        TileUtilities.DrawTile(GetSubMesh(Materials.matRidgeGlowLow), i);
                    
                    ridgeCount++;
                }
            }

            RPLog.D($"[RP] WorldLayer_Ridges: Built mesh with {ridgeCount} ridge tiles");
            FinalizeMesh(MeshParts.All);
        }
    }
}
