using System.Linq;
using KSP.Localization;
using KSP.UI.Screens;
using Subterra.Core.Tubes;
using UnityEngine;
using static System.FormattableString;

namespace Subterra
{
    /// <summary>
    /// Debug window (Alt+Shift+U; toolbar button when Settings.Debug): jump to sites, chunk wireframe,
    /// the field and environment at the camera, patch status, frame cost and meshing queue.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class DebugWindow : MonoBehaviour
    {
        private static DebugWindow instance;
        private Rect window = new Rect(80, 80, 420, 520);
        private Vector2 scroll;
        private bool visible;
        private bool wireframe;
        private bool coloured;
        private string confirmSite;
        private ApplicationLauncherButton button;

        public static bool Open
        {
            get => instance != null && instance.visible;
            set
            {
                if (instance != null)
                {
                    instance.visible = value;
                }
            }
        }

        private void Awake()
        {
            instance = this;
        }

        private void Start()
        {
            if (Settings.Current.Debug)
            {
                GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            }
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            if (button != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(button);
            }

            if (instance == this)
            {
                instance = null;
            }
        }

        private void AddButton()
        {
            if (button == null && ApplicationLauncher.Instance != null)
            {
                button = ApplicationLauncher.Instance.AddModApplication(() => visible = true, () => visible = false, null, null, null, null,
                    ApplicationLauncher.AppScenes.FLIGHT, Texture2D.whiteTexture);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.U) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                visible = !visible;
            }
        }

        private void OnGUI()
        {
            if (visible)
            {
                window = GUILayout.Window(GetInstanceID(), window, Draw, Localizer.Format("#Subterra_Debug_Title"));
            }
        }

        private void Draw(int id)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            CelestialBody body = FlightGlobals.currentMainBody;
            BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime);

            GUILayout.Label(Localizer.Format("#Subterra_Debug_Sites"));
            if (runtime != null)
            {
                foreach (TubeNetwork net in runtime.Catalog.Named)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(net.SiteName);
                    if (confirmSite == net.Id)
                    {
                        if (GUILayout.Button(Localizer.Format("#Subterra_Debug_Confirm")))
                        {
                            Jump(net);
                            confirmSite = null;
                        }
                    }
                    else if (GUILayout.Button(Localizer.Format("#Subterra_Debug_Jump")))
                    {
                        confirmSite = net.Id;
                    }

                    GUILayout.EndHorizontal();
                }
            }

            bool wire = GUILayout.Toggle(wireframe, Localizer.Format("#Subterra_Debug_Wireframe"));
            if (wire != wireframe)
            {
                wireframe = wire;
            }

            UndergroundState camera = UndergroundTracker.Camera;
            GUILayout.Label(Localizer.Format("#Subterra_Debug_Camera"));
            if (camera.Inside)
            {
                GUILayout.Label(Invariant($"{Localizer.Format("#Subterra_Debug_Field")}: {camera.Net.Sdf.Distance(camera.Local):F2} m ({camera.Net.Id})"));
                GUILayout.Label(Invariant($"{Localizer.Format("#Subterra_Debug_Sky")}: {camera.Sky:F2}"));
                GUILayout.Label(Invariant($"{Localizer.Format("#Subterra_Debug_Temperature")}: {camera.RockTemp:F1} K"));
                GUILayout.Label($"{Localizer.Format("#Subterra_Debug_Layer")}: {camera.Layer}");
                GUILayout.Label($"{Localizer.Format("#Subterra_Debug_Zone")}: {camera.Zone.Kind}");
                GUILayout.Label($"{Localizer.Format("#Subterra_Debug_Biome")}: {camera.Biome}");
                GUILayout.Label(Invariant($"{Localizer.Format("#Subterra_Debug_Depth")}: {camera.Depth:F0} m"));
            }
            else
            {
                GUILayout.Label(Localizer.Format("#Subterra_Debug_Outside"));
            }

            GUILayout.Label(Localizer.Format("#Subterra_Debug_Patches"));
            foreach (var pair in PatchManager.Status.OrderBy(p => p.Key))
            {
                GUILayout.Label($"  {pair.Key}: {pair.Value}");
            }

            var (avg, max, frames) = SubterraClock.Stats(5);
            GUILayout.Label(Invariant($"{Localizer.Format("#Subterra_Debug_FrameTime")}: {avg:F2} / {max:F2} ms ({frames})"));
            ChunkStreamer streamer = ChunkStreamer.Instance;
            if (streamer != null)
            {
                GUILayout.Label(Invariant($"{Localizer.Format("#Subterra_Debug_Chunks")}: {streamer.ActiveChunks} / {streamer.CachedChunks}, {streamer.CachedBytes / (1024 * 1024)} MB, {Localizer.Format("#Subterra_Debug_Queue")} {streamer.PendingJobs} + {WorkerPool.Shared.Pending}"));
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        // "Wireframe": chunks drawn in flat colours per LOD (red LOD0 … blue LOD3) to see the chunk layout.
        private void LateUpdate()
        {
            // Nothing to do unless the colours are on, or were on last frame (to put the rock back).
            if (ChunkStreamer.Instance == null || !wireframe && !coloured)
            {
                return;
            }

            coloured = wireframe;
            foreach (ChunkObject chunk in ChunkStreamer.Instance.Chunks)
            {
                chunk.DebugMaterial(wireframe ? LodMaterial(chunk.Key.Lod) : null);
            }
        }

        private static readonly Color[] LodColours = { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.8f, 0.2f), new Color(0.3f, 1f, 0.4f), new Color(0.3f, 0.5f, 1f) };

        private static readonly Material[] LodMaterials = new Material[4];

        private static Material LodMaterial(int lod)
        {
            lod = Mathf.Clamp(lod, 0, 3);
            return LodMaterials[lod] ?? (LodMaterials[lod] = new Material(Shader.Find("KSP/Unlit")) { color = LodColours[lod] });
        }

        // Puts the active vessel 50 m above the first skylight (or node) of a site.
        private static void Jump(TubeNetwork net)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                return;
            }

            double lat, lon;
            if (net.Skylights.Count > 0)
            {
                lat = net.Skylights[0].Lat;
                lon = net.Skylights[0].Lon;
            }
            else
            {
                lat = net.Nodes[0].Lat;
                lon = net.Nodes[0].Lon;
            }

            FlightGlobals.fetch.SetVesselPosition(FlightGlobals.GetBodyIndex(vessel.mainBody), lat, lon, 50, Vector3.zero, asl: false, easeToSurface: false);
        }
    }
}
