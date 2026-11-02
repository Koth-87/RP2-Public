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

        // Removed class-level buffers to allow thread-safety
        // Buffers are now allocated per-thread in ComputeOceanCurrents

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

                // Ocean polar cooling: much less severe than land
                // Above 70°N/S, add extra cooling but only 30% of land extreme
                // Using perturbed latitude for consistent boundaries
                if (perturbedLatDeg > 70f)
                {
                    float polarFactor = (perturbedLatDeg - 70f) / 20f; // 0 at 70°, 1 at 90°
                    oceanTemp += (settings.polarColdExtra.CurrentValue * 0.3f) * polarFactor;
                }

                climate.LatitudinalSeaLevelTemp[i] = oceanTemp;
            }
            else
            {
                // ===== LAND TEMPERATURE =====
                // Land cools more aggressively due to:
                // - Lower heat capacity
                // - No ocean circulation moderating effect
                // - Albedo feedback in polar polar regions (snow/ice coverage)
                float landTemp = Mathf.Lerp(climate.PoleMean, climate.EquatorMean, c);

                // Land polar cooling: severe above 70°N/S
                // Accounts for continental ice sheets and extreme polar climates
                // Using perturbed latitude for consistent boundaries
                if (perturbedLatDeg > 70f)
                {
                    float polarFactor = (perturbedLatDeg - 70f) / 20f; // 0 at 70°, 1 at 90°
                    landTemp += settings.polarColdExtra.CurrentValue * polarFactor;
                }

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
            if (parallel)
            {
                // Allocate buffers per thread
                Parallel.For(0, tileCount, 
                    () => new { 
                        Visited = new HashSet<int>(), 
                        Queue = new Queue<(int, int)>(), 
                        Neighbors = new List<PlanetTile>() 
                    },
                    (i, loopState, localState) => 
                    {
                        ComputeOceanCurrentsForTile(i, localState.Visited, localState.Queue, localState.Neighbors);
                        return localState;
                    },
                    (localState) => { }
                );
            }
            else
            {
                // Allocate single set of buffers for main thread
                HashSet<int> visited = new HashSet<int>();
                Queue<(int, int)> queue = new Queue<(int, int)>();
                List<PlanetTile> neighbors = new List<PlanetTile>();

                for (int i = 0; i < tileCount; i++)
                {
                    ComputeOceanCurrentsForTile(i, visited, queue, neighbors);
                }
            }
        }

        private void ComputeOceanCurrentsForTile(int i, HashSet<int> visited, Queue<(int, int)> queue, List<PlanetTile> neighbors)
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
            
            // Add planetary wave perturbation (Rossby waves) to break up perfectly linear currents
            float lonRad = climate.Longitude[i];
            float latRadSigned = climate.LatitudeSigned[i];
            
            float perturbedLatDegSigned = climate.ClimateLatitudeDeg(latDegSigned, lonRad, latRadSigned);
            float perturbedLatDeg = Mathf.Abs(perturbedLatDegSigned);

            float subtropicsWeight = CurrentLatWeight(perturbedLatDeg);

            // Storm-track warming peaks at 50°N/S and has no coast side.
            float latDiffStorms = (perturbedLatDeg - 50f) / 12f;
            float stormTrackWeight = Mathf.Exp(-(latDiffStorms * latDiffStorms));

            int maxCoastHops = Mathf.Clamp(Mathf.RoundToInt(225f / EarthScaleConstants.KM_PER_TILE), 1, 14);
            int maxSearchHops = Mathf.Max(maxCoastHops + 4, 6);
            int westHops = HopsToLand(i, -1f, maxSearchHops, visited, queue, neighbors);
            int eastHops = HopsToLand(i, 1f, maxSearchHops, visited, queue, neighbors);
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
                if (climate.MaritimeAnnual != null && climate.UpwindSST != null)
                {
                    float pull = Mathf.Clamp01(settings.maritimeBlendWeight.CurrentValue * climate.MaritimeAnnual[i]);
                    temp = Mathf.Lerp(temp, climate.UpwindSST[i], pull);
                }
                else if (climate.NearestOceanTile[i] >= 0 && climate.CoastalModerationStrength[i] > 0.01f)
                {
                    float sstNear = climate.SST[climate.NearestOceanTile[i]];
                    temp = Mathf.Lerp(temp, sstNear, climate.CoastalModerationStrength[i]);
                }

                float elevKm = climate.ElevationKm[i];
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
