using System.Collections;

namespace Subterra.DevTools
{
    /// <summary>Steps for Subterra's own UI.</summary>
    [StepProvider]
    internal static class UiSteps
    {
        internal static void Register()
        {
            StepRegistry.Register("debugWindow", DebugWindowStep);
        }

        // debugWindow open = true|false — shows or hides the debug window.
        private static IEnumerator DebugWindowStep(ConfigNode node, TestContext ctx)
        {
            bool open = node.ArgBool("open", true);
            DebugWindow.Open = open;
            yield return null;
            ctx.Pass(DebugWindow.Open == open ? (open ? "open" : "closed") : "no debug window in this scene");
        }
    }
}
