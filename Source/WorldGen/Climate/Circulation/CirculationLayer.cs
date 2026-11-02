using System;
using System.Threading.Tasks;
using RimWorld.Planet;
using UnityEngine;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Climate;
using Planets.WorldGen.Climate.Mesh;
using Planets.WorldGen.Constants;

namespace Planets.WorldGen.Climate.Circulation
{
    /// <summary>
    /// Steady diagnostic wind. Pressure is a latitude profile plus the warmth or cold of the air
    /// relative to the ocean at its own latitude. The profile is the ocean hemisphere's, scaled by
    /// how much of each latitude band is ocean, with the measured winter-minus-summer shape added.
    /// Two layers share that profile. A warm departure over land
    /// is a deep column, so both layers feel it at the warm strength. A warm departure over ocean
    /// uses the cold strength. A coastal cell blends the two by its land fraction. A cold departure
    /// is shallow, so only the lower layer feels it. The broad part of the anomaly, smoothed again
    /// to about 2,500 km, carries those strengths. The leftover after the 400 km smooth is 0.75 hPa per degree inland and 1 at the ocean.
    /// A stronger leftover near the ocean was tried and taken back. The lower layer cannot cross a mountain wall.
    /// A high plateau that is not itself a wall uses the upper layer, so the plateau is not a barrier.
    /// The balance gives the layer wind. The surface wind is a fixed share of it, smaller over land.
    /// Runs only when the new-climate toggle is on, and replaces the latitude-belt winds.
    /// </summary>
    public class CirculationLayer : IClimateLayer
    {
        public const float Omega = 7.2921e-5f;
        public const float AirDensity = 1.2f;
        /// <summary>Same as the temperature layer's land rate, so sea-level-equivalent temperature does not change when that rate does.</summary>
        public const float LapsePerKm = ClimateReferenceData.LandSurfaceLapsePerKm;
        public const float CellSpacingKm = 110f;
        /// <summary>A non-wall surface at or above this height is in the upper flow. Tibet's interior is near 5 km and is large hills, not a wall. The Alps sit well below this and are blocked as a wall instead.</summary>
        public const float PlateauKm = 2f;
        /// <summary>A mesh cell blocks the lower wind when at least half its land tiles are a wall, and it has at least two land tiles. One ridge tile does not turn a whole 110 km cell into a wall.</summary>
        public const float BarrierLandShare = 0.5f;
        /// <summary>The equatorial trough follows the thermal equator only within this many degrees of it.</summary>
        public const float TroughHalfWidthDeg = 18f;

        readonly ClimateData climate;
        readonly ClimateConfiguration config;
        readonly int tileCount;
        MeshInputs cachedInputs;
        int[] baryA;
        int[] baryB;
        int[] baryC;
        float[] baryWA;
        float[] baryWB;
        float[] baryWC;
        Vector3[] tileEast;
        Vector3[] tileNorth;
        bool[] isPlateau;

        public string LayerName => "Circulation";

        /// <summary>Everything the wind solve reads, on the mesh. Built once per world. The debug export writes these so the solve can be replayed outside the game.</summary>
        public sealed class MeshInputs
        {
            public CoarseMesh Mesh;
            public int[] TileCell;
            public int[] CellCoverTile;
            public int[] TileCount;
            public float[] LandFrac;
            public float[] ElevKm;
            public float[] Annual;
            public float[] Amp;
            public float[] Cont;
            public float[] OceanDistKm;
            public int[] LandTiles;
            public int[] WallTiles;
            /// <summary>Sum of the cell's ridge normals, each flipped to agree with the first. Zero when the cell has no ridge tile.</summary>
            public Vector3[] RidgeNormalSum;
            /// <summary>Unit uphill direction from a plane fit through the cell's land tiles. Zero when the fit fails.</summary>
            public Vector3[] Uphill;
            public bool[] Barrier;
            public Vector3[] BarrierNormal;
            public int Hidden;
            public int BarrierCells;
            public float ItczShift;
            public float TiltDeg;
        }

        /// <summary>The circulation numbers for one world, read once from the configuration.</summary>
        sealed class SolveSettings
        {
            public float HeatWarm;
            public float HeatCold;
            public float HeatLocal;
            public float HeatLocalCoast;
            public float CoastKm;
            public int SmoothSteps;
            public int BroadSteps;
            public float FrictionOcean;
            public float FrictionLand;
            public float ProfileScale;
            public float CurveOceanPower;
            public float SeasonalScale;
            public float TiltFactor;
            public float OceanReference;
            public float SurfaceOcean;
            public float SurfaceLand;
            /// <summary>At or above this air temperature a cold departure makes no cold high. 900 or more is off.</summary>
            public float ColdGateC;
            public float ColdGateSpan;
            /// <summary>This world's ocean share in 36 bands of 5°, from the south, by area.</summary>
            public float[] BandOceanShare;
        }

        public CirculationLayer(ClimateData climateData, ClimateConfiguration configuration)
        {
            climate = climateData;
            config = configuration;
            tileCount = climate.Latitude.Length;
        }

        public MeshInputs BuildInputs()
        {
            CoarseMesh mesh = CoarseMesh.Get();
            int cells = mesh.Count;
            if (cachedInputs != null)
            {
                Array.Clear(cachedInputs.TileCount, 0, cells);
                AggregateTiles(mesh, cachedInputs.TileCell, cachedInputs.LandFrac, cachedInputs.ElevKm, cachedInputs.Annual, cachedInputs.Amp, cachedInputs.Cont, cachedInputs.TileCount);
                if (cachedInputs.CellCoverTile != null)
                {
                    for (int c = 0; c < cells; c++)
                    {
                        int bestTile = cachedInputs.CellCoverTile[c];
                        if (bestTile < 0)
                            continue;
                        cachedInputs.TileCount[c] = 1;
                        bool land = climate.OceanMask == null || !climate.OceanMask[bestTile];
                        cachedInputs.LandFrac[c] = land ? 1f : 0f;
                        cachedInputs.ElevKm[c] = climate.ElevationKm != null ? climate.ElevationKm[bestTile] : 0f;
                        float refOcean = climate.LatitudinalSeaLevelTemp != null && bestTile < climate.LatitudinalSeaLevelTemp.Length
                            ? climate.LatitudinalSeaLevelTemp[bestTile]
                            : climate.AnnualMeanTemp[bestTile];
                        cachedInputs.Annual[c] = land ? climate.AnnualMeanTemp[bestTile] : refOcean;
                        cachedInputs.Amp[c] = climate.SeasonalAmp[bestTile];
                        cachedInputs.Cont[c] = land && climate.Continentality != null && bestTile < climate.Continentality.Length
                            ? climate.Continentality[bestTile]
                            : 0f;
                    }
                }
                for (int c = 0; c < cells; c++)
                {
                    if (cachedInputs.TileCount[c] > 0)
                        continue;
                    float lat = Mathf.Asin(Mathf.Clamp(mesh.Position[c].y, -1f, 1f));
                    cachedInputs.Annual[c] = HiddenOceanTemperature(lat);
                    cachedInputs.Amp[c] = 4f + 10f * Mathf.Abs(Mathf.Sin(lat));
                }
                cachedInputs.ItczShift = ItczShiftDegrees();
                cachedInputs.TiltDeg = AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt);
                return cachedInputs;
            }

