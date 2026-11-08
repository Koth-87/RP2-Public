using RimWorld;
using UnityEngine;
using Verse;
using Planets.Core;

namespace Planets.WorldGen.Astronomy
{
    /// <summary>
    /// Spherical solar altitude, azimuth, declination, and year-phase geometry used by diurnal temperature,
    /// plant season patches, and <see cref="API.RealisticPlanetsAPI"/>.
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

        /// <summary>
        /// Exact spherical solar insolation and day-length geometry at the summer solstice, winter solstice,
        /// and equinox for a given absolute latitude (degrees) and planetary axial tilt (degrees).
        /// Uses the sunset hour angle <c>h0 = acos(clamp(-tan(phi) * tan(delta), -1, 1))</c>.
        /// </summary>
        public readonly struct SolsticeInsolation
        {
            /// <summary>Equator equinox daily insolation reference (1 / pi ≈ 0.3183099).</summary>
            public const float EquatorEquinoxReference = 1f / Mathf.PI;

            /// <summary>Normalized daily-mean top-of-atmosphere insolation at the summer solstice.</summary>
            public readonly float SummerDailyQ;
            /// <summary>Normalized daily-mean top-of-atmosphere insolation at the winter solstice.</summary>
            public readonly float WinterDailyQ;
            /// <summary>Normalized daily-mean top-of-atmosphere insolation at the equinox.</summary>
            public readonly float EquinoxDailyQ;
            /// <summary>Summer solstice day length as a fraction of 24 hours (0 to 1).</summary>
            public readonly float SummerDayFraction;
            /// <summary>Winter solstice day length as a fraction of 24 hours (0 to 1).</summary>
            public readonly float WinterDayFraction;
            /// <summary>Sine of solar noon altitude at the summer solstice (clamped to >= 0).</summary>
            public readonly float SummerNoonSin;
            /// <summary>Sine of solar noon altitude at the winter solstice (clamped to >= 0).</summary>
            public readonly float WinterNoonSin;
            /// <summary>Multi-month polar-night depth: 0 at the polar circle (90° − tilt) to 1 at the pole.</summary>
            public readonly float PolarNightDepth;

            public SolsticeInsolation(
                float summerDailyQ,
                float winterDailyQ,
                float equinoxDailyQ,
                float summerDayFraction,
                float winterDayFraction,
                float summerNoonSin,
                float winterNoonSin,
                float polarNightDepth)
            {
                SummerDailyQ = summerDailyQ;
                WinterDailyQ = winterDailyQ;
                EquinoxDailyQ = equinoxDailyQ;
                SummerDayFraction = summerDayFraction;
                WinterDayFraction = winterDayFraction;
                SummerNoonSin = summerNoonSin;
                WinterNoonSin = winterNoonSin;
                PolarNightDepth = polarNightDepth;
            }
        }

        /// <summary>
        /// Computes daily insolation, day-length fraction, noon altitude sine, and polar-night depth at the
        /// summer and winter solstices from spherical astronomy.
        /// </summary>
        public static SolsticeInsolation GetSolsticeInsolation(float absLatDeg, float tiltDeg)
        {
            double phi = Mathf.Clamp(Mathf.Abs(absLatDeg), 0f, 89.999f) * (System.Math.PI / 180.0);
            double delta = tiltDeg * (System.Math.PI / 180.0);
            double sinPhi = System.Math.Sin(phi);
            double cosPhi = System.Math.Cos(phi);
            double tanPhi = System.Math.Tan(phi);

            void Step(double decl, out float q, out float dayFrac, out float noonSin)
            {
                double sinDec = System.Math.Sin(decl);
                double cosDec = System.Math.Cos(decl);
                double x = -tanPhi * System.Math.Tan(decl);
                if (x < -1.0) x = -1.0;
                if (x > 1.0) x = 1.0;
                double h0 = System.Math.Acos(x);
                double rawQ = (1.0 / System.Math.PI) * (h0 * sinPhi * sinDec + cosPhi * cosDec * System.Math.Sin(h0));
                q = (float)System.Math.Max(0.0, rawQ);
                dayFrac = (float)(h0 / System.Math.PI);
                noonSin = (float)System.Math.Max(0.0, sinPhi * sinDec + cosPhi * cosDec);
            }

            Step(delta, out float qSum, out float daySum, out float noonSum);
            Step(-delta, out float qWin, out float dayWin, out float noonWin);
            Step(0.0, out float qEq, out _, out _);

            double sinNoonWinRaw = sinPhi * System.Math.Sin(-delta) + cosPhi * System.Math.Cos(-delta);
            double sinTilt = System.Math.Max(System.Math.Abs(System.Math.Sin(delta)), 1e-6);
            float polarNightDepth = Mathf.Clamp01((float)(-sinNoonWinRaw / sinTilt));

            return new SolsticeInsolation(qSum, qWin, qEq, daySum, dayWin, noonSum, noonWin, polarNightDepth);
        }

        /// <summary>
        /// Diurnal solar altitude span factor in [0, 1] for a given signed latitude and solar declination (both in degrees).
        /// Equals (noonAltitudeDeg - meanAltitudeDeg) / 90°, matching <see cref="Patches.Climate.DiurnalSwing"/>:
        /// 1 at the equator equinox, smaller under midnight sun (sun circles at constant altitude), and 0 during polar night.
        /// </summary>
        public static float DiurnalAltitudeSpanFactor(float latDeg, float declinationDeg)
        {
            float latRad = latDeg * Mathf.Deg2Rad;
            float decRad = declinationDeg * Mathf.Deg2Rad;
            float sinMean = Mathf.Sin(latRad) * Mathf.Sin(decRad);
            float meanAlt = Mathf.Asin(Mathf.Clamp(sinMean, -1f, 1f)) * Mathf.Rad2Deg;
            float sinNoon = sinMean + Mathf.Cos(latRad) * Mathf.Cos(decRad);
            float noonAlt = Mathf.Asin(Mathf.Clamp(sinNoon, -1f, 1f)) * Mathf.Rad2Deg;
            float maxDelta = Mathf.Max(0f, noonAlt - meanAlt);
            if (meanAlt + maxDelta <= 0f || maxDelta < 0.5f)
                return 0f;
            return Mathf.Clamp01(maxDelta / 90f);
        }
    }
}
