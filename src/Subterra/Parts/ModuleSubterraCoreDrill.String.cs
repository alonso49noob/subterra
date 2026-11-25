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

        private GameObject drillString;
        private float stringSpin;

        private void LateUpdate()
        {
            bool show = HighLogic.LoadedSceneIsFlight && vessel != null && vessel.loaded && BoreholeOn && !sideways && !horizontal && !retracted
                && depth - holeTop > 0.5 && holeBody == vessel.mainBody.bodyName;
            if (show)
            {
                CelestialBody body = vessel.mainBody;
                double lat = body.GetLatitude(part.transform.position), lon = body.GetLongitude(part.transform.position);
                show = Distance(body, lat, lon, holeLat, holeLon) <= HoleTolerance;
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
            Vector3d top = part.transform.position;
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
