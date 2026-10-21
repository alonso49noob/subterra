using HarmonyLib;

namespace Subterra
{
    /// <summary>
    /// Patch <c>pqsHole</c>: KSP re-stitches a quad's edges in the private PQ.UpdateVisibility by
    /// assigning a fresh triangle list, which would close the skylight holes again.
    /// </summary>
    public static class PqsHolePatch
    {
        public const string Id = "pqsHole";

        public static void Register()
        {
            PatchManager.Register(Id, harmony => harmony.Patch(
                AccessTools.Method(typeof(PQ), "UpdateVisibility") ?? throw new System.MissingMethodException("PQ", "UpdateVisibility"),
                postfix: new HarmonyMethod(typeof(PqsHolePatch), nameof(Postfix))));
        }

        private static void Postfix(PQ __instance)
        {
            try
            {
                PQSMod_SubterraSkylight mod = SkylightInjector.ModFor(__instance.sphereRoot);
                if (mod != null)
                {
                    mod.AfterVisibilityUpdate(__instance);
                }
            }
            catch (System.Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }
    }
}
