using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// What lies past the drawn part of a deep hole (Lucas: the bottom of a deep hole was a black dot, which broke the
    /// immersion). Deep in a cave or shaft with no sky in sight, the camera clears to the rock's colour (its glow where it is
    /// hot) instead of the stars or black, and the sky cameras are off: the far end of a hole kilometres deep fades into rock.
    /// Put back near an opening, out of the cave, or in water (the underwater view has its own).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class DeepBackdrop : MonoBehaviour
    {
        /// <summary>Metres under the ground the camera must be, and the sky visibility under which it counts as shut in.</summary>
        private const double MinDepth = 60;
        private const float MaxSky = 0.01f;

        private Camera camera;
        private CameraClearFlags clearFlags;
        private Color background;
        private bool active;

        private void LateUpdate()
        {
            UndergroundState state = UndergroundTracker.Camera;
            Camera main = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            bool wanted = main != null && state.Inside && state.Depth > MinDepth && state.Sky < MaxSky && !UnderwaterView.Under && !MapView.MapIsEnabled;
            if (!wanted)
            {
                Restore();
                return;
            }

            if (!active)
            {
                active = true;
                camera = main;
                clearFlags = main.clearFlags;
                background = main.backgroundColor;
                SkyCameras.Hide(this, main);
            }

            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = RockColour(state);
        }

        // Dark rock, or the glow of hot rock (the glow look only), dimmed: it is far away.
        private static Color RockColour(UndergroundState state)
        {
            var rock = new Color(0.025f, 0.023f, 0.022f, 1);
            if (Settings.Current.DeepRockLook != "glow" || !state.RockTemp.HasValue || state.RockTemp.Value <= Core.Meshing.Incandescence.GlowStart)
            {
                return rock;
            }

            Core.Meshing.Incandescence.Colour(state.RockTemp.Value, out float r, out float g, out float b);
            float k = Core.Meshing.Incandescence.Intensity(state.RockTemp.Value) * 0.45f;
            return new Color(rock.r + r * k, rock.g + g * k, rock.b + b * k, 1);
        }

        private void Restore()
        {
            if (!active)
            {
                return;
            }

            active = false;
            if (camera != null)
            {
                camera.clearFlags = clearFlags;
                camera.backgroundColor = background;
            }

            SkyCameras.Show(this);
            camera = null;
        }

        private void OnDestroy() => Restore();
    }
}
