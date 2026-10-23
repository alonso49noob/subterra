using System;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Excavation;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;
using UnityEngine;

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

        private static readonly List<ArmedBlast> Armed = new List<ArmedBlast>();
        private static int nextBlast = 1;

        public static IReadOnlyList<ArmedBlast> ArmedBlasts => Armed;

        /// <summary>Leaves a loaded blast hole at (lat, lon), independent of the vessel that loaded it.</summary>
        public static ArmedBlast Arm(BodyRuntime runtime, double lat, double lon, BlastDesign design)
        {
            Subscribe();
            var blast = new ArmedBlast
            {
                Id = $"BL-{runtime.Body.bodyName}-{nextBlast++}",
                Body = runtime.Body.bodyName,
                Lat = lat,
                Lon = lon,
                HoleTop = design.HoleTop,
                HoleBottom = design.HoleBottom,
                Radius = design.Radius,
                TotalKg = design.TotalKg,
                TopDeckKg = design.TopDeck?.Kg ?? 0,
            };
            Armed.Add(blast);
            Log.Info($"Blast {blast.Id} armed: {design.Decks.Count} decks, {design.TotalKg:F0} kg, hole {design.HoleTop:F0}-{design.HoleBottom:F0} m");
            return blast;
        }

        public static ArmedBlast FindBlast(string id) => Armed.Find(b => b.Id == id);

        /// <summary>
        /// Fires a loaded hole: a vertical shaft of the blast's radius opens along it (joining the cave it crosses,
        /// with a new skylight at the top), and loaded parts near the top take the blast (destroyed, or heated and
        /// pushed).
        /// </summary>
        public static void Detonate(BodyRuntime runtime, ArmedBlast blast)
        {
            Armed.Remove(blast);
            CelestialBody body = runtime.Body;
            Vec3d dir = BodyFrame.Direction(blast.Lat, blast.Lon);
            double surface = KspSampler.SurfaceRadius(body, blast.Lat, blast.Lon);
            Vec3d bottom = dir * (surface - blast.HoleBottom), top = dir * surface;
            var spec = new ExcavationSpec($"XP-{body.bodyName}-{nextNumber++}", body.bodyName, bottom, (float)blast.Radius) { Shape = ExcavationShape.Shaft };
            spec.Points.Add(Vec3.Zero);
            spec.Points.Add(spec.ToLocal(top));
            Specs.Add(spec);
            TubeNetwork reached = Reached(runtime, spec, 0);
            if (reached != null)
            {
                spec.JoinedTo = reached.Id;
            }

            Build(runtime, spec, 0);
            Vector3d world = Frames.ToWorld(body, top);
            Damage(world, blast.TopDeckKg, confined: blast.HoleTop + 1 < blast.HoleBottom);
            try
            {
                FXMonger.ExplodeWithDebris(world, Math.Min(1, blast.TotalKg / 500), null);
            }
            catch (Exception e)
            {
                Log.Warn($"Blast effect failed: {e.Message}");
            }

            Log.Info($"Blast {blast.Id} opened shaft {spec.Id} ({blast.Radius:F1} m, {blast.HoleTop:F0}-{blast.HoleBottom:F0} m){(reached != null ? " into " + reached.Id : "")}");
        }

        /// <summary>
        /// Fires a horizontal round: a tunnel of <paramref name="radius"/> opens from <paramref name="floor"/> (body
        /// coordinates, its floor at the face) <paramref name="length"/> m along <paramref name="dir"/>; vessels near the
        /// face take the blast.
        /// </summary>
        public static ExcavationSpec BlastRound(BodyRuntime runtime, Vec3d floor, Vec3d dir, double length, float radius, double kg)
        {
            ExcavationSpec spec = Start(runtime, floor, radius);
            for (double d = 1; d < length + 0.5; d += 1)
            {
                Dig(runtime, spec, floor + dir * Math.Min(d, length));
            }

            Vector3d world = Frames.ToWorld(runtime.Body, floor + dir * (length * 0.5));
            Damage(world, kg, confined: true);
            try
            {
                FXMonger.ExplodeWithDebris(world, Math.Min(1, kg / 500), null);
            }
            catch (Exception e)
            {
                Log.Warn($"Blast effect failed: {e.Message}");
            }

            Log.Info($"Round fired: tunnel {spec.Id}, {length:F1} m, radius {radius:F1} m, {kg:F0} kg");
            return spec;
        }

        /// <summary>Loaded vessels within the danger radius of a charge at <paramref name="world"/>.</summary>
        public static List<Vessel> VesselsInDanger(Vector3d world, double kg, bool confined)
        {
            (double _, double damage) = BlastSafety.Radii(kg, confined);
            return FlightGlobals.VesselsLoaded.Where(v => v != null && v.parts.Any(p => (p.transform.position - world).magnitude < damage)).ToList();
        }

        private static void Damage(Vector3d world, double kg, bool confined)
        {
            (double destroy, double damage) = BlastSafety.Radii(kg, confined);
            var wrecked = new List<Part>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded.ToList())
            {
                foreach (Part p in v.parts)
                {
                    double d = (p.transform.position - world).magnitude;
                    if (d < destroy)
                    {
                        wrecked.Add(p);
                    }
                    else if (d < damage)
                    {
                        double f = 1 - (d - destroy) / Math.Max(1e-3, damage - destroy);
                        p.temperature = Math.Min(p.maxTemp * 0.95, p.temperature + 600 * f);
                        p.Rigidbody?.AddExplosionForce((float)(50 * f * kg), world, (float)damage, 0.5f, UnityEngine.ForceMode.Impulse);
                    }
                }
            }

            foreach (Part p in wrecked)
            {
                p.explode();
            }

            if (wrecked.Count > 0)
            {
                Log.Info($"Blast destroyed {wrecked.Count} parts");
            }
        }

        /// <summary>A loaded blast hole waiting to be fired (saved with the game).</summary>
        public sealed class ArmedBlast
        {
            public string Id;
            public string Body;
            public double Lat;
            public double Lon;
            public double HoleTop;
            public double HoleBottom;
            public double Radius;
            public double TotalKg;
            public double TopDeckKg;

            public ConfigNode ToNode()
            {
                var n = new ConfigNode("ARMED_BLAST");
                n.AddValue("id", Id);
                n.AddValue("body", Body);
                foreach (var (key, value) in new[] { ("lat", Lat), ("lon", Lon), ("holeTop", HoleTop), ("holeBottom", HoleBottom), ("radius", Radius), ("kg", TotalKg), ("topDeckKg", TopDeckKg) })
                {
                    n.AddValue(key, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }

                return n;
            }

            public static ArmedBlast FromNode(ConfigNode n)
            {
                double Get(string key) => double.TryParse(n.GetValue(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
                var b = new ArmedBlast
                {
                    Id = n.GetValue("id"), Body = n.GetValue("body"), Lat = Get("lat"), Lon = Get("lon"), HoleTop = Get("holeTop"),
                    HoleBottom = Get("holeBottom"), Radius = Get("radius"), TotalKg = Get("kg"), TopDeckKg = Get("topDeckKg"),
                };
                return string.IsNullOrEmpty(b.Id) || string.IsNullOrEmpty(b.Body) || double.IsNaN(b.Lat) || double.IsNaN(b.HoleBottom) || double.IsNaN(b.Radius) ? null : b;
            }
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
            Armed.Clear();
            Bases.Clear();
            nextNumber = 1;
            nextBlast = 1;
        }

        public static void Save(ConfigNode node)
        {
            node.AddValue("nextExcavation", nextNumber);
            node.AddValue("nextBlast", nextBlast);
            foreach (ExcavationSpec spec in Specs)
            {
                node.AddNode(CfgAdapter.ToKsp(spec.ToCfg()));
            }

            foreach (ArmedBlast blast in Armed)
            {
                node.AddNode(blast.ToNode());
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

            if (int.TryParse(node.GetValue("nextBlast"), out int blastNumber))
            {
                nextBlast = Math.Max(1, blastNumber);
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

            foreach (ConfigNode n in node.GetNodes("ARMED_BLAST"))
            {
                ArmedBlast blast = ArmedBlast.FromNode(n);
                if (blast != null)
                {
                    Armed.Add(blast);
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
            double length = (b - a).Length;
            int samples = Math.Max(2, (int)Math.Ceiling(length / 2)) + 1;
            foreach (TubeNetwork net in runtime.Catalog.NetworksNear((a + b) * 0.5, length * 0.5 + spec.Radius + 50))
            {
                if (net.Id.StartsWith("XP-", StringComparison.Ordinal))
                {
                    continue;
                }

                // Every 2 m along the piece (a blast shaft can cross a cave anywhere along it).
                for (int i = 0; i < samples; i++)
                {
                    Vec3d point = a + (b - a) * ((double)i / (samples - 1));
                    Vec3 lift = spec.Shape == ExcavationShape.Tunnel ? net.UpAt(net.ToLocal(point)) * spec.Radius : Vec3.Zero;
                    if (net.Sdf.Distance(net.ToLocal(point) + lift) < 0)
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
                // The rim (with its collider) first, then the cut: whatever stands there never loses the ground.
                foreach (Skylight opening in openings)
                {
                    // While the tunnel grows under its trench, the rim (which closes the trench where there is no tunnel yet) is rebuilt.
                    bool underMouth = (spec.ToBody(spec.Points[spec.Points.Count - 1]) - spec.ToBody(opening.SurfaceCenter)).Length < opening.Radius + spec.Radius;
                    SkylightRenderer.Instance?.BuildRimNow(runtime, alone, opening, rebuild: changed >= 0 && spec.Shape == ExcavationShape.Tunnel && underMouth);
                }

                SkylightInjector.AddNetworks(runtime, new[] { alone });
                // A mouth clipped into the terrain follows its tunnel: cut again while the tunnel grows under it (once it is
                // past the mouth, the ground over it no longer changes).
                bool nearMouth = false;
                foreach (Skylight opening in openings)
                {
                    nearMouth |= (spec.ToBody(spec.Points[spec.Points.Count - 1]) - spec.ToBody(opening.SurfaceCenter)).Length < opening.Radius + spec.Radius + 2;
                }

                if (nearMouth)
                {
                    SkylightInjector.RefreshMouth(runtime, alone);
                }
                foreach (Skylight opening in openings)
                {
                    SkylightRenderer.Instance?.Settle(alone.ToBody(opening.SurfaceCenter), opening.Radius);
                }
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
            // Every 2 m along the tunnel, not only at its points (a straight tunnel has two): the mesh is clipped
            // against the nearest sample's terrain, and a coarse one leaves walls standing over the ground.
            var dirList = new List<Vec3d>();
            var radiusList = new List<double>();
            void Add(Vec3 p)
            {
                Vec3d dir = spec.ToBody(p).Normalized;
                BodyFrame.ToLatLon(dir, out double lat, out double lon);
                dirList.Add(dir);
                radiusList.Add(KspSampler.SurfaceRadius(body, lat, lon));
            }

            for (int i = 0; i < spec.Points.Count; i++)
            {
                Add(spec.Points[i]);
                if (i + 1 < spec.Points.Count)
                {
                    int steps = (int)((spec.Points[i + 1] - spec.Points[i]).Length / 2);
                    for (int k = 1; k < steps; k++)
                    {
                        Add(Vec3.Lerp(spec.Points[i], spec.Points[i + 1], (float)k / steps));
                    }
                }
            }

            dirs = dirList;
            radii = radiusList;
        }

        // The mouth of a tunnel dug from the surface: a hole of the tunnel's size where its first point is.
        /// <summary>Longest trench (m) a nearly level tunnel opens before its mouth is capped.</summary>
        private const double MaxTrench = 30;

        private static List<Skylight> Openings(CelestialBody body, ExcavationSpec spec)
        {
            var list = new List<Skylight>();
            // A tunnel opens where it starts, a blast shaft at its top.
            Vec3d start = spec.ToBody(spec.Shape == ExcavationShape.Shaft ? spec.Points[spec.Points.Count - 1] : spec.Points[0]);
            BodyFrame.ToLatLon(start.Normalized, out double lat, out double lon);
            double surface = KspSampler.SurfaceRadius(body, lat, lon);
            if (Math.Abs(start.Length - surface) <= spec.Radius + 1)
            {
                // A sloping tunnel opens a trench, not a round hole: the ground is open along its axis until the
                // roof (one diameter over the floor) is under the terrain. The first piece's direction is
                // extended to find that point; the opening covers the trench (its smooth rim closes the rest).
                Vec3d centre = start.Normalized * surface;
                float radius = spec.Radius + 0.5f;
                if (spec.Shape == ExcavationShape.Tunnel && spec.Points.Count >= 2)
                {
                    Vec3d dir = (spec.ToBody(spec.Points[1]) - start).Normalized;
                    double trench = 0;
                    for (double d = 0; d <= MaxTrench; d += 0.5)
                    {
                        Vec3d p = start + dir * d;
                        BodyFrame.ToLatLon(p.Normalized, out double la, out double lo);
                        trench = d;
                        if (KspSampler.SurfaceRadius(body, la, lo) - p.Length > 2 * spec.Radius + 0.5)
                        {
                            break;
                        }
                    }

                    Vec3d middle = start + dir * (trench * 0.5);
                    BodyFrame.ToLatLon(middle.Normalized, out double mla, out double mlo);
                    centre = middle.Normalized * KspSampler.SurfaceRadius(body, mla, mlo);
                    radius = (float)(trench * 0.5) + spec.Radius + 1f;
                    lat = mla;
                    lon = mlo;
                }

                list.Add(new Skylight
                {
                    SurfaceCenter = spec.ToLocal(centre),
                    Up = centre.Normalized.ToVec3(),
                    Radius = radius,
                    Lat = lat,
                    Lon = lon,
                    Index = 0,
                    Owner = spec.Id,
                    Smooth = spec.Shape == ExcavationShape.Tunnel,
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
            Index = o.Index,
            Owner = o.Owner,
            Smooth = o.Smooth,
        };
    }
}
