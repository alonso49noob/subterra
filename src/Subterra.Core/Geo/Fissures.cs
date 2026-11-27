using System;
using System.Collections.Generic;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Geo
{
    /// <summary>
    /// The lines of a <see cref="ReliefKind.Fissures"/> relief (Enceladus's tiger stripes): <see cref="ReliefSpec.Count"/>
    /// parallel fissures <see cref="ReliefSpec.Spacing"/> apart, 2 × <see cref="ReliefSpec.Radius"/> long, across the
    /// heading, each bowed a little and of its own length. Shared by the terrain (ReliefField) and the jets drawn along them.
    /// </summary>
    public static class Fissures
    {
        /// <summary>Points along each fissure.</summary>
        public const int Points = 13;

        /// <summary>Each fissure as unit directions from the body's centre, in order along it.</summary>
        public static List<Vec3d[]> Lines(ReliefSpec s, double bodyRadius)
        {
            var lines = new List<Vec3d[]>();
            Vec3d centre = BodyFrame.Direction(s.Lat, s.Lon);
            Vec3d north = (new Vec3d(0, 1, 0) - centre * centre.Y);
            north = north.LengthSquared < 1e-12 ? new Vec3d(1, 0, 0) : north.Normalized;
            Vec3d east = Vec3d.Cross(north, centre).Normalized;
            double h = s.Heading * Math.PI / 180;
            Vec3d along = north * Math.Cos(h) + east * Math.Sin(h);
            Vec3d across = Vec3d.Cross(centre, along).Normalized;
            int count = Math.Max(1, s.Count);
            var rng = new Rng(Hash.Of(s.Name ?? "fissures"));
            for (int k = 0; k < count; k++)
            {
                double offset = (k - (count - 1) * 0.5) * s.Spacing;
                // Inner stripes longest; each bowed one way by up to 8 % of its length, shifted a little along.
                double half = s.Radius * (1 - 0.25 * Math.Abs(k - (count - 1) * 0.5) / Math.Max(1, count * 0.5)) * rng.Range(0.85, 1.05);
                double bow = half * rng.Range(0.03, 0.08) * (rng.NextDouble() < 0.5 ? -1 : 1);
                double shift = s.Spacing * rng.Range(-0.3, 0.3);
                var line = new Vec3d[Points];
                for (int i = 0; i < Points; i++)
                {
                    double t = (double)i / (Points - 1) * 2 - 1;
                    double a = shift + t * half, c = offset + bow * (1 - t * t);
                    line[i] = (centre + (along * a + across * c) / bodyRadius).Normalized;
                }

                lines.Add(line);
            }

            return lines;
        }

        /// <summary>Distance (m) from a direction to the nearest point of a line, and where along it (0 to 1).</summary>
        public static double Distance(Vec3d[] line, Vec3d dir, double bodyRadius, out double share)
        {
            double best = double.MaxValue, total = 0, bestAlong = 0;
            var lengths = new double[line.Length];
            for (int i = 1; i < line.Length; i++)
            {
                total += (line[i] - line[i - 1]).Length;
                lengths[i] = total;
            }

            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vec3d a = line[i], b = line[i + 1];
                Vec3d ab = b - a, ap = dir - a;
                double t = Math.Max(0, Math.Min(1, Vec3d.Dot(ap, ab) / Math.Max(1e-18, ab.LengthSquared)));
                double dist = (ap - ab * t).Length;
                if (dist < best)
                {
                    best = dist;
                    bestAlong = lengths[i] + t * (lengths[i + 1] - lengths[i]);
                }
            }

            share = total > 0 ? bestAlong / total : 0;
            return best * bodyRadius;
        }
    }
}
