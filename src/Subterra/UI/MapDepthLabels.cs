using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// In the map and the tracking station, a label under the icon of each craft resting under the ground with how deep it
    /// is (Lucas: a depth indicator for craft landed underground). Depths are worked out every couple of seconds per craft.
    /// [Claude, revisar]
    /// </summary>
    public abstract class MapDepthLabels : MonoBehaviour
    {
        /// <summary>Metres under the terrain from which a craft gets a label.</summary>
        private const double MinDepth = 3;

        private const float Refresh = 2f;

        private readonly Dictionary<Vessel, double> depths = new Dictionary<Vessel, double>();
        private float nextRefresh;
        private GUIStyle style;

        protected abstract bool Showing { get; }

        private void Update()
        {
            if (!Showing || Time.realtimeSinceStartup < nextRefresh || FlightGlobals.Vessels == null)
            {
                return;
            }

            nextRefresh = Time.realtimeSinceStartup + Refresh;
            depths.Clear();
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || v.mainBody == null || v.mainBody.pqsController == null || v.vesselType == VesselType.Flag
                    || v.vesselType == VesselType.SpaceObject || v.vesselType == VesselType.Unknown
                    || !(v.Landed || v.Splashed || v.situation == Vessel.Situations.PRELAUNCH)
                    || !BodyRegistry.Instance.TryGet(v.mainBody, out _))
                {
                    continue;
                }

                double depth = KspSampler.SurfaceRadius(v.mainBody, v.latitude, v.longitude) - (v.mainBody.Radius + v.altitude);
                if (depth > MinDepth)
                {
                    depths[v] = depth;
                }
            }
        }

        private void OnGUI()
        {
            if (!Showing || depths.Count == 0 || PlanetariumCamera.Camera == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 11, fontStyle = FontStyle.Bold };
                style.normal.textColor = new Color(1f, 0.78f, 0.35f);
            }

            Camera cam = PlanetariumCamera.Camera;
            foreach (KeyValuePair<Vessel, double> pair in depths)
            {
                Vessel v = pair.Key;
                if (v == null || v.mainBody == null)
                {
                    continue;
                }

                Vector3d world = v.mainBody.GetWorldSurfacePosition(v.latitude, v.longitude, v.altitude);
                Vector3 screen = cam.WorldToScreenPoint(ScaledSpace.LocalToScaledSpace(world));
                if (screen.z <= 0)
                {
                    continue;
                }

                string text = Localizer.Format("#Subterra_Map_Depth", Depth(pair.Value));
                var rect = new Rect(screen.x - 60, Screen.height - screen.y + 10, 120, 18);
                Color old = style.normal.textColor;
                style.normal.textColor = Color.black;
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, style);
                style.normal.textColor = old;
                GUI.Label(rect, text, style);
            }
        }

        private static string Depth(double metres) =>
            metres >= 10000 ? (metres / 1000).ToString("0.0") + " km" : metres.ToString("#,0") + " m";
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class MapDepthLabelsFlight : MapDepthLabels
    {
        protected override bool Showing => MapView.MapIsEnabled;
    }

    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public sealed class MapDepthLabelsTrackingStation : MapDepthLabels
    {
        protected override bool Showing => true;
    }
}
