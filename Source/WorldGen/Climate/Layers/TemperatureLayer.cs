using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Planets.Utilities.Logging;
using Planets.Utilities.WorldGen;
using Planets.WorldGen;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Climate;
using Planets.WorldGen.Constants;
using Planets.Core;
using Planets.Core.Configuration;

namespace Planets.WorldGen.Climate.Layers
{
    /// <summary>
    /// Computes latitudinal sea-level temperatures (with ocean/land split and polar cooling),
    /// western/eastern boundary currents and mid-latitude storm-track SST anomalies, coastal moderation,
    /// and final annual mean temperatures (blending sea-level temperature toward upwind or nearest SST first,
    /// then applying the 4.5°C/km land surface lapse rate, snow albedo cooling above 3 km, inland penalty,
    /// and axial-tilt annual insolation offset).
    /// </summary>
    public class TemperatureLayer : IClimateLayer
    {
        private ClimateData climate;
        private int tileCount;
        private ClimateConfiguration settings;
        private int[] cachedWestHops;
        private int[] cachedEastHops;
        private int cachedMaxCoastHops;

        /// <summary>
        /// Layer name for logging and debugging
        /// </summary>
        public string LayerName => "Temperature";

        public TemperatureLayer(ClimateData climateData, ClimateConfiguration climateConfig)
        {
            climate = climateData;
            settings = climateConfig ?? new ClimateConfiguration();
            tileCount = climate.Latitude.Length;
        }

        /// <summary>
        /// Execute all temperature computation steps in sequence (IClimateLayer interface)
        /// </summary>
        public void Execute(ClimateData climateData)
        {
            ComputeTemperature();
        }

        /// <summary>
        /// Internal method: Execute all temperature computation steps in sequence
        /// </summary>
        private void ComputeTemperature()
        {
            bool useParallel = Controller.Settings.perfParallelTemperature.CurrentValue;

            ComputeLatitudinalTemperature(useParallel);
            ComputeOceanCurrents(useParallel);
            ComputeCoastalModeration(useParallel);
            ComputeFinalTemperature(useParallel);

            if (Planets.Core.Controller.DebugEnabled)
            {
                RPLog.D($"[Temperature] Computed annual mean temperature for {tileCount} tiles");
            }
        }

        // NOTE: Configuration parameters are read directly from 'settings' throughout the layer.
        // We do NOT store them in ClimateData, which is only for computed results.

        // ========== LATITUDINAL SEA-LEVEL TEMPERATURE ==========

        /// <summary>
        /// Compute base sea-level temperature from latitude alone
        /// </summary>
        private void ComputeLatitudinalTemperature(bool parallel)
        {
            if (parallel)
            {
                Parallel.For(0, tileCount, i => ComputeLatitudinalTemperatureForTile(i));
            }
            else
            {
                for (int i = 0; i < tileCount; i++) ComputeLatitudinalTemperatureForTile(i);
            }
        }

        private void ComputeLatitudinalTemperatureForTile(int i)
        {
            float lat = climate.Latitude[i];
            float latDegSigned = climate.LatitudeSigned[i] * Mathf.Rad2Deg;
            
            // Add planetary wave perturbation (Rossby waves) to break up perfectly linear isotherms
            // Using the same waves as WindLayer for physical consistency
            float lonRad = climate.Longitude[i];
            float latRadSigned = climate.LatitudeSigned[i];
            
            float perturbedLatDegSigned = climate.ClimateLatitudeDeg(latDegSigned, lonRad, latRadSigned);
            float perturbedLatDeg = Mathf.Abs(perturbedLatDegSigned);
            float perturbedLatRad = perturbedLatDeg * Mathf.Deg2Rad;
            
            // We still use the original unperturbed latDeg for the polar extreme cooling cutoff
            float latDeg = Mathf.Abs(latDegSigned);
            
            // Standard cosine curve for interpolation between poles and equator
            float cosLat = Mathf.Max(0f, Mathf.Cos(perturbedLatRad));
            float c = Mathf.Pow(cosLat, settings.latTempExponent.CurrentValue);

            if (climate.OceanMask[i])
            {
                // ===== OCEAN TEMPERATURE =====
                // Ocean has high thermal inertia - currents transport heat poleward
                float oceanExponent = settings.latTempExponent.CurrentValue * 0.91f;
                float cOcean = Mathf.Pow(cosLat, oceanExponent);
                float oceanEquator = climate.EquatorMean + ClimateReferenceData.OceanEquatorOffset;
                float oceanPole = climate.PoleMean + ClimateReferenceData.OceanPoleOffset;
                float oceanTemp = Mathf.Lerp(oceanPole, oceanEquator, cOcean);

                climate.LatitudinalSeaLevelTemp[i] = oceanTemp;
            }
            else
            {
                // ===== LAND TEMPERATURE =====
                // Land cools more aggressively due to:
                // - Lower heat capacity
                // - No ocean circulation moderating effect
                float landTemp = Mathf.Lerp(climate.PoleMean, climate.EquatorMean, c);

                climate.LatitudinalSeaLevelTemp[i] = landTemp;
            }
        }

