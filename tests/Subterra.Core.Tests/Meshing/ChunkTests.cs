using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class ChunkPlannerTests
    {
        private static readonly Aabb Net2km = new Aabb(new Vec3(0, -100, 0), new Vec3(2000, 100, 300));
        private static readonly Vec3 Camera = new Vec3(1000, 10, 150);

        private static HashSet<ChunkKey> Desired(Vec3 camera)
        {
            var set = new HashSet<ChunkKey>();
            ChunkPlanner.Desired("n", Net2km, camera, _ => true, set);
            return set;
        }

        [Fact]
        public void CameraChunkIsLod0AndFarIsLod3()
        {
            var set = Desired(Camera);
            Assert.Contains(set, k => k.Lod == 0 && k.Bounds.Contains(Camera));
            foreach (var k in set.Where(k => k.Bounds.DistanceTo(Camera) > 1100))
            {
                Assert.Equal(3, k.Lod);
            }

            Assert.Contains(Desired(new Vec3(100, 10, 150)), k => k.Lod == 3);
        }

        [Fact]
        public void NoOverlaps()
        {
            var list = Desired(Camera).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    Aabb a = list[i].Bounds, b = list[j].Bounds;
                    float dx = System.Math.Min(a.Max.X, b.Max.X) - System.Math.Max(a.Min.X, b.Min.X);
                    float dy = System.Math.Min(a.Max.Y, b.Max.Y) - System.Math.Max(a.Min.Y, b.Min.Y);
                    float dz = System.Math.Min(a.Max.Z, b.Max.Z) - System.Math.Max(a.Min.Z, b.Min.Z);
                    Assert.False(dx > 0 && dy > 0 && dz > 0, $"{list[i]} overlaps {list[j]}");
                }
            }
        }

        [Fact]
        public void NoSurfaceNothing()
        {
            var set = new HashSet<ChunkKey>();
            ChunkPlanner.Desired("n", Net2km, Camera, _ => false, set);
            Assert.Empty(set);
        }

        [Fact] public void FarCameraNothing() => Assert.Empty(Desired(new Vec3(7100, 0, 150)));

        [Fact]
        public void CollidersAreLod0AroundVessel()
        {
            var set = new HashSet<ChunkKey>();
            var vessel = new Vec3(500, 5, 100);
            ChunkPlanner.Colliders("n", new[] { vessel }, 150, _ => true, set);
            Assert.NotEmpty(set);
            Assert.All(set, k => { Assert.Equal(0, k.Lod); Assert.True(k.Bounds.DistanceTo(vessel) <= 150); });
        }
    }

    public class ChunkCacheTests
    {
        private static ChunkKey K(int x) => new ChunkKey("n", 0, x, 0, 0);

        [Fact]
        public void EvictsOldestFirst()
        {
            var cache = new ChunkCache<string>(100);
            cache.Put(K(1), "a", 40);
            cache.Put(K(2), "b", 40);
            cache.Put(K(3), "c", 40);
            var evicted = cache.EvictOver(new HashSet<ChunkKey>()).ToList();
            Assert.Equal(K(1), Assert.Single(evicted).Key);
            Assert.True(cache.Bytes <= 100);
        }

        [Fact]
        public void TouchMakesRecent()
        {
            var cache = new ChunkCache<string>(100);
            cache.Put(K(1), "a", 40);
            cache.Put(K(2), "b", 40);
            cache.Touch(K(1), 10);
            cache.Put(K(3), "c", 40);
            Assert.Equal(K(2), Assert.Single(cache.EvictOver(null)).Key);
        }

        [Fact]
        public void KeepIsNeverEvicted()
        {
            var cache = new ChunkCache<string>(100);
            cache.Put(K(1), "a", 40);
            cache.Put(K(2), "b", 40);
            cache.Put(K(3), "c", 40);
            var keep = new HashSet<ChunkKey> { K(1), K(2), K(3) };
            Assert.Empty(cache.EvictOver(keep));
            Assert.Equal(120, cache.Bytes);
            keep.Remove(K(3));
            Assert.Equal(K(3), Assert.Single(cache.EvictOver(keep)).Key);
        }
    }
}
