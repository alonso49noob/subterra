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

        /// <summary>Longest horizontal hole (m): one round (Lucas: long enough to blast deep vaults). [Claude, revisar]</summary>
        [KSPField] public float maxRound = 40f;

        /// <summary>
        /// Drill horizontally whatever the mount (Lucas: the blast tool should work sideways too, to make very deep vaults): the
        /// bit swings level and drills toward <see cref="blastHeading"/>, degrees from the vessel's nose. Mounted on its side,
        /// the drill also drills sideways, toward its bit.
        /// </summary>
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Drill_Horizontal")]
        [UI_Toggle(disabledText = "#Subterra_Off", enabledText = "#Subterra_On")]
        public bool horizontal;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#Subterra_Drill_Heading", guiUnits = "°")]
        [UI_FloatRange(minValue = -180f, maxValue = 180f, stepIncrement = 5f)]
        public float blastHeading;

        // The longest round now: with KSP's infinite propellant cheat (free blasting) five times longer.
        private double MaxRoundNow => FreeBlasting ? maxRound * 5 : maxRound;

        [KSPField(isPersistant = true)] public bool sideways;
        [KSPField(isPersistant = true)] public string roundBody = "";
        [KSPField(isPersistant = true)] public double roundX, roundY, roundZ, roundDirX, roundDirY, roundDirZ;
        [KSPField(isPersistant = true)] public double roundLength;

        /// <summary>Metres of open space (a cave, a pocket) between the drill and the rock face where its hole starts.</summary>
        [KSPField(isPersistant = true)] public double roundOffset;

        /// <summary>Farthest (m) a sideways hole starts from the drill, across open space to the face.</summary>
        private const double MaxReachToFace = 20;
        [KSPField(isPersistant = true)] public double roundKg;
        [KSPField(isPersistant = true)] public bool roundArmed;

        /// <summary>
        /// Sine of the steepest bit that still drills sideways on its own (20° from level): a drill clearly mounted on its side.
        /// It was 45°, and a rover tipped on the rubble at the bottom of a shaft drilled sideways instead of down (Lucas could
        /// not start another hole from the bottom of one). The <see cref="horizontal"/> toggle drills sideways at any tilt.
        /// </summary>
        private const double SidewaysSine = 0.342;

        private Vec3d RoundFloor => new Vec3d(roundX, roundY, roundZ);

        private Vec3d RoundDir => new Vec3d(roundDirX, roundDirY, roundDirZ);

        // True when the drill is mounted on its side: the sideways hole is worked here instead of the vertical one.
        private bool DrillSideways(double dt, BodyRuntime runtime, CelestialBody body)
        {
            Vector3d upWorld = (part.transform.position - body.position).normalized;
            Vector3d axis;
            if (horizontal)
            {
                // Level, toward the heading from the vessel's nose.
                Vector3 nose = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, (Vector3)upWorld);
                if (nose.sqrMagnitude < 1e-4f)
                {
                    nose = Vector3.ProjectOnPlane(-vessel.ReferenceTransform.forward, (Vector3)upWorld);
                }

                axis = Quaternion.AngleAxis(blastHeading, (Vector3)upWorld) * nose.normalized;
                sideways = true;
            }
            else if (drillAxis != Vector3.zero)
            {
                axis = part.transform.TransformDirection(drillAxis.normalized);
                sideways = Math.Abs(Vector3d.Dot(axis, upWorld)) < SidewaysSine;
            }
            else
            {
                sideways = false;
                return false;
            }
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
                roundOffset = -1;
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
                uiStatus = Localizer.Format(roundArmed ? "#Subterra_Drill_RoundLoaded" : "#Subterra_Drill_IdleSideways");
                return true;
            }

            if (roundLength >= MaxRoundNow - 0.01)
            {
                Finish("#Subterra_Drill_RoundReady");
                return true;
            }

            // Only rock ahead: not open air, not a cave.
            // From inside a cave or pocket, the hole starts at the rock face ahead (the bit reaches across to it).
            if (roundOffset < 0)
            {
                roundOffset = 0;
                while (roundOffset < MaxReachToFace && runtime.Catalog.FindContaining(mouthBody + dir * (roundOffset + 0.5)) != null)
                {
                    roundOffset += 0.5;
                }

                if (roundOffset >= MaxReachToFace)
                {
                    roundOffset = -1;
                    Finish("#Subterra_Drill_NoRock");
                    return true;
                }
            }

            Vec3d tip = mouthBody + dir * (roundOffset + roundLength + 0.5);
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

            roundLength = Math.Min(MaxRoundNow, roundLength + rate * dt);
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
            if (!FreeBlasting)
            {
                double have = vessel.parts.Sum(p => p.Resources.Cast<PartResource>().Where(r => r.resourceName == "Explosive").Sum(r => r.amount));
                if (have < kg)
                {
                    ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_NeedExplosive", kg.ToString("F0"), have.ToString("F0")), 6f);
                    return;
                }

                part.RequestResource("Explosive", kg);
            }
            roundKg = kg;
            roundArmed = true;
            Log.Info($"Round loaded on {vessel.GetDisplayName()}: {roundLength:F1} m from {Math.Max(0, roundOffset):F1} m ahead, {kg:F0} kg, radius {blastRadius:F1} m");
            drilling = false;
            (double destroy, double damage) = BlastSafety.Radii(ExcavationService.RoundDelayKg(kg, roundLength), true);
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

            Vector3d face = Frames.ToWorld(body, RoundFloor + RoundDir * (Math.Max(0, roundOffset) + roundLength * 0.5));
            List<Vessel> inDanger = ExcavationService.VesselsInDanger(face, ExcavationService.RoundDelayKg(roundKg, roundLength), true);
            if (inDanger.Count > 0)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Drill_Danger", string.Join(", ", inDanger.Select(v => v.GetDisplayName()))), 6f, ScreenMessageStyle.UPPER_CENTER);
            }

            roundArmed = false;
            UpdateEvents();
            StartCoroutine(FuseRound(runtime, RoundFloor + RoundDir * Math.Max(0, roundOffset), RoundDir, roundLength, roundKg));
            roundLength = 0;
            roundOffset = -1;
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
