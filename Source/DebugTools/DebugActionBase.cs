using System;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate;

namespace Planets.DebugTools
{
    /// <summary>
    /// Base class for all Realistic Planets debug actions
    /// Provides consistent logging, error handling, and utility methods
    /// </summary>
    public static class DebugActionBase
    {
        // Dev mode menu categories
        public const string CATEGORY_TOOLS = "RP Tools";
        public const string CATEGORY_EXPORTS = "RP Exports";
        
        /// <summary>
        /// Standard logging method for all debug actions
        /// </summary>
        public static void LogDebugInfo(string message, string category = "General")
        {
            string formattedMessage = $"[Realistic Planets - {category}] {message}";
            
            switch (category.ToLower())
            {
                case "climate":
                    RPLog.Climate(message);
                    break;
                case "compatibility":
                    RPLog.Compat(message);
                    break;
                default:
                    RPLog.D(formattedMessage);
                    break;
            }
        }
        
        /// <summary>
        /// Standard error logging for debug actions
        /// </summary>
        public static void LogError(string error, string context = "")
        {
            string message = string.IsNullOrEmpty(context) ? 
                error : 
                $"{context}: {error}";
                
            RPLog.E(message);
        }
        
        /// <summary>
        /// Check if world is loaded and show appropriate message if not
        /// </summary>
        public static bool ValidateWorldLoaded(string actionName)
        {
            if (Find.World == null)
            {
                Messages.Message($"{actionName} requires a loaded world", MessageTypeDefOf.RejectInput);
                LogError($"{actionName} called without loaded world");
                return false;
            }
            return true;
        }
        
        
        /// <summary>
        /// Get the tile under cursor with validation
        /// </summary>
        public static int GetValidCursorTile(string actionName)
        {
            PlanetTile mouseTile = GenWorld.MouseTile();
            if (!mouseTile.Valid || Find.WorldGrid?.Surface == null || mouseTile.Layer != Find.WorldGrid.Surface)
            {
                Messages.Message($"{actionName} requires cursor to be over a surface tile",
                                MessageTypeDefOf.RejectInput);
                return -1;
            }
            int tileID = mouseTile.tileId;
            if (tileID < 0)
            {
                Messages.Message($"{actionName} requires cursor to be over a valid tile", 
                                MessageTypeDefOf.RejectInput);
                return -1;
            }
            return tileID;
        }
        
        /// <summary>
        /// Create standardized report header
        /// </summary>
        public static StringBuilder CreateReportHeader(string title, string subtitle = "")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {title} ===");
            if (!string.IsNullOrEmpty(subtitle))
            {
                sb.AppendLine(subtitle);
            }
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            return sb;
        }
        
        /// <summary>
        /// Show result dialog with consistent formatting
        /// </summary>
        public static void ShowResultDialog(string content, string title, bool isError = false)
        {
            var dialog = new Dialog_MessageBox(content, title);
            Find.WindowStack.Add(dialog);
            
            if (isError)
            {
                LogError(content, title);
            }
            else
            {
                RPLog.D($"{title}: {content.Substring(0, Math.Min(content.Length, 100))}...");
            }
        }
        
        /// <summary>
        /// Safe execution wrapper for debug actions
        /// </summary>
        public static void SafeExecute(string actionName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                string errorMessage = $"Error in {actionName}: {e.Message}";
                LogError($"{errorMessage}\n{e.StackTrace}");
                Messages.Message($"Debug action failed: {e.Message}", MessageTypeDefOf.RejectInput);
            }
        }
        
        /// <summary>
        /// Format coordinate string consistently
        /// </summary>
        public static string FormatCoordinates(Vector2 coords)
        {
            return $"{Math.Abs(coords.y):F1}°{(coords.y >= 0 ? "N" : "S")}, {Math.Abs(coords.x):F1}°{(coords.x >= 0 ? "E" : "W")}";
        }
        
        /// <summary>
        /// Format percentage string consistently
        /// </summary>
        public static string FormatPercentage(float value, int decimals = 1)
        {
            return $"{(value * 100f).ToString($"F{decimals}")}%";
        }
        
        /// <summary>
        /// Get world statistics summary
        /// </summary>
        public static string GetWorldStatsSummary()
        {
            if (Find.World == null) return "No world loaded";
            
            int totalTiles = Find.WorldGrid.TilesCount;
            int landTiles = 0;
            int oceanTiles = 0;
            
            for (int i = 0; i < totalTiles; i++)
            {
                if (Find.WorldGrid[i].WaterCovered)
                    oceanTiles++;
                else
                    landTiles++;
            }
            
            return $"World: {totalTiles} tiles ({landTiles} land, {oceanTiles} ocean)";
        }
    }
}
