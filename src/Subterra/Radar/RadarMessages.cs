using System.Collections.Generic;
using KSP.Localization;
using Subterra.Core.Radar;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Radar messages on screen without spam (spec 4.3 f): new hints are counted per instrument and body, and the
    /// first one opens a window of <see cref="GroupSeconds"/> of real time after which a single message gives the
    /// total, e.g. "Eco: 3 new hints on Mun". Main thread only; <see cref="Update"/> must be called every so often
    /// (the lazy detector does).
    /// </summary>
    public static class RadarMessages
    {
        public const float GroupSeconds = 10f;

        private static readonly List<Pending> pending = new List<Pending>();

        /// <summary>Counts <paramref name="count"/> new hints of the instrument on the body (a body name).</summary>
        public static void Hint(string body, int count, InstrumentKind kind)
        {
            if (count <= 0 || string.IsNullOrEmpty(body))
            {
                return;
            }

            foreach (Pending p in pending)
            {
                if (p.Body == body && p.Kind == kind)
                {
                    p.Count += count;
                    return;
                }
            }

            pending.Add(new Pending { Body = body, Kind = kind, Count = count, Due = Time.realtimeSinceStartup + GroupSeconds });
        }

        /// <summary>Shows the messages whose window has closed (all of them with <paramref name="flush"/>).</summary>
        public static void Update(bool flush = false)
        {
            float now = Time.realtimeSinceStartup;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending p = pending[i];
                if (!flush && now < p.Due)
                {
                    continue;
                }

                pending.RemoveAt(i);
                string text = Localizer.Format("#Subterra_Radar_NewHints", InstrumentName(p.Kind), p.Count, BodyName(p.Body));
                // A fixed English part for the logs and the in-game tests (the test copy may run in any language).
                Log.Info($"Radar: {p.Count} new hints on {p.Body} by {p.Kind}: \"{text}\"");
                ScreenMessages.PostScreenMessage(text, 6f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private static string InstrumentName(InstrumentKind kind)
        {
            switch (kind)
            {
                case InstrumentKind.Gravimeter:
                    return Localizer.Format("#Subterra_Radar_Name_Plomada");
                case InstrumentKind.GroundRadar:
                    return Localizer.Format("#Subterra_Radar_Name_Zahori");
                case InstrumentKind.Seismic:
                    return Localizer.Format("#Subterra_Radar_Name_Seismic");
                default:
                    return Localizer.Format("#Subterra_Radar_Name_Eco");
            }
        }

        private static string BodyName(string body)
        {
            CelestialBody cb = FlightGlobals.GetBodyByName(body);
            return cb != null ? cb.displayName.LocalizeRemoveGender() : body;
        }

        private sealed class Pending
        {
            public string Body;
            public InstrumentKind Kind;
            public int Count;
            public float Due;
        }
    }
}
