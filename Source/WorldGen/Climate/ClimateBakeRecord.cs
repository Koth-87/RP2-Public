using System.Runtime.InteropServices;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.Utilities.Logging;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Geography;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// What was true when this world's climate was baked. Diagnostics rebuild from this, not from the player's current settings.
    /// climateSettings includes the circulation numbers (warm and cold heat-low strengths, the inland and coastal leftover strengths, the coastal fade length, the two smoothing lengths, the retired single strength, ocean drag, land drag, the profile scale, the ocean-share power, the seasonal shape, the ocean reference, the two surface factors, and the ocean-air numbers: fade length over land and over water, storm drift, breeze length, winter weight, coastal blend, and the two ocean-swing shares).
    /// Those four have no settings-screen control. A world keeps the copies captured here even if the mod-settings defaults change later.
    /// </summary>
    public class ClimateBakeRecord : IExposable
    {
        public int modelVersion;
        public ClimateConfiguration climateSettings = new ClimateConfiguration();
        public HydrologyConfiguration hydrologySettings = new HydrologyConfiguration();
        public SeaLevel seaLevel = SeaLevel.Normal;
        public AxialTilt axialTilt = AxialTilt.Normal;
        public int subcount = 10;
        public float coverage = 1f;
        public OverallRainfall rainfall = OverallRainfall.Normal;
        public OverallTemperature temperature = OverallTemperature.Normal;
        public float riverDensity = 1f;
        public int inputHash;
        public int bakedAtTick;
        public string source = "Generation";
        public bool earthHeightmap;

        public void ExposeData()
        {
            if (climateSettings != null)
                climateSettings.ForceSaveAll = true;
            if (hydrologySettings != null)
                hydrologySettings.ForceSaveAll = true;

            Scribe_Values.Look(ref modelVersion, "modelVersion", 0);
            Scribe_Deep.Look(ref climateSettings, "climateSettings");
            Scribe_Deep.Look(ref hydrologySettings, "hydrologySettings");
            Scribe_Values.Look(ref seaLevel, "seaLevel", SeaLevel.Normal);
            Scribe_Values.Look(ref axialTilt, "axialTilt", AxialTilt.Normal);
            Scribe_Values.Look(ref subcount, "subcount", 10);
            Scribe_Values.Look(ref coverage, "coverage", 1f);
            Scribe_Values.Look(ref rainfall, "rainfall", OverallRainfall.Normal);
            Scribe_Values.Look(ref temperature, "temperature", OverallTemperature.Normal);
            Scribe_Values.Look(ref riverDensity, "riverDensity", 1f);
            Scribe_Values.Look(ref inputHash, "inputHash", 0);
            Scribe_Values.Look(ref bakedAtTick, "bakedAtTick", 0);
            Scribe_Values.Look(ref source, "source", "Generation");
            Scribe_Values.Look(ref earthHeightmap, "earthHeightmap", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (climateSettings == null)
                    climateSettings = new ClimateConfiguration();
                if (hydrologySettings == null)
                    hydrologySettings = new HydrologyConfiguration();
                climateSettings.ForceSaveAll = true;
                hydrologySettings.ForceSaveAll = true;
            }
        }

        public void Capture(ClimateConfiguration climate, HydrologyConfiguration hydrology, string bakeSource)
        {
            modelVersion = ClimateModelVersion.Current;
            climateSettings = new ClimateConfiguration();
            if (climate != null)
                climateSettings.CopyFrom(climate);
            hydrologySettings = new HydrologyConfiguration();
            if (hydrology != null)
                hydrologySettings.CopyFrom(hydrology);
            seaLevel = Planets_GameComponent.ActiveSeaLevel;
            axialTilt = Planets_GameComponent.ActiveAxialTilt;
            subcount = Planets_GameComponent.subcount;
            coverage = Find.World?.info?.planetCoverage ?? 1f;
            rainfall = Find.World?.info?.overallRainfall ?? OverallRainfall.Normal;
            temperature = Find.World?.info?.overallTemperature ?? OverallTemperature.Normal;
            riverDensity = Planets_GameComponent.riverDensity;
            ClimateData baked = Find.World?.GetComponent<ClimateData>();
            inputHash = HashGeography(Find.WorldGrid?.Surface, baked?.ExactElevationMeters);
            bakedAtTick = Find.TickManager?.TicksGame ?? 0;
            source = bakeSource ?? "Generation";
        }

        public static int HashGeography(PlanetLayer layer, float[] exactElevation)
        {
            unchecked
            {
                uint hash = 2166136261;
                int count = layer?.Tiles?.Count ?? 0;
                bool useExact = exactElevation != null && exactElevation.Length == count;
                for (int i = 0; i < count; i++)
                {
                    Tile tile = layer.Tiles[i];
                    int elevation = useExact
                        ? FloatBits(exactElevation[i])
                        : (tile == null ? 0 : UnityEngine.Mathf.RoundToInt(tile.elevation));
                    hash ^= (uint)elevation;
                    hash *= 16777619;
                    int water = 0;
                    if (tile?.biome == BiomeDefOf.Ocean)
                        water = 1;
                    else if (tile?.biome == BiomeDefOf.Lake)
                        water = 2;
                    hash ^= (uint)water;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }

        private static int FloatBits(float value)
        {
            FloatBitsUnion bits = default;
            bits.Float = value;
            return bits.Int;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBitsUnion
        {
            [FieldOffset(0)] public float Float;
            [FieldOffset(0)] public int Int;
        }
    }
}
