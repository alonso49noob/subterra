using System;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Water;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The floor of a body's ocean under the ice, seen and touched (Lucas: from the water, pockets carved in the rock under
    /// the floor floated in the dark, for the floor was never drawn; a sinking craft had nothing to land on). A patch around
    /// the active vessel, with a collider, while it is near the floor; made again as it moves on. It is the rock of the
    /// layer there, with the same iridescence as the cave walls at that depth and heat.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class SeaFloorRenderer : MonoBehaviour
    {
        /// <summary>Half the patch's side (m), its cells (m), and how far over the floor it is shown.</summary>
        // Wide enough that its edge is always past the fog (Lucas saw the edge of Minmus's floor), 10 m cells.
        private const float HalfSize = 900f, Cell = 10f, ShowHeight = 1500f;

        /// <summary>Move this far (m) from the patch's centre and a new one is made around the vessel.</summary>
        private const float Rebuild = 250f;

        private GameObject patch;
        private CelestialBody patchBody;
        private Vec3d patchCentre;
        private bool building;
        private int patchRevision = -1;
        private float nextLook;

        /// <summary>True while a floor patch is shown (for tests).</summary>
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

            // Near the floor: in the water within ShowHeight of it, or just under it (in a pocket carved below the floor).
            Vec3d at = Frames.ToBody(body, vessel.CoMD);
            Core.Geo.BodyFrame.ToLatLon(at.Normalized, out double lat, out _);
            double over = at.Length - runtime.Ocean.FloorRadius(lat);
            if (over > ShowHeight || over < -200 || at.Length > runtime.Ocean.CeilingRadius(lat))
            {
                Drop();
                return;
            }

            if (building || (patch != null && patchBody == body && patchRevision == ExcavationService.Revision && (at.Normalized * patchCentre.Length - patchCentre).Length < Rebuild))
            {
                return;
            }

            building = true;
            patchRevision = ExcavationService.Revision;
            OceanShell ocean = runtime.Ocean;
            Subterra.Core.Procedural.NetworkCatalog catalog = runtime.Catalog;
            ulong seed = Hash.Of(body.bodyName + "/seafloor");
            WorkerPool.Shared.Enqueue(() =>
            {
                MeshData data;
                Vec3d centre;
                try
                {
                    // Open where a cave, shaft or pocket goes through the floor (its void just over or under the surface).
                    data = SeaFloorMesher.Build(ocean, at, HalfSize, Cell, seed, out centre,
                        p => catalog.FindContaining(p + p.Normalized * 1.5) != null || catalog.FindContaining(p - p.Normalized * 1.5) != null);
                }
                catch (Exception e)
                {
                    Log.Error($"Sea floor failed: {e}");
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
            patch = new GameObject("SubterraSeaFloor") { layer = 15 };
            Mesh mesh = MeshUploader.Upload(prepared, "Subterra sea floor", readable: true);
            patch.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = patch.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = FloorMaterial(body, centre);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            patch.AddComponent<MeshCollider>().sharedMesh = mesh;
            patchBody = body;
            patchCentre = centre;
            patch.transform.position = Frames.ToWorld(body, centre);
            patch.transform.rotation = body.pqsController.transform.rotation;
            Shown = true;
        }

        // The rock of the layer at the floor's depth, iridescent as the caves are at that depth and heat.
        private static Material FloorMaterial(CelestialBody body, Vec3d centre)
        {
            if (!BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                return MaterialFactory.ForLayer(null, body);
            }

            double depth = Math.Max(0, body.Radius - centre.Length);
            LayerSpec layer = runtime.Strata.LayerAt(depth, "");
            return MaterialFactory.ForLayer(layer, body, runtime.Geotherm.Temperature(depth, ""), depth);
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
