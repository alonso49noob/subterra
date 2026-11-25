using System;
using System.Collections.Generic;
using Subterra.Core.Procedural;

namespace Subterra.Core.Radar
{
    /// <summary>
    /// Tells which 50 km network cells (<see cref="CellGrid"/>) of a body gained orbital coverage. Each cell gets a
    /// latitude/longitude box of coverage-grid cells (its <see cref="CellGrid.GridRect"/> grown by a margin, so the
    /// parts of its networks that stick out of the cell are inside); the signature of a cell is the sum of the
    /// coverage bytes of both instruments over its box. A coverage byte only goes up, so a signature goes up exactly
    /// when some coverage in the box improved. Comparing with the signatures seen last time gives the cells to look
    /// at again, whatever the scanner did in between (one pass, or a whole warp catch-up of many orbits).
    /// Building it costs one <see cref="CellGrid.GridRect"/> per cell; a full pass reads every coverage byte about
    /// twice (boxes overlap). Both are meant for a worker thread. Not thread-safe: one user at a time.
    /// </summary>
    public sealed class CellCoverageIndex
    {
        // Per cell: first and last coverage row, and one or two column ranges (the second empty unless the box
        // crosses the antimeridian).
        private readonly int[] rowMin, rowMax, colMin, colMax, colMin2, colMax2;

        public CellCoverageIndex(CellGrid grid, double marginMeters)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            int n = grid.CellCount;
            rowMin = new int[n];
            rowMax = new int[n];
            colMin = new int[n];
            colMax = new int[n];
            colMin2 = new int[n];
            colMax2 = new int[n];
            for (int i = 0; i < n; i++)
            {
                var (latMin, latMax, lonMin, lonMax) = grid.GridRect(CellId.FromIndex(i, grid.N), marginMeters);
                rowMin[i] = CoverageGrid.RowOf(latMin);
                rowMax[i] = CoverageGrid.RowOf(latMax);
                colMin2[i] = 0;
                colMax2[i] = -1;
                if (lonMax - lonMin >= 360)
                {
                    colMin[i] = 0;
                    colMax[i] = CoverageGrid.Width - 1;
                    continue;
                }

                int c0 = CoverageGrid.ColumnOf(lonMin), c1 = CoverageGrid.ColumnOf(lonMax);
                if (c0 <= c1)
                {
                    colMin[i] = c0;
                    colMax[i] = c1;
                }
                else
                {
                    // Across the antimeridian: from c0 to the east edge, and from the west edge to c1.
                    colMin[i] = c0;
                    colMax[i] = CoverageGrid.Width - 1;
                    colMin2[i] = 0;
                    colMax2[i] = c1;
                }
            }
        }

        public CellGrid Grid { get; }

        public int CellCount => rowMin.Length;

        /// <summary>The sum of the coverage bytes of both grids (either may be null) over the cell's box.</summary>
        public long Signature(int cell, CoverageGrid a, CoverageGrid b)
        {
            return Sum(cell, a) + Sum(cell, b);
        }

        /// <summary>
        /// The cells whose signature is not the one in <paramref name="last"/> (indexed by cell index, length
        /// <see cref="CellCount"/>), in index order; <paramref name="last"/> is updated to the new signatures.
        /// </summary>
        public List<int> Changed(CoverageGrid a, CoverageGrid b, long[] last)
        {
            if (last == null || last.Length != CellCount)
            {
                throw new ArgumentException("one signature per cell expected", nameof(last));
            }

            var changed = new List<int>();
            for (int i = 0; i < last.Length; i++)
            {
                long s = Signature(i, a, b);
                if (s != last[i])
                {
                    last[i] = s;
                    changed.Add(i);
                }
            }

            return changed;
        }

        private long Sum(int cell, CoverageGrid grid)
        {
            if (grid == null)
            {
                return 0;
            }

            long sum = 0;
            for (int row = rowMin[cell]; row <= rowMax[cell]; row++)
            {
                sum += grid.SumRow(row, colMin[cell], colMax[cell]);
                if (colMax2[cell] >= colMin2[cell])
                {
                    sum += grid.SumRow(row, colMin2[cell], colMax2[cell]);
                }
            }

            return sum;
        }
    }
}
