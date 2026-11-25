using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Radar;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class DetectionStoreTests
    {
        private static SubsurfaceTarget T(string id, string network, int primitive, double lat = 0, double lon = 0) =>
            new SubsurfaceTarget { Id = id, NetworkId = network, PrimitiveIndex = primitive, Lat = lat, Lon = lon };

        private static Detection Hint(double u = 100, double dmin = 10, double dmax = 40) =>
            new Detection(DetectionLevel.Hint, u, dmin, dmax);

        private static Detection Det(double u = 50, double dmin = 15, double dmax = 30) =>
            new Detection(DetectionLevel.Detected, u, dmin, dmax);

        [Fact]
        public void HintThenDetected()
        {
            var store = new DetectionStore();
            var raised = new List<DetectionLevel>();
            store.LevelRaised += (n, l) => raised.Add(l);

            Assert.True(store.Apply("Mun", T("a", "net1", 0), Hint()));
            Assert.True(store.Apply("Mun", T("a", "net1", 0), Det()));

            Assert.Equal(DetectionLevel.Detected, store.Get("net1").Level);
            Assert.Equal(new[] { DetectionLevel.Hint, DetectionLevel.Detected }, raised.ToArray());
        }

        [Fact]
        public void NoneToDetectedFiresOnce()
        {
            var store = new DetectionStore();
            var raised = new List<DetectionLevel>();
            store.LevelRaised += (n, l) => raised.Add(l);
            store.Apply("Mun", T("a", "net1", 0), Det());
            store.Apply("Mun", T("b", "net1", 1), Det());
            Assert.Equal(new[] { DetectionLevel.Detected }, raised.ToArray());
        }

        [Fact]
        public void DetectedNotLoweredByHint()
        {
            var store = new DetectionStore();
            var raised = 0;
            store.Apply("Mun", T("a", "net1", 0), Det());
            store.LevelRaised += (n, l) => raised++;
            Assert.False(store.Apply("Mun", T("a", "net1", 0), Hint(u: 500)));
            Assert.Equal(DetectionLevel.Detected, store.Get("net1").Level);
            Assert.Equal(0, raised);
        }

        [Fact]
        public void SmallerUncertaintyKept()
        {
            var store = new DetectionStore();
            Assert.True(store.Apply("Mun", T("a", "net1", 0), Det(u: 50, dmin: 15, dmax: 30)));
            Assert.False(store.Apply("Mun", T("a", "net1", 0), Det(u: 80, dmin: 1, dmax: 2)));
            var d = store.Get("net1");
            Assert.Equal(50, d.Uncertainty);
            Assert.Equal(15, d.DepthMin);
            Assert.Equal(30, d.DepthMax);

            Assert.True(store.Apply("Mun", T("a", "net1", 0), Det(u: 20, dmin: 18, dmax: 25)));
            d = store.Get("net1");
            Assert.Equal(20, d.Uncertainty);
            Assert.Equal(18, d.DepthMin);
            Assert.Equal(25, d.DepthMax);
        }

        [Fact]
        public void PrimitiveBitsAccumulate()
        {
            var store = new DetectionStore();
            store.Apply("Mun", T("a", "net1", 3), Det());
            Assert.True(store.Apply("Mun", T("b", "net1", 7), Det()));
            Assert.False(store.Apply("Mun", T("b", "net1", 7), Det()));
            Assert.True(store.IsPrimitiveDetected("net1", 3));
            Assert.True(store.IsPrimitiveDetected("net1", 7));
            Assert.False(store.IsPrimitiveDetected("net1", 4));
            Assert.False(store.IsPrimitiveDetected("net1", 500));
            Assert.False(store.IsPrimitiveDetected("net1", -1));
            Assert.False(store.IsPrimitiveDetected("other", 3));
        }

        [Fact]
        public void HintDoesNotSetBits()
        {
            var store = new DetectionStore();
            store.Apply("Mun", T("a", "net1", 3), Hint());
            Assert.False(store.IsPrimitiveDetected("net1", 3));
            Assert.Equal(DetectionLevel.Hint, store.Get("net1").Level);
        }

        [Fact]
        public void HexRoundTrip()
        {
            var bits = new BitArray(20);
            bits[0] = true;
            bits[3] = true;
            bits[9] = true;
            bits[19] = true;
            var d = new NetworkDetection { NetworkId = "n", Body = "Mun", Primitives = bits };
            var hex = d.PrimitivesHex;
            Assert.Equal("090208", hex);
            var back = NetworkDetection.FromHex(hex, out var ok);
            Assert.True(ok);
            for (var i = 0; i < 20; i++) Assert.Equal(bits[i], back[i]);
            for (var i = 20; i < back.Length; i++) Assert.False(back[i]);
        }

        [Fact]
        public void EmptyHexIsValidAndEmpty()
        {
            var back = NetworkDetection.FromHex("", out var ok);
            Assert.True(ok);
            Assert.Equal(0, back.Length);
            Assert.Equal("", new NetworkDetection().PrimitivesHex);
        }

        [Theory]
        [InlineData("zz")]
        [InlineData("abc")]
        [InlineData("0g")]
        [InlineData(null)]
        public void GarbageHexIsIgnored(string hex)
        {
            var back = NetworkDetection.FromHex(hex, out var ok);
            Assert.False(ok);
            Assert.NotNull(back);
            Assert.Equal(0, back.Length);
        }

        [Fact]
        public void NoNetworkIdIgnored()
        {
            var store = new DetectionStore();
            var raised = 0;
            store.LevelRaised += (n, l) => raised++;
            Assert.False(store.Apply("Mun", T("col", null, -1), Det()));
            Assert.False(store.Apply("Mun", T("col", "", -1), Det()));
            Assert.Empty(store.All);
            Assert.Equal(0, raised);
        }

        [Fact]
        public void NoneDetectionIgnored()
        {
            var store = new DetectionStore();
            Assert.False(store.Apply("Mun", T("a", "net1", 0), Detection.None));
            Assert.Null(store.Get("net1"));
        }

        [Fact]
        public void CentroidIsMeanOfTargets()
        {
            var store = new DetectionStore();
            store.Apply("Mun", T("a", "net1", 0, lat: 10, lon: 20), Det());
            store.Apply("Mun", T("b", "net1", 1, lat: 20, lon: 40), Hint());
            var d = store.Get("net1");
            Assert.Equal(15, d.Lat, 6);
            Assert.Equal(30, d.Lon, 6);
        }

        [Fact]
        public void CentroidAcrossAntimeridian()
        {
            var store = new DetectionStore();
            store.Apply("Mun", T("a", "net1", 0, lat: 0, lon: 179), Det());
            store.Apply("Mun", T("b", "net1", 1, lat: 0, lon: -179), Det());
            var d = store.Get("net1");
            Assert.Equal(180, Math.Abs(d.Lon), 6);
        }

        [Fact]
        public void SameTargetTwiceDoesNotSkewCentroid()
        {
            var store = new DetectionStore();
            store.Apply("Mun", T("a", "net1", 0, lat: 10, lon: 10), Det());
            store.Apply("Mun", T("a", "net1", 0, lat: 10, lon: 10), Det(u: 5));
            store.Apply("Mun", T("b", "net1", 1, lat: 20, lon: 10), Det());
            Assert.Equal(15, store.Get("net1").Lat, 6);
        }

        [Fact]
        public void PutThenGetAndKeepApplying()
        {
            var store = new DetectionStore();
            var bits = new BitArray(8);
            bits[2] = true;
            store.Put(new NetworkDetection
            {
                NetworkId = "net1", Body = "Mun", Level = DetectionLevel.Hint, Uncertainty = 90,
                DepthMin = 5, DepthMax = 50, Lat = 12, Lon = 34, Primitives = bits,
            });
            var d = store.Get("net1");
            Assert.Equal(DetectionLevel.Hint, d.Level);
            Assert.True(store.IsPrimitiveDetected("net1", 2));
            Assert.True(store.Apply("Mun", T("a", "net1", 5, lat: 12, lon: 34), Det()));
            Assert.Equal(DetectionLevel.Detected, store.Get("net1").Level);
            Assert.True(store.IsPrimitiveDetected("net1", 2));
            Assert.True(store.IsPrimitiveDetected("net1", 5));
        }

        [Fact]
        public void AllIsASnapshot()
        {
            var store = new DetectionStore();
            store.Apply("Mun", T("a", "net1", 0), Det());
            var snapshot = store.All.ToList();
            store.Apply("Mun", T("b", "net2", 0), Det());
            Assert.Single(snapshot);
            Assert.Equal(2, store.All.Count());

            // Mutating a returned copy does not change the store.
            snapshot[0].Level = DetectionLevel.None;
            Assert.Equal(DetectionLevel.Detected, store.Get("net1").Level);

            // Enumerating while another call writes does not throw.
            foreach (var d in store.All) store.Apply("Mun", T("c", "net3", 0), Hint());
        }

        [Fact]
        public void GetUnknownIsNull()
        {
            Assert.Null(new DetectionStore().Get("nope"));
        }

        [Fact]
        public void ClearForgetsEverythingAndStaysUsable()
        {
            var store = new DetectionStore();
            var raised = 0;
            store.Apply("Mun", T("a", "net1", 0), Det());
            store.Apply("Mun", T("b", "net2", 0), Hint());
            store.LevelRaised += (n, l) => raised++;

            store.Clear();

            Assert.Null(store.Get("net1"));
            Assert.Empty(store.All);
            Assert.False(store.IsPrimitiveDetected("net1", 0));
            Assert.Equal(0, raised);
            Assert.True(store.Apply("Mun", T("a", "net1", 0), Hint()));
            Assert.Equal(1, raised);
            Assert.Equal(DetectionLevel.Hint, store.Get("net1").Level);
        }

        [Fact]
        public void VersionRisesOnlyWhenTheRecordsChange()
        {
            var store = new DetectionStore();
            int v0 = store.Version;

            Assert.True(store.Apply("Mun", T("a", "net1", 0, 10, 20), Hint(u: 100)));
            int v1 = store.Version;
            Assert.True(v1 > v0);

            // The same target seen again with nothing better: nothing changed, so the version stays.
            Assert.False(store.Apply("Mun", T("a", "net1", 0, 10, 20), Hint(u: 100)));
            Assert.Equal(v1, store.Version);

            // A second target moves the centroid even if the level and the uncertainty do not improve.
            store.Apply("Mun", T("b", "net1", 1, 12, 22), Hint(u: 100));
            int v2 = store.Version;
            Assert.True(v2 > v1);

            Assert.True(store.Apply("Mun", T("a", "net1", 0, 10, 20), Det()));
            int v3 = store.Version;
            Assert.True(v3 > v2);

            store.Put(new NetworkDetection { NetworkId = "net2", Body = "Mun", Level = DetectionLevel.Hint });
            int v4 = store.Version;
            Assert.True(v4 > v3);

            store.Clear();
            Assert.True(store.Version > v4);
        }
    }
}
