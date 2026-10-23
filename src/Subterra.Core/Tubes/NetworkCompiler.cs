using System;
using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;

namespace Subterra.Core.Tubes
{
    /// <summary>Turns a site graph into a <see cref="TubeNetwork"/>: paths → primitives → SDF, skylights, zones.</summary>
    public static class NetworkCompiler
    {
        /// <summary>Tubes narrower than this are branches.</summary>
        public const double BranchWidth = 50;

        /// <summary>|latitude| from which tubes far from any skylight are frozen vaults.</summary>
        public const double FrozenLatitude = 75;

        /// <summary>Path distance from the nearest skylight beyond which a polar tube is a frozen vault.</summary>
        public const double FrozenDistance = 500;

        /// <summary>Largest wall noise amplitude of a tube (m).</summary>
        public const double MaxRoughness = 4;

        /// <summary>The shaft rises this far above the terrain so rays and meshes clear the surface.</summary>
        public const float ShaftAboveSurface = 20f;

        /// <summary>Every latitude/longitude <see cref="Compile"/> samples (heights for all, biomes not needed).</summary>
        public static IEnumerable<(double lat, double lon)> RequiredSamples(SiteSpec site, double bodyRadius)
        {
            foreach (var point in TubePath.SamplePoints(site, bodyRadius))
            {
                yield return point;
            }

            foreach (NodeSpec node in site.Nodes)
            {
                yield return (node.Lat, node.Lon);
                if (node.Feature == NodeFeature.Skylight && node.Ramp)
                {
                    yield return RampTop(site, node, bodyRadius);
                }

                if (node.Feature == NodeFeature.Portal)
                {
                    foreach (var point in PortalSamples(node, bodyRadius))
                    {
                        yield return point;
                    }
                }
            }
        }

        public static TubeNetwork Compile(SiteSpec site, string body, double bodyRadius, IHeightSampler heights, List<string> warnings,
            Func<double, double, double> relief = null)
        {
            string id = site.IsNamed ? "site:" + site.Name : site.Name;
            var nodePoints = new PathPoint[site.Nodes.Count];
            for (int i = 0; i < site.Nodes.Count; i++)
            {
                nodePoints[i] = TubePath.NodePoint(site, i, heights, warnings, bodyRadius, relief);
            }

            Vec3d anchor = nodePoints[0].FloorCenter;
            Vec3 Local(Vec3d bodyPos) => (bodyPos - anchor).ToVec3();

            var paths = new List<List<PathPoint>>();
            foreach (EdgeSpec edge in site.Edges)
            {
                paths.Add(TubePath.Build(site, edge, bodyRadius, heights, warnings, relief));
            }

            int[] groups = ChainGroups(site);
            double[] skylightDistance = NodeDistanceToSkylight(site, paths);
            var prims = new List<Primitive>();
            var surfaceDirs = new List<Vec3d>();
            var surfaceRadii = new List<double>();

            for (int e = 0; e < site.Edges.Count; e++)
            {
                EdgeSpec edge = site.Edges[e];
                List<PathPoint> pts = paths[e];
                double edgeLength = PathLength(pts);
                double along = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    surfaceDirs.Add(pts[i].Up);
                    surfaceRadii.Add(pts[i].SurfaceRadius);
                    if (i == 0)
                    {
                        continue;
                    }

                    PathPoint a = pts[i - 1], b = pts[i];
                    double step = Vec3d.Distance(a.FloorCenter, b.FloorCenter);
                    double startAlong = along;
                    double mid = along + step * 0.5;
                    along += step;
                    double toSkylight = Math.Min(skylightDistance[edge.From] + mid, skylightDistance[edge.To] + (edgeLength - mid));
                    double width = Math.Min(a.Width, b.Width);
                    double lat = (a.Lat + b.Lat) * 0.5;
                    ZoneKind zone = width < BranchWidth ? ZoneKind.TubeBranch
                        : Math.Abs(lat) >= FrozenLatitude && toSkylight >= FrozenDistance ? ZoneKind.FrozenVault
                        : ZoneKind.TubeGallery;

                    var segment = new TubeSegment(Local(a.FloorCenter), Local(b.FloorCenter), a.Up.ToVec3(), b.Up.ToVec3(),
                        (float)a.Width, (float)b.Width, (float)a.Height, (float)b.Height, (float)edge.BenchHeight, zone)
                    {
                        // Wall noise grows with the tube but not past MaxRoughness: in wide tubes a bigger amplitude left
                        // thin floating slabs of rock under the roof.
                        Roughness = (float)Math.Min(MaxRoughness, edge.Roughness * width),
                        NoiseScale = (float)Math.Max(4, 0.3 * width),
                        Group = groups[e],
                        Source = e,
                        AlongStart = (float)startAlong,
                    };
                    prims.Add(segment);
                }
            }

