using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Geography;

namespace Planets.Presets.WorldGen
{
    /// <summary>
    /// Immutable built-in world preset definition (e.g. Vanilla, Earthlike, Ocean, Iceball).
    /// </summary>
    public class WorldPreset
    {
        public string Name { get; }
        public SeaLevel SeaLevel { get; }
        public AxialTilt AxialTilt { get; }
        public OverallRainfall Rainfall { get; }
        public OverallTemperature Temperature { get; }
        public OverallPopulation Population { get; }

        public WorldPreset(
            string name,
            SeaLevel seaLevel,
            AxialTilt axialTilt,
            OverallRainfall rainfall,
            OverallTemperature temperature,
            OverallPopulation population)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            SeaLevel = seaLevel;
            AxialTilt = axialTilt;
            Rainfall = rainfall;
            Temperature = temperature;
            Population = population;
        }
    }

    /// <summary>
    /// Fluent builder for <see cref="WorldPreset"/> instances.
    /// </summary>
    public class WorldPresetBuilder
    {
        private string name;
        private SeaLevel? seaLevel;
        private AxialTilt? axialTilt;
        private OverallRainfall? rainfall;
        private OverallTemperature? temperature;
        private OverallPopulation? population;

        public WorldPresetBuilder Name(string value)
        {
            name = value;
            return this;
        }

        public WorldPresetBuilder SeaLevel(SeaLevel value)
        {
            seaLevel = value;
            return this;
        }

        public WorldPresetBuilder AxialTilt(AxialTilt value)
        {
            axialTilt = value;
            return this;
        }

        public WorldPresetBuilder Rainfall(OverallRainfall value)
        {
            rainfall = value;
            return this;
        }

        public WorldPresetBuilder Temperature(OverallTemperature value)
        {
            temperature = value;
            return this;
        }

        public WorldPresetBuilder Population(OverallPopulation value)
        {
            population = value;
            return this;
        }

        public WorldPreset Build()
        {
            if (string.IsNullOrEmpty(name))
                throw new InvalidOperationException("Preset name is required");
            if (!seaLevel.HasValue)
                throw new InvalidOperationException("SeaLevel is required");
            if (!axialTilt.HasValue)
                throw new InvalidOperationException("AxialTilt is required");
            if (!rainfall.HasValue)
                throw new InvalidOperationException("Rainfall is required");
            if (!temperature.HasValue)
                throw new InvalidOperationException("Temperature is required");
            if (!population.HasValue)
                throw new InvalidOperationException("Population is required");

            return new WorldPreset(
                name,
                seaLevel.Value,
                axialTilt.Value,
                rainfall.Value,
                temperature.Value,
                population.Value);
        }
    }

    /// <summary>
    /// Registry of built-in <see cref="WorldPreset"/> definitions.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WorldPresetUtility
    {
        public static IList<WorldPreset> WorldPresets { get; private set; }

        static WorldPresetUtility()
        {
            BuildWorldPresetList();
        }

        private static WorldPreset WorldPreset_Vanilla() => new WorldPresetBuilder()
            .Name("Planets.Vanilla")
            .SeaLevel(SeaLevel.Normal)
            .AxialTilt(AxialTilt.Normal)
            .Rainfall(OverallRainfall.Normal)
            .Temperature(OverallTemperature.Normal)
            .Population(OverallPopulation.Normal)
            .Build();

        private static WorldPreset WorldPreset_Desert() => new WorldPresetBuilder()
            .Name("Planets.Desert")
            .SeaLevel(SeaLevel.SlightlyLow)
            .AxialTilt(AxialTilt.VeryLow)
            .Rainfall(OverallRainfall.LittleBitLess)
            .Temperature(OverallTemperature.LittleBitWarmer)
            .Population(OverallPopulation.LittleBitLess)
            .Build();

        private static WorldPreset WorldPreset_Frozen() => new WorldPresetBuilder()
            .Name("Planets.Frozen")
            .SeaLevel(SeaLevel.Low)
            .AxialTilt(AxialTilt.VeryHigh)
            .Rainfall(OverallRainfall.LittleBitLess)
            .Temperature(OverallTemperature.Cold)
            .Population(OverallPopulation.LittleBitLess)
            .Build();

        private static WorldPreset WorldPreset_Earthlike() => new WorldPresetBuilder()
            .Name("Planets.Earthlike")
            .SeaLevel(SeaLevel.SlightlyHigh)
            .AxialTilt(AxialTilt.Normal)
            .Rainfall(OverallRainfall.LittleBitMore)
            .Temperature(OverallTemperature.Normal)
            .Population(OverallPopulation.Normal)
            .Build();

        private static WorldPreset WorldPreset_Boreal() => new WorldPresetBuilder()
            .Name("Planets.Boreal")
            .SeaLevel(SeaLevel.Normal)
            .AxialTilt(AxialTilt.High)
            .Rainfall(OverallRainfall.LittleBitMore)
            .Temperature(OverallTemperature.LittleBitColder)
            .Population(OverallPopulation.LittleBitMore)
            .Build();

        private static WorldPreset WorldPreset_Iceball() => new WorldPresetBuilder()
            .Name("Planets.Iceball")
            .SeaLevel(SeaLevel.Normal)
            .AxialTilt(AxialTilt.Low)
            .Rainfall(OverallRainfall.Little)
            .Temperature(OverallTemperature.VeryCold)
            .Population(OverallPopulation.Little)
            .Build();

        private static WorldPreset WorldPreset_Jungle() => new WorldPresetBuilder()
            .Name("Planets.Jungle")
            .SeaLevel(SeaLevel.SlightlyHigh)
            .AxialTilt(AxialTilt.VeryLow)
            .Rainfall(OverallRainfall.High)
            .Temperature(OverallTemperature.LittleBitWarmer)
            .Population(OverallPopulation.High)
            .Build();

        private static WorldPreset WorldPreset_Ocean() => new WorldPresetBuilder()
            .Name("Planets.Ocean")
            .SeaLevel(SeaLevel.High)
            .AxialTilt(AxialTilt.Low)
            .Rainfall(OverallRainfall.High)
            .Temperature(OverallTemperature.Normal)
            .Population(OverallPopulation.AlmostNone)
            .Build();

        private static WorldPreset WorldPreset_AlienDiversity() => new WorldPresetBuilder()
            .Name("Planets.AlienDiversity")
            .SeaLevel(SeaLevel.SlightlyHigh)
            .AxialTilt(AxialTilt.High)
            .Rainfall(OverallRainfall.High)
            .Temperature(OverallTemperature.LittleBitWarmer)
            .Population(OverallPopulation.Normal)
            .Build();

        private static WorldPreset WorldPreset_ExoticWetlands() => new WorldPresetBuilder()
            .Name("Planets.ExoticWetlands")
            .SeaLevel(SeaLevel.High)
            .AxialTilt(AxialTilt.VeryLow)
            .Rainfall(OverallRainfall.VeryHigh)
            .Temperature(OverallTemperature.LittleBitWarmer)
            .Population(OverallPopulation.LittleBitLess)
            .Build();

        private static WorldPreset WorldPreset_AlienBadlands() => new WorldPresetBuilder()
            .Name("Planets.AlienBadlands")
            .SeaLevel(SeaLevel.Normal)
            .AxialTilt(AxialTilt.VeryHigh)
            .Rainfall(OverallRainfall.LittleBitLess)
            .Temperature(OverallTemperature.LittleBitColder)
            .Population(OverallPopulation.LittleBitLess)
            .Build();

        private static WorldPreset WorldPreset_Custom() => new WorldPresetBuilder()
            .Name("Planets.Custom")
            .SeaLevel(SeaLevel.Normal)
            .AxialTilt(AxialTilt.Normal)
            .Rainfall(OverallRainfall.Normal)
            .Temperature(OverallTemperature.Normal)
            .Population(OverallPopulation.Normal)
            .Build();

        private static void BuildWorldPresetList()
        {
            var presetList = new List<WorldPreset>
            {
                WorldPreset_Vanilla(),
                WorldPreset_Desert(),
                WorldPreset_Frozen(),
                WorldPreset_Earthlike(),
                WorldPreset_Boreal(),
                WorldPreset_Iceball(),
                WorldPreset_Jungle(),
                WorldPreset_Ocean()
            };

            if (ModsConfig.IsActive("sarg.alphabiomes"))
            {
                presetList.Add(WorldPreset_AlienDiversity());
                presetList.Add(WorldPreset_ExoticWetlands());
                presetList.Add(WorldPreset_AlienBadlands());

                if (Controller.DebugEnabled)
                    RPLog.D("[Core] Added Alpha Biomes presets to world preset list");
            }

            presetList.Add(WorldPreset_Custom());
            WorldPresets = presetList;

            if (Controller.DebugEnabled)
                RPLog.D($"[Core] Built world preset list with {WorldPresets.Count} presets");
        }
    }
}
