using System;
using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;

namespace Subterra.Core.Procedural
{
    /// <summary>
    /// Deterministic procedural networks for one cell of a <see cref="FieldSpec"/>: Poisson-disc origins in
    /// the allowed biomes, trunks that flow downhill like lava, a few branches (chambers at junctions) and
    /// skylights where the roof is thinnest. Same inputs → same networks; nothing is saved in the game.
    /// </summary>
    public static class FieldGenerator
    {
        public const int MaxNetworksPerCell = 10;
        public const double StepLength = 400;
        public const double OriginSpacing = 8000;
        public const double CellMargin = 10000;

        /// <summary>Longest trunk (m), whatever the FieldSpec says.</summary>
        public const double MaxLength = 100000;
        private const double BodySpecWidthMin = 20;

        /// <summary>
        /// The networks of every field of a body in one cell, in field order. The fields share the cap of
        /// <see cref="MaxNetworksPerCell"/> and the id counter, so ids stay unique (cell × 10 + k).
        /// </summary>
        public static List<SiteSpec> Generate(IEnumerable<FieldSpec> fields, string body, string idPrefix, CellGrid grid, CellId cell, IHeightSampler h, IBiomeSampler b)
        {
            var result = new List<SiteSpec>();
            int next = 0;
            foreach (FieldSpec f in fields)
            {
                if (next >= MaxNetworksPerCell)
                {
                    break;
                }

                GenerateField(f, body, idPrefix, grid, cell, h, b, ref next, result);
            }

            return result;
        }

        /// <summary>The networks of a single field in one cell (as the only field of its body).</summary>
        public static List<SiteSpec> Generate(FieldSpec f, string body, string idPrefix, CellGrid grid, CellId cell, IHeightSampler h, IBiomeSampler b) =>
            Generate(new[] { f }, body, idPrefix, grid, cell, h, b);

        // A hidden network: no skylight, the tube stays closed under the ground.
        private static void Hide(SiteSpec site)
        {
            foreach (NodeSpec node in site.Nodes)
            {
                if (node.Feature == NodeFeature.Skylight)
                {
                    node.Feature = NodeFeature.None;
                    node.SkylightRadius = 0;
                }
            }
        }

        // Adds the field's networks; `next` is the cell's next free network index (the id's last digit).
        private static void GenerateField(FieldSpec f, string body, string idPrefix, CellGrid grid, CellId cell, IHeightSampler h, IBiomeSampler b,
            ref int next, List<SiteSpec> result)
        {
            var rng = new Rng(Hash.Combine(Hash.Of(body), Hash.Of(f.Name), (ulong)cell.Index(grid.N), f.Seed));
            double radius = grid.BodyRadius;
            var allowedBiomes = new HashSet<string>(f.Biomes, StringComparer.OrdinalIgnoreCase);

            // Allowed samples inside the cell and the fraction of the cell they cover.
            var allowed = new List<Vec3d>();
            int inCell = 0;
            foreach (var (lat, lon) in grid.SampleGrid(cell, 64))
            {
                Vec3d dir = BodyFrame.Direction(lat, lon);
                if (grid.CellOf(dir) != cell)
                {
                    continue;
                }

                inCell++;
                if (allowedBiomes.Contains(b.Biome(lat, BodyFrame.WrapLon(lon)) ?? ""))
                {
                    allowed.Add(dir);
                }
            }

            if (inCell == 0 || allowed.Count == 0 || f.NetworksPer10k <= 0)
            {
                return;
            }

            double area = 4 * Math.PI * radius * radius / (6.0 * grid.N * grid.N);
            double expected = f.NetworksPer10k * area / 1e10 * allowed.Count / inCell;
            int count = (int)Math.Floor(expected);
            if (rng.NextDouble() < expected - count)
            {
                count++;
            }

            count = Math.Min(count, MaxNetworksPerCell - next);
            var origins = new List<Vec3d>();
            for (int k = 0; k < count; k++)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    Vec3d candidate = allowed[rng.Range(0, allowed.Count)];
                    bool free = true;
                    foreach (Vec3d o in origins)
                    {
                        if (BodyFrame.SurfaceDistance(o, candidate, radius) < OriginSpacing)
                        {
                            free = false;
                            break;
                        }
                    }

                    if (free)
                    {
                        origins.Add(candidate);
                        break;
                    }
                }
            }

