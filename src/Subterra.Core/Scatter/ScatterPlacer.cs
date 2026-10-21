using System;
using System.Collections.Generic;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Scatter
{
    /// <summary>
    /// Places props over a chunk's cave surface: by triangle area (density per 100 m²), surface type
    /// from the face normal against the local vertical, zone filter, noise mask; seeded per chunk.
    /// </summary>
    public static class ScatterPlacer
    {
        public static List<ScatterInstance> Place(MeshData chunk, ScatterSpec spec, ulong chunkSeed,
            Func<Vec3, Vec3> localUp, Func<Vec3, ZoneKind> zoneAt, Func<Vec3, bool> inRubbleZone)
        {
            var result = new List<ScatterInstance>();
            var rng = new Rng(chunkSeed);
            int[][] mask = spec.NoiseFrequency > 0 ? Noise.Tables(Hash.Of(spec.Name)) : null;
            List<int> tris = chunk.Triangles;
            int end = Math.Min(chunk.SkirtTriangleStart, tris.Count);
            for (int i = 0; i < end; i += 3)
            {
                Vec3 a = chunk.Vertices[tris[i]], b = chunk.Vertices[tris[i + 1]], c = chunk.Vertices[tris[i + 2]];
                Vec3 cross = Vec3.Cross(b - a, c - a);
                float area = cross.Length * 0.5f;
                if (area <= 0)
                {
                    continue;
                }

                // Draw the count first so the random sequence does not depend on the filters below.
                double expected = spec.Density / 100.0 * area;
                int count = (int)Math.Floor(expected);
                if (rng.NextDouble() < expected - count)
                {
                    count++;
                }

                if (count == 0)
                {
                    continue;
                }

                Vec3 n = cross / (area * 2);
                Vec3 centroid = (a + b + c) / 3f;
                Vec3 up = localUp(centroid);
                float cos = Vec3.Dot(n, up);
                ScatterPlacement surface = cos > 0.7f ? ScatterPlacement.Floor : cos < -0.7f ? ScatterPlacement.Ceiling : ScatterPlacement.Wall;
                bool placementOk = spec.Placement == ScatterPlacement.SkylightRubble
                    ? surface == ScatterPlacement.Floor && inRubbleZone(centroid)
                    : surface == spec.Placement;
                Vec3 surfaceUp = surface == ScatterPlacement.Ceiling ? -n : n;

                for (int k = 0; k < count; k++)
                {
                    // Always consume the same draws per instance (determinism across filters).
                    double r1 = rng.NextDouble(), r2 = rng.NextDouble(), angle = rng.Range(0, 2 * Math.PI);
                    double scale = spec.Scale.Min + (Math.Max(spec.Scale.Min, spec.Scale.Max) - spec.Scale.Min) * Math.Pow(rng.NextDouble(), spec.ScalePower);
                    if (!placementOk)
                    {
                        continue;
                    }

                    if (r1 + r2 > 1)
                    {
                        r1 = 1 - r1;
                        r2 = 1 - r2;
                    }

                    Vec3 p = a + (b - a) * (float)r1 + (c - a) * (float)r2;
                    if (spec.Zones.Count > 0 && !spec.Zones.Contains(zoneAt(p)))
                    {
                        continue;
                    }

                    if (mask != null && (Noise.Fbm(p * (float)spec.NoiseFrequency, mask, 3) + 1) / 2 <= spec.NoiseThreshold)
                    {
                        continue;
                    }

                    // A prop's up follows the surface towards `surfaceUp`: the face normal (into the void),
                    // except on ceilings, where props hang below their origin and up points into the rock
                    // like the local vertical (lerping towards the normal there would pass through zero).
                    Vec3 instanceUp = Vec3.Lerp(up, surfaceUp, (float)spec.AlignToSurface).Normalized;
                    if (instanceUp.LengthSquared == 0)
                    {
                        instanceUp = surfaceUp;
                    }

                    Vec3 helper = Math.Abs(instanceUp.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
                    Vec3 t1 = Vec3.Cross(instanceUp, helper).Normalized;
                    Vec3 t2 = Vec3.Cross(t1, instanceUp);
                    Vec3 forward = (t1 * (float)Math.Cos(angle) + t2 * (float)Math.Sin(angle)).Normalized;
                    Vec3 seat = p - instanceUp * (float)(spec.Sink * scale);
                    result.Add(new ScatterInstance(seat, instanceUp, forward, (float)scale));
                }
            }

            return result;
        }

        public static ulong ChunkSeed(ChunkKey k, string scatterName) =>
            Hash.Combine(Hash.Of(k.NetworkId), (ulong)k.Lod, (ulong)(long)k.X, (ulong)(long)k.Y, (ulong)(long)k.Z, Hash.Of(scatterName));
    }
}
