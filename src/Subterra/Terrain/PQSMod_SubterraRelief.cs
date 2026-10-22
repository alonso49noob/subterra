using HarmonyLib;
using Subterra.Core.Geo;

namespace Subterra
{
    /// <summary>
    /// Raises and sinks a body's terrain where its RELIEF features are (volcano cones, rille trenches): adds
    /// <see cref="ReliefField.HeightDelta"/> to every vertex height the PQS builds, and to every height it is asked for
    /// (GetSurfaceHeight runs the mods too, so Subterra's samplers see the same ground). Order 500: after stock height mods.
    /// </summary>
    public sealed class PQSMod_SubterraRelief : PQSMod
    {
        public const int ModOrder = 500;

        public ReliefField Field;

        public override void OnVertexBuildHeight(PQS.VertexBuildData data)
        {
            if (Field != null)
            {
                data.vertHeight += Field.HeightDelta(data.directionFromCenter.ToCore());
            }
        }

        /// <summary>Adds the mod to a body's PQS (once) and rebuilds its terrain if it is already built.</summary>
        public static void Attach(BodyRuntime runtime)
        {
            PQS pqs = runtime.Body?.pqsController;
            if (pqs == null || runtime.Spec.Reliefs.Count == 0 || pqs.GetComponentInChildren<PQSMod_SubterraRelief>(true) != null)
            {
                return;
            }

            var go = new UnityEngine.GameObject("SubterraRelief");
            go.transform.SetParent(pqs.transform, false);
            PQSMod_SubterraRelief mod = go.AddComponent<PQSMod_SubterraRelief>();
            mod.sphere = pqs;
            mod.order = ModOrder;
            mod.modEnabled = true;
            mod.requirements = PQS.ModiferRequirements.Default;
            mod.Field = new ReliefField(runtime.Spec.Reliefs, runtime.Body.Radius);
            runtime.Relief = mod.Field;
            AccessTools.Method(typeof(PQS), "SetupMods").Invoke(pqs, null);
            if (pqs.isActive)
            {
                pqs.RebuildSphere();
            }

            Log.Info($"Relief: {runtime.Spec.Reliefs.Count} features on {runtime.Body.bodyName}");
        }
    }
}
