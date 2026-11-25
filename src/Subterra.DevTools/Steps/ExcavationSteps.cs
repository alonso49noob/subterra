using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Excavation;
using Subterra.Core.Model;
using Subterra.Core.Tubes;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Steps and assertions for sub-project 2: drill and borer modules on a vessel in flight, tunnels.</summary>
    [StepProvider]
    internal static class ExcavationSteps
    {
        internal static void Register()
        {
            StepRegistry.Register("addModule", AddModule);
            StepRegistry.Register("moduleEvent", ModuleEvent);
            StepRegistry.Register("addResource", AddResource);
            StepRegistry.Register("setField", SetField);
            StepRegistry.Register("teleportIntoTunnel", TeleportIntoTunnel);
            StepRegistry.Register("aimLaser", AimLaser);
            StepRegistry.RegisterAssertion("resourceAtLeast", ResourceAtLeast);
            StepRegistry.RegisterAssertion("partExists", PartExists);
            StepRegistry.RegisterAssertion("objectExists", ObjectExists);
            StepRegistry.RegisterAssertion("objectGone", ObjectGone);
            StepRegistry.RegisterAssertion("componentExists", ComponentExists);
            StepRegistry.RegisterAssertion("drillDepthAtLeast", DrillDepthAtLeast);
            StepRegistry.RegisterAssertion("drillLayer", DrillLayer);
            StepRegistry.RegisterAssertion("coreSample", CoreSample);
            StepRegistry.RegisterAssertion("tunnelLengthAtLeast", TunnelLengthAtLeast);
            StepRegistry.RegisterAssertion("tunnelJoined", TunnelJoined);
            StepRegistry.RegisterAssertion("networkHasZone", NetworkHasZone);
        }

        // addModule module = ModuleSubterraCoreDrill pitch = -20 <any KSPField = value> — adds the module to the
        // tracked vessel's root part, loads the given fields, starts it. pitch (degrees, borer only) sets
        // digAxis to the vessel's forward direction turned up (+) or down (−).
        private static IEnumerator AddModule(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            string name = node.RequireArg("module");
            Part root = vessel.rootPart;
            PartModule module = root.AddModule(name, true) ?? throw new InvalidOperationException($"cannot add {name}");
            var values = new ConfigNode("MODULE");
            values.AddValue("name", name);
            foreach (ConfigNode.Value v in node.values)
            {
                if (v.name != "do" && v.name != "module" && v.name != "pitch")
                {
                    values.AddValue(v.name, v.value);
                }
            }

            if (node.HasValue("pitch"))
            {
                Vector3 forward = vessel.ReferenceTransform.up;
                Vector3 up = (vessel.CoM - vessel.mainBody.position).normalized;
                Vector3 level = Vector3.ProjectOnPlane(forward, up).normalized;
                float pitch = (float)node.ArgDouble("pitch", 0);
                Vector3 dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(level, up)) * level;
                Vector3 local = root.transform.InverseTransformDirection(dir);
                values.AddValue("digAxis", Invariant($"{local.x}, {local.y}, {local.z}"));
                // The laser is aimed by elevation (degrees up from the horizontal) instead.
                values.AddValue("elevation", Invariant($"{Mathf.Asin(Mathf.Clamp(Vector3.Dot(dir, up), -1f, 1f)) * Mathf.Rad2Deg}"));
                values.AddValue("heading", "0");
            }

            module.Load(values);
            module.OnStart(PartModule.StartState.Landed);
            yield return null;
            ctx.Pass($"{name} on {root.partInfo.title}");
        }

        // moduleEvent module = ModuleSubterraCoreDrill event = StartDrilling — runs a module's KSPEvent.
        // With all = true it runs on every module of that name on the tracked vessel (not only the first); with
        // part = SubterraEco on the first one on a part of that name.
        private static IEnumerator ModuleEvent(ConfigNode node, TestContext ctx)
        {
            string moduleName = node.RequireArg("module");
            string name = node.RequireArg("event");
            var modules = new List<PartModule>();
            if (node.ArgBool("all", false))
            {
                Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
                modules.AddRange(vessel.parts.SelectMany(p => p.Modules.Cast<PartModule>()).Where(m => m.moduleName == moduleName));
                if (modules.Count == 0)
                {
                    throw new InvalidOperationException($"no {moduleName} on {vessel.GetDisplayName()}");
                }
            }
            else if (node.HasValue("part"))
            {
                Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
                string partName = node.RequireArg("part");
                modules.Add(vessel.parts.Where(p => p.partInfo != null && p.partInfo.name == partName)
                    .SelectMany(p => p.Modules.Cast<PartModule>()).FirstOrDefault(m => m.moduleName == moduleName)
                    ?? throw new InvalidOperationException($"no {moduleName} on a {partName} of {vessel.GetDisplayName()}"));
            }
            else
            {
                modules.Add(Find(ctx, moduleName));
            }

            foreach (PartModule module in modules)
            {
                BaseEvent e = module.Events[name] ?? throw new ArgumentException($"no event {name}");
                e.Invoke();
            }

            yield return null;
            ctx.Pass($"{moduleName}.{name} x{modules.Count}");
        }

        // addResource name = Water max = 50 amount = 0 — a tank of that resource on the tracked vessel's root part.
        private static IEnumerator AddResource(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            string name = node.RequireArg("name");
            if (PartResourceLibrary.Instance.GetDefinition(name) == null)
            {
                throw new ArgumentException($"no resource {name} in this game");
            }

            var resource = new ConfigNode("RESOURCE");
            resource.AddValue("name", name);
            resource.AddValue("amount", node.ArgDouble("amount", 0));
            resource.AddValue("maxAmount", node.ArgDouble("max", 50));
            vessel.rootPart.AddResource(resource);
            vessel.UpdateResourceSets();
            yield return null;
            ctx.Pass($"{name} tank on {vessel.rootPart.partInfo.title}");
        }

        // objectExists prefix = SubterraBlastRings — a live GameObject whose name starts with that.
        private static bool ObjectExists(ConfigNode node, TestContext ctx) => CountObjects(node, ctx) > 0;

        // objectGone prefix = SubterraBlastRings — no live GameObject whose name starts with that.
        private static bool ObjectGone(ConfigNode node, TestContext ctx) => CountObjects(node, ctx) == 0;

        private static int CountObjects(ConfigNode node, TestContext ctx)
        {
            string prefix = node.RequireArg("prefix");
            int count = UnityEngine.Object.FindObjectsOfType<Transform>().Count(t => t.name.StartsWith(prefix, StringComparison.Ordinal));
            ctx.Detail = $"{count} object(s) named {prefix}*";
            return count;
        }

        // componentExists type = ProfileWindow — a live component of that type (simple name).
        private static bool ComponentExists(ConfigNode node, TestContext ctx)
        {
            string type = node.RequireArg("type");
            int count = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>().Count(b => b.GetType().Name == type);
            ctx.Detail = $"{count} {type}";
            return count > 0;
        }

        // resourceAtLeast name = Water min = 0.1 — the tracked vessel holds at least that much.
        private static bool ResourceAtLeast(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            string name = node.RequireArg("name");
            double amount = vessel.parts.SelectMany(p => p.Resources.Cast<PartResource>()).Where(r => r.resourceName == name).Sum(r => r.amount);
            var pump = vessel.parts.SelectMany(p => p.Modules.Cast<PartModule>()).OfType<ModuleSubterraWellPump>().FirstOrDefault();
            ctx.Detail = Invariant($"{name} {amount:F3}{(pump == null ? "" : $"; pump: {pump.uiStatus}, output {pump.uiOutput}")}");
            return amount >= node.ArgDouble("min", 0.1);
        }

        // setField module = ModuleSubterraTunnelBorer field = baseRate value = 5 — sets a KSPField of a module on the tracked vessel.
        private static IEnumerator SetField(ConfigNode node, TestContext ctx)
        {
            PartModule module = Find(ctx, node.RequireArg("module"));
            string field = node.RequireArg("field"), value = node.RequireArg("value");
            var values = new ConfigNode("MODULE");
            values.AddValue(field, value);
            module.Fields.Load(values);
            yield return null;
            ctx.Pass($"{module.moduleName}.{field} = {module.Fields[field]?.GetValue(module)}");
        }

        // teleportIntoTunnel along = 0.6 up = 1.5 — puts the tracked vessel inside the borer's tunnel, at that fraction
        // of its length, up metres over its floor, upright and facing along it (no roll: wheels on the floor).
        private static IEnumerator TeleportIntoTunnel(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            var borer = (ModuleSubterraLaser)Find(ctx, nameof(ModuleSubterraLaser));
            ExcavationSpec tunnel = borer.Tunnel ?? throw new InvalidOperationException("the laser has no tunnel yet");
            double along = node.ArgDouble("along", 0.6), up = node.ArgDouble("up", 1.5);
            Core.Numerics.Vec3 first = tunnel.Points[0], last = tunnel.Points[tunnel.Points.Count - 1];
            float length = (last - first).Length;
            if (node.HasValue("back"))
            {
                // back = metres short of the face (a vehicle as long as the rover must not reach into the rock).
                along = Math.Max(0, 1 - node.ArgDouble("back", 6) / Math.Max(1e-3, length));
            }

            Core.Numerics.Vec3d floor = tunnel.ToBody(Core.Numerics.Vec3.Lerp(first, last, (float)along));
            Core.Numerics.Vec3d target = floor + floor.Normalized * up;
            CelestialBody body = vessel.mainBody;

            ctx.Release();
            OrbitPhysicsManager.HoldVesselUnpack(2);
            vessel.GoOnRails();
            vessel.Landed = true;
            Vector3d world = Frames.ToWorld(body, target);
            Vector3 ahead = (Vector3)(Frames.ToWorld(body, tunnel.ToBody(last)) - Frames.ToWorld(body, tunnel.ToBody(first)));
            Vector3 vertical = (world - body.position).normalized;

            // Yaw about the local vertical so the borer's head points the tunnel's way, then pitch to the tunnel's slope.
            Vector3 head = (Vector3)borer.AimDirection();
            Vector3 headLevel = Vector3.ProjectOnPlane(head, vertical), aheadLevel = Vector3.ProjectOnPlane(ahead, vertical);
            Quaternion yaw = Quaternion.AngleAxis(Vector3.SignedAngle(headLevel, aheadLevel, vertical), vertical);
            Vector3 side = Vector3.Cross(vertical, aheadLevel).normalized;
            float slope = Vector3.SignedAngle(aheadLevel, ahead, side);
            Quaternion pitch = Quaternion.AngleAxis(slope, side);
            vessel.SetRotation(pitch * yaw * vessel.transform.rotation);
            vessel.SetPosition(world);
            body.GetLatLonAlt(vessel.transform.position, out vessel.latitude, out vessel.longitude, out vessel.altitude);
            TestContext.RestOrbitHere(vessel);
            yield return null;
            ctx.Pass(Invariant($"{vessel.GetDisplayName()} in {tunnel.Id} at {along:P0} of its {length:F1} m (slope {slope:F0}°), altitude {vessel.altitude:F1} m"));
        }

        // aimLaser site = DevGeode node = 0 — aims the tracked vessel's laser (heading from its nose, elevation) at that
        // network node's middle.
        private static IEnumerator AimLaser(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            var laser = (ModuleSubterraLaser)Find(ctx, nameof(ModuleSubterraLaser));
            TubeNetwork net = NetworkSteps.FindNetwork(node, vessel, out _);
            Core.Numerics.Vec3d target = NetworkSteps.NodePosition(net, NetworkSteps.NodeIndex(net, node.Arg("node", "0")), true, 0);
            Vector3 dir = ((Vector3)(Frames.ToWorld(vessel.mainBody, target) - laser.part.transform.position)).normalized;
            Vector3 up = (vessel.CoM - vessel.mainBody.position).normalized;
            Vector3 nose = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, up).normalized;
            laser.elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(dir, up), -1f, 1f)) * Mathf.Rad2Deg;
            laser.heading = Vector3.SignedAngle(nose, Vector3.ProjectOnPlane(dir, up), up);
            yield return null;
            ctx.Pass(Invariant($"laser aimed at {net.Id}: heading {laser.heading:F0}°, elevation {laser.elevation:F0}°"));
        }

        // partExists name = SubterraCoreDrill — the part was loaded (its cfg and modules parse).
        private static bool PartExists(ConfigNode node, TestContext ctx)
        {
            string name = node.RequireArg("name");
            AvailablePart part = PartLoader.getPartInfoByName(name);
            ctx.Detail = part == null ? $"no part {name}" : $"{part.title}: {string.Join(", ", part.partPrefab.Modules.Cast<PartModule>().Select(m => m.moduleName))}";
            return part != null;
        }

        // drillDepthAtLeast min = 50 — the core drill's hole is at least that deep.
        private static bool DrillDepthAtLeast(ConfigNode node, TestContext ctx)
        {
            var drill = (ModuleSubterraCoreDrill)Find(ctx, nameof(ModuleSubterraCoreDrill));
            double min = node.ArgDouble("min", 1);
            ctx.Detail = Invariant($"depth {drill.Depth:F1} m, layer {drill.Layer?.Name ?? "-"}, status {drill.uiStatus}, rate {drill.uiRate:F3} m/s");
            return drill.Depth >= min;
        }

        // drillLayer name = MareBasalt — the layer at the bottom of the hole.
        private static bool DrillLayer(ConfigNode node, TestContext ctx)
        {
            var drill = (ModuleSubterraCoreDrill)Find(ctx, nameof(ModuleSubterraCoreDrill));
            string expected = node.RequireArg("name");
            ctx.Detail = Invariant($"layer {drill.Layer?.Name ?? "-"} at {drill.Depth:F1} m");
            return drill.Layer?.Name == expected;
        }

        // coreSample subject = subterraCoreSample@MunSrfLandedMareBasalt — takes a sample (closing the dialog) and checks its subject.
        private static bool CoreSample(ConfigNode node, TestContext ctx)
        {
            var drill = (ModuleSubterraCoreDrill)Find(ctx, nameof(ModuleSubterraCoreDrill));
            if (drill.GetScienceCount() == 0)
            {
                drill.TakeCoreSample();
            }

            KSP.UI.Screens.Flight.Dialogs.ExperimentsResultDialog dialog = UnityEngine.Object.FindObjectOfType<KSP.UI.Screens.Flight.Dialogs.ExperimentsResultDialog>();
            if (dialog != null)
            {
                UnityEngine.Object.Destroy(dialog.gameObject);
            }

            ScienceData[] data = drill.GetData();
            string expected = node.RequireArg("subject");
            ctx.Detail = data.Length == 0 ? "no data" : string.Join(", ", data.Select(d => Invariant($"{d.subjectID} ({d.dataAmount} Mits)")));
            return data.Any(d => d.subjectID == expected);
        }

        // tunnelLengthAtLeast min = 3 — the borer's tunnel is at least that long.
        private static bool TunnelLengthAtLeast(ConfigNode node, TestContext ctx)
        {
            var borer = (ModuleSubterraLaser)Find(ctx, nameof(ModuleSubterraLaser));
            ExcavationSpec tunnel = borer.Tunnel;
            double length = 0;
            if (tunnel != null)
            {
                for (int i = 0; i + 1 < tunnel.Points.Count; i++)
                {
                    length += (tunnel.Points[i + 1] - tunnel.Points[i]).Length;
                }
            }

            ctx.Detail = Invariant($"{tunnel?.Id ?? "no tunnel"}: {length:F1} m, {tunnel?.PieceCount ?? 0} pieces, joined to {tunnel?.JoinedTo ?? "-"}; status {borer.uiStatus}; {ExcavationService.All.Count} tunnels in the game");
            return length >= node.ArgDouble("min", 1);
        }

        // tunnelJoined to = site:DevTube — the borer's tunnel joined that network.
        private static bool TunnelJoined(ConfigNode node, TestContext ctx)
        {
            var borer = (ModuleSubterraLaser)Find(ctx, nameof(ModuleSubterraLaser));
            string expected = node.RequireArg("to");
            ctx.Detail = $"{borer.Tunnel?.Id ?? "no tunnel"} joined to {borer.Tunnel?.JoinedTo ?? "-"}";
            return borer.Tunnel?.JoinedTo == expected;
        }

        // networkHasZone id = site:DevTube zone = Excavation — the catalog's network has a primitive of that zone.
        private static bool NetworkHasZone(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            if (!BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                ctx.Detail = "no Subterra body";
                return false;
            }

            string id = node.RequireArg("id");
            var zone = (ZoneKind)Enum.Parse(typeof(ZoneKind), node.RequireArg("zone"));
            TubeNetwork net = runtime.Catalog.Find(id);
            int count = net?.Primitives.Count(p => p.Zone == zone) ?? 0;
            ctx.Detail = net == null ? $"no network {id}" : $"{id}: {net.Primitives.Count} primitives, {count} {zone}";
            return count > 0;
        }

        private static PartModule Find(TestContext ctx, string moduleName)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            return vessel.parts.SelectMany(p => p.Modules.Cast<PartModule>()).FirstOrDefault(m => m.moduleName == moduleName)
                ?? throw new InvalidOperationException($"no {moduleName} on {vessel.GetDisplayName()}");
        }
    }
}
