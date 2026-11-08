using System;
using UnityEngine;
using Verse;
using Planets.Utilities.Logging;
using Planets.Utilities.WorldGen;
using Planets.WorldGen;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Climate;
using Planets.Core.Configuration;
using Planets.Core;

namespace Planets.WorldGen.Climate.Layers
{
    /// <summary>
    /// Computes full annual seasonal temperature amplitude (<c>SeasonalAmp</c>) from planetary axial tilt,
    /// modulated by ocean thermal damping (and advected continental-air share when the maritime layer is active),
    /// continental interior amplification, and high-elevation damping above 2 km.
    /// Writes <c>MaxSummerTemp = AnnualMeanTemp + SeasonalAmp / 2</c> and <c>MinWinterTemp = AnnualMeanTemp - SeasonalAmp / 2</c>.
    /// </summary>
    public class SeasonalityLayer : IClimateLayer
    {
        private ClimateData climate;
        private int tileCount;
        private ClimateConfiguration settings;

        /// <summary>
        /// Layer name for logging and debugging
        /// </summary>
        public string LayerName => "Seasonality";

        public SeasonalityLayer(ClimateData climateData, ClimateConfiguration climateConfig)
        {
            climate = climateData;
            settings = climateConfig ?? new ClimateConfiguration();
            tileCount = climate.Latitude.Length;
        }

        /// <summary>
        /// Execute all seasonality computation steps in sequence (IClimateLayer interface)
        /// </summary>
        public void Execute(ClimateData climateData)
        {
            ComputeSeasonality();
        }

        /// <summary>
        /// Internal method: Execute all seasonality computation steps in sequence
        /// </summary>
        private void ComputeSeasonality()
        {
            // Compute seasonal amplitude with ocean/land/elevation effects
            ComputeSeasonalAmplitude();

            // Calculate seasonal extremes (summer/winter temperatures)
            ComputeSeasonalExtremes();

            if (Planets.Core.Controller.DebugEnabled)
            {
                RPLog.D($"[Seasonality] Computed seasonal variation for {tileCount} tiles");
            }
        }

        // ========== SEASONAL AMPLITUDE ==========

        /// <summary>
        /// Compute final seasonal amplitude
        /// Sets: SeasonalAmp
        /// 
        /// Algorithm:
        /// 1. Get base amplitude from Planets_TemperatureTuning.SeasonalTempVariationCurve
        ///    This is physics-based from planet's axial tilt (obliquity)
        /// 2. Apply ocean damping: ~65-80% of base depending on latitude
        /// 3. Apply land amplification: varies from 1.30x to 2.06x by latitude
        /// 4. Apply elevation damping: reduces amplitude at high elevations
        /// 
        /// The base curve from TemperatureTuning represents ideal solar forcing
        /// based on axial tilt. We then apply regional modifiers.
        /// </summary>
        private void ComputeSeasonalAmplitude()
        {
            float tiltDeg = AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt);
            float qEq0 = SolarGeometry.SolsticeInsolation.EquatorEquinoxReference;

