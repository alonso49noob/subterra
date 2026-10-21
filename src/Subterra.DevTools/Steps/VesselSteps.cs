using System;
using System.Collections;

namespace Subterra.DevTools
{
    /// <summary>Vessel control steps.</summary>
    [StepProvider]
    internal static class VesselSteps
    {
        internal static void Register()
        {
            StepRegistry.Register("brakes", Brakes);
        }

        // brakes on = true — sets the brakes action group of the tracked vessel (a parked rover must not roll).
        private static IEnumerator Brakes(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            bool on = node.ArgBool("on", true);
            vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, on);
            yield return null;
            ctx.Pass($"brakes {(on ? "on" : "off")} on {vessel.GetDisplayName()}");
        }
    }
}
