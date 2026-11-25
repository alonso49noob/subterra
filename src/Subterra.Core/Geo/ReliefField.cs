using System;
using System.Collections.Generic;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Geo
{
    /// <summary>
    /// The terrain height a body's <see cref="ReliefSpec"/>s add (or take away) at a direction from its centre: volcano
    /// cones with a crater, rille trenches with intact bridges. Smooth everywhere (the terrain's own shading stays right),
    /// zero outside each feature's reach, cheap to test far away (one dot product per feature). Thread-safe.
    /// </summary>
    public sealed class ReliefField
    {
        private readonly List<Feature> features = new List<Feature>();
        private readonly double radius;

        public ReliefField(IEnumerable<ReliefSpec> specs, double bodyRadius)
        {
            radius = bodyRadius;
            foreach (ReliefSpec s in specs)
            {
                features.Add(s.Kind == ReliefKind.Volcano ? (Feature)new Volcano(s, bodyRadius) : new Rille(s, bodyRadius));
            }
        }

        public bool IsEmpty => features.Count == 0;

        /// <summary>Metres added to the terrain's height at <paramref name="dir"/> (a unit vector, body frame).</summary>
        public double HeightDelta(Vec3d dir)
        {
            double sum = 0;
            foreach (Feature f in features)
            {
                if (Vec3d.Dot(dir, f.Centre) >= f.CosReach)
                {
                    sum += f.Delta(dir);
                }
            }

            return sum;
        }

        private abstract class Feature
        {
            public Vec3d Centre;

            /// <summary>Cosine of the angle (from the centre) beyond which the feature adds nothing.</summary>
            public double CosReach;

            public abstract double Delta(Vec3d dir);
        }

        // A cone with a smooth foot and a rounded top, its crater a bowl sunk into the top.
        private sealed class Volcano : Feature
        {
            private readonly double baseRadius, height, craterRadius, craterDepth, bodyRadius;

            public Volcano(ReliefSpec s, double bodyRadius)
            {
                Centre = BodyFrame.Direction(s.Lat, s.Lon);
                baseRadius = s.Radius;
                height = s.Height;
                craterRadius = s.CraterRadius;
                craterDepth = s.CraterDepth;
                this.bodyRadius = bodyRadius;
                CosReach = Math.Cos(baseRadius / bodyRadius);
            }

            public override double Delta(Vec3d dir)
            {
                double d = Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(dir, Centre)))) * bodyRadius;
                if (d >= baseRadius)
                {
                    return 0;
                }

                // Cone: smoothstep from the foot, so the slope eases into the plain and into the top.
                double t = 1 - d / baseRadius;
                double cone = height * t * t * (3 - 2 * t);
                if (d >= craterRadius * 1.3)
                {
                    return cone;
                }

                // Crater: a bowl whose floor is craterDepth under the rim, with a raised lip at its edge.
                double u = d / craterRadius;
                double bowl = u < 1 ? -craterDepth * (1 - u * u) : 0;
                double lip = craterDepth * 0.08 * Math.Exp(-Math.Pow((u - 1) / 0.12, 2));
                return cone + bowl + lip;
            }
        }

        // A trench along a path: a U profile (flat floor, steep upper walls), shallower to nothing over bridges and at the ends.
        private sealed class Rille : Feature
        {
            private readonly List<Vec3d> path = new List<Vec3d>();
            private readonly List<double> along = new List<double>();
            private readonly double halfWidth, depth, total, bodyRadius;
            private readonly List<(double centre, double half)> bridges = new List<(double, double)>();

            public Rille(ReliefSpec s, double bodyRadius)
            {
                this.bodyRadius = bodyRadius;
                halfWidth = s.Width * 0.5;
                depth = s.Depth;
                foreach (var (lat, lon) in s.Path)
                {
                    path.Add(BodyFrame.Direction(lat, lon));
                }

                double sum = 0;
                along.Add(0);
                for (int i = 1; i < path.Count; i++)
                {
                    sum += Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(path[i - 1], path[i])))) * bodyRadius;
                    along.Add(sum);
                }

                total = sum;
                foreach (var (at, length) in s.Bridges)
                {
                    bridges.Add((at * total, length * 0.5));
                }

                // Centre and reach: the middle of the path, out to its farthest point plus the trench's width.
                Vec3d mid = new Vec3d(0, 0, 0);
                foreach (Vec3d p in path)
                {
                    mid = mid + p;
                }

                Centre = path.Count > 0 ? mid.Normalized : new Vec3d(0, 0, 1);
                double reach = 0;
                foreach (Vec3d p in path)
                {
                    reach = Math.Max(reach, Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(p, Centre)))));
                }

                CosReach = Math.Cos(reach + 2 * s.Width / bodyRadius);
            }

            public override double Delta(Vec3d dir)
            {
                if (path.Count < 2)
                {
                    return 0;
                }

                // Nearest point on the path (segments in the tangent plane at their start: fine at trench scale).
                double best = double.MaxValue, bestAlong = 0;
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    Vec3d a = path[i], b = path[i + 1];
                    Vec3d ab = b - a, ap = dir - a;
                    double t = Math.Max(0, Math.Min(1, Vec3d.Dot(ap, ab) / Math.Max(1e-18, ab.LengthSquared)));
                    double dist = (ap - ab * t).Length * bodyRadius;
                    if (dist < best)
                    {
                        best = dist;
                        bestAlong = along[i] + t * (along[i + 1] - along[i]);
                    }
                }

                if (best >= halfWidth)
                {
                    return 0;
                }

                // U profile: flat over the inner half, walls rising smoothly to the rim.
                double x = best / halfWidth;
                double profile = x < 0.5 ? 1 : 1 - Smooth((x - 0.5) / 0.5);

                // Ends taper over one width; bridges keep the ground, ending in a steep face (a smooth drop over an eighth of
                // the width each side: the tube comes out of a cliff, not across a long slope). [Claude, revisar]
                double ends = Math.Min(Smooth(bestAlong / (2 * halfWidth)), Smooth((total - bestAlong) / (2 * halfWidth)));
                double open = 1;
                foreach (var (centre, half) in bridges)
                {
                    double off = Math.Abs(bestAlong - centre) - half;
                    open = Math.Min(open, Smooth(off / (halfWidth * 0.25)));
                }

                return -depth * profile * ends * open;
            }

            private static double Smooth(double x)
            {
                x = Math.Max(0, Math.Min(1, x));
                return x * x * (3 - 2 * x);
            }
        }
    }
}
