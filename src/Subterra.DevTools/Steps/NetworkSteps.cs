using System;
using System.Collections;
using System.Linq;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Steps that place vessels relative to Subterra's networks.</summary>
    [StepProvider]
    internal static class NetworkSteps
    {
        internal static void Register()
        {
            StepRegistry.Register("teleportToNetwork", TeleportToNetwork);
            StepRegistry.Register("networkInfo", NetworkInfo);
            StepRegistry.Register("terrainProfile", TerrainProfile);
        }

        /// <summary>The network named by <c>site</c> (a named site, or <c>procedural</c> = the nearest generated one).</summary>
        internal static TubeNetwork FindNetwork(ConfigNode node, Vessel vessel, out BodyRuntime runtime)
        {
            if (!BodyRegistry.Instance.TryGet(vessel.mainBody, out runtime))
            {
                throw new InvalidOperationException($"Subterra is not active on {vessel.mainBody.bodyName}");
            }

            string site = node.RequireArg("site");
            if (!string.Equals(site, "procedural", StringComparison.OrdinalIgnoreCase))
            {
                TubeNetwork named = runtime.Catalog.Find("site:" + site);
                if (named != null)
                {
                    return named;
                }

                // Right after a teleport to another body the vessel can still count as on the old one: look everywhere.
                foreach (CelestialBody other in FlightGlobals.Bodies)
                {
                    if (BodyRegistry.Instance.TryGet(other, out BodyRuntime candidate) && (named = candidate.Catalog.Find("site:" + site)) != null)
                    {
                        runtime = candidate;
                        return named;
                    }
                }

                throw new ArgumentException($"no named site '{site}' on any body");
            }

            Vec3d here = Frames.ToBody(vessel.mainBody, vessel.GetWorldPos3D());
            // field = <TUBE_FIELD name>: only networks of that field (to visit one style among several).
            string field = node.Arg("field", null);
            TubeNetwork nearest = runtime.Catalog.All.Where(n => !n.IsNamed && (field == null || n.Site?.Field == field))
                .OrderBy(n => Vec3d.Distance(n.Anchor, here))
                .FirstOrDefault();
            return nearest ?? throw new InvalidOperationException("no procedural network generated yet");
        }

        // terrainProfile body = Moho lat0 = 88 lon0 = 0 lat1 = 88 lon1 = 180 samples = 200 — logs the terrain altitude along a
        // great circle (diagnostics: to measure a feature such as the Mohole).
        private static IEnumerator TerrainProfile(ConfigNode node, TestContext ctx)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(node.Arg("body", "Moho")) ?? throw new ArgumentException("unknown body");
            var a = Subterra.Core.Geo.BodyFrame.Direction(node.ArgDouble("lat0", 88), node.ArgDouble("lon0", 0));
            var b = Subterra.Core.Geo.BodyFrame.Direction(node.ArgDouble("lat1", 88), node.ArgDouble("lon1", 180));
            int n = (int)node.ArgDouble("samples", 200);
            var lines = new System.Text.StringBuilder();
            double min = double.MaxValue, minLat = 0, minLon = 0;
            for (int i = 0; i <= n; i++)
            {
                Vec3d d = (a * (1 - (double)i / n) + b * ((double)i / n)).Normalized;
                Subterra.Core.Geo.BodyFrame.ToLatLon(d, out double lat, out double lon);
                double alt = KspSampler.SurfaceRadius(body, lat, lon) - body.Radius;
                lines.AppendLine(Invariant($"  {lat:F4}, {lon:F4}: {alt:F0} m"));
                if (alt < min)
                {
                    min = alt;
                    minLat = lat;
                    minLon = lon;
                }
            }

            Debug.Log("[Subterra.DevTools] terrain profile on " + body.bodyName + System.Environment.NewLine + lines);
            yield return null;
            ctx.Pass(Invariant($"lowest {min:F0} m at {minLat:F4}, {minLon:F4}"));
        }

        // networkInfo site = procedural — logs the network's nodes (feature, roof depth, width, height, terrain) and where its void
        // comes within 1 m of the terrain outside its skylights (diagnostics).
        private static IEnumerator NetworkInfo(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            TubeNetwork net = FindNetwork(node, vessel, out BodyRuntime runtime);
            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"{net.Id}: {net.Site?.Nodes.Count} nodes, {net.Site?.Edges.Count} edges, {net.Primitives.Count} primitives, {net.Skylights.Count} skylights");
            for (int i = 0; net.Site != null && i < net.Site.Nodes.Count; i++)
            {
                var n = net.Site.Nodes[i];
                lines.AppendLine(Invariant($"  node {i}: {n.Feature} depth {n.Depth:F0} width {n.Width:F0} height {n.Height:F0} at {n.Lat:F4}, {n.Lon:F4}"));
            }

            // Void points near the surface, away from skylights.
            int open = 0;
            Aabb b = net.Bounds;
            // An 8 m grid, coarser on big networks (at most about two million points: kilometres-wide ones hung the game).
            Vec3 size = b.Max - b.Min;
            float step = Math.Max(8f, (float)Math.Pow(Math.Max(1.0, (double)size.X * size.Y * size.Z) / 2e6, 1.0 / 3.0));
            for (float x = b.Min.X; x <= b.Max.X; x += step)
            {
                for (float y = b.Min.Y; y <= b.Max.Y; y += step)
                {
                    for (float z = b.Min.Z; z <= b.Max.Z; z += step)
                    {
                        var p = new Vec3(x, y, z);
                        double above = net.AboveSurface(p);
                        if (above < -1.5 || above > 0 || net.Sdf.Distance(p) >= 0)
                        {
                            continue;
                        }

                        int nearest = net.Sdf.NearestPrimitive(p);
                        if (nearest >= 0 && net.Primitives[nearest] is Subterra.Core.Sdf.Shaft)
                        {
                            continue;
                        }

                        if (open++ < 12)
                        {
                            Vec3d bp = net.ToBody(p);
                            Subterra.Core.Geo.BodyFrame.ToLatLon(bp.Normalized, out double la, out double lo);
                            lines.AppendLine(Invariant($"  open to the surface at {la:F4}, {lo:F4} (primitive {nearest}: {net.Primitives[nearest].GetType().Name}, zone {net.Primitives[nearest].Zone})"));
                        }
                    }
                }

                if (x % 400 < 8)
                {
                    yield return null;
                }
            }

            lines.AppendLine($"  {open} void samples within 1.5 m of the terrain outside skylights");
            Debug.Log("[Subterra.DevTools] " + lines);
            ctx.Pass($"{net.Id}: {open} open samples");
        }

        /// <summary>A node index, or "junction" (the node where most tubes meet) or "deepest".</summary>
        private static int NodeIndex(TubeNetwork net, string arg)
        {
            if (string.Equals(arg, "junction", StringComparison.OrdinalIgnoreCase) && net.Site != null)
            {
                var degree = new int[net.Site.Nodes.Count];
                foreach (var e in net.Site.Edges)
                {
                    degree[e.From]++;
                    degree[e.To]++;
                }

                return Array.IndexOf(degree, degree.Max());
            }

            if (string.Equals(arg, "deepest", StringComparison.OrdinalIgnoreCase) && net.Site != null)
            {
                double max = net.Site.Nodes.Max(n => n.Depth);
                return net.Site.Nodes.FindIndex(n => n.Depth == max);
            }

            return int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Body position <paramref name="offsetUp"/> m above the floor of node i (inside) or above the terrain over it.</summary>
        internal static Vec3d NodePosition(TubeNetwork net, int nodeIndex, bool inside, double offsetUp)
        {
            if (nodeIndex < 0 || nodeIndex >= net.Nodes.Count)
            {
                throw new ArgumentException($"{net.Id} has {net.Nodes.Count} nodes, no node {nodeIndex}");
            }

            PathPoint p = net.Nodes[nodeIndex];
            return inside ? p.FloorCenter + p.Up * offsetUp : p.Up * (p.SurfaceRadius + offsetUp);
        }

        // teleportToNetwork site = DevTube|procedural node = 1 inside = true offsetUp = 2 hold = false —
        // moves the active vessel (after it is off rails) to that point, at rest.
        private static IEnumerator TeleportToNetwork(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            TubeNetwork net = FindNetwork(node, vessel, out _);
            int index = NodeIndex(net, node.Arg("node", "1"));
            bool inside = node.ArgBool("inside", true);
            double offsetUp = node.ArgDouble("offsetUp", 2);
            bool hold = node.ArgBool("hold", false);
            Vec3d target = NodePosition(net, index, inside, offsetUp);

            // Placed in the network's own body's frame, and its orbit restarted around that body. Right after a teleport
            // to another body the vessel can still count as on the old one (its sphere of influence not switched yet);
            // releasing a hold then restarted its orbit around the old body, which sent it back to Kerbin or far off.
            // So the hold is dropped without a release, and the vessel stays on rails until placed.
            CelestialBody body = FlightGlobals.GetBodyByName(net.Body) ?? throw new InvalidOperationException($"no body {net.Body}");
            ctx.Forget();
            yield return ctx.WaitFor(() => vessel.loaded, 60, "the vessel to be loaded");
            // Like the cheat menu's SetVesselPosition: on rails first (moving an unpacked vessel far in one
            // frame gives its parts a huge velocity), moved while pinned (Landed = idle orbit driver);
            // KSP — or Subterra's load guard, inside a cave — lets it go.
            OrbitPhysicsManager.HoldVesselUnpack(2);
            if (!vessel.packed)
            {
                vessel.GoOnRails();
            }
            vessel.Landed = true;
            // Keep the vessel's attitude to the local vertical (it lands upright, not as it stood on the other side).
            Vector3d world = Frames.ToWorld(body, target);
            Vector3 before = (vessel.transform.position - body.position).normalized;
            string axes = Invariant($"up·vertical {Vector3.Dot(vessel.transform.up, before):F2}, forward·vertical {Vector3.Dot(vessel.transform.forward, before):F2}");
            // upright = true: wheels down whatever it did before (an SPH craft's top is its root's -forward).
            Vector3 newUp = (world - body.position).normalized;
            Vector3 oldUp = node.ArgBool("upright", false) ? -vessel.transform.forward : (vessel.transform.position - body.position).normalized;
            vessel.SetRotation(Quaternion.FromToRotation(oldUp, newUp) * vessel.transform.rotation);
            vessel.SetPosition(world);
            // Latitude, longitude and altitude from the target itself, not from the transform: right after a teleport to
            // another body the transform (and the floating origin) can still be far off, which put the vessel 100 000 km away.
            Core.Geo.BodyFrame.ToLatLon(target, out vessel.latitude, out vessel.longitude);
            vessel.altitude = target.Length - body.Radius;
            TestContext.RestOrbitHere(vessel, body);
            vessel.Landed = true;
            yield return null;
            if (inside)
            {
                PropGuard.Watch(vessel);
            }

            if (hold)
            {
                ctx.Hold(vessel);
            }

            ctx.Pass(Invariant($"{net.Id} node {index} {(inside ? "inside" : "above")} {offsetUp} m: altitude {vessel.altitude:F1} m{(hold ? ", held" : "")} (before: {axes})"));
        }
    }
}

namespace Subterra.DevTools
{
    /// <summary>
    /// For the first seconds after a teleport into a cave: turns off the colliders of scattered props (boulders) the
    /// vessel lands on or in. A vessel dropped onto a boulder, which no driver would do, was punched apart by KSP.
    /// </summary>
    internal sealed class PropGuard : UnityEngine.MonoBehaviour
    {
        private Vessel vessel;
        private float until;

        public static void Watch(Vessel vessel)
        {
            var guard = new UnityEngine.GameObject("SubterraTestPropGuard").AddComponent<PropGuard>();
            guard.vessel = vessel;
            guard.until = UnityEngine.Time.time + 15f;
        }

        private void FixedUpdate()
        {
            if (vessel == null || UnityEngine.Time.time > until)
            {
                Destroy(gameObject);
                return;
            }

            foreach (UnityEngine.Collider c in UnityEngine.Physics.OverlapSphere(vessel.GetWorldPos3D(), 8f, 1 << 15, UnityEngine.QueryTriggerInteraction.Ignore))
            {
                if (c.enabled && c.gameObject.name.StartsWith("SubterraScatter", System.StringComparison.Ordinal))
                {
                    c.enabled = false;
                }
            }
        }
    }
}
