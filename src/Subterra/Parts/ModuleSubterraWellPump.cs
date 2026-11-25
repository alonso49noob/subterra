using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;
using Subterra.Core.Model;

namespace Subterra
{
    /// <summary>
    /// The SB-P well pump (sub-project 2): draws what the layer at the bottom of a core drill's hole (same
    /// vessel, the deepest one) gives (LAYER YIELD), into the vessel's tanks. Resources that do not exist in
    /// the game (Water without CommunityResourcePack) are skipped.
    /// </summary>
    public sealed class ModuleSubterraWellPump : PartModule
    {
        [KSPField] public float powerUse = 0.5f;

        /// <summary>Multiplies every layer's yield.</summary>
        [KSPField] public float pumpRate = 1f;

        [KSPField(isPersistant = true)] public bool pumping;

        [KSPField(guiActive = true, guiName = "#Subterra_Pump_Output")]
        public string uiOutput = "-";

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Status")]
        public string uiStatus = "";

        [KSPEvent(guiActive = true, guiName = "#Subterra_Pump_Start")]
        public void StartPumping()
        {
            pumping = true;
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Pump_Stop")]
        public void StopPumping()
        {
            pumping = false;
            UpdateEvents();
        }

        public override void OnStart(StartState state)
        {
            UpdateEvents();
        }

        public override string GetInfo() => Localizer.Format("#Subterra_Pump_Info", powerUse.ToString("F1"));

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || Off)
            {
                return;
            }

            try
            {
                Pump(TimeWarp.fixedDeltaTime);
            }
            catch (Exception e)
            {
                pumping = false;
                UpdateEvents();
                Log.Error($"Well pump on {vessel.GetDisplayName()} stopped: {e}");
            }
        }

        private void Pump(double dt)
        {
            ModuleSubterraCoreDrill drill = vessel.FindPartModulesImplementing<ModuleSubterraCoreDrill>()
                .Where(d => d.Depth >= 0 && d.Layer != null).OrderByDescending(d => d.Depth).FirstOrDefault();
            LayerSpec layer = drill?.Layer;
            List<KeyValuePair<string, double>> yields = layer == null
                ? new List<KeyValuePair<string, double>>()
                : layer.Yields.Where(y => PartResourceLibrary.Instance.GetDefinition(y.Key) != null).ToList();
            // A hole into a flooded cave: its water comes up (Lucas: pump from caves and lakes the hole breaks into).
            if (drill != null && PartResourceLibrary.Instance.GetDefinition("Water") != null && drill.HoleReachesWater())
            {
                yields.Add(new KeyValuePair<string, double>("Water", CaveWaterRate));
            }

            // Gas a drill struck comes up too, until the pocket is empty.
            bool gas = drill != null && drill.gasLeft > 0 && PartResourceLibrary.Instance.GetDefinition(drill.gasResource) != null;
            if (gas)
            {
                yields.Add(new KeyValuePair<string, double>(drill.gasResource, GasRate));
            }

            uiOutput = yields.Count == 0 ? "-" : string.Join(", ", yields.Select(y => $"{y.Key} {y.Value * pumpRate:F3}/s"));
            if (!pumping)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_Idle");
                return;
            }

            if (yields.Count == 0)
            {
                uiStatus = Localizer.Format(drill == null ? "#Subterra_Pump_NoHole" : "#Subterra_Pump_Dry");
                return;
            }

            double need = powerUse * dt;
            double got = need > 0 ? part.RequestResource("ElectricCharge", need) : 0;
            if (got < need * 0.99)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_NoPower");
                return;
            }

            foreach (KeyValuePair<string, double> y in yields)
            {
                double units = y.Value * pumpRate * dt;
                if (gas && y.Key == drill.gasResource && y.Value == GasRate)
                {
                    units = drill.DrawGas(units);
                }

                part.RequestResource(y.Key, -units);
            }

            uiStatus = Localizer.Format("#Subterra_Pump_Pumping");
        }

        /// <summary>Units per second (times pumpRate) drawn from a gas pocket (decidido por Claude, revisar).</summary>
        private const double GasRate = 0.5;

        /// <summary>Water per second (times pumpRate) from a flooded cave the hole broke into.</summary>
        private const double CaveWaterRate = 0.05;

        private void UpdateEvents() => RefreshEvents();

        // On a stock drill the pump is one of its borehole tools: hidden (and idle) while borehole mode is off.
        private bool Off => part.FindModuleImplementing<ModuleSubterraCoreDrill>() is ModuleSubterraCoreDrill drill && !drill.BoreholeActive;

        internal void RefreshEvents()
        {
            bool off = Off;
            if (off && pumping)
            {
                pumping = false;
            }

            Events[nameof(StartPumping)].active = !off && !pumping;
            Events[nameof(StopPumping)].active = !off && pumping;
            Fields[nameof(uiOutput)].guiActive = !off;
            Fields[nameof(uiStatus)].guiActive = !off;
        }
    }
}
