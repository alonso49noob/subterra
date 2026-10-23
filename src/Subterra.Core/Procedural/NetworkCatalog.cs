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
        private volatile Snapshot current = new Snapshot(new List<TubeNetwork>(), new Dictionary<CellId, IReadOnlyList<TubeNetwork>>(), new List<TubeNetwork>());

        public NetworkCatalog(BodySpec spec, double bodyRadius)
        {
            this.spec = spec;
            Grid = new CellGrid(bodyRadius);
        }

        public CellGrid Grid { get; }

        public BodySpec Spec => spec;

        public IReadOnlyList<TubeNetwork> Named => current.Named;

        /// <summary>Every network known so far (named first, then generated cells, then tunnels dug alone).</summary>
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

                foreach (TubeNetwork net in snapshot.Dug)
                {
                    yield return net;
                }
            }
        }

        public int ReadyCellCount => current.Cells.Count;

        public void AddNamed(TubeNetwork network)
        {
            Snapshot old = current;
            var named = new List<TubeNetwork>(old.Named) { network };
            current = new Snapshot(named, old.Cells, old.Dug);
        }

        public bool IsCellReady(CellId c) => current.Cells.ContainsKey(c);

        public void AddCell(CellId c, List<TubeNetwork> networks)
        {
            Snapshot old = current;
            var cells = new Dictionary<CellId, IReadOnlyList<TubeNetwork>>(old.Cells) { [c] = networks.ToArray() };
            current = new Snapshot(old.Named, cells, old.Dug);
        }

        /// <summary>Adds a tunnel dug by the player that stands alone, or replaces it if one with its id is there.</summary>
        public void PutDug(TubeNetwork network)
        {
            Snapshot old = current;
            var dug = new List<TubeNetwork>(old.Dug);
            int index = dug.FindIndex(n => n.Id == network.Id);
            if (index >= 0)
            {
                dug[index] = network;
            }
            else
            {
                dug.Add(network);
            }

            current = new Snapshot(old.Named, old.Cells, dug);
        }

        /// <summary>Removes a standalone dug tunnel (it joined another network); false if it was not there.</summary>
        public bool RemoveDug(string id)
        {
            Snapshot old = current;
            var dug = new List<TubeNetwork>(old.Dug);
            if (dug.RemoveAll(n => n.Id == id) == 0)
            {
                return false;
            }

            current = new Snapshot(old.Named, old.Cells, dug);
            return true;
        }

        /// <summary>
        /// Swaps the network with <paramref name="network"/>'s id (named, in a cell or dug) for it: a network a tunnel
        /// was joined into. False if no network has that id.
        /// </summary>
        public bool Replace(TubeNetwork network)
        {
            Snapshot old = current;
            int named = old.Named.FindIndex(n => n.Id == network.Id);
            if (named >= 0)
            {
                var list = new List<TubeNetwork>(old.Named) { [named] = network };
                current = new Snapshot(list, old.Cells, old.Dug);
                return true;
            }

            foreach (KeyValuePair<CellId, IReadOnlyList<TubeNetwork>> cell in old.Cells)
            {
                for (int i = 0; i < cell.Value.Count; i++)
                {
                    if (cell.Value[i].Id == network.Id)
                    {
                        var nets = new TubeNetwork[cell.Value.Count];
                        for (int j = 0; j < nets.Length; j++)
                        {
                            nets[j] = j == i ? network : cell.Value[j];
                        }

                        var cells = new Dictionary<CellId, IReadOnlyList<TubeNetwork>>(old.Cells) { [cell.Key] = nets };
                        current = new Snapshot(old.Named, cells, old.Dug);
                        return true;
                    }
                }
            }

            if (old.Dug.Exists(n => n.Id == network.Id))
            {
                PutDug(network);
                return true;
            }

            return false;
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

        /// <summary>A network whose void is within <paramref name="margin"/> m of the point (or contains it), or null.</summary>
        public TubeNetwork FindNear(Vec3d bodyPos, float margin)
        {
            foreach (TubeNetwork net in All)
            {
                Vec3 local = net.ToLocal(bodyPos);
                if (net.Bounds.Contains(local) && net.Sdf.Distance(local) < margin)
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
            public Snapshot(List<TubeNetwork> named, Dictionary<CellId, IReadOnlyList<TubeNetwork>> cells, List<TubeNetwork> dug)
            {
                Named = named;
                Cells = cells;
                Dug = dug;
            }

            public List<TubeNetwork> Named { get; }

            public Dictionary<CellId, IReadOnlyList<TubeNetwork>> Cells { get; }

            /// <summary>Tunnels dug by the player that do not reach any network yet.</summary>
            public List<TubeNetwork> Dug { get; }
        }
    }
}
