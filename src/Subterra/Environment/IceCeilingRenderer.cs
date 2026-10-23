using System;
using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using Subterra.Core.Water;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The ice over a body's ocean, seen and touched from below (sub-project 4): a patch of its underside around the
    /// active vessel, with a collider, while the vessel is in the ocean near it; made again as the vessel moves on.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class IceCeilingRenderer : MonoBehaviour
    {
        /// <summary>Half the patch's side (m), its cells (m), and how deep under the ice it is shown.</summary>
        private const float HalfSize = 300f, Cell = 8f, ShowDepth = 1500f;

        /// <summary>Move this far (m) from the patch's centre and a new one is made around the vessel.</summary>
        private const float Rebuild = 120f;

        private GameObject patch;
        private CelestialBody patchBody;
        private Vec3d patchCentre;
        private bool building;
        private float nextLook;

        /// <summary>True while a ceiling patch is shown (for tests).</summary>
        public static bool Shown { get; private set; }

        private void LateUpdate()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (patch != null && patchBody != null)
            {
                // Kept in the body's frame (the floating origin moves the world under it).
                patch.transform.position = Frames.ToWorld(patchBody, patchCentre);
                patch.transform.rotation = patchBody.pqsController.transform.rotation;
            }

            if (vessel == null || Time.time < nextLook)
            {
                return;
            }

            nextLook = Time.time + 0.5f;
            CelestialBody body = vessel.mainBody;
            if (!Settings.Current.WaterPhysics || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) || runtime.Ocean == null)
            {
                Drop();
                return;
            }

            Vec3d at = Frames.ToBody(body, vessel.CoMD);
            WaterSample? sea = runtime.Ocean.At(at - at.Normalized * 30);
            if (sea == null || sea.Value.Depth > ShowDepth)
            {
                Drop();
                return;
            }

            if (building || (patch != null && patchBody == body && (at.Normalized * patchCentre.Length - patchCentre).Length < Rebuild))
            {
                return;
            }

            building = true;
            OceanShell ocean = runtime.Ocean;
            ulong seed = Hash.Of(body.bodyName + "/ice");
            WorkerPool.Shared.Enqueue(() =>
            {
                MeshData data;
                Vec3d centre;
                try
                {
                    data = IceCeilingMesher.Build(ocean, at, HalfSize, Cell, seed, out centre);
                }
                catch (Exception e)
                {
                    Log.Error($"Ice ceiling failed: {e}");
                    building = false;
                    return;
                }

                PreparedMesh prepared = MeshUploader.Prepare(data);
                WorkerPool.Shared.RunOnMain(() => Show(body, centre, prepared));
            });
        }

        private void Show(CelestialBody body, Vec3d centre, PreparedMesh prepared)
        {
            building = false;
            Drop();
            patch = new GameObject("SubterraIceCeiling") { layer = 15 };
            Mesh mesh = MeshUploader.Upload(prepared, "Subterra ice ceiling", readable: true);
            patch.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = patch.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFactory.Create("Ice_d", "Ice_n", new Color(0.62f, 0.72f, 0.80f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            patch.AddComponent<MeshCollider>().sharedMesh = mesh;
            patchBody = body;
            patchCentre = centre;
            patch.transform.position = Frames.ToWorld(body, centre);
            patch.transform.rotation = body.pqsController.transform.rotation;
            Shown = true;
        }

        private void Drop()
        {
            Shown = false;
            if (patch != null)
            {
                Destroy(patch);
                patch = null;
            }
        }

        private void OnDestroy() => Drop();
    }
}
