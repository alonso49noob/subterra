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

            // The trunk's roof rises and sinks along it (a slow wave, up to a third of its depth).
            double wave = rng.Range(0, 0.35), phase = rng.Range(0, 2 * Math.PI), period = rng.Range(6, 14);
            // Its width swells and narrows too (a slower wave), with now and then a squeeze to crawl through or a
            // room where the lava pooled: something to find on the way instead of one even corridor.
            double swell = rng.Range(0.1, 0.3), swellPhase = rng.Range(0, 2 * Math.PI), swellPeriod = rng.Range(5, 11);
            for (int i = 0; i < trunk.Count; i++)
            {
                double roof = depth * (1 + wave * Math.Sin(phase + 2 * Math.PI * i / period));
                double scale = 1 + swell * Math.Sin(swellPhase + 2 * Math.PI * i / swellPeriod);
                double odd = rng.NextDouble();
                if (i > 0 && i < trunk.Count - 1)
                {
                    scale *= odd < 0.1 ? 0.55 : odd > 0.93 ? 1.6 : 1;
                }

                site.Nodes.Add(MakeNode(trunk[i], roof, trunkWidth * scale, rng));
            }

            for (int i = 1; i < trunk.Count; i++)
            {
                site.Edges.Add(new EdgeSpec { From = i - 1, To = i, Sinuosity = 0.5 });
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

                // Sometimes the roof fell in twice close together (a short pit chain), the next node down the tube too.
                if (rng.NextDouble() < 0.3 && best + 1 < trunk.Count && site.Nodes[best + 1].Feature == NodeFeature.None)
                {
                    NodeSpec pair = site.Nodes[best + 1];
                    pair.Feature = NodeFeature.Skylight;
                    pair.SkylightRadius = Math.Max(10, Math.Min(pair.Width / 2, pair.Width * rng.Range(0.2, 0.4)));
                    previous = along[best + 1];
                }
            }

            // A network, not a corridor: branches leave the trunk (and branches leave them), some coming back into the
            // trunk further on (braided tubes, as lava finds side paths and rejoins), chambers at some junctions.
            var trunkIndices = new List<int>();
            for (int i = 0; i < trunk.Count; i++)
            {
                trunkIndices.Add(i);
            }

            AddBraids(rng, walker, site, trunkIndices);
            AddBranches(f, rng, walker, site, trunkIndices, trunkIndices, length, depth, 1);
            AddSideSkylights(f, rng, site, trunk.Count);

            return site;
        }

        /// <summary>
        /// Grows a named site into a network around its own nodes (Lucas: named sites were three-node corridors): a braid
        /// beside most of its passages, side branches from its nodes (some branching again, some rejoining the site), and
        /// skylights on what was added. The site's own nodes, features and edges stay as they are. Skipped for special shapes
        /// (sealed rooms, deep shafts at a fixed altitude, a rille's tube) and for sites with <c>network = false</c>.
        /// Deterministic per site name.
        /// </summary>
        public static void Elaborate(SiteSpec site, double bodyRadius, IHeightSampler heights)
        {
            if (!site.IsNamed || !site.Network || site.Elaborated || site.UnderRelief || site.Nodes.Count < 2
                || site.Nodes.Exists(n => !double.IsNaN(n.Altitude) || n.Feature == NodeFeature.Geode || n.Feature == NodeFeature.Portal))
            {
                return;
            }

            site.Elaborated = true;
            var rng = new Rng(Hash.Combine(Hash.Of(site.Name), Hash.Of("elaborate")));
            var walker = new Walker(heights, new AnyBiome(), new HashSet<string> { "" }, bodyRadius, (-90, 90, -180, 180));
            int original = site.Nodes.Count;
            var edges = new List<EdgeSpec>(site.Edges);

            // A braid beside most passages: out to one side and back, some on a lower level.
            foreach (EdgeSpec e in edges)
            {
                NodeSpec a = site.Nodes[e.From], z = site.Nodes[e.To];
                Vec3d pa = Direction(a), pz = Direction(z);
                double length = BodyFrame.SurfaceDistance(pa, pz, bodyRadius);
                if (length < 500 || rng.NextDouble() > 0.7)
                {
                    continue;
                }

                int steps = Math.Max(2, (int)(length / 250));
                double bow = Math.Min(300, length * rng.Range(0.12, 0.3)) * (rng.NextDouble() < 0.5 ? -1 : 1);
                double width = Math.Max(BranchWidthMin, Math.Min(a.Width, z.Width) * rng.Range(0.3, 0.6));
                double drop = rng.NextDouble() < 0.4 ? rng.Range(20, 50) : rng.Range(-5, 5);
                Vec3d side = Vec3d.Cross(pa, walker.Heading(pa, pz)).Normalized;
                int previous = e.From;
                for (int k = 1; k < steps; k++)
                {
                    double t = k / (double)steps;
                    Vec3d p = (pa + (pz - pa) * t).Normalized;
                    Vec3d q = (p + side * (bow * Math.Sin(Math.PI * t) / bodyRadius)).Normalized;
                    double roof = a.Depth + (z.Depth - a.Depth) * t + drop * Math.Sin(Math.PI * t);
                    site.Nodes.Add(MakeNode(q, roof, width, rng));
                    site.Edges.Add(new EdgeSpec { From = previous, To = site.Nodes.Count - 1, Sinuosity = 0.6 });
                    previous = site.Nodes.Count - 1;
                }

                site.Edges.Add(new EdgeSpec { From = previous, To = e.To, Sinuosity = 0.6 });
            }

            // Side branches from the site's own nodes: 0.4–1.5 km, narrower, now and then one more off them; some come
            // back into another of the site's nodes when they end near it.
            for (int i = 0; i < original; i++)
            {
                if (rng.NextDouble() > 0.65)
                {
                    continue;
                }

                AddSiteBranch(site, rng, walker, i, original, rng.Range(400, 1500), 1);
            }

            var f = new FieldSpec { SkylightSpacing = new Range(1500, 3000) };
            AddSideSkylights(f, rng, site, original);
        }

        private static void AddSiteBranch(SiteSpec site, Rng rng, Walker walker, int from, int original, double length, int order)
        {
            NodeSpec root = site.Nodes[from];
            Vec3d p = Direction(root);
            List<Vec3d> path = walker.Walk(p, walker.Rotate(p, RandomHeading(walker, p, rng), 0), length, rng);
            if (path.Count < 2)
            {
                return;
            }

            double width = Math.Max(BranchWidthMin, root.Width * rng.Range(0.25, 0.55));
            int previous = from;
            for (int j = 1; j < path.Count; j++)
            {
                site.Nodes.Add(MakeNode(path[j], root.Depth + rng.Range(-5, 15), width, rng));
                site.Edges.Add(new EdgeSpec { From = previous, To = site.Nodes.Count - 1 });
                previous = site.Nodes.Count - 1;
                if (order < 2 && j < path.Count - 1 && rng.NextDouble() < 0.3)
                {
                    AddSiteBranch(site, rng, walker, previous, original, length * 0.5, order + 1);
                }
            }

            // Back into the site where it ends near one of its nodes (a loop).
            Vec3d end = path[path.Count - 1];
            for (int i = 0; i < original; i++)
            {
                if (i != from && BodyFrame.SurfaceDistance(end, Direction(site.Nodes[i]), walker.Radius) < 600 && rng.NextDouble() < 0.6)
                {
                    site.Edges.Add(new EdgeSpec { From = previous, To = i });
                    break;
                }
            }
        }

        private static Vec3d RandomHeading(Walker walker, Vec3d p, Rng rng)
        {
            Vec3d north = (new Vec3d(0, 1, 0) - p * p.Y).Normalized;
            return walker.Rotate(p, north.LengthSquared > 0 ? north : new Vec3d(1, 0, 0), rng.Range(0, 2 * Math.PI));
        }

        private sealed class AnyBiome : IBiomeSampler
        {
            public string Biome(double latDeg, double lonDeg) => "";
        }

        /// <summary>Braids per km of trunk: side channels that leave the trunk and come back into it (Lucas: corridors).</summary>
        private const double BraidsPerKm = 0.7;

        // Braided tubes, as lava splits around an obstacle and joins again: a channel beside the trunk between two of its
        // nodes, bowing out 60–250 m, narrower, sometimes on a level of its own (tens of metres under, with ramps down and
        // up at its ends): loops to drive round, two levels to explore.
        private static void AddBraids(Rng rng, Walker walker, SiteSpec site, List<int> trunk)
        {
            int count = (int)Math.Round(trunk.Count * StepLength / 1000 * BraidsPerKm * rng.Range(0.6, 1.4));
            for (int b = 0; b < count && trunk.Count >= 5; b++)
            {
                int span = 2 + (int)(rng.NextDouble() * 4);
                int from = 1 + (int)(rng.NextDouble() * Math.Max(1, trunk.Count - span - 2));
                int to = Math.Min(trunk.Count - 1, from + span);
                NodeSpec a = site.Nodes[trunk[from]], z = site.Nodes[trunk[to]];
                double bow = rng.Range(60, 250) * (rng.NextDouble() < 0.5 ? -1 : 1);
                double width = Math.Max(BranchWidthMin, Math.Min(a.Width, z.Width) * rng.Range(0.35, 0.7));
                double drop = rng.NextDouble() < 0.4 ? rng.Range(25, 70) : rng.Range(-8, 8);
                int previous = trunk[from];
                for (int k = 1; k < span * 2; k++)
                {
                    // Along the trunk's own course (its nodes, interpolated), pushed sideways by the bow.
                    double t = k / (double)(span * 2);
                    double at = from + t * span;
                    int i0 = (int)Math.Floor(at), i1 = Math.Min(trunk.Count - 1, i0 + 1);
                    Vec3d p0 = Direction(site.Nodes[trunk[i0]]), p1 = Direction(site.Nodes[trunk[i1]]);
                    Vec3d p = (p0 + (p1 - p0) * (at - i0)).Normalized;
                    Vec3d side = Vec3d.Cross(p, walker.Heading(p0, p1)).Normalized;
                    double offset = bow * Math.Sin(Math.PI * t) * (1 + 0.25 * (rng.NextDouble() - 0.5));
                    Vec3d q = (p + side * (offset / walker.Radius)).Normalized;
                    double roof = a.Depth + (z.Depth - a.Depth) * t + drop * Math.Sin(Math.PI * t);
                    site.Nodes.Add(MakeNode(q, roof, width, rng));
                    site.Edges.Add(new EdgeSpec { From = previous, To = site.Nodes.Count - 1, Sinuosity = 0.6 });
                    previous = site.Nodes.Count - 1;
                }

                site.Edges.Add(new EdgeSpec { From = previous, To = trunk[to], Sinuosity = 0.6 });
            }
        }

        // Skylights off the trunk too (branches and braids): the roof falls in wherever it is thin, not along one line.
        private static void AddSideSkylights(FieldSpec f, Rng rng, SiteSpec site, int trunkCount)
        {
            double chance = StepLength / Math.Max(StepLength, (f.SkylightSpacing.Min + f.SkylightSpacing.Max) * 0.5) * 0.8;
            for (int i = trunkCount; i < site.Nodes.Count; i++)
            {
                NodeSpec node = site.Nodes[i];
                if (node.Feature == NodeFeature.None && node.Depth < 120 && rng.NextDouble() < chance)
                {
                    node.Feature = NodeFeature.Skylight;
                    node.SkylightRadius = Math.Max(8, Math.Min(node.Width / 2, node.Width * rng.Range(0.3, 0.5)));
                }
            }
        }

        /// <summary>Narrowest side passage (m): room for a rover, tight enough to feel like a crawl after the big tubes.</summary>
        private const double BranchWidthMin = 10;

        /// <summary>Deepest branching: branches of the trunk (1), of those (2) and of those again (3).</summary>
        private const int MaxBranchOrder = 3;

        /// <summary>Share of branches that dive to a deeper level.</summary>
        private const double DiveChance = 0.35;

        /// <summary>How much deeper (m) a diving branch's roof ends up.</summary>
        private static readonly Range DiveDepth = new Range(50, 180);

        /// <summary>Nodes (of 400 m) a dive is spread over: 1–3 km of ramp, 2–15°.</summary>
        private const int DiveNodes = 3;

        /// <summary>Share of branches that come back into the trunk when their end passes near it (a loop).</summary>
        private const double RejoinChance = 0.45;

        /// <summary>A branch end this close (m) to a trunk node further on joins it.</summary>
        private const double RejoinDistance = 1500;

        // Branches from the interior nodes of `chain` (node indices in order): each a downhill walk leaving at 25–55°,
        // narrower than its parent; some rejoin the trunk; first-order branches branch again.
        private static void AddBranches(FieldSpec f, Rng rng, Walker walker, SiteSpec site, List<int> chain, List<int> trunk,
            double length, double depth, int order)
        {
            for (int k = 1; k < chain.Count - 1; k++)
            {
                if (rng.NextDouble() >= f.BranchChance * (order == 1 ? 1.1 : order == 2 ? 0.55 : 0.3))
                {
                    continue;
                }

                int i = chain[k];
                Vec3d p = Direction(site.Nodes[i]);
                Vec3d heading = walker.Heading(p, Direction(site.Nodes[chain[k + 1]]));
                double angle = rng.Range(25, 55) * (rng.NextDouble() < 0.5 ? -1 : 1) * BodyFrame.Deg2Rad;
                Vec3d start = walker.Rotate(p, heading, angle);
                List<Vec3d> branch = walker.Walk(p, start, length * rng.Range(0.15, 0.45) / order, rng);
                if (branch.Count < 2)
                {
                    continue;
                }

                double branchWidth = Math.Max(BranchWidthMin, site.Nodes[i].Width * rng.Range(0.25, 0.65));

                // Some branches dive: over their first nodes they go down to a deeper level, where their own branches spread
                // (deeper networks under the shallow ones).
                bool dives = rng.NextDouble() < DiveChance;
                double from = site.Nodes[i].Depth, to = dives ? from + rng.Range(DiveDepth.Min, DiveDepth.Max) : from;
                var indices = new List<int> { i };
                int previousIndex = i;
                for (int j = 1; j < branch.Count; j++)
                {
                    double t = Math.Min(1.0, j / (double)DiveNodes);
                    site.Nodes.Add(MakeNode(branch[j], from + (to - from) * t, branchWidth, rng));
                    site.Edges.Add(new EdgeSpec { From = previousIndex, To = site.Nodes.Count - 1 });
                    previousIndex = site.Nodes.Count - 1;
                    indices.Add(previousIndex);
                }

                // Back into the trunk: the nearest trunk node beyond the junction, if the branch ends close to it.
                if (rng.NextDouble() < RejoinChance && !dives)
                {
                    Vec3d end = branch[branch.Count - 1];
                    int best = -1;
                    double bestDistance = RejoinDistance;
                    int junction = trunk.IndexOf(i);
                    for (int t = Math.Max(0, junction) + 2; t < trunk.Count; t++)
                    {
                        double d = BodyFrame.SurfaceDistance(end, Direction(site.Nodes[trunk[t]]), walker.Radius);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = trunk[t];
                        }
                    }

                    if (best >= 0)
                    {
                        site.Edges.Add(new EdgeSpec { From = previousIndex, To = best });
                    }
                }

                if (site.Nodes[i].Feature == NodeFeature.None && rng.NextDouble() < f.ChamberChance)
                {
                    MakeChamber(site.Nodes[i]);
                }

                if (order < MaxBranchOrder)
                {
                    AddBranches(f, rng, walker, site, indices, trunk, length, to, order + 1);
                }
            }
        }

        private static Vec3d Direction(NodeSpec node) => BodyFrame.Direction(node.Lat, node.Lon);

        // A chamber is a dome reaching a fifth of its height over the tube's roof and spreading a width all round, where
        // the ground may be lower than at the node: its roof is pushed down so the dome stays under the ground
        // (a dome breaking the surface opened to the sky with no rim).
        private static void MakeChamber(NodeSpec node)
        {
            node.Feature = NodeFeature.Chamber;
            node.Depth = Math.Max(node.Depth, 0.2 * 1.2 * node.Height + TubePath.MinRoof(node.Width) + 0.05 * node.Width + 5);
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
                    site.Nodes[join].Width = Math.Min(500, site.Nodes[join].Width * rng.Range(2, 4));
                    MakeChamber(site.Nodes[join]);
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
                    MakeChamber(node);
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
            double nodeWidth = Math.Min(500, Math.Max(BranchWidthMin, width * rng.Range(0.94, 1.06)));
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

                // The bend (turn per step) drifts slowly, so the course swings in arcs and S-curves like a meandering
                // flow; on the flat maria the slope alone kept it nearly straight, its skylights in a row.
                double bend = rng.Range(-0.5, 0.5) * MaxTurn;
                while (travelled + StepLength <= length + 1e-6)
                {
                    // Lava (and water) keeps its course and bends slowly towards the slope: mostly the previous heading
                    // turned by the bend, some downhill, a little wander, never more than MaxTurn per step (no zigzag).
                    bend = Math.Max(-0.85 * MaxTurn, Math.Min(0.85 * MaxTurn, bend + rng.Range(-0.35, 0.35) * MaxTurn));
                    Vec3d down = Downhill(p) ?? previous;
                    Vec3d jitter = RandomTangent(p, rng) * 0.08;
                    Vec3d heading = Tangent(p, Rotate(p, previous, bend) * 0.8 + down * 0.2 + jitter);
                    if (heading.LengthSquared < 1e-12)
                    {
                        heading = previous;
                    }

                    heading = LimitTurn(p, previous, heading);

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

            /// <summary>Largest change of heading between two steps (radians).</summary>
            private const double MaxTurn = 24 * Math.PI / 180;

            // `heading` turned back towards `previous` (both tangent at p) if they are more than MaxTurn apart.
            private static Vec3d LimitTurn(Vec3d p, Vec3d previous, Vec3d heading)
            {
                Vec3d before = Tangent(p, previous);
                if (before.LengthSquared < 1e-12)
                {
                    return heading;
                }

                double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(before, heading))));
                if (angle <= MaxTurn)
                {
                    return heading;
                }

                double sign = Vec3d.Dot(Vec3d.Cross(before, heading), p) >= 0 ? 1 : -1;
                Vec3d side = Vec3d.Cross(p, before).Normalized;
                return (before * Math.Cos(MaxTurn) + side * (sign * Math.Sin(MaxTurn))).Normalized;
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
