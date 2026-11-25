using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// M0 spike (radar Task 1): can a slightly larger transparent sphere, child of <c>body.scaledBody</c>, carry
    /// a 1024 x 512 texture in the map view without flicker or a broken draw order? Throwaway code.
    /// </summary>
    [StepProvider]
    internal static class OverlaySpike
    {
        private static readonly string[] ShaderCandidates =
        {
            "Unlit/Transparent",
            "KSP/Alpha/Unlit Transparent",
            "Legacy Shaders/Transparent/Diffuse",
            "KSP/Scenery/Unlit (Transparent)",
        };

        internal static void Register()
        {
            StepRegistry.RegisterSpike("OverlaySpike", Create);
            StepRegistry.RegisterSpike("MapFocus", MapFocus);
            StepRegistry.RegisterSpike("OverlayClear", Clear);
        }

        // spike name = OverlayClear — destroys the overlay spheres, so another variant can be drawn.
        private static IEnumerator Clear(ConfigNode node, TestContext ctx)
        {
            int count = 0;
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                Transform child = body.scaledBody == null ? null : body.scaledBody.transform.Find("SubterraOverlaySpike");
                if (child != null)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                    count++;
                }
            }

            yield return null;
            ctx.Pass(count + " overlays removed");
        }

        // spike name = OverlaySpike bodies = Mun,Kerbin scale = 1.005 shader = <optional> queue = <optional> â€”
        // adds the overlay sphere to each body's scaledBody and logs what the scaled body looks like.
        private static IEnumerator Create(ConfigNode node, TestContext ctx)
        {
            var report = new StringBuilder();
            foreach (string candidate in ShaderCandidates)
            {
                Shader found = Shader.Find(candidate);
                report.Append(Invariant($"Shader.Find('{candidate}') = {(found == null ? "null" : found.name + ", supported " + found.isSupported + ", queue " + found.renderQueue)}; "));
            }

            string wanted = node.Arg("shader");
            Shader shader = null;
            foreach (string candidate in wanted != null ? new[] { wanted } : ShaderCandidates)
            {
                shader = Shader.Find(candidate);
                if (shader != null)
                {
                    break;
                }
            }

            if (shader == null)
            {
                throw new InvalidOperationException("no transparent shader found. " + report);
            }

            float scale = (float)node.ArgDouble("scale", 1.005);
            Texture2D texture = TestTexture();
            foreach (string name in node.Arg("bodies", "Mun,Kerbin").Split(','))
            {
                CelestialBody body = FlightGlobals.GetBodyByName(name.Trim()) ?? throw new ArgumentException("unknown body " + name);
                GameObject scaled = body.scaledBody ?? throw new InvalidOperationException(body.bodyName + " has no scaledBody");
                var meshFilter = scaled.GetComponent<MeshFilter>();
                float boundsRadius = 1000f;
                float meanRadius = 1000f;
                float maxRadius = 1000f;
                float minRadius = 1000f;
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    Mesh mesh = meshFilter.sharedMesh;
                    boundsRadius = mesh.bounds.extents.x;
                    double sum = 0;
                    maxRadius = 0;
                    minRadius = float.MaxValue;
                    foreach (Vector3 v in mesh.vertices)
                    {
                        float m = v.magnitude;
                        sum += m;
                        maxRadius = Mathf.Max(maxRadius, m);
                        minRadius = Mathf.Min(minRadius, m);
                    }

                    meanRadius = (float)(sum / mesh.vertexCount);
                }

                string reference = node.Arg("ref", "mean");
                double mapMax = body.pqsController != null ? body.pqsController.mapMaxHeight : 0;
                // pqs: the scaled mesh has radius 1000 at body.Radius; the terrain reaches mapMaxHeight above it
                // (Parallax displaces the vertices in the shader, so the mesh itself shows no relief).
                float radius = reference == "pqs" ? (float)(1000.0 * (1.0 + mapMax / body.Radius))
                    : reference == "max" ? maxRadius : reference == "bounds" ? boundsRadius : meanRadius;

                // Unity's primitive sphere has only 24 x 16 segments: between its vertices the surface sags up to
                // 0.9 % under the nominal radius, so the terrain of the scaled mesh pokes through a 0.5 % shell.
                // mesh = hires uses a 128 x 64 sphere instead.
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "SubterraOverlaySpike";
                UnityEngine.Object.Destroy(sphere.GetComponent<Collider>());
                if (node.Arg("mesh", "primitive") == "hires")
                {
                    sphere.GetComponent<MeshFilter>().sharedMesh = UvSphere(128, 64);
                }

                sphere.layer = scaled.layer;
                sphere.transform.SetParent(scaled.transform, false);
                // The primitive sphere has radius 0.5; the scaled mesh has radius `radius` in the parent's frame.
                sphere.transform.localScale = Vector3.one * (2f * radius * scale);
                var material = new Material(shader) { mainTexture = texture, color = Color.white };
                if (node.HasValue("queue"))
                {
                    material.renderQueue = (int)node.ArgDouble("queue", 3000);
                }

                sphere.GetComponent<MeshRenderer>().sharedMaterial = material;

                report.AppendLine();
                report.Append(Invariant(
                    $"{body.bodyName}: scaledBody layer {scaled.layer}, scale {scaled.transform.localScale.x:G6}, vertex radius min {minRadius:F1} mean {meanRadius:F1} max {maxRadius:F1}, bounds x {boundsRadius:F1}, body radius {body.Radius:F0} m, pqs mapMaxHeight {mapMax:F0} m, overlay ref {reference} {radius:F1} x {scale}, shader '{shader.name}' queue {material.renderQueue}"));
                report.AppendLine();
                foreach (Renderer renderer in scaled.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.gameObject == sphere)
                    {
                        continue;
                    }

                    Material shared = renderer.sharedMaterial;
                    if (shared != null && node.ArgBool("props", false))
                    {
                        var props = new StringBuilder();
                        Shader sh = shared.shader;
                        for (int i = 0; i < sh.GetPropertyCount(); i++)
                        {
                            UnityEngine.Rendering.ShaderPropertyType type = sh.GetPropertyType(i);
                            string pname = sh.GetPropertyName(i);
                            if (type == UnityEngine.Rendering.ShaderPropertyType.Float || type == UnityEngine.Rendering.ShaderPropertyType.Range)
                            {
                                props.Append(Invariant($"{pname}={shared.GetFloat(pname):G5} "));
                            }
                        }

                        report.AppendLine("    float properties of '" + renderer.gameObject.name + "': " + props);
                    }

                    Material m = renderer.sharedMaterial;
                    report.AppendLine(Invariant(
                        $"    existing '{renderer.gameObject.name}' layer {renderer.gameObject.layer} active {renderer.gameObject.activeSelf} shader '{(m == null ? "none" : m.shader.name)}' queue {(m == null ? -1 : m.renderQueue)} scale {renderer.transform.lossyScale.x:G6}"));
                }
            }

            Log.Info("OverlaySpike: " + report);
            ctx.Pass(Invariant($"overlay on {node.Arg("bodies", "Mun,Kerbin")} with '{shader.name}'"));
            yield break;
        }

        // spike name = MapFocus body = Mun distance = 4 (in body radii) heading = 0 pitch = 0.3 â€” enters the map
        // view if needed and points the planetarium camera at the body.
        private static IEnumerator MapFocus(ConfigNode node, TestContext ctx)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(node.RequireArg("body")) ?? throw new ArgumentException("unknown body");
            if (!MapView.MapIsEnabled)
            {
                MapView.EnterMapView();
                yield return new WaitForSeconds(1f);
            }

            PlanetariumCamera camera = PlanetariumCamera.fetch ?? throw new InvalidOperationException("no planetarium camera");
            camera.SetTarget(body.MapObject);
            yield return null;
            float distance = (float)(body.Radius * ScaledSpace.InverseScaleFactor * node.ArgDouble("distance", 4));
            camera.SetDistance(distance);
            camera.camHdg = (float)node.ArgDouble("heading", 0);
            camera.camPitch = (float)node.ArgDouble("pitch", 0.3);
            yield return new WaitForSeconds(1f);
            ctx.Pass(Invariant($"map view on {body.bodyName}, camera distance {camera.Distance:F0} (scaled units)"));
        }

        // Unit-diameter UV sphere (radius 0.5) with equirectangular UVs.
        private static Mesh UvSphere(int lonSteps, int latSteps)
        {
            var vertices = new Vector3[(lonSteps + 1) * (latSteps + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int j = 0; j <= latSteps; j++)
            {
                float lat = Mathf.PI * j / latSteps - Mathf.PI / 2f;
                for (int i = 0; i <= lonSteps; i++)
                {
                    float lon = 2f * Mathf.PI * i / lonSteps;
                    int index = j * (lonSteps + 1) + i;
                    vertices[index] = 0.5f * new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    uvs[index] = new Vector2((float)i / lonSteps, (float)j / latSteps);
                }
            }

            var triangles = new List<int>();
            for (int j = 0; j < latSteps; j++)
            {
                for (int i = 0; i < lonSteps; i++)
                {
                    int a = j * (lonSteps + 1) + i;
                    int b = a + lonSteps + 1;
                    // Outward-facing: Unity's front faces are clockwise seen from outside, in a left-handed frame.
                    triangles.AddRange(new[] { a, b, a + 1 });
                    triangles.AddRange(new[] { a + 1, b, b + 1 });
                }
            }

            var mesh = new Mesh { name = "SubterraOverlaySphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // 1024 x 512 checkerboard (32 px squares): translucent colour on the white squares, clear on the others;
        // an opaque meridian and equator so the orientation can be read in a screenshot.
        private static Texture2D TestTexture()
        {
            const int width = 1024;
            const int height = 512;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool on = ((x / 32) + (y / 32)) % 2 == 0;
                    Color32 c = on ? new Color32(255, 80, 40, 150) : new Color32(0, 0, 0, 0);
                    if (y == height / 2 || y == height / 2 - 1 || x == 0 || x == 1 || x == width / 2)
                    {
                        c = new Color32(255, 255, 0, 255);
                    }

                    pixels[y * width + x] = c;
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
