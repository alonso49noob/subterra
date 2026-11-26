using System.Collections;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The map view with the active vessel underground (Lucas: a mess from Vall's ocean). The map camera started on the
    /// vessel, inside the planet, which is not drawn from inside; and KSP drew the "orbit" of a craft that was neither
    /// landed nor splashed (falling in a shaft, just set there): a trajectory through the inside of the body. Opening the
    /// map underground now looks at the body from outside, and that trajectory is not drawn while the craft is underground.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class UndergroundMap : MonoBehaviour
    {
        /// <summary>Distance of the map camera from the body, in body radii.</summary>
        private const double FocusRadii = 3.5;

        private bool wasMap;
        private PatchedConicRenderer hidden;

        /// <summary>Whether the vessel is under the ground: in a cave or pocket, in an ocean under the ice, or buried.</summary>
        public static bool IsUnderground(Vessel vessel) =>
            vessel != null && vessel.mainBody != null
            && (UndergroundTracker.For(vessel).Inside || Underground.OceanAt(vessel.mainBody, vessel.CoMD) != null || Underground.BuriedDepth(vessel) > 3);

        private void LateUpdate()
        {
            bool map = MapView.MapIsEnabled;
            Vessel vessel = FlightGlobals.ActiveVessel;
            bool under = map && IsUnderground(vessel);
            if (map && !wasMap && under)
            {
                StartCoroutine(FocusBody(vessel));
            }

            wasMap = map;
            if (under)
            {
                Hide(vessel.patchedConicRenderer);
            }
            else
            {
                Show();
            }
        }

        // Once KSP has finished entering the map (it targets the vessel), the body instead, seen whole.
        private IEnumerator FocusBody(Vessel vessel)
        {
            yield return new WaitForSeconds(0.3f);
            if (!MapView.MapIsEnabled || PlanetariumCamera.fetch == null || vessel == null || vessel.mainBody == null)
            {
                yield break;
            }

            CelestialBody body = vessel.mainBody;
            PlanetariumCamera.fetch.SetTarget(body.MapObject);
            PlanetariumCamera.fetch.SetDistance((float)(body.Radius * FocusRadii / ScaledSpace.ScaleFactor));
            Log.Info($"Map view: {vessel.GetDisplayName()} is underground; the map looks at {body.bodyName} instead");
        }

        private void Hide(PatchedConicRenderer renderer)
        {
            if (renderer == null || renderer == hidden)
            {
                return;
            }

            Show();
            if (renderer.patchRenders != null)
            {
                foreach (PatchRendering patch in renderer.patchRenders)
                {
                    patch?.DestroyVector();
                }
            }

            renderer.enabled = false;
            hidden = renderer;
        }

        private void Show()
        {
            if (hidden != null)
            {
                hidden.enabled = true;
            }

            hidden = null;
        }

        private void OnDestroy() => Show();
    }
}
