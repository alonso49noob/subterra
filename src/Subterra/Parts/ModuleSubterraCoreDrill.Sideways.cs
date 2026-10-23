using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;
using Subterra.Core.Excavation;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The core drill mounted on its side (its bit within 45° of level): it drills a horizontal blast hole into a rock face
    /// (a cliff, a cave or tunnel wall) up to <see cref="maxRound"/> m, then loads and fires it as a round of
    /// drill-and-blast tunnelling: a tunnel of <see cref="blastRadius"/> opens along the hole (Lucas: the blast drill
    /// should work horizontally too).
    /// </summary>
    public sealed partial class ModuleSubterraCoreDrill
    {
        /// <summary>
        /// The drill bit's direction in the part's own axes (the stock MiniDrill's bit comes out along -Y, set in its cfg); zero:
        /// the module only drills straight down.
        /// </summary>
        [KSPField] public Vector3 drillAxis = Vector3.zero;

        /// <summary>Longest horizontal hole (m): one round. [Claude, revisar]</summary>
        [KSPField] public float maxRound = 12f;

        [KSPField(isPersistant = true)] public bool sideways;
        [KSPField(isPersistant = true)] public string roundBody = "";
        [KSPField(isPersistant = true)] public double roundX, roundY, roundZ, roundDirX, roundDirY, roundDirZ;
        [KSPField(isPersistant = true)] public double roundLength;
        [KSPField(isPersistant = true)] public double roundKg;
        [KSPField(isPersistant = true)] public bool roundArmed;

        /// <summary>Sine of the steepest bit that still drills sideways (45°).</summary>
        private const double SidewaysSine = 0.7071;

        private Vec3d RoundFloor => new Vec3d(roundX, roundY, roundZ);

        private Vec3d RoundDir => new Vec3d(roundDirX, roundDirY, roundDirZ);

        // True when the drill is mounted on its side: the sideways hole is worked here instead of the vertical one.
        private bool DrillSideways(double dt, BodyRuntime runtime, CelestialBody body)
        {
            if (drillAxis == Vector3.zero)
            {
                sideways = false;
                return false;
            }

            Vector3d axis = part.transform.TransformDirection(drillAxis.normalized);
            Vector3d upWorld = (part.transform.position - body.position).normalized;
            sideways = Math.Abs(Vector3d.Dot(axis, upWorld)) < SidewaysSine;
            if (!sideways)
            {
                return false;
            }

            Vector3d mouth = part.transform.position + axis * 0.5;
            Vec3d mouthBody = Frames.ToBody(body, mouth);
            Vec3d up = mouthBody.Normalized;
            Vec3d dir = Frames.ToBody(body, mouth + axis) - mouthBody;
            dir = (dir - up * Vec3d.Dot(dir, up)).Normalized;

            // A new hole when the drill has moved (or turned) away from the last one.
            Vec3d floor = FloorUnder(body, mouth, up);
            if (roundBody != body.bodyName || (floor - RoundFloor).Length > HoleTolerance || Vec3d.Dot(dir, RoundDir) < 0.95)
            {
                if (roundArmed)
                {
                    // A loaded round stays where it was drilled: only its own detonation clears it.
                    uiStatus = Localizer.Format("#Subterra_Drill_RoundLoaded");
                    return true;
                }

                roundBody = body.bodyName;
                roundX = floor.X;
                roundY = floor.Y;
                roundZ = floor.Z;
                roundDirX = dir.X;
                roundDirY = dir.Y;
                roundDirZ = dir.Z;
                roundLength = 0;
            }

            uiDepth = (float)roundLength;
            BodyFrame.ToLatLon(up, out double lat, out double lon);
            string biome = KspSampler.Biome(body, lat, lon);
            double depthHere = Math.Max(0, KspSampler.SurfaceRadius(body, lat, lon) - mouthBody.Length);
            LayerSpec layer = runtime.Strata.LayerAt(depthHere, biome);
            Layer = layer;
            uiLayer = layer == null ? "-" : string.IsNullOrEmpty(layer.DisplayName) ? layer.Name : Localizer.Format(layer.DisplayName);
            if (!drilling || retracted || roundArmed || !vessel.Landed)
            {
                uiStatus = Localizer.Format(roundArmed ? "#Subterra_Drill_RoundLoaded" : "#Subterra_Drill_Idle");
                return true;
            }

            if (roundLength >= maxRound - 0.01)
            {
                Finish("#Subterra_Drill_RoundReady");
                return true;
            }

            // Only rock ahead: not open air, not a cave.
            Vec3d tip = mouthBody + dir * (roundLength + 0.5);
            BodyFrame.ToLatLon(tip.Normalized, out double tipLat, out double tipLon);
            if (tip.Length > KspSampler.SurfaceRadius(body, tipLat, tipLon) - 0.2 || runtime.Catalog.FindContaining(tip) != null)
            {
                Finish(roundLength >= 2 ? "#Subterra_Drill_RoundReady" : "#Subterra_Drill_NoRock");
                return true;
            }

            double need = powerUse * dt;
            double got = need > 0 ? part.RequestResource("ElectricCharge", need) : 0;
            if (got < need * 0.99)
            {
                uiStatus = Localizer.Format("#Subterra_Drill_NoPower");
                return true;
            }

            double rate = DrillMath.Rate(baseRate, layer?.Hardness ?? 0.5, runtime.Geotherm.Temperature(depthHere, biome), maxRockTemp) * Settings.Current.DigSpeed;
            uiRate = (float)rate;
            if (rate <= 0)
            {
                Finish("#Subterra_Drill_TooHard");
                return true;
            }

            roundLength = Math.Min(maxRound, roundLength + rate * dt);
            working = true;
            uiStatus = Localizer.Format("#Subterra_Drill_Sideways");
            return true;
        }

        private void Finish(string why)
        {
            drilling = false;
            UpdateEvents();
            ScreenMessages.PostScreenMessage(Localizer.Format(why), 5f);
            uiStatus = Localizer.Format(why);
        }

        // The ground (or cave floor) under the drill: a ray down to the terrain or the cave's colliders.
        private Vec3d FloorUnder(CelestialBody body, Vector3d mouth, Vec3d up)
        {
            Vector3d down = -(mouth - body.position).normalized;
            if (Physics.Raycast(mouth, down, out RaycastHit hit, 30f, 1 << 15, QueryTriggerInteraction.Ignore))
            {
                return Frames.ToBody(body, hit.point);
            }

            return Frames.ToBody(body, mouth) - up * 1.5;
        }

        // Loads the sideways hole: explosive for the rock of a tunnel of the blast radius along it.
        private void LoadRound()
        {
            if (!BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime) || roundLength < 2)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_TooShallow"), 5f);
                return;
            }

            double kg = BlastDesign.PowderFactor(Layer) * Math.PI * blastRadius * blastRadius * roundLength;
            double have = vessel.parts.Sum(p => p.Resources.Cast<PartResource>().Where(r => r.resourceName == "Explosive").Sum(r => r.amount));
            if (have < kg)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_NeedExplosive", kg.ToString("F0"), have.ToString("F0")), 6f);
                return;
            }

            part.RequestResource("Explosive", kg);
            roundKg = kg;
            roundArmed = true;
            drilling = false;
            (double destroy, double damage) = BlastSafety.Radii(kg, true);
            ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_Loaded", 1, kg.ToString("F0"), destroy.ToString("F0"), damage.ToString("F0")),
                8f, ScreenMessageStyle.UPPER_CENTER);
            UpdateEvents();
        }

        private void DetonateRound()
        {
            CelestialBody body = FlightGlobals.GetBodyByName(roundBody);
            if (body == null || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                roundArmed = false;
                UpdateEvents();
                return;
            }

            Vector3d face = Frames.ToWorld(body, RoundFloor + RoundDir * (roundLength * 0.5));
            List<Vessel> inDanger = ExcavationService.VesselsInDanger(face, roundKg, true);
            if (inDanger.Count > 0)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_Danger", string.Join(", ", inDanger.Select(v => v.GetDisplayName()))), 6f, ScreenMessageStyle.UPPER_CENTER);
            }

            roundArmed = false;
            UpdateEvents();
            StartCoroutine(FuseRound(runtime, RoundFloor, RoundDir, roundLength, roundKg));
            roundLength = 0;
        }

        private System.Collections.IEnumerator FuseRound(BodyRuntime runtime, Vec3d floor, Vec3d dir, double length, double kg)
        {
            for (int s = Mathf.CeilToInt(fuseSeconds); s > 0; s--)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_Countdown", s), 1f, ScreenMessageStyle.UPPER_CENTER);
                yield return new WaitForSeconds(1f);
            }

            ExcavationService.BlastRound(runtime, floor, dir, length, blastRadius, kg);
        }
    }
}
