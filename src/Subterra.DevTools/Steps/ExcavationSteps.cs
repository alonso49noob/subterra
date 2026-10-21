using System;
using System.Collections;
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
            StepRegistry.RegisterAssertion("partExists", PartExists);
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
            }

            module.Load(values);
            module.OnStart(PartModule.StartState.Landed);
            yield return null;
            ctx.Pass($"{name} on {root.partInfo.title}");
        }

        // moduleEvent module = ModuleSubterraCoreDrill event = StartDrilling — runs a module's KSPEvent.
        private static IEnumerator ModuleEvent(ConfigNode node, TestContext ctx)
        {
            PartModule module = Find(ctx, node.RequireArg("module"));
            string name = node.RequireArg("event");
            BaseEvent e = module.Events[name] ?? throw new ArgumentException($"no event {name}");
            e.Invoke();
            yield return null;
            ctx.Pass($"{module.moduleName}.{name}");
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
            var borer = (ModuleSubterraTunnelBorer)Find(ctx, nameof(ModuleSubterraTunnelBorer));
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
            var borer = (ModuleSubterraTunnelBorer)Find(ctx, nameof(ModuleSubterraTunnelBorer));
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