        // ========== OCEAN CURRENT ANOMALIES ==========

        /// <summary>
        /// Calculate ocean current temperature anomalies
        /// Sets: CurrentsAnomalySST, SST
        /// 
        /// Simplified approach based on latitude and basin edge detection:
        /// - Western boundary currents (warm): Gulf Stream, Kuroshio (~25° latitude)
        /// - Eastern boundary currents (cool): Canary, Benguela (~25° latitude)
        /// - Storm track warming: ~50° latitude (atmospheric interaction)
        /// 
        /// Each current has latitude-dependent weighting using Gaussian curves
        /// Detection of basin edges uses directional land checking
        /// </summary>
        private void ComputeOceanCurrents(bool parallel)
        {
            EnsureCoastHops(parallel);

            bool haveWind = climate.SummerWind != null && climate.SummerWind.Length >= tileCount
                && climate.WinterWind != null && climate.WinterWind.Length >= tileCount
                && climate.EquinoxWind != null && climate.EquinoxWind.Length >= tileCount;

            if (haveWind)
            {
                ComputeWindDrivenCurrents(parallel);
                return;
            }

            int legacyMaxCoast = Mathf.Clamp(Mathf.RoundToInt(225f / EarthScaleConstants.KM_PER_TILE), 1, 14);
            if (parallel)
            {
                Parallel.For(0, tileCount, i => ComputeInitialOceanCurrentsForTile(i, legacyMaxCoast));
            }
            else
            {
                for (int i = 0; i < tileCount; i++)
                    ComputeInitialOceanCurrentsForTile(i, legacyMaxCoast);
            }
        }

        /// <summary>
        /// Recompute sea-surface current anomalies from the circulation wind at the start of a coupling round,
        /// before the maritime layer carries UpwindSST onto the continents.
        /// </summary>
        public void RefreshCurrentsFromWind()
        {
            ComputeOceanCurrents(Controller.Settings.perfParallelTemperature.CurrentValue);
        }

        private void EnsureCoastHops(bool parallel)
        {
            if (cachedWestHops != null && cachedEastHops != null)
                return;

            float kmPerTile = Mathf.Max(1f, EarthScaleConstants.KM_PER_TILE);
            cachedMaxCoastHops = Mathf.Clamp(Mathf.RoundToInt(500f / kmPerTile), 1, 20);
            int maxSearchHops = Mathf.Max(cachedMaxCoastHops + 6, 16);
            cachedWestHops = new int[tileCount];
            cachedEastHops = new int[tileCount];

            if (parallel)
            {
                Parallel.For(0, tileCount,
                    () => new
                    {
                        Visited = new HashSet<int>(),
                        Queue = new Queue<(int, int)>(),
                        Neighbors = new List<PlanetTile>()
                    },
                    (i, loopState, localState) =>
                    {
                        if (!climate.OceanMask[i] || !IsOpenOcean(i))
                        {
                            cachedWestHops[i] = -1;
                            cachedEastHops[i] = -1;
                        }
                        else
                        {
                            cachedWestHops[i] = HopsToLand(i, -1f, maxSearchHops, localState.Visited, localState.Queue, localState.Neighbors);
                            cachedEastHops[i] = HopsToLand(i, 1f, maxSearchHops, localState.Visited, localState.Queue, localState.Neighbors);
                        }
                        return localState;
                    },
                    (localState) => { }
                );
            }
            else
            {
                var visited = new HashSet<int>();
                var queue = new Queue<(int, int)>();
                var neighbors = new List<PlanetTile>();
                for (int i = 0; i < tileCount; i++)
                {
                    if (!climate.OceanMask[i] || !IsOpenOcean(i))
                    {
                        cachedWestHops[i] = -1;
                        cachedEastHops[i] = -1;
                    }
                    else
                    {
                        cachedWestHops[i] = HopsToLand(i, -1f, maxSearchHops, visited, queue, neighbors);
                        cachedEastHops[i] = HopsToLand(i, 1f, maxSearchHops, visited, queue, neighbors);
                    }
                }
            }
        }

