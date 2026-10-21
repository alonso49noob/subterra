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
            StepRegistry.Register("trace", Trace);
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

        // trace seconds = 10 — logs the tracked vessel's height over the terrain, vertical speed and what its parts touch
        // every quarter second (diagnostics).
        private static IEnumerator Trace(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            float until = Time.time + (float)node.ArgDouble("seconds", 10);
            while (Time.time < until && vessel != null)
            {
                var touching = new System.Collections.Generic.HashSet<string>();
                foreach (Part p in vessel.parts)
                {
                    foreach (Collider c in Physics.OverlapSphere(p.transform.position, 0.6f, 1 << 15, QueryTriggerInteraction.Ignore))
                    {
                        touching.Add(p.partInfo.name + ">" + c.gameObject.name);
                    }
                }

                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[Subterra.DevTools] trace h {0:F2} vs {1:F2} touching {2}",
                    vessel.heightFromTerrain, vessel.verticalSpeed, string.Join(", ", touching)));
                yield return new WaitForSeconds(0.25f);
            }

            ctx.Pass("trace");
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
