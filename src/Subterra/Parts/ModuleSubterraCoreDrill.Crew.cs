using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Drills that bore one hole together (Lucas: the more Drill-O-Matic Juniors of one vessel join a hole, the faster it goes
    /// down). The drilling drills of a vessel whose bits are within <see cref="CrewReach"/> of each other form a crew: the
    /// first of them (by flight id) leads and bores its hole at the sum of the crew's rates; the others take its hole as
    /// theirs (so any of them can go on with it, load it or sample it) and draw their own power. Linear in the number of
    /// drills. [Claude, revisar]
    /// </summary>
    public sealed partial class ModuleSubterraCoreDrill
    {
        /// <summary>Metres between the bits of drills that bore one hole together, across and up or down.</summary>
        private const double CrewReach = 8;

        /// <summary>Seconds a drill counts as powered after its last powered physics step.</summary>
        private const float PoweredFor = 0.25f;

        // Last physics time this drill drilled with power, and whether it is helping another drill's hole now.
        private float poweredAt = -1;
        private bool assisting;

        private bool InCrew => BoreholeOn && drilling && !sideways && !horizontal && !retracted && vessel != null && vessel.Landed
            && poweredAt >= 0 && Time.fixedTime - poweredAt <= PoweredFor;

        // The drilling drills of the vessel, by flight id.
        private List<ModuleSubterraCoreDrill> Crewmates()
        {
            List<ModuleSubterraCoreDrill> all = vessel.FindPartModulesImplementing<ModuleSubterraCoreDrill>();
            all.RemoveAll(d => d != this && !d.InCrew);
            all.Sort((a, b) => a.part.flightID.CompareTo(b.part.flightID));
            return all;
        }

        /// <summary>The drill whose hole this one bores: itself, or the first drilling drill of the vessel near it that leads.</summary>
        private ModuleSubterraCoreDrill Leader(CelestialBody body) => LeaderOf(this, Crewmates(), body);

        // Leaders are picked in flight-id order: a drill joins the first leader near it, or leads itself.
        private static ModuleSubterraCoreDrill LeaderOf(ModuleSubterraCoreDrill drill, List<ModuleSubterraCoreDrill> crew, CelestialBody body)
        {
            var leaders = new List<ModuleSubterraCoreDrill>();
            foreach (ModuleSubterraCoreDrill d in crew)
            {
                ModuleSubterraCoreDrill lead = leaders.Find(l => Near(body, l.BitPosition, d.BitPosition));
                if (lead == null)
                {
                    leaders.Add(d);
                }

                if (d == drill)
                {
                    return lead ?? d;
                }
            }

            return drill;
        }

        private static bool Near(CelestialBody body, Vector3d a, Vector3d b)
        {
            Vector3d up = (a - body.position).normalized;
            Vector3d delta = b - a;
            double vertical = Vector3d.Dot(delta, up);
            return System.Math.Abs(vertical) <= CrewReach && (delta - up * vertical).magnitude <= CrewReach;
        }

        /// <summary>How many drills bore this one's hole (itself included) and their summed rate (m/s in rock of hardness 0).</summary>
        private int CrewRate(CelestialBody body, out double rate)
        {
            rate = baseRate;
            int count = 1;
            List<ModuleSubterraCoreDrill> crew = Crewmates();
            foreach (ModuleSubterraCoreDrill d in crew)
            {
                if (d != this && LeaderOf(d, crew, body) == this)
                {
                    rate += d.baseRate;
                    count++;
                }
            }

            return count;
        }

        // Helping the leader: its hole becomes this drill's (so it can go on with it if the leader stops), power drawn, no boring.
        private void Assist(CelestialBody body, ModuleSubterraCoreDrill lead, double lat, double lon)
        {
            holeBody = lead.holeBody;
            holeLat = lead.holeLat;
            holeLon = lead.holeLon;
            depth = lead.depth;
            holeTop = lead.holeTop;
            reachedCaves = lead.reachedCaves;
            gasChecked = lead.gasChecked;
            gasResource = lead.gasResource;
            gasLeft = lead.gasLeft;
            holeOffset = Distance(body, lat, lon, holeLat, holeLon);
            Layer = lead.Layer;
            uiDepth = lead.uiDepth;
            uiLayer = lead.uiLayer;
            uiRockTemp = lead.uiRockTemp;
            uiRate = lead.uiRate;
            uiStatus = Localizer.Format("#Subterra_Drill_Assisting", lead.part.partInfo?.title ?? lead.part.name);
            assisting = true;
            working = true;
        }

        // The leader met rock it cannot bore: its helpers stop too.
        private void StopCrew(CelestialBody body)
        {
            List<ModuleSubterraCoreDrill> crew = Crewmates();
            foreach (ModuleSubterraCoreDrill d in crew)
            {
                if (d != this && LeaderOf(d, crew, body) == this)
                {
                    d.drilling = false;
                    d.assisting = false;
                    d.UpdateEvents();
                }
            }
        }
    }
}
