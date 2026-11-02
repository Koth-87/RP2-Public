using System;
using RimWorld.Planet;
using Verse;
using Planets.Core.Configuration;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Geography;

namespace Planets.Presets.WorldGen
{
    /// <summary>
    /// Stores a complete world generation configuration: world creation parameters
    /// plus Advanced Climate and Hydrology settings.
    /// </summary>
    public class WorldGenPreset : IExposable
    {
        public string name = "Unnamed";
        public string description = "";
        public long savedDateTicks = DateTime.Now.Ticks;

        public string planetName = "";
        public string seedString = "";
        public int subcount = 10;
        public float riverDensity = 1f;
        public float ancientRoadDensity = 1f;
        public float factionRoadDensity = 1f;
        public float planetCoverage = 0.3f;
        public SeaLevel seaLevel = SeaLevel.Normal;
        public OverallRainfall rainfall = OverallRainfall.Normal;
        public OverallTemperature temperature = OverallTemperature.Normal;
        public OverallPopulation population = OverallPopulation.Normal;
        public float pollution = 0.05f;
        public LandmarkDensity landmarkDensity = LandmarkDensity.Normal;
        public AxialTilt axialTilt = AxialTilt.Normal;
        public float mountains = 1f;
        public float caveSystems = 1f;
        public float insectTerritoryScale = 1f;
        public float hillinessBias = 0f;
        public bool includeSeedAndName = false;

        public ClimateConfiguration climateConfig = new ClimateConfiguration();
        public HydrologyConfiguration hydroConfig = new HydrologyConfiguration();

        public DateTime SavedDate => new DateTime(savedDateTicks);

        public WorldGenPreset() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "Unnamed");
            Scribe_Values.Look(ref description, "description", "");
            Scribe_Values.Look(ref savedDateTicks, "savedDateTicks", DateTime.Now.Ticks);
            Scribe_Values.Look(ref planetName, "planetName", "");
            Scribe_Values.Look(ref seedString, "seedString", "");
            Scribe_Values.Look(ref subcount, "subcount", 10);
            Scribe_Values.Look(ref riverDensity, "riverDensity", 1f);
            Scribe_Values.Look(ref ancientRoadDensity, "ancientRoadDensity", 1f);
            Scribe_Values.Look(ref factionRoadDensity, "factionRoadDensity", 1f);
            Scribe_Values.Look(ref planetCoverage, "planetCoverage", 0.3f);
            Scribe_Values.Look(ref seaLevel, "seaLevel", SeaLevel.Normal);
            Scribe_Values.Look(ref rainfall, "rainfall", OverallRainfall.Normal);
            Scribe_Values.Look(ref temperature, "temperature", OverallTemperature.Normal);
            Scribe_Values.Look(ref population, "population", OverallPopulation.Normal);
            Scribe_Values.Look(ref pollution, "pollution", 0.05f);
            Scribe_Values.Look(ref landmarkDensity, "landmarkDensity", LandmarkDensity.Normal);
            Scribe_Values.Look(ref axialTilt, "axialTilt", AxialTilt.Normal);
            Scribe_Values.Look(ref mountains, "mountains", 1f);
            Scribe_Values.Look(ref caveSystems, "caveSystems", 1f);
            Scribe_Values.Look(ref insectTerritoryScale, "insectTerritoryScale", 1f);
            Scribe_Values.Look(ref hillinessBias, "hillinessBias", 0f);
            Scribe_Values.Look(ref includeSeedAndName, "includeSeedAndName", false);
            Scribe_Deep.Look(ref climateConfig, "climateConfig");
            Scribe_Deep.Look(ref hydroConfig, "hydroConfig");
            if (climateConfig == null)
                climateConfig = new ClimateConfiguration();
            if (hydroConfig == null)
                hydroConfig = new HydrologyConfiguration();
            if (seedString == null)
                seedString = "";
        }

        public string GetSummary()
        {
            return $"{planetCoverage:P0} coverage, {temperature}, {rainfall}";
        }
    }
}
