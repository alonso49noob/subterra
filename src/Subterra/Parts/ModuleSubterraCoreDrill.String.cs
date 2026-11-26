using System;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The drill string (Lucas: the Drill-O-Matic's model extended so it can reach kilometres down): a steel rod from the
    /// drill down its hole to the bottom, turning while it drills. Under the ground it is hidden by the rock, so it shows
    /// where the hole crosses a cave, and at the mouth.
    /// </summary>
    public sealed partial class ModuleSubterraCoreDrill
    {
        /// <summary>Radius (m) of the drill string.</summary>
        [KSPField] public float stringRadius = 0.08f;

        /// <summary>Depth (m) where the hole began (0 on the surface; a cave's floor depth from inside one).</summary>
        [KSPField(isPersistant = true)] public double holeTop;

        /// <summary>
        /// The model transform at the bit's tip (the stock Drill-O-Matics' "ImpactTransform", which moves out with the deploy
        /// animation). The hole starts under it and the drill string comes out of it (Lucas: the string came out beside the
        /// stock drill's bit, from the part's origin). Empty or missing: the part's origin.
        /// </summary>
        [KSPField] public string bitTransform = "ImpactTransform";

        /// <summary>Metres from the hole's axis at which this drill joined another drill's hole (0: it bored the hole itself).</summary>
        [KSPField(isPersistant = true)] public double holeOffset;

        /// <summary>
        /// Metres the bit may stand above the hole's top or below its bottom and still be on that hole. Lucas: put down deep
        /// under the same spot (Set Position), the drill took up the hole bored from the surface there, and its string ran
        /// up from the drill to that hole's bottom, near the ground.
        /// </summary>
        private const double HoleReach = 15;

        private GameObject drillString;
        private float stringSpin;
        private Transform bit;
        private bool bitSearched;

        /// <summary>The bit's tip in world space (the part's origin if the model has no such transform).</summary>
        internal Vector3d BitPosition
        {
            get
            {
                if (!bitSearched)
                {
                    bitSearched = true;
                    bit = string.IsNullOrEmpty(bitTransform) ? null : part.FindModelTransform(bitTransform);
                }

                return bit != null ? bit.position : part.transform.position;
            }
        }

        /// <summary>Depth (m) of a point under the terrain straight above it (0 over the ground).</summary>
        private static double DepthOf(CelestialBody body, Vector3d world, double lat, double lon) =>
            Math.Max(0, KspSampler.SurfaceRadius(body, lat, lon) - (world - body.position).magnitude);

        /// <summary>Depth (m) of the bit under the terrain straight above it (0 over the ground).</summary>
        private double BitDepth(CelestialBody body)
        {
            Vector3d world = BitPosition;
            return DepthOf(body, world, body.GetLatitude(world), body.GetLongitude(world));
        }

        /// <summary>True if the bit, at that place and depth, is on the hole this drill bores (over its axis, between its top and bottom).</summary>
        private bool OnHole(CelestialBody body, double lat, double lon, double bitDepth) =>
            depth >= 0 && holeBody == body.bodyName && Distance(body, lat, lon, holeLat, holeLon) <= Math.Max(HoleTolerance, holeOffset + 1)
            && bitDepth >= holeTop - HoleReach && bitDepth <= depth + HoleReach;

        private void LateUpdate()
        {
            bool show = HighLogic.LoadedSceneIsFlight && vessel != null && vessel.loaded && BoreholeOn && !sideways && !horizontal && !retracted
                && !assisting && depth - holeTop > 0.5 && holeBody == vessel.mainBody.bodyName;
            if (show)
            {
                CelestialBody body = vessel.mainBody;
                Vector3d at = BitPosition;
                double lat = body.GetLatitude(at), lon = body.GetLongitude(at);
                show = OnHole(body, lat, lon, DepthOf(body, at, lat, lon));
            }

            if (!show)
            {
                if (drillString != null && drillString.activeSelf)
                {
                    drillString.SetActive(false);
                }

                return;
            }

            if (drillString == null)
            {
                drillString = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                drillString.name = "SubterraDrillString";
                Destroy(drillString.GetComponent<Collider>());
                Shader shader = Shader.Find("KSP/Specular") ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Standard");
                var renderer = drillString.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = new Material(shader) { color = new Color(0.55f, 0.56f, 0.58f) };
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            CelestialBody b = vessel.mainBody;
            Vector3d top = BitPosition;
            Vec3d dir = BodyFrame.Direction(holeLat, holeLon);
            double surface = KspSampler.SurfaceRadius(b, holeLat, holeLon);
            Vector3d bottom = Frames.ToWorld(b, dir * (surface - depth));
            Vector3 along = (Vector3)(bottom - top);
            float length = along.magnitude;
            if (length < 0.1f)
            {
                drillString.SetActive(false);
                return;
            }

            if (working)
            {
                stringSpin = (stringSpin + 360f * Time.deltaTime) % 360f;
            }

            // A Unity cylinder is 2 m tall and 1 m wide, along its own up axis.
            drillString.transform.position = (Vector3)top + along * 0.5f;
            drillString.transform.rotation = Quaternion.FromToRotation(Vector3.up, along / length) * Quaternion.Euler(0, stringSpin, 0);
            drillString.transform.localScale = new Vector3(stringRadius * 2, length * 0.5f, stringRadius * 2);
            drillString.layer = part.gameObject.layer;
            drillString.SetActive(true);
        }

        private void OnDestroy()
        {
            if (drillString != null)
            {
                Destroy(drillString.GetComponent<MeshRenderer>().sharedMaterial);
                Destroy(drillString);
            }
        }
    }
}
