using System;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;

namespace Subterra.Core.Model
{
    /// <summary>
    /// A block of other rock inside a body, off its centre, not a layer round it (Lucas: Gilly as the fragment of a larger,
    /// differentiated body: a lump of its parent's iron core and blocks of its mantle's olivine, mixed into the rubble).
    /// A lumpy sphere: its centre <see cref="Depth"/> metres under the datum at (<see cref="Lat"/>, <see cref="Lon"/>), its
    /// radius <see cref="Radius"/>, wandering in and out by <see cref="Lumpiness"/>. Inside it, its rock (<see cref="Layer"/>)
    /// replaces the layers for meshes, drills, samples, science, resources and the layer map.
    /// </summary>
    public sealed class InclusionSpec
    {
        public string Name { get; set; }

        public double Lat { get; set; }

        public double Lon { get; set; }

        /// <summary>Depth (m) of its centre under the body's datum.</summary>
        public double Depth { get; set; }

        public double Radius { get; set; } = 500;

        /// <summary>How far its surface wanders in and out (share of the radius).</summary>
        public double Lumpiness { get; set; } = 0.2;

        /// <summary>Its rock (name, display name, hardness, density, tint, texture, yields).</summary>
        public LayerSpec Layer { get; set; }

        /// <summary>Its centre in the body frame, for a body of <paramref name="bodyRadius"/>.</summary>
        public Vec3d Centre(double bodyRadius) => BodyFrame.Direction(Lat, Lon) * (bodyRadius - Depth);

        /// <summary>Whether <paramref name="bodyPos"/> (body frame) is inside it.</summary>
        public bool Contains(Vec3d bodyPos, double bodyRadius)
        {
            Vec3d d = bodyPos - Centre(bodyRadius);
            double length = d.Length;
            double most = Radius * (1 + Lumpiness);
            if (length > most)
            {
                return false;
            }

            if (length < Radius * (1 - Lumpiness))
            {
                return true;
            }

            return length < SurfaceRadius(d / Math.Max(1e-9, length));
        }

        /// <summary>Its radius (m) in a direction from its centre (a smooth lumpy surface, the same every time).</summary>
        public double SurfaceRadius(Vec3d direction)
        {
            ulong seed = Hash.Of(Name ?? "inclusion");
            var p = new Vec3((float)(direction.X * 1.7), (float)(direction.Y * 1.7), (float)(direction.Z * 1.7));
            double n = Noise.Fbm(p, seed, 3);
            return Radius * (1 + Lumpiness * Math.Max(-1, Math.Min(1, n * 2)));
        }
    }
}
