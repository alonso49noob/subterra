using System;
using HarmonyLib;

namespace Subterra
{
    /// <summary>
    /// Gravity under the ground (Lucas: a kill barrier on the way to a body's core). KSP pulls towards a point mass,
    /// GM/r², which grows without bound as a vessel goes down a shaft towards the centre and tore craft apart near it. Inside
    /// a body (closer to the centre than its radius) the pull is that of a uniform sphere: it falls in a straight line to
    /// nothing at the centre, GM·r/R³. Above the radius nothing changes.
    /// </summary>
    public static class InteriorGravityPatch
    {
        public const string Id = "interiorGravity";

        public static void Register()
        {
            PatchManager.Register(Id, harmony => harmony.Patch(
                AccessTools.Method(typeof(FlightGlobals), nameof(FlightGlobals.getGeeForceAtPosition), new[] { typeof(Vector3d), typeof(CelestialBody) })
                    ?? throw new MissingMethodException("FlightGlobals", "getGeeForceAtPosition"),
                postfix: new HarmonyMethod(typeof(InteriorGravityPatch), nameof(Postfix))));
        }

        private static void Postfix(Vector3d pos, CelestialBody mainBody, ref Vector3d __result)
        {
            if (mainBody == null || mainBody.Radius <= 0)
            {
                return;
            }

            double r = (pos - mainBody.position).magnitude;
            if (r < mainBody.Radius)
            {
                double s = r / mainBody.Radius;
                __result *= s * s * s;
            }
        }
    }
}
