using System;
using System.Collections;
using UnityEngine;

namespace Subterra.DevTools
{
    /// <summary>Vessel control steps.</summary>
    [StepProvider]
    internal static class VesselSteps
    {
        internal static void Register()
        {
            StepRegistry.Register("brakes", Brakes);
            StepRegistry.Register("lights", Lights);
            StepRegistry.Register("floodlight", Floodlight);
        }

        // lights on = true — the tracked vessel's Light action group (its lamps).
        private static IEnumerator Lights(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            bool on = node.ArgBool("on", true);
            vessel.ActionGroups.SetGroup(KSPActionGroup.Light, on);
            yield return null;
            ctx.Pass($"lights {(on ? "on" : "off")} on {vessel.GetDisplayName()}");
        }

        // floodlight on = true, range = 90, intensity = 2.2, up = 6 — a photographer's light over the tracked vessel
        // (presentation shots only: not part of the mod).
        private static IEnumerator Floodlight(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            Transform old = vessel.transform.Find("SubterraFloodlight");
            if (old != null)
            {
                UnityEngine.Object.Destroy(old.gameObject);
            }

            if (node.ArgBool("on", true))
            {
                var go = new GameObject("SubterraFloodlight");
                go.transform.SetParent(vessel.transform, false);
                go.transform.position = vessel.transform.position + (Vector3)(vessel.upAxis * node.ArgDouble("up", 6));
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = (float)node.ArgDouble("range", 90);
                light.intensity = (float)node.ArgDouble("intensity", 2.2);
                light.color = new Color(1f, 0.95f, 0.88f);
                light.shadows = LightShadows.Soft;
                light.renderMode = LightRenderMode.ForcePixel;
            }

            yield return null;
            ctx.Pass("floodlight " + (node.ArgBool("on", true) ? "on" : "off"));
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
