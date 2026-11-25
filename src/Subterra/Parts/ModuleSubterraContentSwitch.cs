using System;
using KSP.Localization;

namespace Subterra
{
    /// <summary>
    /// A stock tank that can hold Subterra's resource instead of its own (Lucas: the stock parts with an extra option, not
    /// Subterra copies of them; first the ore tanks as explosive magazines). Switched in the editor, or in flight while the
    /// tank is empty; the capacity is the same mass of the other resource. [Claude, revisar]
    /// </summary>
    public sealed class ModuleSubterraContentSwitch : PartModule
    {
        /// <summary>The tank's own resource and the one it can hold instead.</summary>
        [KSPField] public string baseResource = "Ore";
        [KSPField] public string altResource = "Explosive";

        [KSPField(isPersistant = true)] public bool holdingAlt;

        /// <summary>The tank's own capacity, read from the part's config when it is first seen.</summary>
        [KSPField(isPersistant = true)] public double baseMax = -1;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Tank_Contents")]
        public string uiContents = "";

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Tank_Switch")]
        public void Switch()
        {
            string now = holdingAlt ? altResource : baseResource;
            if (HighLogic.LoadedSceneIsFlight && part.Resources.Contains(now) && part.Resources[now].amount > 1e-6)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Tank_NotEmpty"), 4f);
                return;
            }

            holdingAlt = !holdingAlt;
            Apply(true);
        }

        public override void OnStart(StartState state) => Apply(false);

        // The part holds the chosen resource (and not the other), full in the editor when just switched.
        private void Apply(bool switched)
        {
            PartResourceDefinition alt = PartResourceLibrary.Instance.GetDefinition(altResource);
            PartResourceDefinition bas = PartResourceLibrary.Instance.GetDefinition(baseResource);
            if (alt == null || bas == null)
            {
                Events[nameof(Switch)].active = false;
                return;
            }

            if (baseMax < 0)
            {
                baseMax = part.Resources.Contains(baseResource) ? part.Resources[baseResource].maxAmount : 0;
            }

            // The same mass of the other resource.
            double altMax = bas.density > 0 && alt.density > 0 ? baseMax * bas.density / alt.density : baseMax;
            string want = holdingAlt ? altResource : baseResource;
            string drop = holdingAlt ? baseResource : altResource;
            double max = holdingAlt ? altMax : baseMax;
            if (part.Resources.Contains(drop))
            {
                part.Resources.Remove(drop);
            }

            bool full = switched && HighLogic.LoadedSceneIsEditor;
            if (!part.Resources.Contains(want))
            {
                part.Resources.Add(want, full ? max : 0, max, true, true, false, true, PartResource.FlowMode.Both);
            }
            else
            {
                PartResource r = part.Resources[want];
                r.maxAmount = max;
                r.amount = full ? max : Math.Min(r.amount, max);
            }

            uiContents = PartResourceLibrary.Instance.GetDefinition(want).displayName;
            if (switched)
            {
                MonoUtilities.RefreshPartContextWindow(part);
                if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null)
                {
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
            }
        }

        public override string GetInfo() => Localizer.Format("#Subterra_Tank_Info", altResource);
    }
}
