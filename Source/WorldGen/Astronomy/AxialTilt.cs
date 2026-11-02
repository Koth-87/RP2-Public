using System;

namespace Planets.WorldGen.Astronomy
{
    /// <summary>
    /// Planetary axial tilt (obliquity) setting affecting seasonal temperature variation and annual insolation.
    /// </summary>
    public enum AxialTilt
    {
        VeryLow,
        Low,
        Normal,
        High,
        VeryHigh
    }

    /// <summary>
    /// Utility helpers for <see cref="AxialTilt"/>.
    /// </summary>
    public static class AxialTiltUtility
    {
        private static int cachedEnumValuesCount = -1;

        public static int EnumValuesCount
        {
            get
            {
                if (cachedEnumValuesCount < 0)
                    cachedEnumValuesCount = Enum.GetNames(typeof(AxialTilt)).Length;
                return cachedEnumValuesCount;
            }
        }
    }
}
