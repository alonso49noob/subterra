using System.Collections.Generic;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;

namespace Subterra.Core.Procedural
{
    /// <summary>
    /// The networks of one body: named sites plus procedural cells generated on demand. Readers on any
    /// thread see immutable snapshots; writes (AddNamed, AddCell) must come from one thread (KSP's main thread).
    /// </summary>
    public sealed class NetworkCatalog
    {
        private readonly BodySpec spec;
        private volatile Snapshot current = new Snapshot(new List<TubeNetwork>(), new Dictionary<CellId, IReadOnlyList<TubeNetwork>>());

        public NetworkCatalog(BodySpec spec, double bodyRadius)
        {
            this.spec = spec;
            Grid = new CellGrid(bodyRadius);
        }

        public CellGrid Grid { get; }

        public BodySpec Spec => spec;

        public IReadOnlyList<TubeNetwork> Named => current.Named;

        /// <summary>Every network known so far (named first, then generated cells).</summary>
        public IEnumerable<TubeNetwork> All
        {
            get
            {
                Snapshot snapshot = current;
                foreach (TubeNetwork net in snapshot.Named)
                {
                    yield return net;
                }

                foreach (IReadOnlyList<TubeNetwork> cell in snapshot.Cells.Values)
                {
                    foreach (TubeNetwork net in cell)
                    {
                        yield return net;
                    }
                }
            }
        }

        public int ReadyCellCount => current.Cells.Count;

        public void AddNamed(TubeNetwork network)
        {
            Snapshot old = current;
            var named = new List<TubeNetwork>(old.Named) { network };
            current = new Snapshot(named, old.Cells);
        }

        public bool IsCellReady(CellId c) => current.Cells.ContainsKey(c);

        public void AddCell(CellId c, List<TubeNetwork> networks)
        {
            Snapshot old = current;
            var cells = new Dictionary<CellId, IReadOnlyList<TubeNetwork>>(old.Cells) { [c] = networks.ToArray() };
            current = new Snapshot(old.Named, cells);
        }

        /// <summary>Cells near the point whose procedural networks have not been generated (none without TUBE_FIELD).</summary>
        public IEnumerable<CellId> MissingCellsNear(Vec3d bodyPos, double radius)
        {
            if (spec.Fields.Count == 0)
            {
                yield break;
            }

            Snapshot snapshot = current;
            foreach (CellId c in Grid.Near(bodyPos.Normalized, radius))
            {
                if (!snapshot.Cells.ContainsKey(c))
                {
                    yield return c;
                }
            }
        }

        /// <summary>Networks whose bounds come within <paramref name="radius"/> of the point.</summary>
        public IEnumerable<TubeNetwork> NetworksNear(Vec3d bodyPos, double radius)
        {
            foreach (TubeNetwork net in All)
            {
                if (net.Bounds.DistanceTo(net.ToLocal(bodyPos)) <= radius)
                {
                    yield return net;
                }
            }
        }

        /// <summary>The network whose void contains the point, or null.</summary>
        public TubeNetwork FindContaining(Vec3d bodyPos)
        {
            foreach (TubeNetwork net in All)
            {
                Vec3 local = net.ToLocal(bodyPos);
                if (net.Bounds.Contains(local) && net.IsInside(local))
                {
                    return net;
                }
            }

            return null;
        }

        public TubeNetwork Find(string id)
        {
            foreach (TubeNetwork net in All)
            {
                if (net.Id == id)
                {
                    return net;
                }
            }

            return null;
        }

        private sealed class Snapshot
        {
            public Snapshot(List<TubeNetwork> named, Dictionary<CellId, IReadOnlyList<TubeNetwork>> cells)
            {
                Named = named;
                Cells = cells;
            }

            public List<TubeNetwork> Named { get; }

            public Dictionary<CellId, IReadOnlyList<TubeNetwork>> Cells { get; }
        }
    }
}
