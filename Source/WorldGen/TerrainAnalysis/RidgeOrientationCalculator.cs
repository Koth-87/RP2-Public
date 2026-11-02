using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Planets.Utilities.Logging;

namespace Planets.WorldGen.TerrainAnalysis
{
    /// <summary>
    /// Phase 11: Ridge Orientation Calculation.
    /// Computes local across-ridge tangent normals (<c>RidgeNormal</c>) per ridge tile via river-system boundary edges,
    /// falling back to 2D tangent-plane PCA line fitting and 1-ring smoothing.
    /// </summary>
    public class RidgeOrientationCalculator : TerrainAnalysisBase
    {
        private enum OrientationMethod
        {
            BoundaryEdge,
            SameSystemLineFit,
            Omnidirectional
        }

        private const float CoherenceThreshold = 0.6f;

        public override void Execute()
        {
            RPLog.D("[Terrain Analysis] Computing local ridge orientations and identities");

            int ridgeCount = 0;
            int primarySuccessCount = 0;
            int fallbackSuccessCount = 0;
            int omnidirectionalCount = 0;

            float minCoherence = float.MaxValue;
            float sumCoherence = 0f;
            int coherenceCount = 0;

            for (int i = 0; i < tileCount; i++)
            {
                if (!climate.IsRidgeTile[i] || climate.OceanMask[i])
                {
                    climate.RidgeNormal[i] = Vector3.zero;
                    climate.RidgeNeighborSystemID[i] = -1;
                    continue;
                }

                ridgeCount++;
                ComputeRidgeIdentity(i);

                OrientationMethod method = ComputeLocalOrientation(i, out float coherence);
                if (method == OrientationMethod.BoundaryEdge)
                {
                    primarySuccessCount++;
                    minCoherence = Mathf.Min(minCoherence, coherence);
                    sumCoherence += coherence;
                    coherenceCount++;
                }
                else if (method == OrientationMethod.SameSystemLineFit)
                {
                    fallbackSuccessCount++;
                }
                else
                {
                    omnidirectionalCount++;
                }
            }

            ApplySmoothingPass();

            float meanCoherence = coherenceCount > 0 ? sumCoherence / coherenceCount : 0f;
            RPLog.D($"[Terrain Analysis] Processed {ridgeCount} ridge tiles: " +
                    $"{primarySuccessCount} boundary-edge, {fallbackSuccessCount} line-fit, " +
                    $"{omnidirectionalCount} omnidirectional");
            RPLog.D($"[Terrain Analysis] Coherence statistics: min={minCoherence:F3}, mean={meanCoherence:F3}, threshold={CoherenceThreshold:F3}");
        }

        private void ComputeRidgeIdentity(int tileID)
        {
            int mySystem = climate.RiverSystemIDTopology[tileID];
            if (mySystem < 0)
            {
                climate.RidgeNeighborSystemID[tileID] = -1;
                return;
            }

            var systemCounts = new Dictionary<int, int>();
            neighborBuffer.Clear();
            grid.GetTileNeighbors(tileID, neighborBuffer);

            foreach (var neighbor in neighborBuffer)
            {
                int nID = neighbor.tileId;
                if (climate.OceanMask[nID])
                    continue;

                int nSystem = climate.RiverSystemIDTopology[nID];
                if (nSystem >= 0 && nSystem != mySystem)
                {
                    if (!systemCounts.ContainsKey(nSystem))
                        systemCounts[nSystem] = 0;
                    systemCounts[nSystem]++;
                }
            }

            climate.RidgeNeighborSystemID[tileID] = systemCounts.Count > 0
                ? systemCounts.OrderByDescending(kvp => kvp.Value).First().Key
                : -1;
        }

