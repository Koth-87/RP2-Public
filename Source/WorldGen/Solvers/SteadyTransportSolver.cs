using System;

namespace Planets.WorldGen.Solvers
{
    /// <summary>
    /// Sparse steady transport. No RimWorld types, so a coarse mesh can call it too.
    /// Equation: diagonal[i] * x[i] - sum_j weight[i, j] * x[j] = rhs[i].
    /// Weights are stored by row. The sweep order is the preconditioner: one upstream pass.
    /// </summary>
    public sealed class TransportSystem
    {
        public int Count;
        public int[] RowStart;
        public int[] Column;
        public float[] Weight;
        public float[] Diagonal;
        public float[] Rhs;
        public int[] SweepOrder;
    }

    public sealed class TransportSolution
    {
        public float[] X;
        public int Iterations;
        public float RelativeImbalance;
        public bool Converged;
        /// <summary>Last residual divided by the one before it. Near 1 means the error is still shrinking slowly.</summary>
        public float ShrinkRatio;
    }

    public static class SteadyTransportSolver
    {
        public static TransportSolution Solve(TransportSystem system, float relativeTolerance, int maxIterations)
        {
            int n = system.Count;
            var x = new float[n];
            var r = new float[n];
            var rHat = new float[n];
            var p = new float[n];
            var v = new float[n];
            var s = new float[n];
            var t = new float[n];
            var pHat = new float[n];
            var sHat = new float[n];
            var ax = new float[n];

            Array.Copy(system.Rhs, r, n);
            Array.Copy(r, rHat, n);
            double bNorm = Norm(r);
            if (bNorm < 1e-12)
            {
                return new TransportSolution
                {
                    X = x,
                    Iterations = 0,
                    RelativeImbalance = 0f,
                    Converged = true,
                    ShrinkRatio = 0f
                };
            }

            double rho = 1.0;
            double alpha = 1.0;
            double omega = 1.0;
            double previous = bNorm;
            float shrink = 1f;
            int used = 0;
            bool converged = false;
            for (int iter = 0; iter < maxIterations; iter++)
            {
                double rhoPrev = rho;
                rho = Dot(rHat, r);
                if (Math.Abs(rho) < 1e-30 || Math.Abs(omega) < 1e-30)
                    break;

                double beta = (rho / rhoPrev) * (alpha / omega);
                for (int i = 0; i < n; i++)
                    p[i] = (float)(r[i] + beta * (p[i] - omega * v[i]));

                Precondition(system, p, pHat);
                Apply(system, pHat, v);
                double denom = Dot(rHat, v);
                if (Math.Abs(denom) < 1e-30)
                    break;
                alpha = rho / denom;

                for (int i = 0; i < n; i++)
                    s[i] = (float)(r[i] - alpha * v[i]);
                double sNorm = Norm(s);
                if (sNorm / bNorm <= relativeTolerance)
                {
                    for (int i = 0; i < n; i++)
                        x[i] += (float)(alpha * pHat[i]);
                    Array.Copy(s, r, n);
                    used = iter + 1;
                    shrink = (float)(sNorm / Math.Max(previous, 1e-30));
                    previous = sNorm;
                    converged = true;
                    break;
                }

                Precondition(system, s, sHat);
                Apply(system, sHat, t);
                double tt = Dot(t, t);
                if (tt < 1e-30)
                    break;
                omega = Dot(t, s) / tt;

                for (int i = 0; i < n; i++)
                {
                    x[i] += (float)(alpha * pHat[i] + omega * sHat[i]);
                    r[i] = (float)(s[i] - omega * t[i]);
                }

                double rNorm = Norm(r);
                used = iter + 1;
                shrink = (float)(rNorm / Math.Max(previous, 1e-30));
                previous = rNorm;
                if (rNorm / bNorm <= relativeTolerance)
                {
                    converged = true;
                    break;
                }
            }

            Apply(system, x, ax);
            double imbalance = 0.0;
            for (int i = 0; i < n; i++)
            {
                double diff = system.Rhs[i] - ax[i];
                imbalance += diff * diff;
            }
            double relative = Math.Sqrt(imbalance) / bNorm;
            return new TransportSolution
            {
                X = x,
                Iterations = used,
                RelativeImbalance = (float)relative,
                Converged = converged && relative <= relativeTolerance * 10.0,
                ShrinkRatio = shrink
            };
        }

        private static void Apply(TransportSystem system, float[] x, float[] y)
        {
            int n = system.Count;
            for (int i = 0; i < n; i++)
            {
                double sum = system.Diagonal[i] * (double)x[i];
                int end = system.RowStart[i + 1];
                for (int k = system.RowStart[i]; k < end; k++)
                    sum -= system.Weight[k] * (double)x[system.Column[k]];
                y[i] = (float)sum;
            }
        }

        /// <summary>One upstream sweep. Tiles not yet visited contribute nothing, so a loop's cut edge waits.</summary>
        private static void Precondition(TransportSystem system, float[] v, float[] z)
        {
            Array.Clear(z, 0, system.Count);
            int[] order = system.SweepOrder;
            for (int s = 0; s < order.Length; s++)
            {
                int i = order[s];
                double acc = v[i];
                int end = system.RowStart[i + 1];
                for (int k = system.RowStart[i]; k < end; k++)
                    acc += system.Weight[k] * (double)z[system.Column[k]];
                float diag = system.Diagonal[i];
                z[i] = Math.Abs(diag) > 1e-12f ? (float)(acc / diag) : 0f;
            }
        }

        private static double Dot(float[] a, float[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
                sum += a[i] * (double)b[i];
            return sum;
        }

        private static double Norm(float[] a)
        {
            return Math.Sqrt(Dot(a, a));
        }
    }
}
