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
                Vec3 up = p.Up.ToVec3();
                if (node.Feature == NodeFeature.Skylight)
                {
                    Vec3d bottom = p.FloorCenter + p.Up * (p.Height - 2);
                    double length = p.SurfaceRadius + ShaftAboveSurface - bottom.Length;
                    var shaft = new Shaft(Local(bottom), up, (float)node.SkylightRadius, (float)length)
                    {
                        Roughness = (float)(0.08 * node.SkylightRadius),
                        NoiseScale = (float)Math.Max(4, 0.5 * node.SkylightRadius),
                        Source = i,
                    };
                    prims.Add(shaft);
                    skylights.Add(new Skylight
                    {
                        SurfaceCenter = Local(p.Up * p.SurfaceRadius),
                        Up = up,
                        Radius = (float)node.SkylightRadius,
                        Lat = node.Lat,
                        Lon = node.Lon,
                        SurfaceRadius = p.SurfaceRadius,
                        ShaftBottom = Local(bottom),
                        Index = skylights.Count,
                    });
                }
                else if (node.Feature == NodeFeature.Portal)
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
