using System.Collections.Generic;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The sky cameras (stars, planets) switched off while anything needs them off: the underwater view, the deep backdrop.
    /// Counted by owner, so one giving them back does not show the stars to the other (under water, at the bottom of a shaft).
    /// </summary>
    public static class SkyCameras
    {
        private static readonly HashSet<object> Owners = new HashSet<object>();
        private static readonly List<Camera> Hidden = new List<Camera>();

        public static void Hide(object owner, Camera main)
        {
            if (!Owners.Add(owner) || Owners.Count > 1)
            {
                return;
            }

            foreach (Camera sky in Camera.allCameras)
            {
                if (sky != main && sky.enabled && (sky.name == "GalaxyCamera" || sky.name == "Camera ScaledSpace"))
                {
                    sky.enabled = false;
                    Hidden.Add(sky);
                }
            }
        }

        public static void Show(object owner)
        {
            if (!Owners.Remove(owner) || Owners.Count > 0)
            {
                return;
            }

            foreach (Camera sky in Hidden)
            {
                if (sky != null)
                {
                    sky.enabled = true;
                }
            }

            Hidden.Clear();
        }
    }
}
