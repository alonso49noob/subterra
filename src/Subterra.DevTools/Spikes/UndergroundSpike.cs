using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// M0 spike (Task 5): a closed 60 × 60 × 30 m room 100 m under the terrain (layer-15 mesh collider,
    /// child of the PQS) and the patches that keep vessels and the camera working inside it; with
    /// <c>patches = off</c> the same scenario shows why each patch is needed.
    /// </summary>
    [StepProvider]
    internal static class UndergroundSpike
    {
        private static readonly Vector3 HalfSize = new Vector3(30f, 15f, 30f);
        private static GameObject room;
        private static MeshCollider roomCollider;
        private static bool patchesOn;
        private static Harmony harmony;
        private static float savedMinHeightAtMin, savedMinHeightAtMax;
        private static bool cameraOverridden;
        private static bool probing;

        internal static void Register()
        {
            StepRegistry.RegisterSpike("TestRoom", TestRoom);
            StepRegistry.RegisterSpike("LoadGuardProbe", LoadGuardProbe);
            StepRegistry.RegisterAssertion("insideRoom", (n, ctx) => Check(ctx, Inside(ctx.TrackedVessel?.GetWorldPos3D() ?? Vector3d.zero), "inside the room"));
            StepRegistry.RegisterAssertion("cameraBelowTerrain", CameraBelowTerrain);
            StepRegistry.RegisterAssertion("vesselPacked", (n, ctx) => Check(ctx, ctx.TrackedVessel != null && ctx.TrackedVessel.packed, "packed"));
            StepRegistry.RegisterAssertion("positionDelta", PositionDelta);
        }

        // spike name = TestRoom patches = on|off depth = 100 place = true placeHeight = 10 — builds the room
        // under the active vessel and (place) moves the vessel that high above its floor (big craft need
        // room: parts below the root must not start inside the floor collider).
        private static IEnumerator TestRoom(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            CelestialBody body = vessel.mainBody;
            PQS pqs = body.pqsController;
            patchesOn = string.Equals(node.Arg("patches", "on"), "on", StringComparison.OrdinalIgnoreCase);
            float depth = (float)node.ArgDouble("depth", 100);
            bool place = node.ArgBool("place", true);
            float placeHeight = (float)node.ArgDouble("placeHeight", 10);

            yield return ctx.WaitFor(() => !vessel.packed, 60, "the vessel to go off rails");
            double terrain = pqs.GetSurfaceHeight(body.GetRelSurfaceNVector(vessel.latitude, vessel.longitude)) - body.Radius;
            Vector3d floor = body.GetWorldSurfacePosition(vessel.latitude, vessel.longitude, terrain - depth);
            Vector3 up = (floor - body.position).normalized;

            if (room != null)
            {
                UnityEngine.Object.Destroy(room);
            }

            room = new GameObject("SubterraTestRoom") { layer = 15 };
            room.transform.SetParent(pqs.transform, true);
            room.transform.position = floor + (Vector3d)(up * HalfSize.y);
            room.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            Mesh mesh = RoomMesh();
            room.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = room.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("KSP/Bumped Specular")) { color = new Color(0.45f, 0.43f, 0.4f) };
            roomCollider = room.AddComponent<MeshCollider>();
            roomCollider.sharedMesh = mesh;

            ApplyPatches();
            if (!probing)
            {
                probing = true;
                GameEvents.onFlightReady.Add(() => ctx.Runner.StartCoroutine(Probe("flight ready")));
            }

            if (place)
            {
                // Moving an unpacked vessel 100 m in one frame gives its parts a huge velocity: pack it,
                // move it pinned (Landed = idle orbit driver), and let KSP unpack it (as SetVesselPosition does).
                OrbitPhysicsManager.HoldVesselUnpack(5);
                vessel.GoOnRails();
                vessel.Landed = true;
                vessel.SetPosition(floor + (Vector3d)(up * placeHeight));
                body.GetLatLonAlt(vessel.transform.position, out vessel.latitude, out vessel.longitude, out vessel.altitude);
                yield return null;
                yield return ctx.WaitFor(() => !vessel.packed, 60, "the vessel to go off rails in the room");
            }

            ctx.Pass(Invariant($"room floor {depth} m under the terrain (altitude {body.GetAltitude(floor):F1} m), patches {(patchesOn ? "on" : "off")}{(place ? ", vessel placed" : "")}"));
        }

        // Logs the active vessel every frame for 3 s (diagnosing what happens around unpacking).
        private static IEnumerator Probe(string why)
        {
            float until = Time.realtimeSinceStartup + 3f;
            Debug.Log("[Subterra.DevTools] probe: " + why);
            while (Time.realtimeSinceStartup < until)
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null)
                {
                    Debug.Log(Invariant($"[Subterra.DevTools] probe f{Time.frameCount}: {v.GetDisplayName()} packed {v.packed} landed {v.Landed} alt {v.altitude:F2} terrainAlt {v.terrainAltitude:F2} hft {v.heightFromTerrain:F2} inside {Inside(v.GetWorldPos3D())} srfSpeed {v.srfSpeed:F2}"));
                }

                yield return null;
            }
        }

        // spike name = LoadGuardProbe — packs the vessel, removes the room collider for 5 s while a
        // load guard keeps it on rails, restores the collider, releases the guard; FAIL if the vessel
        // unpacked without a collider or did not survive.
        private static IEnumerator LoadGuardProbe(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            if (roomCollider == null)
            {
                throw new InvalidOperationException("run spike TestRoom first");
            }

            int parts = TestContext.PartCount(vessel);
            var saved = new Dictionary<VesselRanges.Situation, (float pack, float unpack)>();
            foreach (VesselRanges.Situation s in Situations(vessel))
            {
                saved[s] = (s.pack, s.unpack);
                s.unpack = 1f;
                s.pack = 1.5f;
            }

            OrbitPhysicsManager.HoldVesselUnpack(2);
            vessel.GoOnRails();
            yield return null;
            roomCollider.enabled = false;
            bool unpackedWithoutCollider = false;
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until)
            {
                OrbitPhysicsManager.HoldVesselUnpack(2);
                unpackedWithoutCollider |= !vessel.packed;
                yield return null;
            }

            roomCollider.enabled = true;
            for (int i = 0; i < 10; i++)
            {
                OrbitPhysicsManager.HoldVesselUnpack(2);
                yield return null;
            }

            foreach (var pair in saved)
            {
                pair.Key.pack = pair.Value.pack;
                pair.Key.unpack = pair.Value.unpack;
            }

            yield return ctx.WaitFor(() => !vessel.packed, 60, "the vessel to go off rails");
            yield return new WaitForSeconds(5f);
            bool alive = vessel != null && vessel.state != Vessel.State.DEAD && TestContext.PartCount(vessel) >= parts;
            string text = Invariant($"packed while the collider was missing: {!unpackedWithoutCollider}; alive {alive}, {vessel.situation}, inside room {Inside(vessel.GetWorldPos3D())}");
            if (unpackedWithoutCollider || !alive)
            {
                ctx.Fail(text);
            }
            else
            {
                ctx.Pass(text);
            }
        }

        private static IEnumerable<VesselRanges.Situation> Situations(Vessel v)
        {
            VesselRanges r = v.vesselRanges;
            return new[] { r.landed, r.prelaunch, r.flying, r.splashed, r.subOrbital };
        }

        private static bool Check(TestContext ctx, bool result, string what)
        {
            ctx.Detail = (result ? "" : "not ") + what;
            return result;
        }

        private static bool CameraBelowTerrain(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            Camera camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (vessel == null || camera == null)
            {
                ctx.Detail = "no vessel or camera";
                return false;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d p = camera.transform.position;
            double lat = body.GetLatitude(p), lon = body.GetLongitude(p), alt = body.GetAltitude(p);
            double terrain = body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(lat, lon)) - body.Radius;
            ctx.Detail = Invariant($"camera altitude {alt:F1} m, terrain {terrain:F1} m, camera {(Inside(p) ? "inside" : "outside")} the room");
            return alt < terrain - 5;
        }

        // positionDelta max = 1 ref = a — the tracked vessel is within max metres of snapshot ref.
        private static bool PositionDelta(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            string reference = node.RequireArg("ref");
            double max = node.ArgDouble("max", 1);
            if (vessel == null || !ctx.Snapshots.TryGetValue(reference, out Vector3d s))
            {
                ctx.Detail = vessel == null ? "no vessel" : "no snapshot " + reference;
                return false;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d then = body.GetWorldSurfacePosition(s.x, s.y, s.z);
            Vector3d now = body.GetWorldSurfacePosition(vessel.latitude, vessel.longitude, vessel.altitude);
            double delta = (now - then).magnitude;
            ctx.Detail = Invariant($"moved {delta:F2} m (max {max}), altitude {s.z:F1} → {vessel.altitude:F1} m");
            return delta <= max;
        }

        internal static bool Inside(Vector3d world)
        {
            if (room == null)
            {
                return false;
            }

            Vector3 local = room.transform.InverseTransformPoint(world);
            return Mathf.Abs(local.x) < HalfSize.x && Mathf.Abs(local.y) < HalfSize.y && Mathf.Abs(local.z) < HalfSize.z;
        }

        private static double FloorAltitude()
        {
            Vector3 floor = room.transform.TransformPoint(new Vector3(0, -HalfSize.y, 0));
            return FlightGlobals.ActiveVessel.mainBody.GetAltitude(floor);
        }

        private static void ApplyPatches()
        {
            if (harmony != null)
            {
                return;
            }

            harmony = new Harmony("Subterra.DevTools.UndergroundSpike");
            harmony.Patch(AccessTools.Method(typeof(Vessel), nameof(Vessel.PQSAltitude)),
                postfix: new HarmonyMethod(typeof(UndergroundSpike), nameof(PqsAltitudePostfix)));
            harmony.Patch(AccessTools.Method(typeof(Vessel), nameof(Vessel.CheckGroundCollision)),
                prefix: new HarmonyMethod(typeof(UndergroundSpike), nameof(CheckGroundCollisionPrefix)));
            harmony.Patch(AccessTools.Method(typeof(Vessel), "getCorrectedLandedAltitude"),
                prefix: new HarmonyMethod(typeof(UndergroundSpike), nameof(CorrectedLandedAltitudePrefix)));
            MethodInfo update = AccessTools.Method(typeof(FlightCamera), "UpdateCameraTransform");
            harmony.Patch(update,
                prefix: new HarmonyMethod(typeof(UndergroundSpike), nameof(CameraPrefix)),
                postfix: new HarmonyMethod(typeof(UndergroundSpike), nameof(CameraPostfix)),
                transpiler: new HarmonyMethod(typeof(UndergroundSpike), nameof(CameraTranspiler)));
        }

        // Inside the room the "terrain" under a vessel is the room floor (CheckKill, altimeter, landing).
        private static void PqsAltitudePostfix(Vessel __instance, ref double __result)
        {
            if (patchesOn && Inside(__instance.GetWorldPos3D()))
            {
                __result = FloorAltitude();
            }
        }

        // Same effect as skipGroundPositioning (which GoOffRails overwrites): no snap to the surface above.
        private static bool CheckGroundCollisionPrefix(Vessel __instance, ref bool __result)
        {
            bool skip = patchesOn && Inside(__instance.GetWorldPos3D());
            Debug.Log(Invariant($"[Subterra.DevTools] CheckGroundCollision {__instance.GetDisplayName()} alt {__instance.altitude:F2} inside {Inside(__instance.GetWorldPos3D())} skip {skip}"));
            if (skip)
            {
                __result = true;
                return false;
            }

            return true;
        }

        // Loading a landed vessel lifts it to max(saved altitude, PQS surface): keep the saved altitude in the room.
        private static bool CorrectedLandedAltitudePrefix(double lat, double lon, double alt, CelestialBody body, ref double __result)
        {
            bool inside = patchesOn && Inside(body.GetWorldSurfacePosition(lat, lon, alt));
            Debug.Log(Invariant($"[Subterra.DevTools] getCorrectedLandedAltitude alt {alt:F2} inside {inside}"));
            if (inside)
            {
                __result = alt;
                return false;
            }

            return true;
        }

        private static void CameraPrefix(FlightCamera __instance)
        {
            if (!patchesOn || !Inside(__instance.GetPivot().position))
            {
                return;
            }

            savedMinHeightAtMin = __instance.minHeightAtMinDist;
            savedMinHeightAtMax = __instance.minHeightAtMaxDist;
            __instance.minHeightAtMinDist = -10000f;
            __instance.minHeightAtMaxDist = -10000f;
            cameraOverridden = true;
        }

        private static void CameraPostfix(FlightCamera __instance)
        {
            if (cameraOverridden)
            {
                __instance.minHeightAtMinDist = savedMinHeightAtMin;
                __instance.minHeightAtMaxDist = savedMinHeightAtMax;
                cameraOverridden = false;
            }
        }

        private static IEnumerable<CodeInstruction> CameraTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo raycast = AccessTools.Method(typeof(Physics), nameof(Physics.Raycast),
                new[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
            MethodInfo replacement = AccessTools.Method(typeof(UndergroundSpike), nameof(TerrainRaycast));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(raycast))
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, replacement) { labels = instruction.labels, blocks = instruction.blocks };
                    continue;
                }

                yield return instruction;
            }

            if (replaced != 1)
            {
                Debug.LogError($"[Subterra.DevTools] camera transpiler replaced {replaced} raycasts (expected 1)");
            }
        }

        // The camera's terrain ray starts `distance` above the pivot; under ground it would hit the
        // surface above and lift the camera out. Inside the room, cast from the pivot instead.
        private static bool TerrainRaycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction query)
        {
            if (patchesOn && FlightCamera.fetch != null)
            {
                Vector3 pivot = origin + direction * (maxDistance * 0.5f);
                if (Inside(pivot))
                {
                    return Physics.Raycast(pivot - direction * 0.5f, direction, out hit, maxDistance, layerMask, query);
                }
            }

            return Physics.Raycast(origin, direction, out hit, maxDistance, layerMask, query);
        }

        // 60 × 60 × 30 m box around the origin, faces wound and normals pointing inwards.
        private static Mesh RoomMesh()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 n in axes)
            {
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                int start = vertices.Count;
                for (int k = 0; k < 4; k++)
                {
                    float su = (k & 1) == 0 ? -1 : 1, sv = (k & 2) == 0 ? -1 : 1;
                    Vector3 p = Vector3.Scale(n * 1f + u * su + v * sv, HalfSize);
                    vertices.Add(p);
                    normals.Add(-n);
                }

                // Inward: Unity's front face is where Cross(b - a, c - a) points, here -n.
                tris.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
