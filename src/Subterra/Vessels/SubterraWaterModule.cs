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

        /// <summary>A swimming kerbal: depth (m) of his middle under the surface where he floats, head out.</summary>
        private const double FloatDepth = 0.3;

        /// <summary>A swimming kerbal: rise speed (m/s) per metre under his float depth, and its limit.</summary>
        private const double RiseRate = 1.0, MaxRise = 2.0;

        private float nextLook;
        private TubeNetwork net;
        private Subterra.Core.Water.OceanShell ocean;
        private readonly System.Collections.Generic.List<Part> crushed = new System.Collections.Generic.List<Part>();

        /// <summary>Parts under water now (for tests and the debug window).</summary>
        public int Submerged { get; private set; }

        /// <summary>Deepest part under the water level (m), or 0.</summary>
        public double DeepestDepth { get; private set; }

        // The parts in Subterra's water this physics frame, with the share of each under it.
        private readonly System.Collections.Generic.Dictionary<Part, double> wet = new System.Collections.Generic.Dictionary<Part, double>();

        /// <summary>
        /// Whether <paramref name="part"/> is in a cave's or an ocean's water now, and how much of it. Its WaterContact is set
        /// from this, so KSP counts the vessel as splashed and a kerbal swims (CaveSeaPatch keeps KSP's own sea from
        /// clearing it).
        /// </summary>
        public bool InWater(Part part, out double fraction) => wet.TryGetValue(part, out fraction);

        public override Activation GetActivation() => Activation.LoadedVessels | Activation.FlightScene;

        public static SubterraWaterModule Of(Vessel vessel) => vessel == null ? null : vessel.FindVesselModuleImplementing<SubterraWaterModule>();

        // The ocean's water at a part, its depth counted from the ceiling (a part just under the ice is partly in it).
        private WaterSample? OceanSample(Vec3d bodyPos, float height)
        {
            WaterSample? lower = ocean.At(bodyPos - bodyPos.Normalized * height);
            return lower == null ? (WaterSample?)null : new WaterSample(lower.Value.Depth - height, lower.Value.Density, lower.Value.Up, lower.Value.Colour);
        }

        private void FixedUpdate()
        {
            bool wasWet = wet.Count > 0;
            Submerged = 0;
            DeepestDepth = 0;
            foreach (Part dry in wet.Keys)
            {
                if (dry != null)
                {
                    dry.WaterContact = false;
                }
            }

            wet.Clear();
            try
            {
                Float();
            }
            finally
            {
                MarkSplashed(wasWet);
            }
        }

        // KSP does not work vessel.Splashed out every frame: its sea code sets it when a part goes in (and re-checks the
        // parts when one comes out), and that code is skipped in Subterra's water (CaveSeaPatch). Without it a kerbal in a
        // cave's water was never "splashed": he fell, hit the floor and lay there as a ragdoll instead of swimming (Lucas).
        private void MarkSplashed(bool wasWet)
        {
            if (vessel == null || !vessel.loaded)
            {
                return;
            }

            if (wet.Count > 0)
            {
                vessel.Splashed = true;
                vessel.Landed = false;
            }
            else if (wasWet)
            {
                vessel.checkSplashed();
            }
        }

        private void Float()
        {
            if (vessel == null || !vessel.loaded || vessel.packed || !HighLogic.LoadedSceneIsFlight || !Settings.Current.WaterPhysics)
            {
                return;
            }

            CelestialBody body = vessel.mainBody;
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + LookInterval;
                net = Underground.Locate(body, vessel.CoMD, out TubeNetwork found, out _) && found.Waters.Count > 0 ? found : null;
                ocean = null;
                if (net == null && BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) && runtime.Ocean != null)
                {
                    // Near or in the ocean: its ceiling within 50 m above the vessel.
                    Vec3d at = Frames.ToBody(body, vessel.CoMD);
                    ocean = runtime.Ocean.At(at - at.Normalized * 50) != null ? runtime.Ocean : null;
                }
            }

            if (net == null && ocean == null)
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
                    WaterSample? sample = net != null ? net.WaterAt(net.ToLocal(bodyPos), margin: 3f, above: height) : OceanSample(bodyPos, height);
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
                    wet[part] = fraction;
                    part.WaterContact = true;
                    part.submergedPortion = fraction;
                    double density = sample.Value.Density;
                    double g = FlightGlobals.getGeeForceAtPosition(world).magnitude;
                    Vector3d up = Frames.DirectionToWorld(body, sample.Value.Up).normalized;

                    // Lift: the water the part's box displaces (kN: KSP's forces are in kilonewtons, masses in tonnes).
                    double volume = size.x * size.y * size.z * WaterPhysics.FillFactor * part.buoyancy;
                    // part.isKerbalEVA() did not find kerbals in Retooled (Jeb sank): the EVA module is looked for itself.
                    if (vessel.isEVA || part.FindModuleImplementing<KerbalEVA>() != null)
                    {
                        // A kerbal in a suit (air in it) is lighter than water, as in KSP's own sea: it floats. [Claude, revisar]
                        // His whole mass (the rigidbody's carries his kit; the part's can be less).
                        volume = Math.Max(rb.mass, part.mass + part.GetResourceMass()) * 1000 / SuitDensity;
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

                    KerbalEVA eva = part.FindModuleImplementing<KerbalEVA>();
                    if (eva != null && eva.isRagdoll && eva.ragdollNodes != null && eva.ragdollNodes.Length > 0)
                    {
                        // A ragdoll kerbal moves by his limbs' bodies, not his part's: the same lift and drag, as an
                        // acceleration, on each limb. KSP floats a ragdoll only in its own sea (below sea level), where
                        // its lift is already on the limb.
                        Vector3 accel = force / Math.Max(rb.mass, 1e-3);
                        foreach (KerbalRagdollNode node in eva.ragdollNodes)
                        {
                            if (node?.rb == null || (body.ocean && FlightGlobals.getAltitudeAtPos(node.go.transform.position, body) < 0))
                            {
                                continue;
                            }

                            node.rb.AddForce(accel, ForceMode.Acceleration);
                        }
                    }
                    else if (eva != null && eva.fsm != null && (eva.fsm.CurrentState == eva.st_swim_idle || eva.fsm.CurrentState == eva.st_swim_fwd))
                    {
                        // Swimming, KSP sets the kerbal's velocity itself every physics step (the way he swims, nothing
                        // up or down): a lift force only moved him a few centimetres a second, and Jeb stayed 19 m down.
                        // Added as a velocity change on top of KSP's, he rises to the surface and floats there with his
                        // head out. [Claude, revisar]
                        double rise = Math.Max(-MaxRise, Math.Min(MaxRise, (sample.Value.Depth - FloatDepth) * RiseRate));
                        rb.AddForce(up * rise, ForceMode.VelocityChange);
                    }
                    else
                    {
                        rb.AddForce(force, ForceMode.Force);
                    }

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