            var skylights = new List<Skylight>();
            for (int i = 0; i < site.Nodes.Count; i++)
            {
                NodeSpec node = site.Nodes[i];
                PathPoint p = nodePoints[i];
                surfaceDirs.Add(p.Up);
                surfaceRadii.Add(p.SurfaceRadius);
                if (node.Feature == NodeFeature.Portal)
                {
                    foreach (var (lat, lon) in PortalSamples(node, bodyRadius))
                    {
                        surfaceDirs.Add(BodyFrame.Direction(lat, lon));
                        surfaceRadii.Add(heights.SurfaceRadius(lat, lon));
                    }
                }

                Vec3 up = p.Up.ToVec3();
                if (node.Feature == NodeFeature.Skylight)
                {
                    Vec3d bottom = p.FloorCenter + p.Up * (p.Height - 2);
                    double length = p.SurfaceRadius + ShaftAboveSurface - bottom.Length;
                    // A natural collapse: lobed rim and walls overhanging the cave below (a bell, wider still for a cenote);
                    // the noise fades out under the rim, where the ground is cut along the shaft's smooth shape (no rim ring).
                    var shaft = new Shaft(Local(bottom), up, (float)node.SkylightRadius, (float)length)
                    {
                        Roughness = (float)(0.08 * node.SkylightRadius),
                        NoiseScale = (float)Math.Max(4, 0.5 * node.SkylightRadius),
                        Source = i,
                        Flare = node.Cenote ? 1.8f : 1.3f,
                        Lobes = node.Cenote ? 0.22f : 0.15f,
                        LobeSeed = (float)(Hash.Of(id + "/" + i) % 1000) / 100f,
                        Ground = ShaftAboveSurface,
                    };
                    prims.Add(shaft);
                    skylights.Add(new Skylight
                    {
                        SurfaceCenter = Local(p.Up * p.SurfaceRadius),
                        Up = up,
                        Radius = (float)(1.3 * node.SkylightRadius),
                        Lat = node.Lat,
                        Lon = node.Lon,
                        SurfaceRadius = p.SurfaceRadius,
                        ShaftBottom = Local(bottom),
                        Index = skylights.Count,
                        Smooth = true,
                        Natural = true,
                        Shaft = shaft,
                    });
                }
                if (node.Feature == NodeFeature.Skylight && node.Ramp)
                {
                    AddRamp(site, i, p, heights, bodyRadius, prims, skylights, Local);
                }

                if (node.Feature == NodeFeature.Portal)
                {
                    skylights.Add(new Skylight
                    {
                        SurfaceCenter = Local(p.Up * p.SurfaceRadius),
                        Up = up,
                        Radius = (float)Math.Max(node.SkylightRadius, node.Width),
                        Lat = node.Lat,
                        Lon = node.Lon,
                        SurfaceRadius = p.SurfaceRadius,
                        ShaftBottom = Local(p.FloorCenter),
                        Index = skylights.Count,
                        Smooth = true,
                    });
                }
                else if (node.Feature == NodeFeature.Geode)
                {
                    // Smooth, round walls (a geode is a bubble in the rock), its own zone for crystals and science.
                    prims.Add(new Chamber(Local(p.FloorCenter), up, (float)node.Width, (float)Math.Max(node.Height, 0.6 * node.Width))
                    {
                        Roughness = (float)(0.02 * node.Width),
                        NoiseScale = (float)Math.Max(4, 0.3 * node.Width),
                        Zone = ZoneKind.Geode,
                        Source = i,
                    });
                }
                else if (node.Feature == NodeFeature.Chamber)
                {
                    prims.Add(new Chamber(Local(p.FloorCenter), up, (float)node.Width, (float)(1.2 * node.Height))
                    {
                        Roughness = (float)Math.Min(MaxRoughness * 1.5, 0.05 * node.Width),
                        NoiseScale = (float)Math.Max(4, 0.3 * node.Width),
                        Source = i,
                    });
                }
            }

