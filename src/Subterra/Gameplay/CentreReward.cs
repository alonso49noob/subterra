using System;
using KSP.Localization;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// A reward for whoever digs to the centre of a world (Lucas: something for the brave who make it to the deepest part of
    /// the core). The first time on each body (saved with the game) that a core drill's hole, or a vessel down a dug hole or a
    /// cave, comes within <see cref="Reach"/> of the body's centre: a dialog with that world's own words, and in career and
    /// science games science, funds and reputation that grow with how hard the world is to dig and how far it is (its landed
    /// science value) and how big it is. [Claude, revisar: the amounts]
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class CentreReward : MonoBehaviour
    {
        /// <summary>Metres from a body's centre that count as reaching it.</summary>
        public const double Reach = 1000;

        private float nextCheck;

        // The active vessel, down a cave or a dug hole, near the centre.
        private void Update()
        {
            if (Time.time < nextCheck || !FlightGlobals.ready)
            {
                return;
            }

            nextCheck = Time.time + 1f;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.mainBody == null || (v.CoMD - v.mainBody.position).magnitude > Reach)
            {
                return;
            }

            if (UndergroundTracker.For(v).Inside)
            {
                Reached(v, v.mainBody);
            }
        }

        /// <summary>The vessel reached the body's centre (by digging): the reward, the first time on that body.</summary>
        public static void Reached(Vessel vessel, CelestialBody body)
        {
            SubterraScenario scenario = SubterraScenario.Instance;
            if (scenario == null || body == null || !scenario.ReachCentre(body.bodyName, vessel?.GetDisplayName(), Planetarium.GetUniversalTime()))
            {
                return;
            }

            double science = Science(body);
            string rewards = Grant(science);
            string name = body.displayName.LocalizeRemoveGender();
            string lore = Localizer.TryGetStringByTag("#Subterra_Centre_" + body.bodyName, out _)
                ? Localizer.Format("#Subterra_Centre_" + body.bodyName, name)
                : Localizer.Format("#Subterra_Centre_Any", name);
            string message = lore + (string.IsNullOrEmpty(rewards) ? "" : "\n\n" + rewards);
            Log.Info($"Centre of {body.bodyName} reached by {vessel?.GetDisplayName() ?? "-"}: {rewards}");
            ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Centre_Message", name), 10f, ScreenMessageStyle.UPPER_CENTER);
            PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "SubterraCentre",
                Localizer.Format("#Subterra_Centre_Title", name), message, Localizer.Format("#Subterra_Centre_Ok"), false, HighLogic.UISkin);
        }

        /// <summary>Science for reaching the centre: 25 × the landed science value × the radius in 50 km, at least 50.</summary>
        public static double Science(CelestialBody body) =>
            Math.Max(50, 25 * body.scienceValues.LandedDataValue * body.Radius / 50000);

        // Science in career and science games, funds (2000 per point) and reputation (25 + a tenth of it) in career.
        private static string Grant(double science)
        {
            Game.Modes mode = HighLogic.CurrentGame?.Mode ?? Game.Modes.SANDBOX;
            if (mode != Game.Modes.CAREER && mode != Game.Modes.SCIENCE_SANDBOX)
            {
                return "";
            }

            string text = "";
            if (ResearchAndDevelopment.Instance != null)
            {
                ResearchAndDevelopment.Instance.AddScience((float)science, TransactionReasons.Progression);
                text += Localizer.Format("#Subterra_Centre_Science", science.ToString("N0"));
            }

            if (mode == Game.Modes.CAREER)
            {
                double funds = science * 2000;
                float reputation = (float)(25 + science / 10);
                Funding.Instance?.AddFunds(funds, TransactionReasons.Progression);
                Reputation.Instance?.AddReputation(reputation, TransactionReasons.Progression);
                text += "\n" + Localizer.Format("#Subterra_Centre_Funds", funds.ToString("N0"));
                text += "\n" + Localizer.Format("#Subterra_Centre_Reputation", reputation.ToString("N0"));
            }

            return text;
        }
    }
}
