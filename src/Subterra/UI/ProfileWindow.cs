using System;
using System.Text;
using KSP.Localization;
using Subterra.Core.Environment;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The "Profile" of a core drill's hole: the column of layers where it stands (depths, hardness, rock temperature,
    /// what each yields), the drill's depth, the caves it broke into and any gas it struck. One per drill part.
    /// </summary>
    public sealed class ProfileWindow : MonoBehaviour
    {
        private ModuleSubterraCoreDrill drill;
        private Rect window = new Rect(Screen.width * 0.6f, Screen.height * 0.2f, 380, 10);
        private Vector2 scroll;

        public static void Toggle(ModuleSubterraCoreDrill drill)
        {
            ProfileWindow open = drill.part.gameObject.GetComponent<ProfileWindow>();
            if (open != null)
            {
                Destroy(open);
                return;
            }

            drill.part.gameObject.AddComponent<ProfileWindow>().drill = drill;
        }

        private void OnGUI()
        {
            if (drill == null || drill.vessel == null || !drill.vessel.loaded)
            {
                Destroy(this);
                return;
            }

            window = GUILayout.Window(GetInstanceID(), window, Draw, Localizer.Format("#Subterra_Profile_Title"), GUILayout.Width(380));
        }

        private void Draw(int id)
        {
            CelestialBody body = drill.vessel.mainBody;
            if (!BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                GUILayout.Label(Localizer.Format("#Subterra_Drill_NoStrata"));
                GUI.DragWindow();
                return;
            }

            double lat = drill.holeBody == body.bodyName ? drill.holeLat : drill.vessel.latitude;
            double lon = drill.holeBody == body.bodyName ? drill.holeLon : drill.vessel.longitude;
            string biome = KspSampler.Biome(body, lat, lon);
            double depth = Math.Max(0, drill.Depth);
            GUILayout.Label(Localizer.Format("#Subterra_Profile_Where", body.displayName.LocalizeRemoveGender(), biome ?? "-", depth.ToString("F1")));

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(260));
            foreach (LayerSpan span in runtime.Strata.Spans(biome))
            {
                bool here = depth >= span.Top && depth < span.Bottom;
                string name = string.IsNullOrEmpty(span.Layer.DisplayName) ? span.Layer.Name : Localizer.Format(span.Layer.DisplayName);
                double? temp = runtime.Geotherm.Temperature((span.Top + Math.Min(span.Bottom, span.Top + 1000)) * 0.5, biome);
                var line = new StringBuilder();
                line.Append(here ? "▶ " : "   ");
                line.Append(name);
                line.Append("  ").Append(span.Top.ToString("F0")).Append("–")
                    .Append(double.IsInfinity(span.Bottom) || span.Bottom > 1e6 ? "∞" : span.Bottom.ToString("F0")).Append(" m");
                line.Append("  ").Append(Localizer.Format("#Subterra_Profile_Hardness", span.Layer.Hardness.ToString("F2")));
                if (temp.HasValue)
                {
                    line.Append("  ").Append(temp.Value.ToString("F0")).Append(" K");
                }

                foreach (var y in span.Layer.Yields)
                {
                    line.Append("  ").Append(y.Key);
                }

                if (span.Layer.GasChance > 0 && !string.IsNullOrEmpty(span.Layer.GasResource))
                {
                    line.Append("  ").Append(Localizer.Format("#Subterra_Profile_GasRisk", span.Layer.GasResource));
                }

                GUILayout.Label(line.ToString());
            }

            GUILayout.EndScrollView();
            int caves = string.IsNullOrEmpty(drill.reachedCaves) ? 0 : drill.reachedCaves.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Length;
            GUILayout.Label(Localizer.Format("#Subterra_Profile_Caves", caves));
            if (drill.gasLeft > 0)
            {
                GUILayout.Label(Localizer.Format("#Subterra_Profile_Gas", drill.gasResource, drill.gasLeft.ToString("F0")));
            }

            if (GUILayout.Button(Localizer.Format("#Subterra_Profile_Close")))
            {
                Destroy(this);
            }

            GUI.DragWindow();
        }
    }
}