            if (prims.Count == 0)
            {
                warnings?.Add($"{site.Name}: no tube, skylight or chamber; network is empty");
            }

            var sdf = new NetworkSdf(prims, new SpatialGrid(prims), Hash.Of(id));
            return new TubeNetwork(id, body, site.IsNamed ? site.Name : null, site.DisplayName, anchor, prims, sdf, skylights,
                surfaceDirs.ToArray(), surfaceRadii.ToArray(), nodePoints, site);
        }

        /// <summary>Spacing (m) of the terrain samples around a portal.</summary>
        public const double PortalSampleSpacing = 5;

        // The ground around a portal, every few metres: where a tube comes out of a steep slope, the cave's mesh is
        // clipped along the real slope (with the path's few samples the clip followed their nearest-sample cells, and
        // the mouth looked rectangular from inside).
        internal static IEnumerable<(double lat, double lon)> PortalSamples(NodeSpec node, double bodyRadius)
        {
            Vec3d at = BodyFrame.Direction(node.Lat, node.Lon);
            Vec3d east = Vec3d.Cross(new Vec3d(0, 1, 0), at);
            east = east.LengthSquared < 1e-9 ? new Vec3d(1, 0, 0) : east.Normalized;
            Vec3d north = Vec3d.Cross(at, east).Normalized;
            double half = 0.6 * Math.Max(node.Width, node.Height) + 15;
            int n = (int)Math.Ceiling(half / PortalSampleSpacing);
            for (int i = -n; i <= n; i++)
            {
                for (int j = -n; j <= n; j++)
                {
                    Vec3d p = (at + (east * (i * PortalSampleSpacing) + north * (j * PortalSampleSpacing)) / bodyRadius).Normalized;
                    BodyFrame.ToLatLon(p, out double lat, out double lon);
                    yield return (lat, lon);
                }
            }
        }

        /// <summary>Slope of a skylight's ramp (degrees): a rover climbs it, as it does a dug tunnel's.</summary>
        public const double RampSlope = 18;

        /// <summary>Width and height (m) of a ramp passage.</summary>
        public const float RampWidth = 14, RampHeight = 10;

        // Where a skylight's ramp reaches the ground: out from the node across its tube (square to the way to its first
        // neighbour, the side picked per node), as far as the slope needs to climb the node's depth and height.
        public static (double lat, double lon) RampTop(SiteSpec site, NodeSpec node, double bodyRadius)
        {
            Vec3d at = BodyFrame.Direction(node.Lat, node.Lon);
            Vec3d side = RampDirection(site, node, at);
            double run = (node.Depth + node.Height + 2) / Math.Tan(RampSlope * Math.PI / 180) + node.Width * 0.5;
            Vec3d top = (at + side * (run / bodyRadius)).Normalized;
            BodyFrame.ToLatLon(top, out double lat, out double lon);
            return (lat, lon);
        }

        private static Vec3d RampDirection(SiteSpec site, NodeSpec node, Vec3d at)
        {
            int index = site.Nodes.IndexOf(node);
            Vec3d towards = new Vec3d(0, 1, 0);
            foreach (EdgeSpec e in site.Edges)
            {
                if (e.From == index || e.To == index)
                {
                    NodeSpec other = site.Nodes[e.From == index ? e.To : e.From];
                    towards = BodyFrame.Direction(other.Lat, other.Lon) - at;
                    break;
                }
            }

            towards = (towards - at * Vec3d.Dot(towards, at)).Normalized;
            Vec3d side = Vec3d.Cross(at, towards).Normalized;
            if (side.LengthSquared < 0.5)
            {
                side = Vec3d.Cross(at, new Vec3d(1, 0, 0)).Normalized;
            }

            return Hash.Of((site.Name ?? "") + "/" + index) % 2 == 0 ? side : side * -1;
        }

