using UnityEngine;

namespace Planets.WorldGen.Astronomy
{
    /// <summary>
    /// Annual mean temperature offset from planetary axial tilt (obliquity).
    /// Annual-mean insolation follows the Legendre series in Nadeau and McGehee (2017).
    /// Offset is zero at Normal tilt (22.5°); higher tilt warms the poles and cools the equator.
    /// </summary>
    public static class AxialAnnualInsolation
    {
        private const float DegreesPerInsolation = 70f;

        public static float TemperatureOffset(float latitudeDeg, float tiltDeg)
        {
            float sinLat = Mathf.Sin(latitudeDeg * Mathf.Deg2Rad);
            float reference = AnnualMeanInsolation(sinLat, Mathf.Cos(AxialTiltCurves.GetTiltDegrees(AxialTilt.Normal) * Mathf.Deg2Rad));
            float actual = AnnualMeanInsolation(sinLat, Mathf.Cos(tiltDeg * Mathf.Deg2Rad));
            return (actual - reference) * DegreesPerInsolation;
        }

        private static float AnnualMeanInsolation(float sinLat, float cosObliquity)
        {
            return 1f
                - (5f / 8f) * Legendre2(cosObliquity) * Legendre2(sinLat)
                - (9f / 64f) * Legendre4(cosObliquity) * Legendre4(sinLat)
                - (65f / 1024f) * Legendre6(cosObliquity) * Legendre6(sinLat);
        }

        private static float Legendre2(float y)
        {
            return (3f * y * y - 1f) * 0.5f;
        }

        private static float Legendre4(float y)
        {
            float y2 = y * y;
            return (35f * y2 * y2 - 30f * y2 + 3f) / 8f;
        }

        private static float Legendre6(float y)
        {
            float y2 = y * y;
            float y4 = y2 * y2;
            return (231f * y4 * y2 - 315f * y4 + 105f * y2 - 5f) / 16f;
        }
    }
}
