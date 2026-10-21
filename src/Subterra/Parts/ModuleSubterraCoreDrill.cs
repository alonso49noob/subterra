using System;
using System.Collections.Generic;
using KSP.Localization;
using KSP.UI.Screens.Flight.Dialogs;
using Subterra.Core.Excavation;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The SB-1 core drill (sub-project 2): bores a narrow hole straight down from where the vessel stands
    /// (the surface or a cave floor), through the body's strata, slower in hard and hot rock; takes one core
    /// sample per layer (science subject: body + layer) and reports caves the hole breaks into.
    /// The hole has no geometry; it is lost if the vessel moves away. Drills only while the vessel is loaded.
    /// </summary>
    public sealed class ModuleSubterraCoreDrill : PartModule, IScienceDataContainer
    {
        public const string ExperimentId = "subterraCoreSample";

        /// <summary>Metres the vessel may move before the hole is lost.</summary>
        private const double HoleTolerance = 3;

        [KSPField] public float baseRate = 0.5f;
        [KSPField] public float powerUse = 1.5f;
        [KSPField] public float maxDepth = 2000f;
        [KSPField] public float maxRockTemp = 900f;
        [KSPField] public float transmitValue = 0.5f;

        [KSPField(isPersistant = true)] public bool drilling;
        [KSPField(isPersistant = true)] public double depth = -1;
        [KSPField(isPersistant = true)] public double holeLat;
        [KSPField(isPersistant = true)] public double holeLon;
        [KSPField(isPersistant = true)] public string holeBody = "";
        [KSPField(isPersistant = true)] public string reachedCaves = "";

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Depth", guiFormat = "F1", guiUnits = " m")]
        public float uiDepth;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Layer")]
        public string uiLayer = "-";

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_RockTemp", guiFormat = "F0", guiUnits = " K")]
        public float uiRockTemp;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Rate", guiFormat = "F3", guiUnits = " m/s")]
        public float uiRate;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Status")]
        public string uiStatus = "";

        private readonly List<ScienceData> data = new List<ScienceData>();

        /// <summary>Depth (m below the terrain) the hole has reached; -1 without a hole.</summary>
        public double Depth => depth;

        /// <summary>The layer at the bottom of the hole, or null.</summary>
        public LayerSpec Layer { get; private set; }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Drill_Start")]
        public void StartDrilling()
        {
            if (!vessel.Landed)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_NotLanded"), 4f);
                return;
            }

            drilling = true;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Drill_Stop")]
        public void StopDrilling()
        {
            drilling = false;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Drill_Sample")]
        public void TakeCoreSample()
        {
            if (data.Count > 0)
            {
                ReviewData();
                return;
            }

            if (!BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime) || depth < 0 || Layer == null)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_NoLayer"), 4f);
                return;
            }

            ScienceExperiment experiment = ResearchAndDevelopment.GetExperiment(ExperimentId);
            if (experiment == null)
            {
                return;
            }

            CelestialBody body = vessel.mainBody;
            string displayName = string.IsNullOrEmpty(Layer.DisplayName) ? Layer.Name : Localizer.Format(Layer.DisplayName);
            ScienceSubject subject = ResearchAndDevelopment.GetExperimentSubject(experiment, ExperimentSituations.SrfLanded, body, Layer.Name, displayName);

            // Worth more the deeper the layer (set, not multiplied: the subject is kept between samples).
            double mid = (Layer.Top + Math.Min(Layer.Bottom, Layer.Top + 1000)) * 0.5;
            subject.subjectValue = body.scienceValues.LandedDataValue * (float)(1 + mid / 1000);

            data.Add(new ScienceData(experiment.baseValue * experiment.dataScale, transmitValue, 0f, subject.id, subject.title, false, part.flightID));
            ReviewData();
        }

        public override void OnStart(StartState state)
        {
            UpdateEvents();
        }

        public override void OnSave(ConfigNode node)
        {
            foreach (ScienceData d in data)
            {
                d.Save(node.AddNode("ScienceData"));
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            data.Clear();
            foreach (ConfigNode n in node.GetNodes("ScienceData"))
            {
                data.Add(new ScienceData(n));
            }
        }

        public override string GetInfo() =>
            Localizer.Format("#Subterra_Drill_Info", maxDepth.ToString("F0"), powerUse.ToString("F1"), maxRockTemp.ToString("F0"));

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
            {
                return;
            }

            try
            {
                Drill(TimeWarp.fixedDeltaTime);
            }
            catch (Exception e)
            {
                drilling = false;
                Log.Error($"Core drill on {vessel.GetDisplayName()} stopped: {e}");
            }
        }

        private void Drill(double dt)
        {
            if (!BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                uiStatus = Localizer.Format("#Subterra_Drill_NoStrata");
                return;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d world = part.transform.position;
            double lat = body.GetLatitude(world), lon = body.GetLongitude(world);
            string biome = KspSampler.Biome(body, lat, lon);

            // A new hole where the vessel stands (first time, or after it moved away from the old one).
            if (depth < 0 || holeBody != body.bodyName || Distance(body, lat, lon, holeLat, holeLon) > HoleTolerance)
            {
                holeBody = body.bodyName;
                holeLat = lat;
                holeLon = lon;
                reachedCaves = "";
                UndergroundState state = UndergroundTracker.For(vessel);
                depth = state.Inside ? state.Depth : 0;
            }

            if (!drilling || !vessel.Landed)
            {
                Show(Borehole.Advance(depth, 0, baseRate, maxRockTemp, maxDepth, runtime.Strata, runtime.Geotherm, biome),
                    drilling ? "#Subterra_Drill_NotLanded" : "#Subterra_Drill_Idle");
                return;
            }

            double need = powerUse * dt;
            double got = need > 0 ? part.RequestResource("ElectricCharge", need) : 0;
            if (got < need * 0.99)
            {
                Show(Borehole.Advance(depth, 0, baseRate, maxRockTemp, maxDepth, runtime.Strata, runtime.Geotherm, biome), "#Subterra_Drill_NoPower");
                return;
            }

            double before = depth;
            Borehole.Progress p = Borehole.Advance(depth, dt * Settings.Current.DigSpeed, baseRate, maxRockTemp, maxDepth, runtime.Strata, runtime.Geotherm, biome);
            depth = p.Depth;
            CheckCaves(runtime, body, lat, lon, before, depth);
            if (p.Blocked)
            {
                drilling = false;
                UpdateEvents();
                string why = depth >= maxDepth - 0.01 ? "#Subterra_Drill_MaxDepth" : "#Subterra_Drill_TooHard";
                ScreenMessages.PostScreenMessage(Localizer.Format(why), 5f);
                Show(p, why);
                return;
            }

            Show(p, "#Subterra_Drill_Drilling");
        }

        // A cave crossed by the hole between two depths is discovered (once per hole).
        private void CheckCaves(BodyRuntime runtime, CelestialBody body, double lat, double lon, double from, double to)
        {
            Vec3d dir = BodyFrame.Direction(lat, lon);
            double surface = KspSampler.SurfaceRadius(body, lat, lon);
            for (double d = Math.Floor(from / 2) * 2 + 2; d <= to; d += 2)
            {
                TubeNetwork net = runtime.Catalog.FindContaining(dir * (surface - d));
                if (net == null || reachedCaves.Contains("|" + net.Id + "|"))
                {
                    continue;
                }

                reachedCaves += "|" + net.Id + "|";
                DiscoveryService.DiscoverNetwork(vessel, net);
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_Breakthrough", d.ToString("F0")), 6f, ScreenMessageStyle.UPPER_CENTER);
                Log.Info($"Core drill on {vessel.GetDisplayName()} reached {net.Id} at {d:F0} m");
            }
        }

        private void Show(Borehole.Progress p, string status)
        {
            Layer = p.Layer;
            uiDepth = (float)Math.Max(0, depth);
            uiLayer = p.Layer == null ? "-" : string.IsNullOrEmpty(p.Layer.DisplayName) ? p.Layer.Name : Localizer.Format(p.Layer.DisplayName);
            uiRockTemp = (float)(p.RockTemp ?? 0);
            uiRate = (float)p.Rate;
            uiStatus = Localizer.Format(status);
        }

        private void UpdateEvents()
        {
            Events[nameof(StartDrilling)].active = !drilling;
            Events[nameof(StopDrilling)].active = drilling;
        }

        private static double Distance(CelestialBody body, double lat0, double lon0, double lat1, double lon1)
        {
            Vec3d a = BodyFrame.Direction(lat0, lon0), b = BodyFrame.Direction(lat1, lon1);
            return (a - b).Length * body.Radius;
        }

        // --- IScienceDataContainer ---------------------------------------------------------------

        public ScienceData[] GetData() => data.ToArray();

        public int GetScienceCount() => data.Count;

        public bool IsRerunnable() => true;

        public void ReturnData(ScienceData d)
        {
            if (d != null && !data.Contains(d))
            {
                data.Add(d);
            }
        }

        public void DumpData(ScienceData d) => data.Remove(d);

        public void ReviewData()
        {
            foreach (ScienceData d in data.ToArray())
            {
                ReviewDataItem(d);
            }
        }

        public void ReviewDataItem(ScienceData d)
        {
            var page = new ExperimentResultDialogPage(part, d, d.baseTransmitValue, d.transmitBonus, false, "", true,
                new ScienceLabSearch(vessel, d), DumpData, _ => { }, Transmit, _ => { });
            ExperimentsResultDialog.DisplayResult(page);
        }

        private void Transmit(ScienceData d)
        {
            IScienceDataTransmitter transmitter = ScienceUtil.GetBestTransmitter(vessel);
            if (transmitter == null)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#autoLOC_237738"), 4f);
                return;
            }

            transmitter.TransmitData(new List<ScienceData> { d });
            data.Remove(d);
        }
    }
}
