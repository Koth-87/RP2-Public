using System;
using Verse;

namespace Planets.WorldGen.Geography
{
    /// <summary>
    /// Sea level setting controlling global ocean share (ordered from least water to most water).
    /// </summary>
    public enum SeaLevel
    {
        Low,
        SlightlyLow,
        Normal,
        SlightlyHigh,
        High
    }

    /// <summary>
    /// UI translation and enumeration helpers for <see cref="SeaLevel"/>.
    /// </summary>
    public static class SeaLevelUtility
    {
        public static int EnumValuesCount => Enum.GetValues(typeof(SeaLevel)).Length;

        public static string GetLabel(SeaLevel level)
        {
            return ("Planets.SeaLevel_" + level).Translate();
        }

        public static string GetDescription(SeaLevel level)
        {
            return ("Planets.SeaLevel_" + level + "_Desc").Translate();
        }
    }
}
