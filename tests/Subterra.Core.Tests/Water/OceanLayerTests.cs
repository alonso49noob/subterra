using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Environment;
using Subterra.Core.Model;
using Subterra.Core.Water;
using Xunit;

namespace Subterra.Core.Tests.Water
{
    public class OceanLayerTests
    {
        private static readonly LayerSpec Ice = new LayerSpec { Name = "IcyCrust", Top = 0, Bottom = 20000 };
        private static readonly LayerSpec Mantle = new LayerSpec { Name = "RockyMantle", Top = 20000, Bottom = 200000 };
        private static readonly LayerSpec Core = new LayerSpec { Name = "Core", Top = 200000, Bottom = 300000 };
        private static readonly Strata Vall = new Strata(new List<LayerSpec> { Ice, Mantle, Core });
        private static readonly OceanSpec Spec = new OceanSpec { CeilingPole = 1000, CeilingEquator = 10000, Thickness = 30000 };

        [Fact]
        public void TheOceanCutsTheLayersItCrosses()
        {
            LayerSpec ocean = OceanLayer.For(Spec);
            List<LayerSpan> spans = OceanLayer.Insert(Vall.Spans(""), ocean, 9844, 39844);
            Assert.Equal(new[] { "IcyCrust", OceanLayer.Name, "RockyMantle", "Core" }, spans.Select(s => s.Layer.Name));
            Assert.Equal(9844, spans[0].Bottom, 3);
            Assert.Equal(9844, spans[1].Top, 3);
            Assert.Equal(39844, spans[1].Bottom, 3);
            Assert.Equal(39844, spans[2].Top, 3);
        }

        [Fact]
        public void ALayerInsideTheOceanIsHidden()
        {
            var thin = new Strata(new List<LayerSpec> { Ice, new LayerSpec { Name = "Slush", Top = 20000, Bottom = 25000 }, Mantle });
            List<LayerSpan> spans = OceanLayer.Insert(thin.Spans(""), OceanLayer.For(Spec), 9844, 39844);
            Assert.DoesNotContain(spans, s => s.Layer.Name == "Slush");
        }

        [Fact]
        public void TheOceanIsTheLayerInItsRange()
        {
            LayerSpec ocean = OceanLayer.For(Spec);
            Assert.Same(Ice, OceanLayer.LayerAt(Vall, "", 5000, ocean, 9844, 39844));
            Assert.Same(ocean, OceanLayer.LayerAt(Vall, "", 20000, ocean, 9844, 39844));
            Assert.Same(Mantle, OceanLayer.LayerAt(Vall, "", 40000, ocean, 9844, 39844));
            Assert.Equal(0, ocean.Hardness);
            Assert.True(ocean.Yields.ContainsKey("Water"));
        }

        [Fact]
        public void ItsDepthsFollowTheCeiling()
        {
            var shell = new OceanShell(Spec, 300000);
            var (top, bottom) = OceanLayer.Depths(shell, 0, 302000);
            Assert.Equal(12000, top, 3);
            Assert.Equal(42000, bottom, 3);
        }
    }
}
