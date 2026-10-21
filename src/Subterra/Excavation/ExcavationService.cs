using System;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Excavation;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;

namespace Subterra
{
    /// <summary>
    /// The tunnels dug in the current game (sub-project 2): keeps them per body, builds their networks in the
    /// catalog, joins a tunnel into the cave it reaches, and tells the streamer what to mesh again. Main thread.
    /// Natural networks stay the same for everyone; tunnels belong to the save (SubterraScenario).
    /// </summary>
    public static class ExcavationService
    {
        private static readonly List<ExcavationSpec> Specs = new List<ExcavationSpec>();

        // The version of each network before any tunnel was joined into it, by body + id: joins are always
        // rebuilt from it, so pieces are never added twice.
        private static readonly Dictionary<string, TubeNetwork> Bases = new Dictionary<string, TubeNetwork>();
        private static int nextNumber = 1;
        private static bool subscribed;

        public static IReadOnlyList<ExcavationSpec> All => Specs;

        public static ExcavationSpec Find(string id) => Specs.Find(s => s.Id == id);

        /// <summary>Starts a tunnel whose floor begins at <paramref name="floor"/> (body coordinates).</summary>
        public static ExcavationSpec Start(BodyRuntime runtime, Vec3d floor, float radius)
        {
            Subscribe();
            var spec = new ExcavationSpec($"XP-{runtime.Body.bodyName}-{nextNumber++}", runtime.Body.bodyName, floor, radius);
            spec.Append(Vec3.Zero);
            Specs.Add(spec);
            return spec;
        }

        /// <summary>Digs <paramref name="spec"/> on to <paramref name="floor"/> and updates the networks around it.</summary>
        public static void Dig(BodyRuntime runtime, ExcavationSpec spec, Vec3d floor)
        {
            int piece = spec.Append(spec.ToLocal(floor));
            if (piece < 0)
            {
                return;
            }

            if (spec.JoinedTo == null)
            {
                TubeNetwork reached = Reached(runtime, spec, piece);
                if (reached != null)
                {
                    spec.JoinedTo = reached.Id;
                    runtime.Catalog.RemoveDug(spec.Id);
                    ChunkStreamer.Instance?.NetworkRemoved(spec.Id);
                    Log.Info($"Tunnel {spec.Id} reached {reached.Id}");
                }
            }

            Build(runtime, spec, piece);
        }

        /// <summary>Forgets every tunnel (another save is loading): the networks they changed go back to how they were.</summary>
        public static void Reset()
        {
            foreach (BodyRuntime runtime in BodyRegistry.Instance.Bodies.Values)
            {
                foreach (ExcavationSpec spec in Specs.Where(s => s.Body == runtime.Body.bodyName))
                {
                    if (runtime.Catalog.RemoveDug(spec.Id))
                    {
                        ChunkStreamer.Instance?.NetworkRemoved(spec.Id);
                    }
                }

                foreach (KeyValuePair<string, TubeNetwork> pair in Bases.Where(p => p.Key.StartsWith(runtime.Body.bodyName + "/", StringComparison.Ordinal)).ToList())
                {
                    if (runtime.Catalog.Replace(pair.Value))
                    {
                        ChunkStreamer.Instance?.NetworkChanged(pair.Value, pair.Value.Bounds);
                    }
                }
            }

            Specs.Clear();
            Bases.Clear();
            nextNumber = 1;
        }

        public static void Save(ConfigNode node)
        {
            node.AddValue("nextExcavation", nextNumber);
            foreach (ExcavationSpec spec in Specs)
            {
                node.AddNode(CfgAdapter.ToKsp(spec.ToCfg()));
            }
        }

        public static void Load(ConfigNode node)
        {
            Reset();
            Subscribe();
            if (int.TryParse(node.GetValue("nextExcavation"), out int next))
            {
                nextNumber = Math.Max(1, next);
            }

            var warnings = new List<string>();
            foreach (ConfigNode n in node.GetNodes("EXCAVATION"))
            {
                ExcavationSpec spec = ExcavationSpec.FromCfg(CfgAdapter.ToCore(n), warnings);
                if (spec != null && spec.Points.Count > 0)
                {
                    Specs.Add(spec);
                }
            }

            foreach (string warning in warnings)
            {
                Log.Warn(warning);
            }

            foreach (BodyRuntime runtime in BodyRegistry.Instance.Bodies.Values)
            {
                foreach (ExcavationSpec spec in Specs.Where(s => s.Body == runtime.Body.bodyName))
                {
                    Build(runtime, spec, spec.PieceCount - 1);
                }
            }

            if (Specs.Count > 0)
            {
                Log.Info($"Loaded {Specs.Count} dug tunnels");
            }
        }

        private static void Subscribe()
        {
            if (!subscribed)
            {
                subscribed = true;
                CellScheduler.NetworksAdded += OnNetworksAdded;
            }
        }

        // A cell generated after loading: tunnels joined into its networks are joined again.
        private static void OnNetworksAdded(BodyRuntime runtime, List<TubeNetwork> networks)
        {
            foreach (TubeNetwork net in networks)
            {
                foreach (ExcavationSpec spec in Specs.Where(s => s.Body == runtime.Body.bodyName && s.JoinedTo == net.Id).ToList())
                {
                    Build(runtime, spec, spec.PieceCount - 1);
                }
            }
        }

