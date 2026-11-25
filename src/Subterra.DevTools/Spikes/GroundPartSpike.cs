using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// M0 spike (radar Task 3): can a part be left on the ground without the Breaking Ground DLC, either from a kerbal's
    /// inventory on EVA (ModuleInventoryPart, the 1.11+ EVA construction code) or dropped from a rover with a decoupler?
    /// Throwaway code.
    /// </summary>
    [StepProvider]
    internal static class GroundPartSpike
    {
        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static void Register()
        {
            StepRegistry.RegisterSpike("GroundPart", GroundPart);
            StepRegistry.RegisterSpike("RoverDrop", RoverDrop);
            StepRegistry.RegisterSpike("GroundPartCheck", Check);
        }

        // spike name = GroundPartCheck part = sensorAccelerometer — logs every vessel made of that part (after a quickload,
        // to see that the dropped part persists as a vessel).
        private static IEnumerator Check(ConfigNode node, TestContext ctx)
        {
            string partName = node.Arg("part", "sensorAccelerometer");
            var found = FlightGlobals.Vessels.Where(v => v.protoVessel != null && v.protoVessel.protoPartSnapshots.Count == 1 && v.protoVessel.protoPartSnapshots[0].partName == partName).ToList();
            var report = new StringBuilder("after the reload, vessels made of " + partName + ": " + found.Count + Environment.NewLine);
            foreach (Vessel v in found)
            {
                report.AppendLine("    " + Describe(v));
            }

            Finish(report, ctx, found.Count > 0);
            yield break;
        }

        private static string Describe(Vessel v)
        {
            return Invariant(
                $"'{v.GetDisplayName()}' type {v.vesselType}, situation {v.situation}, Landed {v.Landed}, loaded {v.loaded}, packed {v.packed}, parts {v.parts.Count}, srfSpeed {v.srfSpeed:F3} m/s, alt {v.altitude:F2} m, {Vector3d.Distance(v.GetWorldPos3D(), FlightGlobals.ActiveVessel.GetWorldPos3D()):F1} m from the active vessel");
        }

        // spike name = GroundPart part = sensorAccelerometer distance = 3 — the kerbal on EVA puts the part in its
        // inventory (by code) and leaves it on the ground that far in front of it; logs what became of it.
        private static IEnumerator GroundPart(ConfigNode node, TestContext ctx)
        {
            Vessel kerbal = FlightGlobals.ActiveVessel;
            if (kerbal == null || !kerbal.isEVA)
            {
                throw new InvalidOperationException("the active vessel is not a kerbal on EVA");
            }

            string partName = node.Arg("part", "sensorAccelerometer");
            double distance = node.ArgDouble("distance", 3);
            var report = new StringBuilder();
            AvailablePart info = PartLoader.getPartInfoByName(partName) ?? throw new ArgumentException("unknown part " + partName);
            report.AppendLine($"part {partName}: title '{info.title}', file {info.partUrl}, modules [{string.Join(", ", info.partPrefab.Modules.Cast<PartModule>().Select(m => m.moduleName).ToArray())}]");
            ModuleCargoPart cargo = info.partPrefab.FindModuleImplementing<ModuleCargoPart>() ?? throw new InvalidOperationException(partName + " has no ModuleCargoPart");
            report.AppendLine(Invariant($"ModuleCargoPart: packedVolume {cargo.packedVolume}, stackableQuantity {cargo.stackableQuantity}, mass {info.partPrefab.mass}"));
            report.AppendLine("DLC: any expansion installed = " + Expansions.ExpansionsLoader.IsAnyExpansionInstalled() + ", Serenity (Breaking Ground) = " + Expansions.ExpansionsLoader.IsExpansionInstalled("Serenity") + ", MakingHistory = " + Expansions.ExpansionsLoader.IsExpansionInstalled("MakingHistory"));

            ModuleInventoryPart inventory = kerbal.rootPart.FindModuleImplementing<ModuleInventoryPart>() ?? throw new InvalidOperationException("the kerbal has no ModuleInventoryPart");
            report.AppendLine(Invariant($"kerbal inventory: slots {inventory.InventorySlots}, empty {inventory.TotalEmptySlots()}, AbleToPlaceParts {inventory.AbleToPlaceParts}, IsKerbalOnEVA {inventory.IsKerbalOnEVA}, volumeCapacity {inventory.volumeCapacity}, massCapacity {inventory.massCapacity}"));

            // The default load (parachute, EVA propellant) fills both slots: free one, like a player would.
            if (inventory.TotalEmptySlots() == 0)
            {
                report.AppendLine("inventory full: ClearPartAtSlot(0) = " + inventory.ClearPartAtSlot(0));
            }

            int slot = inventory.FirstEmptySlot();
            bool stored = inventory.StoreCargoPartAtSlot(partName, slot);
            report.AppendLine($"StoreCargoPartAtSlot('{partName}', {slot}) = {stored}; stored parts now {inventory.InventoryItemCount}");
            if (!stored)
            {
                Finish(report, ctx, false);
                yield break;
            }

            StoredPart storedPart = inventory.storedParts[slot];
            HashSet<uint> before = new HashSet<uint>(FlightGlobals.Vessels.Select(v => v.persistentId));
            report.AppendLine("vessels before: " + FlightGlobals.Vessels.Count);

            // Where: 'distance' metres in front of the kerbal, on the terrain (physics layer 15).
            Vector3 up = (kerbal.transform.position - kerbal.mainBody.position).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(kerbal.transform.forward, up).normalized;
            Vector3 origin = kerbal.transform.position + forward * (float)distance + up * 5f;
            Vector3 position = origin - up * 5f;
            Quaternion rotation = Quaternion.LookRotation(forward, up);
            if (Physics.Raycast(origin, -up, out RaycastHit hit, 30f, 1 << 15))
            {
                position = hit.point + hit.normal * 0.05f;
                rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, hit.normal).normalized, hit.normal);
                report.AppendLine(Invariant($"terrain hit {hit.distance - 5f:F2} m below the kerbal's height, normal {Vector3.Angle(up, hit.normal):F1} deg from vertical"));
            }
            else
            {
                report.AppendLine("no terrain hit: dropping at the kerbal's height");
            }

            MethodInfo deploy = typeof(ModuleInventoryPart).GetMethod("DeployGroundPart", Inst) ?? throw new MissingMethodException("DeployGroundPart");
            report.AppendLine("DeployGroundPart: " + deploy + (deploy.IsPublic ? " (public)" : " (non-public)"));
            deploy.Invoke(inventory, new object[] { storedPart, position, rotation });

            // The interactive flow removes the stored part after placing it; do the same if it is still there.
            bool cleared = !inventory.IsSlotEmpty(slot) && inventory.ClearPartAtSlot(slot);
            report.AppendLine($"slot {slot} cleared by us: {cleared}; inventory items now {inventory.InventoryItemCount}");

            foreach (float wait in new[] { 0.5f, 2f, 7.5f })
            {
                yield return new WaitForSeconds(wait);
                var created = FlightGlobals.Vessels.Where(v => !before.Contains(v.persistentId)).ToList();
                report.AppendLine(Invariant($"t+{wait}s: {created.Count} new vessel(s)"));
                foreach (Vessel v in created)
                {
                    report.AppendLine("    " + Describe(v));
                }
            }

            Finish(report, ctx, FlightGlobals.Vessels.Any(v => !before.Contains(v.persistentId)));
        }

        // spike name = RoverDrop part = stackDecouplerMini ejection = 0 — fires a decoupler of the active rover and logs the vessel
        // that is left on the ground (type, situation, speed); then makes it a Probe, to see whether KSP keeps it.
        private static IEnumerator RoverDrop(ConfigNode node, TestContext ctx)
        {
            Vessel rover = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            string partName = node.Arg("part", "stackDecouplerMini");
            Part decoupler = rover.parts.FirstOrDefault(p => p.partInfo.name == partName)
                ?? rover.parts.FirstOrDefault(p => p.FindModuleImplementing<ModuleDecouplerBase>() != null)
                ?? throw new ArgumentException("no decoupler on " + rover.vesselName + ": " + string.Join(", ", rover.parts.Select(p => p.partInfo.name).Distinct().ToArray()));
            var report = new StringBuilder();
            ModuleDecouplerBase module = decoupler.FindModuleImplementing<ModuleDecouplerBase>();
            report.AppendLine($"decoupler {decoupler.partInfo.name}: module {(module == null ? "none" : module.moduleName)}, parent {decoupler.parent?.partInfo.name}, children [{string.Join(", ", decoupler.children.Select(c => c.partInfo.name).ToArray())}]");
            if (node.HasValue("ejection"))
            {
                // The stock decoupler throws the part away (49 m/s in the first run); a geophone must stay where it falls.
                report.AppendLine($"ejectionForce {module.ejectionForce} -> {node.ArgDouble("ejection", 0)}");
                module.ejectionForce = (float)node.ArgDouble("ejection", 0);
            }

            HashSet<uint> before = new HashSet<uint>(FlightGlobals.Vessels.Select(v => v.persistentId));
            module.Decouple();
            foreach (float wait in new[] { 0.5f, 2f, 7.5f })
            {
                yield return new WaitForSeconds(wait);
                var created = FlightGlobals.Vessels.Where(v => !before.Contains(v.persistentId)).ToList();
                report.AppendLine(Invariant($"t+{wait}s: {created.Count} new vessel(s)"));
                foreach (Vessel v in created)
                {
                    report.AppendLine("    " + Describe(v));
                    if (wait == 0.5f)
                    {
                        v.vesselType = VesselType.Probe;
                    }
                }
            }

            Finish(report, ctx, FlightGlobals.Vessels.Any(v => !before.Contains(v.persistentId)));
        }

        private static void Finish(StringBuilder report, TestContext ctx, bool ok)
        {
            Log.Info("GroundPartSpike report:\n" + report);
            System.IO.File.AppendAllText(System.IO.Path.Combine(ctx.ResultsDir, "groundpart.txt"), report + Environment.NewLine);
            if (ok)
            {
                ctx.Pass("see groundpart.txt");
            }
            else
            {
                ctx.Fail("no new vessel (see groundpart.txt)");
            }
        }
    }
}