        private void ComputeInitialOceanCurrentsForTile(int i, int maxCoastHops)
        {
            if (!climate.OceanMask[i])
                return;
            if (!IsOpenOcean(i))
            {
                climate.CurrentsAnomalySST[i] = 0f;
                climate.SST[i] = climate.LatitudinalSeaLevelTemp[i];
                return;
            }

            float latDegSigned = climate.LatitudeSigned[i] * Mathf.Rad2Deg;
            float lonRad = climate.Longitude[i];
            float latRadSigned = climate.LatitudeSigned[i];

            float perturbedLatDegSigned = climate.ClimateLatitudeDeg(latDegSigned, lonRad, latRadSigned);
            float perturbedLatDeg = Mathf.Abs(perturbedLatDegSigned);

            float subtropicsWeight = CurrentLatWeight(perturbedLatDeg);
            float latDiffStorms = (perturbedLatDeg - 50f) / 12f;
            float stormTrackWeight = Mathf.Exp(-(latDiffStorms * latDiffStorms));

            int westHops = cachedWestHops[i];
            int eastHops = cachedEastHops[i];
            bool warmWest = westHops >= 0 && westHops <= maxCoastHops;
            bool coolEast = eastHops >= 0 && eastHops <= maxCoastHops;
            if (warmWest && coolEast)
            {
                if (westHops < eastHops)
                    coolEast = false;
                else if (eastHops < westHops)
                    warmWest = false;
                else
                {
                    warmWest = false;
                    coolEast = false;
                }
            }

            float anomaly = 0f;
            if (warmWest)
                anomaly += settings.EffectiveWarmWBCurrent * subtropicsWeight;
            if (coolEast)
                anomaly += settings.EffectiveCoolEBCurrent * subtropicsWeight;

            anomaly += settings.EffectiveStormWarmCurrent * stormTrackWeight;

            climate.CurrentsAnomalySST[i] = anomaly;
            climate.SST[i] = climate.LatitudinalSeaLevelTemp[i] + anomaly;
        }

