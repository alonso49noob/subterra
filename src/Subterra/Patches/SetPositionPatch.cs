using System;
using HarmonyLib;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The cheat menu's Set Position under the ground (Lucas: a negative altitude should take the craft into the
    /// subsurface). KSP takes the altitude's absolute value; with this patch a negative one (the safety check turned off)
    /// puts the vessel that many metres under the terrain (under the sea level instead when "do not place underwater" is
    /// on and the ground is under it). In solid rock, a pocket a little larger than the vessel is carved around it
    /// (Settings: carveOnSetPosition), so it is not crushed by the rock.
    /// </summary>
    public static class SetPositionPatch
    {
        public const string Id = "setPosition";

        /// <summary>Room around the vessel in a carved pocket (m).</summary>
        private const float Margin = 2f;

        private struct Wanted
        {
            public double Lat, Lon, Altitude;
        }

        public static void Register()
        {
            PatchManager.Register(Id, harmony => harmony.Patch(
                AccessTools.Method(typeof(FlightGlobals), nameof(FlightGlobals.SetVesselPosition),
                    new[] { typeof(int), typeof(double), typeof(double), typeof(double), typeof(Vector3), typeof(bool), typeof(bool), typeof(double) })
                    ?? throw new MissingMethodException("FlightGlobals", "SetVesselPosition"),
                prefix: new HarmonyMethod(typeof(SetPositionPatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(SetPositionPatch), nameof(Postfix))));
        }

        private static void Prefix(int selBodyIndex, double latitude, double longitude, double altitude, bool asl, out Wanted? __state)
        {
            __state = null;
            try
            {
                if (altitude >= 0 || selBodyIndex < 0 || selBodyIndex >= FlightGlobals.Bodies.Count)
                {
                    return;
                }

                CelestialBody body = FlightGlobals.Bodies[selBodyIndex];
                if (body.pqsController == null || !BodyRegistry.Instance.TryGet(body, out _))
                {
                    return;
                }

                double ground = body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(latitude, longitude)) - body.Radius;
                double from = asl && ground < 0 ? 0 : ground;
                __state = new Wanted { Lat = latitude, Lon = longitude, Altitude = from + altitude };
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        // KSP has put the vessel over the ground (on rails, about to let it go): moved down to where it was asked, with a
        // pocket carved around it first, so it is inside a void when it goes off rails.
        private static void Postfix(int selBodyIndex, Wanted? __state)
        {
            if (__state == null)
            {
                return;
            }

            try
            {
                Wanted w = __state.Value;
                CelestialBody body = FlightGlobals.Bodies[selBodyIndex];
                Vessel vessel = FlightGlobals.ActiveVessel;
                if (vessel == null || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
                {
                    return;
                }

                Vec3d centre = BodyFrame.Direction(w.Lat, w.Lon) * (body.Radius + w.Altitude);
                if (Settings.Current.CarveOnSetPosition && Settings.Current.Excavation && runtime.Catalog.FindContaining(centre) == null
                    && runtime.Ocean?.At(centre) == null)
                {
                    Vector3 size = vessel.vesselSize;
                    float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z), 1f);
                    ExcavationService.Pocket(runtime, centre, longest * 0.5f + Margin, longest + Margin * 2);
                }

                // Pinned where it is asked (Landed: its orbit is not flown while it waits on rails for the cave's
                // colliders; Subterra's load guard lets it go once they exist), as DevTools' teleportToNetwork does.
                Vector3d rhs = body.GetWorldSurfacePosition(w.Lat, w.Lon, w.Altitude) - body.position;
                vessel.SetPosition(body.position + rhs);
                vessel.latitude = w.Lat;
                vessel.longitude = w.Lon;
                vessel.altitude = w.Altitude;
                Vector3d velocity = Vector3d.Cross(body.angularVelocity, rhs);
                vessel.orbit.UpdateFromStateVectors(rhs.xzy, velocity.xzy, body, Planetarium.GetUniversalTime());
                vessel.orbitDriver.updateFromParameters();
                vessel.Landed = true;
                Log.Info($"Set Position: {vessel.GetDisplayName()} put at altitude {w.Altitude:F0} m at {w.Lat:F4}, {w.Lon:F4} on {body.bodyName}");
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }
    }
}
