using System;
using System.Collections;
using System.Text;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Test-only steps for the map view and the Tracking Station.</summary>
    [StepProvider]
    internal static class MapSteps
    {
        internal static void Register()
        {
            StepRegistry.Register("mapView", MapViewStep);
            StepRegistry.Register("mapOverlayShown", MapOverlayShown);
            StepRegistry.Register("mapOverlayFail", MapOverlayFail);
        }

        // mapOverlayFail count = 1 - the next paints of the map overlay throw on the worker (the failure path).
        private static IEnumerator MapOverlayFail(ConfigNode node, TestContext ctx)
        {
            int count = (int)node.ArgDouble("count", 1);
            MapOverlay.FailNextPaints(count);
            yield return null;
            ctx.Pass(count + " paint(s) will fail");
        }

        // mapOverlayShown on = false - presses the overlay's toolbar button (or releases it): the player's choice that
        // the overlay follows.
        private static IEnumerator MapOverlayShown(ConfigNode node, TestContext ctx)
        {
            bool on = node.ArgBool("on", true);
            MapOverlay.UserShown = on;
            yield return null;
            ctx.Pass("overlay button " + (on ? "pressed" : "released"));
        }

        // mapView body = Mun on = true distance = 4 heading = 0 pitch = 0.3 lat = -6.6 lon = 2.2 probe = false
        // - enters (on = true, the default) or leaves the map view, points the planetarium camera at the body and
        // sets its distance in body radii. With lat and lon the camera is put on the line from the body's centre
        // through that surface point, so the point is in the middle of the picture (with sunward = true, the camera
        // goes to the lit side and the subsolar point is reported); heading and pitch, in radians, are used otherwise). Works in the Tracking Station too (no map view to enter there).
        private static IEnumerator MapViewStep(ConfigNode node, TestContext ctx)
        {
            if (!node.ArgBool("on", true))
            {
                if (MapView.MapIsEnabled)
                {
                    MapView.ExitMapView();
                    yield return new WaitForSeconds(1f);
                }

                ctx.Pass("map view off");
                yield break;
            }

            CelestialBody body = FlightGlobals.GetBodyByName(node.RequireArg("body")) ?? throw new ArgumentException("unknown body");
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && !MapView.MapIsEnabled)
            {
                MapView.EnterMapView();
                yield return new WaitForSeconds(1f);
            }

            PlanetariumCamera camera = PlanetariumCamera.fetch ?? throw new InvalidOperationException("no planetarium camera");
            camera.SetTarget(body.MapObject);
            yield return null;
            camera.SetDistance((float)(body.Radius * ScaledSpace.InverseScaleFactor * node.ArgDouble("distance", 4)));

            if (node.ArgBool("probe", false))
            {
                var report = new StringBuilder();
                foreach (float h in new[] { 0f, 1f, 2f })
                {
                    foreach (float p in new[] { 0f, 0.5f, -0.5f })
                    {
                        camera.camHdg = h;
                        camera.camPitch = p;
                        yield return new WaitForSeconds(0.5f);
                        Vector3 d = CameraDirection(camera, body);
                        report.Append(Invariant($"[hdg {h} pitch {p} -> ({d.x:F3},{d.y:F3},{d.z:F3})] "));
                    }
                }

                ctx.Pass("camera probe " + report);
                yield break;
            }

            Vector3? wanted = null;
            string sun = "";
            bool sunward = node.ArgBool("sunward", false);
            if (sunward || node.HasValue("lat") && node.HasValue("lon"))
            {
                Vector3d world;
                if (sunward)
                {
                    // The subsolar point, in the body's own latitude and longitude (from the body's geographic axes).
                    world = (FlightGlobals.Bodies[0].position - body.position).normalized;
                    Vector3d e0 = (body.GetWorldSurfacePosition(0, 0, 0) - body.position).normalized;
                    Vector3d e90 = (body.GetWorldSurfacePosition(0, 90, 0) - body.position).normalized;
                    Vector3d n = (body.GetWorldSurfacePosition(90, 0, 0) - body.position).normalized;
                    sun = Invariant($", subsolar point lat {Math.Asin(Vector3d.Dot(world, n)) * 180 / Math.PI:F1} lon {Math.Atan2(Vector3d.Dot(world, e90), Vector3d.Dot(world, e0)) * 180 / Math.PI:F1}");
                }
                else
                {
                    world = (body.GetWorldSurfacePosition(node.ArgDouble("lat", 0), node.ArgDouble("lon", 0), 0) - body.position).normalized;
                }

                // The camera's heading is measured from an axis that is not the world's (about 50 degrees off for
                // Kerbin at the start of a game), so read where heading 0 puts the camera and turn from there.
                camera.camHdg = 0;
                camera.camPitch = 0;
                yield return new WaitForSeconds(0.5f);
                Vector3 zero = CameraDirection(camera, body);
                camera.camHdg = Mathf.Atan2((float)world.x, (float)world.z) - Mathf.Atan2(zero.x, zero.z);
                camera.camPitch = Mathf.Asin((float)world.y);
                wanted = new Vector3((float)world.x, (float)world.y, (float)world.z);
            }
            else
            {
                camera.camHdg = (float)node.ArgDouble("heading", 0);
                camera.camPitch = (float)node.ArgDouble("pitch", 0.3);
            }

            yield return new WaitForSeconds(1.5f);
            Vector3 got = CameraDirection(camera, body);
            string aim = wanted.HasValue ? Invariant($", {Vector3.Angle(got, wanted.Value):F2} deg from the point") : "";
            ctx.Pass(Invariant($"map on {body.bodyName}, camera at ({got.x:F3},{got.y:F3},{got.z:F3}) from the centre, hdg {camera.camHdg:F3} pitch {camera.camPitch:F3}, distance {camera.Distance:F0}") + aim + sun);
        }

        private static Vector3 CameraDirection(PlanetariumCamera camera, CelestialBody body)
        {
            return (camera.GetCameraTransform().position - body.scaledBody.transform.position).normalized;
        }
    }
}