        private OrientationMethod ComputeLocalOrientation(int tileID, out float coherence)
        {
            coherence = 0f;
            int mySystem = climate.RiverSystemIDTopology[tileID];
            Vector3 tileCentroid = climate.TileCenters[tileID];

            Vector3 boundaryNormal = ComputeBoundaryEdgeNormal(tileID, mySystem, tileCentroid, out coherence, out int boundaryCount);
            if (boundaryCount >= 1 && coherence >= CoherenceThreshold)
            {
                climate.RidgeNormal[tileID] = boundaryNormal;
                return OrientationMethod.BoundaryEdge;
            }

            List<int> ridgeNeighbors = GatherRidgeNeighbors(tileID, rings: 2, filterToSystem: mySystem);
            if (ridgeNeighbors.Count < 3)
            {
                climate.RidgeNormal[tileID] = Vector3.zero;
                return OrientationMethod.Omnidirectional;
            }

            Vector3 lineNormal = ComputeLineFitNormal(tileID, ridgeNeighbors);
            if (lineNormal.sqrMagnitude < 0.001f)
            {
                climate.RidgeNormal[tileID] = Vector3.zero;
                return OrientationMethod.Omnidirectional;
            }

            climate.RidgeNormal[tileID] = lineNormal;
            return OrientationMethod.SameSystemLineFit;
        }

        private List<int> GatherRidgeNeighbors(int tileID, int rings, int filterToSystem)
        {
            var result = new List<int>();
            var currentRing = new List<int>();
            var nextRing = new List<int>();

            bfsResult.Clear();
            currentRing.Add(tileID);
            bfsResult.Add(tileID);

            if (climate.IsRidgeTile[tileID] && (filterToSystem < 0 || climate.RiverSystemIDTopology[tileID] == filterToSystem))
                result.Add(tileID);

            for (int ring = 1; ring <= rings; ring++)
            {
                nextRing.Clear();
                foreach (int currentTile in currentRing)
                {
                    neighborBuffer.Clear();
                    grid.GetTileNeighbors(currentTile, neighborBuffer);

                    foreach (var neighbor in neighborBuffer)
                    {
                        int nID = neighbor.tileId;
                        if (bfsResult.Add(nID))
                        {
                            nextRing.Add(nID);
                            if (climate.IsRidgeTile[nID] && !climate.OceanMask[nID]
                                && (filterToSystem < 0 || climate.RiverSystemIDTopology[nID] == filterToSystem))
                            {
                                result.Add(nID);
                            }
                        }
                    }
                }

                if (nextRing.Count == 0)
                    break;

                var temp = currentRing;
                currentRing = nextRing;
                nextRing = temp;
            }

            return result;
        }

        private Vector3 ComputeBoundaryEdgeNormal(int tileID, int mySystem, Vector3 tileCentroid, out float coherence, out int boundaryCount)
        {
            Vector3 sumDirection = Vector3.zero;
            boundaryCount = 0;
            Vector3 surfaceNormal = tileCentroid.normalized;

            neighborBuffer.Clear();
            grid.GetTileNeighbors(tileID, neighborBuffer);

            foreach (var neighbor in neighborBuffer)
            {
                int nID = neighbor.tileId;
                if (climate.OceanMask[nID])
                    continue;

                int nSystem = climate.RiverSystemIDTopology[nID];
                if (nSystem >= 0 && nSystem != mySystem)
                {
                    Vector3 neighborPos = climate.TileCenters[nID];
                    Vector3 toNeighbor = (neighborPos - tileCentroid).normalized;
                    Vector3 projected = toNeighbor - surfaceNormal * Vector3.Dot(toNeighbor, surfaceNormal);

                    if (projected.sqrMagnitude > 0.001f)
                    {
                        sumDirection += projected.normalized;
                        boundaryCount++;
                    }
                }
            }

            if (boundaryCount == 0)
            {
                coherence = 0f;
                return Vector3.zero;
            }

            coherence = sumDirection.magnitude / boundaryCount;
            return sumDirection.sqrMagnitude < 0.001f ? Vector3.zero : sumDirection.normalized;
        }