            for (int i = 0; i < tileCount; i++)
            {
                float latDegSigned = climate.LatitudeSigned[i] * Mathf.Rad2Deg;
                float absLat = Mathf.Abs(latDegSigned);

                // Add planetary wave perturbation for consistent climate boundaries
                float lonRad = climate.Longitude[i];
                float latRadSigned = climate.LatitudeSigned[i];

                float perturbedLatDegSigned = climate.ClimateLatitudeDeg(latDegSigned, lonRad, latRadSigned);
                float perturbedLatDeg = Mathf.Abs(perturbedLatDegSigned);

                // Step 1: Get base amplitude from axial tilt curve
                // This is the fundamental seasonal amplitude driven by planetary obliquity
                float baseAmp = AxialTiltCurves.SeasonalTempVariationCurve.Evaluate(perturbedLatDeg);

                float oceanBaseAmp = ComputeOceanAmplitude(baseAmp, perturbedLatDeg);
                float landFullAmp = ComputeLandAmplitude(baseAmp, perturbedLatDeg, i);
                float amplitude;

                // Step 2: Apply ocean vs land modifier
                if (climate.OceanMask[i])
                {
                    // ===== OCEAN SEASONALITY =====
                    amplitude = oceanBaseAmp;
                    if (climate.ContinentalAir != null)
                        amplitude *= settings.oceanSwingOpenShare.CurrentValue + settings.oceanSwingContinentalGain.CurrentValue * climate.ContinentalAir[i];
                }
                else
                {
                    // ===== LAND SEASONALITY =====
                    amplitude = landFullAmp;
                }

                // Step 3: Apply elevation damping
                float elevKm = Mathf.Max(0f, climate.ElevationKm[i]);
                float dampingFactor = elevKm > 2.0f ? ComputeElevationDamping(elevKm) : 1.0f;
                amplitude *= dampingFactor;

                // Step 4: Spherical solar geometry — polar night radiative loss, 24h midnight-sun tundra absorption,
                // and permanent-ice summer co-albedo reflection (replaces hardcoded 70° polarColdExtra).
                var sol = SolarGeometry.GetSolsticeInsolation(absLat, tiltDeg);
                float polarNight = sol.PolarNightDepth;
                float midnightSun = Mathf.Clamp01((sol.SummerDayFraction - 0.75f) / 0.25f);
                float polarCold = settings.polarColdExtra.CurrentValue; // default -6.0 C

                float winterRadLand = polarCold * polarNight;
                float winterRadOcean = (polarCold * 0.3f) * polarNight;

                if (climate.OceanMask[i])
                {
                    climate.AnnualMeanTemp[i] += winterRadOcean;
                }
                else
                {
                    float thawedSummer = climate.AnnualMeanTemp[i] + 0.5f * amplitude;
                    float snowThawHeadroom = Mathf.Clamp01((16.0f - thawedSummer) / 14.0f);
                    float qRatio = Mathf.Min(sol.SummerDailyQ / qEq0, 1.25f);
                    float lowTundra = Mathf.Clamp01(1.0f - elevKm / 0.9f) * midnightSun * snowThawHeadroom;
                    float tundraSummerWarm = 12.0f * lowTundra * qRatio;

                    float permIceSheet = Mathf.Clamp01((-8.0f - thawedSummer) / 10.0f) * Mathf.Clamp01((elevKm - 1.2f) / 1.0f);
                    float contExcess = Mathf.Max(0f, (landFullAmp - oceanBaseAmp) * dampingFactor);

                    float dWin = winterRadLand;
                    float dSum = winterRadLand + tundraSummerWarm - permIceSheet * (0.95f * contExcess) * qRatio;
                    climate.AnnualMeanTemp[i] += 0.5f * (dSum + dWin);
                    amplitude = Mathf.Max(0f, amplitude + (dSum - dWin));
                }

                // Step 5: Baroclinic mid-latitude continental summer moderation & long-night winter basin inversion
                if (!climate.OceanMask[i] && climate.MaritimeAnnual != null)
                {
                    float cont = Mathf.Clamp01(climate.Continentality[i]);
                    float contGate = Mathf.Clamp01((cont - 0.50f) / 0.35f);
                    float midBaroclinic = Mathf.Clamp01((sol.SummerDayFraction - 0.60f) / 0.12f)
                        * Mathf.Clamp01((0.90f - sol.SummerDayFraction) / 0.12f);
                    float dSum = -4.5f * contGate * midBaroclinic;
                    climate.AnnualMeanTemp[i] += 0.5f * dSum;
                    amplitude = Mathf.Max(0f, amplitude + dSum);

                    float coldWin = climate.AnnualMeanTemp[i] - 0.5f * amplitude;
                    float longNight = Mathf.Clamp01((0.42f - sol.WinterDayFraction) / 0.18f);
                    float invGate = elevKm < 1.2f
                        ? longNight * Mathf.Clamp01((-20.0f - coldWin) / 12.0f) * Mathf.Clamp01((cont - 0.55f) / 0.30f)
                        : 0f;
                    float dWinInv = -5.5f * invGate;
                    climate.AnnualMeanTemp[i] += 0.5f * dWinInv;
                    amplitude = Mathf.Max(0f, amplitude - dWinInv);
                }

                climate.SeasonalAmp[i] = amplitude;
            }
        }

        /// <summary>
        /// Compute ocean amplitude with latitude-dependent damping
        /// Ocean thermal inertia reduces amplitude, but effect varies with latitude
        /// </summary>
        private float ComputeOceanAmplitude(float baseAmp, float latDeg)
        {
            float solarBoostFactor;

            if (latDeg < 45f)
            {
                // Low-mid latitudes: Thermal inertia dominates
                // Ocean amplitude only 67-83% of baseline
                // At equator: mostly damped by constant warm water
                // At 45°: more solar forcing overcomes damping
                solarBoostFactor = 0.67f + (latDeg / 45f) * 0.16f;
            }
            else if (latDeg < 65f)
            {
                // Mid-high latitudes: Peak solar forcing effect
                // Ocean amplitude 80-83% of baseline
                // Solar forcing begins to overcome thermal inertia
                float t = (latDeg - 45f) / 20f;
                solarBoostFactor = Mathf.Lerp(0.83f, 0.80f, t);
            }
            else if (latDeg < 75f)
            {
                // High subpolar: Sea ice begins forming
                // Rapid decrease as sea ice provides insulation
                // Ocean amplitude drops to 47% at 75°
                float t = (latDeg - 65f) / 10f;
                solarBoostFactor = Mathf.Lerp(0.80f, 0.47f, t);
            }
            else
            {
                // Extreme polar: Permanent or semi-permanent sea ice
                // Sea ice insulates water below, drastically reduces amplitude
                // At 75°: 47%, at 85°: 21%, at 90°: 15%
                float polarWeight = (latDeg - 75f) / 15f; // 0 at 75°, 1 at 90°
                solarBoostFactor = Mathf.Lerp(0.47f, 0.15f, polarWeight);
            }

            return baseAmp * solarBoostFactor;
        }

