using Subterra.Core.Environment;
using Subterra.Core.Model;
using Subterra.Core.Procedural;
using UnityEngine;

namespace Subterra
{
    /// <summary>Everything Subterra keeps for one body at run time.</summary>
    public sealed class BodyRuntime
    {
        private Transform root;

        public BodyRuntime(BodySpec spec, CelestialBody body)
        {
            Spec = spec;
            Body = body;
            Catalog = new NetworkCatalog(spec, body.Radius);
            Strata = new Strata(spec.Strata);
            Geotherm = new Geotherm(spec.Geotherm, Strata);
        }

        public BodySpec Spec { get; }

        public CelestialBody Body { get; }

        public NetworkCatalog Catalog { get; }

        public Strata Strata { get; }

        public Geotherm Geotherm { get; }

        /// <summary>Parent of every Subterra object of the body: a child of the PQS (moves and turns with the terrain).</summary>
        public Transform Root
        {
            get
            {
                if (root == null && Body.pqsController != null)
                {
                    var go = new GameObject("Subterra:" + Body.bodyName);
                    root = go.transform;
                    root.SetParent(Body.pqsController.transform, false);
                    root.localPosition = Vector3.zero;
                    root.localRotation = Quaternion.identity;
                }

                return root;
            }
        }
    }
}
