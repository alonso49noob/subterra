using System;
using System.Collections.Generic;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;

namespace Subterra.Core.Tubes
{
    /// <summary>
    /// A compiled tube network: primitives, SDF and skylights in a local frame (the PQS axes moved to
    /// <see cref="Anchor"/>, no rotation), plus the queries the plugin needs. Immutable; thread-safe.
    /// </summary>
    public sealed class TubeNetwork
    {
        private readonly Vec3d[] surfaceDirs;
        private readonly double[] surfaceRadii;
        private readonly double minSurfaceRadius;

        public TubeNetwork(string id, string body, string siteName, string displayName, Vec3d anchor,
            IReadOnlyList<Primitive> primitives, NetworkSdf sdf, IReadOnlyList<Skylight> skylights,
            Vec3d[] surfaceDirs, double[] surfaceRadii, IReadOnlyList<PathPoint> nodes = null, SiteSpec site = null)
        {
            Nodes = nodes ?? new PathPoint[0];
            Site = site;
            Id = id;
            Body = body;
            SiteName = siteName;
            DisplayName = displayName;
            Anchor = anchor;
            Primitives = primitives;
            Sdf = sdf;
            Skylights = skylights;
            this.surfaceDirs = surfaceDirs;
            this.surfaceRadii = surfaceRadii;
            minSurfaceRadius = double.MaxValue;
            foreach (double r in surfaceRadii)
            {
                minSurfaceRadius = Math.Min(minSurfaceRadius, r);
            }
            Aabb bounds = primitives.Count > 0 ? primitives[0].Bounds : default;
            foreach (Primitive prim in primitives)
            {
                bounds = Aabb.Union(bounds, prim.Bounds);
            }

            Bounds = bounds;
        }

        /// <summary><c>site:&lt;name&gt;</c> for named sites, <c>MN-0427</c> for procedural networks.</summary>
        public string Id { get; }

        public string Body { get; }

        /// <summary>The named site's name; null for procedural networks.</summary>
        public string SiteName { get; }

        public string DisplayName { get; }

        public bool IsNamed => SiteName != null;

        /// <summary>Origin of the local frame in body coordinates (floor of the first node).</summary>
        public Vec3d Anchor { get; }

        public Aabb Bounds { get; }

        public IReadOnlyList<Primitive> Primitives { get; }

        public NetworkSdf Sdf { get; }

        public IReadOnlyList<Skylight> Skylights { get; }

        /// <summary>The graph nodes after the roof rule: floor centre (body coordinates), terrain radius, size.</summary>
        public IReadOnlyList<PathPoint> Nodes { get; }

        /// <summary>The graph it was compiled from.</summary>
        public SiteSpec Site { get; }

        /// <summary>
        /// The same network with <paramref name="extra"/> primitives (already in this network's frame) added, and
        /// extra terrain samples for them: a dug tunnel joined into it. The wall noise keeps its seed (the id),
        /// so the untouched parts mesh exactly as before.
        /// </summary>
        public TubeNetwork WithPrimitives(IReadOnlyList<Primitive> extra, IReadOnlyList<Vec3d> extraSurfaceDirs, IReadOnlyList<double> extraSurfaceRadii,
            IReadOnlyList<Skylight> extraSkylights = null)
        {
            var prims = new List<Primitive>(Primitives.Count + extra.Count);
            prims.AddRange(Primitives);
            prims.AddRange(extra);
            var dirs = new List<Vec3d>(surfaceDirs);
            dirs.AddRange(extraSurfaceDirs);
            var radii = new List<double>(surfaceRadii);
            radii.AddRange(extraSurfaceRadii);
            var sdf = new NetworkSdf(prims, new SpatialGrid(prims), Hash.Of(Id));
            var skylights = new List<Skylight>(Skylights);
            if (extraSkylights != null)
            {
                skylights.AddRange(extraSkylights);
            }

            return new TubeNetwork(Id, Body, SiteName, DisplayName, Anchor, prims, sdf, skylights, dirs.ToArray(), radii.ToArray(), Nodes, Site);
        }

        /// <summary>
        /// A dug tunnel standing alone (not yet reaching a cave): its pieces as the network, the terrain sampled
        /// along it, and the surface opening if it has one.
        /// </summary>
        public static TubeNetwork ForExcavation(Excavation.ExcavationSpec spec, IReadOnlyList<Vec3d> surfaceDirs, IReadOnlyList<double> surfaceRadii,
            IReadOnlyList<Skylight> openings)
        {
            List<Primitive> prims = spec.Pieces(spec.Anchor);
            var sdf = new NetworkSdf(prims, new SpatialGrid(prims), Hash.Of(spec.Id));
            var dirs = new Vec3d[surfaceDirs.Count];
            var radii = new double[surfaceRadii.Count];
            for (int i = 0; i < dirs.Length; i++)
            {
                dirs[i] = surfaceDirs[i];
                radii[i] = surfaceRadii[i];
            }

            return new TubeNetwork(spec.Id, spec.Body, null, spec.Id, spec.Anchor, prims, sdf, openings ?? new Skylight[0], dirs, radii);
        }