        /// <summary>
        /// Compute land amplitude with continental amplification
        /// Interior continents amplify seasonal swings
        /// </summary>
        private float ComputeLandAmplitude(float baseAmp, float latDeg, int tileID)
        {
            // Get latitude-specific maximum continental multiplier
            // This represents how much interior continents amplify seasonality
            // 1.30x at the equator, peak 2.06x at 60°, 1.46x at the pole
            float maxContMult = InterpolateContinentalMultiplier(latDeg);

            // With the maritime layer, continentality is the share of air that did not come off the ocean.
            // Fully maritime land swings like the latitude curve's ocean swing; fully continental land uses maxContMult.
            // Land under maritime air still has lower thermal inertia than open water, so this endpoint uses the un-damped
            // latitudinal ocean curve rather than the ContinentalAir-damped open-sea swing.
            if (climate.MaritimeAnnual != null)
                return Mathf.Lerp(ComputeOceanAmplitude(baseAmp, latDeg), baseAmp * maxContMult, climate.Continentality[tileID]);

            // Scale amplification by continentality
            // Coast (continentality=0.0): 1.0x baseline
            // Deep continental (continentality=1.0): maxContMult
            float continentalMult = 1f + (maxContMult - 1f) * climate.Continentality[tileID];

            return baseAmp * continentalMult;
        }

        /// <summary>
        /// Smooth baroclinic/radiative land-slab admittance envelope: 1.30x at the equator, peaking near 60°
        /// where synoptic baroclinic exchange and snow transition maximize continental seasonality, and tapering
        /// toward the pole as winter polar night caps longwave loss.
        /// </summary>
        private float InterpolateContinentalMultiplier(float latDeg)
        {
            float rad = Mathf.Clamp(Mathf.Abs(latDeg), 0f, 90f) * Mathf.Deg2Rad;
            float sinLat = Mathf.Sin(rad);
            float cosLat = Mathf.Max(Mathf.Cos(rad), 0.04f);
            return 1.30f + 1.15f * Mathf.Pow(sinLat, 1.25f) * Mathf.Pow(cosLat, 0.45f);
        }

        /// <summary>
        /// Compute elevation damping factor for seasonal amplitude
        /// High mountains have reduced seasonal variation
        /// </summary>
        private float ComputeElevationDamping(float elevKm)
        {
            // Damping profile:
            // At 2km: 100% amplitude retained
            // At 4km: 70% amplitude retained
            // At 6km: 40% amplitude retained
            // At 8km+: 30% amplitude retained (minimum)
            
            float dampingFactor = 1.0f - ((elevKm - 2.0f) * 0.15f);
            
            // Clamp to reasonable range
            dampingFactor = Mathf.Clamp(dampingFactor, 0.3f, 1.0f);
            
            return dampingFactor;
        }

        // ========== SEASONAL EXTREMES ==========

        /// <summary>
        /// Calculate summer maximum and winter minimum temperatures
        /// Sets: MaxSummerTemp, MinWinterTemp
        /// 
        /// SeasonalAmp represents the FULL ANNUAL RANGE (warmest month to coldest month).
        /// To get extremes, we use half-range (deviation from mean):
        /// - MaxSummerTemp = AnnualMeanTemp + (SeasonalAmp / 2)
        /// - MinWinterTemp = AnnualMeanTemp - (SeasonalAmp / 2)
        /// 
        /// These values drive biome selection because they determine:
        /// - Sea ice formation (water freezes)
        /// - Snow melt patterns
        /// - Growing season length
        /// - Whether permafrost can form
        /// 
        /// Example at 70°N:
        /// - AnnualMeanTemp: -10°C
        /// - SeasonalAmp: 60°C (full annual range)
        /// - MaxSummerTemp: +20°C (allows plant growth)
        /// - MinWinterTemp: -40°C (extreme cold stress)
        /// </summary>
        private void ComputeSeasonalExtremes()
        {
            for (int i = 0; i < tileCount; i++)
            {
                // Calculate extremes from mean and amplitude
                // SeasonalAmp represents the FULL range (Winter Min to Summer Max)
                // So we add/subtract half the amplitude from the mean
                float halfAmp = climate.SeasonalAmp[i] * 0.5f;
                climate.MaxSummerTemp[i] = climate.AnnualMeanTemp[i] + halfAmp;
                climate.MinWinterTemp[i] = climate.AnnualMeanTemp[i] - halfAmp;

                // Debug logging for polar regions (sea ice boundaries)
                if (Planets.Core.Controller.DebugEnabled && i % 5000 == 0)
                {
                    float latDeg = Mathf.Abs(climate.Latitude[i] * Mathf.Rad2Deg);
                    if (latDeg > 70f)
                    {
                        RPLog.D($"[Seasonality - Polar] Tile {i} ({latDeg:F1}°): " +
                            $"Annual={climate.AnnualMeanTemp[i]:F1}°C, " +
                            $"Summer={climate.MaxSummerTemp[i]:F1}°C, " +
                            $"Winter={climate.MinWinterTemp[i]:F1}°C, " +
                            $"Amplitude={climate.SeasonalAmp[i]:F1}°C");
                    }
                }
            }
        }
    }
}
