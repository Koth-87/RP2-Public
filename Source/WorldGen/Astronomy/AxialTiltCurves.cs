using System;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.WorldGen.Astronomy
{
    /// <summary>
    /// Manages baseline seasonal temperature variation curves driven by planetary axial tilt.
    /// Uses insolation geometry: <c>amplitude = k * sin(latitude) * sin(tilt_angle)</c>.
    /// Evaluates to the full annual temperature range (warmest to coldest) for a given latitude in degrees [0, 90].
    /// </summary>
    public static class AxialTiltCurves
    {
        public static SimpleCurve SeasonalTempVariationCurve;

        private const float TiltDegrees_VeryLow = 0f;
        private const float TiltDegrees_Low = 11.25f;
        private const float TiltDegrees_Normal = 22.5f;
        private const float TiltDegrees_High = 33.75f;
        private const float TiltDegrees_VeryHigh = 45f;

        /// <summary>Calibrated so Normal tilt (22.5°) produces ~28°C baseline annual range at the poles.</summary>
        private const float AmplitudeScalingFactor = 73.2f;

        static AxialTiltCurves()
        {
            SetDefaultCurve();
        }

        public static float GetTiltDegrees(AxialTilt tilt)
        {
            switch (tilt)
            {
                case AxialTilt.VeryLow: return TiltDegrees_VeryLow;
                case AxialTilt.Low: return TiltDegrees_Low;
                case AxialTilt.Normal: return TiltDegrees_Normal;
                case AxialTilt.High: return TiltDegrees_High;
                case AxialTilt.VeryHigh: return TiltDegrees_VeryHigh;
                default: return TiltDegrees_Normal;
            }
        }

        private static float CalculateSeasonalAmplitude(float normalizedLatitude, float tiltDegrees)
        {
            if (tiltDegrees == 0f)
                return 0f;

            float latRadians = normalizedLatitude * (float)(Math.PI / 2.0);
            float tiltRadians = tiltDegrees * (float)(Math.PI / 180.0);
            return AmplitudeScalingFactor * (float)(Math.Sin(latRadians) * Math.Sin(tiltRadians));
        }

        private static SimpleCurve GenerateSeasonalCurve(float tiltDegrees)
        {
            SimpleCurve curve = new SimpleCurve();
            for (float latDeg = 0f; latDeg <= 90f; latDeg += 1f)
            {
                float normalizedLat = latDeg / 90f;
                float amplitude = CalculateSeasonalAmplitude(normalizedLat, tiltDegrees);
                curve.Add(new CurvePoint(latDeg, amplitude), true);
            }
            return curve;
        }

        private static void SetDefaultCurve()
        {
            SeasonalTempVariationCurve = GenerateSeasonalCurve(TiltDegrees_Normal);
        }

        public static void SetSeasonalCurve()
        {
            AxialTilt tilt = Planets_GameComponent.axialTilt;
            float tiltDegrees = GetTiltDegrees(tilt);
            SeasonalTempVariationCurve = GenerateSeasonalCurve(tiltDegrees);

            if (Controller.DebugEnabled)
            {
                RPLog.D($"[WorldGen] Set seasonal curve for axial tilt: {tilt} ({tiltDegrees}°)");
                RPLog.D($"  Equator (0°): {CalculateSeasonalAmplitude(0f, tiltDegrees):F1}°C full range");
                RPLog.D($"  Mid-lat (45°): {CalculateSeasonalAmplitude(0.5f, tiltDegrees):F1}°C full range");
                RPLog.D($"  Pole (90°): {CalculateSeasonalAmplitude(1f, tiltDegrees):F1}°C full range");
            }
        }
    }
}
