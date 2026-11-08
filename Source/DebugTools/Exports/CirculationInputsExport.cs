using System.Collections;
using System.Globalization;
using System.IO;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Climate;
using Planets.WorldGen.Climate.Circulation;
using Planets.WorldGen.Climate.Mesh;

namespace Planets.DebugTools.Exports
{
    /// <summary>
    /// Writes what the new-climate wind solve reads, on its mesh, plus the wind the game stored.
    /// Calibration/circulation_replay.py rebuilds the wind from these files with other settings.
    /// </summary>
    public static class CirculationInputsExport
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [DebugAction(DebugActionBase.CATEGORY_EXPORTS, "Export Circulation Inputs", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void Export()
        {
            if (!Climate.IsInitialized())
            {
                Messages.Message("Climate system not initialized - generate a world first", MessageTypeDefOf.RejectInput);
                return;
            }

            LongEventHandler.QueueLongEvent(Write(), "Exporting circulation inputs", exception => RPLog.E($"Circulation export failed: {exception}"));
        }

        static IEnumerable Write()
        {
            ClimateData climate = Find.World?.GetComponent<ClimateData>();
            if (climate?.EquinoxWind == null || climate.AnnualMeanTemp == null || climate.TileCenters == null)
            {
                Messages.Message("The new-climate wind has not been computed for this world", MessageTypeDefOf.RejectInput);
                yield break;
            }

            ClimateConfiguration config = climate.bakeRecord?.climateSettings ?? Controller.Settings.climateConfig;
            var layer = new CirculationLayer(climate, config);
            LongEventHandler.SetCurrentEventText("Building circulation inputs");
            yield return null;
            CirculationLayer.MeshInputs inputs = layer.BuildInputs();
            CoarseMesh mesh = inputs.Mesh;

            string cellsName = ExportWriter.GetFilename("CirculationCells", "csv");
            StreamWriter writer = ExportWriter.OpenTemp(cellsName, out string tempPath, out string finalPath);
            try
            {
                WriteHeader(writer, climate, config, inputs);
                writer.WriteLine("Cell,X,Y,Z,AreaM2,TileCount,LandFrac,ElevKm,AnnualC,AmpC,OceanDistKm,LandTiles,WallTiles,RidgeSumX,RidgeSumY,RidgeSumZ,UphillX,UphillY,UphillZ,Barrier,Neighbors,EdgeLengthsM");
                for (int c = 0; c < mesh.Count; c++)
                {
                    Vector3 p = mesh.Position[c];
                    Vector3 ridge = inputs.RidgeNormalSum[c];
                    Vector3 up = inputs.Uphill[c];
                    writer.WriteLine(string.Join(",",
                        c.ToString(Inv),
                        G(p.x), G(p.y), G(p.z),
                        G(mesh.AreaM2[c]),
                        inputs.TileCount[c].ToString(Inv),
                        G(inputs.LandFrac[c]),
                        G(inputs.ElevKm[c]),
                        G(inputs.Annual[c]),
                        G(inputs.Amp[c]),
                        G(inputs.OceanDistKm[c]),
                        inputs.LandTiles[c].ToString(Inv),
                        inputs.WallTiles[c].ToString(Inv),
                        G(ridge.x), G(ridge.y), G(ridge.z),
                        G(up.x), G(up.y), G(up.z),
                        inputs.Barrier[c] ? "1" : "0",
                        string.Join("|", mesh.Neighbors[c]),
                        JoinFloats(mesh.EdgeLengthM[c])));
                    if (c % 8000 == 0)
                    {
                        LongEventHandler.SetCurrentEventText($"Writing cells {c}/{mesh.Count}");
                        yield return null;
                    }
                }
            }
            finally
            {
                writer.Dispose();
            }
            ExportWriter.FinishTemp(tempPath, finalPath);

            string trisName = ExportWriter.GetFilename("CirculationTriangles", "csv");
            writer = ExportWriter.OpenTemp(trisName, out tempPath, out finalPath);
            try
            {
                writer.WriteLine("# Mesh triangles. Corner order is the order the game uses for barycentric sampling.");
                writer.WriteLine("Tri,A,B,C");
                for (int t = 0; t < mesh.TriA.Length; t++)
                    writer.WriteLine($"{t},{mesh.TriA[t]},{mesh.TriB[t]},{mesh.TriC[t]}");
            }
            finally
            {
                writer.Dispose();
            }
            ExportWriter.FinishTemp(tempPath, finalPath);
            yield return null;

            int tiles = climate.TileCenters.Length;
            string tilesName = ExportWriter.GetFilename("CirculationTiles", "csv");
            writer = ExportWriter.OpenTemp(tilesName, out tempPath, out finalPath);
            try
            {
                writer.WriteLine("# One row per tile. Wind columns are the stored new-climate wind, east and north in the tile frame, m/s.");
                writer.WriteLine("# IsLand and IsWall are the circulation layer's own tests. A plateau tile is land, not a wall, and at or above PlateauKm.");
                writer.WriteLine("Tile,Cell,X,Y,Z,Lat,Lon,IsWater,TrueOcean,IsLand,IsWall,ElevKm,JuneEast,JuneNorth,DecEast,DecNorth,EqEast,EqNorth");
                for (int t = 0; t < tiles; t++)
                {
                    Vector3 center = climate.TileCenters[t].normalized;
                    mesh.Frame(center, out Vector3 east, out Vector3 north);
                    Vector3 coords = Find.WorldGrid.LongLatOf(t);
                    bool water = climate.OceanMask != null && t < climate.OceanMask.Length && climate.OceanMask[t];
                    bool trueOcean = climate.TrueOceanMask != null && t < climate.TrueOceanMask.Length && climate.TrueOceanMask[t];
                    float elev = climate.ElevationKm != null ? climate.ElevationKm[t] : 0f;
                    writer.WriteLine(string.Join(",",
                        t.ToString(Inv),
                        inputs.TileCell[t].ToString(Inv),
                        G(center.x), G(center.y), G(center.z),
                        coords.y.ToString("F4", Inv),
                        coords.x.ToString("F4", Inv),
                        water ? "1" : "0",
                        trueOcean ? "1" : "0",
                        layer.IsLandTile(t) ? "1" : "0",
                        layer.IsWallTile(t) ? "1" : "0",
                        G(elev),
                        Part(climate.SummerWind, t, east), Part(climate.SummerWind, t, north),
                        Part(climate.WinterWind, t, east), Part(climate.WinterWind, t, north),
                        Part(climate.EquinoxWind, t, east), Part(climate.EquinoxWind, t, north)));
                    if (t % 20000 == 0)
                    {
                        LongEventHandler.SetCurrentEventText($"Writing tiles {t}/{tiles}");
                        yield return null;
                    }
                }
            }
            finally
            {
                writer.Dispose();
            }
            ExportWriter.FinishTemp(tempPath, finalPath);
            Messages.Message($"Circulation inputs exported to {cellsName}, {trisName}, {tilesName}", MessageTypeDefOf.NeutralEvent);
        }

        static void WriteHeader(StreamWriter writer, ClimateData climate, ClimateConfiguration config, CirculationLayer.MeshInputs inputs)
        {
            float tilt = AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt);
            writer.WriteLine("# Circulation inputs. One row per mesh cell. Cells with TileCount 0 are already filled as ocean.");
            writer.WriteLine($"# WorldSeed: {Find.World.info.seedString}");
            writer.WriteLine($"# ClimateModel: {climate.bakeRecord?.modelVersion ?? 0}");
            writer.WriteLine("# NewClimate: on");
            writer.WriteLine("# Frame: east = cross(radial, (0,1,0)) normalized, or (1,0,0) at a pole. north = cross(east, radial) normalized.");
            writer.WriteLine("# Value AxialTiltDeg: " + G(tilt));
            writer.WriteLine("# Value ItczShiftDeg: " + G(inputs.ItczShift));
            writer.WriteLine("# Value EarthRadiusM: " + G(CoarseMesh.EarthRadiusM));
            writer.WriteLine("# Value Omega: " + G(CirculationLayer.Omega));
            writer.WriteLine("# Value AirDensity: " + G(CirculationLayer.AirDensity));
            writer.WriteLine("# Value LapsePerKm: " + G(CirculationLayer.LapsePerKm));
            writer.WriteLine("# Value CellSpacingKm: " + G(CirculationLayer.CellSpacingKm));
            writer.WriteLine("# Value PlateauKm: " + G(CirculationLayer.PlateauKm));
            writer.WriteLine("# Value BarrierLandShare: " + G(CirculationLayer.BarrierLandShare));
            writer.WriteLine("# Value TroughHalfWidthDeg: " + G(CirculationLayer.TroughHalfWidthDeg));
            writer.WriteLine("# Value circHeatWarmHpaPerC: " + G(config.EffectiveCircHeatWarmHpaPerC));
            writer.WriteLine("# Value circHeatColdHpaPerC: " + G(config.circHeatColdHpaPerC.CurrentValue));
            writer.WriteLine("# Value circColdGateC: " + G(config.circColdGateC.CurrentValue));
            writer.WriteLine("# Value circColdGateSpanC: " + G(config.circColdGateSpanC.CurrentValue));
            writer.WriteLine("# Value circHeatLocalHpaPerC: " + G(config.circHeatLocalHpaPerC.CurrentValue));
            writer.WriteLine("# Value circHeatLocalCoastHpaPerC: " + G(config.circHeatLocalCoastHpaPerC.CurrentValue));
            writer.WriteLine("# Value circLocalCoastKm: " + G(config.circLocalCoastKm.CurrentValue));
            writer.WriteLine("# Value circSmoothingKm: " + G(config.circSmoothingKm.CurrentValue));
            writer.WriteLine("# Value circBroadSmoothingKm: " + G(config.circBroadSmoothingKm.CurrentValue));
            writer.WriteLine("# Value circFrictionOcean: " + G(config.circFrictionOcean.CurrentValue));
            writer.WriteLine("# Value circFrictionLand: " + G(config.circFrictionLand.CurrentValue));
            writer.WriteLine("# Value circPressureProfileScale: " + G(config.circPressureProfileScale.CurrentValue));
            writer.WriteLine("# Value circCurveOceanPower: " + G(config.circCurveOceanPower.CurrentValue));
            writer.WriteLine("# Value circSeasonalProfileScale: " + G(config.EffectiveCircSeasonalProfileScale));
            writer.WriteLine("# Value circOceanReference: " + G(config.circOceanReference.CurrentValue));
            writer.WriteLine("# Value circSurfaceOcean: " + G(config.circSurfaceOcean.CurrentValue));
            writer.WriteLine("# Value circSurfaceLand: " + G(config.circSurfaceLand.CurrentValue));
            writer.WriteLine("# PressureProfile: " + Table(ClimateReferenceData.SamplePressureProfile()));
            writer.WriteLine("# SeasonalDelta: " + Table(ClimateReferenceData.SampleSeasonalPressureDelta()));
            writer.WriteLine("# OceanShareReference: " + Table(ClimateReferenceData.SampleOceanShareReference()));
        }

        static string Table((float x, float y)[] table)
        {
            var points = new string[table.Length];
            for (int i = 0; i < table.Length; i++)
                points[i] = G(table[i].x) + ":" + G(table[i].y);
            return string.Join("|", points);
        }

        static string Part(Vector3[] wind, int tile, Vector3 axis)
        {
            if (wind == null || tile >= wind.Length)
                return "0";
            return Vector3.Dot(wind[tile], axis).ToString("F5", Inv);
        }

        static string G(float value)
        {
            return value.ToString("G9", Inv);
        }

        static string JoinFloats(float[] values)
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                parts[i] = G(values[i]);
            return string.Join("|", parts);
        }
    }
}
