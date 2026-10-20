using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// M0 spike (Task 4): which shader can draw Subterra's rock meshes. ShaderDump lists every loaded
    /// shader; ShaderRoom shows a closed room (inward faces) per candidate material in front of the
    /// camera, screenshots it, measures the picture and tries GPU instancing with it.
    /// </summary>
    [StepProvider]
    internal static class ShaderSpike
    {
        private const int RoomLayer = 15;

        internal static void Register()
        {
            StepRegistry.RegisterSpike("ShaderDump", Dump);
            StepRegistry.RegisterSpike("ShaderRoom", Room);
        }

        // spike name = ShaderDump — writes shaders.txt (name, supported, properties).
        private static IEnumerator Dump(ConfigNode node, TestContext ctx)
        {
            var sb = new StringBuilder();
            Shader[] shaders = Resources.FindObjectsOfTypeAll<Shader>().OrderBy(s => s.name).ToArray();
            foreach (Shader shader in shaders)
            {
                sb.Append(shader.name).Append(shader.isSupported ? "" : " (unsupported)").AppendLine();
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    sb.Append("    ").Append(shader.GetPropertyName(i)).Append(" : ").Append(shader.GetPropertyType(i)).AppendLine();
                }
            }

            File.WriteAllText(Path.Combine(ctx.ResultsDir, "shaders.txt"), sb.ToString());
            int triplanar = shaders.Count(s => s.name.IndexOf("Triplanar", StringComparison.OrdinalIgnoreCase) >= 0);
            ctx.Pass($"{shaders.Length} shaders, {triplanar} triplanar → shaders.txt");
            yield break;
        }

        // spike name = ShaderRoom — one room per candidate material: room-<i>.png and shader-rooms.txt.
        private static IEnumerator Room(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            Camera camera = FlightCamera.fetch.mainCamera;
            List<(string label, Material material)> candidates = Candidates(vessel.mainBody);

            Mesh room = RoomMesh(40f);
            Mesh cube = CubeMesh(2f);
            var report = new StringBuilder();
            var errors = new List<string>();
            Application.LogCallback onLog = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning && message.IndexOf("instanc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    errors.Add(message);
                }
            };

            int index = 0;
            foreach (var (label, material) in candidates)
            {
                var go = new GameObject("SubterraShaderRoom" + index) { layer = RoomLayer };
                // The camera sits inside the room, lit by a lamp at the camera (as in a cave).
                go.transform.position = camera.transform.position;
                go.transform.rotation = Quaternion.LookRotation(camera.transform.forward, (vessel.transform.position - vessel.mainBody.position).normalized);
                go.AddComponent<MeshFilter>().sharedMesh = room;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                var lamp = new GameObject("SubterraShaderLamp").AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.range = 60f;
                lamp.intensity = 1.5f;
                lamp.transform.position = camera.transform.position;

                errors.Clear();
                Application.logMessageReceived += onLog;
                material.enableInstancing = true;
                bool instancingFlag = material.enableInstancing;
                var matrices = new Matrix4x4[10];
                for (int i = 0; i < matrices.Length; i++)
                {
                    matrices[i] = Matrix4x4.TRS(go.transform.position + go.transform.right * (i * 3 - 15), Quaternion.identity, Vector3.one);
                }

                string drawError = null;
                float until = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < until)
                {
                    try
                    {
                        Graphics.DrawMeshInstanced(cube, 0, material, matrices, matrices.Length, null, ShadowCastingMode.On, true, RoomLayer);
                    }
                    catch (Exception e)
                    {
                        drawError = e.GetType().Name + ": " + e.Message;
                    }

                    yield return null;
                }

                yield return new WaitForEndOfFrame();
                string stats = Capture(Path.Combine(ctx.ResultsDir, $"room-{index}.png"));
                Application.logMessageReceived -= onLog;
                string line = Invariant(
                    $"room-{index}: {label} | shader '{material.shader.name}' supported {material.shader.isSupported} | instancing flag {instancingFlag}, draw error {drawError ?? "none"}, log errors {errors.Count}{(errors.Count > 0 ? " first: " + errors[0] : "")} | {stats}");
                report.AppendLine(line);
                UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(lamp.gameObject);
                yield return null;
                index++;
            }

            File.WriteAllText(Path.Combine(ctx.ResultsDir, "shader-rooms.txt"), report.ToString());
            ctx.Pass($"{candidates.Count} materials → shader-rooms.txt, room-0..{candidates.Count - 1}.png");
        }

        private static List<(string, Material)> Candidates(CelestialBody body)
        {
            var list = new List<(string, Material)>();
            Texture2D albedo = NoiseTexture(512, false);
            Texture2D normal = NoiseTexture(512, true);

            Material surface = body.pqsController != null ? body.pqsController.surfaceMaterial : null;
            if (surface != null)
            {
                list.Add(($"clone of {body.bodyName} surfaceMaterial", new Material(surface)));
            }

            foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>().OrderBy(s => s.name))
            {
                bool triplanar = shader.name.IndexOf("Triplanar", StringComparison.OrdinalIgnoreCase) >= 0;
                bool parallax = shader.name.IndexOf("Parallax", StringComparison.OrdinalIgnoreCase) >= 0
                    && shader.name.IndexOf("Scaled", StringComparison.OrdinalIgnoreCase) < 0;
                if ((triplanar || parallax) && (surface == null || shader != surface.shader))
                {
                    list.Add(("noise textures", Configure(new Material(shader), albedo, normal)));
                }
            }

            Shader bumped = Shader.Find("KSP/Bumped Specular");
            if (bumped != null)
            {
                list.Add(("noise textures, box UV", Configure(new Material(bumped), albedo, normal)));
            }

            return list;
        }

        // Every texture property gets the noise (normal-map-like names get the flat normal map);
        // main colour white; opacity/fade-like floats 1.
        private static Material Configure(Material material, Texture2D albedo, Texture2D normal)
        {
            Shader shader = material.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Texture:
                        bool isNormal = name.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;
                        material.SetTexture(name, isNormal ? normal : albedo);
                        break;
                    case ShaderPropertyType.Color:
                        // Only the main colour: rim, burn and temperature colours must keep their (black) defaults.
                        if (name == "_Color")
                        {
                            material.SetColor(name, Color.white);
                        }

                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        if (name.IndexOf("Opacity", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Fade", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            material.SetFloat(name, 1f);
                        }

                        break;
                }
            }

            return material;
        }

        private static Texture2D NoiseTexture(int size, bool flatNormal)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var pixels = new Color32[size * size];
            var random = new System.Random(7);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte g = (byte)(90 + random.Next(80));
                pixels[i] = flatNormal ? new Color32(128, 128, 255, 128) : new Color32(g, g, g, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        // Cube of the given size centred on the origin, faces wound inwards, box UVs (10 m per tile), white vertex colours.
        private static Mesh RoomMesh(float size)
        {
            Mesh mesh = CubeMesh(size);
            int[] tris = mesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            }

            Vector3[] normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = -normals[i];
            }

            mesh.triangles = tris;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CubeMesh(float size)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            float h = size / 2;
            foreach (Vector3 n in axes)
            {
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                int start = vertices.Count;
                for (int k = 0; k < 4; k++)
                {
                    float su = (k & 1) == 0 ? -1 : 1, sv = (k & 2) == 0 ? -1 : 1;
                    Vector3 p = n * h + u * (su * h) + v * (sv * h);
                    vertices.Add(p);
                    normals.Add(n);
                    uvs.Add(new Vector2(Vector3.Dot(p, u) / 10f, Vector3.Dot(p, v) / 10f));
                }

                // Outward: Unity's front face is where Cross(b - a, c - a) points, here +n.
                tris.AddRange(new[] { start, start + 1, start + 2, start + 1, start + 3, start + 2 });
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), vertices.Count).ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Saves the frame and returns centre-crop statistics (mean RGB, luminance spread, magenta share).
        private static string Capture(string path)
        {
            int width = Screen.width, height = Screen.height;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                int cw = width / 3, ch = height / 3;
                Color[] crop = texture.GetPixels(width / 2 - cw / 2, height / 2 - ch / 2, cw, ch);
                double r = 0, g = 0, b = 0, l2 = 0;
                int magenta = 0;
                foreach (Color c in crop)
                {
                    r += c.r;
                    g += c.g;
                    b += c.b;
                    double l = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    l2 += l * l;
                    if (c.r > 0.8f && c.b > 0.8f && c.g < 0.3f)
                    {
                        magenta++;
                    }
                }

                int n = crop.Length;
                double lum = 0.2126 * r / n + 0.7152 * g / n + 0.0722 * b / n;
                double sd = Math.Sqrt(Math.Max(0, l2 / n - lum * lum));
                return Invariant($"centre mean rgb ({r / n:F2}, {g / n:F2}, {b / n:F2}), luminance sd {sd:F3}, magenta {100.0 * magenta / n:F1}%");
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