        /// <summary>
        /// Wind-driven subtropical and subpolar ocean gyre currents, eastern-boundary Ekman upwelling,
        /// and equatorial cold tongue driven by the circulation's surface wind field.
        /// </summary>
        private void ComputeWindDrivenCurrents(bool parallel)
        {
            const int bands = 90;
            var uTile = new float[tileCount];
            var vTile = new float[tileCount];
            var uBandSum = new double[bands];
            var nBandCount = new int[bands];

            for (int i = 0; i < tileCount; i++)
            {
                if (!climate.OceanMask[i] || !IsOpenOcean(i))
                    continue;

                Vector3 w = 0.25f * climate.SummerWind[i] + 0.25f * climate.WinterWind[i] + 0.50f * climate.EquinoxWind[i];
                Vector3 up = climate.TileCenters[i].normalized;
                Vector3 east = Vector3.Cross(Vector3.up, up);
                if (east.sqrMagnitude < 1e-8f)
                    east = Vector3.right;
                else
                    east.Normalize();
                Vector3 north = Vector3.Cross(up, east).normalized;

                float u = Vector3.Dot(w, east);
                float v = Vector3.Dot(w, north);
                uTile[i] = u;
                vTile[i] = v;

                float latDeg = climate.LatitudeSigned[i] * Mathf.Rad2Deg;
                int bIdx = Mathf.Clamp(Mathf.FloorToInt((latDeg + 90f) / 2f), 0, bands - 1);
                uBandSum[bIdx] += u;
                nBandCount[bIdx]++;
            }

            var uBandRaw = new float[bands];
            for (int b = 0; b < bands; b++)
                uBandRaw[b] = nBandCount[b] > 0 ? (float)(uBandSum[b] / nBandCount[b]) : 0f;

            float[] kernel = { 0.15f, 0.20f, 0.30f, 0.20f, 0.15f };
            var uBandSmooth = new float[bands];
            for (int b = 0; b < bands; b++)
            {
                float s = 0f;
                for (int k = -2; k <= 2; k++)
                {
                    int idx = Mathf.Clamp(b + k, 0, bands - 1);
                    s += kernel[k + 2] * uBandRaw[idx];
                }
                uBandSmooth[b] = s;
            }

            float kmPerTile = Mathf.Max(1f, EarthScaleConstants.KM_PER_TILE);
            int maxCoast = cachedMaxCoastHops;

            // Scale the wind-driven gyre amplitudes by the user's sea-current settings relative to default (2.5 / -2.0 / 0.8)
            float warmScale = settings.EffectiveWarmWBCurrent / 2.5f;
            float coolScale = settings.EffectiveCoolEBCurrent / -2.0f;
            float stormScale = settings.EffectiveStormWarmCurrent / 0.8f;

            float warmWb = 2.8f * warmScale;
            float coolEb = -2.6f * coolScale;
            float subpolarEastWarm = 5.0f * warmScale;
            float subpolarWestCool = -3.2f * coolScale;
            float eqUpwell = -1.8f * coolScale;
            float stormWarm = 0.8f * stormScale;

            void EvalTile(int i)
            {
                if (!climate.OceanMask[i])
                    return;
                if (!IsOpenOcean(i))
                {
                    climate.CurrentsAnomalySST[i] = 0f;
                    climate.SST[i] = climate.LatitudinalSeaLevelTemp[i];
                    return;
                }

                float latDeg = climate.LatitudeSigned[i] * Mathf.Rad2Deg;
                float absLat = Mathf.Abs(latDeg);
                float hemi = latDeg >= 0f ? 1f : -1f;
                int bIdx = Mathf.Clamp(Mathf.FloorToInt((latDeg + 90f) / 2f), 0, bands - 1);
                float uBand = uBandSmooth[bIdx];
                float u = uTile[i];
                float v = vTile[i];

                float subtropRegime = Mathf.Clamp01((40f - absLat) / 10f) * Mathf.Clamp(absLat / 10f, 0.25f, 1f);
                float westerlyDrive = Mathf.Clamp(uBand / 2.2f, 0f, 1.25f);
                float subpolarRegime = Mathf.Clamp01((absLat - 42f) / 10f)
                    * Mathf.Clamp01((74f - absLat) / 10f)
                    * (0.4f + 0.6f * westerlyDrive);

                int westHops = cachedWestHops[i];
                int eastHops = cachedEastHops[i];
                float westSubDecay = westHops > 0 ? Mathf.Exp(-((westHops - 1) * kmPerTile) / 180f) : 0f;
                float eastSubDecay = eastHops > 0 ? Mathf.Exp(-((eastHops - 1) * kmPerTile) / 240f) : 0f;
                float westPolDecay = westHops > 0 ? Mathf.Exp(-((westHops - 1) * kmPerTile) / 280f) : 0f;
                float eastPolDecay = eastHops > 0 ? Mathf.Exp(-((eastHops - 1) * kmPerTile) / 450f) : 0f;

                if (westHops > 0 && eastHops > 0 && westHops <= maxCoast && eastHops <= maxCoast)
                {
                    if (westHops < eastHops)
                    {
                        eastSubDecay = 0f;
                        eastPolDecay = 0f;
                    }
                    else if (eastHops < westHops)
                    {
                        westSubDecay = 0f;
                        westPolDecay = 0f;
                    }
                    else
                    {
                        westSubDecay = 0f;
                        westPolDecay = 0f;
                        eastSubDecay = 0f;
                        eastPolDecay = 0f;
                    }
                }

                float equatorward = Mathf.Clamp((-v * hemi + 1.5f) / 3.5f, 0.35f, 1.35f);

                float eqLatNorm = latDeg / 6.0f;
                float eqBand = Mathf.Exp(-(eqLatNorm * eqLatNorm));
                float easterly = Mathf.Clamp(-u / 3.5f, 0f, 1.3f);
                float eastBasin = eastHops > 0 ? Mathf.Exp(-(eastHops * kmPerTile) / 2500f) : 0.3f;
                float eqCool = eqUpwell * eqBand * easterly * (0.35f + 0.85f * eastBasin);

                float stormLatDiff = (absLat - 50f) / 12f;
                float stormW = Mathf.Clamp01(uBand / 4.5f) * Mathf.Exp(-(stormLatDiff * stormLatDiff));

                float anomaly = warmWb * subtropRegime * westSubDecay
                    + coolEb * subtropRegime * equatorward * eastSubDecay
                    + subpolarEastWarm * subpolarRegime * eastPolDecay
                    + subpolarWestCool * subpolarRegime * westPolDecay
                    + eqCool
                    + stormWarm * stormW;

                climate.CurrentsAnomalySST[i] = anomaly;
                climate.SST[i] = climate.LatitudinalSeaLevelTemp[i] + anomaly;
            }

            if (parallel)
                Parallel.For(0, tileCount, EvalTile);
            else
            {
                for (int i = 0; i < tileCount; i++)
                    EvalTile(i);
            }
        }

