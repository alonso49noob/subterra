using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Subterra.Core.Radar
{
    /// <summary>
    /// What the radar has found so far for one cave network: the best level reached, the smallest uncertainty and the
    /// depth range that went with it, the centroid of the targets seen, and which primitives were found at the
    /// <see cref="DetectionLevel.Detected"/> level. Instances handed out by <see cref="DetectionStore"/> are copies.
    /// </summary>
    public sealed class NetworkDetection
    {
        private BitArray _primitives = new BitArray(0);

        // Running centroid state (decidido por Claude, revisar): latitude is a plain mean, longitude is averaged on the
        // unit circle so a network straddling +-180 degrees does not average to 0.
        internal int Count;
        internal double SumLat;
        internal double SumSin;
        internal double SumCos;
        internal HashSet<string> SeenTargets = new HashSet<string>();

        public string NetworkId { get; set; }

        public string Body { get; set; }

        /// <summary>The best level reached so far.</summary>
        public DetectionLevel Level { get; set; }

        /// <summary>The smallest horizontal uncertainty (metres) of any detection applied.</summary>
        public double Uncertainty { get; set; }

        /// <summary>Depth range (metres) of the detection that gave the smallest uncertainty.</summary>
        public double DepthMin { get; set; }

        public double DepthMax { get; set; }

        /// <summary>Centroid of the targets applied to this network (degrees).</summary>
        public double Lat { get; set; }

        public double Lon { get; set; }

        /// <summary>Primitives detected at the Detected level; bit i is primitive i of the network.</summary>
        public BitArray Primitives
        {
            get => _primitives;
            set => _primitives = value == null ? new BitArray(0) : value;
        }

        /// <summary>
        /// The bit set as hex, little-endian: byte k holds bits 8k..8k+7 (bit 8k is the lowest bit), each byte as two
        /// lowercase hex characters. The length is rounded up to whole bytes; an empty set gives "".
        /// </summary>
        public string PrimitivesHex
        {
            get
            {
                var bits = _primitives;
                var bytes = (bits.Length + 7) / 8;
                var sb = new StringBuilder(bytes * 2);
                for (var k = 0; k < bytes; k++)
                {
                    var b = 0;
                    for (var j = 0; j < 8; j++)
                    {
                        var i = k * 8 + j;
                        if (i < bits.Length && bits[i]) b |= 1 << j;
                    }

                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Parses the output of <see cref="PrimitivesHex"/>. Anything that is not an even number of hex digits (or is
        /// null) gives an empty set and ok = false; it never throws. Upper-case digits are accepted.
        /// </summary>
        public static BitArray FromHex(string hex, out bool ok)
        {
            ok = false;
            if (hex == null || hex.Length % 2 != 0) return new BitArray(0);
            var bytes = hex.Length / 2;
            var bits = new BitArray(bytes * 8);
            for (var k = 0; k < bytes; k++)
            {
                var hi = Digit(hex[2 * k]);
                var lo = Digit(hex[2 * k + 1]);
                if (hi < 0 || lo < 0) return new BitArray(0);
                var b = hi * 16 + lo;
                for (var j = 0; j < 8; j++) bits[k * 8 + j] = (b & (1 << j)) != 0;
            }

            ok = true;
            return bits;
        }

        private static int Digit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        internal NetworkDetection Clone() => new NetworkDetection
        {
            NetworkId = NetworkId, Body = Body, Level = Level, Uncertainty = Uncertainty, DepthMin = DepthMin,
            DepthMax = DepthMax, Lat = Lat, Lon = Lon, _primitives = (BitArray)_primitives.Clone(),
            Count = Count, SumLat = SumLat, SumSin = SumSin, SumCos = SumCos,
        };

        internal void AddToCentroid(double lat, double lon)
        {
            Count++;
            SumLat += lat;
            var r = lon * Math.PI / 180.0;
            SumSin += Math.Sin(r);
            SumCos += Math.Cos(r);
            Lat = SumLat / Count;
            if (Math.Sqrt(SumSin * SumSin + SumCos * SumCos) > 1e-9)
                Lon = Math.Atan2(SumSin, SumCos) * 180.0 / Math.PI;
        }

        internal void SeedCentroid()
        {
            Count = 1;
            SumLat = Lat;
            var r = Lon * Math.PI / 180.0;
            SumSin = Math.Sin(r);
            SumCos = Math.Cos(r);
        }
    }

    /// <summary>
    /// The per-save record of radar findings, one <see cref="NetworkDetection"/> per cave network.
    /// Threading: written on KSP's main thread, read from a worker (map overlay). All state is guarded by one lock;
    /// every <see cref="NetworkDetection"/> that leaves the store is a copy, and <see cref="All"/> is a snapshot, so a
    /// worker can enumerate it while the main thread keeps applying detections. <see cref="LevelRaised"/> is raised
    /// on the calling thread after the lock is released.
    /// </summary>
    public sealed class DetectionStore
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, NetworkDetection> _byNetwork = new Dictionary<string, NetworkDetection>();
        private int _version;

        /// <summary>
        /// Rises every time the records change (a detection that improved anything, <see cref="Put"/>,
        /// <see cref="Clear"/>), so a reader that caches something derived from them can tell it is stale with one
        /// int compare. Starts at 0 for each store: a reader that outlives a store must also compare the store itself.
        /// </summary>
        public int Version
        {
            get { lock (_lock) return _version; }
        }

        /// <summary>
        /// Fires once each time a network's best level rises (None to Hint, Hint to Detected, None straight to
        /// Detected). The arguments are a copy of the network's record and the new level.
        /// </summary>
        public event Action<NetworkDetection, DetectionLevel> LevelRaised;

        /// <summary>
        /// Folds one detection of one target into the record of its network. Returns true if anything improved: the
        /// level, the uncertainty, or a primitive detected for the first time. Targets without a network (column
        /// targets) and detections at level None are ignored. The centroid counts each target Id once (decidido por
        /// Claude, revisar), so rescanning one target does not drag it. Uncertainty is the smallest of all detections
        /// and the depth range is the one that came with it (decidido por Claude, revisar).
        /// </summary>
        public bool Apply(string body, SubsurfaceTarget t, Detection d)
        {
            if (t == null || string.IsNullOrEmpty(t.NetworkId) || d.Level == DetectionLevel.None) return false;

            NetworkDetection raisedCopy = null;
            var improved = false;
            var changed = false;
            lock (_lock)
            {
                if (!_byNetwork.TryGetValue(t.NetworkId, out var n))
                {
                    changed = true;
                    n = new NetworkDetection { NetworkId = t.NetworkId, Body = body, Level = DetectionLevel.None };
                    _byNetwork[t.NetworkId] = n;
                }

                if (t.Id == null || n.SeenTargets.Add(t.Id))
                {
                    n.AddToCentroid(t.Lat, t.Lon);
                    changed = true;
                }

                var first = n.Level == DetectionLevel.None;
                if (d.Level > n.Level)
                {
                    n.Level = d.Level;
                    improved = true;
                    raisedCopy = n.Clone();
                }

                if (first || d.Uncertainty < n.Uncertainty)
                {
                    n.Uncertainty = d.Uncertainty;
                    n.DepthMin = d.DepthMin;
                    n.DepthMax = d.DepthMax;
                    improved = true;
                }

                if (d.Level == DetectionLevel.Detected && t.PrimitiveIndex >= 0)
                {
                    var bits = n.Primitives;
                    if (t.PrimitiveIndex >= bits.Length) bits.Length = t.PrimitiveIndex + 1;
                    if (!bits[t.PrimitiveIndex])
                    {
                        bits[t.PrimitiveIndex] = true;
                        improved = true;
                    }
                }

                if (raisedCopy != null) raisedCopy = n.Clone();
                if (changed || improved) _version++;
            }

            if (raisedCopy != null) LevelRaised?.Invoke(raisedCopy, raisedCopy.Level);
            return improved;
        }

        /// <summary>Forgets every record. Does not fire <see cref="LevelRaised"/>.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _byNetwork.Clear();
                _version++;
            }
        }

        /// <summary>A copy of the record for the network, or null if nothing was found there.</summary>
        public NetworkDetection Get(string networkId)
        {
            if (networkId == null) return null;
            lock (_lock) return _byNetwork.TryGetValue(networkId, out var n) ? n.Clone() : null;
        }

        public bool IsPrimitiveDetected(string networkId, int index)
        {
            if (networkId == null || index < 0) return false;
            lock (_lock)
            {
                if (!_byNetwork.TryGetValue(networkId, out var n)) return false;
                return index < n.Primitives.Length && n.Primitives[index];
            }
        }

        /// <summary>A snapshot: copies of every record, safe to enumerate from another thread.</summary>
        public IEnumerable<NetworkDetection> All
        {
            get
            {
                lock (_lock) return _byNetwork.Values.Select(n => n.Clone()).ToList();
            }
        }

        /// <summary>
        /// Stores a copy of a loaded record, replacing any record of that network. Does not fire
        /// <see cref="LevelRaised"/>. The loaded centroid counts as one target for later centroid updates.
        /// </summary>
        public void Put(NetworkDetection d)
        {
            if (d == null || string.IsNullOrEmpty(d.NetworkId)) return;
            var copy = d.Clone();
            copy.SeedCentroid();
            lock (_lock)
            {
                _byNetwork[copy.NetworkId] = copy;
                _version++;
            }
        }
    }
}
