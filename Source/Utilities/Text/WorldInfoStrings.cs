using System;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.Core;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Geography;

namespace Planets.Utilities.Text
{
    /// <summary>
    /// Translation key helpers for world generation parameter display.
    /// Used by world inspector UI to format planet information.
    /// </summary>
    public static class WorldInfoStrings
    {
        /// <summary>
        /// Get translation key for current sea level setting.
        /// </summary>
        public static string GetSeaLevel()
        {
            return SeaLevelUtility.GetLabel(Planets_GameComponent.ActiveSeaLevel);
        }

        /// <summary>
        /// Get translation key for overall rainfall setting.
        /// </summary>
        public static string GetRainfall()
        {
            switch (Find.World.info.overallRainfall)
            {
                case OverallRainfall.AlmostNone:
                    return "Planets.Rainfall_VeryLow";
                case OverallRainfall.Little:
                    return "Planets.Rainfall_Low";
                case OverallRainfall.LittleBitLess:
                    return "Planets.Rainfall_SlightlyLow";
                case OverallRainfall.Normal:
                    return "Planets.Rainfall_Normal";
                case OverallRainfall.LittleBitMore:
                    return "Planets.Rainfall_SlightlyHigh";
                case OverallRainfall.High:
                    return "Planets.Rainfall_High";
                case OverallRainfall.VeryHigh:
                    return "Planets.Rainfall_VeryHigh";
                default:
                    throw new ArgumentOutOfRangeException(nameof(Find.World.info.overallRainfall));
            }
        }

        /// <summary>
        /// Get translation key for overall temperature setting.
        /// </summary>
        public static string GetTemperature()
        {
            switch (Find.World.info.overallTemperature)
            {
                case OverallTemperature.VeryCold:
                    return "Planets.Temperature_VeryLow";
                case OverallTemperature.Cold:
                    return "Planets.Temperature_Low";
                case OverallTemperature.LittleBitColder:
                    return "Planets.Temperature_SlightlyLow";
                case OverallTemperature.Normal:
                    return "Planets.Temperature_Normal";
                case OverallTemperature.LittleBitWarmer:
                    return "Planets.Temperature_SlightlyHigh";
                case OverallTemperature.Hot:
                    return "Planets.Temperature_High";
                case OverallTemperature.VeryHot:
                    return "Planets.Temperature_VeryHigh";
                default:
                    throw new ArgumentOutOfRangeException(nameof(Find.World.info.overallTemperature));
            }
        }

        /// <summary>
        /// Get translation key for axial tilt setting.
        /// </summary>
        public static string GetAxialTilt()
        {
            switch (Planets_GameComponent.ActiveAxialTilt)
            {
                case AxialTilt.VeryLow:
                    return "Planets.AxialTilt_VeryLow";
                case AxialTilt.Low:
                    return "Planets.AxialTilt_Low";
                case AxialTilt.Normal:
                    return "Planets.AxialTilt_Normal";
                case AxialTilt.High:
                    return "Planets.AxialTilt_High";
                case AxialTilt.VeryHigh:
                    return "Planets.AxialTilt_VeryHigh";
                default:
                    throw new ArgumentOutOfRangeException(nameof(Planets_GameComponent.axialTilt));
            }
        }

        /// <summary>
        /// Get all axial tilt translation strings for UI dropdown.
        /// </summary>
        public static string[] GetAxialTiltStrings()
        {
            return new string[] 
            {
                "Planets.AxialTilt_VeryLow".Translate(),
                "Planets.AxialTilt_Low".Translate(), 
                "Planets.AxialTilt_Normal".Translate(),
                "Planets.AxialTilt_High".Translate(),
                "Planets.AxialTilt_VeryHigh".Translate()
            };
        }

        /// <summary>
        /// Get translation key for overall population setting.
        /// </summary>
        public static string GetPopulation()
        {
            switch (Find.World.info.overallPopulation)
            {
                case OverallPopulation.AlmostNone:
                    return "Planets.Population_VeryLow";
                case OverallPopulation.Little:
                    return "Planets.Population_Low";
                case OverallPopulation.LittleBitLess:
                    return "Planets.Population_SlightlyLow";
                case OverallPopulation.Normal:
                    return "Planets.Population_Normal";
                case OverallPopulation.LittleBitMore:
                    return "Planets.Population_SlightlyHigh";
                case OverallPopulation.High:
                    return "Planets.Population_High";
                case OverallPopulation.VeryHigh:
                    return "Planets.Population_VeryHigh";
                default:
                    throw new ArgumentOutOfRangeException(nameof(Find.World.info.overallPopulation));
            }
        }

        /// <summary>
        /// StringBuilder extension for formatted key-value pair appending with translation.
        /// Used for world info display formatting.
        /// </summary>
        /// <param name="stringBuilder">StringBuilder to append to</param>
        /// <param name="key">Translation key for the label</param>
        /// <param name="value">Translation key or value string</param>
        /// <param name="translate">Whether to translate both key and value</param>
        public static void Append(this StringBuilder stringBuilder, string key, string value, bool translate = true)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (translate)
            {
                key = key.Translate();
                value = value.Translate();
            }
            
            stringBuilder.Append(key);
            stringBuilder.Append(": ");
            stringBuilder.Append(value);
            stringBuilder.Append("\n");
        }
    }
}