            var rect = grid.GridRect(cell, CellMargin);
            var walker = new Walker(h, b, allowedBiomes, radius, rect);
            for (int k = 0; k < origins.Count; k++)
            {
                SiteSpec site = f.Style == FieldStyle.Karst ? KarstNetwork(f, rng, walker, origins[k]) : Network(f, rng, walker, origins[k]);
                if (site == null)
                {
                    continue;
                }

                site.Name = $"{idPrefix}-{cell.Index(grid.N) * 10 + next + k:D4}";
                site.Field = f.Name;
                if (f.HiddenChance > 0 && new Rng(Hash.Combine(Hash.Of(site.Name), Hash.Of("hidden"))).NextDouble() < f.HiddenChance)
                {
                    Hide(site);
                }
                result.Add(site);
            }

            next += origins.Count;
        }

        private static SiteSpec Network(FieldSpec f, Rng rng, Walker walker, Vec3d origin)
        {
            // The parser keeps ranges sane; a FieldSpec built in code may not be, so cap what loops depend on.
            double length = rng.Range(f.Length.Min, f.Length.Max);
            if (!(length <= MaxLength))
            {
                length = MaxLength;
            }

            double u = rng.NextDouble();
            double w = f.Width.Min + (f.Width.Max - f.Width.Min) * u * u * u;
            double trunkWidth = Math.Max(BodySpecWidthMin, w);
            double depth = rng.Range(f.RoofDepth.Min, f.RoofDepth.Max);

            var site = new SiteSpec { IsNamed = false };
            List<Vec3d> trunk = walker.Walk(origin, null, length, rng);
            if (trunk.Count < 2)
            {
                return null;
            }

            foreach (Vec3d p in trunk)
            {
                site.Nodes.Add(MakeNode(p, depth, trunkWidth, rng));
            }

            for (int i = 1; i < trunk.Count; i++)
            {
                site.Edges.Add(new EdgeSpec { From = i - 1, To = i });
            }

            // Skylights along the trunk, each the shallowest node within ±500 m of the target distance.
            var along = new double[trunk.Count];
            for (int i = 1; i < trunk.Count; i++)
            {
                along[i] = along[i - 1] + BodyFrame.SurfaceDistance(trunk[i - 1], trunk[i], walker.Radius);
            }

            double previous = 0;
            while (true)
            {
                double target = previous + rng.Range(f.SkylightSpacing.Min, f.SkylightSpacing.Max);
                if (!(target >= previous + StepLength))
                {
                    // Zero, negative or NaN spacing would never move on.
                    target = previous + StepLength;
                }

                if (target > along[trunk.Count - 1] + 500)
                {
                    break;
                }

                int best = -1;
                for (int i = 0; i < trunk.Count; i++)
                {
                    if (Math.Abs(along[i] - target) <= 500 && site.Nodes[i].Feature == NodeFeature.None
                        && (best < 0 || site.Nodes[i].Depth < site.Nodes[best].Depth))
                    {
                        best = i;
                    }
                }

                if (best < 0)
                {
                    previous = target;
                    continue;
                }

                NodeSpec node = site.Nodes[best];
                node.Feature = NodeFeature.Skylight;
                node.SkylightRadius = Math.Max(10, Math.Min(node.Width / 2, node.Width * rng.Range(0.3, 0.5)));
                previous = along[best];
            }

            // Branches from interior trunk nodes, with a chamber at the junction now and then.
            int trunkCount = trunk.Count;
            for (int i = 1; i < trunkCount - 1; i++)
            {
                if (rng.NextDouble() >= f.BranchChance * 0.2)
                {
                    continue;
                }

                Vec3d heading = walker.Heading(trunk[i], trunk[i + 1]);
                double angle = rng.Range(30, 60) * (rng.NextDouble() < 0.5 ? -1 : 1) * BodyFrame.Deg2Rad;
                Vec3d start = walker.Rotate(trunk[i], heading, angle);
                List<Vec3d> branch = walker.Walk(trunk[i], start, length * rng.Range(0.1, 0.4), rng);
                if (branch.Count < 2)
                {
                    continue;
                }

                double branchWidth = Math.Max(BodySpecWidthMin, site.Nodes[i].Width * rng.Range(0.3, 0.6));
                int previousIndex = i;
                for (int j = 1; j < branch.Count; j++)
                {
                    site.Nodes.Add(MakeNode(branch[j], depth, branchWidth, rng));
                    site.Edges.Add(new EdgeSpec { From = previousIndex, To = site.Nodes.Count - 1 });
                    previousIndex = site.Nodes.Count - 1;
                }

                if (site.Nodes[i].Feature == NodeFeature.None && rng.NextDouble() < f.ChamberChance)
                {
                    site.Nodes[i].Feature = NodeFeature.Chamber;
                }
            }

            return site;
        }

        // Karst: a passage per level (two or three levels, 30–80 m apart), each lower one starting a step from a node of the
        // one above (a steep ramp between them); tall, narrow, winding passages; rooms at some joins; dolines (rooms
        // open to the sky) on the top level. Width and length come from the field; heights are taller than lava tubes.
        private static SiteSpec KarstNetwork(FieldSpec f, Rng rng, Walker walker, Vec3d origin)
        {
            double length = rng.Range(f.Length.Min, f.Length.Max);
            if (!(length <= MaxLength))
            {
                length = MaxLength;
            }

            double u = rng.NextDouble();
            double width = Math.Max(KarstWidthMin, f.Width.Min + (f.Width.Max - f.Width.Min) * u * u);
            double depth = rng.Range(f.RoofDepth.Min, f.RoofDepth.Max);
            int levels = rng.NextDouble() < 0.5 ? 2 : 3;

            var site = new SiteSpec { IsNamed = false };
            List<Vec3d> passage = walker.Walk(origin, null, length, rng);
            if (passage.Count < 2)
            {
                return null;
            }

            AddPassage(site, passage, -1, depth, width, rng);
            int levelStart = 0, levelCount = passage.Count;
            for (int level = 1; level < levels; level++)
            {
                // The join: a node of the level above (not its ends), and a start one step aside from it.
                if (levelCount < 3)
                {
                    break;
                }

                int join = levelStart + rng.Range(1, levelCount - 1);
                Vec3d from = BodyFrame.Direction(site.Nodes[join].Lat, site.Nodes[join].Lon);
                Vec3d next = BodyFrame.Direction(site.Nodes[join + 1].Lat, site.Nodes[join + 1].Lon);
                double angle = rng.Range(60, 120) * (rng.NextDouble() < 0.5 ? -1 : 1) * BodyFrame.Deg2Rad;
                Vec3d heading = walker.Rotate(from, walker.Heading(from, next), angle);
                Vec3d start = (from + heading * (StepLength / walker.Radius)).Normalized;
                List<Vec3d> lower = walker.Walk(start, heading, length * rng.Range(0.4, 0.9), rng);
                if (lower.Count < 2)
                {
                    break;
                }

                depth += rng.Range(30, 80);
                width = Math.Max(KarstWidthMin, width * rng.Range(0.7, 1.1));
                levelStart = site.Nodes.Count;
                levelCount = lower.Count;
                AddPassage(site, lower, join, depth, width, rng);
                if (site.Nodes[join].Feature == NodeFeature.None && rng.NextDouble() < 0.5 + f.ChamberChance)
                {
                    site.Nodes[join].Feature = NodeFeature.Chamber;
                    site.Nodes[join].Width = Math.Min(500, site.Nodes[join].Width * rng.Range(2, 4));
                }
            }

            // Rooms along the passages, and dolines on the top level (the first passage).
            for (int i = 1; i < site.Nodes.Count - 1; i++)
            {
                NodeSpec node = site.Nodes[i];
                if (node.Feature != NodeFeature.None || rng.NextDouble() >= f.ChamberChance)
                {
                    continue;
                }

                node.Width = Math.Min(500, node.Width * rng.Range(2, 5));
                node.Height = Math.Max(node.Height, node.Width * rng.Range(0.3, 0.5));
                bool top = i < passage.Count;
                if (top && rng.NextDouble() < KarstDolineChance)
                {
                    node.Feature = NodeFeature.Skylight;
                    node.SkylightRadius = Math.Max(10, node.Width * rng.Range(0.25, 0.4));
                }
                else
                {
                    node.Feature = NodeFeature.Chamber;
                }
            }

            return site;
        }

        /// <summary>Narrowest karst passage (m): a rover still fits.</summary>
        private const double KarstWidthMin = 8;

        /// <summary>Share of top-level karst rooms that are dolines (open to the sky).</summary>
        private const double KarstDolineChance = 0.35;

        // Adds a karst passage: its nodes (tall cross-sections) and the edges along it, the first from `joinFrom` if any.
        private static void AddPassage(SiteSpec site, List<Vec3d> points, int joinFrom, double depth, double width, Rng rng)
        {
            int previous = joinFrom;
            foreach (Vec3d p in points)
            {
                NodeSpec node = MakeNode(p, depth, width, rng);
                node.Width = Math.Max(KarstWidthMin, width * rng.Range(0.7, 1.3));
                node.Height = node.Width * rng.Range(0.8, 1.6);
                node.Depth = Math.Max(TubePath.MinRoof(node.Width), depth * rng.Range(0.9, 1.1));
                site.Nodes.Add(node);
                int index = site.Nodes.Count - 1;
                if (previous >= 0)
                {
                    site.Edges.Add(new EdgeSpec { From = previous, To = index, Sinuosity = 0.6, Roughness = 0.1 });
                }

                previous = index;
            }
        }

        private static NodeSpec MakeNode(Vec3d dir, double depth, double width, Rng rng)
        {
            BodyFrame.ToLatLon(dir, out double lat, out double lon);
            double nodeWidth = Math.Min(500, Math.Max(BodySpecWidthMin, width * rng.Range(0.85, 1.15)));
            return new NodeSpec
            {
                Lat = lat,
                Lon = lon,
                Depth = Math.Max(TubePath.MinRoof(nodeWidth), depth * rng.Range(0.9, 1.1)),
                Width = nodeWidth,
                Height = nodeWidth * rng.Range(0.35, 0.6),
            };
        }

        /// <summary>Steps over the sphere following the downhill direction of the terrain.</summary>
        private sealed class Walker
        {
            private readonly IHeightSampler heights;
            private readonly IBiomeSampler biomes;
            private readonly HashSet<string> allowed;
            private readonly (double latMin, double latMax, double lonMin, double lonMax) rect;

            public Walker(IHeightSampler heights, IBiomeSampler biomes, HashSet<string> allowed, double radius,
                (double latMin, double latMax, double lonMin, double lonMax) rect)
            {
                this.heights = heights;
                this.biomes = biomes;
                this.allowed = allowed;
                Radius = radius;
                this.rect = rect;
            }

            public double Radius { get; }

            // From `start` (with an optional initial heading) for `length` metres. Stops after 3
            // steps in a row outside the allowed biomes (trimming them) or on leaving the cell + margin.
            public List<Vec3d> Walk(Vec3d start, Vec3d? initialHeading, double length, Rng rng)
            {
                var points = new List<Vec3d> { start };
                Vec3d p = start;
                Vec3d previous = initialHeading ?? Downhill(p) ?? RandomTangent(p, rng);
                int outside = 0;
                double travelled = 0;
                while (travelled + StepLength <= length + 1e-6)
                {
                    Vec3d down = Downhill(p) ?? previous;
                    Vec3d jitter = RandomTangent(p, rng) * 0.25;
                    Vec3d heading = Tangent(p, down * 0.7 + previous * 0.3 + jitter);
                    if (heading.LengthSquared < 1e-12)
                    {
                        heading = previous;
                    }

                    Vec3d next = (p + heading * (StepLength / Radius)).Normalized;
                    if (!InRect(next))
                    {
                        break;
                    }

                    points.Add(next);
                    travelled += StepLength;
                    previous = heading;
                    p = next;
                    BodyFrame.ToLatLon(next, out double lat, out double lon);
                    outside = allowed.Contains(biomes.Biome(lat, lon) ?? "") ? 0 : outside + 1;
                    if (outside >= 3)
                    {
                        break;
                    }
                }

                while (outside-- > 0 && points.Count > 1)
                {
                    points.RemoveAt(points.Count - 1);
                }

                return points;
            }

            /// <summary>Unit tangent at <paramref name="from"/> pointing towards <paramref name="to"/>.</summary>
            public Vec3d Heading(Vec3d from, Vec3d to) => Tangent(from, to - from);

            /// <summary>The tangent <paramref name="heading"/> at <paramref name="p"/> turned by <paramref name="angle"/> radians about the vertical.</summary>
            public Vec3d Rotate(Vec3d p, Vec3d heading, double angle)
            {
                Vec3d side = Vec3d.Cross(p, heading);
                return (heading * Math.Cos(angle) + side * Math.Sin(angle)).Normalized;
            }

            // Unit downhill tangent from finite differences (100 m), or null on flat ground.
            private Vec3d? Downhill(Vec3d p)
            {
                Basis(p, out Vec3d east, out Vec3d north);
                double step = 100 / Radius;
                double he = Height(p + east * step) - Height(p - east * step);
                double hn = Height(p + north * step) - Height(p - north * step);
                Vec3d gradient = east * he + north * hn;
                if (gradient.Length < 1e-9)
                {
                    return null;
                }

                return (-gradient).Normalized;
            }

            private double Height(Vec3d dir)
            {
                BodyFrame.ToLatLon(dir, out double lat, out double lon);
                return heights.SurfaceRadius(lat, lon);
            }

            private static Vec3d RandomTangent(Vec3d p, Rng rng)
            {
                Basis(p, out Vec3d east, out Vec3d north);
                double a = rng.Range(0, 2 * Math.PI);
                return east * Math.Cos(a) + north * Math.Sin(a);
            }

            private static Vec3d Tangent(Vec3d p, Vec3d v) => (v - p * Vec3d.Dot(v, p)).Normalized;

            private static void Basis(Vec3d p, out Vec3d east, out Vec3d north)
            {
                east = Vec3d.Cross(new Vec3d(0, 1, 0), p);
                if (east.LengthSquared < 1e-12)
                {
                    east = new Vec3d(0, 0, 1);
                }

                east = east.Normalized;
                north = Vec3d.Cross(p, east).Normalized;
            }

            private bool InRect(Vec3d dir)
            {
                BodyFrame.ToLatLon(dir, out double lat, out double lon);
                if (lat < rect.latMin || lat > rect.latMax)
                {
                    return false;
                }

                if (rect.lonMax - rect.lonMin >= 360)
                {
                    return true;
                }

                double centre = (rect.lonMin + rect.lonMax) * 0.5;
                while (lon - centre > 180)
                {
                    lon -= 360;
                }

                while (lon - centre < -180)
                {
                    lon += 360;
                }

                return lon >= rect.lonMin && lon <= rect.lonMax;
            }
        }
    }
}
