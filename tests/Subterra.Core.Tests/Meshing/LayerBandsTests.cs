using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Environment;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class LayerBandsTests
    {
        private static readonly LayerSpec Regolith = new LayerSpec { Name = "Regolith", Top = 0, Bottom = 8 };
        private static readonly LayerSpec Basalt = new LayerSpec { Name = "MareBasalt", Top = 8, Bottom = 1500 };
        private static readonly LayerSpec Breccia = new LayerSpec { Name = "Megaregolith", Top = 1500, Bottom = 3000 };
        private static readonly Strata Column = new Strata(new List<LayerSpec> { Regolith, Basalt, Breccia });

        // A strip of quads down a wall from depth `from` to `to` (y is up: depth = -y).
        private static MeshData Wall(float from, float to, float step)
        {
            var m = new MeshData();
            for (float d = from; d < to; d += step)
            {
                int i = m.Vertices.Count;
                m.Vertices.Add(new Vec3(0, -d, 0));
                m.Vertices.Add(new Vec3(1, -d, 0));
                m.Vertices.Add(new Vec3(0, -(d + step), 0));
                m.Vertices.Add(new Vec3(1, -(d + step), 0));
                m.Triangles.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
            }

            m.SkirtTriangleStart = m.Triangles.Count;
            return m;
        }

        private static List<(LayerSpec Layer, int[] Triangles)> Split(MeshData m) =>
            LayerBands.Split(m, p => -p.Y, d => Column.LayerAt(d, null));

        [Fact]
        public void OneLayerNeedsNoSplit()
        {
            Assert.Null(Split(Wall(100, 140, 2)));
        }

        [Fact]
        public void AWallAcrossABoundaryChangesRockAtTheBoundary()
        {
            List<(LayerSpec Layer, int[] Triangles)> bands = Split(Wall(1480, 1520, 2));
            Assert.NotNull(bands);
            Assert.Equal(new[] { Basalt, Breccia }, bands.Select(b => b.Layer));
            // Every triangle kept once, and each band's triangles lie on its side of 1500 m.
            MeshData wall = Wall(1480, 1520, 2);
            Assert.Equal(wall.Triangles.Count, bands.Sum(b => b.Triangles.Length));
            foreach ((LayerSpec layer, int[] tris) in bands)
            {
                for (int i = 0; i < tris.Length; i += 3)
                {
                    float depth = -(wall.Vertices[tris[i]].Y + wall.Vertices[tris[i + 1]].Y + wall.Vertices[tris[i + 2]].Y) / 3;
                    Assert.InRange(depth, layer.Top, layer.Bottom);
                }
            }
        }

        [Fact]
        public void AboveTheSurfaceCountsAsTheTopLayer()
        {
            // The rim of a shaft sticks out of the ground a little: depth below 0 is the surface layer, not "no layer".
            List<(LayerSpec Layer, int[] Triangles)> bands = Split(Wall(-4, 12, 2));
            Assert.Equal(new[] { Regolith, Basalt }, bands.Select(b => b.Layer));
        }

        [Fact]
        public void AnEmptyMeshNeedsNoSplit()
        {
            Assert.Null(Split(new MeshData()));
        }
    }
}
