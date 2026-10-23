using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Xunit;

namespace Subterra.Core.Tests.Sdf
{
    public class TubeUvTests
    {
        private static readonly Vec3 Up = new Vec3(0, 1, 0);

        private static TubeSegment Piece(float x0, float x1, float start) =>
            new TubeSegment(new Vec3(x0, 0, 0), new Vec3(x1, 0, 0), Up, Up, 40, 40, 20, 20, 0, ZoneKind.TubeGallery) { AlongStart = start };

        [Fact]
        public void PastEndOnlyOnTheRoundedEnds()
        {
            TubeSegment piece = Piece(0, 100, 0);
            Assert.False(piece.PastEnd(new Vec3(50, 5, 15)));
            Assert.False(piece.PastEnd(new Vec3(104, 5, 15)), "a joint's outer side is still wall");
            Assert.True(piece.PastEnd(new Vec3(115, 5, 0)));
            Assert.True(piece.PastEnd(new Vec3(-15, 5, 0)));
        }

        [Fact]
        public void AlongRunsOnAcrossPieces()
        {
            TubeSegment first = Piece(0, 100, 0), second = Piece(100, 200, 100);
            Vec3 joint = new Vec3(100, 0, 5);
            Assert.Equal(first.WallUv(joint).Y, second.WallUv(joint).Y, 3);
            Assert.Equal(150, second.WallUv(new Vec3(150, 0, 0)).Y, 3);
        }

        [Fact]
        public void AcrossGoesOverTheFloorAndUpTheWallWithoutJumps()
        {
            TubeSegment tube = Piece(0, 100, 0);
            // From the floor centre out to the wall foot and up the arch: across grows steadily.
            float previous = float.MinValue;
            for (int k = 0; k <= 20; k++)
            {
                double angle = System.Math.PI / 2 * k / 20;
                Vec3 p = k == 0 ? new Vec3(50, 0, 0) : new Vec3(50, (float)(20 * System.Math.Sin(angle)), (float)(-20 * System.Math.Cos(angle)));
                float across = System.Math.Abs(tube.WallUv(p).X);
                Assert.True(across >= previous - 0.5f, $"step {k}: {across} after {previous}");
                previous = across;
            }

            Assert.True(previous > 20 && previous < 20 + 31.5f);
        }

        [Fact]
        public void FissuresAreTall()
        {
            Assert.True(new TubeSegment(new Vec3(0, 0, 0), new Vec3(100, 0, 0), Up, Up, 8, 8, 60, 60, 0, ZoneKind.TubeBranch).Tall);
            Assert.False(Piece(0, 100, 0).Tall);
        }
    }
}
