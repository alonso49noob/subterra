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
            public double Lat, Lon, Altitude, Ground;
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
                __state = new Wanted { Lat = latitude, Lon = longitude, Altitude = from + altitude, Ground = ground };
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        // The pocket for a vessel whose root part goes to rootTarget (world): its middle (world), radius and height round the
        // vertical through the craft's middle, from the corners of every part's renderers as the craft is turned now (KSP has
        // already turned it upright for where it goes), plus Margin all round and a little more headroom.
        private static void Fit(Vessel vessel, Vector3d rootTarget, Vector3d up, out Vector3d middle, out float radius, out float height)
        {
            var points = new System.Collections.Generic.List<Vector3d>();
            Vector3d root = vessel.transform.position;
            foreach (Part part in vessel.parts)
            {
                foreach (Renderer r in part.GetComponentsInChildren<Renderer>(false))
                {
                    if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.bounds.size.sqrMagnitude <= 0)
                    {
                        continue;
                    }

                    Bounds b = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3d((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                        points.Add(rootTarget + (corner - root));
                    }
                }

                points.Add(rootTarget + ((Vector3d)part.transform.position - root));
            }

            Vector3d centroid = Vector3d.zero;
            foreach (Vector3d p in points)
            {
                centroid += p;
            }

            centroid /= Math.Max(1, points.Count);
            double low = 0, high = 0, wide = 0;
            foreach (Vector3d p in points)
            {
                Vector3d d = p - centroid;
                double h = Vector3d.Dot(d, up);
                low = Math.Min(low, h);
                high = Math.Max(high, h);
                wide = Math.Max(wide, (d - up * h).magnitude);
            }

            middle = centroid + up * ((low + high) * 0.5 + Headroom * 0.5);
            radius = (float)Math.Max(1.5, wide + Margin);
            height = (float)Math.Max(3, high - low + Margin * 2 + Headroom);
        }

        /// <summary>Extra room (m) over the craft in a carved pocket.</summary>
        private const double Headroom = 1;

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
                Vector3d rhs = body.GetWorldSurfacePosition(w.Lat, w.Lon, w.Altitude) - body.position;
                if (Settings.Current.CarveOnSetPosition && Settings.Current.Excavation && runtime.Catalog.FindContaining(centre) == null
                    && runtime.Ocean?.At(centre) == null
                    // In rock only: not in KSP's sea over a sea floor (Set Position under the sea level, off the coast).
                    && w.Altitude < w.Ground - 1)
                {
                    // Round the vessel as it will stand there (Lucas: craft set deep fell through the floor of their pocket and
                    // kept going down). The pocket was sized from vessel.vesselSize and centred on the root part: parts below a
                    // root at one end of the craft started under its floor, and a cave's floor only stops what comes from
                    // above. Now from every part's real extent, centred on the craft, with room all round.
                    Fit(vessel, body.position + rhs, rhs.normalized, out Vector3d middle, out float radius, out float height);
                    ExcavationService.Pocket(runtime, Frames.ToBody(body, middle), radius, height);
                }

                // Pinned where it is asked (Landed: its orbit is not flown while it waits on rails for the cave's
                // colliders; Subterra's load guard lets it go once they exist), as DevTools' teleportToNetwork does.
                vessel.SetPosition(body.position + rhs);
                vessel.latitude = w.Lat;
                vessel.longitude = w.Lon;
                vessel.altitude = w.Altitude;
                Vector3d velocity = Vector3d.Cross(body.angularVelocity, rhs);
                vessel.orbit.UpdateFromStateVectors(rhs.xzy, velocity.xzy, body, Planetarium.GetUniversalTime());
                vessel.orbitDriver.updateFromParameters();
                vessel.Landed = true;

                // Held still while it waits on rails. KSP had put it on rails before this, not yet landed, so its orbit
                // driver was left flying its Kepler orbit, whose gravity is a point mass's: 100 km under Kerbin it pulled
                // the craft out of its pocket at 14 m/s² (358 m/s² 500 km down) before it was let go (Lucas: it ignored
                // its pocket and kept going down).
                if (vessel.packed && vessel.orbitDriver != null)
                {
                    vessel.orbitDriver.SetOrbitMode(OrbitDriver.UpdateMode.IDLE);
                }
                Log.Info($"Set Position: {vessel.GetDisplayName()} put at altitude {w.Altitude:F0} m at {w.Lat:F4}, {w.Lon:F4} on {body.bodyName}");
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }
    }
}
