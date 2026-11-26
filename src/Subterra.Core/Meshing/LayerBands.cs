using System;
using System.Collections.Generic;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// The rock layers a chunk's walls cross: each triangle takes the layer at its own depth, so a shaft kilometres deep
    /// changes rock where the layer changes, not at a chunk's edge (a chunk used to take the layer at its centre).
    /// </summary>
    public static class LayerBands
    {
        /// <summary>
        /// The triangles of <paramref name="mesh"/> grouped by layer (indices into its triangle list, three per triangle),
        /// in order of first appearance; null when the whole mesh lies in one layer (the usual case: one material).
        /// </summary>
        /// <param name="depthOf">Depth (m, down from the surface) of a point of the mesh.</param>
        /// <param name="layerAt">The layer at a depth (null where there is none).</param>
        public static List<(LayerSpec Layer, int[] Triangles)> Split(MeshData mesh, Func<Vec3, double> depthOf, Func<double, LayerSpec> layerAt)
        {
            List<int> tris = mesh.Triangles;
            int count = tris.Count / 3;
            if (count == 0)
            {
                return null;
            }

            var layerOf = new LayerSpec[count];
            LayerSpec first = null;
            bool several = false;
            for (int t = 0; t < count; t++)
            {
                Vec3 a = mesh.Vertices[tris[t * 3]], b = mesh.Vertices[tris[t * 3 + 1]], c = mesh.Vertices[tris[t * 3 + 2]];
                var centre = new Vec3((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, (a.Z + b.Z + c.Z) / 3);
                LayerSpec layer = layerAt(Math.Max(0, depthOf(centre)));
                layerOf[t] = layer;
                if (t == 0)
                {
                    first = layer;
                }
                else if (!ReferenceEquals(layer, first))
                {
                    several = true;
                }
            }

            if (!several)
            {
                return null;
            }

            var order = new List<LayerSpec>();
            var lists = new List<List<int>>();
            for (int t = 0; t < count; t++)
            {
                int k = IndexOf(order, layerOf[t]);
                if (k < 0)
                {
                    k = order.Count;
                    order.Add(layerOf[t]);
                    lists.Add(new List<int>());
                }

                lists[k].Add(tris[t * 3]);
                lists[k].Add(tris[t * 3 + 1]);
                lists[k].Add(tris[t * 3 + 2]);
            }

            var result = new List<(LayerSpec, int[])>(order.Count);
            for (int k = 0; k < order.Count; k++)
            {
                result.Add((order[k], lists[k].ToArray()));
            }

            return result;
        }

        private static int IndexOf(List<LayerSpec> order, LayerSpec layer)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (ReferenceEquals(order[i], layer))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
