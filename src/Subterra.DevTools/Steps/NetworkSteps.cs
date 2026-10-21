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
            int index = (int)node.ArgDouble("node", 1);
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
            vessel.SetPosition(Frames.ToWorld(body, target));
            body.GetLatLonAlt(vessel.transform.position, out vessel.latitude, out vessel.longitude, out vessel.altitude);
            TestContext.RestOrbitHere(vessel);
            vessel.Landed = true;
            yield return null;
            if (hold)
            {
                ctx.Hold(vessel);
            }

            ctx.Pass(Invariant($"{net.Id} node {index} {(inside ? "inside" : "above")} {offsetUp} m: altitude {vessel.altitude:F1} m{(hold ? ", held" : "")}"));
        }
    }
}
