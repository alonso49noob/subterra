using System;
using System.Collections.Generic;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>The built-in assertions for <c>assert what = …</c> and <c>waitUntil what = …</c>.</summary>
    [StepProvider]
    internal static class CoreAssertions
    {
        internal static void Register()
        {
            StepRegistry.RegisterAssertion("vesselAlive", VesselAlive);
            StepRegistry.RegisterAssertion("vesselLanded", VesselLanded);
            StepRegistry.RegisterAssertion("logContains", LogContains);
            StepRegistry.RegisterAssertion("noSubterraExceptions", NoSubterraExceptions);
        }

        // vesselAlive — the tracked vessel still exists, is not dead and has all the parts it had
        // when it was tracked (launch, eva, switchTo).
        private static bool VesselAlive(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null)
            {
                ctx.Detail = "the tracked vessel is gone";
                return false;
            }

            int parts = TestContext.PartCount(vessel);
            bool dead = vessel.state == Vessel.State.DEAD;
            ctx.Detail = Invariant(
                $"{vessel.GetDisplayName()}: {parts}/{ctx.TrackedPartCount} parts{(dead ? ", DEAD" : "")}, {vessel.situation} at {vessel.mainBody.bodyName}, altitude {vessel.altitude:F1} m");
            return !dead && parts >= ctx.TrackedPartCount;
        }

        // vesselLanded — the tracked vessel is touching the ground.
        private static bool VesselLanded(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null)
            {
                ctx.Detail = "the tracked vessel is gone";
                return false;
            }

            ctx.Detail = Invariant($"{vessel.GetDisplayName()}: {vessel.situation} at {vessel.mainBody.bodyName}, {vessel.heightFromTerrain:F1} m above terrain");
            return vessel.Landed;
        }

        // logContains text = "…" — a message logged since the DevTools assembly loaded contains the
        // text (case-sensitive; surrounding quotes are not part of it).
        private static bool LogContains(ConfigNode node, TestContext ctx)
        {
            string text = node.RequireArg("text");
            // The runner's own lines (which quote assertions and their texts) do not count.
            bool found = LogCapture.FindAll(entry => entry.Message.Contains(text) && !entry.Message.StartsWith("[Subterra.DevTools]")).Count > 0;
            ctx.Detail = found ? null : $"no log message contains \"{text}\"";
            return found;
        }

        // noSubterraExceptions — no exception logged whose message or stack trace mentions Subterra.
        private static bool NoSubterraExceptions(ConfigNode node, TestContext ctx)
        {
            List<LogCapture.Entry> hits = LogCapture.FindAll(IsSubterraException);
            if (hits.Count == 0)
            {
                return true;
            }

            string first = hits[0].Message;
            ctx.Detail = $"{hits.Count} found, first: {(first.Length > 200 ? first.Substring(0, 200) + "…" : first)}";
            return false;
        }

        private static bool IsSubterraException(LogCapture.Entry entry)
        {
            bool exception = entry.Type == LogType.Exception
                || ((entry.Type == LogType.Error || entry.Type == LogType.Assert)
                    && entry.Message.IndexOf("Exception", StringComparison.Ordinal) >= 0);
            return exception
                && (entry.Message.IndexOf("Subterra", StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.StackTrace.IndexOf("Subterra", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
