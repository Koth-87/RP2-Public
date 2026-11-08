using System;
using UnityEngine;
using Planets.WorldGen.Astronomy;

namespace Planets.WorldGen.Climate.Moisture
{
    /// <summary>
    /// Shared Hargreaves-Samani potential evapotranspiration (PET) with FAO-56 solar-declination
    /// extraterrestrial radiation (WS30 step 4b). Used by <see cref="MoistureBudgetLayer"/> during
    /// the Budyko land-recycling loop and by <see cref="Layers.AridityLayer"/> for the final aridity index.
    /// </summary>
    public static class PotentialEvaporation
    {
        private static readonly float[] MonthDays = { 31f, 28f, 31f, 30f, 31f, 30f, 31f, 31f, 30f, 31f, 30f, 31f };

        /// <summary>
        /// FAO-56 solar declination in degrees at the middle of a calendar month (0 = Jan, 6 = Jul),
        /// scaled by the world's axial tilt relative to Earth's 22.5°.
        /// </summary>
        public static float MonthDeclinationDeg(int monthIndex, float tiltDeg)
        {
            float day = 0f;
            for (int m = 0; m < monthIndex; m++)
                day += MonthDays[m];
            day += MonthDays[monthIndex] * 0.5f;
            double earthDeclRad = 0.409 * Math.Sin(2.0 * Math.PI * day / 365.0 - 1.39);
            float scale = tiltDeg / 22.5f;
            return (float)(earthDeclRad * (180.0 / Math.PI)) * scale;
        }

        /// <summary>
        /// FAO-56 daily extraterrestrial radiation R_a, expressed as equivalent millimetres of evaporation per day.
        /// </summary>
        public static float ExtraterrestrialMmPerDay(float latDeg, float declinationDeg)
        {
            double phi = latDeg * (Math.PI / 180.0);
            double delta = declinationDeg * (Math.PI / 180.0);
            double arg = -Math.Tan(phi) * Math.Tan(delta);
            if (arg < -1.0) arg = -1.0;
            if (arg > 1.0) arg = 1.0;
            double sunset = Math.Acos(arg);
            double mj = (24.0 * 60.0 / Math.PI) * 0.0820 * (
                sunset * Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Sin(sunset));
            return (float)(Math.Max(0.0, mj) * 0.408);
        }

        /// <summary>
        /// Computes per-snapshot and annual PET (mm/year) and estimated daily temperature range (°C)
        /// across the four thermal snapshots (July, January, April, October, weighted 0.25 each).
        /// </summary>
        public static void ComputeFourSnapshots(
            ClimateData climate,
            float[] aridity,
            float tiltDeg,
            bool useContStress,
            bool usePolarFactor,
            float[] annualPet,
            float[] dtrOut,
            float[] petJul,
            float[] petJan,
            float[] petApr,
            float[] petOct)
        {
            int n = climate.Latitude.Length;
            float declJul = MonthDeclinationDeg(6, tiltDeg);
            float declJan = MonthDeclinationDeg(0, tiltDeg);
            float declApr = MonthDeclinationDeg(3, tiltDeg);
            float declOct = MonthDeclinationDeg(9, tiltDeg);

            for (int i = 0; i < n; i++)
            {
                float latDeg = climate.LatitudeSigned[i] * Mathf.Rad2Deg;
                float absLat = Mathf.Abs(latDeg);
                float cont = Mathf.Clamp01(climate.Continentality[i]);
                float ai = aridity != null && i < aridity.Length ? aridity[i] : 1f;
                float dtr = DailyRange.Estimate(cont, ai);
                if (dtrOut != null && i < dtrOut.Length)
                    dtrOut[i] = dtr;

                float sqrtDtrJul = Mathf.Sqrt(DailyRange.EstimateForSnapshot(dtr, latDeg, declJul));
                float sqrtDtrJan = Mathf.Sqrt(DailyRange.EstimateForSnapshot(dtr, latDeg, declJan));
                float sqrtDtrApr = Mathf.Sqrt(DailyRange.EstimateForSnapshot(dtr, latDeg, declApr));
                float sqrtDtrOct = Mathf.Sqrt(DailyRange.EstimateForSnapshot(dtr, latDeg, declOct));
                float tAnn = climate.AnnualMeanTemp[i];
                float halfAmp = 0.5f * climate.SeasonalAmp[i];
                float sign = latDeg >= 0f ? 1f : -1f;
                float tJul = tAnn + sign * halfAmp;
                float tJan = tAnn - sign * halfAmp;
                float tEq = tAnn;

                float raJul = ExtraterrestrialMmPerDay(latDeg, declJul);
                float raJan = ExtraterrestrialMmPerDay(latDeg, declJan);
                float raApr = ExtraterrestrialMmPerDay(latDeg, declApr);
                float raOct = ExtraterrestrialMmPerDay(latDeg, declOct);

                float pJul = 0.0023f * raJul * Mathf.Max(tJul + 17.8f, 0.1f) * sqrtDtrJul * 365f;
                float pJan = 0.0023f * raJan * Mathf.Max(tJan + 17.8f, 0.1f) * sqrtDtrJan * 365f;
                float pApr = 0.0023f * raApr * Mathf.Max(tEq + 17.8f, 0.1f) * sqrtDtrApr * 365f;
                float pOct = 0.0023f * raOct * Mathf.Max(tEq + 17.8f, 0.1f) * sqrtDtrOct * 365f;

                float factor = useContStress ? (1f + 0.15f * cont) : 1f;
                if (usePolarFactor && tiltDeg > 0.1f)
                {
                    var sol = SolarGeometry.GetSolsticeInsolation(absLat, tiltDeg);
                    float midnightSun = Mathf.Clamp01((sol.SummerDayFraction - 0.67f) / 0.33f);
                    factor *= 1f + 0.40f * midnightSun;
                }

                pJul *= factor;
                pJan *= factor;
                pApr *= factor;
                pOct *= factor;

                if (petJul != null) petJul[i] = pJul;
                if (petJan != null) petJan[i] = pJan;
                if (petApr != null) petApr[i] = pApr;
                if (petOct != null) petOct[i] = pOct;
                if (annualPet != null)
                    annualPet[i] = 0.25f * (pJul + pJan + pApr + pOct);
            }
        }
    }
}
