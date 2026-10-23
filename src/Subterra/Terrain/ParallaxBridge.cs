using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Terrain holes under Parallax (Continued), which draws each near terrain quad as its own copy of the quad's mesh
    /// (made once, when the camera comes near) and hides the real quad behind a see-through material. A hole cut into the
    /// quad's mesh afterwards reached neither: the copy kept the old ground or vanished with the whole quad, and the
    /// triangles around the hole took the see-through material. Here, by reflection (no reference to Parallax): the copy
    /// is dropped after a cut (Parallax makes it again from the cut mesh), and the triangles around a hole are drawn with
    /// Parallax's own material for that quad. Does nothing without Parallax.
    /// </summary>
    internal static class ParallaxBridge
    {
        private static bool looked;
        private static IDictionary quadData;
        private static FieldInfo materialField, initializedField;
        private static MethodInfo outOfRange;

        /// <summary>Parallax's material for <paramref name="quad"/>; null without Parallax or for a quad it does not draw.</summary>
        public static Material QuadMaterial(PQ quad)
        {
            object data = DataOf(quad);
            return data != null ? materialField?.GetValue(data) as Material : null;
        }

        /// <summary>After the quad's mesh changed: Parallax drops its copy and makes it again from the new mesh.</summary>
        public static void Refresh(PQ quad)
        {
            object data = DataOf(quad);
            if (data == null || outOfRange == null)
            {
                return;
            }

            try
            {
                if (initializedField == null || initializedField.GetValue(data) is bool built && built)
                {
                    outOfRange.Invoke(data, null);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Parallax quad refresh failed: {e.InnerException?.Message ?? e.Message}");
            }
        }

        private static IDictionary scatterData;
        private static FieldInfo scatterTriangles, scatterTriangleBuffer, scatterTriangleCount, scatterList;
        private static MethodInfo scatterStart, scatterCleanup;

        /// <summary>
        /// After the quad's triangles changed: Parallax's scatters (rocks) on it are spread again over the triangles left,
        /// so none float over a hole (they are spread over the quad's triangles once, when it first shows).
        /// </summary>
        public static void RefreshScatters(PQ quad)
        {
            Look();
            if (scatterData == null || quad == null || quad.mesh == null)
            {
                return;
            }

            try
            {
                object data = scatterData.Contains(quad) ? scatterData[quad] : null;
                if (data == null)
                {
                    return;
                }

                int[] triangles = quad.mesh.triangles;
                (scatterTriangleBuffer.GetValue(data) as ComputeBuffer)?.Dispose();
                var buffer = new ComputeBuffer(Math.Max(1, triangles.Length), sizeof(int), ComputeBufferType.Structured);
                buffer.SetData(triangles);
                scatterTriangles.SetValue(data, triangles);
                scatterTriangleBuffer.SetValue(data, buffer);
                scatterTriangleCount.SetValue(data, triangles.Length / 3);
                if (scatterList.GetValue(data) is IList scatters)
                {
                    foreach (object scatter in scatters)
                    {
                        scatterCleanup.Invoke(scatter, null);
                        scatterStart.Invoke(scatter, null);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Parallax scatter refresh failed: {e.InnerException?.Message ?? e.Message}");
                scatterData = null;
            }
        }

        private static void LookScatters()
        {
            Type component = AccessTools.TypeByName("Parallax.ScatterComponent");
            Type quadData = AccessTools.TypeByName("Parallax.ScatterSystemQuadData");
            Type data = AccessTools.TypeByName("Parallax.ScatterData");
            if (component == null || quadData == null || data == null)
            {
                return;
            }

            scatterTriangles = AccessTools.Field(quadData, "triangles");
            scatterTriangleBuffer = AccessTools.Field(quadData, "sourceTrianglesBuffer");
            scatterTriangleCount = AccessTools.Field(quadData, "numMeshTriangles");
            scatterList = AccessTools.Field(quadData, "quadScatters");
            scatterStart = AccessTools.Method(data, "Start");
            scatterCleanup = AccessTools.Method(data, "Cleanup");
            if (scatterTriangles != null && scatterTriangleBuffer != null && scatterTriangleCount != null && scatterList != null
                && scatterStart != null && scatterCleanup != null)
            {
                scatterData = AccessTools.Field(component, "scatterQuadData")?.GetValue(null) as IDictionary;
            }
        }

        private static object DataOf(PQ quad)
        {
            Look();
            if (quadData == null || quad == null)
            {
                return null;
            }

            try
            {
                return quadData.Contains(quad) ? quadData[quad] : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Look()
        {
            if (looked)
            {
                return;
            }

            looked = true;
            Type mod = AccessTools.TypeByName("Parallax.PQSMod_Parallax");
            Type data = AccessTools.TypeByName("Parallax.TerrainShaderQuadData");
            if (mod == null || data == null)
            {
                return;
            }

            quadData = AccessTools.Field(mod, "terrainQuadData")?.GetValue(null) as IDictionary;
            materialField = AccessTools.Field(data, "quadMaterial");
            initializedField = AccessTools.Field(data, "alreadyInitialized");
            outOfRange = AccessTools.Method(data, "OutOfRange");
            LookScatters();
            Log.Info(quadData != null && materialField != null && outOfRange != null
                ? "Parallax found: terrain holes keep its quads in step"
                : "Parallax found but its quad data is not where expected: terrain holes may show its quads wrong");
        }
    }
}
