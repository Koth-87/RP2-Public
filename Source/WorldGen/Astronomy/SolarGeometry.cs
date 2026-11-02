using RimWorld;
using UnityEngine;
using Verse;
using Planets.Core;

namespace Planets.WorldGen.Astronomy
{
    /// <summary>
    /// Spherical solar altitude, azimuth, declination, and year-phase geometry used by diurnal temperature,
    /// plant season patches, and <see cref="Api.RealisticPlanetsApi"/>.
    /// </summary>
    public static class SolarGeometry
    {
        private static int TicksPerDay => GenDate.TicksPerDay;
        private static float DaysPerYear => GenDate.DaysPerYear;

        /// <summary>
        /// Computes sun altitude (degrees), azimuth (radians), and whether the sun is rising.
        /// Uses spherical astronomy: <c>sin(alt) = sin(lat)*sin(dec) + cos(lat)*cos(dec)*cos(hourAngle)</c>.
        /// </summary>
        public static void GetSunAltitudeAzimuth(
            float longitudeDeg,
            float latitudeDeg,
            int absTick,
            out float altitudeDeg,
            out float azimuthRad,
            out bool isRising)
        {
            float decRad = GetSolarDeclinationRad(absTick, longitudeDeg);
            float latRad = latitudeDeg * Mathf.Deg2Rad;
            float hourAngleRad = GetLocalHourAngleRad(absTick, longitudeDeg);

            float sinAlt = Mathf.Sin(latRad) * Mathf.Sin(decRad)
                + Mathf.Cos(latRad) * Mathf.Cos(decRad) * Mathf.Cos(hourAngleRad);
            sinAlt = Mathf.Clamp(sinAlt, -1f, 1f);
            altitudeDeg = Mathf.Asin(sinAlt) * Mathf.Rad2Deg;

            azimuthRad = ComputeAzimuthRad(latRad, decRad, hourAngleRad, sinAlt);
            isRising = hourAngleRad < 0f;
        }

        /// <summary>
        /// Solar declination in radians. Positive in northern summer.
        /// Aligned with the RimWorld calendar (year phase 0 = spring equinox, day 15 = northern summer solstice).
        /// </summary>
        public static float GetSolarDeclinationRad(int absTick, float longitudeDeg)
        {
            float tiltDeg = AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt);
            float tiltRad = tiltDeg * Mathf.Deg2Rad;
            return tiltRad * Mathf.Sin(2f * Mathf.PI * YearPhase(absTick));
        }

        /// <summary>
        /// Fractional progress through the year in [0, 1).
        /// 0 is the spring equinox, 0.25 (day 15) is the northern summer solstice, and 0.75 (day 45) is the southern summer solstice.
        /// </summary>
        public static float YearPhase(long absTick)
        {
            long ticksPerYear = (long)(DaysPerYear * TicksPerDay);
            if (ticksPerYear <= 0)
                return 0f;
            long t = absTick % ticksPerYear;
            if (t < 0)
                t += ticksPerYear;
            return t / (float)ticksPerYear;
        }

        private static float GetLocalHourAngleRad(int absTick, float longitudeDeg)
        {
            long localTicks = absTick + GenDate.LocalTicksOffsetFromLongitude(longitudeDeg);
            float dayPhase = (localTicks % TicksPerDay) / (float)TicksPerDay;
            float hourAngle = (dayPhase - 0.5f) * 2f * Mathf.PI;
            hourAngle = Mathf.Repeat(hourAngle + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
            return hourAngle;
        }

        private static float ComputeAzimuthRad(float latRad, float decRad, float hourAngleRad, float sinAlt)
        {
            float y = -Mathf.Sin(hourAngleRad);
            float x = Mathf.Tan(decRad) * Mathf.Cos(latRad) - Mathf.Sin(latRad) * Mathf.Cos(hourAngleRad);
            float az = Mathf.Atan2(y, x);
            if (az < 0f)
                az += 2f * Mathf.PI;
            return az;
        }
    }
}
