using System;
using KSP.Localization;

namespace Subterra
{
    /// <summary>
    /// The SB-G geothermal generator (Lucas: a part that turns the heat of the depths into charge, more the hotter it is; and
    /// it must not cool the craft, that would be too strong). A heat engine between the rock round it and a cold side kept
    /// at <see cref="coldTemp"/>: its share of <see cref="maxRate"/> is the Carnot efficiency, <c>1 - Tcold / Trock</c>, so it
    /// gives nothing in rock at room temperature, half at 600 K and nine tenths past 3000 K. Works in time warp (loaded
    /// craft). [Claude, revisar]
    /// </summary>
    public sealed class ModuleSubterraGeothermal : PartModule
    {
        /// <summary>Electric charge per second in the hottest rock.</summary>
        [KSPField] public float maxRate = 12f;

        /// <summary>Temperature (K) of the cold side.</summary>
        [KSPField] public float coldTemp = 300f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Geo_Active")]
        [UI_Toggle(disabledText = "#Subterra_Off", enabledText = "#Subterra_On")]
        public bool active = true;

        [KSPField(guiActive = true, guiName = "#Subterra_Geo_Output", guiFormat = "F2", guiUnits = " EC/s")]
        public float uiOutput;

        [KSPField(guiActive = true, guiName = "#Subterra_Geo_Rock", guiFormat = "F0", guiUnits = " K")]
        public float uiRock;

        [KSPField(guiActive = true, guiName = "#Subterra_Drill_Status")]
        public string uiStatus = "";

        private float nextRock;
        private double? rock;

        public override string GetInfo() =>
            Localizer.Format("#Subterra_Geo_Info", maxRate.ToString("F1"), (maxRate * (1 - coldTemp / 600.0)).ToString("F1"), (maxRate * (1 - coldTemp / 1500.0)).ToString("F1"));

        /// <summary>Share of the most output in rock at <paramref name="rockTemp"/> K with the cold side at <paramref name="cold"/> K.</summary>
        public static double Efficiency(double rockTemp, double cold) => rockTemp <= cold ? 0 : 1 - cold / rockTemp;

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
            {
                return;
            }

            uiOutput = 0;
            if (!active)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_Idle");
                return;
            }

            // The rock's temperature round the craft: once a second (the cave's, or the rock it is buried in).
            if (UnityEngine.Time.time >= nextRock)
            {
                nextRock = UnityEngine.Time.time + 1f;
                UndergroundState state = UndergroundTracker.For(vessel);
                rock = state.Inside ? state.RockTemp : ThermalPatches.RockTempAround(vessel);
            }

            if (!rock.HasValue)
            {
                uiRock = 0;
                uiStatus = Localizer.Format("#Subterra_Geo_NotUnderground");
                return;
            }

            uiRock = (float)rock.Value;
            double rate = maxRate * Efficiency(rock.Value, coldTemp);
            if (rate <= 0)
            {
                uiStatus = Localizer.Format("#Subterra_Geo_TooCold");
                return;
            }

            double dt = TimeWarp.fixedDeltaTime;
            double made = -part.RequestResource("ElectricCharge", -rate * dt);
            uiOutput = (float)(made / dt);
            uiStatus = Localizer.Format(made > 0 ? "#Subterra_Geo_Generating" : "#Subterra_Geo_Full");
        }
    }
}
