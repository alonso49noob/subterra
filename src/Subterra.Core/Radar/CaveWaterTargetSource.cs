using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Subterra.Core.Environment;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Procedural;
using Subterra.Core.Tubes;

namespace Subterra.Core.Radar
{
    /// <summary>
    /// One <see cref="TargetKind.Water"/> target per still water level in the caves of a body (the nodes with
    /// <c>water</c>). Spec 4.1 shows water as an attribute of the network until parts 4 and 5; this also registers it as
    /// a target (ruling R27), attached to its network. Ids are <c>water:&lt;netId&gt;#&lt;i&gt;</c>, i counting the
    /// water levels of the network in order.
    /// </summary>
    public sealed class CaveWaterTargetSource : ISubsurfaceTargetSource
    {
        // The density of water in g/cm3: the contrast the gravimeter and the seismic survey see.
        private const double WaterDensity = 1.0;

        private readonly NetworkCatalog catalog;
        private readonly Strata strata;
        private readonly IBiomeSampler biomes;

        // Networks are immutable (a dug tunnel makes a new instance), so their targets are computed once.
        private readonly ConditionalWeakTable<TubeNetwork, IReadOnlyList<SubsurfaceTarget>> cache =
            new ConditionalWeakTable<TubeNetwork, IReadOnlyList<SubsurfaceTarget>>();

        public CaveWaterTargetSource(NetworkCatalog catalog, Strata strata, IBiomeSampler biomes)
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
            var targets = new List<SubsurfaceTarget>();
            if (net.Site == null)
            {
                return targets.AsReadOnly();
            }

            // TubeNetwork.Waters lists one level per node with water, in node order: pair them with their nodes.
            IReadOnlyList<WaterSurface> waters = net.Waters;
            int w = 0;
            for (int i = 0; i < net.Site.Nodes.Count && i < net.Nodes.Count && w < waters.Count; i++)
            {
                NodeSpec node = net.Site.Nodes[i];
                if (node.Water > 0)
                {
                    targets.Add(TargetOf(net, waters[w], node.Water, w));
                    w++;
                }
            }

            return targets.AsReadOnly();
        }

        private SubsurfaceTarget TargetOf(TubeNetwork net, WaterSurface water, double waterDepth, int index)
        {
            // The slab of water lies under its surface: its centre is half its depth below it.
            Vec3d surface = net.ToBody(water.Centre);
            Vec3d position = surface - Vec3d.FromVec3(water.Up).Normalized * (waterDepth * 0.5);
            double depth = Math.Max(0, net.SurfaceRadiusNear(water.Centre) - surface.Length);
            BodyFrame.ToLatLon(position, out double lat, out double lon);
            LayerSpec host = strata.LayerAt(depth, biomes.Biome(lat, lon));
            return new SubsurfaceTarget
            {
                Id = $"water:{net.Id}#{index}",
                Kind = TargetKind.Water,
                NetworkId = net.Id,
                PrimitiveIndex = -1,
                Position = position,
                Lat = lat,
                Lon = lon,
                Width = 2.0 * water.Radius,
                Height = waterDepth,
                Length = 2.0 * water.Radius,
                Depth = depth,
                DensityContrast = WaterDensity,
                Host = host,
            };
        }
    }
}
