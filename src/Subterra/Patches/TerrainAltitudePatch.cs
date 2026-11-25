using System;
using HarmonyLib;

namespace Subterra
{
    /// <summary>
    /// Patches <c>terrainAltitude</c> and <c>groundPositioning</c>. Inside a cave the terrain under a
    /// vessel is the cave floor: Vessel.PQSAltitude (used for packed and unloaded vessels, and by
    /// CheckKill's "crashed through terrain") returns the floor's altitude. Vessel.CheckGroundCollision,
    /// which lifts a vessel going off rails onto the first ground hit from 500 m above the PQS, is
    /// skipped (setting skipGroundPositioning does not work: GoOffRails overwrites it). Loading a landed
    /// vessel would also lift it to the PQS surface (private Vessel.getCorrectedLandedAltitude): kept in caves.
    /// Patch bodies never throw into KSP (see <see cref="PatchManager.Fault"/>).
    /// </summary>
    public static class TerrainAltitudePatch
    {
        public const string Id = "terrainAltitude";
        public const string GroundId = "groundPositioning";

        /// <summary>How far (m) out of a cave's void a vessel still counts as in it for the terrain checks.</summary>
        private const float NearMargin = 4f;

        public static void Register()
        {
            PatchManager.Register(Id, harmony =>
            {
                harmony.Patch(
                    AccessTools.Method(typeof(Vessel), nameof(Vessel.PQSAltitude)),
                    postfix: new HarmonyMethod(typeof(TerrainAltitudePatch), nameof(PqsAltitudePostfix)));
                harmony.Patch(
                    AccessTools.Method(typeof(Vessel), nameof(Vessel.CheckKill)) ?? throw new MissingMethodException("Vessel", "CheckKill"),
                    prefix: new HarmonyMethod(typeof(TerrainAltitudePatch), nameof(CheckKillPrefix)));
            });
            PatchManager.Register(GroundId, harmony =>
            {
                harmony.Patch(AccessTools.Method(typeof(Vessel), nameof(Vessel.CheckGroundCollision)),
                    prefix: new HarmonyMethod(typeof(TerrainAltitudePatch), nameof(CheckGroundCollisionPrefix)));
                harmony.Patch(AccessTools.Method(typeof(Vessel), "getCorrectedLandedAltitude") ?? throw new MissingMethodException("Vessel", "getCorrectedLandedAltitude"),
                    prefix: new HarmonyMethod(typeof(TerrainAltitudePatch), nameof(CorrectedLandedAltitudePrefix)));
            });
        }

        /// <summary>How far (m) from a void a vessel is spared KSP's "crashed through terrain".</summary>
        private const float KillMargin = 10f;

        // KSP destroys a vessel whose altitude is under its terrainAltitude; that value comes from a ray down (or from
        // PQSAltitude), and a frame can keep the terrain of the surface it stood on. A vessel dropping into a dug shaft was
        // killed right under the mouth (Lucas, a 200 km shaft on the Mun), where the real terrain was a little higher than
        // the network's samples and its underground state not yet updated. In or near a void (or in an ocean under ice)
        // the check is skipped: the void is open space, whatever the terrain over it says.
        private static bool CheckKillPrefix(Vessel __instance)
        {
            try
            {
                if (__instance == null || __instance.mainBody == null || !BodyRegistry.Instance.TryGet(__instance.mainBody, out BodyRuntime runtime))
                {
                    return true;
                }

                Vector3d world = __instance.GetWorldPos3D();
                Core.Numerics.Vec3d at = Frames.ToBody(__instance.mainBody, world);
                if (runtime.Catalog.FindNear(at, KillMargin) != null || Underground.OceanAt(__instance.mainBody, world) != null)
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }

            return true;
        }

        private static void PqsAltitudePostfix(Vessel __instance, ref double __result)
        {
            try
            {
                SubterraVesselModule module = SubterraVesselModule.Of(__instance);
                UndergroundState state = module != null ? module.State : UndergroundTracker.Compute(__instance.mainBody, __instance.GetWorldPos3D(), false);
                if (!state.Inside)
                {
                    // Just out of a cave's void (a wheel in the rubble, the centre in a wall's bump): still in the cave, its
                    // ground right here; else KSP compared it with the terrain far above and destroyed it ("crashed
                    // through terrain").
                    if (BodyRegistry.Instance.TryGet(__instance.mainBody, out BodyRuntime near))
                    {
                        Core.Numerics.Vec3d at = Frames.ToBody(__instance.mainBody, __instance.GetWorldPos3D());
                        Core.Tubes.TubeNetwork cave = near.Catalog.FindNear(at, NearMargin);
                        if (cave != null && cave.AboveSurface(cave.ToLocal(at)) < 0)
                        {
                            __result = __instance.altitude - 0.5;
                            return;
                        }
                    }

                    // In the ocean under the ice: the ground is the sea floor.
                    Subterra.Core.Water.WaterSample? sea = Underground.OceanAt(__instance.mainBody, __instance.GetWorldPos3D());
                    if (sea != null && BodyRegistry.Instance.TryGet(__instance.mainBody, out BodyRuntime runtime))
                    {
                        __result = __instance.altitude - (runtime.Ocean.Spec.Thickness - sea.Value.Depth);
                    }

                    return;
                }

                // In the void with no floor in reach (a blast shaft kilometres deep): the ground is far below. Left alone, the
                // result was the terrain at the top of the shaft, so KSP saw everything going in "crash through terrain"
                // and destroyed it (Lucas: dug holes killed you as soon as you went in).
                float? floor = state.Net.FloorBelow(state.Local);
                __result = __instance.altitude - (floor ?? Core.Tubes.TubeNetwork.FloorSearchDistance);
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        // Loading a landed vessel lifts it to max(saved altitude, PQS surface) (M0): keep the saved altitude in a cave.
        private static bool CorrectedLandedAltitudePrefix(double lat, double lon, double alt, CelestialBody body, ref double __result)
        {
            try
            {
                if (body == null || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
                {
                    return true;
                }

                var bodyPos = Core.Geo.BodyFrame.Direction(lat, lon) * (body.Radius + alt);
                if (runtime.Catalog.FindContaining(bodyPos) == null && runtime.Ocean?.At(bodyPos) == null)
                {
                    return true;
                }

                __result = alt;
                return false;
            }
            catch (Exception e)
            {
                PatchManager.Fault(GroundId, e);
                return true;
            }
        }

        private static bool CheckGroundCollisionPrefix(Vessel __instance, ref bool __result)
        {
            try
            {
                if (Underground.Locate(__instance.mainBody, __instance.GetWorldPos3D(), out _, out _) || Underground.OceanAt(__instance.mainBody, __instance.GetWorldPos3D()) != null)
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception e)
            {
                PatchManager.Fault(GroundId, e);
            }

            return true;
        }
    }
}
