using System;
using System.Collections.Generic;
using Subterra.Core.Environment;
using Subterra.Core.Excavation;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Radar
{
    /// <summary>Targets of the rock column under a point: layer boundaries and gas pockets. Stateless.</summary>
    public static class ColumnTargets
    {
        /// <summary>One target per boundary between consecutive layers shallower than <paramref name="maxDepth"/>.</summary>
        public static IReadOnlyList<SubsurfaceTarget> Boundaries(Strata strata, string biome, Vec3d surfacePoint, double maxDepth)
        {
            var result = new List<SubsurfaceTarget>();
            IReadOnlyList<LayerSpan> spans = strata.Spans(biome);
            for (int i = 0; i + 1 < spans.Count; i++)
            {
                LayerSpan above = spans[i];
                LayerSpan below = spans[i + 1];
                double depth = above.Bottom;
                if (!(depth < maxDepth))
                {
                    break;
                }

                result.Add(Make($"layer:{above.Layer.Name}/{below.Layer.Name}", TargetKind.LayerBoundary, surfacePoint, depth,
                    0, Math.Abs(above.Layer.Density - below.Layer.Density), below.Layer));
            }

            return result;
        }

        /// <summary>One target per layer of the column that holds a gas pocket at this place, shallower than <paramref name="maxDepth"/>.</summary>
        public static IReadOnlyList<SubsurfaceTarget> Gas(string body, double lat, double lon, Strata strata, string biome,
            Vec3d surfacePoint, double maxDepth)
        {
            var result = new List<SubsurfaceTarget>();
            long latCell = (long)Math.Floor(lat / GasPocket.CellDegrees);
            long lonCell = (long)Math.Floor(lon / GasPocket.CellDegrees);
            foreach (LayerSpan span in strata.Spans(biome))
            {
                if (!(span.Top < maxDepth))
                {
                    break;
                }

                if (!GasPocket.Hit(body, lat, lon, span.Layer))
                {
                    continue;
                }

                // decidido por Claude, revisar: a pocket fills its whole layer interval and takes the layer's density.
                result.Add(Make($"gas:{body}:{span.Layer.Name}:{latCell},{lonCell}", TargetKind.Gas, surfacePoint, span.Top,
                    span.Bottom - span.Top, span.Layer.Density, span.Layer));
            }

            return result;
        }

        private static SubsurfaceTarget Make(string id, TargetKind kind, Vec3d surfacePoint, double depth, double height,
            double density, LayerSpec host)
        {
            Vec3d position = surfacePoint - surfacePoint.Normalized * depth;
            BodyFrame.ToLatLon(position, out double lat, out double lon);
            return new SubsurfaceTarget
            {
                Id = id,
                Kind = kind,
                NetworkId = null,
                PrimitiveIndex = -1,
                Position = position,
                Lat = lat,
                Lon = lon,
                Width = double.PositiveInfinity,
                Height = height,
                Length = double.PositiveInfinity,
                Depth = depth,
                DensityContrast = density,
                Host = host,
            };
        }
    }
}
