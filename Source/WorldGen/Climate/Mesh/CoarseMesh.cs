using System.Collections.Generic;
using UnityEngine;

namespace Planets.WorldGen.Climate.Mesh
{
    /// <summary>
    /// Icosahedral mesh for the large-scale wind. Six splits of the 12-vertex icosahedron:
    /// 10 × 4^6 + 2 = 40,962 cells, about 110 km apart. Cached for the process.
    /// </summary>
    public sealed class CoarseMesh
    {
        public const int Subdivisions = 6;
        public const float EarthRadiusM = 6371000f;

        public readonly int Count;
        public readonly Vector3[] Position;
        public readonly int[][] Neighbors;
        public readonly float[][] EdgeLengthM;
        public readonly float[] AreaM2;
        public readonly int[] TriA;
        public readonly int[] TriB;
        public readonly int[] TriC;
        public readonly int[][] VertexTriangles;

        static CoarseMesh cached;

        public static CoarseMesh Get()
        {
            if (cached == null)
                cached = Build();
            return cached;
        }

        CoarseMesh(int count, Vector3[] position, int[][] neighbors, float[][] edgeLengthM, float[] areaM2,
            int[] triA, int[] triB, int[] triC, int[][] vertexTriangles)
        {
            Count = count;
            Position = position;
            Neighbors = neighbors;
            EdgeLengthM = edgeLengthM;
            AreaM2 = areaM2;
            TriA = triA;
            TriB = triB;
            TriC = triC;
            VertexTriangles = vertexTriangles;
        }

        static CoarseMesh Build()
        {
            float golden = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var verts = new List<Vector3>
            {
                Norm(-1f, golden, 0f), Norm(1f, golden, 0f), Norm(-1f, -golden, 0f), Norm(1f, -golden, 0f),
                Norm(0f, -1f, golden), Norm(0f, 1f, golden), Norm(0f, -1f, -golden), Norm(0f, 1f, -golden),
                Norm(golden, 0f, -1f), Norm(golden, 0f, 1f), Norm(-golden, 0f, -1f), Norm(-golden, 0f, 1f)
            };
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };

            var mid = new Dictionary<long, int>();
            for (int split = 0; split < Subdivisions; split++)
            {
                var next = new List<int>(faces.Count * 4);
                for (int f = 0; f < faces.Count; f += 3)
                {
                    int a = faces[f];
                    int b = faces[f + 1];
                    int c = faces[f + 2];
                    int ab = Midpoint(verts, mid, a, b);
                    int bc = Midpoint(verts, mid, b, c);
                    int ca = Midpoint(verts, mid, c, a);
                    next.Add(a); next.Add(ab); next.Add(ca);
                    next.Add(b); next.Add(bc); next.Add(ab);
                    next.Add(c); next.Add(ca); next.Add(bc);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }
                faces = next;
                mid.Clear();
            }

            int count = verts.Count;
            int triCount = faces.Count / 3;
            var triA = new int[triCount];
            var triB = new int[triCount];
            var triC = new int[triCount];
            var vertTris = new List<int>[count];
            var neighbors = new List<int>[count];
            var area = new double[count];
            var edgeTris = new Dictionary<long, long>();
            for (int i = 0; i < count; i++)
            {
                vertTris[i] = new List<int>(6);
                neighbors[i] = new List<int>(6);
            }

            for (int t = 0; t < triCount; t++)
            {
                int a = faces[t * 3];
                int b = faces[t * 3 + 1];
                int c = faces[t * 3 + 2];
                triA[t] = a;
                triB[t] = b;
                triC[t] = c;
                vertTris[a].Add(t);
                vertTris[b].Add(t);
                vertTris[c].Add(t);
                float triArea = TriangleAreaM2(verts[a], verts[b], verts[c]);
                area[a] += triArea / 3.0;
                area[b] += triArea / 3.0;
                area[c] += triArea / 3.0;
                NoteEdge(edgeTris, neighbors, a, b, t);
                NoteEdge(edgeTris, neighbors, b, c, t);
                NoteEdge(edgeTris, neighbors, c, a, t);
            }

            var edgeLength = new float[count][];
            var neighborIds = new int[count][];
            var triIds = new int[count][];
            var centroids = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                Vector3 sum = verts[triA[t]] + verts[triB[t]] + verts[triC[t]];
                centroids[t] = sum.normalized;
            }

            for (int i = 0; i < count; i++)
            {
                neighborIds[i] = neighbors[i].ToArray();
                triIds[i] = vertTris[i].ToArray();
                edgeLength[i] = new float[neighborIds[i].Length];
                for (int k = 0; k < neighborIds[i].Length; k++)
                {
                    int j = neighborIds[i][k];
                    if (!edgeTris.TryGetValue(EdgeKey(i, j), out long packed))
                        continue;
                    int t0 = (int)(packed & 0xFFFFF);
                    int t1 = (int)((packed >> 20) & 0xFFFFF);
                    if (t1 == 0xFFFFF)
                    {
                        edgeLength[i][k] = GreatCircle(verts[i], verts[j]) * 0.5f;
                        continue;
                    }
                    edgeLength[i][k] = GreatCircle(centroids[t0], centroids[t1]);
                }
            }

            var areaM2 = new float[count];
            for (int i = 0; i < count; i++)
                areaM2[i] = (float)area[i];

            return new CoarseMesh(count, verts.ToArray(), neighborIds, edgeLength, areaM2, triA, triB, triC, triIds);
        }

        static void NoteEdge(Dictionary<long, long> edgeTris, List<int>[] neighbors, int a, int b, int tri)
        {
            long key = EdgeKey(a, b);
            if (!edgeTris.TryGetValue(key, out long packed))
            {
                edgeTris[key] = (long)tri | (0xFFFFFL << 20);
                neighbors[a].Add(b);
                neighbors[b].Add(a);
                return;
            }
            int first = (int)(packed & 0xFFFFF);
            edgeTris[key] = (long)first | ((long)tri << 20);
        }

        static long EdgeKey(int a, int b)
        {
            if (a > b)
            {
                int swap = a;
                a = b;
                b = swap;
            }
            return ((long)a << 32) | (uint)b;
        }

        static int Midpoint(List<Vector3> verts, Dictionary<long, int> mid, int a, int b)
        {
            long key = EdgeKey(a, b);
            if (mid.TryGetValue(key, out int id))
                return id;
            id = verts.Count;
            verts.Add((verts[a] + verts[b]).normalized);
            mid[key] = id;
            return id;
        }

        static Vector3 Norm(float x, float y, float z)
        {
            return new Vector3(x, y, z).normalized;
        }

        static float TriangleAreaM2(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            return 0.5f * Vector3.Cross(ab, ac).magnitude * EarthRadiusM * EarthRadiusM;
        }

        static float GreatCircle(Vector3 a, Vector3 b)
        {
            float dot = Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f);
            return Mathf.Acos(dot) * EarthRadiusM;
        }

        public void Frame(Vector3 position, out Vector3 east, out Vector3 north)
        {
            Vector3 radial = position.sqrMagnitude > 1e-8f ? position.normalized : Vector3.up;
            east = Vector3.Cross(radial, Vector3.up);
            if (east.sqrMagnitude < 1e-8f)
                east = Vector3.right;
            east.Normalize();
            north = Vector3.Cross(east, radial).normalized;
        }
    }
}