        private void ApplySmoothingPass()
        {
            var smoothedNormals = new Vector3[tileCount];

            for (int i = 0; i < tileCount; i++)
            {
                if (!climate.IsRidgeTile[i] || climate.OceanMask[i])
                {
                    smoothedNormals[i] = climate.RidgeNormal[i];
                    continue;
                }

                Vector3 currentNormal = climate.RidgeNormal[i];
                if (currentNormal.sqrMagnitude < 0.001f)
                {
                    smoothedNormals[i] = Vector3.zero;
                    continue;
                }

                int mySystem = climate.RiverSystemIDTopology[i];
                Vector3 sumNormals = currentNormal;
                int count = 1;

                neighborBuffer.Clear();
                grid.GetTileNeighbors(i, neighborBuffer);

                foreach (var neighbor in neighborBuffer)
                {
                    int nID = neighbor.tileId;
                    if (!climate.IsRidgeTile[nID] || climate.OceanMask[nID])
                        continue;
                    if (climate.RiverSystemIDTopology[nID] != mySystem)
                        continue;

                    Vector3 nNormal = climate.RidgeNormal[nID];
                    if (nNormal.sqrMagnitude < 0.001f)
                        continue;

                    sumNormals += nNormal;
                    count++;
                }

                smoothedNormals[i] = count > 0 && sumNormals.sqrMagnitude > 0.001f
                    ? sumNormals.normalized
                    : currentNormal;
            }

            for (int i = 0; i < tileCount; i++)
                climate.RidgeNormal[i] = smoothedNormals[i];
        }

        private Vector3 ComputeLineFitNormal(int tileID, List<int> ridgeNeighbors)
        {
            Vector3 tileCentroid = climate.TileCenters[tileID];
            Vector3 n = tileCentroid.normalized;
            Vector3 north = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(n, north)) > 0.9f)
                north = Vector3.right;

            Vector3 u = Vector3.Cross(north, n).normalized;
            Vector3 v = Vector3.Cross(n, u).normalized;

            const float radius = 100f;
            Vector3 centroid3D = Vector3.zero;
            foreach (int id in ridgeNeighbors)
                centroid3D += climate.TileCenters[id] * radius;
            centroid3D /= ridgeNeighbors.Count;

            float[] x = new float[ridgeNeighbors.Count];
            float[] y = new float[ridgeNeighbors.Count];

            for (int i = 0; i < ridgeNeighbors.Count; i++)
            {
                Vector3 pos = climate.TileCenters[ridgeNeighbors[i]] * radius;
                Vector3 offset = pos - centroid3D;
                x[i] = Vector3.Dot(offset, u);
                y[i] = Vector3.Dot(offset, v);
            }

            float meanX = x.Average();
            float meanY = y.Average();

            float cov_xx = 0f, cov_yy = 0f, cov_xy = 0f;
            for (int i = 0; i < ridgeNeighbors.Count; i++)
            {
                float dx = x[i] - meanX;
                float dy = y[i] - meanY;
                cov_xx += dx * dx;
                cov_yy += dy * dy;
                cov_xy += dx * dy;
            }
            cov_xx /= ridgeNeighbors.Count;
            cov_yy /= ridgeNeighbors.Count;
            cov_xy /= ridgeNeighbors.Count;

            float trace = cov_xx + cov_yy;
            float det = cov_xx * cov_yy - cov_xy * cov_xy;
            float discriminant = Mathf.Max(0f, trace * trace - 4f * det);
            float lambda1 = (trace + Mathf.Sqrt(discriminant)) / 2f;

            float ev_x = cov_xy;
            float ev_y = lambda1 - cov_xx;
            float ev_norm = Mathf.Sqrt(ev_x * ev_x + ev_y * ev_y);
            if (ev_norm < 0.001f)
                return Vector3.zero;

            ev_x /= ev_norm;
            ev_y /= ev_norm;

            Vector3 tangent = (u * ev_x + v * ev_y).normalized;
            return Vector3.Cross(centroid3D.normalized, tangent).normalized;
        }
    }
}
