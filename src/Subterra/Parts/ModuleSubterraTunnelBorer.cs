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
    public sealed class ModuleSubterraTunnelBorer : PartModule
    {
        /// <summary>Metres dug per step (one call to ExcavationService.Dig).</summary>
        private const double Step = 1;

        /// <summary>The face stops advancing when it is this far ahead of the head (drive forward to dig on).</summary>
        private const double Reach = 3;

        [KSPField] public float radius = 2.5f;
        [KSPField] public float baseRate = 0.15f;
        [KSPField] public float powerUse = 15f;
        [KSPField] public float maxRockTemp = 700f;
        [KSPField] public float maxSlope = 30f;
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
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Borer_Stop")]
        public void StopDigging()
        {
            digging = false;
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
            UpdateEvents();
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
            if (!digging)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_Idle");
                return;
            }

            if (!Settings.Current.Excavation || !BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                uiStatus = Localizer.Format("#Subterra_Borer_Disabled");
                return;
            }

            if (vessel.srfSpeed > 0.5)
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
            double slope = Math.Asin(Math.Max(-1, Math.Min(1, Vec3d.Dot(dir, up)))) * 180 / Math.PI;
            if (Math.Abs(slope) > maxSlope)
            {
                uiStatus = Localizer.Format("#Subterra_Borer_TooSteep");
                return;
            }

            // The tunnel's floor centre under the head.
            Vec3d floor = headBody - up * radius;
            ExcavationSpec tunnel = Tunnel;
            if (tunnel != null && tunnel.Body != body.bodyName)
            {
                tunnel = null;
            }

            Vec3d face = tunnel == null ? floor : tunnel.ToBody(tunnel.Points[tunnel.Points.Count - 1]);
            if (tunnel != null && Vec3d.Dot(face - floor, dir) > Reach)
            {
                uiStatus = Localizer.Format("#Subterra_Borer_DriveOn");
                return;
            }

            BodyFrame.ToLatLon(face.Normalized, out double lat, out double lon);
            double surface = KspSampler.SurfaceRadius(body, lat, lon);
            double depth = Math.Max(0, surface - face.Length);
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
            uiStatus = Localizer.Format("#Subterra_Borer_Digging");
            if (progress < Step)
            {
                return;
            }

            progress -= Step;
            if (tunnel == null)
            {
                tunnel = ExcavationService.Start(runtime, floor, radius);
                excavationId = tunnel.Id;
                Log.Info($"Tunnel {tunnel.Id} started by {vessel.GetDisplayName()}");
            }

            ExcavationService.Dig(runtime, tunnel, face + dir * Step);
            uiLength = TunnelLength(tunnel);

            // Spoil: part of the rock dug out comes back as Ore (density in t/m³, Ore 0.01 t/unit).
            double tonnes = Math.PI * radius * radius * Step * (layer?.Density ?? 2.7) * spoilOre;
            part.RequestResource("Ore", -tonnes / 0.01);
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
