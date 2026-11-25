using Subterra.Core.Radar;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class BearingTests
    {
        [Theory]
        [InlineData(0, 0, 1, 0, 0)]
        [InlineData(0, 0, 0, 1, 90)]
        [InlineData(0, 0, -1, 0, 180)]
        [InlineData(0, 0, 0, -1, 270)]
        [InlineData(0, 179.5, 0, -179.5, 90)]
        [InlineData(0, -179.5, 0, 179.5, 270)]
        public void CardinalDirections(double lat1, double lon1, double lat2, double lon2, double expected)
        {
            Assert.Equal(expected, Bearing.Degrees(lat1, lon1, lat2, lon2), 6);
        }

        [Fact]
        public void NorthEastIsBetween()
        {
            Assert.InRange(Bearing.Degrees(0, 0, 0.1, 0.1), 44.9, 45.1);
        }

        [Fact]
        public void SamePointIsZero()
        {
            Assert.Equal(0, Bearing.Degrees(10, 20, 10, 20));
        }
    }
}
