using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Subterra.Core.Environment;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Procedural;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;

namespace Subterra.Core.Radar
{
    /// <summary>One target per primitive of every cave network of a body.</summary>
    public sealed class NetworkTargetSource : ISubsurfaceTargetSource
    {
        // decidido por Claude, revisar: density of the rock where no layer covers the depth (the parser default).
        private const double DefaultDensity = 2.5;

        private readonly NetworkCatalog catalog;
        private readonly Strata strata;
        private readonly IBiomeSampler biomes;

        // Networks are immutable (a dug tunnel makes a new instance), so their targets are computed once.
        private readonly ConditionalWeakTable<TubeNetwork, IReadOnlyList<SubsurfaceTarget>> cache =
            new ConditionalWeakTable<TubeNetwork, IReadOnlyList<SubsurfaceTarget>>();

        public NetworkTargetSource(NetworkCatalog catalog, Strata strata, IBiomeSampler biomes)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.strata = strata ?? throw new ArgumentNullException(nameof(strata));
            this.biomes = biomes ?? throw new ArgumentNullException(nameof(biomes));
        }

        public IEnumerable<SubsurfaceTarget> TargetsNear(Vec3d bodyPos, double radius)
        {
            foreach (TubeNetwork net in catalog.NetworksNear(bodyPos, radius))
            {
                foreach (SubsurfaceTarget target in TargetsOf(net))
                {
                    if (Vec3d.Distance(target.Position, bodyPos) <= radius)
                    {
                        yield return target;
                    }
                }
            }
        }

        public IReadOnlyList<SubsurfaceTarget> TargetsOf(TubeNetwork net)
        {
            if (net == null)
            {
                throw new ArgumentNullException(nameof(net));
            }

            return cache.GetValue(net, Build);
        }

        private IReadOnlyList<SubsurfaceTarget> Build(TubeNetwork net)
        {
            var targets = new List<SubsurfaceTarget>(net.Primitives.Count);
            for (int i = 0; i < net.Primitives.Count; i++)
            {
                targets.Add(TargetOf(net, net.Primitives[i], i));
            }

            return targets;
        }

        // The centre is the centre of the void (a tube's bounds are centred on its floor line, so they would
        // put it half a height too deep).
        private SubsurfaceTarget TargetOf(TubeNetwork net, Primitive prim, int index)
        {
            Vec3 centre;
            double width, height, length;
            switch (prim)
            {
                case TubeSegment tube:
                    {
                        Vec3 mid = (tube.A + tube.B) * 0.5f;
                        centre = mid + net.UpAt(mid) * (tube.Height * 0.5f);
                        width = tube.Width;
                        height = tube.Height;
                        length = tube.Length;
                        break;
                    }

                case Chamber chamber:
                    centre = chamber.FloorCenter + chamber.Up * (chamber.Height * 0.5f);
                    width = 2.0 * chamber.Radius;
                    height = chamber.Height;
                    length = 2.0 * chamber.Radius;
                    break;

                case Shaft shaft:
                    centre = shaft.BottomCenter + shaft.Up * (shaft.Length * 0.5f);
                    width = 2.0 * shaft.Radius;
                    height = shaft.Length;
                    length = 2.0 * shaft.Radius;
                    break;

                default:
                    // Unknown primitive: its box centre and typical size.
                    centre = prim.Bounds.Center;
                    width = height = length = prim.Width;
                    break;
            }

            Vec3d position = net.ToBody(centre);
            double depth = Math.Max(0, net.SurfaceRadiusNear(centre) - (position.Length + height * 0.5));
            BodyFrame.ToLatLon(position, out double lat, out double lon);
            LayerSpec host = strata.LayerAt(depth, biomes.Biome(lat, lon));
            return new SubsurfaceTarget
            {
                Id = $"{net.Id}#{index}",
                Kind = prim.Zone == ZoneKind.Geode ? TargetKind.Sealed : TargetKind.Void,
                NetworkId = net.Id,
                PrimitiveIndex = index,
                Position = position,
                Lat = lat,
                Lon = lon,
                Width = width,
                Height = height,
                Length = length,
                Depth = depth,
                DensityContrast = host?.Density ?? DefaultDensity,
                Host = host,
            };
        }
    }
}