            var inputs = new MeshInputs
            {
                Mesh = mesh,
                TileCell = MapTiles(mesh),
                TileCount = new int[cells],
                LandFrac = new float[cells],
                ElevKm = new float[cells],
                Annual = new float[cells],
                Amp = new float[cells],
                Cont = new float[cells],
                ItczShift = ItczShiftDegrees(),
                TiltDeg = AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt),
            };
            AggregateTiles(mesh, inputs.TileCell, inputs.LandFrac, inputs.ElevKm, inputs.Annual, inputs.Amp, inputs.Cont, inputs.TileCount);
            inputs.CellCoverTile = FillUncoveredVisibleCells(mesh, inputs);

            for (int c = 0; c < cells; c++)
            {
                if (inputs.TileCount[c] > 0)
                    continue;
                inputs.Hidden++;
                inputs.LandFrac[c] = 0f;
                inputs.ElevKm[c] = -1f;
                float lat = Mathf.Asin(Mathf.Clamp(mesh.Position[c].y, -1f, 1f));
                inputs.Annual[c] = HiddenOceanTemperature(lat);
                inputs.Amp[c] = 4f + 10f * Mathf.Abs(Mathf.Sin(lat));
            }

            inputs.OceanDistKm = OceanDistanceKm(mesh, inputs.TileCell, inputs.CellCoverTile);
            AggregateWalls(mesh, inputs);
            inputs.Barrier = new bool[cells];
            inputs.BarrierNormal = new Vector3[cells];
            inputs.BarrierCells = ResolveBarriers(inputs, BarrierLandShare);
            PrecomputeTileSampling(mesh, inputs.TileCell);
            cachedInputs = inputs;
            return inputs;
        }

        void PrecomputeTileSampling(CoarseMesh mesh, int[] tileCell)
        {
            baryA = new int[tileCount];
            baryB = new int[tileCount];
            baryC = new int[tileCount];
            baryWA = new float[tileCount];
            baryWB = new float[tileCount];
            baryWC = new float[tileCount];
            tileEast = new Vector3[tileCount];
            tileNorth = new Vector3[tileCount];
            isPlateau = new bool[tileCount];
            float plateauDepth = Mathf.Exp(-PlateauKm / 8f);

            Parallel.For(0, tileCount, t =>
            {
                Vector3 center = climate.TileCenters[t].normalized;
                mesh.Frame(center, out tileEast[t], out tileNorth[t]);
                FindBarycentric(mesh, center, tileCell[t],
                    out baryA[t], out baryB[t], out baryC[t],
                    out baryWA[t], out baryWB[t], out baryWC[t]);
                isPlateau[t] = IsLandTile(t) && !IsWallTile(t) && (
                    climate.ColumnDepth != null && t < climate.ColumnDepth.Length
                        ? climate.ColumnDepth[t] <= plateauDepth
                        : (climate.ElevationKm != null && climate.ElevationKm[t] >= PlateauKm));
            });
        }

        public void Execute(ClimateData climateData)
        {
            MeshInputs inputs = BuildInputs();
            CoarseMesh mesh = inputs.Mesh;
            int[] tileCell = inputs.TileCell;
            int cells = mesh.Count;
            if (inputs.Hidden > 0 && Controller.DebugEnabled)
                RPLog.D($"[Circulation] {inputs.Hidden} mesh cells have no tiles and are treated as ocean.");

            Ensure(ref climate.EquinoxWind);
            Ensure(ref climate.JuneConvergence);
            Ensure(ref climate.DecemberConvergence);
            Ensure(ref climate.EquinoxConvergence);

            int smoothSteps = StepsForKm(config.circSmoothingKm.CurrentValue);
            var settings = new SolveSettings
            {
                HeatWarm = config.EffectiveCircHeatWarmHpaPerC,
                HeatCold = config.circHeatColdHpaPerC.CurrentValue,
                HeatLocal = config.circHeatLocalHpaPerC.CurrentValue,
                HeatLocalCoast = config.circHeatLocalCoastHpaPerC.CurrentValue,
                CoastKm = config.circLocalCoastKm.CurrentValue,
                SmoothSteps = smoothSteps,
                BroadSteps = Mathf.Max(smoothSteps, StepsForKm(config.circBroadSmoothingKm.CurrentValue)),
                FrictionOcean = config.circFrictionOcean.CurrentValue,
                FrictionLand = config.circFrictionLand.CurrentValue,
                ProfileScale = config.circPressureProfileScale.CurrentValue,
                CurveOceanPower = config.circCurveOceanPower.CurrentValue,
                SeasonalScale = config.EffectiveCircSeasonalProfileScale,
                TiltFactor = Mathf.Sin(inputs.TiltDeg * Mathf.Deg2Rad) / Mathf.Sin(22.5f * Mathf.Deg2Rad),
                OceanReference = config.circOceanReference.CurrentValue,
                SurfaceOcean = config.circSurfaceOcean.CurrentValue,
                SurfaceLand = config.circSurfaceLand.CurrentValue,
                ColdGateC = config.circColdGateC.CurrentValue,
                ColdGateSpan = Mathf.Max(0.1f, config.circColdGateSpanC.CurrentValue),
                BandOceanShare = BandOceanShare(mesh, inputs.LandFrac),
            };
            float itcz = inputs.ItczShift;
            if (Controller.DebugEnabled)
                RPLog.D($"[Circulation] {inputs.BarrierCells} mesh cells block the lower wind.");

            var juneU = new float[cells];
            var juneV = new float[cells];
            var juneUpperU = new float[cells];
            var juneUpperV = new float[cells];
            var decU = new float[cells];
            var decV = new float[cells];
            var decUpperU = new float[cells];
            var decUpperV = new float[cells];
            var eqU = new float[cells];
            var eqV = new float[cells];
            var eqUpperU = new float[cells];
            var eqUpperV = new float[cells];
            var juneDiv = new float[cells];
            var decDiv = new float[cells];
            var eqDiv = new float[cells];
            var juneTU = new float[cells];
            var juneTV = new float[cells];
            var decTU = new float[cells];
            var decTV = new float[cells];
            var eqTU = new float[cells];
            var eqTV = new float[cells];

            Ensure(ref climate.TransportJune);
            Ensure(ref climate.TransportDecember);
            Ensure(ref climate.TransportEquinox);

            float[] itczBySnap = { itcz, -itcz, 0f };
            float[] signBySnap = { 1f, -1f, 0f };
            float[][] lowU = { juneU, decU, eqU };
            float[][] lowV = { juneV, decV, eqV };
            float[][] upU = { juneUpperU, decUpperU, eqUpperU };
            float[][] upV = { juneUpperV, decUpperV, eqUpperV };
            float[][] divs = { juneDiv, decDiv, eqDiv };
            float[][] transU = { juneTU, decTU, eqTU };
            float[][] transV = { juneTV, decTV, eqTV };
            Vector3[][] surfWinds = { climate.SummerWind, climate.WinterWind, climate.EquinoxWind };
            float[][] convs = { climate.JuneConvergence, climate.DecemberConvergence, climate.EquinoxConvergence };
            Vector3[][] transWinds = { climate.TransportJune, climate.TransportDecember, climate.TransportEquinox };

            Parallel.For(0, 3, s =>
            {
                SolveSnapshot(mesh, inputs, settings, itczBySnap[s], signBySnap[s],
                    lowU[s], lowV[s], upU[s], upV[s], divs[s], transU[s], transV[s]);
                WriteSurface(mesh, tileCell, lowU[s], lowV[s], upU[s], upV[s], surfWinds[s], convs[s], divs[s]);
                WriteTransport(mesh, tileCell, transU[s], transV[s], transWinds[s]);
            });
            climate.TileCell = tileCell;
            climate.CellCoverTile = inputs.CellCoverTile;
            climate.CellAnnual = inputs.Annual;
            climate.CellAmp = inputs.Amp;
            climate.CellTransportJuneEast = juneTU;
            climate.CellTransportJuneNorth = juneTV;
            climate.CellTransportDecEast = decTU;
            climate.CellTransportDecNorth = decTV;
            climate.CellTransportEqEast = eqTU;
            climate.CellTransportEqNorth = eqTV;
            WriteAnnual();
            if (Controller.DebugEnabled)
                LogBands(mesh, tileCell, juneU, juneV);
        }

        /// <summary>June adds the seasonal swing in the north and subtracts it in the south. sign is +1 June, -1 December, 0 equinox. itczShift is the thermal equator in degrees north.</summary>
        static void SolveSnapshot(CoarseMesh mesh, MeshInputs inputs, SolveSettings s, float itczShift, float seasonSign,
            float[] lowerEast, float[] lowerNorth, float[] upperEast, float[] upperNorth, float[] divergence,
            float[] transportEast, float[] transportNorth)
        {
            int n = mesh.Count;
            float[] landFrac = inputs.LandFrac;
            float[] elevKm = inputs.ElevKm;
            float[] annual = inputs.Annual;
            float[] amp = inputs.Amp;
            float[] cont = inputs.Cont;
            float[] oceanDistKm = inputs.OceanDistKm;
            var star = new float[n];
            for (int i = 0; i < n; i++)
            {
                float latDeg = Mathf.Asin(Mathf.Clamp(mesh.Position[i].y, -1f, 1f)) * Mathf.Rad2Deg;
                float hemi = latDeg >= 0f ? 1f : -1f;
                float temp = annual[i] + seasonSign * hemi * 0.5f * amp[i];
                if (seasonSign * hemi > 0f && landFrac[i] > 0f)
                {
                    float cSum = 1f - Mathf.Exp(-(oceanDistKm[i] + 1200f * Mathf.Max(0f, elevKm[i])) / 900f);
                    float midLat = Mathf.Clamp01((Mathf.Abs(latDeg) - 26f) / 14f);
                    float highLat = Mathf.Clamp01((Mathf.Abs(latDeg) - 35f) / 25f);
                    float dSum = (12f * (cSum - cont[i]) * midLat - 5.5f * cSum * highLat) * landFrac[i];
                    temp += dSum;
                }
                star[i] = temp + LapsePerKm * Mathf.Max(0f, elevKm[i]);
            }

            var zonal = ZonalMean(mesh, star, landFrac, s.OceanReference);
            var anomaly = new float[n];
            for (int i = 0; i < n; i++)
                anomaly[i] = star[i] - zonal[i];

            // A shallow cold dome needs cold ground. The cold part is gated by the air's own temperature before
            // smoothing, so warm subtropical land in winter makes no cold high while Siberia keeps its own.
            float[] broadCold = null;
            if (s.ColdGateC < 900f)
            {
                broadCold = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float latDeg = Mathf.Asin(Mathf.Clamp(mesh.Position[i].y, -1f, 1f)) * Mathf.Rad2Deg;
                    float air = annual[i] + seasonSign * (latDeg >= 0f ? 1f : -1f) * 0.5f * amp[i];
                    float weight = Mathf.Clamp01((s.ColdGateC - air) / s.ColdGateSpan);
                    broadCold[i] = anomaly[i] < 0f ? anomaly[i] * weight : anomaly[i];
                }
                Smooth(mesh, broadCold, s.SmoothSteps);
                Smooth(mesh, broadCold, s.BroadSteps - s.SmoothSteps);
            }

            Smooth(mesh, anomaly, s.SmoothSteps);
            var broad = (float[])anomaly.Clone();
            Smooth(mesh, broad, s.BroadSteps - s.SmoothSteps);

            var lowerPressure = new float[n];
            var upperPressure = new float[n];
            var background = new float[n];
            float[] summerLand = Mathf.Abs(seasonSign) > 0f ? SummerLand(mesh, landFrac, seasonSign) : null;
            float[] plateauMonsoon = Mathf.Abs(seasonSign) > 0f ? PlateauMonsoon(mesh, landFrac, elevKm, seasonSign) : null;
            float baseItcz = Mathf.Abs(itczShift);
            float monsoonItcz = 20f * s.TiltFactor;
            for (int i = 0; i < n; i++)
            {
                float latDeg = Mathf.Asin(Mathf.Clamp(mesh.Position[i].y, -1f, 1f)) * Mathf.Rad2Deg;
                float landMask = summerLand != null ? summerLand[i] : 1f;
                float itczHere = plateauMonsoon != null
                    ? Mathf.Lerp(baseItcz, monsoonItcz, plateauMonsoon[i]) * seasonSign
                    : itczShift;
                background[i] = SeasonalPressure(latDeg, itczHere, seasonSign, s, landMask);
            }
            // Smooth the background pressure field on the mesh so 5° table knots and 10° longitude bins
            // do not produce second-derivative rings in divergence/convergence.
            Smooth(mesh, background, Mathf.Max(s.SmoothSteps, 16));

            for (int i = 0; i < n; i++)
            {
                float warm = Mathf.Max(0f, broad[i]);
                float cold = Mathf.Min(0f, broadCold != null ? broadCold[i] : broad[i]);
                // The broad piece is the heat low and the cold high. Warm land uses HeatWarm; warm ocean uses HeatCold; coastal cells blend by land fraction.
                float warmPerC = Mathf.Lerp(s.HeatCold, s.HeatWarm, landFrac[i]);
                // The leftover is the 400 km field minus that broad piece, applied only to positive (warm) local anomalies so semi-enclosed warm seas do not get a cold-high cap.
                float coastWeight = s.CoastKm > 1f ? Mathf.Clamp01(oceanDistKm[i] / s.CoastKm) : 1f;
                float localPerC = Mathf.Lerp(s.HeatLocalCoast, s.HeatLocal, coastWeight);
                float local = localPerC * Mathf.Max(0f, anomaly[i] - broad[i]);
                lowerPressure[i] = background[i] - warmPerC * warm - s.HeatCold * cold - local;
                upperPressure[i] = background[i] - warmPerC * warm - local;
            }

            BalanceWind(mesh, lowerPressure, landFrac, s.FrictionOcean, s.FrictionLand, lowerEast, lowerNorth);
            BalanceWind(mesh, upperPressure, landFrac, s.FrictionOcean, s.FrictionLand, upperEast, upperNorth);
            DeflectLower(mesh, inputs.Barrier, inputs.BarrierNormal, lowerEast, lowerNorth);

            for (int i = 0; i < n; i++)
            {
                mesh.Frame(mesh.Position[i], out Vector3 east, out Vector3 north);
                float flux = 0f;
                int[] nbr = mesh.Neighbors[i];
                float[] length = mesh.EdgeLengthM[i];
                for (int k = 0; k < nbr.Length; k++)
                {
                    int j = nbr[k];
                    Vector3 step = mesh.Position[j] - mesh.Position[i];
                    float alongEast = Vector3.Dot(step, east);
                    float alongNorth = Vector3.Dot(step, north);
                    float horiz = Mathf.Sqrt(alongEast * alongEast + alongNorth * alongNorth);
                    if (horiz < 1e-8f)
                        continue;
                    float nEast = alongEast / horiz;
                    float nNorth = alongNorth / horiz;
                    // Subtract cell i's own velocity so sum_k n_k * length_k = 0 for a uniform wind on barycentric dual edges.
                    float speedDiff = 0.5f * ((lowerEast[j] - lowerEast[i]) * nEast + (lowerNorth[j] - lowerNorth[i]) * nNorth);
                    flux += speedDiff * length[k];
                }
                divergence[i] = mesh.AreaM2[i] > 1f ? flux / mesh.AreaM2[i] : 0f;
            }

            // Ocean air rides the upper layer at layer speed: no cold high, no wall, no surface slowing.
            Array.Copy(upperEast, transportEast, n);
            Array.Copy(upperNorth, transportNorth, n);

            // Rising and sinking air come from the layer wind above. The tiles get the slower surface wind.
            for (int i = 0; i < n; i++)
            {
                float factor = Mathf.Lerp(s.SurfaceOcean, s.SurfaceLand, landFrac[i]);
                lowerEast[i] *= factor;
                lowerNorth[i] *= factor;
                upperEast[i] *= factor;
                upperNorth[i] *= factor;
            }
        }

        static void BalanceWind(CoarseMesh mesh, float[] pressure, float[] landFrac, float rOcean, float rLand, float[] eastMs, float[] northMs)
        {
            for (int i = 0; i < mesh.Count; i++)
            {
                mesh.Frame(mesh.Position[i], out Vector3 east, out Vector3 north);
                Gradient(mesh, pressure, i, east, north, out float gx, out float gy);
                float lat = Mathf.Asin(Mathf.Clamp(mesh.Position[i].y, -1f, 1f));
                float f = 2f * Omega * Mathf.Sin(lat);
                float r = Mathf.Lerp(rOcean, rLand, landFrac[i]);
                float denom = r * r + f * f;
                if (denom < 1e-20f)
                    continue;
                // G is the pressure gradient divided by air density. Pressure is in hPa.
                float gEast = gx * 100f / AirDensity;
                float gNorth = gy * 100f / AirDensity;
                eastMs[i] = -(r * gEast + f * gNorth) / denom;
                northMs[i] = -(r * gNorth - f * gEast) / denom;
            }
        }

        /// <summary>
        /// A wall tile is mountainous, impassable, or a ridge. Large hills are not a wall:
        /// Tibet's interior is large hills at about 5 km, and treating that as a wall would
        /// make the plateau a barrier. The sign of a ridge normal is arbitrary.
        /// </summary>
        public bool IsWallTile(int tile)
        {
            if (climate.IsRidgeTile != null && tile < climate.IsRidgeTile.Length && climate.IsRidgeTile[tile])
                return true;
            if (climate.HillinessClassification == null || tile >= climate.HillinessClassification.Length)
                return false;
            Hilliness hill = climate.HillinessClassification[tile];
            return hill == Hilliness.Mountainous || hill == Hilliness.Impassable;
        }

        public bool IsLandTile(int tile)
        {
            if (climate.OceanMask != null && tile < climate.OceanMask.Length && climate.OceanMask[tile])
                return false;
            if (climate.ElevationKm != null && tile < climate.ElevationKm.Length && climate.ElevationKm[tile] <= 0f)
                return false;
            return true;
        }

        void AggregateWalls(CoarseMesh mesh, MeshInputs inputs)
        {
            int n = mesh.Count;
            inputs.LandTiles = new int[n];
            inputs.WallTiles = new int[n];
            inputs.RidgeNormalSum = new Vector3[n];
            inputs.Uphill = new Vector3[n];
            var normalRef = new Vector3[n];
            var xx = new double[n];
            var xy = new double[n];
            var yy = new double[n];
            var hx = new double[n];
            var hy = new double[n];
            for (int t = 0; t < tileCount; t++)
            {
                if (!IsLandTile(t))
                    continue;
                int c = inputs.TileCell[t];
                inputs.LandTiles[c]++;
                if (IsWallTile(t))
                {
                    inputs.WallTiles[c]++;
                    if (climate.IsRidgeTile != null && climate.RidgeNormal != null && climate.IsRidgeTile[t])
                    {
                        Vector3 facing = climate.RidgeNormal[t];
                        if (facing.sqrMagnitude > 1e-8f)
                        {
                            if (normalRef[c] == Vector3.zero)
                                normalRef[c] = facing;
                            else if (Vector3.Dot(facing, normalRef[c]) < 0f)
                                facing = -facing;
                            inputs.RidgeNormalSum[c] += facing;
                        }
                    }
                }
                mesh.Frame(mesh.Position[c], out Vector3 east, out Vector3 north);
                Vector3 step = climate.TileCenters[t] - mesh.Position[c];
                double dx = Vector3.Dot(step, east) * CoarseMesh.EarthRadiusM;
                double dy = Vector3.Dot(step, north) * CoarseMesh.EarthRadiusM;
                double height = climate.ElevationKm != null ? climate.ElevationKm[t] : 0f;
                xx[c] += dx * dx;
                xy[c] += dx * dy;
                yy[c] += dy * dy;
                hx[c] += height * dx;
                hy[c] += height * dy;
            }

            for (int c = 0; c < n; c++)
            {
                if (inputs.LandTiles[c] == 0 && inputs.CellCoverTile != null && inputs.CellCoverTile[c] >= 0)
                {
                    int ct = inputs.CellCoverTile[c];
                    if (IsLandTile(ct))
                    {
                        inputs.LandTiles[c] = 1;
                        if (IsWallTile(ct))
                        {
                            inputs.WallTiles[c] = 1;
                            if (climate.IsRidgeTile != null && climate.RidgeNormal != null && climate.IsRidgeTile[ct])
                                inputs.RidgeNormalSum[c] = climate.RidgeNormal[ct];
                        }
                    }
                }
                double det = xx[c] * yy[c] - xy[c] * xy[c];
                if (System.Math.Abs(det) < 1e-6)
                    continue;
                float gx = (float)((hx[c] * yy[c] - hy[c] * xy[c]) / det);
                float gy = (float)((hy[c] * xx[c] - hx[c] * xy[c]) / det);
                mesh.Frame(mesh.Position[c], out Vector3 east, out Vector3 north);
                Vector3 uphill = east * gx + north * gy;
                if (uphill.sqrMagnitude > 1e-12f)
                    inputs.Uphill[c] = uphill.normalized;
            }
        }

        /// <summary>A wall cell takes its ridge direction when it has one, otherwise the uphill direction.</summary>
        static int ResolveBarriers(MeshInputs inputs, float landShare)
        {
            int blocked = 0;
            for (int c = 0; c < inputs.Barrier.Length; c++)
            {
                int minLand = inputs.TileCount[c] <= 2 ? 1 : 2;
                bool wall = inputs.LandTiles[c] >= minLand && inputs.WallTiles[c] >= inputs.LandTiles[c] * landShare;
                inputs.Barrier[c] = wall;
                inputs.BarrierNormal[c] = Vector3.zero;
                if (!wall)
                    continue;
                blocked++;
                inputs.BarrierNormal[c] = inputs.RidgeNormalSum[c].sqrMagnitude > 1e-8f
                    ? inputs.RidgeNormalSum[c].normalized
                    : inputs.Uphill[c];
            }
            return blocked;
        }

        /// <summary>
        /// On a wall, drop the part of the lower wind that crosses the ridge. On a free cell
        /// beside a wall, drop the part that blows into that wall. The along-wall part stays,
        /// which is what can channel a wind that the pressure field already has.
        /// </summary>
        static void DeflectLower(CoarseMesh mesh, bool[] barrier, Vector3[] barrierNormal, float[] eastMs, float[] northMs)
        {
            for (int i = 0; i < mesh.Count; i++)
            {
                if (!barrier[i] || barrierNormal[i].sqrMagnitude < 1e-8f)
                    continue;
                mesh.Frame(mesh.Position[i], out Vector3 east, out Vector3 north);
                float nEast = Vector3.Dot(barrierNormal[i], east);
                float nNorth = Vector3.Dot(barrierNormal[i], north);
                float mag = Mathf.Sqrt(nEast * nEast + nNorth * nNorth);
                if (mag < 0.05f)
                    continue;
                nEast /= mag;
                nNorth /= mag;
                float cross = eastMs[i] * nEast + northMs[i] * nNorth;
                eastMs[i] -= cross * nEast;
                northMs[i] -= cross * nNorth;
            }

            var keptEast = (float[])eastMs.Clone();
            var keptNorth = (float[])northMs.Clone();
            for (int i = 0; i < mesh.Count; i++)
            {
                if (barrier[i])
                    continue;
                mesh.Frame(mesh.Position[i], out Vector3 east, out Vector3 north);
                float u = eastMs[i];
                float v = northMs[i];
                int[] nbr = mesh.Neighbors[i];
                for (int k = 0; k < nbr.Length; k++)
                {
                    int j = nbr[k];
                    if (!barrier[j])
                        continue;
                    Vector3 step = mesh.Position[j] - mesh.Position[i];
                    float alongEast = Vector3.Dot(step, east);
                    float alongNorth = Vector3.Dot(step, north);
                    float horiz = Mathf.Sqrt(alongEast * alongEast + alongNorth * alongNorth);
                    if (horiz < 1e-8f)
                        continue;
                    float dEast = alongEast / horiz;
                    float dNorth = alongNorth / horiz;
                    float into = u * dEast + v * dNorth;
                    if (into > 0f)
                    {
                        u -= into * dEast;
                        v -= into * dNorth;
                    }
                }
                keptEast[i] = u;
                keptNorth[i] = v;
            }
            Array.Copy(keptEast, eastMs, eastMs.Length);
            Array.Copy(keptNorth, northMs, northMs.Length);
        }

        /// <summary>
        /// Subtropical crests stay on geographic latitude. Only the equatorial trough
        /// follows the thermal equator, and only within 18° of it. Sliding the whole
        /// profile put December's crest near 43°S and turned 40°S easterly.
        /// The ridge and the storm belt are carried by open ocean, so the profile is scaled by this
        /// band's ocean share over the band where it was measured. The seasonal shape is added after
        /// that scaling: winter hemisphere up in the subtropics and down in the subpolar band.
        /// </summary>
        static float SeasonalPressure(float latDeg, float itczShift, float seasonSign, SolveSettings s, float landMask = 1f)
        {
            float pressure = ClimateReferenceData.SeaLevelPressureAnomaly(latDeg) * s.ProfileScale;
            float dist = Mathf.Abs(latDeg - itczShift);
            if (dist < TroughHalfWidthDeg)
            {
                float shifted = ClimateReferenceData.SeaLevelPressureAnomaly(latDeg - itczShift) * s.ProfileScale;
                float weight = 0.5f * (1f + Mathf.Cos(Mathf.PI * dist / TroughHalfWidthDeg)) * landMask;
                pressure = Mathf.Lerp(pressure, shifted, weight);
            }
            if (s.CurveOceanPower > 0f)
            {
                float here = BandShareAt(s.BandOceanShare, latDeg);
                float reference = Mathf.Max(ClimateReferenceData.OceanShareAt(latDeg), 0.05f);
                pressure *= Mathf.Pow(Mathf.Clamp(here / reference, 0f, 1.5f), s.CurveOceanPower);
            }
            if (Mathf.Abs(s.SeasonalScale) > 1e-12f)
            {
                float hemi = latDeg >= 0f ? 1f : -1f;
                pressure -= seasonSign * hemi * s.SeasonalScale * s.TiltFactor * ClimateReferenceData.SeasonalPressureDeltaAt(latDeg) * landMask;
            }
            return pressure;
        }

        /// <summary>Normalized summer-hemisphere subtropical land fraction (10–40°) by 10° longitude sector, confining the seasonal trough shift and bump to land longitudes.</summary>
        static float[] SummerLand(CoarseMesh mesh, float[] landFrac, float seasonSign)
        {
            const int bins = 36;
            var land = new double[bins];
            var total = new double[bins];
            for (int i = 0; i < mesh.Count; i++)
            {
                LatLon(mesh.Position[i], out float lat, out float lon);
                float latS = lat * seasonSign;
                if (latS < 10f || latS >= 40f)
                    continue;
                int bin = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 10f), 0, bins - 1);
                double area = mesh.AreaM2[i];
                land[bin] += landFrac[i] * area;
                total[bin] += area;
            }
            var share = new float[bins];
            for (int b = 0; b < bins; b++)
                share[b] = total[b] > 0.0 ? (float)(land[b] / total[b]) : 0f;
            var smooth = new float[bins];
            for (int b = 0; b < bins; b++)
            {
                float avg = (share[(b + bins - 1) % bins] + share[b] + share[(b + 1) % bins]) / 3f;
                smooth[b] = Mathf.Clamp01(avg / 0.45f);
            }
            var atCell = new float[mesh.Count];
            for (int i = 0; i < mesh.Count; i++)
            {
                LatLon(mesh.Position[i], out _, out float lon);
                int bin = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 10f), 0, bins - 1);
                atCell[i] = smooth[bin];
            }
            return atCell;
        }

        /// <summary>Where a subtropical plateau (24–40°, >1.5 km) sits poleward of a tropical ocean (−5 to 18°), pulls the summer monsoon trough poleward.</summary>
        static float[] PlateauMonsoon(CoarseMesh mesh, float[] landFrac, float[] elevKm, float seasonSign)
        {
            const int bins = 36;
            var platSum = new double[bins];
            var bandArea = new double[bins];
            var oceanSum = new double[bins];
            var eqArea = new double[bins];
            for (int i = 0; i < mesh.Count; i++)
            {
                LatLon(mesh.Position[i], out float lat, out float lon);
                float latS = lat * seasonSign;
                int bin = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 10f), 0, bins - 1);
                double area = mesh.AreaM2[i];
                if (latS >= 24f && latS <= 40f)
                {
                    float pw = Mathf.Clamp01((elevKm[i] - 1.5f) / 1.5f) * landFrac[i];
                    platSum[bin] += pw * area;
                    bandArea[bin] += area;
                }
                if (latS >= -5f && latS <= 18f)
                {
                    oceanSum[bin] += (1f - landFrac[i]) * area;
                    eqArea[bin] += area;
                }
            }
            var idx = new float[bins];
            for (int b = 0; b < bins; b++)
            {
                float pShare = bandArea[b] > 0.0 ? (float)(platSum[b] / bandArea[b]) : 0f;
                float oShare = eqArea[b] > 0.0 ? (float)(oceanSum[b] / eqArea[b]) : 0f;
                idx[b] = Mathf.Clamp01(pShare / 0.25f) * Mathf.Clamp01(oShare / 0.50f);
            }
            var smooth = new float[bins];
            for (int b = 0; b < bins; b++)
                smooth[b] = (idx[(b + bins - 1) % bins] + idx[b] + idx[(b + 1) % bins]) / 3f;
            var atCell = new float[mesh.Count];
            for (int i = 0; i < mesh.Count; i++)
            {
                LatLon(mesh.Position[i], out _, out float lon);
                int bin = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 10f), 0, bins - 1);
                atCell[i] = smooth[bin];
            }
            return atCell;
        }

        /// <summary>Ocean share of each 5° band from the south, weighted by cell area. Hidden cells count as ocean.</summary>
        static float[] BandOceanShare(CoarseMesh mesh, float[] landFrac)
        {
            const int bins = 36;
            var sea = new double[bins];
            var area = new double[bins];
            for (int i = 0; i < mesh.Count; i++)
            {
                float lat = Mathf.Asin(Mathf.Clamp(mesh.Position[i].y, -1f, 1f)) * Mathf.Rad2Deg;
                int bin = Mathf.Clamp(Mathf.FloorToInt((lat + 90f) / 5f), 0, bins - 1);
                sea[bin] += (1f - landFrac[i]) * mesh.AreaM2[i];
                area[bin] += mesh.AreaM2[i];
            }
            var share = new float[bins];
            for (int b = 0; b < bins; b++)
                share[b] = (float)(sea[b] / Math.Max(area[b], 1.0));
            return share;
        }

        /// <summary>Linear between band centres (87.5°S to 87.5°N), held flat past either end.</summary>
        static float BandShareAt(float[] share, float latDeg)
        {
            float x = (latDeg + 87.5f) / 5f;
            if (x <= 0f)
                return share[0];
            if (x >= share.Length - 1)
                return share[share.Length - 1];
            int i = (int)x;
            float t = x - i;
            return share[i] + t * (share[i + 1] - share[i]);
        }

        /// <summary>
        /// Mean of each 5° band, moved toward the ocean-only mean of that band by oceanReference.
        /// At 1 a cell's departure is measured from the ocean at its latitude, so land carries its
        /// own net high or low. A band with almost no ocean keeps the plain mean.
        /// </summary>
        static float[] ZonalMean(CoarseMesh mesh, float[] values, float[] landFrac, float oceanReference)
        {
            const int bins = 36;
            var sum = new double[bins];
            var num = new int[bins];
            var seaSum = new double[bins];
            var seaWeight = new double[bins];
            for (int i = 0; i < mesh.Count; i++)
            {
                float lat = Mathf.Asin(Mathf.Clamp(mesh.Position[i].y, -1f, 1f)) * Mathf.Rad2Deg;
                int bin = Mathf.Clamp(Mathf.FloorToInt((lat + 90f) / 5f), 0, bins - 1);
                sum[bin] += values[i];
                num[bin]++;
                float sea = 1f - landFrac[i];
                seaSum[bin] += values[i] * sea;
                seaWeight[bin] += sea;
            }
            var mean = new float[bins];
            for (int b = 0; b < bins; b++)
            {
                mean[b] = num[b] > 0 ? (float)(sum[b] / num[b]) : 0f;
                if (oceanReference > 0f && seaWeight[b] > 5.0)
                    mean[b] += oceanReference * ((float)(seaSum[b] / seaWeight[b]) - mean[b]);
            }
            var atCell = new float[mesh.Count];
            for (int i = 0; i < mesh.Count; i++)
            {
                float lat = Mathf.Asin(Mathf.Clamp(mesh.Position[i].y, -1f, 1f)) * Mathf.Rad2Deg;
                atCell[i] = BandShareAt(mean, lat);
            }
            return atCell;
        }

        float[] OceanDistanceKm(CoarseMesh mesh, int[] tileCell, int[] cellCoverTile)
        {
            var sum = new float[mesh.Count];
            var count = new int[mesh.Count];
            bool haveDistance = climate.OceanDistKm != null && climate.OceanDistKm.Length >= tileCount;
            if (haveDistance)
            {
                for (int t = 0; t < tileCount; t++)
                {
                    int c = tileCell[t];
                    sum[c] += climate.OceanDistKm[t];
                    count[c]++;
                }
            }
            for (int c = 0; c < mesh.Count; c++)
            {
                if (count[c] > 0)
                    sum[c] /= count[c];
                else if (haveDistance && cellCoverTile != null && cellCoverTile[c] >= 0)
                    sum[c] = climate.OceanDistKm[cellCoverTile[c]];
                else if (!haveDistance)
                    sum[c] = 100000f;
            }
            return sum;
        }

        /// <summary>
        /// On smaller planet scales (subdivisions 5–8), a single tile is wider than the 110 km coarse mesh,
        /// so some coarse cells inside the visible map do not contain a tile center. Match those cells to
        /// their covering tile so they are not misclassified as hidden ocean.
        /// </summary>
        int[] FillUncoveredVisibleCells(CoarseMesh mesh, MeshInputs inputs)
        {
            int cells = mesh.Count;
            var cover = new int[cells];
            for (int c = 0; c < cells; c++)
                cover[c] = -1;

            bool needLookup = false;
            for (int c = 0; c < cells; c++)
            {
                if (inputs.TileCount[c] == 0)
                {
                    needLookup = true;
                    break;
                }
            }
            if (!needLookup)
                return cover;

            const int latBins = 90;
            const int lonBins = 180;
            var bins = new System.Collections.Generic.List<int>[latBins * lonBins];
            for (int t = 0; t < tileCount; t++)
            {
                LatLon(climate.TileCenters[t], out float lat, out float lon);
                int key = BinKey(lat, lon, latBins, lonBins);
                if (bins[key] == null)
                    bins[key] = new System.Collections.Generic.List<int>(4);
                bins[key].Add(t);
            }

            float coverage = Verse.Find.World?.info?.planetCoverage ?? 1f;
            float maxDistKm = Mathf.Max(EarthScaleConstants.KM_PER_TILE * 1.25f, CellSpacingKm * 1.15f);
            float minDot = coverage >= 0.989f
                ? -2f
                : Mathf.Cos(maxDistKm / (CoarseMesh.EarthRadiusM * 0.001f));
            int searchRadius = Mathf.Clamp(Mathf.CeilToInt(maxDistKm / 220f), 1, 6);

            for (int c = 0; c < cells; c++)
            {
                if (inputs.TileCount[c] > 0)
                    continue;
                Vector3 pos = mesh.Position[c];
                LatLon(pos, out float lat, out float lon);
                int latBin = Mathf.Clamp(Mathf.FloorToInt((lat + 90f) / 2f), 0, latBins - 1);
                int lonBin = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 2f), 0, lonBins - 1);
                int bestTile = -1;
                float bestDot = -2f;
                for (int dLat = -searchRadius; dLat <= searchRadius; dLat++)
                {
                    int lb = latBin + dLat;
                    if (lb < 0 || lb >= latBins)
                        continue;
                    for (int dLon = -searchRadius; dLon <= searchRadius; dLon++)
                    {
                        int ob = (lonBin + dLon + lonBins) % lonBins;
                        var list = bins[lb * lonBins + ob];
                        if (list == null)
                            continue;
                        for (int n = 0; n < list.Count; n++)
                        {
                            int t = list[n];
                            float dot = Vector3.Dot(pos, climate.TileCenters[t].normalized);
                            if (dot > bestDot)
                            {
                                bestDot = dot;
                                bestTile = t;
                            }
                        }
                    }
                }

                // Fallback full scan on tiny worlds (subdivision 5–6) if bin window missed near poles
                if (bestTile < 0 && coverage >= 0.989f)
                {
                    for (int t = 0; t < tileCount; t++)
                    {
                        float dot = Vector3.Dot(pos, climate.TileCenters[t].normalized);
                        if (dot > bestDot)
                        {
                            bestDot = dot;
                            bestTile = t;
                        }
                    }
                }

                if (bestTile >= 0 && bestDot >= minDot)
                {
                    cover[c] = bestTile;
                    inputs.TileCount[c] = 1;
                    bool land = climate.OceanMask == null || !climate.OceanMask[bestTile];
                    inputs.LandFrac[c] = land ? 1f : 0f;
                    inputs.ElevKm[c] = climate.ElevationKm != null ? climate.ElevationKm[bestTile] : 0f;
                    float refOcean = climate.LatitudinalSeaLevelTemp != null && bestTile < climate.LatitudinalSeaLevelTemp.Length
                        ? climate.LatitudinalSeaLevelTemp[bestTile]
                        : climate.AnnualMeanTemp[bestTile];
                    inputs.Annual[c] = land ? climate.AnnualMeanTemp[bestTile] : refOcean;
                    inputs.Amp[c] = climate.SeasonalAmp[bestTile];
                    inputs.Cont[c] = land && climate.Continentality != null && bestTile < climate.Continentality.Length
                        ? climate.Continentality[bestTile]
                        : 0f;
                }
            }
            return cover;
        }

        static int StepsForKm(float km)
        {
            return Mathf.Max(1, Mathf.RoundToInt(Mathf.Pow(km / CellSpacingKm, 2f)));
        }

        static void Smooth(CoarseMesh mesh, float[] values, int steps)
        {
            var next = new float[values.Length];
            for (int step = 0; step < steps; step++)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    int[] nbr = mesh.Neighbors[i];
                    float sum = 0f;
                    for (int k = 0; k < nbr.Length; k++)
                        sum += values[nbr[k]];
                    next[i] = nbr.Length > 0 ? sum / nbr.Length : values[i];
                }
                Array.Copy(next, values, values.Length);
            }
        }

        static void Gradient(CoarseMesh mesh, float[] pressure, int i, Vector3 east, Vector3 north, out float gx, out float gy)
        {
            double xx = 0, xy = 0, yy = 0, px = 0, py = 0;
            int[] nbr = mesh.Neighbors[i];
            for (int k = 0; k < nbr.Length; k++)
            {
                int j = nbr[k];
                Vector3 step = mesh.Position[j] - mesh.Position[i];
                double dx = Vector3.Dot(step, east) * CoarseMesh.EarthRadiusM;
                double dy = Vector3.Dot(step, north) * CoarseMesh.EarthRadiusM;
                double dp = pressure[j] - pressure[i];
                xx += dx * dx;
                xy += dx * dy;
                yy += dy * dy;
                px += dp * dx;
                py += dp * dy;
            }
            double det = xx * yy - xy * xy;
            if (Math.Abs(det) < 1e-6)
            {
                gx = 0f;
                gy = 0f;
                return;
            }
            gx = (float)((px * yy - py * xy) / det);
            gy = (float)((py * xx - px * xy) / det);
        }

        int[] MapTiles(CoarseMesh mesh)
        {
            const int latBins = 90;
            const int lonBins = 180;
            var bins = new System.Collections.Generic.List<int>[latBins * lonBins];
            for (int c = 0; c < mesh.Count; c++)
            {
                LatLon(mesh.Position[c], out float lat, out float lon);
                int key = BinKey(lat, lon, latBins, lonBins);
                if (bins[key] == null)
                    bins[key] = new System.Collections.Generic.List<int>(4);
                bins[key].Add(c);
            }

            var tileCell = new int[tileCount];
            for (int t = 0; t < tileCount; t++)
            {
                Vector3 center = climate.TileCenters[t];
                LatLon(center, out float lat, out float lon);
                int latBin = Mathf.Clamp(Mathf.FloorToInt((lat + 90f) / 2f), 0, latBins - 1);
                int lonBin = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 2f), 0, lonBins - 1);
                int best = 0;
                float bestDot = -2f;
                for (int dLat = -1; dLat <= 1; dLat++)
                {
                    int lb = latBin + dLat;
                    if (lb < 0 || lb >= latBins)
                        continue;
                    for (int dLon = -1; dLon <= 1; dLon++)
                    {
                        int ob = (lonBin + dLon + lonBins) % lonBins;
                        var list = bins[lb * lonBins + ob];
                        if (list == null)
                            continue;
                        for (int n = 0; n < list.Count; n++)
                        {
                            int c = list[n];
                            float dot = Vector3.Dot(center.normalized, mesh.Position[c]);
                            if (dot > bestDot)
                            {
                                bestDot = dot;
                                best = c;
                            }
                        }
                    }
                }
                tileCell[t] = best;
            }
            return tileCell;
        }

        void AggregateTiles(CoarseMesh mesh, int[] tileCell, float[] landFrac, float[] elevKm, float[] annual, float[] amp, float[] cont, int[] count)
        {
            var landCount = new int[mesh.Count];
            var landTemp = new double[mesh.Count];
            var oceanTemp = new double[mesh.Count];
            var landAmp = new double[mesh.Count];
            var oceanAmp = new double[mesh.Count];
            var landCont = new double[mesh.Count];
            var elev = new double[mesh.Count];
            for (int t = 0; t < tileCount; t++)
            {
                int c = tileCell[t];
                count[c]++;
                float height = climate.ElevationKm != null ? climate.ElevationKm[t] : 0f;
                elev[c] += height;
                bool land = climate.OceanMask == null || !climate.OceanMask[t];
                if (land)
                {
                    landCount[c]++;
                    landTemp[c] += climate.AnnualMeanTemp[t];
                    landAmp[c] += climate.SeasonalAmp[t];
                    if (climate.Continentality != null && t < climate.Continentality.Length)
                        landCont[c] += climate.Continentality[t];
                }
                else
                {
                    float refOcean = climate.LatitudinalSeaLevelTemp != null && t < climate.LatitudinalSeaLevelTemp.Length
                        ? climate.LatitudinalSeaLevelTemp[t]
                        : climate.AnnualMeanTemp[t];
                    oceanTemp[c] += refOcean;
                    oceanAmp[c] += climate.SeasonalAmp[t];
                }
            }
            for (int c = 0; c < mesh.Count; c++)
            {
                if (count[c] == 0)
                    continue;
                landFrac[c] = landCount[c] / (float)count[c];
                elevKm[c] = (float)(elev[c] / count[c]);
                int oceanCount = count[c] - landCount[c];
                float tLand = landCount[c] > 0 ? (float)(landTemp[c] / landCount[c]) : 0f;
                float tOcean = oceanCount > 0 ? (float)(oceanTemp[c] / oceanCount) : tLand;
                float aLand = landCount[c] > 0 ? (float)(landAmp[c] / landCount[c]) : 0f;
                float aOcean = oceanCount > 0 ? (float)(oceanAmp[c] / oceanCount) : aLand;
                annual[c] = landFrac[c] * tLand + (1f - landFrac[c]) * tOcean;
                amp[c] = landFrac[c] * aLand + (1f - landFrac[c]) * aOcean;
                cont[c] = landCount[c] > 0 ? (float)(landCont[c] / landCount[c]) : 0f;
            }
        }

        /// <summary>
        /// Tiles at or above <see cref="PlateauKm"/> that are not a wall take the upper wind.
        /// Everywhere else takes the lower wind, including the wall itself. Rising and sinking
        /// air stays the lower layer, so a plateau's horizontal wind and its convergence can differ.
        /// </summary>
        void WriteSurface(CoarseMesh mesh, int[] tileCell, float[] lowerEast, float[] lowerNorth, float[] upperEast, float[] upperNorth,
            Vector3[] wind, float[] convergence, float[] cellDivergence)
        {
            var sampled = new float[tileCount];
            for (int t = 0; t < tileCount; t++)
            {
                int a = baryA[t], b = baryB[t], c = baryC[t];
                float wa = baryWA[t], wb = baryWB[t], wc = baryWC[t];
                float div = wa * cellDivergence[a] + wb * cellDivergence[b] + wc * cellDivergence[c];
                float u, v;
                if (isPlateau[t])
                {
                    u = wa * upperEast[a] + wb * upperEast[b] + wc * upperEast[c];
                    v = wa * upperNorth[a] + wb * upperNorth[b] + wc * upperNorth[c];
                }
                else
                {
                    u = wa * lowerEast[a] + wb * lowerEast[b] + wc * lowerEast[c];
                    v = wa * lowerNorth[a] + wb * lowerNorth[b] + wc * lowerNorth[c];
                }
                wind[t] = tileEast[t] * u + tileNorth[t] * v;
                sampled[t] = div;
            }
            float scale = PercentileAbs(sampled, 0.95f);
            if (scale < 1e-12f)
                scale = 1f;
            for (int t = 0; t < tileCount; t++)
                convergence[t] = Mathf.Clamp(-sampled[t] / scale, -1f, 1f);
        }

        void WriteTransport(CoarseMesh mesh, int[] tileCell, float[] eastMs, float[] northMs, Vector3[] wind)
        {
            for (int t = 0; t < tileCount; t++)
            {
                int a = baryA[t], b = baryB[t], c = baryC[t];
                float wa = baryWA[t], wb = baryWB[t], wc = baryWC[t];
                float u = wa * eastMs[a] + wb * eastMs[b] + wc * eastMs[c];
                float v = wa * northMs[a] + wb * northMs[b] + wc * northMs[c];
                wind[t] = tileEast[t] * u + tileNorth[t] * v;
            }
        }

        public static void FindBarycentric(CoarseMesh mesh, Vector3 center, int nearest,
            out int a, out int b, out int c, out float wa, out float wb, out float wc)
        {
            int[] tris = mesh.VertexTriangles[nearest];
            for (int n = 0; n < tris.Length; n++)
            {
                int t = tris[n];
                a = mesh.TriA[t];
                b = mesh.TriB[t];
                c = mesh.TriC[t];
                if (!Barycentric(mesh.Position[a], mesh.Position[b], mesh.Position[c], center, out wa, out wb, out wc))
                    continue;
                float sum = wa + wb + wc;
                if (sum < 1e-6f)
                    continue;
                wa /= sum;
                wb /= sum;
                wc /= sum;
                return;
            }
            a = nearest;
            b = nearest;
            c = nearest;
            wa = 1f;
            wb = 0f;
            wc = 0f;
        }

        static void Sample(CoarseMesh mesh, Vector3 center, int nearest, float[] eastMs, float[] northMs, float[] divergence,
            out float u, out float v, out float div)
        {
            FindBarycentric(mesh, center, nearest, out int a, out int b, out int c, out float wa, out float wb, out float wc);
            u = wa * eastMs[a] + wb * eastMs[b] + wc * eastMs[c];
            v = wa * northMs[a] + wb * northMs[b] + wc * northMs[c];
            div = wa * divergence[a] + wb * divergence[b] + wc * divergence[c];
        }

        void WriteAnnual()
        {
            for (int t = 0; t < tileCount; t++)
            {
                Vector3 sumW = climate.SummerWind[t];
                Vector3 winW = climate.WinterWind[t];
                Vector3 eqW = climate.EquinoxWind[t];
                Vector3 wind = 0.25f * sumW + 0.25f * winW + 0.5f * eqW;
                float meanSpd = 0.25f * sumW.magnitude + 0.25f * winW.magnitude + 0.5f * eqW.magnitude;
                climate.WindSpeed[t] = meanSpd;
                if (wind.sqrMagnitude > 0.25f)
                {
                    climate.WindDirection[t] = wind.normalized;
                }
                else if (meanSpd > 0.5f)
                {
                    // Where opposite seasonal monsoons cancel in the vector mean, show the stronger solstice direction.
                    Vector3 dom = sumW.sqrMagnitude >= winW.sqrMagnitude ? sumW : winW;
                    climate.WindDirection[t] = dom.sqrMagnitude > 0.04f ? dom.normalized : Vector3.zero;
                }
                else
                {
                    climate.WindDirection[t] = Vector3.zero;
                }
                climate.ConvergenceIndex[t] = 0.25f * climate.JuneConvergence[t] + 0.25f * climate.DecemberConvergence[t] + 0.5f * climate.EquinoxConvergence[t];
            }
        }

        void LogBands(CoarseMesh mesh, int[] tileCell, float[] eastMs, float[] northMs)
        {
            var sumU = new double[18];
            var sumS = new double[18];
            var num = new int[18];
            for (int t = 0; t < tileCount; t++)
            {
                float lat = climate.LatitudeSigned[t] * Mathf.Rad2Deg;
                int band = Mathf.Clamp(Mathf.FloorToInt((lat + 90f) / 10f), 0, 17);
                Vector3 wind = climate.SummerWind[t];
                mesh.Frame(climate.TileCenters[t], out Vector3 east, out _);
                float u = Vector3.Dot(wind, east);
                sumU[band] += u;
                sumS[band] += wind.magnitude;
                num[band]++;
            }
            RPLog.D("[Circulation] June mean eastward wind and speed (m/s) by 10° band, from the south:");
            for (int b = 0; b < 18; b++)
            {
                if (num[b] == 0)
                    continue;
                int lat0 = b * 10 - 90;
                RPLog.D($"[Circulation] {lat0,4} to {lat0 + 10,3}: eastward {sumU[b] / num[b],6:F2}, speed {sumS[b] / num[b],5:F2}");
            }
        }

        float HiddenOceanTemperature(float latRad)
        {
            float equator = climate.EquatorMean + ClimateReferenceData.OceanEquatorOffset;
            float pole = climate.PoleMean + ClimateReferenceData.OceanPoleOffset;
            return pole + (equator - pole) * Mathf.Cos(latRad);
        }

        float ItczShiftDegrees()
        {
            float tilt = AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt);
            return tilt * (8f / 22.5f);
        }

        void Ensure(ref Vector3[] values)
        {
            if (values == null || values.Length != tileCount)
                values = new Vector3[tileCount];
        }

        void Ensure(ref float[] values)
        {
            if (values == null || values.Length != tileCount)
                values = new float[tileCount];
        }

        static void LatLon(Vector3 position, out float latDeg, out float lonDeg)
        {
            Vector3 p = position.normalized;
            latDeg = Mathf.Asin(Mathf.Clamp(p.y, -1f, 1f)) * Mathf.Rad2Deg;
            lonDeg = Mathf.Atan2(p.x, -p.z) * Mathf.Rad2Deg;
        }

        static int BinKey(float lat, float lon, int latBins, int lonBins)
        {
            int lb = Mathf.Clamp(Mathf.FloorToInt((lat + 90f) / 2f), 0, latBins - 1);
            int ob = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 2f), 0, lonBins - 1);
            return lb * lonBins + ob;
        }

        static bool Barycentric(Vector3 a, Vector3 b, Vector3 c, Vector3 p, out float wa, out float wb, out float wc)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float area = n.magnitude;
            if (area < 1e-10f)
            {
                wa = wb = wc = 0f;
                return false;
            }
            n /= area;
            wa = Vector3.Dot(Vector3.Cross(b - p, c - p), n) / area;
            wb = Vector3.Dot(Vector3.Cross(c - p, a - p), n) / area;
            wc = Vector3.Dot(Vector3.Cross(a - p, b - p), n) / area;
            return wa >= -0.02f && wb >= -0.02f && wc >= -0.02f;
        }

        static float PercentileAbs(float[] values, float p)
        {
            var copy = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
                copy[i] = Mathf.Abs(values[i]);
            Array.Sort(copy);
            int index = Mathf.Clamp(Mathf.RoundToInt((copy.Length - 1) * p), 0, copy.Length - 1);
            return copy[index];
        }
    }
}
