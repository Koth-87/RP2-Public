using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace Planets.Compatibility.GeologicalLandforms
{
    internal static class RP2TileLookup
    {
        public static bool TryGetTile(Vector3 pos, out int tileId, out SurfaceTile tile)
        {
            tileId = GeologicalLandformsCompat.GetTileIDAt(pos);
            if (tileId < 0 || Find.WorldGrid == null || tileId >= Find.WorldGrid.TilesCount)
            {
                tile = null;
                return false;
            }

            tile = Find.WorldGrid[tileId];
            return true;
        }

        public static bool TryGetTile(int tileId, out SurfaceTile tile)
        {
            if (tileId < 0 || Find.WorldGrid == null || tileId >= Find.WorldGrid.TilesCount)
            {
                tile = null;
                return false;
            }
            tile = Find.WorldGrid[tileId];
            return true;
        }
    }

    public interface IRP2NoiseAdapter
    {
        double GetValueForTile(int tileId);
    }

    /// <summary>
    /// Translation layer adapters that make RP2's terrain data look like vanilla noise modules to GL.
    /// GL's reverse-patched GenerateTileFor samples these "noise" modules to generate fake tiles
    /// for biome transition calculations. Our adapters return RP2's actual terrain data instead.
    /// This ensures GL's caves generate under RP2's actual mountains and transitions use RP2's climate data.
    /// </summary>
    
    /// <summary>
    /// Adapter for elevation - feeds RP2's actual elevation to GL.
    /// GL uses this to:
    /// - Detect tiles below sea level (filter out ocean tiles for cave generation)
    /// - Calculate biome transitions at different elevations
    /// </summary>
    public class RP2ElevationAdapter : ModuleBase, IRP2NoiseAdapter
    {
        /// <summary>
        /// Constructor - pass 0 to base because we have no source noise modules.
        /// We generate values directly from RP2's terrain data.
        /// </summary>
        public RP2ElevationAdapter() : base(0)
        {
        }
        
        public override double GetValue(double x, double y, double z)
        {
            var pos = new Vector3((float)x, (float)y, (float)z);

            if (!RP2TileLookup.TryGetTile(pos, out _, out var tile))
                return 0.0;

            return tile.elevation;
        }

        public double GetValueForTile(int tileId)
        {
            if (!RP2TileLookup.TryGetTile(tileId, out var tile))
                return 0.0;
            return tile.elevation;
        }
    }
    
    /// <summary>
    /// Adapter for hilliness - maps RP2's hilliness categories to continuous noise values.
    /// GL uses this to:
    /// - Detect impassable mountains (threshold ~0.036) for cave network generation
    /// - Calculate terrain ruggedness for biome transitions
    /// </summary>
    public class RP2HillinessAdapter : ModuleBase, IRP2NoiseAdapter
    {
        /// <summary>
        /// Constructor - pass 0 to base because we have no source noise modules.
        /// </summary>
        public RP2HillinessAdapter() : base(0)
        {
        }
        
        // GL uses impassableThreshold = 0.03629999980330467 to detect mountains
        public override double GetValue(double x, double y, double z)
        {
            var pos = new Vector3((float)x, (float)y, (float)z);

            if (!RP2TileLookup.TryGetTile(pos, out _, out var tile))
                return 0.0;

            return HillinessToNoiseValue(tile.hilliness);
        }

        public double GetValueForTile(int tileId)
        {
            if (!RP2TileLookup.TryGetTile(tileId, out var tile))
                return 0.0;
            return HillinessToNoiseValue(tile.hilliness);
        }
        
        /// <summary>
        /// Maps RimWorld hilliness enum to continuous noise values.
        /// Values calibrated to match vanilla noiseMountainLines thresholds.
        /// </summary>
        private static double HillinessToNoiseValue(Hilliness h)
        {
            return h switch
            {
                Hilliness.Impassable => 0.0,      // <= 0.0363 (impassable)
                Hilliness.Mountainous => 0.08,    // 0.0363 < x <= 0.12 (mountainous)
                Hilliness.LargeHills => 0.3,      // > 0.235 (flat/hills branch)
                Hilliness.SmallHills => 0.3,      // > 0.235 (flat/hills branch)
                Hilliness.Flat => 0.3,            // > 0.235 (flat/hills branch)
                _ => 0.3
            };
        }
    }
    
    /// <summary>
    /// Adapter for hills patch noise - indicates whether a tile should be hilly.
    /// Used by vanilla GenerateTileFor when noiseMountainLines is in the flat/hills range.
    /// </summary>
    public class RP2HillsPatchesAdapter : ModuleBase, IRP2NoiseAdapter
    {
        /// <summary>
        /// Constructor - pass 0 to base because we have no source noise modules.
        /// </summary>
        public RP2HillsPatchesAdapter() : base(0)
        {
        }

        public override double GetValue(double x, double y, double z)
        {
            var pos = new Vector3((float)x, (float)y, (float)z);

            if (!RP2TileLookup.TryGetTile(pos, out _, out var tile))
                return 0.0;

            bool isHilly = tile.hilliness == Hilliness.SmallHills || tile.hilliness == Hilliness.LargeHills;
            return isHilly ? 1.0 : -1.0;
        }

        public double GetValueForTile(int tileId)
        {
            if (!RP2TileLookup.TryGetTile(tileId, out var tile))
                return 0.0;
            bool isHilly = tile.hilliness == Hilliness.SmallHills || tile.hilliness == Hilliness.LargeHills;
            return isHilly ? 1.0 : -1.0;
        }
    }

    /// <summary>
    /// Adapter for temperature offset - feeds vanilla-style temperature offsets to GL.
    /// GL uses this to reconstruct absolute temperature in GenerateTileFor.
    /// </summary>
    public class RP2TemperatureOffsetAdapter : ModuleBase, IRP2NoiseAdapter
    {
        /// <summary>
        /// Constructor - pass 0 to base because we have no source noise modules.
        /// </summary>
        public RP2TemperatureOffsetAdapter() : base(0)
        {
        }
        
        public override double GetValue(double x, double y, double z)
        {
            var pos = new Vector3((float)x, (float)y, (float)z);
            return GetValueInternal(pos, -1);
        }

        public double GetValueForTile(int tileId)
        {
             if (Find.WorldGrid == null || tileId < 0 || tileId >= Find.WorldGrid.TilesCount) return 0.0;
             var pos = Find.WorldGrid.GetTileCenter(tileId);
             return GetValueInternal(pos, tileId);
        }

        private double GetValueInternal(Vector3 pos, int tileId)
        {
             if (tileId == -1)
             {
                 if (!RP2TileLookup.TryGetTile(pos, out tileId, out _))
                    return 0.0;
             }
             
             // Optimized lookup now that we have tileId if passed
             var tile = Find.WorldGrid[tileId];

            SimpleCurve temperatureCurve = null;
            if (Find.World != null)
                temperatureCurve = Find.World.info.overallTemperature.GetTemperatureCurve();
            float targetTemp = tile.temperature;
            float preCurveTemp = temperatureCurve != null ? temperatureCurve.EvaluateInverted(targetTemp) : targetTemp;
            float baseTemp = BaseTemperatureAtLatitude(LatitudeFromPos(pos)) - TemperatureReductionAtElevation(tile.elevation);
            return preCurveTemp - baseTemp;
        }

        private static readonly SimpleCurve AvgTempByLatitudeCurve = new SimpleCurve()
        {
            { new CurvePoint(0.0f, 30f), true },
            { new CurvePoint(0.1f, 29f), true },
            { new CurvePoint(0.5f, 7f), true },
            { new CurvePoint(1f, -37f), true }
        };

        private static float BaseTemperatureAtLatitude(float lat)
        {
            float x = Mathf.Abs(lat) / 90f;
            return AvgTempByLatitudeCurve.Evaluate(x);
        }

        private static float TemperatureReductionAtElevation(float elev)
        {
            if (elev < 250f)
                return 0f;

            return Mathf.Lerp(0f, 40f, (elev - 250f) / 4750f);
        }

        private static float LatitudeFromPos(Vector3 pos)
        {
            float normalized = Mathf.Clamp(pos.y / WorldGrid.PlanetRadius, -1f, 1f);
            return Mathf.Asin(normalized) * Mathf.Rad2Deg;
        }
    }
    
    /// <summary>
    /// Adapter for rainfall - feeds RP2's actual annual rainfall to GL.
    /// GL uses this for biome transition calculations at tile boundaries.
    /// </summary>
    public class RP2RainfallAdapter : ModuleBase, IRP2NoiseAdapter
    {
        /// <summary>
        /// Constructor - pass 0 to base because we have no source noise modules.
        /// </summary>
        public RP2RainfallAdapter() : base(0)
        {
        }
        
        public override double GetValue(double x, double y, double z)
        {
            var pos = new Vector3((float)x, (float)y, (float)z);

            if (!RP2TileLookup.TryGetTile(pos, out _, out var tile))
                return 0.0;

            // Return annual rainfall in mm
            return tile.rainfall;
        }

        public double GetValueForTile(int tileId)
        {
             if (!RP2TileLookup.TryGetTile(tileId, out var tile))
                return 0.0;
             return tile.rainfall;
        }
    }

    /// <summary>
    /// Adapter for swampiness - feeds RP2's actual swampiness to GL.
    /// GL uses this for biome transition calculations at tile boundaries.
    /// </summary>
    public class RP2SwampinessAdapter : ModuleBase, IRP2NoiseAdapter
    {
        /// <summary>
        /// Constructor - pass 0 to base because we have no source noise modules.
        /// </summary>
        public RP2SwampinessAdapter() : base(0)
        {
        }
        
        public override double GetValue(double x, double y, double z)
        {
            var pos = new Vector3((float)x, (float)y, (float)z);

            if (!RP2TileLookup.TryGetTile(pos, out _, out var tile))
                return 0.0;

            // Return swampiness (0-1)
            return tile.swampiness;
        }

        public double GetValueForTile(int tileId)
        {
             if (!RP2TileLookup.TryGetTile(tileId, out var tile))
                return 0.0;
             return tile.swampiness;
        }
    }
}