        // ========== COASTAL MODERATION ==========

        /// <summary>
        /// Calculate coastal moderation strength
        /// </summary>
        private void ComputeCoastalModeration(bool parallel)
        {
            if (parallel)
            {
                Parallel.For(0, tileCount, i => ComputeCoastalModerationForTile(i));
            }
            else
            {
                for (int i = 0; i < tileCount; i++) ComputeCoastalModerationForTile(i);
            }
        }

        private void ComputeCoastalModerationForTile(int i)
        {
            if (!climate.OceanMask[i])
            {
                float d = climate.OceanDistKm[i];
                float w = Mathf.Exp(-d / Mathf.Max(1f, settings.EffectiveCoastBlendKm));
                climate.CoastalModerationStrength[i] = w;
            }
        }

        // ========== FINAL TEMPERATURE ==========

        /// <summary>
        /// Final temperature only, for a coupling round after the maritime layer has changed continentality
        /// and the upwind sea temperature. The latitude curve and the currents do not change.
        /// </summary>
        public void RefreshFinal()
        {
            ComputeFinalTemperature(Controller.Settings.perfParallelTemperature.CurrentValue);
        }

        /// <summary>
        /// Compute final annual mean temperature
        /// </summary>
        private void ComputeFinalTemperature(bool parallel)
        {
            if (parallel)
            {
                Parallel.For(0, tileCount, i => ComputeFinalTemperatureForTile(i));
            }
            else
            {
                for (int i = 0; i < tileCount; i++) ComputeFinalTemperatureForTile(i);
            }
        }

