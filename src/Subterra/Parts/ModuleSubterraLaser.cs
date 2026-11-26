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
    /// The SB-L ablation lasers, which replace the tunnel borer (Lucas: an instrument that vaporises the rock in whatever
    /// direction you want, aimed with a window that opens and closes; electricity, and a stronger version that uses
    /// xenon, as excimer lasers do). The beam carves a persistent tunnel (ExcavationService) from its face the way it is
    /// aimed (heading and elevation from the vessel's nose), up to <see cref="reach"/> metres from the emitter; drive on
    /// and keep firing to go further. Continuous beam; a smaller bore goes faster. [Claude, revisar]
    /// Saved crafts with the old SB-T borer keep their part (same name, same mount) and get this module.
    /// </summary>
    public sealed class ModuleSubterraLaser : PartModule
    {
        /// <summary>Metres per second through soft rock with a <see cref="ReferenceRadius"/> m bore (DrillMath.Rate).</summary>
        [KSPField] public float baseRate = 0.6f;

        /// <summary>Electric charge per second while firing.</summary>
        [KSPField] public float powerUse = 20f;

        /// <summary>Xenon per second while firing (the excimer version; 0 = none). Without it the beam does not fire.</summary>
        [KSPField] public float xenonUse = 0f;

        /// <summary>Farthest (m) the beam reaches from the emitter to the tunnel's face.</summary>
        [KSPField] public float reach = 40f;

        [KSPField] public float maxRockTemp = 1500f;
        [KSPField] public float minRadius = 1f;
        [KSPField] public float maxRadius = 5f;

        /// <summary>Beam colour (r, g, b).</summary>
        [KSPField] public Vector3 beamColour = new Vector3(1f, 0.15f, 0.1f);

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Laser_Radius", guiUnits = " m", guiFormat = "F1")]
        [UI_FloatRange(minValue = 1f, maxValue = 5f, stepIncrement = 0.25f)]
        public float radius = 2.5f;

        /// <summary>Aim: degrees right of the vessel's nose, and up (+) or down (−) from the horizontal.</summary>
        [KSPField(isPersistant = true)] public float heading;
        [KSPField(isPersistant = true)] public float elevation = -15f;

        [KSPField(isPersistant = true)] public bool firing;
        [KSPField(isPersistant = true)] public string excavationId = "";

        [KSPField(guiActive = true, guiName = "#Subterra_Borer_Length", guiFormat = "F1", guiUnits = " m")]
        public float uiLength;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Layer")]
        public string uiLayer = "-";

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Rate", guiFormat = "F3", guiUnits = " m/s")]
        public float uiRate;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Status")]
        public string uiStatus = "";

        /// <summary>The bore radius baseRate is given for; a smaller bore goes faster (the same power on less rock).</summary>
        private const float ReferenceRadius = 2.5f;

        /// <summary>Metres per dig step, and where a new tunnel starts ahead of the emitter (past its radius).</summary>
        private const float Step = 1f, StartMargin = 1f;

        private double progress;
        private bool aimOpen;
        private Rect aimRect = new Rect(300, 200, 280, 10);
        private LineRenderer beam;
        private Light glow;

        public ExcavationSpec Tunnel => string.IsNullOrEmpty(excavationId) ? null : ExcavationService.Find(excavationId);

        [KSPEvent(guiActive = true, guiName = "#Subterra_Laser_Fire")]
        public void StartFiring()
        {
            firing = true;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Laser_Cease")]
        public void StopFiring()
        {
            firing = false;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Laser_Aim")]
        public void ToggleAim() => aimOpen = !aimOpen;

        [KSPEvent(guiActive = true, guiName = "#Subterra_Borer_New")]
        public void NewTunnel()
        {
            excavationId = "";
            uiLength = 0;
        }

        [KSPAction("#Subterra_Laser_Fire")]
        public void FireAction(KSPActionParam p) => StartFiring();

        [KSPAction("#Subterra_Laser_Cease")]
        public void CeaseAction(KSPActionParam p) => StopFiring();

        [KSPAction("#Subterra_Laser_Toggle")]
        public void ToggleAction(KSPActionParam p)
        {
            firing = !firing;
            UpdateEvents();
        }

        public override void OnStart(StartState state)
        {
            foreach (UI_Control c in new[] { Fields[nameof(radius)].uiControlEditor, Fields[nameof(radius)].uiControlFlight })
            {
                if (c is UI_FloatRange range)
                {
                    range.minValue = minRadius;
                    range.maxValue = maxRadius;
                }
            }

            radius = Mathf.Clamp(radius, minRadius, maxRadius);
            UpdateEvents();
        }

        public override string GetInfo() => Localizer.Format(xenonUse > 0 ? "#Subterra_Laser_InfoXenon" : "#Subterra_Laser_Info",
            baseRate.ToString("F2"), powerUse.ToString("F0"), reach.ToString("F0"), minRadius.ToString("F1"), maxRadius.ToString("F1"), xenonUse.ToString("F3"));

        private void UpdateEvents()
        {
            Events[nameof(StartFiring)].active = !firing;
            Events[nameof(StopFiring)].active = firing;
        }

        /// <summary>The beam's direction in world space: the aim turned from the vessel's nose (levelled).</summary>
        public Vector3d AimDirection()
        {
            Vector3 up = (vessel.CoM - vessel.mainBody.position).normalized;
            Vector3 nose = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, up);
            if (nose.sqrMagnitude < 1e-4f)
            {
                nose = Vector3.ProjectOnPlane(-vessel.ReferenceTransform.forward, up);
            }

            nose.Normalize();
            Vector3 level = Quaternion.AngleAxis(heading, up) * nose;
            Vector3 side = Vector3.Cross(up, level).normalized;
            return Quaternion.AngleAxis(-elevation, side) * level;
        }

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
            {
                return;
            }

            try
            {
                Fire(TimeWarp.fixedDeltaTime);
            }
            catch (Exception e)
            {
                firing = false;
                UpdateEvents();
                Log.Error($"Laser on {vessel.GetDisplayName()} stopped: {e}");
            }
        }

        private Vector3d? beamEnd;

        private void Fire(double dt)
        {
            beamEnd = null;
            if (!firing)
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
            Vector3d dirWorld = AimDirection();
            Vector3d emitterWorld = part.transform.position;
            Vec3d emitter = Frames.ToBody(body, emitterWorld);
            Vec3d dir = (Frames.ToBody(body, emitterWorld + dirWorld) - emitter).Normalized;

            ExcavationSpec tunnel = Tunnel;
            if (tunnel != null && tunnel.Body != body.bodyName)
            {
                tunnel = null;
            }

            // The tunnel's axis at its face (an excavation stores its floor centre, a radius under the axis).
            Vec3d face = default;
            if (tunnel != null)
            {
                Vec3d floor = tunnel.ToBody(tunnel.Points[tunnel.Points.Count - 1]);
                face = floor + floor.Normalized * tunnel.Radius;
                if ((face - emitter).Length > reach)
                {
                    uiStatus = Localizer.Format("#Subterra_Laser_OutOfReach");
                    return;
                }
            }

            Vec3d target = tunnel == null ? emitter + dir * (radius + StartMargin) : face;
            BodyFrame.ToLatLon(target.Normalized, out double lat, out double lon);
            double depth = Math.Max(0, KspSampler.SurfaceRadius(body, lat, lon) - target.Length);
            string biome = KspSampler.Biome(body, lat, lon);
            LayerSpec layer = runtime.Strata.LayerAt(depth, biome);
            double limit = DrillMath.VoidLimit(Settings.Current.VoidStrengthMPa, layer?.Density ?? 2.7, body.gravParameter / (body.Radius * body.Radius));
            if (depth > limit && !ModuleSubterraCoreDrill.FreeBlasting)
            {
                uiStatus = Localizer.Format("#Subterra_Borer_TooDeep", limit.ToString("F0"));
                return;
            }

            if (ExcavationService.All.Where(s => s.Body == body.bodyName).Sum(s => s.PieceCount) >= Settings.Current.MaxTunnelSegments)
            {
                uiStatus = Localizer.Format("#Subterra_Borer_Limit");
                return;
            }

            double need = powerUse * dt;
            if (need > 0 && part.RequestResource("ElectricCharge", need) < need * 0.99)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_NoPower");
                return;
            }

            if (xenonUse > 0 && part.RequestResource("XenonGas", xenonUse * dt) < xenonUse * dt * 0.99)
            {
                uiStatus = Localizer.Format("#Subterra_Laser_NoXenon");
                return;
            }

            float r = tunnel?.Radius ?? radius;
            double rate = DrillMath.Rate(baseRate, layer?.Hardness ?? 0.5, runtime.Geotherm?.Temperature(depth, biome), CheatOptions.IgnoreMaxTemperature ? 0 : maxRockTemp)
                * (ReferenceRadius / r) * (ReferenceRadius / r) * Settings.Current.DigSpeed;
            uiRate = (float)rate;
            uiLayer = layer == null ? "-" : string.IsNullOrEmpty(layer.DisplayName) ? layer.Name : Localizer.Format(layer.DisplayName);
            beamEnd = Frames.ToWorld(body, target);
            if (rate <= 0)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_TooHard");
                return;
            }

            uiStatus = Localizer.Format("#Subterra_Laser_Firing");
            progress += rate * dt;
            if (progress < Step)
            {
                return;
            }

            progress -= Step;
            if (tunnel == null)
            {
                // From the surface (not from a cave floor): the tunnel starts on the ground, its mouth cut in front.
                Vec3d start = target;
                double ground = KspSampler.SurfaceRadius(body, lat, lon);
                if (Math.Abs(ground - start.Length) < radius + 2)
                {
                    start = start.Normalized * (ground - 0.1 + radius);
                }

                tunnel = ExcavationService.Start(runtime, start - start.Normalized * radius, radius);
                excavationId = tunnel.Id;
                face = start;
                Log.Info($"Tunnel {tunnel.Id} started by {vessel.GetDisplayName()} (laser, radius {radius:F1} m)");
            }

            Vec3d next = face + dir * Step;
            ExcavationService.Dig(runtime, tunnel, next - next.Normalized * tunnel.Radius);
            uiLength += Step;
        }

        private void LateUpdate()
        {
            bool show = HighLogic.LoadedSceneIsFlight && vessel != null && firing && beamEnd.HasValue;
            if (!show)
            {
                if (beam != null)
                {
                    beam.enabled = false;
                    glow.enabled = false;
                }

                return;
            }

            if (beam == null)
            {
                var go = new GameObject("SubterraLaserBeam");
                beam = go.AddComponent<LineRenderer>();
                Shader shader = Shader.Find("KSP/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
                var colour = new Color(beamColour.x, beamColour.y, beamColour.z, 1f);
                beam.material = new Material(shader) { color = colour };
                beam.startColor = beam.endColor = colour;
                beam.startWidth = 0.12f;
                beam.endWidth = 0.06f;
                beam.positionCount = 2;
                beam.useWorldSpace = true;
                beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                beam.receiveShadows = false;
                glow = new GameObject("SubterraLaserGlow").AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = colour;
                glow.range = 12f;
                glow.intensity = 3f;
                // Per pixel: as an "auto" light down the hole it went into the ambient term of the ground above, which lit up a
                // whole terrain quad at night (Lucas: a lighter rectangle round the laser's hole by the runway).
                glow.renderMode = LightRenderMode.ForcePixel;
                glow.shadows = LightShadows.None;
            }

            beam.SetPosition(0, part.transform.position);
            beam.SetPosition(1, beamEnd.Value);
            glow.transform.position = beamEnd.Value;
            beam.enabled = true;
            glow.enabled = true;
        }

        private void OnGUI()
        {
            if (!aimOpen || !HighLogic.LoadedSceneIsFlight || vessel != FlightGlobals.ActiveVessel)
            {
                return;
            }

            aimRect = GUILayout.Window(GetInstanceID(), aimRect, AimWindow, Localizer.Format("#Subterra_Laser_AimTitle"));
        }

        private void AimWindow(int id)
        {
            GUILayout.Label(Localizer.Format("#Subterra_Laser_Heading") + ": " + heading.ToString("F0") + "°");
            heading = Mathf.Round(GUILayout.HorizontalSlider(heading, -180f, 180f));
            GUILayout.Label(Localizer.Format("#Subterra_Laser_Elevation") + ": " + elevation.ToString("F0") + "°");
            elevation = Mathf.Round(GUILayout.HorizontalSlider(elevation, -90f, 90f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↓"))
            {
                elevation = -90f;
            }

            if (GUILayout.Button("—"))
            {
                elevation = 0f;
            }

            if (GUILayout.Button("↑"))
            {
                elevation = 90f;
            }

            GUILayout.EndHorizontal();
            if (GUILayout.Button(Localizer.Format(firing ? "#Subterra_Laser_Cease" : "#Subterra_Laser_Fire")))
            {
                firing = !firing;
                UpdateEvents();
            }

            GUILayout.Label(uiStatus);
            if (GUILayout.Button(Localizer.Format("#Subterra_Laser_Close")))
            {
                aimOpen = false;
            }

            GUI.DragWindow();
        }

        private void OnDestroy()
        {
            // Unity objects already destroyed with the scene compare equal to null.
            if (beam != null)
            {
                Destroy(beam.material);
                Destroy(beam.gameObject);
            }

            if (glow != null)
            {
                Destroy(glow.gameObject);
            }
        }
    }
}
