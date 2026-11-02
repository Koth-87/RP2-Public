using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.Core;
using Planets.Utilities.Text;
using Planets.WorldGen.Constants;

namespace Planets.Patches.UI
{
    /// <summary>
    /// Adds optional information to the world tile inspector.
    /// 
    /// Controlled by ModSettings toggles:
    /// - showStoneTypes: Display stone types present in the tile
    /// - showGrowingPeriod: Display outdoor growing period (quadrums)
    /// - showDiseaseFrequency: Display average disease frequency
    /// 
    /// Each section can be toggled independently in Mod Settings (Main tab).
    /// Includes duplicate-check to prevent re-entry.
    /// </summary>
    [HarmonyPatch(typeof(WorldInspectPane), "TileInspectString", MethodType.Getter)]
    public static class Patch_TileInspectString
    {
        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            // Check if we've already added our info to prevent duplicates
            // (Can happen if patch runs multiple times or with other mods)
            if (__result.Contains("StoneTypesHere".Translate()) ||
                __result.Contains("OutdoorGrowingPeriod".Translate()) ||
                __result.Contains("AverageDiseaseFrequency".Translate()))
            {
                return; // Already added, don't add again
            }

            var sb = new StringBuilder(__result);
            {
                sb.AppendLine();

                // Stone types (if enabled)
                if (Controller.Settings.showStoneTypes.CurrentValue)
                {
                    Append_StoneTypes(sb);
                }

                // Growing period (if enabled)
                if (Controller.Settings.showGrowingPeriod.CurrentValue)
                {
                    Append_GrowingQuadrums(sb);
                }

                // Disease frequency (if enabled)
                if (Controller.Settings.showDiseaseFrequency.CurrentValue)
                {
                    Append_DiseaseFrequency(sb);
                }
            }
            __result = sb.ToString();
        }

        /// <summary>
        /// Appends stone types present in the selected tile.
        /// Only shown for buildable tiles (not ocean, etc.).
        /// </summary>
        private static bool TryGetSurfaceTile(out int tileId, out Tile tile)
        {
            tileId = -1;
            tile = null;
            PlanetTile selected = Find.WorldSelector.SelectedTile;
            if (!selected.Valid || Find.WorldGrid?.Surface == null || selected.Layer != Find.WorldGrid.Surface)
                return false;
            tileId = selected.tileId;
            tile = Find.WorldGrid[tileId];
            return tile != null;
        }

        private static void Append_StoneTypes(StringBuilder sb)
        {
            if (!TryGetSurfaceTile(out int selTileID, out Tile selTile))
                return;

            // Only show stone types for buildable tiles
            if (selTile.PrimaryBiome.canBuildBase)
            {
                string stoneTypesLabel = "StoneTypesHere".Translate();
                var stoneTypes = Find.World.NaturalRockTypesIn(selTileID);

                // Format as comma-separated list: "Sandstone, granite, limestone"
                string stoneList = stoneTypes
                    .Select(stone => stone.label)
                    .ToCommaList(true) // useAnd: true
                    .CapitalizeFirst();

                // Use Append extension from WorldInfoStrings
                sb.Append(stoneTypesLabel, stoneList, translate: false);
            }
        }

        /// <summary>
        /// Appends outdoor growing period description.
        /// Uses vanilla's accurate calculation method (same as MapMode_GrowingPeriod).
        /// </summary>
        private static void Append_GrowingQuadrums(StringBuilder sb)
        {
            if (!TryGetSurfaceTile(out int selTileID, out _))
                return;

            string growingPeriodLabel = "OutdoorGrowingPeriod".Translate();
            string growingPeriodDesc = Planets.WorldGen.Climate.ClimateUtility.FormatGrowingPeriodDescription(selTileID);

            // Use Append extension from WorldInfoStrings
            sb.Append(growingPeriodLabel, growingPeriodDesc, translate: false);
        }

        /// <summary>
        /// Appends average disease frequency for the tile's biome.
        /// Calculated from biome's mean time between diseases (MTB).
        /// Format: "X.X per year"
        /// </summary>
        private static void Append_DiseaseFrequency(StringBuilder sb)
        {
            if (!TryGetSurfaceTile(out _, out Tile selTile))
                return;
            if (selTile.PrimaryBiome == null || selTile.PrimaryBiome.diseaseMtbDays <= 0f)
                return;

            string diseaseLabel = "AverageDiseaseFrequency".Translate();

            // Calculate diseases per year from mean time between (MTB) in days
            // MTB = mean time between, so frequency = RimWorld days per year / MTB days
            float diseasesPerYear = EarthScaleConstants.RIMWORLD_DAYS_PER_YEAR / selTile.PrimaryBiome.diseaseMtbDays;
            string diseaseValue = $"{diseasesPerYear:F1} {"PerYear".Translate()}";

            // Use Append extension from WorldInfoStrings
            sb.Append(diseaseLabel, diseaseValue, translate: false);
        }
    }
}
