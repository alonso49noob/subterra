using Subterra.Core.Meshing;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class IncandescenceTests
    {
        [Fact]
        public void CoolRockDoesNotGlow()
        {
            Assert.Equal(0, Incandescence.Intensity(300));
            Assert.Equal(0, Incandescence.Intensity(700));
        }

        [Fact]
        public void TheGlowGrowsWithHeatAndStaysModerate()
        {
            float previous = 0;
            foreach (double k in new[] { 900.0, 1200, 1800, 2500, 3000 })
            {
                float i = Incandescence.Intensity(k);
                Assert.True(i > previous, $"{k} K: {i}");
                previous = i;
            }

            Assert.Equal(Incandescence.MaxIntensity, Incandescence.Intensity(6000), 3);
        }

        [Fact]
        public void LikeTheDiagramsRedMantleOrangeOuterCoreYellowInnerCore()
        {
            Incandescence.Colour(1500, out float r1, out float g1, out float b1);
            Incandescence.Colour(3000, out float r2, out float g2, out float b2);
            Incandescence.Colour(4000, out float r3, out float g3, out float b3);
            Incandescence.Colour(6500, out float r4, out float g4, out float b4);
            Assert.True(r1 > 0.6f && g1 < 0.15f, "red");
            Assert.True(r2 > 0.95f && g2 > 0.3f && g2 < 0.55f && b2 < 0.15f, "orange");
            Assert.True(r3 > 0.95f && g3 > 0.6f && b3 < 0.4f, "yellow");
            Assert.True(g4 > 0.85f && b4 > 0.6f, "white-hot");
        }
    }
}
