using System;
using KSP.Localization;

namespace Subterra
{
    /// <summary>
    /// The SB-K evaporative coolers (Lucas: an inline part, mounted like a fuel tank, to cool a craft that got too hot going
    /// deep). Deep under the ground there is nowhere for a radiator to shed heat (the rock around is the hot thing), so the
    /// cooler pumps the heat of every part of the vessel above its set temperature into water and vents it as steam:
    /// about 2.5 MJ per kilogram boiled off. Pumps take a little electricity. Its own intake tops the water up from cave
    /// water and seas. [Claude, revisar]
    /// </summary>
    public sealed class ModuleSubterraHeatSink : PartModule
    {
        /// <summary>Most heat (kW) it takes out of the vessel.</summary>
        [KSPField] public float maxPower = 200f;

        /// <summary>Heat (kJ) one unit of the coolant takes away (water: warmed up and boiled, ~2.5 MJ per kg).</summary>
        [KSPField] public float kJPerUnit = 2500f;

        [KSPField] public string coolant = "Water";

        /// <summary>Electric charge per second while it cools.</summary>
        [KSPField] public float powerUse = 1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Cooler_Active")]
        [UI_Toggle(disabledText = "#Subterra_Off", enabledText = "#Subterra_On")]
        public bool active = true;

        /// <summary>Parts are kept at or under this temperature (K).</summary>
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Cooler_Target", guiUnits = " K")]
        [UI_FloatRange(minValue = 250f, maxValue = 600f, stepIncrement = 10f)]
        public float targetTemp = 310f;

        [KSPField(guiActive = true, guiName = "#Subterra_Cooler_Load", guiFormat = "F0", guiUnits = " kW")]
        public float uiLoad;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Status")]
        public string uiStatus = "";

        private float nextLog;

        public override string GetInfo() => Localizer.Format("#Subterra_Cooler_Info", maxPower.ToString("F0"), (maxPower / kJPerUnit * 3600).ToString("F0"), powerUse.ToString("F1"));

        /// <summary>
        /// True while this cooler holds the craft down in time warp (it has coolant and power): KSP's warp ("analytic")
        /// temperature is capped at its target by ThermalPatches. Lucas: the coolers stopped working in time warp, where KSP
        /// works the craft's temperature out in one go and ignores the heat taken out of each part.
        /// </summary>
        public bool HoldsInWarp { get; private set; }

        public float TargetTemp => targetTemp;

        // In time warp: coolant and power used at the full rate (the heat it would take out is not known part by part there).
        private void WarpCooling()
        {
            double dt = TimeWarp.fixedDeltaTime;
            double need = powerUse * dt;
            if (need > 0 && part.RequestResource("ElectricCharge", need) < need * 0.99)
            {
                HoldsInWarp = false;
                uiStatus = Localizer.Format("#Subterra_Drill_NoPower");
                return;
            }

            double units = maxPower * dt / kJPerUnit;
            double got = part.RequestResource(coolant, units);
            HoldsInWarp = got > units * 0.5;
            uiLoad = HoldsInWarp ? maxPower : 0;
            uiStatus = Localizer.Format(HoldsInWarp ? "#Subterra_Cooler_Cooling" : "#Subterra_Cooler_Empty");
        }

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
            {
                return;
            }

            uiLoad = 0;
            if (!active)
            {
                HoldsInWarp = false;
                uiStatus = Localizer.Format("#Subterra_Drill_Idle");
                return;
            }

            if (vessel.packed || (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > 1))
            {
                WarpCooling();
                return;
            }

            HoldsInWarp = false;
            double dt = TimeWarp.fixedDeltaTime;

            // The heat above the set temperature, in every part (core and skin): what the cooler would take this step.
            double excess = 0;
            foreach (Part p in vessel.parts)
            {
                excess += Math.Max(0, p.temperature - targetTemp) * p.thermalMass + Math.Max(0, p.skinTemperature - targetTemp) * p.skinThermalMass;
            }

            if (excess <= 0)
            {
                uiStatus = Localizer.Format("#Subterra_Cooler_Cool");
                return;
            }

            double need = powerUse * dt;
            if (need > 0 && part.RequestResource("ElectricCharge", need) < need * 0.99)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_NoPower");
                return;
            }

            // At most maxPower, and only as much as the coolant left can take.
            double take = Math.Min(excess, maxPower * dt);
            double units = take / kJPerUnit;
            double got = part.RequestResource(coolant, units);
            if (got <= 0)
            {
                uiStatus = Localizer.Format("#Subterra_Cooler_Empty");
                return;
            }

            take *= got / units;
            double share = take / excess;
            foreach (Part p in vessel.parts)
            {
                double core = Math.Max(0, p.temperature - targetTemp) * p.thermalMass * share;
                double skin = Math.Max(0, p.skinTemperature - targetTemp) * p.skinThermalMass * share;
                // As heat fluxes (kW) KSP's thermal integration applies: temperatures set directly were mostly undone by it.
                if (core > 0)
                {
                    p.AddThermalFlux(-core / dt);
                }

                if (skin > 0)
                {
                    p.AddSkinThermalFlux(-skin / dt);
                }
            }

            if (UnityEngine.Time.time >= nextLog)
            {
                Part first = vessel.rootPart;
                Log.Info($"Cooler diag: excess {excess:F0} kJ, take {take:F0} kJ, root core {first.temperature:F1} K (mass {first.thermalMass:F2} kJ/K), skin {first.skinTemperature:F1} K (mass {first.skinThermalMass:F2}), flux in {first.thermalInternalFlux:F1}");
            }

            uiLoad = (float)(take / dt);
            uiStatus = Localizer.Format("#Subterra_Cooler_Cooling");
            if (UnityEngine.Time.time >= nextLog)
            {
                nextLog = UnityEngine.Time.time + 10f;
                Log.Info($"Cooler on {vessel.GetDisplayName()}: {uiLoad:F0} kW out, {got / dt:F2} {coolant}/s boiled off");
            }
        }
    }
}
