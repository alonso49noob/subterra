using KSP.Localization;

namespace Subterra
{
    /// <summary>
    /// The water engine's intake (Lucas: an engine that runs on water). In water (KSP's sea, a cave's, an ocean under
    /// ice) it fills the part's Water from around it, so the engine never runs dry there; out of water the engine runs on
    /// what its tank holds. [Claude, revisar]
    /// </summary>
    public sealed class ModuleSubterraWaterIntake : PartModule
    {
        /// <summary>Units of Water per second drawn from around the part.</summary>
        [KSPField] public float intakeRate = 10f;

        [KSPField] public string resourceName = "Water";

        [KSPField(guiActive = true, guiName = "#Subterra_Intake_Status")]
        public string uiStatus = "";

        public override string GetInfo() => Localizer.Format("#Subterra_Intake_Info", intakeRate.ToString("F0"));

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || vessel.packed)
            {
                return;
            }

            SubterraWaterModule water = SubterraWaterModule.Of(vessel);
            bool wet = part.WaterContact || (water != null && water.InWater(part, out _));
            uiStatus = Localizer.Format(wet ? "#Subterra_Intake_Drawing" : "#Subterra_Intake_Dry");
            if (wet && PartResourceLibrary.Instance.GetDefinition(resourceName) != null)
            {
                part.RequestResource(resourceName, -intakeRate * TimeWarp.fixedDeltaTime, ResourceFlowMode.NO_FLOW);
            }
        }
    }
}
