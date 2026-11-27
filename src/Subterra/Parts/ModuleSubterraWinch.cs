using System;
using KSP.Localization;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The SB-W winch (Lucas: a safe way down the blast shafts, an elevator or a cable that holds the craft, instead of
    /// falling to the bottom or into the walls). Hooked at the mouth of a hole, it holds the whole vessel on a cable:
    /// lowers it or hauls it up at the chosen speed, keeps it on the hole's axis (off the walls) and its spin damped,
    /// slows it before the floor and stops it a few metres over it. No geometry: the cable is drawn, and its pull is a
    /// force on every part (gravity held, velocity steered). [Claude, revisar]
    /// </summary>
    public sealed class ModuleSubterraWinch : PartModule
    {
        /// <summary>Fastest the cable runs (m/s).</summary>
        [KSPField] public float maxSpeed = 500f;

        /// <summary>Electric charge per second while the motor runs (holding needs none: a brake).</summary>
        [KSPField] public float powerUse = 0.4f;

        /// <summary>Cable length (m); 0 = as long as the hole.</summary>
        [KSPField] public float maxCable = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Winch_Speed", guiUnits = " m/s")]
        [UI_FloatRange(minValue = 0.5f, maxValue = 500f, stepIncrement = 0.5f)]
        public float speed = 5f;

        [KSPField(isPersistant = true)] public bool hooked;

        /// <summary>0 hold, 1 lowering, 2 hauling up.</summary>
        [KSPField(isPersistant = true)] public int motion;

        [KSPField(isPersistant = true)] public double anchorLat;
        [KSPField(isPersistant = true)] public double anchorLon;

        /// <summary>Distance (m) from the body's centre of the cable's top (where it was hooked).</summary>
        [KSPField(isPersistant = true)] public double anchorRadius;

        [KSPField(isPersistant = true)] public string anchorBody = "";

        [KSPField(guiActive = true, guiName = "#Subterra_Winch_Cable", guiFormat = "F0", guiUnits = " m")]
        public float uiCable;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Status")]
        public string uiStatus = "";

        /// <summary>Metres over the floor where the descent stops, and the stretch over which it slows down.</summary>
        private const float StopGap = 3f, SlowPerSecond = 0.5f;

        /// <summary>How hard the cable steers the velocity (1/s), the most it accelerates the vessel besides holding its
        /// weight (m/s²), and the pull back to the axis (m/s per metre off it, and its limit).</summary>
        private const double Gain = 2.0, MaxAccel = 40.0, AxisGain = 0.8, MaxAxisSpeed = 3.0, SpinDamping = 3.0;

        /// <summary>Deceleration (m/s²) the safety brake plans with, and the integral gain (1/s²) of the speed control.</summary>
        private const double BrakeAccel = 6.0, IntegralGain = 0.6;

        private Vector3d integral;
        private GameObject cable;
        private GameObject[] tripod;
        private Material cableMaterial, tripodMaterial;
        private float nextTrace;

        // A part of the hanging vessel destroyed: where it was (diagnostics: the top parts broke as it started down).
        private void OnPartDie(Part p)
        {
            if (!hooked || p == null || p.vessel != vessel || vessel.mainBody == null)
            {
                return;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d fromBody = p.transform.position - body.position;
            Vector3d axis = Frames.DirectionToWorld(body, BodyFrame.Direction(anchorLat, anchorLon).ToVec3());
            Vector3d off = fromBody - axis * Vector3d.Dot(fromBody, axis);
            Log.Info($"Winch on {vessel.GetDisplayName()}: {p.partInfo.title} destroyed {off.magnitude:F2} m off the axis, {fromBody.magnitude - anchorRadius:F1} m from the hook height, temperature {p.temperature:F0}/{p.maxTemp:F0} K, speed {vessel.srfSpeed:F1} m/s, vessel g {vessel.geeForce:F1}");
        }

        // What the hanging vessel hits (diagnostics: parts broke as it started down a shaft).
        private void OnCollision(EventReport report)
        {
            if (hooked && report?.origin != null && report.origin.vessel == vessel)
            {
                Log.Info($"Winch on {vessel.GetDisplayName()}: {report.origin.partInfo.title} hit {report.other} at {vessel.srfSpeed:F1} m/s");
            }
        }

        /// <summary>Distance (m) from the body's centre of the ground the anchor's tripod stands on.</summary>
        [KSPField(isPersistant = true)] public double anchorGround;

        /// <summary>Radius (m) of the tripod's footprint round the hole's axis.</summary>
        [KSPField(isPersistant = true)] public float anchorSpread = 1.5f;

        /// <summary>Most metres over the ground (or a cave's floor) the craft may stand to hook: the cable needs something to
        /// be anchored to (Lucas: hooked in mid air the winch held the craft floating, an exploit).</summary>
        private const double MaxHookHeight = 6;

        [KSPEvent(guiActive = true, guiName = "#Subterra_Winch_Hook")]
        public void Hook()
        {
            CelestialBody body = vessel.mainBody;
            Vector3d upWorld = (vessel.GetWorldPos3D() - body.position).normalized;
            double clearance = Clearance(upWorld, out double lowest);
            if (!vessel.Landed && !vessel.Splashed && clearance > MaxHookHeight)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Winch_NoGround"), 4f);
                return;
            }

            // The vessel's own position (CoMD lags behind on a vessel just moved on rails).
            Vec3d at = Frames.ToBody(body, vessel.GetWorldPos3D());
            BodyFrame.ToLatLon(at, out anchorLat, out anchorLon);
            anchorGround = at.Length - lowest - Math.Max(0, Math.Min(clearance, MaxHookHeight));

            // Hooked by a blast shaft's mouth (the nearest): the cable hangs on the shaft's axis (the vessel is drawn over it
            // before it goes down), not wherever the vessel stood by the rim; the tripod straddles the hole, its feet past
            // the rounded lip (Lucas: the tripod did not fit the hole). With no hole, it straddles the craft.
            double best = double.MaxValue, holeRadius = 0;
            foreach (Core.Excavation.ExcavationSpec shaft in ExcavationService.All)
            {
                if (shaft.Shape != Core.Excavation.ExcavationShape.Shaft || shaft.Body != body.bodyName || shaft.Points.Count < 2)
                {
                    continue;
                }

                Vec3d top = shaft.ToBody(shaft.Points[shaft.Points.Count - 1]);
                Vec3d axis = top.Normalized;
                Vec3d off = at - axis * Vec3d.Dot(at, axis);
                if (off.Length < shaft.Radius + 6 && Math.Abs(at.Length - top.Length) < 60 && off.Length < best)
                {
                    best = off.Length;
                    BodyFrame.ToLatLon(axis, out anchorLat, out anchorLon);
                    holeRadius = shaft.Radius;
                }
            }

            anchorSpread = holeRadius > 0
                ? (float)(holeRadius + Math.Min(4.0, 0.6 * holeRadius) + 1.2)
                : (float)Math.Max(1.6, HalfWidth(upWorld) + 1.0);

            // The pulley over the craft (it hangs under it) and at least as high as the tripod is wide, so the legs stand
            // at a tripod's angle.
            double partHeight = Vector3d.Dot((Vector3d)part.transform.position - body.position, upWorld) - anchorGround;
            anchorRadius = anchorGround + Math.Max(1.4 * anchorSpread, partHeight + 1.5);
            feetKey = null;
            anchorBody = body.bodyName;
            hooked = true;
            motion = 0;
            nextBottom = 0;
            UpdateEvents();
            ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Winch_Hooked"), 4f);
        }

        // The deepest bottom (distance from the body's centre) of the dug shafts on the cable's axis, 0 if none: the brake
        // stops the craft over it even before the floor's colliders are built (Lucas: a safety brake at the bottom).
        private double shaftBottom;
        private float nextBottom;

        private double ShaftBottom(CelestialBody body)
        {
            if (Time.time < nextBottom)
            {
                return shaftBottom;
            }

            nextBottom = Time.time + 2f;
            shaftBottom = 0;
            Vec3d axis = BodyFrame.Direction(anchorLat, anchorLon);
            foreach (Core.Excavation.ExcavationSpec shaft in ExcavationService.All)
            {
                if (shaft.Shape != Core.Excavation.ExcavationShape.Shaft || shaft.Body != body.bodyName || shaft.Points.Count < 2)
                {
                    continue;
                }

                Vec3d bottom = shaft.ToBody(shaft.Points[0]);
                Vec3d off = bottom - axis * Vec3d.Dot(bottom, axis);
                if (off.Length < shaft.Radius + 2 && Vec3d.Dot(bottom, axis) > 0 && (shaftBottom == 0 || bottom.Length < shaftBottom))
                {
                    shaftBottom = bottom.Length;
                }
            }

            return shaftBottom;
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Winch_Lower")]
        public void Lower() => SetMotion(1);

        [KSPEvent(guiActive = true, guiName = "#Subterra_Winch_Stop")]
        public void Stop() => SetMotion(0);

        [KSPEvent(guiActive = true, guiName = "#Subterra_Winch_Raise")]
        public void Raise() => SetMotion(2);

        [KSPEvent(guiActive = true, guiName = "#Subterra_Winch_Release")]
        public void Release()
        {
            hooked = false;
            motion = 0;
            UpdateEvents();
        }

        [KSPAction("#Subterra_Winch_Lower")]
        public void LowerAction(KSPActionParam p) => SetMotion(1);

        [KSPAction("#Subterra_Winch_Stop")]
        public void StopAction(KSPActionParam p) => SetMotion(0);

        [KSPAction("#Subterra_Winch_Raise")]
        public void RaiseAction(KSPActionParam p) => SetMotion(2);

        private void SetMotion(int m)
        {
            if (!hooked)
            {
                Hook();
                if (!hooked)
                {
                    return;
                }
            }

            motion = m;
            UpdateEvents();
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                GameEvents.onCollision.Add(OnCollision);
                GameEvents.onPartDie.Add(OnPartDie);
            }

            if (Fields[nameof(speed)].uiControlEditor is UI_FloatRange editor)
            {
                editor.maxValue = maxSpeed;
            }

            if (Fields[nameof(speed)].uiControlFlight is UI_FloatRange flight)
            {
                flight.maxValue = maxSpeed;
            }

            UpdateEvents();
        }

        public override string GetInfo() => Localizer.Format("#Subterra_Winch_Info", maxSpeed.ToString("F0"), powerUse.ToString("F1"),
            maxCable > 0 ? maxCable.ToString("F0") + " m" : "∞");

        private void UpdateEvents()
        {
            Events[nameof(Hook)].active = !hooked;
            Events[nameof(Release)].active = hooked;
            Events[nameof(Lower)].active = motion != 1;
            Events[nameof(Raise)].active = hooked && motion != 2;
            Events[nameof(Stop)].active = hooked && motion != 0;
        }

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || vessel.packed || !hooked)
            {
                return;
            }

            try
            {
                Hold(TimeWarp.fixedDeltaTime);
            }
            catch (Exception e)
            {
                hooked = false;
                Log.Error($"Winch on {vessel.GetDisplayName()} let go: {e}");
            }
        }

        private void Hold(double dt)
        {
            CelestialBody body = vessel.mainBody;
            if (body.bodyName != anchorBody)
            {
                Release();
                return;
            }

            Vector3d centre = vessel.CoMD;
            Vector3d fromBody = centre - body.position;
            double radius = fromBody.magnitude;
            Vector3d up = fromBody / radius;
            Vector3d axis = Frames.DirectionToWorld(body, BodyFrame.Direction(anchorLat, anchorLon).ToVec3());
            Vector3d offAxis = fromBody - axis * Vector3d.Dot(fromBody, axis);
            double paidOut = anchorRadius - radius;
            uiCable = (float)Math.Max(0, paidOut);

            // The motor needs power to run; without it the brake only holds.
            int m = motion;
            if (m != 0 && powerUse > 0 && part.RequestResource("ElectricCharge", powerUse * dt) < powerUse * dt * 0.99)
            {
                m = 0;
                uiStatus = Localizer.Format("#Subterra_Drill_NoPower");
            }

            double vertical = 0;
            if (m == 1 && offAxis.magnitude > 1.0)
            {
                // Over the hole's axis first (hooked by the rim): the floor under the vessel would be the rim itself.
                uiStatus = Localizer.Format("#Subterra_Winch_Centring");
            }
            else if (m == 1)
            {
                double floor = Clearance(up, out double lowest);
                double room = floor - StopGap;
                // The dug shaft's bottom, known before its floor is built and in reach of the ray.
                double bottom = ShaftBottom(body);
                if (bottom > 0)
                {
                    room = Math.Min(room, radius - lowest - bottom - StopGap);
                }

                if (maxCable > 0)
                {
                    room = Math.Min(room, maxCable - paidOut);
                }

                if (room <= 0)
                {
                    motion = 0;
                    UpdateEvents();
                    ScreenMessages.PostScreenMessage(Localizer.Format(maxCable > 0 && paidOut >= maxCable ? "#Subterra_Winch_CableEnd" : "#Subterra_Winch_Bottom"), 4f);
                    Log.Info($"Winch on {vessel.GetDisplayName()} stopped {floor:F1} m over the floor, {paidOut:F0} m of cable out");
                }
                else
                {
                    // The safety brake: no faster than what stops at BrakeAccel before the bottom (and slow for the last metres).
                    vertical = -Math.Min(speed, Math.Max(0.3, Math.Min(Math.Sqrt(2 * BrakeAccel * room), room * SlowPerSecond + 2)));
                    uiStatus = Localizer.Format(-vertical < speed - 0.5 && room < 2 * speed * speed / BrakeAccel ? "#Subterra_Winch_Braking" : "#Subterra_Winch_Lowering");
                }
            }
            else if (m == 2)
            {
                if (paidOut <= 0)
                {
                    motion = 0;
                    UpdateEvents();
                    ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Winch_Top"), 4f);
                }
                else
                {
                    vertical = Math.Min(speed, Math.Max(0.3, Math.Min(Math.Sqrt(2 * BrakeAccel * paidOut), paidOut * SlowPerSecond + 2)));
                    uiStatus = Localizer.Format("#Subterra_Winch_Raising");
                }
            }

            if (motion == 0 && m == motion)
            {
                uiStatus = Localizer.Format("#Subterra_Winch_Holding");
            }

            double off = offAxis.magnitude;
            if (motion != 0 && Time.time >= nextTrace)
            {
                nextTrace = Time.time + 5f;
                Log.Info($"Winch on {vessel.GetDisplayName()}: {paidOut:F1} m of cable out, {off:F2} m off the axis, vertical {Vector3d.Dot(vessel.srf_velocity, up):F1} m/s, target {vertical:F1} m/s");
            }

            // Over the pulley the cable is slack: it only pulls (Lucas: the winch held craft floating in the air).
            if (paidOut < -0.5)
            {
                uiStatus = Localizer.Format("#Subterra_Winch_Slack");
                integral = Vector3d.zero;
                return;
            }

            Vector3d target = up * vertical;
            if (off > 0.05)
            {
                target -= offAxis / off * Math.Min(MaxAxisSpeed, off * AxisGain);
            }

            // Proportional and integral (Lucas: the speed set was not the speed it went: the air's drag deep down, which the
            // integral makes up for).
            Vector3d error = target - vessel.srf_velocity;
            integral += error * (IntegralGain * dt);
            if (integral.magnitude > MaxAccel)
            {
                integral = integral.normalized * MaxAccel;
            }

            Vector3d steer = error * Gain + integral;
            if (steer.magnitude > MaxAccel)
            {
                steer = steer.normalized * MaxAccel;
            }

            foreach (Part p in vessel.parts)
            {
                Rigidbody rb = p.Rigidbody;
                if (rb == null || p.physicalSignificance != Part.PhysicalSignificance.FULL)
                {
                    continue;
                }

                // Its weight held, its velocity steered, its spin damped.
                Vector3d weight = FlightGlobals.getGeeForceAtPosition(rb.worldCenterOfMass);
                rb.AddForce(steer - weight, ForceMode.Acceleration);
                rb.AddTorque(-rb.angularVelocity * (float)SpinDamping, ForceMode.Acceleration);
            }
        }

        // Metres from the vessel's lowest point to whatever is under it (cave floor, terrain), 1000 if nothing near.
        private double Clearance(Vector3d up, out double lowest)
        {
            Vector3 down = -(Vector3)up;
            Vector3 origin = vessel.CoMD;
            lowest = 0;
            foreach (Part p in vessel.parts)
            {
                lowest = Math.Max(lowest, Vector3.Dot(p.transform.position - origin, down));
            }

            double best = 1000;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, down, 1000f, (1 << ChunkObject.Layer) | 1, QueryTriggerInteraction.Ignore))
            {
                Part hitPart = hit.collider.GetComponentInParent<Part>();
                if (hitPart != null && hitPart.vessel == vessel)
                {
                    continue;
                }

                best = Math.Min(best, hit.distance - lowest);
            }

            return best;
        }

        private void LateUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !hooked || vessel.mainBody.bodyName != anchorBody)
            {
                ShowRig(false);
                return;
            }

            if (cable == null)
            {
                BuildRig();
            }

            // The cable a lit, braided cylinder (Lucas: a cable texture; the flat line drawn before vanished looking up at it).
            CelestialBody body = vessel.mainBody;
            Vec3d dir = BodyFrame.Direction(anchorLat, anchorLon);
            Vector3 top = Frames.ToWorld(body, dir * anchorRadius);
            // The anchor (Lucas: a base for it; then: the tripod looked ugly and did not fit the hole): a steel tripod
            // straddling the hole, each foot on a pad on the ground where it really is (sampled once per hook), braced a
            // third of the way up, a head block at the top and the pulley the cable runs over under it.
            Vector3 up = Frames.DirectionToWorld(body, dir.ToVec3());
            Vector3 east = Vector3.Cross(up, Vector3.Cross(Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right, up)).normalized;
            Vector3 north = Vector3.Cross(up, east);
            // The cable leaves the pulley's rim on the axis, level with its axle.
            Stretch(cable, top + up * 0.3f, part.transform.position, CableRadius, true);
            Feet(body, dir);
            Vector3 head = top + up * 0.75f;
            var feet = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                feet[i] = Frames.ToWorld(body, feetBody[i]);
                Vector3 corner = head + (feet[i] - head).normalized * 0.25f;
                Stretch(tripod[i], feet[i], corner, LegRadius, false);
                // The pad, flat on the ground.
                tripod[3 + i].transform.position = feet[i] + up * 0.03f;
                tripod[3 + i].transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
                tripod[3 + i].transform.localScale = new Vector3(0.6f, 0.03f, 0.6f);
            }

            // The braces between the legs, a third of the way up.
            for (int i = 0; i < 3; i++)
            {
                Vector3 a = Vector3.Lerp(feet[i], head, 0.33f), b = Vector3.Lerp(feet[(i + 1) % 3], head, 0.33f);
                Stretch(tripod[6 + i], a, b, LegRadius * 0.55f, false);
            }

            // The head block (a cube) and the pulley wheel under it, its axle level: the cable leaves its rim on the axis.
            tripod[9].transform.position = head;
            tripod[9].transform.rotation = Quaternion.LookRotation(north, up);
            tripod[9].transform.localScale = new Vector3(0.55f, 0.4f, 0.55f);
            tripod[10].transform.position = top + up * 0.3f + east * 0.3f;
            tripod[10].transform.rotation = Quaternion.FromToRotation(Vector3.up, north);
            tripod[10].transform.localScale = new Vector3(0.6f, 0.06f, 0.6f);
            ShowRig(true);
        }

        private const float CableRadius = 0.035f, LegRadius = 0.07f;

        // The feet in the body frame, on the terrain round the axis (sampled when hooked, and again if the hook moved).
        private readonly Vec3d[] feetBody = new Vec3d[3];
        private string feetKey;

        private void Feet(CelestialBody body, Vec3d dir)
        {
            string key = anchorLat.ToString("F6") + "/" + anchorLon.ToString("F6") + "/" + anchorSpread.ToString("F2");
            if (key == feetKey)
            {
                return;
            }

            feetKey = key;
            Vec3d northB = (new Vec3d(0, 1, 0) - dir * dir.Y);
            northB = northB.LengthSquared < 1e-12 ? new Vec3d(1, 0, 0) : northB.Normalized;
            Vec3d eastB = Vec3d.Cross(northB, dir).Normalized;
            double ground = anchorGround > 0 ? anchorGround : anchorRadius - 2;
            for (int i = 0; i < 3; i++)
            {
                double a = i * 2 * Math.PI / 3 + 0.4;
                Vec3d foot = (dir * ground + (eastB * Math.Cos(a) + northB * Math.Sin(a)) * anchorSpread).Normalized;
                BodyFrame.ToLatLon(foot, out double la, out double lo);
                double r = body.pqsController != null ? KspSampler.SurfaceRadius(body, la, lo) : ground;
                // Underground (hooked on a cave's floor) the terrain is far over it: the floor the craft stood on.
                if (r > ground + 3 || r < ground - 20)
                {
                    r = ground;
                }

                feetBody[i] = foot * r;
            }
        }

        private void BuildRig()
        {
            Shader lit = Shader.Find("KSP/Specular") ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Standard");
            cableMaterial = new Material(Shader.Find("KSP/Diffuse") ?? lit) { color = new Color(0.62f, 0.6f, 0.55f), mainTexture = BraidTexture() };
            // Galvanised steel legs; dark iron pads, braces' ends, head and pulley.
            tripodMaterial = new Material(lit) { color = new Color(0.6f, 0.62f, 0.64f) };
            if (tripodMaterial.HasProperty("_Shininess"))
            {
                tripodMaterial.SetFloat("_Shininess", 0.4f);
            }

            darkMaterial = new Material(lit) { color = new Color(0.2f, 0.2f, 0.22f) };
            cable = Cylinder("SubterraWinchCable", cableMaterial);
            tripod = new GameObject[11];
            for (int i = 0; i < 11; i++)
            {
                bool dark = (i >= 3 && i < 6) || i >= 9;
                tripod[i] = i == 9
                    ? Primitive(PrimitiveType.Cube, "SubterraWinchHead", darkMaterial)
                    : Cylinder("SubterraWinchTripod", dark ? darkMaterial : tripodMaterial);
            }
        }

        private Material darkMaterial;

        private GameObject Cylinder(string name, Material material) => Primitive(PrimitiveType.Cylinder, name, material);

        private GameObject Primitive(PrimitiveType type, string name, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.layer = part.gameObject.layer;
            return go;
        }

        // A Unity cylinder (2 m tall, 1 m wide) from a to b; the cable's braid repeats every 12 cm.
        private void Stretch(GameObject go, Vector3 a, Vector3 b, float radius, bool braid)
        {
            Vector3 along = b - a;
            float length = along.magnitude;
            if (length < 0.01f)
            {
                go.SetActive(false);
                return;
            }

            go.transform.position = a + along * 0.5f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, along / length);
            go.transform.localScale = new Vector3(radius * 2, length * 0.5f, radius * 2);
            if (braid)
            {
                cableMaterial.mainTextureScale = new Vector2(1, length / 0.12f);
            }
        }

        private void ShowRig(bool show)
        {
            if (cable != null && cable.activeSelf != show)
            {
                cable.SetActive(show);
            }

            if (tripod != null)
            {
                foreach (GameObject leg in tripod)
                {
                    if (leg != null && leg.activeSelf != show)
                    {
                        leg.SetActive(show);
                    }
                }
            }
        }

        // Diagonal strands of a twisted wire rope, light and dark.
        private static Texture2D braid;

        private static Texture2D BraidTexture()
        {
            if (braid != null)
            {
                return braid;
            }

            const int w = 32, h = 32;
            braid = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "SubterraWinchBraid", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Four strands round the rope, each a band slanting along it.
                    float s = ((x * 4f / w) + (y / (float)h)) % 1f;
                    float shade = 0.55f + 0.45f * Mathf.Sin(s * Mathf.PI);
                    byte v = (byte)(255 * shade);
                    px[y * w + x] = new Color32(v, v, v, 255);
                }
            }

            braid.SetPixels32(px);
            braid.Apply(true);
            return braid;
        }

        private void OnDestroy()
        {
            GameEvents.onCollision.Remove(OnCollision);
            GameEvents.onPartDie.Remove(OnPartDie);
            if (cable != null)
            {
                Destroy(cable);
            }

            if (tripod != null)
            {
                foreach (GameObject leg in tripod)
                {
                    if (leg != null)
                    {
                        Destroy(leg);
                    }
                }
            }

            if (cableMaterial != null)
            {
                Destroy(cableMaterial);
            }

            if (tripodMaterial != null)
            {
                Destroy(tripodMaterial);
            }

            if (darkMaterial != null)
            {
                Destroy(darkMaterial);
            }
        }

        // The craft's half width across the vertical (its farthest part from the axis through its centre of mass).
        private double HalfWidth(Vector3d up)
        {
            Vector3d centre = vessel.CoMD;
            double most = 0;
            foreach (Part p in vessel.parts)
            {
                Vector3d d = (Vector3d)p.transform.position - centre;
                most = Math.Max(most, (d - up * Vector3d.Dot(d, up)).magnitude);
            }

            return most;
        }
    }
}

