using System;
using System.Collections.Generic;
using System.Globalization;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Tubes
{
    /// <summary>
    /// Centre lines of tubes: centripetal Catmull-Rom through the nodes (on the unit sphere), lateral
    /// wander from fBm, subdivision to at most <see cref="MaxStep"/> metres, and the roof rule.
    /// <see cref="SamplePoints"/> lists exactly the latitudes/longitudes <see cref="Build"/> reads,
    /// so the heights can be sampled beforehand on KSP's main thread.
    /// </summary>
    public static class TubePath
    {
        public const double MaxStep = 500;

        // Terrain can rise above the body's mean radius and the floor height changes between samples;
        // the angular step is planned for a radius this much larger so 3D steps stay under MaxStep.
        private const double StepSafety = 1.15;
        private const int LengthSamples = 64;

        /// <summary>Minimum roof thickness for a tube of this width: max(8 m, 0.08 × width).</summary>
        public static double MinRoof(double width) => Math.Max(8.0, 0.08 * width);

        public static IReadOnlyList<(double lat, double lon)> SamplePoints(SiteSpec site, double bodyRadius)
        {
            var points = new List<(double lat, double lon)>();
            for (int e = 0; e < site.Edges.Count; e++)
            {
                foreach (Vec3d dir in Directions(site, e, bodyRadius))
                {
                    BodyFrame.ToLatLon(dir, out double lat, out double lon);
                    points.Add((lat, lon));
                }
            }

            if (site.Edges.Count == 0)
            {
                foreach (NodeSpec node in site.Nodes)
                {
                    points.Add((node.Lat, node.Lon));
                }
            }

            return points;
        }

        public static List<PathPoint> Build(SiteSpec site, EdgeSpec edge, double bodyRadius, IHeightSampler heights, List<string> warnings)
        {
            int edgeIndex = site.Edges.IndexOf(edge);
            if (edgeIndex < 0)
            {
                throw new ArgumentException("edge is not part of the site");
            }

            NodeSpec from = site.Nodes[edge.From];
            NodeSpec to = site.Nodes[edge.To];
            double depthFrom = RoofDepth(site, edge.From, warnings);
            double depthTo = RoofDepth(site, edge.To, warnings);

            List<Vec3d> dirs = Directions(site, edgeIndex, bodyRadius);
            var points = new List<PathPoint>(dirs.Count);
            for (int i = 0; i < dirs.Count; i++)
            {
                double u = dirs.Count == 1 ? 0 : (double)i / (dirs.Count - 1);
                BodyFrame.ToLatLon(dirs[i], out double lat, out double lon);
                double surface = heights.SurfaceRadius(lat, lon);
                double depth = Lerp(depthFrom, depthTo, u);
                double width = Lerp(from.Width, to.Width, u);
                double height = Lerp(from.Height, to.Height, u);
                Vec3d up = BodyFrame.Direction(lat, lon);
                points.Add(new PathPoint
                {
                    Lat = lat,
                    Lon = lon,
                    Depth = depth,
                    Width = width,
                    Height = height,
                    Up = up,
                    SurfaceRadius = surface,
                    FloorCenter = up * (surface - depth - height),
                });
            }

            return points;
        }

        /// <summary>Single-node sites (a lone skylight or chamber) have no edges: their one path point.</summary>
        public static PathPoint NodePoint(SiteSpec site, int nodeIndex, IHeightSampler heights, List<string> warnings)
        {
            NodeSpec node = site.Nodes[nodeIndex];
            double depth = RoofDepth(site, nodeIndex, warnings);
            double surface = heights.SurfaceRadius(node.Lat, node.Lon);
            Vec3d up = BodyFrame.Direction(node.Lat, node.Lon);
            return new PathPoint
            {
                Lat = node.Lat,
                Lon = node.Lon,
                Depth = depth,
                Width = node.Width,
                Height = node.Height,
                Up = up,
                SurfaceRadius = surface,
                FloorCenter = up * (surface - depth - node.Height),
            };
        }

        /// <summary>The node's roof depth after the roof rule (warns once per node of a named site).</summary>
        public static double RoofDepth(SiteSpec site, int nodeIndex, List<string> warnings)
        {
            NodeSpec node = site.Nodes[nodeIndex];
            double min = MinRoof(node.Width);
            if (node.Depth >= min)
            {
                return node.Depth;
            }

            if (site.IsNamed && warnings != null)
            {
                string message = $"{site.Name}: roof too thin at node {nodeIndex}, depth raised to {min.ToString("0.##", CultureInfo.InvariantCulture)} m";
                if (!warnings.Contains(message))
                {
                    warnings.Add(message);
                }
            }

            return min;
        }

        // Unit directions along edge e, From to To inclusive, with the lateral wander applied.
        private static List<Vec3d> Directions(SiteSpec site, int e, double bodyRadius)
        {
            EdgeSpec edge = site.Edges[e];
            Vec3d p1 = NodeDir(site, edge.From);
            Vec3d p2 = NodeDir(site, edge.To);
            // Tangents continue into the neighbouring edges; a free end mirrors the edge.
            int before = Other(site, edge.From, edge.To);
            int after = Other(site, edge.To, edge.From);
            Vec3d p0 = before >= 0 ? NodeDir(site, before) : p1 * 2 - p2;
            Vec3d p3 = after >= 0 ? NodeDir(site, after) : p2 * 2 - p1;

            // Angular length of the spline (fine polyline) sets the number of segments.
            double chordAngle = Angle(p1, p2);
            double sinuosity = edge.Sinuosity;
            ulong seed = Hash.Combine(Hash.Of(site.Name), (ulong)e);
            double length = 0;
            Vec3d previous = Point(p0, p1, p2, p3, 0, sinuosity, chordAngle, seed);
            for (int i = 1; i <= LengthSamples; i++)
            {
                Vec3d current = Point(p0, p1, p2, p3, (double)i / LengthSamples, sinuosity, chordAngle, seed);
                length += Angle(previous, current);
                previous = current;
            }

            double maxAngle = MaxStep / (bodyRadius * StepSafety);
            int segments = Math.Max(1, (int)Math.Ceiling(length / maxAngle));
            var dirs = new List<Vec3d>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                dirs.Add(Point(p0, p1, p2, p3, (double)i / segments, sinuosity, chordAngle, seed));
            }

            return dirs;
        }

        private static Vec3d Point(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3, double u, double sinuosity, double chordAngle, ulong seed)
        {
            Vec3d dir = CatmullRom(p0, p1, p2, p3, u).Normalized;
            if (sinuosity <= 0 || u <= 0 || u >= 1)
            {
                return dir;
            }

            Vec3d ahead = CatmullRom(p0, p1, p2, p3, Math.Min(1, u + 1e-3)).Normalized;
            Vec3d behind = CatmullRom(p0, p1, p2, p3, Math.Max(0, u - 1e-3)).Normalized;
            Vec3d side = Vec3d.Cross(dir, ahead - behind).Normalized;
            double noise = Noise.Fbm(new Vec3((float)(u * 3.0), 0.37f, 0.71f), seed, 3);
            double offset = sinuosity * chordAngle * 0.15 * noise * Math.Sin(Math.PI * u);
            return (dir + side * offset).Normalized;
        }

        // Centripetal Catmull-Rom (Barry–Goldman), u in [0, 1] between p1 and p2.
        private static Vec3d CatmullRom(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3, double u)
        {
            double t0 = 0;
            double t1 = t0 + Knot(p0, p1);
            double t2 = t1 + Knot(p1, p2);
            double t3 = t2 + Knot(p2, p3);
            double t = t1 + (t2 - t1) * u;
            Vec3d a1 = Blend(p0, p1, t0, t1, t);
            Vec3d a2 = Blend(p1, p2, t1, t2, t);
            Vec3d a3 = Blend(p2, p3, t2, t3, t);
            Vec3d b1 = Blend(a1, a2, t0, t2, t);
            Vec3d b2 = Blend(a2, a3, t1, t3, t);
            return Blend(b1, b2, t1, t2, t);
        }

        private static double Knot(Vec3d a, Vec3d b) => Math.Max(Math.Sqrt((b - a).Length), 1e-9);

        private static Vec3d Blend(Vec3d a, Vec3d b, double ta, double tb, double t) =>
            a * ((tb - t) / (tb - ta)) + b * ((t - ta) / (tb - ta));

        private static double Angle(Vec3d a, Vec3d b) => Math.Atan2(Vec3d.Cross(a, b).Length, Vec3d.Dot(a, b));

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static Vec3d NodeDir(SiteSpec site, int index) => BodyFrame.Direction(site.Nodes[index].Lat, site.Nodes[index].Lon);

        // The node joined to `at` by an edge other than the one to `exclude`, or -1.
        private static int Other(SiteSpec site, int at, int exclude)
        {
            foreach (EdgeSpec e in site.Edges)
            {
                if (e.From == at && e.To != exclude)
                {
                    return e.To;
                }

                if (e.To == at && e.From != exclude)
                {
                    return e.From;
                }
            }

            return -1;
        }
    }
}
