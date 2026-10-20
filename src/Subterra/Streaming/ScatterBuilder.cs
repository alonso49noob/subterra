using System;
using System.Collections.Generic;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Scatter;
using Subterra.Core.Tubes;

namespace Subterra
{
    /// <summary>One scatter's merged props for a chunk (built on a worker).</summary>
    public sealed class ScatterMesh
    {
        public string Scatter;
        public string Model;
        public MeshData Visual;
        public MeshData Collider;
        public int Instances;
        public List<ScatterInstance> Placed;
    }

    /// <summary>
    /// Places and merges the body's CAVE_SCATTERs over a chunk: LOD0 chunks get LOD0 props, LOD1 chunks
    /// LOD1 props (chunk LODs stand in for the scatter distances), coarser chunks none. Thread-safe.
    /// </summary>
    public static class ScatterBuilder
    {
        public static List<ScatterMesh> Build(MeshData chunk, ChunkKey key, TubeNetwork net, IReadOnlyList<ScatterSpec> scatters)
        {
            var result = new List<ScatterMesh>();
            if (chunk.IsEmpty || key.Lod > 1 || scatters.Count == 0)
            {
                return result;
            }

            foreach (ScatterSpec spec in scatters)
            {
                List<ScatterInstance> placed = ScatterPlacer.Place(chunk, spec, ScatterPlacer.ChunkSeed(key, spec.Name),
                    p => net.UpAt(p), p => net.ZoneAt(p).Kind, p => InRubbleZone(net, p));
                if (placed.Count == 0)
                {
                    continue;
                }

                // Instances go to the scatter's models in turn.
                for (int m = 0; m < spec.Models.Count; m++)
                {
                    var subset = new List<ScatterInstance>();
                    for (int i = m; i < placed.Count; i += spec.Models.Count)
                    {
                        subset.Add(placed[i]);
                    }

                    if (subset.Count == 0 || !PropLibrary.TryGet(spec.Models[m], key.Lod, out MeshData prop))
                    {
                        continue;
                    }

                    var scatter = new ScatterMesh
                    {
                        Scatter = spec.Name,
                        Model = spec.Models[m],
                        Visual = ScatterMerger.Merge(prop, subset, out int dropped),
                        Instances = subset.Count - dropped,
                        Placed = subset,
                    };
                    if (spec.Collide && PropLibrary.TryGet(spec.Models[m], 1, out MeshData lod1))
                    {
                        scatter.Collider = ScatterMerger.Merge(lod1, subset, out _);
                    }

                    result.Add(scatter);
                }
            }

            return result;
        }

        // Floor under a skylight shaft (within 1.2 radii horizontally).
        private static bool InRubbleZone(TubeNetwork net, Vec3 p)
        {
            foreach (Skylight s in net.Skylights)
            {
                Vec3 d = p - s.SurfaceCenter;
                Vec3 horizontal = d - s.Up * Vec3.Dot(d, s.Up);
                if (horizontal.Length < s.Radius * 1.2f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
