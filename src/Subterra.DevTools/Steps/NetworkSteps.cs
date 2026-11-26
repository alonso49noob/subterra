using System;
using System.Collections;
using System.Linq;
using Subterra.Core.Geo;
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
            StepRegistry.Register("jumpToSite", JumpToSite);
            StepRegistry.Register("networkInfo", NetworkInfo);
            StepRegistry.Register("terrainProfile", TerrainProfile);
            StepRegistry.RegisterAssertion("insideNetwork", InsideNetwork);
            StepRegistry.RegisterAssertion("waterDepth", WaterDepth);
            StepRegistry.RegisterAssertion("cameraUnderwater", CameraUnderwater);
            StepRegistry.RegisterAssertion("evaState", EvaState);
            StepRegistry.RegisterAssertion("scattererUnderwater", ScattererUnderwater);
            StepRegistry.RegisterAssertion("altitudeBelow", AltitudeBelow);
        }

        // altitudeBelow max = 1300 min = 1200 — the tracked vessel's altitude (over the datum) is at most max (it went down)
        // and, with min, at least min (it did not fall on through).
        private static bool AltitudeBelow(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            double max = node.ArgDouble("max", 0), min = node.ArgDouble("min", double.MinValue);
            ctx.Detail = Invariant($"{vessel.GetDisplayName()}: altitude {vessel.altitude:F1} m (at most {max:F0}{(min > double.MinValue ? $", at least {min:F0}" : "")}), {vessel.situation}, {vessel.srfSpeed:F1} m/s, packed {vessel.packed}");
            return vessel.altitude <= max && vessel.altitude >= min;
        }

        // scattererUnderwater expect = false — whether any of Scatterer's oceans has its underwater view on (Scatterer
        // installed; without it the step passes only for expect = false).
        private static bool ScattererUnderwater(ConfigNode node, TestContext ctx)
        {
            Type type = HarmonyLib.AccessTools.TypeByName("Scatterer.OceanNode");
            bool expect = node.ArgBool("expect", false);
            if (type == null)
            {
                ctx.Detail = "Scatterer is not installed";
                return !expect;
            }

            System.Reflection.FieldInfo field = HarmonyLib.AccessTools.Field(type, "isUnderwater");
            bool under = false;
            int count = 0;
            foreach (UnityEngine.Object ocean in UnityEngine.Object.FindObjectsOfType(type))
            {
                count++;
                under |= (bool)field.GetValue(ocean);
            }

            ctx.Detail = $"{count} Scatterer ocean(s), underwater view {(under ? "on" : "off")}, patch {(PatchManager.IsActive(ScattererPatch.Id) ? "active" : "not active")}";
            return under == expect;
        }

        // evaState contains = Swim — the tracked kerbal's EVA state's name contains that text (and the vessel's situation).
        private static bool EvaState(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            KerbalEVA eva = vessel.FindPartModuleImplementing<KerbalEVA>();
            string state = eva?.fsm?.currentStateName ?? "no EVA module";
            ctx.Detail = $"{vessel.GetDisplayName()}: state {state}, {vessel.situation}, splashed {vessel.Splashed}";
            return state.IndexOf(node.RequireArg("contains"), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // cameraUnderwater expect = true — whether the underwater view is on (camera under a cave's or an ocean's water,
        // or inside the ice over an ocean), with where the camera is.
        private static bool CameraUnderwater(ConfigNode node, TestContext ctx)
        {
            Camera camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            CelestialBody body = FlightGlobals.currentMainBody;
            string where = camera == null || body == null ? "no camera" : Invariant($"camera {camera.name} at altitude {body.GetAltitude(camera.transform.position):F1} m, near clip {camera.nearClipPlane:F2}");
            GameObject veil = GameObject.Find("SubterraUnderwater");
            ctx.Detail = $"underwater view {(UnderwaterView.Under ? "on" : "off")}, veil {(veil == null ? "none" : veil.activeInHierarchy ? "shown on layer " + veil.layer : "hidden")}; {where}";
            return UnderwaterView.Under == node.ArgBool("expect", true);
        }

        /// <summary>The network named by <c>site</c> (a named site, or <c>procedural</c> = the nearest generated one).</summary>
        internal static TubeNetwork FindNetwork(ConfigNode node, Vessel vessel, out BodyRuntime runtime)
        {
            if (!BodyRegistry.Instance.TryGet(vessel.mainBody, out runtime))
            {
                throw new InvalidOperationException($"Subterra is not active on {vessel.mainBody.bodyName}");
            }

            string site = node.RequireArg("site");
            if (string.Equals(site, "dug", StringComparison.OrdinalIgnoreCase))
            {
                // site = dug: the nearest hole dug alone (a shaft, a blast hole, a pocket), not joined to a cave.
                Vec3d from = Frames.ToBody(vessel.mainBody, vessel.GetWorldPos3D());
                return runtime.Catalog.All.Where(n => !n.IsNamed && n.Site == null).OrderBy(n => Vec3d.Distance(n.Anchor, from)).FirstOrDefault()
                    ?? throw new InvalidOperationException("nothing dug yet");
            }

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
            // size = giant | narrow: only networks of that size class (every passage at most 16 m wide: narrow; the first
            // node 120 m or more: giant).
            string size = node.Arg("size", null);
            bool Sized(TubeNetwork n) =>
                size == null || n.Site == null
                || (size == "narrow" && n.Site.Nodes.TrueForAll(p => p.Width <= 16))
                || (size == "giant" && n.Site.Nodes[0].Width >= 120);
            TubeNetwork nearest = runtime.Catalog.All.Where(n => !n.IsNamed && (field == null || n.Site?.Field == field) && Sized(n))
                .OrderBy(n => Vec3d.Distance(n.Anchor, here))
                .FirstOrDefault();
            return nearest ?? throw new InvalidOperationException($"no procedural network{(size != null ? " of size " + size : "")} generated yet");
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
        internal static int NodeIndex(TubeNetwork net, string arg)
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

        // A point on the network's first ramp: 0 at its top (its floor 3 m over the ground), 1 at the node's floor.
        private static Vec3d RampPosition(TubeNetwork net, double along, double offsetUp)
        {
            int i = net.Site?.Nodes.FindIndex(n => n.Ramp) ?? -1;
            if (i < 0)
            {
                throw new ArgumentException($"{net.Id} has no ramp");
            }

            var (lat, lon) = NetworkCompiler.RampTop(net.Site, net.Site.Nodes[i], FlightGlobals.GetBodyByName(net.Body).Radius);
            Skylight mouth = net.Skylights.OrderBy(s => Math.Abs(s.Lat - lat) + Math.Abs(s.Lon - lon)).First();
            Vec3d topUp = BodyFrame.Direction(lat, lon);
            Vec3d top = topUp * (mouth.SurfaceRadius + 3);
            PathPoint p = net.Nodes[i];
            Vec3d at = top + (p.FloorCenter - top) * along;
            return at + (topUp + (p.Up - topUp) * along).Normalized * offsetUp;
        }

        // waterDepth min = 0 max = 3 — the tracked vessel's deepest part is that far under a cave's water (submerged = true: some part is).
        private static bool WaterDepth(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            SubterraWaterModule water = SubterraWaterModule.Of(vessel);
            double depth = water?.DeepestDepth ?? 0;
            int parts = water?.Submerged ?? 0;
            ctx.Detail = Invariant($"{vessel.GetDisplayName()}: {parts} part(s) in water, deepest {depth:F1} m, {vessel.srfSpeed:F1} m/s, {vessel.situation}");
            return depth >= node.ArgDouble("min", double.MinValue) && depth <= node.ArgDouble("max", double.MaxValue) && (!node.ArgBool("submerged", true) || parts > 0);
        }

        // insideNetwork site = X — the tracked vessel is inside that network's rock (in its tubes, not on the ground).
        private static bool InsideNetwork(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            TubeNetwork net = FindNetwork(node, vessel, out _);
            Vec3d at = Frames.ToBody(vessel.mainBody, vessel.GetWorldPos3D());
            float d = net.Sdf.Distance(net.ToLocal(at));
            ctx.Detail = Invariant($"{net.Id}: distance {d:F1} m, {vessel.situation}, {vessel.heightFromTerrain:F1} m above terrain, {vessel.srfSpeed:F1} m/s");
            return d < 0;
        }

        // teleportToNetwork site = DevTube|procedural node = 1 inside = true offsetUp = 2 hold = false target = 0 —
        // (target = 0: over/in the centre of that radar target of the network instead of the node)
        // moves the active vessel (after it is off rails) to that point, at rest.
        // jumpToSite site = DevTube: the Subterra window's own "Jump" to a named site (as a player uses it), then waits.
        private static IEnumerator JumpToSite(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            string site = node.RequireArg("site");
            if (!BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                throw new InvalidOperationException($"no Subterra data for {vessel.mainBody.bodyName}");
            }

            TubeNetwork net = runtime.Catalog.Named.FirstOrDefault(n => n.SiteName == site) ?? throw new InvalidOperationException($"no site {site} on {vessel.mainBody.bodyName}");
            var jump = typeof(DebugWindow).GetMethod("Jump", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("DebugWindow.Jump not found");
            jump.Invoke(null, new object[] { net });
            ctx.Detail = Invariant($"jumped to {site} ({net.Id})");
            yield return new WaitForSeconds((float)node.ArgDouble("wait", 3));
        }

        private static IEnumerator TeleportToNetwork(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            bool inside = node.ArgBool("inside", true);
            double offsetUp = node.ArgDouble("offsetUp", 2);
            bool hold = node.ArgBool("hold", false);
            TubeNetwork net = null;
            int index = -1;
            Vec3d target;
            string bodyName, where;
            if (string.Equals(node.Arg("site"), "ocean", StringComparison.OrdinalIgnoreCase))
            {
                // site = ocean body = Vall lat = 88 lon = 0 depth = 500: that far under the ice of the body's ocean.
                bodyName = node.RequireArg("body");
                CelestialBody oceanBody = FlightGlobals.GetBodyByName(bodyName) ?? throw new InvalidOperationException($"no body {bodyName}");
                if (!BodyRegistry.Instance.TryGet(oceanBody, out BodyRuntime runtime) || runtime.Ocean == null)
                {
                    throw new InvalidOperationException($"{bodyName} has no ocean");
                }

                double lat = node.ArgDouble("lat", 0), depth = node.ArgDouble("depth", 100);
                Vec3d dir = Core.Geo.BodyFrame.Direction(lat, node.ArgDouble("lon", 0));
                where = Invariant($"{bodyName} ocean {depth} m under the ice");
                // near = tower|pillar standoff = 40: beside the nearest ice tower (or Pol's pillar), that many metres off
                // its side.
                string near = node.Arg("near", "");
                if (near.Length > 0)
                {
                    ulong seed = Hash.Of(oceanBody.bodyName + "/ice");
                    double standoff = node.ArgDouble("standoff", 40);
                    Vec3d axis = default;
                    double width = 0, best = double.MaxValue;
                    if (string.Equals(near, "pillar", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var pillar in Core.Water.OceanFeatures.PillarsNear(runtime.Ocean, dir, 8000, seed))
                        {
                            double d = (pillar.Direction - dir).Length;
                            if (d < best)
                            {
                                best = d;
                                axis = pillar.Direction;
                                width = pillar.Radius * 1.25;
                            }
                        }
                    }
                    else
                    {
                        foreach (var tower in Core.Water.OceanFeatures.TowersNear(runtime.Ocean, dir, seed, 4))
                        {
                            double d = (tower.Direction - dir).Length;
                            if (d < best)
                            {
                                best = d;
                                axis = tower.Direction;
                                width = tower.BaseRadius;
                            }
                        }
                    }

                    if (best == double.MaxValue)
                    {
                        throw new InvalidOperationException($"no {near} near {lat:F1}, {node.ArgDouble("lon", 0):F1} on {bodyName}");
                    }

                    Vec3d side = Vec3d.Cross(axis, Math.Abs(axis.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0)).Normalized;
                    dir = (axis + side * ((width + standoff) / runtime.Ocean.BodyRadius)).Normalized;
                    Core.Geo.BodyFrame.ToLatLon(dir, out lat, out _);
                    where += Invariant($", {width + standoff:F0} m from the axis of a {near}");
                }

                target = dir * (runtime.Ocean.CeilingRadius(lat) - depth);
                inside = false;
            }
            else if (string.Equals(node.Arg("site"), "dug", StringComparison.OrdinalIgnoreCase))
            {
                // site = dug end = bottom|top offsetUp = 3: over the deepest (or highest) point of the void of the nearest
                // hole dug alone (a core drill's shaft, a blast hole), searched on a 1 m grid.
                net = FindNetwork(node, vessel, out _);
                bool top = string.Equals(node.Arg("end", "bottom"), "top", StringComparison.OrdinalIgnoreCase);
                Aabb b = net.Bounds;
                Vec3d deepest = default;
                double lowest = double.MaxValue;
                // A 1 m grid, coarser on big holes (a 2.4 km shaft took 70 s at 1 m): at most about two million points.
                Vec3 extent = b.Max - b.Min;
                float g = Math.Max(1f, (float)Math.Pow(Math.Max(1.0, (double)extent.X * extent.Y * extent.Z) / 2e6, 1.0 / 3.0));
                for (float x = b.Min.X; x <= b.Max.X; x += g)
                {
                    for (float y = b.Min.Y; y <= b.Max.Y; y += g)
                    {
                        for (float z = b.Min.Z; z <= b.Max.Z; z += g)
                        {
                            var p = new Vec3(x, y, z);
                            Vec3d at = net.ToBody(p);
                            double score = top ? -at.Length : at.Length;
                            if (score < lowest && net.IsInside(p))
                            {
                                lowest = score;
                                deepest = at;
                            }
                        }
                    }
                }

                if (lowest == double.MaxValue)
                {
                    throw new InvalidOperationException($"{net.Id} has no void");
                }

                if (top)
                {
                    // The middle of the mouth, not its highest point (that is on the rim, by the higher ground).
                    Vec3d sum = default;
                    int count = 0;
                    for (float x = b.Min.X; x <= b.Max.X; x += g)
                    {
                        for (float y = b.Min.Y; y <= b.Max.Y; y += g)
                        {
                            for (float z = b.Min.Z; z <= b.Max.Z; z += g)
                            {
                                var p = new Vec3(x, y, z);
                                Vec3d at = net.ToBody(p);
                                if (at.Length > deepest.Length - Math.Max(3, g) && net.IsInside(p))
                                {
                                    sum += at;
                                    count++;
                                }
                            }
                        }
                    }

                    deepest = sum.Normalized * deepest.Length;
                }

                target = deepest + deepest.Normalized * offsetUp;
                bodyName = net.Body;
                where = Invariant($"{net.Id} {(top ? "highest" : "deepest")} point (altitude {deepest.Length - vessel.mainBody.Radius:F0} m) + {offsetUp} m");
                inside = false;
            }
            else
            {
                net = FindNetwork(node, vessel, out BodyRuntime netRuntime);
                index = NodeIndex(net, node.Arg("node", "1"));
                target = NodePosition(net, index, inside, offsetUp);
                if (node.HasValue("ramp"))
                {
                    target = RampPosition(net, node.ArgDouble("ramp", 0.2), offsetUp);
                }

                if (node.HasValue("target"))
                {
                    // target = 0: over (above the terrain) or in (inside = true: at the same depth as) the centre of that
                    // radar target of the network, i.e. of its primitive of that index.
                    int which = (int)node.ArgDouble("target", 0);
                    Vec3d centre = netRuntime.NetworkTargets.TargetsOf(net)[which].Position;
                    target = inside ? centre + centre.Normalized * offsetUp
                        : centre.Normalized * (net.SurfaceRadiusNear(net.ToLocal(centre)) + offsetUp);
                }

                bodyName = net.Body;
                where = Invariant($"{net.Id} node {index} {(inside ? "inside" : "above")} {offsetUp} m");
            }

            // Placed in the network's own body's frame, and its orbit restarted around that body. Right after a teleport
            // to another body the vessel can still count as on the old one (its sphere of influence not switched yet);
            // releasing a hold then restarted its orbit around the old body, which sent it back to Kerbin or far off.
            // So the hold is dropped without a release, and the vessel stays on rails until placed.
            CelestialBody body = FlightGlobals.GetBodyByName(bodyName) ?? throw new InvalidOperationException($"no body {bodyName}");
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
            Vector3 oldUp = node.ArgBool("upright", false) ? -vessel.transform.forward
                : node.ArgBool("rootUp", false) ? vessel.transform.up : (vessel.transform.position - body.position).normalized;
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

            ctx.Pass(Invariant($"{where}: altitude {vessel.altitude:F1} m{(hold ? ", held" : "")} (before: {axes})"));
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
