using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>Uniform hash grid over primitive bounds. Immutable after construction (thread-safe reads).</summary>
    public sealed class SpatialGrid
    {
        private readonly IReadOnlyList<Primitive> prims;
        private readonly float cellSize;
        private readonly Dictionary<(int, int, int), int[]> cells = new Dictionary<(int, int, int), int[]>();

        [ThreadStatic]
        private static HashSet<int> Seen;

        public SpatialGrid(IReadOnlyList<Primitive> prims, float cellSize = 128f)
        {
            this.prims = prims;
            this.cellSize = cellSize;
            var building = new Dictionary<(int, int, int), List<int>>();
            for (int i = 0; i < prims.Count; i++)
            {
                Aabb b = prims[i].Bounds;
                Cell(b.Min, out int x0, out int y0, out int z0);
                Cell(b.Max, out int x1, out int y1, out int z1);
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int z = z0; z <= z1; z++)
                        {
                            if (!building.TryGetValue((x, y, z), out List<int> list))
                            {
                                list = new List<int>();
                                building[(x, y, z)] = list;
                            }

                            list.Add(i);
                        }
                    }
                }
            }

            foreach (var pair in building)
            {
                cells[pair.Key] = pair.Value.ToArray();
            }
        }

        public float CellSize => cellSize;

        /// <summary>Adds (once each, ascending) the primitives whose bounds intersect <paramref name="box"/>.</summary>
        public void Query(Aabb box, List<int> results)
        {
            Cell(box.Min, out int x0, out int y0, out int z0);
            Cell(box.Max, out int x1, out int y1, out int z1);
            HashSet<int> seen = Seen ?? (Seen = new HashSet<int>());
            seen.Clear();
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!cells.TryGetValue((x, y, z), out int[] list))
                        {
                            continue;
                        }

                        foreach (int i in list)
                        {
                            if (seen.Add(i) && prims[i].Bounds.Intersects(box))
                            {
                                results.Add(i);
                            }
                        }
                    }
                }
            }

            results.Sort();
        }

        /// <summary>The primitives listed in the cell that contains <paramref name="p"/> (no copy; may be empty).</summary>
        public int[] At(Vec3 p)
        {
            Cell(p, out int x, out int y, out int z);
            return cells.TryGetValue((x, y, z), out int[] list) ? list : Array.Empty<int>();
        }

        /// <summary>The grid cell that contains <paramref name="p"/> (the one <see cref="At"/> reads).</summary>
        internal Aabb CellBox(Vec3 p)
        {
            Cell(p, out int x, out int y, out int z);
            var min = new Vec3(x * cellSize, y * cellSize, z * cellSize);
            return new Aabb(min, min + new Vec3(cellSize, cellSize, cellSize));
        }

        private void Cell(Vec3 p, out int x, out int y, out int z)
        {
            x = (int)Math.Floor(p.X / cellSize);
            y = (int)Math.Floor(p.Y / cellSize);
            z = (int)Math.Floor(p.Z / cellSize);
        }
    }
}
