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
                return runtime.Catalog.Find("site:" + site) ?? throw new ArgumentException($"no named site '{site}' on {vessel.mainBody.bodyName}");
            }

            Vec3d here = Frames.ToBody(vessel.mainBody, vessel.GetWorldPos3D());
            TubeNetwork nearest = runtime.Catalog.All.Where(n => !n.IsNamed)
                .OrderBy(n => Vec3d.Distance(n.Anchor, here))
                .FirstOrDefault();
            return nearest ?? throw new InvalidOperationException("no procedural network generated yet");
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
            for (float x = b.Min.X; x <= b.Max.X; x += 8)
            {
                for (float y = b.Min.Y; y <= b.Max.Y; y += 8)
                {
                    for (float z = b.Min.Z; z <= b.Max.Z; z += 8)
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

            ctx.Release();
            yield return ctx.WaitFor(() => !vessel.packed, 60, "the vessel to go off rails");
            CelestialBody body = vessel.mainBody;
            // Like the cheat menu's SetVesselPosition: on rails first (moving an unpacked vessel far in one
            // frame gives its parts a huge velocity), moved while pinned (Landed = idle orbit driver);
            // KSP — or Subterra's load guard, inside a cave — lets it go.
            OrbitPhysicsManager.HoldVesselUnpack(2);
            vessel.GoOnRails();
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
            body.GetLatLonAlt(vessel.transform.position, out vessel.latitude, out vessel.longitude, out vessel.altitude);
            TestContext.RestOrbitHere(vessel);
            vessel.Landed = true;
            yield return null;
            if (hold)
            {
                ctx.Hold(vessel);
            }

            ctx.Pass(Invariant($"{net.Id} node {index} {(inside ? "inside" : "above")} {offsetUp} m: altitude {vessel.altitude:F1} m{(hold ? ", held" : "")} (before: {axes})"));
        }
    }
}
