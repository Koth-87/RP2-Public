using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace MapModeFramework
{
    public abstract class WorldLayer_MapMode : WorldDrawLayer
    {
        public MapModeComponent MapModeComponent => MapModeComponent.Instance;
        public MapMode CurrentMapMode => MapModeComponent.currentMapMode;
        public virtual bool Active => CurrentMapMode?.WorldLayerClass == GetType() && CurrentMapMode.Active;

        public bool Regenerated => !ShouldRegenerate && ready;
        public override bool ShouldRegenerate => MapModeComponent.regenerateNow && Active && ready;
        protected bool ready = true;

        protected virtual bool RegenerateAsynchronously => true;

        public override bool VisibleWhenLayerNotSelected => false;

        private int tooltipCacheTile = -2;
        private string tooltipCache;

        public override void Render()
        {
            if (!Active)
            {
                return;
            }
            base.Render();
        }

        public virtual void OnGUI()
        {
            if (Find.World == null || Find.WorldGrid == null || !TileUtilities.IsSurfaceLayerSelected())
                return;
            try
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                if (Find.World.UI.selector.dragBox.IsValidAndActive || Find.WorldTargeter.IsTargeting || Find.ScreenshotModeHandler.Active)
                {
                    return;
                }
                if (!TileUtilities.TryGetSurfaceMouseTile(out int mouseTile))
                    return;
                Vector2 screenPos = GenWorldUI.WorldToUIPosition(Find.WorldGrid.GetTileCenter(mouseTile));
                Rect rect = new Rect(screenPos.x - 40f, screenPos.y - 40f, 80f, 80f);
                DrawSettings drawSettings = MapModeComponent.drawSettings;
                if (drawSettings.displayLabels)
                {
                    string tileLabel = GetTileLabel(mouseTile);
                    if (!tileLabel.NullOrEmpty())
                        Widgets.Label(rect, tileLabel);
                }
                Text.Anchor = TextAnchor.UpperLeft;
                if (drawSettings.doTooltip)
                {
                    if (tooltipCacheTile != mouseTile)
                    {
                        tooltipCacheTile = mouseTile;
                        tooltipCache = GetTooltip(mouseTile);
                    }
                    if (!tooltipCache.NullOrEmpty())
                    {
                        TipSignal tip = new TipSignal(() => tooltipCache, mouseTile);
                        TooltipHandler.TipRegion(rect, tip);
                    }
                }
            }
            catch (Exception ex)
            {
                Core.ErrorOnce($"World layer tooltip failed: {ex.Message}", "WorldLayerOnGUI".GetHashCode());
            }
        }

        public override IEnumerable Regenerate()
        {
            foreach (object item in base.Regenerate())
            {
                yield return item;
            }
            if (!Active)
            {
                yield break;
            }
            if (RegenerateAsynchronously && !WorldRegenHandler.IsBusy)
            {
                WorldRegenHandler.RequestAsyncWorldRegeneration(this);
            }
        }

        public virtual async Task BuildSubMeshes(CancellationToken token)
        {
            ready = false;
            bool interrupted = false;
            Dictionary<Material, List<int>> tileMaterials = new Dictionary<Material, List<int>>();
            try
            {
                ModCompatibility.SnapshotExplorationVisibility();
                Task worker = Task.Run(() => PrepareMeshes(tileMaterials, token), token);
                WorldRegenHandler.currentWorkerTask = worker;
                await worker;
            }
            catch (OperationCanceledException)
            {
                Core.Message("Async regeneration interrupted");
                interrupted = true;
            }
            catch (Exception ex)
            {
                Core.Error($"Error during asynchronous regeneration of {this}: {ex.Message}");
            }
            finally
            {
                if (!interrupted)
                {
                    foreach (var (material, tiles) in tileMaterials)
                    {
                        // Once per tile. GetSubMesh splits only when it is called past 40,000 vertices.
                        for (int i = 0; i < tiles.Count; i++)
                            TileUtilities.DrawTile(GetSubMesh(material), tiles[i]);
                    }
                    FinalizeMesh(MeshParts.All);
                    MapModeComponent.Notify_RegenerationComplete(CurrentMapMode);
                }
                WorldRegenHandler.Notify_Finished();
                ready = true;
            }
        }

        public virtual void PrepareMeshes(Dictionary<Material, List<int>> tileMaterials, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            WorldGrid grid = Find.WorldGrid;
            int tilesCount = grid.TilesCount;

            token.ThrowIfCancellationRequested();
            var allTiles = Enumerable.Range(0, tilesCount);
            WorldRegenHandler.tilesToPrepare = allTiles.Where(x => ValidTile(x, false)).Count();
            
            for (int i = 0; i < tilesCount; i++)
            {
                token.ThrowIfCancellationRequested();
                if (!ValidTile(i, false))
                {
                    continue;
                }
                token.ThrowIfCancellationRequested();
                Material material = GetMaterial(i);
                if (material == BaseContent.ClearMat)
                {
                    continue;
                }
                token.ThrowIfCancellationRequested();
                if (!tileMaterials.TryGetValue(material, out List<int> tileList))
                {
                    tileList = new List<int>();
                    tileMaterials[material] = tileList;
                }
                tileList.Add(i);
                WorldRegenHandler.tilesPrepared++;
            }
        }

        public bool ValidTile(int tile, bool allPossible = true)
        {
            if (!allPossible && !ModCompatibility.DrawTile(tile))
            {
                return false;
            }
            if (TileUtilities.IsWaterForOverlay(tile))
            {
                if (!CurrentMapMode.def.allowWaterTiles)
                    return false;
                if (!CurrentMapMode.CanToggleWater || !MapModeComponent.drawSettings.includeWater)
                    return false;
            }
            return true;
        }

        public virtual Material GetMaterial(int tile) => CurrentMapMode.GetMaterial(tile);

        public virtual string GetTileLabel(int tile)
        {
            if (ModCompatibility.OverrideLabel(tile, out string label))
            {
                return label;
            }
            return CurrentMapMode.GetTileLabel(tile);
        }

        public virtual string GetTooltip(int tile)
        {
            if (ModCompatibility.OverrideTooltip(tile, out string tooltip))
            {
                return tooltip;
            }
            return CurrentMapMode.GetTooltip(tile);
        }
    }
}