        public Vec3 ToLocal(Vec3d bodyPos) => (bodyPos - Anchor).ToVec3();

        public Vec3d ToBody(Vec3 local) => Anchor + Vec3d.FromVec3(local);

        /// <summary>Local vertical at a local point.</summary>
        public Vec3 UpAt(Vec3 local) => ToBody(local).Normalized.ToVec3();

        /// <summary>In the void and under the terrain (the part of a skylight shaft above the ground is open air).</summary>
        public bool IsInside(Vec3 local) => Bounds.Contains(local) && Sdf.Distance(local) < 0 && AboveSurface(local) < 0;

        /// <summary>Terrain radius above the path point nearest to the direction of <paramref name="local"/>.</summary>
        public double SurfaceRadiusNear(Vec3 local)
        {
            Vec3d dir = ToBody(local).Normalized;
            double best = double.MinValue;
            double radius = surfaceRadii.Length > 0 ? surfaceRadii[0] : Anchor.Length;
            for (int i = 0; i < surfaceDirs.Length; i++)
            {
                double dot = Vec3d.Dot(dir, surfaceDirs[i]);
                if (dot > best)
                {
                    best = dot;
                    radius = surfaceRadii[i];
                }
            }

            return radius;
        }

        /// <summary>Lowest terrain radius over the network's path points (a quick bound for <see cref="AboveSurface"/>).</summary>
        public double MinSurfaceRadius => minSurfaceRadius;

        /// <summary>Metres of <paramref name="local"/> above the terrain (negative under it), using the nearest path point's terrain.</summary>
        public double AboveSurface(Vec3 local) => ToBody(local).Length - SurfaceRadiusNear(local);

        /// <summary>Distance (m) straight down from <paramref name="local"/> to the cave floor; null if not in the void.</summary>
        public float? FloorBelow(Vec3 local)
        {
            if (!IsInside(local))
            {
                return null;
            }

            Vec3 down = -UpAt(local);
            float travelled = 0;
            float inside = 0;
            const float maxDistance = 2000f;
            while (travelled < maxDistance)
            {
                float d = Sdf.Distance(local + down * travelled);
                if (d >= 0)
                {
                    // Bisect between the last point in the void and this one.
                    float lo = inside, hi = travelled;
                    for (int i = 0; i < 20; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Sdf.Distance(local + down * mid) < 0)
                        {
                            lo = mid;
                        }
                        else
                        {
                            hi = mid;
                        }
                    }

                    return (lo + hi) * 0.5f;
                }

                inside = travelled;
                travelled += Math.Max(0.1f, -d * 0.9f);
            }

            return null;
        }

        public ZoneInfo ZoneAt(Vec3 local)
        {
            if (!Bounds.Contains(local))
            {
                return ZoneInfo.None;
            }

            int nearest = Sdf.NearestPrimitive(local);
            if (nearest < 0 || Sdf.Distance(local) >= 0)
            {
                return ZoneInfo.None;
            }

            return new ZoneInfo(Primitives[nearest].Zone, SiteName, Id);
        }

        /// <summary>
        /// Marches from <paramref name="from"/> along <paramref name="dir"/> through the void; true if it
        /// gets 10 m above the terrain without entering rock within <paramref name="maxDist"/>.
        /// </summary>
        public bool RayExitsToSky(Vec3 from, Vec3 dir, float maxDist)
        {
            Vec3 d = dir.Normalized;
            float travelled = 0;
            double surface = SurfaceRadiusNear(from);
            float surfaceAt = 0;
            while (travelled <= maxDist)
            {
                Vec3 p = from + d * travelled;
                // The terrain radius changes slowly along a ray: look it up every 25 m.
                if (travelled - surfaceAt > 25f)
                {
                    surface = SurfaceRadiusNear(p);
                    surfaceAt = travelled;
                }

                if (ToBody(p).Length > surface + 10.0)
                {
                    return true;
                }

                float distance = Sdf.Distance(p);
                if (distance > 0)
                {
                    return false;
                }

                travelled += Math.Max(0.5f, -distance);
            }

            return false;
        }

        /// <summary>0–1: 1 under open sky (or straight under a skylight), partial near skylights, 0 deep in a tube.</summary>
        public float SkyVisibility(Vec3 local)
        {
            Vec3 up = UpAt(local);
            if (RayExitsToSky(local, up, 3000f))
            {
                return 1f;
            }

            float sum = 0;
            foreach (Skylight skylight in Skylights)
            {
                Vec3 toSky = skylight.SurfaceCenter + skylight.Up * 15f - local;
                float distance = toSky.Length;
                if (distance > 2000f)
                {
                    continue;
                }

                if (RayExitsToSky(local, toSky, distance + 50f))
                {
                    sum += Math.Min(1f, skylight.Radius * skylight.Radius / Math.Max(1f, distance * distance));
                }
            }

            return Math.Max(0f, Math.Min(1f, sum));
        }
    }
}
