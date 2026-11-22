using System;
using System.Linq;
using KSP.Localization;
using Subterra.Core.Excavation;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The SB-M mole (Lucas: no cryobot part; a drill like the IceMole for the player's own probes). A nose that bores
    /// the way it points, any way (down through the ice to an ocean, sideways, up), melting ice fast and grinding rock
    /// slowly, and screws its probe along the narrow tunnel it leaves: the vessel is held against gravity and pushed
    /// along the bore at the drilling speed. It stops when it breaks into a cave or an ocean. [Claude, revisar]
    /// </summary>
    public sealed class ModuleSubterraMole : PartModule
    {
        /// <summary>Radius (m) of the bore: a little wider than the probes it is meant for.</summary>
        [KSPField] public float radius = 0.6f;

        /// <summary>Metres per second in soft rock (hardness 0); slower in hard rock (DrillMath.Rate).</summary>
        [KSPField] public float baseRate = 0.4f;

        /// <summary>How much faster it goes through ice (the heated nose melts it).</summary>
        [KSPField] public float iceFactor = 4f;

        [KSPField] public float powerUse = 0.8f;
        [KSPField] public float maxRockTemp = 1200f;

        /// <summary>The nose's direction in the part's frame, and how far ahead of the part's centre the nose is.</summary>
        [KSPField] public Vector3 digAxis = Vector3.up;
        [KSPField] public float headOffset = 0.5f;

        [KSPField(isPersistant = true)] public bool boring;
        [KSPField(isPersistant = true)] public string excavationId = "";

        [KSPField(guiActive = true, guiName = "#Subterra_Borer_Length", guiFormat = "F1", guiUnits = " m")]
        public float uiLength;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Layer")]
        public string uiLayer = "-";

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Rate", guiFormat = "F3", guiUnits = " m/s")]
        public float uiRate;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Status")]
        public string uiStatus = "";

        /// <summary>Metres per dig step, and how far ahead of the nose the bore is kept.</summary>
        private const float Step = 1f, Lead = 2f;

        /// <summary>Steering gain (1/s) and the most it accelerates the vessel besides holding its weight (m/s²).</summary>
        private const double Gain = 2.0, MaxAccel = 8.0, SpinDamping = 3.0;

        private double progress;

        private ExcavationSpec Bore => string.IsNullOrEmpty(excavationId) ? null : ExcavationService.Find(excavationId);

        [KSPEvent(guiActive = true, guiName = "#Subterra_Mole_Start")]
        public void StartBoring()
        {
            boring = true;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Mole_Stop")]
        public void StopBoring()
        {
            boring = false;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Mole_New")]
        public void NewBore()
        {
            excavationId = "";
            uiLength = 0;
        }

        [KSPAction("#Subterra_Mole_Start")]
        public void StartAction(KSPActionParam p) => StartBoring();

        [KSPAction("#Subterra_Mole_Stop")]
        public void StopAction(KSPActionParam p) => StopBoring();

        public override void OnStart(StartState state) => UpdateEvents();

        public override string GetInfo() => Localizer.Format("#Subterra_Mole_Info", (radius * 2).ToString("F1"), baseRate.ToString("F2"),
            (baseRate * iceFactor).ToString("F1"), powerUse.ToString("F1"));

        private void UpdateEvents()
        {
            Events[nameof(StartBoring)].active = !boring;
            Events[nameof(StopBoring)].active = boring;
        }

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || vessel.packed)
            {
                return;
            }

            try
            {
                Work(TimeWarp.fixedDeltaTime);
            }
            catch (Exception e)
            {
                boring = false;
                UpdateEvents();
                Log.Error($"Mole on {vessel.GetDisplayName()} stopped: {e}");
            }
        }

        private void Work(double dt)
        {
            if (!boring)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_Idle");
                return;
            }

            if (!Settings.Current.Excavation || !BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                uiStatus = Localizer.Format("#Subterra_Borer_Disabled");
                return;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d axisWorld = part.transform.TransformDirection(digAxis.normalized);
            Vector3d noseWorld = part.transform.position + axisWorld * headOffset;
            Vec3d nose = Frames.ToBody(body, noseWorld);
            Vec3d axis = (Frames.ToBody(body, noseWorld + axisWorld) - nose).Normalized;
            Vec3d up = nose.Normalized;

            // Broke through into an ocean, or into a cave the bore did not start in: done, the probe swims or drives on.
            ExcavationSpec bore = Bore;
            if (bore != null && bore.Body != body.bodyName)
            {
                bore = null;
            }

            Vec3d ahead = nose + axis * (radius + 0.5);
            if (Underground.OceanAt(body, Frames.ToWorld(body, ahead)) != null || (bore != null && bore.JoinedTo != null))
            {
                boring = false;
                UpdateEvents();
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Mole_Through"), 6f, ScreenMessageStyle.UPPER_CENTER);
                Log.Info($"Mole on {vessel.GetDisplayName()} broke through at {nose.Length - body.Radius:F0} m altitude after {uiLength:F0} m");
                return;
            }

            BodyFrame.ToLatLon(up, out double lat, out double lon);
            double depth = Math.Max(0, KspSampler.SurfaceRadius(body, lat, lon) - nose.Length);
            string biome = KspSampler.Biome(body, lat, lon);
            LayerSpec layer = runtime.Strata.LayerAt(depth, biome);
            bool ice = layer != null && (layer.Density < 1.3 || layer.Name.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0);
            double rate = DrillMath.Rate(baseRate, layer?.Hardness ?? 0.5, runtime.Geotherm?.Temperature(depth, biome), CheatOptions.IgnoreMaxTemperature ? 0 : maxRockTemp)
                * (ice ? iceFactor : 1) * Settings.Current.DigSpeed;
            uiRate = (float)rate;
            uiLayer = layer == null ? "-" : string.IsNullOrEmpty(layer.DisplayName) ? layer.Name : Localizer.Format(layer.DisplayName);
            if (rate <= 0)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_TooHard");
                Hold(body, axisWorld, 0);
                return;
            }

            double need = powerUse * dt;
            if (need > 0 && part.RequestResource("ElectricCharge", need) < need * 0.99)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_NoPower");
                Hold(body, axisWorld, 0);
                return;
            }

            // The bore's axis point under the nose; its floor centre (what an excavation stores) is a radius below it.
            Vec3d face = bore == null ? nose : bore.ToBody(bore.Points[bore.Points.Count - 1]) + up * radius;
            double gap = Vec3d.Dot(face - nose, axis);
            if (gap < Lead)
            {
                progress += rate * dt;
                if (progress >= Step)
                {
                    progress -= Step;
                    if (bore == null)
                    {
                        if (ExcavationService.All.Where(s => s.Body == body.bodyName).Sum(s => s.PieceCount) >= Settings.Current.MaxTunnelSegments)
                        {
                            uiStatus = Localizer.Format("#Subterra_Borer_Limit");
                            return;
                        }

                        // Started behind the part, so the probe is inside its own bore from the first step.
                        Vec3d back = Frames.ToBody(body, part.transform.position - axisWorld * (headOffset + 1));
                        bore = ExcavationService.Start(runtime, back - back.Normalized * radius, radius);
                        excavationId = bore.Id;
                        face = back;
                        Log.Info($"Mole bore {bore.Id} started by {vessel.GetDisplayName()}");
                    }

                    Vec3d next = face + axis * Step;
                    ExcavationService.Dig(runtime, bore, next - next.Normalized * radius);
                    uiLength += Step;
                }
            }

            uiStatus = Localizer.Format(ice ? "#Subterra_Mole_Melting" : "#Subterra_Borer_Digging");
            // Screwed along the bore at the drilling speed, never past its face.
            Hold(body, axisWorld, gap > 0.3 ? rate : 0);
        }

        // Holds the vessel's weight and steers its velocity to <paramref name="speed"/> m/s along the nose's axis.
        private void Hold(CelestialBody body, Vector3d axisWorld, double speed)
        {
            Vector3d steer = (axisWorld * speed - vessel.srf_velocity) * Gain;
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

                rb.AddForce(steer - FlightGlobals.getGeeForceAtPosition(rb.worldCenterOfMass), ForceMode.Acceleration);
                rb.AddTorque(-rb.angularVelocity * (float)SpinDamping, ForceMode.Acceleration);
            }
        }
    }
}
