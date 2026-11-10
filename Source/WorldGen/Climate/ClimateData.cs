using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Utilities.Serialization;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Four-type drainage classification for hydrology.
    /// Distinguishes between ocean-draining basins, inland-water basins, spilling depressions, and terminal closed sinks.
    /// </summary>
    public enum DrainageType : byte
    {
        ExorheicOcean = 0,
        EndorheicLake = 1,
        FilledSpilling = 2,
        FilledTerminal = 3
    }

    /// <summary>
    /// Climate data storage and persistence as a <see cref="WorldComponent"/>.
    /// Saves only the compact quantized gameplay pack (<see cref="ClimatePack"/>), exact pre-rounded elevations,
    /// and a <see cref="ClimateBakeRecord"/>. Diagnostic and map-mode overlay arrays are rebuilt on demand.
    /// </summary>
    public class ClimateData : WorldComponent
    {
        // ========== BASIC GEOGRAPHY ==========
        public float[] Latitude;
        public float[] Longitude;
        public float[] LatitudeSigned;
        public float[] ElevationKm;
        /// <summary>Atmospheric column depth fraction p_s / p_0 = exp(-max(ElevationKm, 0) / 8 km).</summary>
        public float[] ColumnDepth;

        /// <summary>
        /// Exact meters after lake conversion and bathymetry normalization, before whole-meter tile rounding.
        /// Used by fill-and-spill and load-time diagnostic rebuilds so drainage trees are identical after reload.
        /// </summary>
        public float[] ExactElevationMeters;
        public Vector3[] TileCenters;

        // ========== OCEAN & WATER BODY DATA ==========
        public bool[] OceanMask;
        public bool[] TrueOceanMask;
        public int[] WaterBodyID;
        public int[] NearestOceanTile;
        public float[] OceanDistKm;
        public float[] InlandBodyStrength;
        public float[] InlandWaterDistKm;
        public float[] InlandWaterInfluence;

        // ========== SCALAR BASELINES ==========
        public float PoleMean;
        public float EquatorMean;
        public float BaseRain;
        public bool EarthGeography;

        public float ClimateLatitudeDeg(float latDegSigned, float lonRad, float latRadSigned)
        {
            if (EarthGeography)
                return latDegSigned;
            return Utilities.WorldGen.ClimateGeometryHelpers.CalculatePerturbedLatitude(latDegSigned, lonRad, latRadSigned, Find.World.info.Seed);
        }

        // ========== TEMPERATURE FIELDS ==========
        public float[] LatitudinalSeaLevelTemp;
        public float[] SST;
        public float[] CurrentsAnomalySST;
        public float[] CoastalModerationStrength;
        public float[] AnnualMeanTemp;

        // ========== CONTINENTALITY & SEASONALITY ==========
        public float[] Continentality;
        public float[] SeasonalAmp;
        public float[] MaxSummerTemp;
        public float[] MinWinterTemp;

        // ========== WIND, CIRCULATION & MARITIME ==========
        public Vector3[] WindDirection;
        public Vector3[] SummerWind;
        public Vector3[] WinterWind;
        public Vector3[] EquinoxWind;

        public float[] JuneConvergence;
        public float[] DecemberConvergence;
        public float[] EquinoxConvergence;

        public Vector3[] TransportJune;
        public Vector3[] TransportDecember;
        public Vector3[] TransportEquinox;

        // Coarse-mesh runtime fields populated by CirculationLayer for UpperMoistureLayer and MoistureBudgetLayer.
        public int[] TileCell;
        public int[] CellCoverTile;
        public float[] CellAnnual;
        public float[] CellAmp;
        public float[] CellTransportJuneEast;
        public float[] CellTransportJuneNorth;
        public float[] CellTransportDecEast;
        public float[] CellTransportDecNorth;
        public float[] CellTransportEqEast;
        public float[] CellTransportEqNorth;

        public float[] MaritimeJune;
        public float[] MaritimeDecember;
        public float[] MaritimeEquinox;
        public float[] MaritimeAnnual;
        public float[] UpwindSST;
        public float[] ContinentalAir;

        public float[] WindSpeed;
        public float[] ConvergenceIndex;
        public int[] UpwindNeighbor;
        public float[] UpwindReliefIndex;
        public float[] LeewardShadowIndex;

        // ========== MOISTURE & RAINFALL ==========
        public float[] MonsoonBoost;
        public float[] MonsoonMoistureSource;
        public float[] MonsoonLiftIndex;
        public float[] TemperatureCapacity;

        public float[] AdvectedIncomingMoistureMm;
        public float[] AdvectedRemainingMoistureMm;
        public float[] AdvectedCondensationMm;
        public float[] AdvectedDryingRatio;

        public float[] MoistureDeletedMm;
        public float[] SummerCondensationMm;
        public float[] WinterCondensationMm;
        public float[] EquinoxCondensationMm;
        public float[] DriestSeasonRainMm;
        public float[] WettestSeasonRainMm;
        public float[] SummerIncomingMm;
        public float[] WinterIncomingMm;
        public float[] SummerDeletedMm;
        public float[] WinterDeletedMm;

        // ========== WS30 / WS31 MOISTURE BUDGET DIAGNOSTICS ==========
        public float[] PrecipitableWaterMm;
        public float[] UpperColumnWaterMm;
        public float[] OceanEvaporationMm;
        public float[] LandEvaporationMm;
        public float[] RainHumidMm;
        public float[] RainRisingMm;
        public float[] RainUpperMm;
        public float[] DistanceAlongWindKm;
        public float[] OceanWaterShare;

        public float[] AnnualRainfall;
        public float[] PotentialEvapotranspiration;
        public float[] DailyRangeC;
        public float[] AridityIndex;

        // ========== TERRAIN METRICS & RIDGES ==========
        public float[] Prominence;
        public float[] Relief;
        public float[] Ruggedness;
        public float[] Steepness;
        public Hilliness[] HillinessClassification;

        public int[] ElevationBand;
        public float[] BandThresholds;
        public float[] SmoothedElevation;
        public bool[] IsMajorPeak;
        public byte[] RidgeTier;
        public bool[] IsRidgeTile;
        public Vector3[] RidgeNormal;
        public int[] RidgeNeighborSystemID;

        // ========== HYDROLOGY – TIER 1 TOPOLOGY ==========
        public int[] FlowDirectionTopology;
        public float[] FlowAccumulationTopology;
        public float[] FlowRain;
        public bool[] EndorheicMask;
        public int[] InitialBasinID;
        public int[] MergedBasinIDTopology;
        public int[] RiverSystemIDTopology;
        public int[] SpillDirectionTopology;
        public int[] MergedLakeIDTopology;
        public DrainageType[] DrainageTypeClassification;

        // ========== HYDROLOGY – TIER 2 CLIMATE RUN ==========
        public int[] FlowDirectionHydrology;
        public float[] FlowAccumulationHydrology;
        public int[] MergedBasinIDHydrology;
        public int[] SpillDirectionHydrology;
        public int[] MergedLakeIDHydrology;
        public int[] RiverSystemIDHydrology;
        public bool[] EndorheicMaskHydrology;
        public float[] RiverPresence;
        public byte[] RiverMagnitude;
        public byte[] WaterBodyType;

        public float[] Swampiness;

        // ========== SAVE STATE ==========
        public int formatVersion = 2;
        public bool ArraysReady;
        public ClimateBakeRecord bakeRecord = new ClimateBakeRecord();

        public ClimateData(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            Stopwatch expose = Stopwatch.StartNew();
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
                formatVersion = 2;
            Scribe_Values.Look(ref formatVersion, "climateFormat", 1);

            Scribe_Values.Look(ref PoleMean, "poleMean", 0f);
            Scribe_Values.Look(ref EquatorMean, "equatorMean", 0f);
            Scribe_Values.Look(ref BaseRain, "baseRain", 0f);

            if (Scribe.mode == LoadSaveMode.Saving || formatVersion >= 2)
            {
                ExposeGameplayPack();
                Scribe_Deep.Look(ref bakeRecord, "bakeRecord");
                if (Scribe.mode == LoadSaveMode.PostLoadInit && bakeRecord == null)
                    bakeRecord = new ClimateBakeRecord();
                LogExpose(expose);
                return;
            }

            ExposeLegacyV1();
            LogExpose(expose);
        }

        private static void LogExpose(Stopwatch expose)
        {
            expose.Stop();
            if (Scribe.mode == LoadSaveMode.Saving && Core.Controller.DebugEnabled)
                Utilities.Logging.RPLog.D($"[Climate] Save expose took {expose.ElapsedMilliseconds} ms.");
        }

        public override void FinalizeInit(bool fromLoad)
        {
            if (!fromLoad)
            {
                ArraysReady = Latitude != null;
                return;
            }

            if (formatVersion < 2)
            {
                ArraysReady = true;
                formatVersion = 2;
                ClimateDiagnostics.NoteLegacySave();
                return;
            }

            if (bakeRecord != null)
            {
                Core.Planets_GameComponent.axialTilt = bakeRecord.axialTilt;
                Core.Planets_GameComponent.seaLevel = bakeRecord.seaLevel;
                Astronomy.AxialTiltCurves.SetSeasonalCurve();
            }
            ApplyExactElevations(Find.WorldGrid?.Surface);
            DeriveGameplayFromTiles();
            ArraysReady = true;
            ValidateArrayLengths();
            ClimateDiagnostics.NoteLoadedBake(this);
            if (HasSavedWind() && ClimateDiagnostics.StalenessBanner == null && bakeRecord != null && bakeRecord.modelVersion == ClimateModelVersion.Current)
            {
                DeriveAnnualWind();
                if (Core.Controller.DebugEnabled)
                    Utilities.Logging.RPLog.D("[Climate] Saved wind restored. Climate rebuild skipped.");
                return;
            }
            ClimateDiagnostics.QueueRebuild(this);
        }

        private bool HasSavedWind()
        {
            int count = Find.WorldGrid?.TilesCount ?? 0;
            return count > 0
                && LengthIs(SummerWind, count)
                && LengthIs(WinterWind, count)
                && LengthIs(EquinoxWind, count)
                && LengthIs(TransportJune, count)
                && LengthIs(TransportDecember, count)
                && LengthIs(TransportEquinox, count)
                && LengthIs(JuneConvergence, count)
                && LengthIs(DecemberConvergence, count)
                && LengthIs(EquinoxConvergence, count);
        }

        private static bool LengthIs(Vector3[] values, int count) => values != null && values.Length == count;

        private static bool LengthIs(float[] values, int count) => values != null && values.Length == count;

        /// <summary>
        /// Annual wind direction, mean speed, and convergence from the three saved seasonal fields.
        /// Same weights as <see cref="Circulation.CirculationLayer"/> after the rain-heated wind pass.
        /// </summary>
        public void DeriveAnnualWind()
        {
            if (SummerWind == null || WinterWind == null || EquinoxWind == null)
                return;
            int n = SummerWind.Length;
            if (WinterWind.Length < n || EquinoxWind.Length < n)
                return;
            if (WindSpeed == null || WindSpeed.Length != n)
                WindSpeed = new float[n];
            if (WindDirection == null || WindDirection.Length != n)
                WindDirection = new Vector3[n];
            if (ConvergenceIndex == null || ConvergenceIndex.Length != n)
                ConvergenceIndex = new float[n];
            bool haveConv = JuneConvergence != null && DecemberConvergence != null && EquinoxConvergence != null
                && JuneConvergence.Length >= n && DecemberConvergence.Length >= n && EquinoxConvergence.Length >= n;
            for (int t = 0; t < n; t++)
            {
                Vector3 sumW = SummerWind[t];
                Vector3 winW = WinterWind[t];
                Vector3 eqW = EquinoxWind[t];
                Vector3 wind = 0.25f * sumW + 0.25f * winW + 0.5f * eqW;
                float meanSpd = 0.25f * sumW.magnitude + 0.25f * winW.magnitude + 0.5f * eqW.magnitude;
                WindSpeed[t] = meanSpd;
                if (wind.sqrMagnitude > 0.25f)
                    WindDirection[t] = wind.normalized;
                else if (meanSpd > 0.5f)
                {
                    Vector3 dom = sumW.sqrMagnitude >= winW.sqrMagnitude ? sumW : winW;
                    WindDirection[t] = dom.sqrMagnitude > 0.04f ? dom.normalized : Vector3.zero;
                }
                else
                    WindDirection[t] = Vector3.zero;
                ConvergenceIndex[t] = haveConv
                    ? 0.25f * JuneConvergence[t] + 0.25f * DecemberConvergence[t] + 0.5f * EquinoxConvergence[t]
                    : 0f;
            }
        }

        private void ExposeGameplayPack()
        {
            ClimatePack.LookHundredths(ref SeasonalAmp, "seasonalAmp_q");
            ClimatePack.LookThousandths(ref AridityIndex, "aridity_q");
            ClimatePack.LookUnitByte(ref Continentality, "continentality_q");
            ClimatePack.LookThousandths(ref UpwindReliefIndex, "upwindRelief_q");
            ClimatePack.LookThousandths(ref LeewardShadowIndex, "leeward_q");
            ClimatePack.LookMillimeters(ref MonsoonBoost, "monsoonBoost_q");
            ClimatePack.LookFlow(ref FlowAccumulationTopology, "flowAccum_q");
            ClimatePack.LookMillimeters(ref DriestSeasonRainMm, "driestRain_q");
            ClimatePack.LookMillimeters(ref WettestSeasonRainMm, "wettestRain_q");
            BinaryDataExposeUtility.Look(ref ExactElevationMeters, "exactElevationM");
            ClimatePack.LookVector3Hundredths(ref SummerWind, "summerWind_q");
            ClimatePack.LookVector3Hundredths(ref WinterWind, "winterWind_q");
            ClimatePack.LookVector3Hundredths(ref EquinoxWind, "equinoxWind_q");
            ClimatePack.LookVector3Hundredths(ref TransportJune, "transportJune_q");
            ClimatePack.LookVector3Hundredths(ref TransportDecember, "transportDecember_q");
            ClimatePack.LookVector3Hundredths(ref TransportEquinox, "transportEquinox_q");
            ClimatePack.LookHundredths(ref JuneConvergence, "juneConv_q");
            ClimatePack.LookHundredths(ref DecemberConvergence, "decemberConv_q");
            ClimatePack.LookHundredths(ref EquinoxConvergence, "equinoxConv_q");
        }

        private void ExposeLegacyV1()
        {
            BinaryDataExposeUtility.Look(ref Latitude, "latitude");
            BinaryDataExposeUtility.Look(ref Longitude, "longitude");
            BinaryDataExposeUtility.Look(ref LatitudeSigned, "latitudeSigned");
            BinaryDataExposeUtility.Look(ref ElevationKm, "elevationKm");
            BinaryDataExposeUtility.Look(ref TileCenters, "tileCenters");

            BinaryDataExposeUtility.Look(ref OceanMask, "oceanMask");
            BinaryDataExposeUtility.Look(ref TrueOceanMask, "trueOceanMask");
            BinaryDataExposeUtility.Look(ref WaterBodyID, "waterBodyID");
            BinaryDataExposeUtility.Look(ref NearestOceanTile, "nearestOceanTile");
            BinaryDataExposeUtility.Look(ref OceanDistKm, "oceanDistKm");

            BinaryDataExposeUtility.Look(ref LatitudinalSeaLevelTemp, "latitudinalSeaLevelTemp");
            BinaryDataExposeUtility.Look(ref SST, "sst");
            BinaryDataExposeUtility.Look(ref CurrentsAnomalySST, "currentsAnomalySST");
            BinaryDataExposeUtility.Look(ref CoastalModerationStrength, "coastalModerationStrength");
            BinaryDataExposeUtility.Look(ref AnnualMeanTemp, "annualMeanTemp");

            BinaryDataExposeUtility.Look(ref Continentality, "continentality");
            BinaryDataExposeUtility.Look(ref SeasonalAmp, "seasonalAmp");

            BinaryDataExposeUtility.Look(ref WindDirection, "windDirection");
            BinaryDataExposeUtility.Look(ref WindSpeed, "windSpeed");
            BinaryDataExposeUtility.Look(ref ConvergenceIndex, "convergenceIndex");
            BinaryDataExposeUtility.Look(ref UpwindNeighbor, "upwindNeighbor");
            BinaryDataExposeUtility.Look(ref UpwindReliefIndex, "upwindReliefIndex");
            BinaryDataExposeUtility.Look(ref LeewardShadowIndex, "leewardShadowIndex");

            BinaryDataExposeUtility.Look(ref MonsoonBoost, "monsoonBoost");
            BinaryDataExposeUtility.Look(ref MonsoonMoistureSource, "monsoonMoistureSource");
            BinaryDataExposeUtility.Look(ref MonsoonLiftIndex, "monsoonLiftIndex");

            BinaryDataExposeUtility.Look(ref TemperatureCapacity, "temperatureCapacity");

            BinaryDataExposeUtility.Look(ref AdvectedIncomingMoistureMm, "advectedIncomingMoistureMm");
            BinaryDataExposeUtility.Look(ref AdvectedRemainingMoistureMm, "advectedRemainingMoistureMm");
            BinaryDataExposeUtility.Look(ref AdvectedCondensationMm, "advectedCondensationMm");
            BinaryDataExposeUtility.Look(ref AdvectedDryingRatio, "advectedDryingRatio");

            BinaryDataExposeUtility.Look(ref AnnualRainfall, "annualRainfall");
            BinaryDataExposeUtility.Look(ref PotentialEvapotranspiration, "potentialEvapotranspiration");
            BinaryDataExposeUtility.Look(ref AridityIndex, "aridityIndex");

            BinaryDataExposeUtility.Look(ref MaxSummerTemp, "maxSummerTemp");
            BinaryDataExposeUtility.Look(ref MinWinterTemp, "minWinterTemp");

            BinaryDataExposeUtility.Look(ref Prominence, "prominence");
            BinaryDataExposeUtility.Look(ref Relief, "relief");
            BinaryDataExposeUtility.Look(ref Ruggedness, "ruggedness");
            BinaryDataExposeUtility.Look(ref Steepness, "steepness");
            BinaryDataExposeUtility.LookEnum(ref HillinessClassification, "hillinessClassification");

            BinaryDataExposeUtility.Look(ref ElevationBand, "elevationBand");
            BinaryDataExposeUtility.Look(ref BandThresholds, "bandThresholds");
            BinaryDataExposeUtility.Look(ref SmoothedElevation, "smoothedElevation");
            BinaryDataExposeUtility.Look(ref IsMajorPeak, "isMajorPeak");
            BinaryDataExposeUtility.Look(ref RidgeTier, "ridgeTier");
            BinaryDataExposeUtility.Look(ref IsRidgeTile, "isRidgeTile");

            BinaryDataExposeUtility.Look(ref RidgeNormal, "ridgeNormal");
            BinaryDataExposeUtility.Look(ref RidgeNeighborSystemID, "ridgeNeighborSystemID");

            BinaryDataExposeUtility.Look(ref FlowDirectionTopology, "flowDirection");
            BinaryDataExposeUtility.Look(ref FlowAccumulationTopology, "flowAccumulation");
            BinaryDataExposeUtility.Look(ref EndorheicMask, "endorheicMask");
            BinaryDataExposeUtility.Look(ref InitialBasinID, "initialBasinID");
            BinaryDataExposeUtility.Look(ref MergedBasinIDTopology, "mergedBasinID");
            BinaryDataExposeUtility.Look(ref RiverSystemIDTopology, "riverSystemID");
            BinaryDataExposeUtility.Look(ref SpillDirectionTopology, "spillDirection");
            BinaryDataExposeUtility.Look(ref MergedLakeIDTopology, "mergedLakeID");
            BinaryDataExposeUtility.LookEnum(ref DrainageTypeClassification, "drainageTypeClassification");

            BinaryDataExposeUtility.Look(ref FlowDirectionHydrology, "flowDirectionHydrology");
            BinaryDataExposeUtility.Look(ref FlowAccumulationHydrology, "flowAccumulationHydrology");
            BinaryDataExposeUtility.Look(ref MergedBasinIDHydrology, "mergedBasinIDHydrology");
            BinaryDataExposeUtility.Look(ref SpillDirectionHydrology, "spillDirectionHydrology");
            BinaryDataExposeUtility.Look(ref MergedLakeIDHydrology, "mergedLakeIDHydrology");
            BinaryDataExposeUtility.Look(ref RiverSystemIDHydrology, "riverSystemIDHydrology");
            BinaryDataExposeUtility.Look(ref EndorheicMaskHydrology, "endorheicMaskHydrology");
            BinaryDataExposeUtility.Look(ref RiverPresence, "riverPresence");
            BinaryDataExposeUtility.Look(ref RiverMagnitude, "riverMagnitude");
            BinaryDataExposeUtility.Look(ref WaterBodyType, "waterBodyType");

            BinaryDataExposeUtility.Look(ref Swampiness, "swampiness");
        }

        public void RememberExactElevations(PlanetLayer layer)
        {
            if (layer?.Tiles == null)
                return;
            int count = layer.Tiles.Count;
            ExactElevationMeters = new float[count];
            for (int i = 0; i < count; i++)
            {
                Tile tile = layer.Tiles[i];
                ExactElevationMeters[i] = tile != null ? tile.elevation : 0f;
            }
        }

        public bool ApplyExactElevations(PlanetLayer layer)
        {
            if (layer?.Tiles == null || ExactElevationMeters == null || ExactElevationMeters.Length != layer.Tiles.Count)
                return false;
            for (int i = 0; i < ExactElevationMeters.Length; i++)
            {
                Tile tile = layer.Tiles[i];
                if (tile != null)
                    tile.elevation = ExactElevationMeters[i];
            }
            return true;
        }

        public void DeriveGameplayFromTiles()
        {
            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = grid?.Surface;
            if (layer?.Tiles == null)
                return;
            int count = layer.Tiles.Count;
            AnnualMeanTemp = Fit(AnnualMeanTemp, count);
            AnnualRainfall = Fit(AnnualRainfall, count);
            Swampiness = Fit(Swampiness, count);
            ElevationKm = Fit(ElevationKm, count);
            ColumnDepth = Fit(ColumnDepth, count);
            Latitude = Fit(Latitude, count);
            Longitude = Fit(Longitude, count);
            LatitudeSigned = Fit(LatitudeSigned, count);
            TileCenters = Fit(TileCenters, count);
            OceanMask = Fit(OceanMask, count);
            MaxSummerTemp = Fit(MaxSummerTemp, count);
            MinWinterTemp = Fit(MinWinterTemp, count);
            for (int i = 0; i < count; i++)
            {
                Tile tile = layer.Tiles[i];
                if (tile == null)
                    continue;
                AnnualMeanTemp[i] = tile.temperature;
                AnnualRainfall[i] = tile.rainfall;
                Swampiness[i] = tile.swampiness;
                ElevationKm[i] = tile.elevation / 1000f;
                Vector2 longLat = grid.LongLatOf(i);
                Latitude[i] = Mathf.Abs(longLat.y) * Mathf.Deg2Rad;
                LatitudeSigned[i] = longLat.y * Mathf.Deg2Rad;
                Longitude[i] = longLat.x * Mathf.Deg2Rad;
                TileCenters[i] = grid.GetTileCenter(i).normalized;
                OceanMask[i] = tile.elevation <= 0f;
                float heightKm = OceanMask[i] ? 0f : Mathf.Max(0f, ElevationKm[i]);
                ColumnDepth[i] = Mathf.Exp(-heightKm / 8f);
                float half = SeasonalAmp != null && i < SeasonalAmp.Length ? SeasonalAmp[i] / 2f : 0f;
                MaxSummerTemp[i] = tile.temperature + half;
                MinWinterTemp[i] = tile.temperature - half;
            }
        }

        public void ValidateArrayLengths()
        {
            int count = Find.WorldGrid?.TilesCount ?? 0;
            if (count <= 0)
                return;
            NullIfLengthMismatch(ref SeasonalAmp, count, "seasonalAmp");
            NullIfLengthMismatch(ref AridityIndex, count, "aridity");
            NullIfLengthMismatch(ref Continentality, count, "continentality");
            NullIfLengthMismatch(ref UpwindReliefIndex, count, "upwindRelief");
            NullIfLengthMismatch(ref LeewardShadowIndex, count, "leeward");
            NullIfLengthMismatch(ref MonsoonBoost, count, "monsoonBoost");
            NullIfLengthMismatch(ref DriestSeasonRainMm, count, "driestRain");
            NullIfLengthMismatch(ref WettestSeasonRainMm, count, "wettestRain");
            NullIfLengthMismatch(ref FlowAccumulationTopology, count, "flowAccum");
            NullIfLengthMismatch(ref ExactElevationMeters, count, "exactElevation");
        }

        public void CopyDiagnosticsFrom(ClimateData source)
        {
            if (source == null)
                return;
            WindDirection = source.WindDirection;
            SummerWind = source.SummerWind;
            WinterWind = source.WinterWind;
            EquinoxWind = source.EquinoxWind;
            JuneConvergence = source.JuneConvergence;
            DecemberConvergence = source.DecemberConvergence;
            EquinoxConvergence = source.EquinoxConvergence;
            TransportJune = source.TransportJune;
            TransportDecember = source.TransportDecember;
            TransportEquinox = source.TransportEquinox;
            TileCell = source.TileCell;
            CellCoverTile = source.CellCoverTile;
            CellAnnual = source.CellAnnual;
            CellAmp = source.CellAmp;
            CellTransportJuneEast = source.CellTransportJuneEast;
            CellTransportJuneNorth = source.CellTransportJuneNorth;
            CellTransportDecEast = source.CellTransportDecEast;
            CellTransportDecNorth = source.CellTransportDecNorth;
            CellTransportEqEast = source.CellTransportEqEast;
            CellTransportEqNorth = source.CellTransportEqNorth;
            MaritimeJune = source.MaritimeJune;
            MaritimeDecember = source.MaritimeDecember;
            MaritimeEquinox = source.MaritimeEquinox;
            MaritimeAnnual = source.MaritimeAnnual;
            UpwindSST = source.UpwindSST;
            ContinentalAir = source.ContinentalAir;
            WindSpeed = source.WindSpeed;
            ConvergenceIndex = source.ConvergenceIndex;
            UpwindNeighbor = source.UpwindNeighbor;
            OceanDistKm = source.OceanDistKm;
            InlandBodyStrength = source.InlandBodyStrength;
            InlandWaterDistKm = source.InlandWaterDistKm;
            InlandWaterInfluence = source.InlandWaterInfluence;
            TrueOceanMask = source.TrueOceanMask;
            WaterBodyID = source.WaterBodyID;
            NearestOceanTile = source.NearestOceanTile;
            LatitudinalSeaLevelTemp = source.LatitudinalSeaLevelTemp;
            SST = source.SST;
            CurrentsAnomalySST = source.CurrentsAnomalySST;
            CoastalModerationStrength = source.CoastalModerationStrength;
            TemperatureCapacity = source.TemperatureCapacity;
            AdvectedIncomingMoistureMm = source.AdvectedIncomingMoistureMm;
            AdvectedRemainingMoistureMm = source.AdvectedRemainingMoistureMm;
            AdvectedCondensationMm = source.AdvectedCondensationMm;
            AdvectedDryingRatio = source.AdvectedDryingRatio;
            MoistureDeletedMm = source.MoistureDeletedMm;
            SummerCondensationMm = source.SummerCondensationMm;
            WinterCondensationMm = source.WinterCondensationMm;
            EquinoxCondensationMm = source.EquinoxCondensationMm;
            PrecipitableWaterMm = source.PrecipitableWaterMm;
            UpperColumnWaterMm = source.UpperColumnWaterMm;
            OceanEvaporationMm = source.OceanEvaporationMm;
            LandEvaporationMm = source.LandEvaporationMm;
            RainHumidMm = source.RainHumidMm;
            RainRisingMm = source.RainRisingMm;
            RainUpperMm = source.RainUpperMm;
            DistanceAlongWindKm = source.DistanceAlongWindKm;
            OceanWaterShare = source.OceanWaterShare;
            ColumnDepth = source.ColumnDepth;
            SummerIncomingMm = source.SummerIncomingMm;
            WinterIncomingMm = source.WinterIncomingMm;
            SummerDeletedMm = source.SummerDeletedMm;
            WinterDeletedMm = source.WinterDeletedMm;
            MonsoonMoistureSource = source.MonsoonMoistureSource;
            MonsoonLiftIndex = source.MonsoonLiftIndex;
            PotentialEvapotranspiration = source.PotentialEvapotranspiration;
            DailyRangeC = source.DailyRangeC;
            Prominence = source.Prominence;
            Relief = source.Relief;
            Ruggedness = source.Ruggedness;
            Steepness = source.Steepness;
            HillinessClassification = source.HillinessClassification;
            ElevationBand = source.ElevationBand;
            BandThresholds = source.BandThresholds;
            SmoothedElevation = source.SmoothedElevation;
            IsMajorPeak = source.IsMajorPeak;
            RidgeTier = source.RidgeTier;
            IsRidgeTile = source.IsRidgeTile;
            RidgeNormal = source.RidgeNormal;
            RidgeNeighborSystemID = source.RidgeNeighborSystemID;
            FlowDirectionTopology = source.FlowDirectionTopology;
            FlowRain = source.FlowRain;
            EndorheicMask = source.EndorheicMask;
            InitialBasinID = source.InitialBasinID;
            MergedBasinIDTopology = source.MergedBasinIDTopology;
            RiverSystemIDTopology = source.RiverSystemIDTopology;
            SpillDirectionTopology = source.SpillDirectionTopology;
            MergedLakeIDTopology = source.MergedLakeIDTopology;
            DrainageTypeClassification = source.DrainageTypeClassification;
            FlowDirectionHydrology = source.FlowDirectionHydrology;
            FlowAccumulationHydrology = source.FlowAccumulationHydrology;
            MergedBasinIDHydrology = source.MergedBasinIDHydrology;
            SpillDirectionHydrology = source.SpillDirectionHydrology;
            MergedLakeIDHydrology = source.MergedLakeIDHydrology;
            RiverSystemIDHydrology = source.RiverSystemIDHydrology;
            EndorheicMaskHydrology = source.EndorheicMaskHydrology;
            RiverPresence = source.RiverPresence;
            RiverMagnitude = source.RiverMagnitude;
            WaterBodyType = source.WaterBodyType;
        }

        private static float[] Fit(float[] values, int count) => values != null && values.Length == count ? values : new float[count];
        private static bool[] Fit(bool[] values, int count) => values != null && values.Length == count ? values : new bool[count];
        private static Vector3[] Fit(Vector3[] values, int count) => values != null && values.Length == count ? values : new Vector3[count];

        private static void NullIfLengthMismatch(ref float[] values, int count, string name)
        {
            if (values != null && values.Length != count)
            {
                Log.Warning($"[RP] Climate array '{name}' length {values.Length} does not match {count} tiles. Dropping it.");
                values = null;
            }
        }

        public void InitializeArrays(int tileCount, bool includeTier2 = false)
        {
            Latitude = new float[tileCount];
            Longitude = new float[tileCount];
            LatitudeSigned = new float[tileCount];
            ElevationKm = new float[tileCount];
            ColumnDepth = new float[tileCount];
            TileCenters = new Vector3[tileCount];

            OceanMask = new bool[tileCount];
            TrueOceanMask = new bool[tileCount];
            WaterBodyID = new int[tileCount];
            NearestOceanTile = new int[tileCount];
            OceanDistKm = new float[tileCount];
            InlandBodyStrength = new float[tileCount];
            InlandWaterDistKm = new float[tileCount];
            InlandWaterInfluence = new float[tileCount];

            for (int i = 0; i < tileCount; i++)
                WaterBodyID[i] = -1;

            LatitudinalSeaLevelTemp = new float[tileCount];
            SST = new float[tileCount];
            CurrentsAnomalySST = new float[tileCount];
            CoastalModerationStrength = new float[tileCount];
            AnnualMeanTemp = new float[tileCount];

            Continentality = new float[tileCount];
            SeasonalAmp = new float[tileCount];

            WindDirection = new Vector3[tileCount];
            SummerWind = new Vector3[tileCount];
            WinterWind = new Vector3[tileCount];
            EquinoxWind = new Vector3[tileCount];
            WindSpeed = new float[tileCount];
            ConvergenceIndex = new float[tileCount];
            UpwindNeighbor = new int[tileCount];
            UpwindReliefIndex = new float[tileCount];
            LeewardShadowIndex = new float[tileCount];

            MonsoonBoost = new float[tileCount];
            MonsoonMoistureSource = new float[tileCount];
            MonsoonLiftIndex = new float[tileCount];

            TemperatureCapacity = new float[tileCount];

            AdvectedIncomingMoistureMm = new float[tileCount];
            AdvectedRemainingMoistureMm = new float[tileCount];
            AdvectedCondensationMm = new float[tileCount];
            AdvectedDryingRatio = new float[tileCount];
            MoistureDeletedMm = new float[tileCount];
            SummerCondensationMm = new float[tileCount];
            WinterCondensationMm = new float[tileCount];
            EquinoxCondensationMm = new float[tileCount];
            DriestSeasonRainMm = new float[tileCount];
            WettestSeasonRainMm = new float[tileCount];
            SummerIncomingMm = new float[tileCount];
            WinterIncomingMm = new float[tileCount];
            SummerDeletedMm = new float[tileCount];
            WinterDeletedMm = new float[tileCount];

            AnnualRainfall = new float[tileCount];
            PotentialEvapotranspiration = new float[tileCount];
            DailyRangeC = new float[tileCount];
            AridityIndex = new float[tileCount];

            MaxSummerTemp = new float[tileCount];
            MinWinterTemp = new float[tileCount];

            Prominence = new float[tileCount];
            Relief = new float[tileCount];
            Ruggedness = new float[tileCount];
            Steepness = new float[tileCount];
            HillinessClassification = new Hilliness[tileCount];

            ElevationBand = new int[tileCount];
            BandThresholds = new float[6];
            SmoothedElevation = new float[tileCount];
            IsMajorPeak = new bool[tileCount];
            RidgeTier = new byte[tileCount];
            IsRidgeTile = new bool[tileCount];

            RidgeNormal = new Vector3[tileCount];
            RidgeNeighborSystemID = new int[tileCount];

            for (int i = 0; i < tileCount; i++)
            {
                RidgeNormal[i] = Vector3.zero;
                RidgeNeighborSystemID[i] = -1;
                UpwindNeighbor[i] = -1;
            }

            FlowDirectionTopology = new int[tileCount];
            FlowAccumulationTopology = new float[tileCount];
            FlowRain = new float[tileCount];
            EndorheicMask = new bool[tileCount];
            InitialBasinID = new int[tileCount];
            MergedBasinIDTopology = new int[tileCount];
            RiverSystemIDTopology = new int[tileCount];
            SpillDirectionTopology = new int[tileCount];
            MergedLakeIDTopology = new int[tileCount];
            DrainageTypeClassification = new DrainageType[tileCount];

            if (includeTier2)
            {
                FlowDirectionHydrology = new int[tileCount];
                FlowAccumulationHydrology = new float[tileCount];
                MergedBasinIDHydrology = new int[tileCount];
                SpillDirectionHydrology = new int[tileCount];
                MergedLakeIDHydrology = new int[tileCount];
                RiverSystemIDHydrology = new int[tileCount];
                EndorheicMaskHydrology = new bool[tileCount];
                RiverPresence = new float[tileCount];
                RiverMagnitude = new byte[tileCount];
                WaterBodyType = new byte[tileCount];
            }

            for (int i = 0; i < tileCount; i++)
            {
                FlowDirectionTopology[i] = -1;
                FlowAccumulationTopology[i] = 1.0f;
                EndorheicMask[i] = false;
                InitialBasinID[i] = -1;
                MergedBasinIDTopology[i] = -1;
                RiverSystemIDTopology[i] = -1;
                SpillDirectionTopology[i] = -1;
                MergedLakeIDTopology[i] = -1;
                DrainageTypeClassification[i] = DrainageType.ExorheicOcean;
                if (!includeTier2)
                    continue;
                FlowDirectionHydrology[i] = -1;
                FlowAccumulationHydrology[i] = 0f;
                MergedBasinIDHydrology[i] = -1;
                SpillDirectionHydrology[i] = -1;
                MergedLakeIDHydrology[i] = -1;
                RiverSystemIDHydrology[i] = -1;
                EndorheicMaskHydrology[i] = false;
                RiverPresence[i] = 0f;
                RiverMagnitude[i] = 0;
                WaterBodyType[i] = 0;
            }

            Swampiness = new float[tileCount];
        }

        public void ClearAllArrays()
        {
            FieldInfo[] fields = GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType.IsArray)
                    fields[i].SetValue(this, null);
            }
        }
    }
}