        private void ComputeFinalTemperatureForTile(int i)
        {
            if (climate.OceanMask[i])
            {
                // Ocean uses SST directly (already includes currents)
                climate.AnnualMeanTemp[i] = climate.SST[i];
            }
            else
            {
                // ===== LAND TEMPERATURE CALCULATION =====
                // Blend the sea-level temperature toward the ocean first.
                // Lapse rate and snow albedo then cool that result, so a coastal mountain stays cold.
                // After the maritime layer, that is the sea the air came from, weighted by how much of the air came off it.
                float temp = climate.LatitudinalSeaLevelTemp[i];
                float elevKm = climate.ElevationKm[i];
                if (climate.MaritimeAnnual != null && climate.UpwindSST != null)
                {
                    float pull = Mathf.Clamp01(settings.maritimeBlendWeight.CurrentValue * climate.MaritimeAnnual[i]);
                    // High elevated plateaus/ice domes (> 0.8 km) decouple from warm marine boundary-layer SST pull
                    if (climate.UpwindSST[i] > temp)
                        pull *= Mathf.Exp(-Mathf.Max(0f, elevKm - 0.8f) / 1.1f);
                    temp = Mathf.Lerp(temp, climate.UpwindSST[i], pull);
                }
                else if (climate.NearestOceanTile[i] >= 0 && climate.CoastalModerationStrength[i] > 0.01f)
                {
                    float sstNear = climate.SST[climate.NearestOceanTile[i]];
                    temp = Mathf.Lerp(temp, sstNear, climate.CoastalModerationStrength[i]);
                }

                temp -= ClimateReferenceData.LandSurfaceLapsePerKm * elevKm;

                // Snow and ice above 3 km: 1°C extra cooling per km.
                if (elevKm > 3.0f)
                    temp -= (elevKm - 3.0f);

                // Inland penalty
                // Continental interiors are slightly cooler (reduced moisture, dry convection)
                // Penalty only applies to truly continental locations (Continentality ≈ 1)
                temp -= settings.inlandMeanPenalty.CurrentValue * climate.Continentality[i];

                climate.AnnualMeanTemp[i] = temp;
            }

            float latDeg = climate.LatitudeSigned[i] * Mathf.Rad2Deg;
            float tiltDeg = AxialTiltCurves.GetTiltDegrees(Planets_GameComponent.ActiveAxialTilt);
            climate.AnnualMeanTemp[i] += AxialAnnualInsolation.TemperatureOffset(latDeg, tiltDeg);
        }

        // ========== HELPER METHODS ==========

        private bool IsOpenOcean(int tileId)
        {
            return climate.TrueOceanMask != null
                && tileId >= 0
                && tileId < climate.TrueOceanMask.Length
                && climate.TrueOceanMask[tileId];
        }

        /// <summary>
        /// Boundary currents stay strong from 12° to 45°, instead of a narrow peak at 25°.
        /// </summary>
        private static float CurrentLatWeight(float latDeg)
        {
            if (latDeg < 12f)
                return Mathf.Lerp(0.3f, 1f, latDeg / 12f);
            if (latDeg <= 45f)
                return 1f;
            return Mathf.Clamp01(1f - (latDeg - 45f) / 20f);
        }

        /// <summary>
        /// Hops west (lonDir -1) or east (lonDir +1) through true ocean until land or a lake.
        /// -1 when nothing is found within maxHops.
        /// </summary>
        private int HopsToLand(int tileID, float lonDir, int maxHops, HashSet<int> directionVisited, Queue<(int, int)> directionQueue, List<PlanetTile> neighborBuffer)
        {
            if (!IsOpenOcean(tileID))
                return -1;

            float myLon = climate.Longitude[tileID] * Mathf.Rad2Deg;
            directionVisited.Clear();
            directionQueue.Clear();
            directionQueue.Enqueue((tileID, 0));
            directionVisited.Add(tileID);

            while (directionQueue.Count > 0)
            {
                var (currentTile, depth) = directionQueue.Dequeue();
                if (depth >= maxHops)
                    continue;

                neighborBuffer.Clear();
                Find.WorldGrid.GetTileNeighbors(currentTile, neighborBuffer);
                foreach (PlanetTile neighbor in neighborBuffer)
                {
                    int nID = neighbor.tileId;
                    if (directionVisited.Contains(nID))
                        continue;
                    directionVisited.Add(nID);

                    float lonDiff = climate.Longitude[nID] * Mathf.Rad2Deg - myLon;
                    if (lonDiff > 180f) lonDiff -= 360f;
                    if (lonDiff < -180f) lonDiff += 360f;
                    bool rightLon = (lonDir < 0f && lonDiff < 0f) || (lonDir > 0f && lonDiff > 0f);
                    if (!rightLon)
                        continue;

                    if (!IsOpenOcean(nID))
                        return depth + 1;
                    directionQueue.Enqueue((nID, depth + 1));
                }
            }

            return -1;
        }
    }
}
