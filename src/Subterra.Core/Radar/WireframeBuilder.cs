using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;

namespace Subterra.Core.Radar
{
    /// <summary>
    /// The 3D outline of the voids the radar has detected: line segments in the network's local frame. Tubes get
    /// elliptical arch rings along their length plus their axis, chambers three ellipses, shafts circles along their
    /// length plus the axis. Pure geometry (no Unity); the plugin transforms and draws the lines.
    /// </summary>
    public static class WireframeBuilder
    {
        /// <summary>Segments of every ring and ellipse.</summary>
        public const int RingSegments = 24;

        /// <summary>Shortest spacing (m) between two rings of a tube or shaft.</summary>
        public const float MinRingSpacing = 10f;

        /// <summary>
        /// The lines of the primitives of <paramref name="net"/> for which <paramref name="include"/> (by primitive
        /// index) is true, in the network's local frame.
        /// </summary>
        public static List<(Vec3 a, Vec3 b)> Build(TubeNetwork net, Func<int, bool> include)
        {
            if (net == null)
            {
                throw new ArgumentNullException(nameof(net));
            }

            if (include == null)
            {
                throw new ArgumentNullException(nameof(include));
            }

            var lines = new List<(Vec3 a, Vec3 b)>();
            for (int i = 0; i < net.Primitives.Count; i++)
            {
                if (!include(i))
                {
                    continue;
                }

                switch (net.Primitives[i])
                {
                    case TubeSegment tube:
                        AddTube(lines, net, tube);
                        break;
                    case Chamber chamber:
                        AddChamber(lines, chamber);
                        break;
                    case Shaft shaft:
                        AddShaft(lines, shaft);
                        break;
                }
            }

            return lines;
        }

        // Rings at 0, step, 2*step ... along A to B (step = max(10 m, width)), one more at B if the last one falls short
        // of it, and the axis. Each ring is the half ellipse of the arch (width x height) closed by the floor line.
        // The side benches of a tube are not drawn (decidido por Claude, revisar): the outline is the arch.
        private static void AddTube(List<(Vec3 a, Vec3 b)> lines, TubeNetwork net, TubeSegment tube)
        {
            Vec3 ab = tube.B - tube.A;
            float length = ab.Length;
            float step = Math.Max(MinRingSpacing, tube.Width);
            Vec3 axis = length > 0 ? ab / length : new Vec3(1, 0, 0);
            float halfWidth = tube.Width * 0.5f;
            foreach (float along in Stations(length, step))
            {
                Vec3 centre = tube.A + axis * along;
                Vec3 up = net.UpAt(centre);
                up = up - axis * Vec3.Dot(up, axis);
                up = up.Length > 1e-6f ? up.Normalized : Perpendicular(axis);
                Vec3 side = Vec3.Cross(up, axis).Normalized;
                Arch(lines, centre, side, up, halfWidth, tube.Height);
            }

            lines.Add((tube.A, tube.B));
        }

        // Three ellipses of the dome: the one on the floor and two upright ones at 90 degrees to each other.
        private static void AddChamber(List<(Vec3 a, Vec3 b)> lines, Chamber chamber)
        {
            Vec3 up = chamber.Up;
            Vec3 e1 = Perpendicular(up);
            Vec3 e2 = Vec3.Cross(up, e1).Normalized;
            Ellipse(lines, chamber.FloorCenter, e1, e2, chamber.Radius, chamber.Radius);
            Arch(lines, chamber.FloorCenter, e1, up, chamber.Radius, chamber.Height);
            Arch(lines, chamber.FloorCenter, e2, up, chamber.Radius, chamber.Height);
        }

        // Circles every max(10 m, 2 * radius) along the shaft, one more at the top if the last falls short, and the axis.
        private static void AddShaft(List<(Vec3 a, Vec3 b)> lines, Shaft shaft)
        {
            Vec3 up = shaft.Up;
            Vec3 e1 = Perpendicular(up);
            Vec3 e2 = Vec3.Cross(up, e1).Normalized;
            float step = Math.Max(MinRingSpacing, 2f * shaft.Radius);
            foreach (float along in Stations(shaft.Length, step))
            {
                Vec3 centre = shaft.BottomCenter + up * along;
                Vec3 previous = default;
                for (int k = 0; k <= RingSegments; k++)
                {
                    double angle = 2 * Math.PI * k / RingSegments;
                    Vec3 dir = e1 * (float)Math.Cos(angle) + e2 * (float)Math.Sin(angle);
                    Vec3 p = centre + dir * ShaftRingRadius(shaft, dir);
                    if (k > 0)
                    {
                        lines.Add((previous, p));
                    }

                    previous = p;
                }
            }

            lines.Add((shaft.BottomCenter, shaft.BottomCenter + up * shaft.Length));
        }

        // The radius of a shaft ring in a direction (local frame, perpendicular to the shaft).
        // after merging main: use Shaft.RimRadius(direction)
        private static float ShaftRingRadius(Shaft shaft, Vec3 dirLocal) => shaft.Radius;

        // 0, step, 2*step ... up to length, plus length itself if the last station is more than a hundredth of a step short.
        private static IEnumerable<float> Stations(float length, float step)
        {
            int count = (int)Math.Floor(length / step + 1e-3f);
            for (int k = 0; k <= count; k++)
            {
                yield return k * step;
            }

            if (length - count * step > 0.01f * step)
            {
                yield return length;
            }
        }

        // A half ellipse from centre + side*hw through centre + up*h to centre - side*hw, closed by the floor line:
        // RingSegments segments in all.
        private static void Arch(List<(Vec3 a, Vec3 b)> lines, Vec3 centre, Vec3 side, Vec3 up, float hw, float height)
        {
            int arcSegments = RingSegments - 1;
            Vec3 first = centre + side * hw;
            Vec3 previous = first;
            for (int k = 1; k <= arcSegments; k++)
            {
                double angle = Math.PI * k / arcSegments;
                Vec3 p = centre + side * (hw * (float)Math.Cos(angle)) + up * (height * (float)Math.Sin(angle));
                lines.Add((previous, p));
                previous = p;
            }

            lines.Add((previous, first));
        }

        private static void Ellipse(List<(Vec3 a, Vec3 b)> lines, Vec3 centre, Vec3 e1, Vec3 e2, float r1, float r2)
        {
            Vec3 previous = centre + e1 * r1;
            for (int k = 1; k <= RingSegments; k++)
            {
                double angle = 2 * Math.PI * k / RingSegments;
                Vec3 p = centre + e1 * (r1 * (float)Math.Cos(angle)) + e2 * (r2 * (float)Math.Sin(angle));
                lines.Add((previous, p));
                previous = p;
            }
        }

        private static Vec3 Perpendicular(Vec3 v)
        {
            Vec3 helper = Math.Abs(v.X) < 0.9f ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
            return Vec3.Cross(v, helper).Normalized;
        }
    }
}