        // A ramp: a straight sloping passage from the node's floor up through the rock to the ground beside the skylight,
        // opened there like a dug tunnel's mouth (the ground cut along its shape): a way in that is not a drop.
        private static void AddRamp(SiteSpec site, int i, PathPoint p, IHeightSampler heights, double bodyRadius,
            List<Primitive> prims, List<Skylight> skylights, Func<Vec3d, Vec3> local)
        {
            NodeSpec node = site.Nodes[i];
            var (lat, lon) = RampTop(site, node, bodyRadius);
            Vec3d topUp = BodyFrame.Direction(lat, lon);
            double surface = heights.SurfaceRadius(lat, lon);
            Vec3d top = topUp * (surface + 3);
            Vec3d bottom = p.FloorCenter;
            prims.Add(new TubeSegment(local(bottom), local(top), p.Up.ToVec3(), topUp.ToVec3(), RampWidth, RampWidth, RampHeight, RampHeight, 0, ZoneKind.TubeBranch)
            {
                Roughness = 0.6f,
                NoiseScale = 6,
                Source = i,
            });
            skylights.Add(new Skylight
            {
                SurfaceCenter = local(topUp * surface),
                Up = topUp.ToVec3(),
                Radius = RampWidth,
                Lat = lat,
                Lon = lon,
                SurfaceRadius = surface,
                ShaftBottom = local(bottom),
                Index = skylights.Count,
                Smooth = true,
            });
        }

        // Edges joined at nodes of degree 2 form one chain (one group: plain min between their pieces).
        private static int[] ChainGroups(SiteSpec site)
        {
            int n = site.Edges.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = i;
            }

            int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
            var degree = new int[site.Nodes.Count];
            var firstEdge = new int[site.Nodes.Count];
            for (int i = 0; i < firstEdge.Length; i++)
            {
                firstEdge[i] = -1;
            }

            for (int e = 0; e < n; e++)
            {
                foreach (int node in new[] { site.Edges[e].From, site.Edges[e].To })
                {
                    degree[node]++;
                    if (firstEdge[node] < 0)
                    {
                        firstEdge[node] = e;
                    }
                }
            }

            for (int e = 0; e < n; e++)
            {
                foreach (int node in new[] { site.Edges[e].From, site.Edges[e].To })
                {
                    if (degree[node] == 2 && firstEdge[node] != e)
                    {
                        parent[Find(e)] = Find(firstEdge[node]);
                    }
                }
            }

            var groups = new int[n];
            for (int e = 0; e < n; e++)
            {
                groups[e] = Find(e);
            }

            return groups;
        }

        // Shortest path length along the tubes from each node to a skylight node (∞ if none).
        private static double[] NodeDistanceToSkylight(SiteSpec site, List<List<PathPoint>> paths)
        {
            int n = site.Nodes.Count;
            var dist = new double[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = site.Nodes[i].Feature == NodeFeature.Skylight ? 0 : double.PositiveInfinity;
            }

            // Bellman-Ford style relaxation; graphs are small.
            for (int iteration = 0; iteration < n; iteration++)
            {
                bool changed = false;
                for (int e = 0; e < site.Edges.Count; e++)
                {
                    double length = PathLength(paths[e]);
                    int a = site.Edges[e].From, b = site.Edges[e].To;
                    if (dist[a] + length < dist[b])
                    {
                        dist[b] = dist[a] + length;
                        changed = true;
                    }

                    if (dist[b] + length < dist[a])
                    {
                        dist[a] = dist[b] + length;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            return dist;
        }

        private static double PathLength(List<PathPoint> pts)
        {
            double length = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                length += Vec3d.Distance(pts[i - 1].FloorCenter, pts[i].FloorCenter);
            }

            return length;
        }
    }
}
