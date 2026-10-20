using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// Which chunks to show and which need colliders. Octree over LOD3 chunks: a chunk of level L &gt; 0
    /// splits into its 8 children when the camera is nearer than <c>Lods[L-1].maxDist</c>; nothing
    /// beyond 4096 m. Subtrees whose box has no surface (<c>hasSurface</c>) are skipped.
    /// </summary>
    public static class ChunkPlanner
    {
        public static readonly (int lod, float maxDist)[] Lods = { (0, 64f), (1, 256f), (2, 1024f), (3, 4096f) };

        public const int TopLod = 3;

        public static void Desired(string netId, Aabb netBounds, Vec3 camera, Func<Aabb, bool> hasSurface, ISet<ChunkKey> result)
        {
            float maxDist = Lods[TopLod].maxDist;
            if (netBounds.DistanceTo(camera) > maxDist)
            {
                return;
            }

            float size = SurfaceNets.N * (1 << TopLod);
            Range(netBounds, size, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        var key = new ChunkKey(netId, TopLod, x, y, z);
                        if (key.Bounds.DistanceTo(camera) <= maxDist)
                        {
                            Visit(key, camera, hasSurface, result);
                        }
                    }
                }
            }
        }

        /// <summary>LOD0 chunks touching a sphere of <paramref name="radius"/> around each vessel.</summary>
        public static void Colliders(string netId, IEnumerable<Vec3> vessels, float radius, Func<Aabb, bool> hasSurface, ISet<ChunkKey> result)
        {
            float size = SurfaceNets.N;
            foreach (Vec3 v in vessels)
            {
                var sphereBox = new Aabb(v, v).Expand(radius);
                Range(sphereBox, size, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int z = z0; z <= z1; z++)
                        {
                            var key = new ChunkKey(netId, 0, x, y, z);
                            Aabb box = key.Bounds;
                            if (box.DistanceTo(v) <= radius && hasSurface(box))
                            {
                                result.Add(key);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>The four (or fewer) children of a chunk one level down that a box covers.</summary>
        public static IEnumerable<ChunkKey> Children(ChunkKey key)
        {
            for (int i = 0; i < 8; i++)
            {
                yield return new ChunkKey(key.NetworkId, key.Lod - 1, key.X * 2 + (i & 1), key.Y * 2 + (i >> 1 & 1), key.Z * 2 + (i >> 2 & 1));
            }
        }

        private static void Visit(ChunkKey key, Vec3 camera, Func<Aabb, bool> hasSurface, ISet<ChunkKey> result)
        {
            Aabb box = key.Bounds;
            if (!hasSurface(box))
            {
                return;
            }

            if (key.Lod > 0 && box.DistanceTo(camera) < Lods[key.Lod - 1].maxDist)
            {
                foreach (ChunkKey child in Children(key))
                {
                    Visit(child, camera, hasSurface, result);
                }

                return;
            }

            result.Add(key);
        }

        private static void Range(Aabb box, float size, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)
        {
            x0 = (int)Math.Floor(box.Min.X / size);
            y0 = (int)Math.Floor(box.Min.Y / size);
            z0 = (int)Math.Floor(box.Min.Z / size);
            x1 = (int)Math.Floor(box.Max.X / size);
            y1 = (int)Math.Floor(box.Max.Y / size);
            z1 = (int)Math.Floor(box.Max.Z / size);
        }
    }
}
