using RimWorld.Planet;
using UnityEngine;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Lightweight climate view for a real world tile or a synthetic/interpolated climate sample (WS1-7).
    /// When <see cref="HasOverrides"/> is false, properties read directly from <see cref="Climate"/> for <see cref="TileID"/>.
    /// When <see cref="HasOverrides"/> is true (such as Geological Landforms midpoint transitions or editor previews),
    /// properties return the sample's overridden values while keeping <see cref="TileID"/> for deterministic noise.
    /// </summary>
    public readonly struct ClimateSample
    {
        public readonly int TileID;
        public readonly bool HasOverrides;
        private readonly Tile backingTile;

        private readonly float annualTemp;
        private readonly float winterMin;
        private readonly float summerMax;
        private readonly float seasonalAmp;
        private readonly float rainfall;
        private readonly float aridity;
        private readonly float elevation;
        private readonly float swampiness;
        private readonly float continentality;
        private readonly float windward;
        private readonly float leeward;
        private readonly float monsoonBoost;
        private readonly float driestSeasonRain;
        private readonly float wettestSeasonRain;
        private readonly float flowAccumulation;
        private readonly Hilliness hilliness;
        private readonly bool waterCovered;

        private ClimateSample(Tile tile, int tileID)
        {
            TileID = tileID;
            HasOverrides = false;
            backingTile = tile;
            annualTemp = 0f;
            winterMin = 0f;
            summerMax = 0f;
            seasonalAmp = 0f;
            rainfall = 0f;
            aridity = 0f;
            elevation = tile != null ? tile.elevation : 0f;
            swampiness = 0f;
            continentality = 0f;
            windward = 0f;
            leeward = 0f;
            monsoonBoost = 0f;
            driestSeasonRain = 0f;
            wettestSeasonRain = 0f;
            flowAccumulation = 0f;
            hilliness = tile != null ? tile.hilliness : Hilliness.Flat;
            waterCovered = tile != null && tile.WaterCovered;
        }

        public ClimateSample(
            int tileID,
            float annualTemp,
            float winterMin,
            float summerMax,
            float seasonalAmp,
            float rainfall,
            float aridity,
            float elevation,
            float swampiness,
            float continentality,
            float windward,
            float leeward,
            float monsoonBoost,
            float driestSeasonRain,
            float wettestSeasonRain,
            float flowAccumulation,
            Hilliness hilliness,
            bool waterCovered)
        {
            TileID = tileID;
            HasOverrides = true;
            backingTile = null;
            this.annualTemp = annualTemp;
            this.winterMin = winterMin;
            this.summerMax = summerMax;
            this.seasonalAmp = seasonalAmp;
            this.rainfall = rainfall;
            this.aridity = aridity;
            this.elevation = elevation;
            this.swampiness = swampiness;
            this.continentality = continentality;
            this.windward = windward;
            this.leeward = leeward;
            this.monsoonBoost = monsoonBoost;
            this.driestSeasonRain = driestSeasonRain;
            this.wettestSeasonRain = wettestSeasonRain;
            this.flowAccumulation = flowAccumulation;
            this.hilliness = hilliness;
            this.waterCovered = waterCovered;
        }

        public static ClimateSample FromTile(Tile tile, int tileID) => new ClimateSample(tile, tileID);

        /// <summary>
        /// Builds an interpolated midpoint sample between two adjacent tiles (WS1-7).
        /// Aridity is computed as midpoint rainfall divided by the average of the two tiles' annual PET.
        /// </summary>
        public static ClimateSample Midpoint(Tile tileA, int idA, Tile tileB, int idB, int rootTileID)
        {
            float elev = 0.5f * ((tileA?.elevation ?? Climate.Elevation(idA)) + (tileB?.elevation ?? Climate.Elevation(idB)));
            float tAnn = 0.5f * (Climate.AnnualTemp(idA) + Climate.AnnualTemp(idB));
            float tWin = 0.5f * (Climate.WinterMin(idA) + Climate.WinterMin(idB));
            float tSum = 0.5f * (Climate.SummerMax(idA) + Climate.SummerMax(idB));
            float amp = 0.5f * (Climate.SeasonalAmp(idA) + Climate.SeasonalAmp(idB));
            float rain = 0.5f * (Climate.Rainfall(idA) + Climate.Rainfall(idB));
            float petMid = 0.5f * (Climate.PET(idA) + Climate.PET(idB));
            float ai = petMid > 1f
                ? Mathf.Max(0.01f, rain / Mathf.Max(petMid, 50f))
                : 0.5f * (Climate.Aridity(idA) + Climate.Aridity(idB));
            float swamp = 0.5f * (Climate.Swampiness(idA) + Climate.Swampiness(idB));
            float cont = 0.5f * (Climate.Continentality(idA) + Climate.Continentality(idB));
            float windw = 0.5f * (Climate.Windward(idA) + Climate.Windward(idB));
            float leew = 0.5f * (Climate.Leeward(idA) + Climate.Leeward(idB));
            float monsoon = 0.5f * (Climate.MonsoonBoost(idA) + Climate.MonsoonBoost(idB));
            float driest = 0.5f * (Climate.DriestSeasonRain(idA) + Climate.DriestSeasonRain(idB));
            float wettest = 0.5f * (Climate.WettestSeasonRain(idA) + Climate.WettestSeasonRain(idB));
            float flow = 0.5f * (Climate.FlowAccumulation(idA) + Climate.FlowAccumulation(idB));
            Hilliness hill = (Hilliness)Mathf.Max((int)(tileA?.hilliness ?? Hilliness.Flat), (int)(tileB?.hilliness ?? Hilliness.Flat));
            bool water = (tileA?.WaterCovered ?? false) && (tileB?.WaterCovered ?? false);

            return new ClimateSample(
                rootTileID, tAnn, tWin, tSum, amp, rain, ai, elev, swamp,
                cont, windw, leew, monsoon, driest, wettest, flow, hill, water);
        }

        public float AnnualTemp => HasOverrides ? annualTemp : Climate.AnnualTemp(TileID);
        public float WinterMin => HasOverrides ? winterMin : Climate.WinterMin(TileID);
        public float SummerMax => HasOverrides ? summerMax : Climate.SummerMax(TileID);
        public float SeasonalAmp => HasOverrides ? seasonalAmp : Climate.SeasonalAmp(TileID);
        public float Rainfall => HasOverrides ? rainfall : Climate.Rainfall(TileID);
        public float Aridity => HasOverrides ? aridity : Climate.Aridity(TileID);
        public float Elevation => HasOverrides ? elevation : (backingTile != null ? backingTile.elevation : Climate.Elevation(TileID));
        public float Swampiness => HasOverrides ? swampiness : Climate.Swampiness(TileID);
        public float Continentality => HasOverrides ? continentality : Climate.Continentality(TileID);
        public float Windward => HasOverrides ? windward : Climate.Windward(TileID);
        public float Leeward => HasOverrides ? leeward : Climate.Leeward(TileID);
        public float MonsoonBoost => HasOverrides ? monsoonBoost : Climate.MonsoonBoost(TileID);
        public float DriestSeasonRain => HasOverrides ? driestSeasonRain : Climate.DriestSeasonRain(TileID);
        public float WettestSeasonRain => HasOverrides ? wettestSeasonRain : Climate.WettestSeasonRain(TileID);
        public float FlowAccumulation => HasOverrides ? flowAccumulation : Climate.FlowAccumulation(TileID);
        public Hilliness Hilliness => HasOverrides ? hilliness : (backingTile != null ? backingTile.hilliness : Hilliness.Flat);
        public bool WaterCovered => HasOverrides ? waterCovered : (backingTile != null && backingTile.WaterCovered);
    }
}
