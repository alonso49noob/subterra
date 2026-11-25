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
        [KSPField] public float maxSpeed = 30f;

        /// <summary>Electric charge per second while the motor runs (holding needs none: a brake).</summary>
        [KSPField] public float powerUse = 0.4f;

        /// <summary>Cable length (m); 0 = as long as the hole.</summary>
        [KSPField] public float maxCable = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Winch_Speed", guiUnits = " m/s")]
        [UI_FloatRange(minValue = 0.5f, maxValue = 30f, stepIncrement = 0.5f)]
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
        private const double Gain = 2.0, MaxAccel = 15.0, AxisGain = 0.8, MaxAxisSpeed = 3.0, SpinDamping = 3.0;

        private LineRenderer cable;
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

        [KSPEvent(guiActive = true, guiName = "#Subterra_Winch_Hook")]
        public void Hook()
        {
            CelestialBody body = vessel.mainBody;
            // The vessel's own position (CoMD lags behind on a vessel just moved on rails).
            Vec3d at = Frames.ToBody(body, vessel.GetWorldPos3D());
            BodyFrame.ToLatLon(at, out anchorLat, out anchorLon);
            anchorRadius = at.Length;

            // Hooked by a blast shaft's mouth: the cable hangs on the shaft's axis (the vessel is drawn over it before it
            // goes down), not wherever the vessel stood by the rim.
            foreach (Core.Excavation.ExcavationSpec shaft in ExcavationService.All)
            {
                if (shaft.Shape != Core.Excavation.ExcavationShape.Shaft || shaft.Body != body.bodyName || shaft.Points.Count < 2)
                {
                    continue;
                }

                Vec3d top = shaft.ToBody(shaft.Points[shaft.Points.Count - 1]);
                Vec3d axis = top.Normalized;
                Vec3d off = at - axis * Vec3d.Dot(at, axis);
                if (off.Length < shaft.Radius + 6 && Math.Abs(at.Length - top.Length) < 60)
                {
                    BodyFrame.ToLatLon(axis, out anchorLat, out anchorLon);
                    break;
                }
            }
            anchorBody = body.bodyName;
            hooked = true;
            motion = 0;
            UpdateEvents();
            ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Winch_Hooked"), 4f);
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
                double floor = Clearance(up);
                double room = floor - StopGap;
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
                    vertical = -Math.Min(speed, Math.Max(0.3, room * SlowPerSecond));
                    uiStatus = Localizer.Format("#Subterra_Winch_Lowering");
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
                    vertical = Math.Min(speed, Math.Max(0.3, paidOut * SlowPerSecond));
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

            Vector3d target = up * vertical;
            if (off > 0.05)
            {
                target -= offAxis / off * Math.Min(MaxAxisSpeed, off * AxisGain);
            }

            Vector3d steer = (target - vessel.srf_velocity) * Gain;
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
        private double Clearance(Vector3d up)
        {
            Vector3 down = -(Vector3)up;
            Vector3 origin = vessel.CoMD;
            double lowest = 0;
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
                if (cable != null)
                {
                    cable.enabled = false;
                }

                return;
            }

            if (cable == null)
            {
                var go = new GameObject("SubterraWinchCable");
                cable = go.AddComponent<LineRenderer>();
                Shader shader = Shader.Find("KSP/Alpha/Unlit Transparent") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                cable.material = new Material(shader) { color = new Color(0.15f, 0.15f, 0.15f, 1f) };
                cable.startWidth = cable.endWidth = 0.06f;
                cable.positionCount = 2;
                cable.useWorldSpace = true;
                cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cable.receiveShadows = false;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d top = Frames.ToWorld(body, BodyFrame.Direction(anchorLat, anchorLon) * anchorRadius);
            cable.SetPosition(0, top);
            cable.SetPosition(1, part.transform.position);
            cable.enabled = true;
        }

        private void OnDestroy()
        {
            GameEvents.onCollision.Remove(OnCollision);
            GameEvents.onPartDie.Remove(OnPartDie);
            if (cable != null)
            {
                Destroy(cable.material);
                Destroy(cable.gameObject);
            }
        }
    }
}

