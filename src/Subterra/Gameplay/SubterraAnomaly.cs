using KSP.Localization;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>A named site's skylight as a surface object: SCANsat's anomaly scanner finds it.</summary>
    public sealed class SubterraAnomaly : PQSSurfaceObject
    {
        public string SiteName;
        public string DisplayName;
        public Vector3d Position;

        public override string SurfaceObjectName => SiteName;

        public override string DisplaySurfaceObjectName => DisplayName;

        public override Vector3d PlanetRelativePosition => Position;
    }

    /// <summary>Adds a <see cref="SubterraAnomaly"/> per named site (first skylight, or first node) to the body's surface objects.</summary>
    public static class AnomalyRegistrar
    {
        public static void Register(BodyRuntime runtime)
        {
            CelestialBody body = runtime.Body;
            PQS pqs = body.pqsController;
            foreach (TubeNetwork net in runtime.Catalog.Named)
            {
                Vector3d position = net.Skylights.Count > 0
                    ? net.ToBody(net.Skylights[0].SurfaceCenter).ToKsp()
                    : (net.Nodes[0].Up * net.Nodes[0].SurfaceRadius).ToKsp();
                var go = new GameObject("SubterraAnomaly " + net.SiteName);
                go.transform.SetParent(pqs.transform, false);
                go.transform.localPosition = position;
                SubterraAnomaly anomaly = go.AddComponent<SubterraAnomaly>();
                anomaly.sphere = pqs;
                anomaly.modEnabled = false;
                anomaly.SiteName = net.SiteName;
                anomaly.DisplayName = string.IsNullOrEmpty(net.DisplayName) ? net.SiteName : Localizer.Format(net.DisplayName);
                anomaly.Position = position;
            }

            body.pqsSurfaceObjects = pqs.GetComponentsInChildren<PQSSurfaceObject>(true);
        }
    }
}
