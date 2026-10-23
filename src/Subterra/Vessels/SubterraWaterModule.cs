using System;
using KSP.Localization;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using Subterra.Core.Water;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Cave water on a loaded vessel (sub-project 4): each part under a cave's water level is lifted (the water its box
    /// displaces), slowed (quadratic drag) and, past its <c>maxPressure</c>, crushed. KSP's own buoyancy only knows the
    /// body's sea; this is for the water in Subterra's caves.
    /// </summary>
    public sealed class SubterraWaterModule : VesselModule
    {
        /// <summary>How often (s) the vessel looks for a cave with water around it.</summary>
        private const float LookInterval = 0.5f;

        /// <summary>A suited kerbal's density (kg/m³).</summary>
        private const double SuitDensity = 800;

        private float nextLook;
        private TubeNetwork net;
        private readonly System.Collections.Generic.List<Part> crushed = new System.Collections.Generic.List<Part>();

        /// <summary>Parts under water now (for tests and the debug window).</summary>
        public int Submerged { get; private set; }

        /// <summary>Deepest part under the water level (m), or 0.</summary>
        public double DeepestDepth { get; private set; }

        public override Activation GetActivation() => Activation.LoadedVessels | Activation.FlightScene;

        public static SubterraWaterModule Of(Vessel vessel) => vessel == null ? null : vessel.FindVesselModuleImplementing<SubterraWaterModule>();

        private void FixedUpdate()
        {
            Submerged = 0;
            DeepestDepth = 0;
            if (vessel == null || !vessel.loaded || vessel.packed || !HighLogic.LoadedSceneIsFlight || !Settings.Current.WaterPhysics)
            {
                return;
            }

            CelestialBody body = vessel.mainBody;
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + LookInterval;
                net = Underground.Locate(body, vessel.CoMD, out TubeNetwork found, out _) && found.Waters.Count > 0 ? found : null;
            }

            if (net == null)
            {
                return;
            }

            using (SubterraClock.Measure("SubterraWater"))
            {
                crushed.Clear();
                double dt = TimeWarp.fixedDeltaTime;
                foreach (Part part in vessel.parts)
                {
                    Rigidbody rb = part.Rigidbody;
                    if (rb == null || part.physicalSignificance != Part.PhysicalSignificance.FULL)
                    {
                        continue;
                    }

                    Vector3d world = rb.worldCenterOfMass;
                    Vec3d bodyPos = Frames.ToBody(body, world);
                    Vector3 size = part.DragCubes != null && part.DragCubes.Cubes.Count > 0 ? part.DragCubes.Cubes[0].Size : Vector3.one;
                    if (size.sqrMagnitude < 1e-6f)
                    {
                        size = Vector3.one;
                    }

                    float height = (size.x + size.y + size.z) / 3;
                    WaterSample? sample = net.WaterAt(net.ToLocal(bodyPos), margin: 3f, above: height);
                    if (sample == null)
                    {
                        continue;
                    }

                    double fraction = WaterPhysics.SubmergedFraction(sample.Value.Depth, height);
                    if (fraction <= 0)
                    {
                        continue;
                    }

                    Submerged++;
                    DeepestDepth = Math.Max(DeepestDepth, sample.Value.Depth);
                    double density = sample.Value.Density;
                    double g = FlightGlobals.getGeeForceAtPosition(world).magnitude;
                    Vector3d up = Frames.DirectionToWorld(body, sample.Value.Up).normalized;

                    // Lift: the water the part's box displaces (kN: KSP's forces are in kilonewtons, masses in tonnes).
                    double volume = size.x * size.y * size.z * WaterPhysics.FillFactor * part.buoyancy;
                    if (part.isKerbalEVA())
                    {
                        // A kerbal in a suit (air in it) is lighter than water, as in KSP's own sea: it floats. [Claude, revisar]
                        volume = rb.mass * 1000 / SuitDensity;
                    }

                    Vector3d force = up * (WaterPhysics.Buoyancy(volume * fraction, density, g) / 1000);

                    // Drag against the part's motion through the water, never more than stops it in one step.
                    Vector3d velocity = vessel.srf_velocity + (Vector3d)(rb.velocity - vessel.rootPart.Rigidbody.velocity);
                    double speed = velocity.magnitude;
                    if (speed > 1e-3)
                    {
                        double area = (size.x * size.y + size.y * size.z + size.x * size.z) / 3 * fraction;
                        double drag = Math.Min(WaterPhysics.Drag(speed, area, density) / 1000, rb.mass * speed / dt);
                        force -= velocity / speed * drag;
                    }

                    rb.AddForce(force, ForceMode.Force);

                    // Crushed past its rated pressure (the air on the water's surface plus the water above it).
                    double kPa = WaterPhysics.PressureKPa(sample.Value.Depth, density, g, vessel.staticPressurekPa);
                    if (kPa > part.maxPressure)
                    {
                        ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Water_Crushed", part.partInfo.title, (kPa / 101.325).ToString("F0")), 5f);
                        Log.Info($"{part.partInfo.title} on {vessel.GetDisplayName()} crushed at {sample.Value.Depth:F0} m of water ({kPa:F0} kPa > {part.maxPressure:F0})");
                        crushed.Add(part);
                    }
                }

                // After the loop: a part that goes takes itself out of vessel.parts.
                foreach (Part part in crushed)
                {
                    part.explode();
                }
            }
        }
    }
}