        // A network (other than dug tunnels) whose void the piece touches, or null.
        private static TubeNetwork Reached(BodyRuntime runtime, ExcavationSpec spec, int piece)
        {
            Vec3d a = spec.ToBody(spec.Points[piece]), b = spec.ToBody(spec.Points[piece + 1]);
            foreach (TubeNetwork net in runtime.Catalog.NetworksNear(b, spec.Radius + 50))
            {
                if (net.Id.StartsWith("XP-", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Vec3d end in new[] { a, b, (a + b) * 0.5 })
                {
                    Vec3 lift = net.UpAt(net.ToLocal(end)) * spec.Radius;
                    if (net.Sdf.Distance(net.ToLocal(end) + lift) < 0)
                    {
                        return net;
                    }
                }
            }

            return null;
        }

        // (Re)builds the network the tunnel lives in after piece `changed` (-1: nothing to remesh yet).
        private static void Build(BodyRuntime runtime, ExcavationSpec spec, int changed)
        {
            if (spec.PieceCount == 0)
            {
                return;
            }

            CelestialBody body = runtime.Body;
            Samples(body, spec, out List<Vec3d> dirs, out List<double> radii);
            List<Skylight> openings = Openings(body, spec);
            TubeNetwork alone = TubeNetwork.ForExcavation(spec, dirs, radii, openings);
            if (openings.Count > 0)
            {
                // Keyed by the tunnel's own id: cut once, kept when the tunnel joins a cave.
                SkylightInjector.AddNetworks(runtime, new[] { alone });
            }

            if (spec.JoinedTo == null)
            {
                runtime.Catalog.PutDug(alone);
                ChunkStreamer.Instance?.NetworkChanged(alone, changed >= 0 ? spec.PieceBounds(changed) : alone.Bounds);
                return;
            }

            string key = body.bodyName + "/" + spec.JoinedTo;
            if (!Bases.TryGetValue(key, out TubeNetwork baseNet))
            {
                baseNet = runtime.Catalog.Find(spec.JoinedTo);
                if (baseNet == null)
                {
                    return; // its cell is not generated yet: OnNetworksAdded joins it then
                }

                Bases[key] = baseNet;
            }

            // Every tunnel joined into this network, in its frame.
            var pieces = new List<Primitive>();
            var allDirs = new List<Vec3d>();
            var allRadii = new List<double>();
            var allOpenings = new List<Skylight>();
            foreach (ExcavationSpec s in Specs.Where(s => s.Body == body.bodyName && s.JoinedTo == spec.JoinedTo))
            {
                pieces.AddRange(s.Pieces(baseNet.Anchor));
                Samples(body, s, out List<Vec3d> d, out List<double> r);
                allDirs.AddRange(d);
                allRadii.AddRange(r);
                foreach (Skylight o in Openings(body, s))
                {
                    allOpenings.Add(InFrame(o, s, baseNet.Anchor));
                }
            }

            TubeNetwork joined = baseNet.WithPrimitives(pieces, allDirs, allRadii, allOpenings);
            runtime.Catalog.Replace(joined);
            Aabb box = changed >= 0 ? spec.Piece(changed, baseNet.Anchor).Bounds : joined.Bounds;
            ChunkStreamer.Instance?.NetworkChanged(joined, box);
        }

        private static void Samples(CelestialBody body, ExcavationSpec spec, out List<Vec3d> dirs, out List<double> radii)
        {
            dirs = new List<Vec3d>(spec.Points.Count);
            radii = new List<double>(spec.Points.Count);
            foreach (Vec3 p in spec.Points)
            {
                Vec3d dir = spec.ToBody(p).Normalized;
                BodyFrame.ToLatLon(dir, out double lat, out double lon);
                dirs.Add(dir);
                radii.Add(KspSampler.SurfaceRadius(body, lat, lon));
            }
        }

        // The mouth of a tunnel dug from the surface: a hole of the tunnel's size where its first point is.
        private static List<Skylight> Openings(CelestialBody body, ExcavationSpec spec)
        {
            var list = new List<Skylight>();
            Vec3d start = spec.ToBody(spec.Points[0]);
            BodyFrame.ToLatLon(start.Normalized, out double lat, out double lon);
            double surface = KspSampler.SurfaceRadius(body, lat, lon);
            if (Math.Abs(start.Length - surface) <= spec.Radius + 1)
            {
                Vec3d centre = start.Normalized * surface;
                list.Add(new Skylight
                {
                    SurfaceCenter = spec.ToLocal(centre),
                    Up = start.Normalized.ToVec3(),
                    Radius = spec.Radius + 0.5f,
                    Lat = lat,
                    Lon = lon,
                    Index = 0,
                });
            }

            return list;
        }

        private static Skylight InFrame(Skylight o, ExcavationSpec spec, Vec3d frame) => new Skylight
        {
            SurfaceCenter = (spec.ToBody(o.SurfaceCenter) - frame).ToVec3(),
            Up = o.Up,
            Radius = o.Radius,
            Lat = o.Lat,
            Lon = o.Lon,
            Index = 1000 + o.Index,
        };
    }
}
