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
    /// The SB-T tunnel borer (sub-project 2): digs a round tunnel ahead of its head, one metre at a time, while
    /// the vessel is (almost) still and the face is within reach; the vessel drives into the new tunnel to go on.
    /// The tunnel is real geometry (ExcavationService), saved with the game, and joins the cave it reaches.
    /// Part of the rock comes out as Ore.
    /// </summary>
    public sealed class ModuleSubterraTunnelBorer : PartModule, IAnimatedModule
    {
        // IAnimatedModule: the part's ModuleAnimationGroup spins the head while ModuleIsActive and blocks the
        // work while it is retracted.
        private bool working;
        private bool retracted;

        public void EnableModule() => retracted = false;

        public void DisableModule() => retracted = true;

        public bool ModuleIsActive() => working;

        public bool IsSituationValid() => true;

        /// <summary>Metres dug per step (one call to ExcavationService.Dig).</summary>
        private const double Step = 1;

        /// <summary>The face stops advancing when it is this far beyond where a new tunnel would start (drive forward to dig on).</summary>
        private const double Reach = 3;

        /// <summary>Metres beyond the tunnel's radius, ahead of the head, where a new tunnel starts.</summary>
        private const double StartMargin = 1.5;

        /// <summary>
        /// Least descent (degrees) while the roof is not yet under the terrain: a portal goes down as a ramp and is soon
        /// covered, instead of a trench running along the surface (decidido por Claude, revisar).
        /// </summary>
        private const double PortalSlope = 15;

        [KSPField] public float radius = 2.5f;
        [KSPField] public float baseRate = 0.15f;
        [KSPField] public float powerUse = 15f;
        [KSPField] public float maxRockTemp = 700f;
        [KSPField] public float maxSlope = 30f;

        /// <summary>Fastest the vessel may move (m/s) while it digs. [Claude, revisar]</summary>
        [KSPField] public float maxSpeed = 3f;
        [KSPField] public float spoilOre = 0.2f;

        /// <summary>Digging direction in the part's own axes (where its head points).</summary>
        [KSPField] public Vector3 digAxis = Vector3.up;

        /// <summary>Distance (m) from the part's origin to the cutting head, along <see cref="digAxis"/>.</summary>
        [KSPField] public float headOffset = 1.5f;

        [KSPField(isPersistant = true)] public bool digging;
        [KSPField(isPersistant = true)] public string excavationId = "";

        [KSPField(guiActive = true, guiName = "#Subterra_Borer_Length", guiFormat = "F1", guiUnits = " m")]
        public float uiLength;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Layer")]
        public string uiLayer = "-";

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Rate", guiFormat = "F3", guiUnits = " m/s")]
        public float uiRate;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Status")]
        public string uiStatus = "";

        private double progress;

        /// <summary>The tunnel this borer is digging, or null.</summary>
        public ExcavationSpec Tunnel => string.IsNullOrEmpty(excavationId) ? null : ExcavationService.Find(excavationId);

        [KSPEvent(guiActive = true, guiName = "#Subterra_Borer_Start")]
        public void StartDigging()
        {
            digging = true;
            ModuleAnimationGroup group = part.FindModuleImplementing<ModuleAnimationGroup>();
            if (group != null && !group.isDeployed)
            {
                group.DeployModule();
            }

            SetArmSolid(false);

            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Borer_Stop")]
        public void StopDigging()
        {
            digging = false;
            SetArmSolid(true);
            UpdateEvents();
        }

        /// <summary>Starts a new tunnel at the head on the next dig, instead of going on with the current one.</summary>
        [KSPEvent(guiActive = true, guiName = "#Subterra_Borer_New")]
        public void NewTunnel()
        {
            excavationId = "";
            progress = 0;
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                SetArmSolid(!digging);
            }

            UpdateEvents();
        }

        // The deployed arm reaches into the ground in front of the vessel (the stock model is made to drill under
        // a lander): solid, it would jack a rover up and tip it over. Its colliders are triggers while it digs.
        private void SetArmSolid(bool solid)
        {
            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                collider.isTrigger = !solid;
            }
        }

        public override string GetInfo() =>
            Localizer.Format("#Subterra_Borer_Info", (2 * radius).ToString("F1"), powerUse.ToString("F0"), maxSlope.ToString("F0"));

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
            {
                return;
            }

            try
            {
                Dig(TimeWarp.fixedDeltaTime);
            }
            catch (Exception e)
            {
                digging = false;
                UpdateEvents();
                Log.Error($"Tunnel borer on {vessel.GetDisplayName()} stopped: {e}");
            }
        }

        private void Dig(double dt)
        {
            working = false;
            if (!digging || retracted)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_Idle");
                return;
            }

            if (!Settings.Current.Excavation || !BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                uiStatus = Localizer.Format("#Subterra_Borer_Disabled");
                return;
            }

            // It digs while the vessel creeps on (it slides on the ramp it digs; driving slowly into the face is how it is used).
            if (vessel.srfSpeed > maxSpeed)
            {
                uiStatus = Localizer.Format("#Subterra_Borer_Moving");
                return;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d dirWorld = part.transform.TransformDirection(digAxis.normalized);
            Vector3d head = part.transform.position + dirWorld * headOffset;
            Vec3d headBody = Frames.ToBody(body, head);
            Vec3d up = headBody.Normalized;
            Vec3d dir = (Frames.ToBody(body, head + dirWorld) - headBody).Normalized;

            // The tunnel goes the way the arm points over the ground, at most maxSlope up or down. However the part
            // is mounted (an arm pointing straight down said "too steep" on flat ground), it digs: an arm nearly
            // vertical digs ahead of the vessel.
            Vec3d level = dir - up * Vec3d.Dot(dir, up);
            if (level.Length < 0.2)
            {
                Vec3d forward = Frames.ToBody(body, vessel.transform.position + (Vector3d)vessel.ReferenceTransform.up) - Frames.ToBody(body, vessel.transform.position);
                level = forward - up * Vec3d.Dot(forward, up);
            }

            if (level.Length < 1e-6)
            {
                uiStatus = Localizer.Format("#Subterra_Borer_TooSteep");
                return;
            }

            double slope = Math.Asin(Math.Max(-1, Math.Min(1, Vec3d.Dot(dir, up)))) * 180 / Math.PI;
            slope = Math.Max(-maxSlope, Math.Min(maxSlope, slope));
            dir = (level.Normalized * Math.Cos(slope * Math.PI / 180) + up * Math.Sin(slope * Math.PI / 180)).Normalized;

            // The tunnel's floor centre under the head.
            Vec3d floor = headBody - up * radius;
            ExcavationSpec tunnel = Tunnel;
            if (tunnel != null && tunnel.Body != body.bodyName)
            {
                tunnel = null;
            }

            // A new tunnel starts ahead of the head (by its radius and a margin), its floor on the ground: the mouth
            // opens in front of the vessel, never under its wheels, as a ramp the vessel then drives down.
            Vec3d start = floor + dir * (radius + StartMargin);
            BodyFrame.ToLatLon(start.Normalized, out double startLat, out double startLon);
            double ground = KspSampler.SurfaceRadius(body, startLat, startLon);
            if (Math.Abs(ground - start.Length) < radius + 2)
            {
                // From the surface (not from a cave floor, far under it).
                start = start.Normalized * (ground - 0.1);
            }
            Vec3d face = tunnel == null ? start : tunnel.ToBody(tunnel.Points[tunnel.Points.Count - 1]);
            if (tunnel != null && Vec3d.Dot(face - floor, dir) > radius + StartMargin + Reach)
            {
                uiStatus = Localizer.Format("#Subterra_Borer_DriveOn");
                return;
            }

            BodyFrame.ToLatLon(face.Normalized, out double lat, out double lon);
            double surface = KspSampler.SurfaceRadius(body, lat, lon);
            double depth = Math.Max(0, surface - face.Length);
            if (depth < 2 * radius + 0.5)
            {
                dir = Descending(dir, face.Normalized, PortalSlope);
            }

            string biome = KspSampler.Biome(body, lat, lon);
            LayerSpec layer = runtime.Strata.LayerAt(depth, biome);
            double limit = DrillMath.VoidLimit(Settings.Current.VoidStrengthMPa, layer?.Density ?? 2.7, body.gravParameter / (body.Radius * body.Radius));
            if (depth > limit)
            {
                uiStatus = Localizer.Format("#Subterra_Borer_TooDeep", limit.ToString("F0"));
                return;
            }

            int pieces = ExcavationService.All.Where(s => s.Body == body.bodyName).Sum(s => s.PieceCount);
            if (pieces >= Settings.Current.MaxTunnelSegments)
            {
                uiStatus = Localizer.Format("#Subterra_Borer_Limit");
                return;
            }

            double need = powerUse * dt;
            double got = need > 0 ? part.RequestResource("ElectricCharge", need) : 0;
            if (got < need * 0.99)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_NoPower");
                return;
            }

            double rate = DrillMath.Rate(baseRate, layer?.Hardness ?? 0.5, runtime.Geotherm.Temperature(depth, biome), maxRockTemp) * Settings.Current.DigSpeed;
            uiRate = (float)rate;
            uiLayer = layer == null ? "-" : string.IsNullOrEmpty(layer.DisplayName) ? layer.Name : Localizer.Format(layer.DisplayName);
            if (rate <= 0)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_TooHard");
                return;
            }

            progress += rate * dt;
            working = true;
            uiStatus = Localizer.Format("#Subterra_Borer_Digging");
            if (progress < Step)
            {
                return;
            }

            progress -= Step;
            if (tunnel == null)
            {
                tunnel = ExcavationService.Start(runtime, start, radius);
                excavationId = tunnel.Id;
                Log.Info($"Tunnel {tunnel.Id} started by {vessel.GetDisplayName()}");
            }

            ExcavationService.Dig(runtime, tunnel, face + dir * Step);
            uiLength = TunnelLength(tunnel);

            // Spoil: part of the rock dug out comes back as Ore (density in t/m³, Ore 0.01 t/unit).
            double tonnes = Math.PI * radius * radius * Step * (layer?.Density ?? 2.7) * spoilOre;
            part.RequestResource("Ore", -tonnes / 0.01);
        }

        // <paramref name="dir"/> turned down (about the horizontal across it) to at least <paramref name="degrees"/> of descent.
        private static Vec3d Descending(Vec3d dir, Vec3d up, double degrees)
        {
            double sin = Vec3d.Dot(dir, up);
            double least = -Math.Sin(degrees * Math.PI / 180);
            if (sin <= least)
            {
                return dir;
            }

            Vec3d level = dir - up * sin;
            if (level.Length < 1e-6)
            {
                return dir;
            }

            return (level.Normalized * Math.Cos(degrees * Math.PI / 180) + up * least).Normalized;
        }

        private static float TunnelLength(ExcavationSpec t)
        {
            double length = 0;
            for (int i = 0; i + 1 < t.Points.Count; i++)
            {
                length += (t.Points[i + 1] - t.Points[i]).Length;
            }

            return (float)length;
        }

        private void UpdateEvents()
        {
            Events[nameof(StartDigging)].active = !digging;
            Events[nameof(StopDigging)].active = digging;
        }
    }
}
