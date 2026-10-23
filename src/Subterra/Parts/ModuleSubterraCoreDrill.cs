using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed partial class ModuleSubterraCoreDrill : PartModule, IScienceDataContainer, IAnimatedModule
    {
        // IAnimatedModule: the part's ModuleAnimationGroup plays its "drilling" animation while ModuleIsActive,
        // and blocks the work while the drill is retracted.
        private bool working;
        private bool retracted;

        public void EnableModule() => retracted = false;

        public void DisableModule() => retracted = true;

        public bool ModuleIsActive() => working;

        public bool IsSituationValid() => true;

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

        // Layers of this hole already checked for gas ("|name|"), and the gas struck (resource, units left) for the pump.
        [KSPField(isPersistant = true)] public string gasChecked = "";
        [KSPField(isPersistant = true)] public string gasResource = "";
        [KSPField(isPersistant = true)] public double gasLeft;
        [KSPField(isPersistant = true)] public double holeLat;
        [KSPField(isPersistant = true)] public double holeLon;
        [KSPField(isPersistant = true)] public string holeBody = "";
        [KSPField(isPersistant = true)] public string reachedCaves = "";

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Depth", guiFormat = "F1", guiUnits = " m")]
        public float uiDepth;

        /// <summary>Explosive (kg) the hole would take if loaded now (refreshed once a second; caves along it not counted).</summary>
        [KSPField(guiActive = true, guiName = "#Subterra_Drill_NeedKg", guiFormat = "F0", guiUnits = " kg")]
        public float uiNeedKg;

        private float nextNeed;

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
            ModuleAnimationGroup group = part.FindModuleImplementing<ModuleAnimationGroup>();
            if (group != null && !group.isDeployed)
            {
                group.DeployModule();
            }

            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Drill_Stop")]
        public void StopDrilling()
        {
            drilling = false;
            // Pull the rod out: a deployed rod left in the ground can jack a low-mounted rover up and tip it.
            ModuleAnimationGroup group = part.FindModuleImplementing<ModuleAnimationGroup>();
            if (group != null && group.isDeployed)
            {
                group.RetractModule();
            }

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

        /// <summary>Radius (m) of the shaft a loaded hole opens.</summary>
        [KSPField] public float blastRadius = 3f;

        /// <summary>Seconds between "detonate" and the blast.</summary>
        [KSPField] public float fuseSeconds = 10f;

        /// <summary>The hole this drill loaded and has not fired yet (ExcavationService), or empty.</summary>
        [KSPField(isPersistant = true)] public string armedBlastId = "";

        /// <summary>Loads the hole with explosive (charges per layer, none across caves, stemming on top) and leaves it armed.</summary>
        [KSPEvent(guiActive = true, guiName = "#Subterra_Profile_Show")]
        public void ShowProfile() => ProfileWindow.Toggle(this);

        [KSPEvent(guiActive = true, guiName = "#Subterra_Drill_Load")]
        public void LoadCharges()
        {
            if (sideways)
            {
                LoadRound();
                return;
            }

            if (!BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime) || depth <= 0)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_NoLayer"), 4f);
                return;
            }

            CelestialBody body = vessel.mainBody;
            string biome = KspSampler.Biome(body, holeLat, holeLon);
            double top = 0;
            UndergroundState state = UndergroundTracker.For(vessel);
            if (state.Inside)
            {
                top = state.Depth;
            }

            BlastDesign design = BlastDesign.Plan(top, depth, blastRadius, runtime.Strata, biome, Voids(runtime, body, top, depth));
            if (design.Decks.Count == 0)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_TooShallow"), 5f);
                return;
            }

            double have = vessel.parts.Sum(p => p.Resources.Cast<PartResource>().Where(r => r.resourceName == "Explosive").Sum(r => r.amount));
            if (have < design.TotalKg)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_NeedExplosive", design.TotalKg.ToString("F0"), have.ToString("F0")), 6f);
                return;
            }

            part.RequestResource("Explosive", design.TotalKg);
            ExcavationService.ArmedBlast blast = ExcavationService.Arm(runtime, holeLat, holeLon, design);
            armedBlastId = blast.Id;
            BlastRings.Show(blast);
            (double destroy, double damage) = BlastSafety.Radii(blast.TopDeckKg, true);
            ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_Loaded", design.Decks.Count, design.TotalKg.ToString("F0"),
                destroy.ToString("F0"), damage.ToString("F0")), 8f, ScreenMessageStyle.UPPER_CENTER);
            drilling = false;
            UpdateEvents();
        }

        /// <summary>Fires the armed hole after the fuse, warning about vessels within the danger radius.</summary>
        [KSPEvent(guiActive = true, guiName = "#Subterra_Drill_Detonate")]
        public void Detonate()
        {
            if (roundArmed)
            {
                DetonateRound();
                return;
            }

            ExcavationService.ArmedBlast blast = ExcavationService.FindBlast(armedBlastId);
            if (blast == null || !BodyRegistry.Instance.TryGet(FlightGlobals.GetBodyByName(blast.Body), out BodyRuntime runtime))
            {
                armedBlastId = "";
                UpdateEvents();
                return;
            }

            CelestialBody body = runtime.Body;
            Vector3d top = Frames.ToWorld(body, BodyFrame.Direction(blast.Lat, blast.Lon) * KspSampler.SurfaceRadius(body, blast.Lat, blast.Lon));
            List<Vessel> inDanger = ExcavationService.VesselsInDanger(top, blast.TopDeckKg, true);
            if (inDanger.Count > 0)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_Danger", string.Join(", ", inDanger.Select(v => v.GetDisplayName()))), 6f, ScreenMessageStyle.UPPER_CENTER);
            }

            armedBlastId = "";
            UpdateEvents();
            StartCoroutine(Fuse(runtime, blast));
        }

        private System.Collections.IEnumerator Fuse(BodyRuntime runtime, ExcavationService.ArmedBlast blast)
        {
            for (int s = Mathf.CeilToInt(fuseSeconds); s > 0; s--)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_Countdown", s), 1f, ScreenMessageStyle.UPPER_CENTER);
                yield return new WaitForSeconds(1f);
            }

            ExcavationService.Detonate(runtime, blast);
            BlastRings.Hide(blast.Id);
        }

        // Depth spans along the hole that lie inside a cave or tunnel (never loaded).
        private List<(double top, double bottom)> Voids(BodyRuntime runtime, CelestialBody body, double from, double to)
        {
            var spans = new List<(double top, double bottom)>();
            Vec3d dir = BodyFrame.Direction(holeLat, holeLon);
            double surface = KspSampler.SurfaceRadius(body, holeLat, holeLon);
            double? start = null;
            for (double d = from; d <= to + 0.5; d += 0.5)
            {
                bool inside = runtime.Catalog.FindContaining(dir * (surface - d)) != null;
                if (inside && start == null)
                {
                    start = d;
                }
                else if (!inside && start != null)
                {
                    spans.Add((start.Value - 1, d + 1));
                    start = null;
                }
            }

            if (start != null)
            {
                spans.Add((start.Value - 1, to + 1));
            }

            return spans;
        }

        public override void OnStart(StartState state)
        {
            UpdateEvents();
            if (HighLogic.LoadedSceneIsFlight && !string.IsNullOrEmpty(armedBlastId) && ExcavationService.FindBlast(armedBlastId) is ExcavationService.ArmedBlast armed)
            {
                BlastRings.Show(armed);
            }
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
            working = false;
            if (!BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                uiStatus = Localizer.Format("#Subterra_Drill_NoStrata");
                return;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d world = part.transform.position;
            double lat = body.GetLatitude(world), lon = body.GetLongitude(world);
            string biome = KspSampler.Biome(body, lat, lon);
            UpdateNeed(runtime, biome);
            if (DrillSideways(dt, runtime, body))
            {
                return;
            }

            // A new hole where the vessel stands (first time, or after it moved away from the old one).
            if (depth < 0 || holeBody != body.bodyName || Distance(body, lat, lon, holeLat, holeLon) > HoleTolerance)
            {
                holeBody = body.bodyName;
                holeLat = lat;
                holeLon = lon;
                reachedCaves = "";
                gasChecked = "";
                gasResource = "";
                gasLeft = 0;
                UndergroundState state = UndergroundTracker.For(vessel);
                depth = state.Inside ? state.Depth : 0;
            }

            if (!drilling || !vessel.Landed || retracted)
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
            CheckGas(body, lat, lon, p.Layer);
            if (p.Blocked)
            {
                drilling = false;
                UpdateEvents();
                string why = depth >= maxDepth - 0.01 ? "#Subterra_Drill_MaxDepth" : "#Subterra_Drill_TooHard";
                ScreenMessages.PostScreenMessage(Localizer.Format(why), 5f);
                Show(p, why);
                return;
            }

            working = true;
            Show(p, "#Subterra_Drill_Drilling");
        }

        // Entering a layer with gas may strike a pocket (fixed per place): a burst of heat and a kick on the part, and gas
        // the pump can draw.
        private void CheckGas(CelestialBody body, double lat, double lon, LayerSpec layer)
        {
            if (layer == null || gasChecked.Contains("|" + layer.Name + "|"))
            {
                return;
            }

            gasChecked += "|" + layer.Name + "|";
            if (!GasPocket.Hit(body.bodyName, lat, lon, layer) || PartResourceLibrary.Instance.GetDefinition(layer.GasResource) == null)
            {
                return;
            }

            gasResource = layer.GasResource;
            gasLeft = GasPocket.Units;
            part.temperature += GasHeat;
            if (part.Rigidbody != null)
            {
                Vector3 up = (part.transform.position - body.position).normalized;
                part.Rigidbody.AddForce(up * (float)(part.Rigidbody.mass * GasKick), ForceMode.Impulse);
            }

            ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_Gas", layer.GasResource), 6f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info($"Core drill on {vessel.GetDisplayName()} struck {layer.GasResource} in {layer.Name} at {depth:F0} m");
        }

        /// <summary>Kelvin a gas strike adds to the drill, and its kick (m/s) (decidido por Claude, revisar).</summary>
        private const double GasHeat = 120, GasKick = 1.5;

        /// <summary>Draws up to <paramref name="units"/> of the struck gas; returns what there was.</summary>
        public double DrawGas(double units)
        {
            double taken = Math.Min(units, gasLeft);
            gasLeft -= taken;
            return taken;
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

        // The explosive the hole as drilled would take (vertical: its decks; sideways: the round), once a second.
        private void UpdateNeed(BodyRuntime runtime, string biome)
        {
            if (Time.time < nextNeed)
            {
                return;
            }

            nextNeed = Time.time + 1f;
            if (sideways)
            {
                uiNeedKg = (float)(BlastDesign.PowderFactor(Layer) * Math.PI * blastRadius * blastRadius * roundLength);
                return;
            }

            UndergroundState state = UndergroundTracker.For(vessel);
            double top = state.Inside ? state.Depth : 0;
            uiNeedKg = depth > top ? (float)BlastDesign.Plan(top, depth, blastRadius, runtime.Strata, biome, null).TotalKg : 0;
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
            Events[nameof(Detonate)].active = !string.IsNullOrEmpty(armedBlastId) || roundArmed;
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
