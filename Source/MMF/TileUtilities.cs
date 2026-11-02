using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MapModeFramework
{
    public static class TileUtilities
    {
        private static WorldGrid Grid => Find.WorldGrid;

        /// <summary>
        /// True when a tile should be treated as water by MMF overlays.
        /// Includes below-sea-level tiles (<see cref="Tile.WaterCovered"/>), Ocean biome tiles,
        /// and inland <see cref="BiomeDefOf.Lake"/> tiles when <c>showLakesAsWater</c> is enabled in <see cref="DrawSettings"/>.
        /// </summary>
        public static bool IsWaterForOverlay(int tileId)
        {
            WorldGrid grid = Grid;
            if (grid == null || tileId < 0 || tileId >= grid.TilesCount)
                return false;
            return IsWaterForOverlay(grid[tileId]);
        }

        public static bool IsWaterForOverlay(Tile tile)
        {
            if (tile == null)
                return false;
            if (tile.WaterCovered || tile.biome == BiomeDefOf.Ocean)
                return true;
            bool showLakesAsWater = MapModeComponent.Instance?.drawSettings?.showLakesAsWater ?? true;
            return showLakesAsWater && tile.biome == BiomeDefOf.Lake;
        }

        /// <summary>True when the player is looking at the surface planet layer.</summary>
        public static bool IsSurfaceLayerSelected()
        {
            WorldGrid grid = Find.WorldGrid;
            return grid?.Surface != null && PlanetLayer.Selected == grid.Surface;
        }

        /// <summary>Mouse tile on the surface layer. Orbit and other layers return false.</summary>
        public static bool TryGetSurfaceMouseTile(out int tileId)
        {
            tileId = -1;
            PlanetTile tile = GenWorld.MouseTile();
            WorldGrid grid = Find.WorldGrid;
            if (!tile.Valid || grid?.Surface == null || tile.Layer != grid.Surface)
                return false;
            tileId = tile.tileId;
            return true;
        }

        public static List<PlanetTile> GetTileNeighbors(int tile)
        {
            List<PlanetTile> neighbors = new List<PlanetTile>();
            Grid.GetTileNeighbors(tile, neighbors);
            return neighbors;
        }

        public static List<Vector3> GetTileVertices(int tile)
        {
            List<Vector3> vertices = new List<Vector3>();
            Grid.GetTileVertices(tile, vertices);
            return vertices;
        }

        public static List<int> GetTileIndices(int tile)
        {
            List<int> indices = new List<int>();
            Grid.GetTileVerticesIndices(tile, indices);
            return indices;
        }

        public static List<Vector3> GetSharedVertices(int tile, int neighbor)
        {
            List<Vector3> tileVertices = GetTileVertices(tile);
            return GetTileVertices(neighbor).Where(x => tileVertices.Contains(x)).ToList();
        }

        public static void DerivePerpendicularVectors(Vector3 v1, Vector3 v2, float distance, out Vector3 v3, out Vector3 v4)
        {
            Vector3 perpendicular = Vector3.Cross(v2, v1).normalized * 0.05f * distance;
            v3 = v1 + perpendicular;
            v4 = v2 + perpendicular;
        }

        public static List<int> GetBorderTiles(List<int> tiles)
        {
            HashSet<int> tileSet = new HashSet<int>(tiles);
            List<int> borderTiles = new List<int>();
            for (int i = 0; i < tiles.Count; i++)
            {
                if (IsBorderTile(tiles[i], tileSet))
                {
                    borderTiles.Add(tiles[i]);
                }
            }
            return borderTiles;
        }

        public static bool IsBorderTile(int tile, HashSet<int> tiles)
        {
            List<PlanetTile> neighbors = GetTileNeighbors(tile);
            return neighbors.Any(x => !tiles.Contains(x));
        }

        public static void DrawTile(LayerSubMesh subMesh, int tile)
        {
            List<Vector3> vertices = GetTileVertices(tile);
            int vertCount = vertices.Count;
            int subVerts = subMesh.verts.Count;
            for (int j = 0; j < vertCount; j++)
            {
                subMesh.verts.Add(vertices[j] + vertices[j].normalized * 0.012f);
                subMesh.uvs.Add((GenGeo.RegularPolygonVertexPosition(vertCount, j) + Vector2.one) / 2f);
                if (j < vertCount - 2)
                {
                    subMesh.tris.Add(subVerts + j + 2);
                    subMesh.tris.Add(subVerts + j + 1);
                    subMesh.tris.Add(subVerts);
                }
            }
        }

        /// <summary>
        /// Push a point from the sphere surface outward for overlay stacking (same offset as DrawTile).
        /// </summary>
        public static float OverlayOffset => 0.012f;

        /// <summary>
        /// Draw a symmetric orientation bar (thin diamond) at the tile center along a world-space direction.
        /// Use this instead of DrawDirectionalArrow when the direction has a 180° sign ambiguity (e.g. ridge normals).
        /// The bar has no pointed tip — both ends are identical — so neither end reads as "forward."
        /// Four vertices, two triangles; safe to call inside GetSubMesh loops.
        /// </summary>
        public static void DrawOrientationBar(LayerSubMesh subMesh, int tile, Vector3 directionWorld, float halfLength, float halfWidth)
        {
            Vector3 center = Grid.GetTileCenter(tile);
            float sphereRadius = center.magnitude;
            Vector3 n = center.normalized;
            Vector3 w = directionWorld;
            float dn = Vector3.Dot(w, n);
            Vector3 tangent = w - dn * n;
            float len = tangent.magnitude;
            if (len < 1e-5f)
                return;
            Vector3 forward = tangent / len;
            Vector3 right = Vector3.Cross(n, forward).normalized;
            float r = sphereRadius + OverlayOffset;
            Vector3 pA = (center + forward * halfLength).normalized * r;  // +axis tip
            Vector3 pB = (center - forward * halfLength).normalized * r;  // -axis tip
            Vector3 pR = (center + right * halfWidth).normalized * r;     // right midpoint
            Vector3 pL = (center - right * halfWidth).normalized * r;     // left midpoint
            int bi = subMesh.verts.Count;
            subMesh.verts.Add(pA);   // 0
            subMesh.verts.Add(pR);   // 1
            subMesh.verts.Add(pB);   // 2
            subMesh.verts.Add(pL);   // 3
            subMesh.uvs.Add(new Vector2(0.5f, 1f));
            subMesh.uvs.Add(new Vector2(1f, 0.5f));
            subMesh.uvs.Add(new Vector2(0.5f, 0f));
            subMesh.uvs.Add(new Vector2(0f, 0.5f));
            // Two triangles, reversed winding to face outward.
            subMesh.tris.Add(bi + 1); subMesh.tris.Add(bi);     subMesh.tris.Add(bi + 3);
            subMesh.tris.Add(bi + 3); subMesh.tris.Add(bi + 2); subMesh.tris.Add(bi + 1);
        }

        /// <summary>
        /// Add a procedural arrow (triangle) at the tile center, oriented by a world-space direction.
        /// Direction is projected onto the tangent plane at the tile center so the arrow lies on the surface.
        /// </summary>
        /// <param name="subMesh">Target submesh</param>
        /// <param name="tile">Tile index</param>
        /// <param name="directionWorld">Direction in world space (e.g. wind or ridge normal); will be normalized and projected onto tangent plane</param>
        /// <param name="size">Half-length of the arrow (tip to base center) in world units. Planet radius is 100, typical tile width ~3 units, so ~1.0–1.5 is a reasonable arrow half-length.</param>
        public static void DrawDirectionalArrow(LayerSubMesh subMesh, int tile, Vector3 directionWorld, float size = 1.5f)
        {
            Vector3 center = Grid.GetTileCenter(tile);
            float sphereRadius = center.magnitude; // typically 100 for the surface layer
            Vector3 n = center.normalized;
            Vector3 w = directionWorld;
            float dn = Vector3.Dot(w, n);
            Vector3 tangent = w - dn * n;
            float len = tangent.magnitude;
            if (len < 1e-5f)
                return;
            Vector3 forward = tangent / len;
            Vector3 right = Vector3.Cross(n, forward).normalized;
            float halfWidth = size * 1.05f;   // 3× original 0.35
            Vector3 tip = center + forward * size * 2f;
            Vector3 baseLeft = center - forward * size * 2f - right * halfWidth;
            Vector3 baseRight = center - forward * size * 2f + right * halfWidth;
            float r = sphereRadius + OverlayOffset;
            tip = tip.normalized * r;
            baseLeft = baseLeft.normalized * r;
            baseRight = baseRight.normalized * r;
            int baseIdx = subMesh.verts.Count;
            subMesh.verts.Add(tip);
            subMesh.verts.Add(baseLeft);
            subMesh.verts.Add(baseRight);
            subMesh.uvs.Add(new Vector2(0.5f, 1f));
            subMesh.uvs.Add(new Vector2(0f, 0f));
            subMesh.uvs.Add(new Vector2(1f, 0f));
            // Reversed winding (matches vanilla DrawTile) so the triangle faces outward from the sphere.
            subMesh.tris.Add(baseIdx + 2);
            subMesh.tris.Add(baseIdx + 1);
            subMesh.tris.Add(baseIdx);
        }
    }
}
