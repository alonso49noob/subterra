using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Radar;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Test-only steps for the subsurface radar.</summary>
    [StepProvider]
    internal static class RadarSteps
    {
        internal static void Register()
        {
            StepRegistry.Register("radarMark", RadarMark);
            StepRegistry.Register("setResourceAmount", SetResourceAmount);
            StepRegistry.Register("subPoint", SubPointStep);
            StepRegistry.Register("subPointAt", SubPointAt);
            StepRegistry.Register("radarSetLastScan", RadarSetLastScan);
            StepRegistry.Register("cavityFailNext", CavityFailNext);
        }

        /// <summary>Key prefix of the points subPoint keeps in TestContext.Snapshots (x = lat, y = lon, z = UT).</summary>
        internal const string PointPrefix = "sub:";

        // subPoint name = a - records where the active vessel is over its body (KSP's own latitude/longitude of the
        // vessel) and the UT, as point a for coverageAt at = a / radarSetLastScan point = a. It also logs, for this
        // point and every point recorded before, where OrbitalScanner.SubPoint puts the vessel at that point's UT
        // with the orbit it has now, and how far that is from where the vessel really was: the check of the
        // body-rotation correction for past UTs. With vessel = Radar D it records that (unloaded) vessel's point now,
        // from its orbit with KSP's own GetLatitude/GetLongitude, and logs nothing else.
        private static IEnumerator SubPointStep(ConfigNode node, TestContext ctx)
        {
            string name = node.RequireArg("name");
            double now = Planetarium.GetUniversalTime();
            string other = node.Arg("vessel");
            if (other != null)
            {
                Vessel o = RadarAssertions.FindVessel(other) ?? throw new ArgumentException("no vessel named " + other);
                Vector3d radial = o.orbit.getRelativePositionAtUT(now).xzy;
                double oLat = o.mainBody.GetLatitude(radial, true), oLon = o.mainBody.GetLongitude(radial, true);
                ctx.Snapshots[PointPrefix + name] = new Vector3d(oLat, oLon, now);
                ctx.Pass(Invariant($"{name}: {other} ({(o.loaded ? "loaded" : "unloaded")}) over {o.mainBody.bodyName} lat {oLat:F3} lon {oLon:F3} at UT {now:F0}"));
                yield break;
            }

            Vessel v = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            ctx.Snapshots[PointPrefix + name] = new Vector3d(v.latitude, v.longitude, now);
            var lines = new List<string>();
            foreach (var pair in ctx.Snapshots.Where(p => p.Key.StartsWith(PointPrefix)).OrderBy(p => p.Value.z))
            {
                OrbitalScanner.SubPoint(v.orbit, pair.Value.z, out double lat, out double lon, out double alt);
                double km = Distance(pair.Value.x, pair.Value.y, lat, lon) * v.mainBody.Radius / 1000;
                lines.Add(Invariant($"{pair.Key.Substring(PointPrefix.Length)} (UT {pair.Value.z:F0}, {now - pair.Value.z:F0} s ago): was lat {pair.Value.x:F3} lon {pair.Value.y:F3}, predicted lat {lat:F3} lon {lon:F3}, off {km:F2} km"));
            }

            ctx.Pass(Invariant($"{name}: {v.mainBody.bodyName} lat {v.latitude:F3} lon {v.longitude:F3} alt {v.altitude:F0} m at UT {now:F0}; ") + string.Join("; ", lines));
            yield break;
        }

        // subPointAt name = ghost back = 600 / subPointAt name = pe1 periapsisAgo = 1 - records as a point where
        // OrbitalScanner.SubPoint puts the active vessel, with the orbit it has now, back seconds ago, or at its
        // periapsis passage that many orbits before the coming one (a predicted point, not a flown one: subPoint
        // logs show the prediction matches flown points).
        private static IEnumerator SubPointAt(ConfigNode node, TestContext ctx)
        {
            string name = node.RequireArg("name");
            Vessel v = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            Orbit orbit = v.orbit;
            double now = Planetarium.GetUniversalTime();
            double ut = node.HasValue("periapsisAgo")
                ? now + orbit.timeToPe - node.ArgDouble("periapsisAgo", 1) * orbit.period
                : now - node.ArgDouble("back", 0);
            OrbitalScanner.SubPoint(orbit, ut, out double lat, out double lon, out double alt);
            ctx.Snapshots[PointPrefix + name] = new Vector3d(lat, lon, ut);
            ctx.Pass(Invariant($"{name}: predicted {v.mainBody.bodyName} lat {lat:F3} lon {lon:F3} alt {alt:F0} m at UT {ut:F0} ({now - ut:F0} s ago)"));
            yield break;
        }

        // radarSetLastScan part = SubterraEco point = a - sets lastScanUT of the radar modules on that part of the tracked
        // vessel to the UT of point a (subPoint), so the orbital scanner walks the track since then.
        private static IEnumerator RadarSetLastScan(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            string partName = node.RequireArg("part");
            string point = node.RequireArg("point");
            if (!ctx.Snapshots.TryGetValue(PointPrefix + point, out Vector3d p))
            {
                throw new ArgumentException("no point " + point + " (subPoint first)");
            }

            var modules = vessel.parts.Where(x => x.partInfo != null && x.partInfo.name == partName)
                .SelectMany(x => x.Modules.OfType<ModuleSubterraRadar>()).ToList();
            if (modules.Count == 0)
            {
                throw new InvalidOperationException($"no radar module on a {partName} of {vessel.GetDisplayName()}");
            }

            foreach (ModuleSubterraRadar m in modules)
            {
                m.lastScanUT = p.z;
            }

            ctx.Pass(Invariant($"lastScanUT of {modules.Count} module(s) on {partName} = {p.z:F0} ({Planetarium.GetUniversalTime() - p.z:F0} s ago)"));
            yield break;
        }

        // cavityFailNext count = 1 - the next count SubterraCavity lookups throw (CavityAbundance.FailNext): the resource
        // patch must answer 0 for them, never the stock value.
        private static IEnumerator CavityFailNext(ConfigNode node, TestContext ctx)
        {
            int count = (int)node.ArgDouble("count", 1);
            CavityAbundance.FailNext(count);
            ctx.Pass(Invariant($"the next {count} SubterraCavity lookup(s) will fail"));
            yield break;
        }

        // Great-circle angle in radians between two points given in degrees.
        internal static double Distance(double lat1, double lon1, double lat2, double lon2)
        {
            const double Rad = Math.PI / 180;
            double a = Math.Sin(lat1 * Rad) * Math.Sin(lat2 * Rad)
                + Math.Cos(lat1 * Rad) * Math.Cos(lat2 * Rad) * Math.Cos((lon1 - lon2) * Rad);
            return Math.Acos(Math.Max(-1, Math.Min(1, a)));
        }

        // radarMark body = Mun lat = 0 lon = 0 altitude = 30000 halfWidth = 2000 instrument = OrbitalRadar hint = MN-test#0
        // uncertainty = 1000 depth = 50 level = Hint - marks the coverage grid as if a scan had passed there at that
        // altitude and, if hint is given (<networkId>#<primitiveIndex>), applies a detection of a synthetic target of
        // that network at lat/lon (level Hint or Detected, at that depth, with that uncertainty in metres).
        // halfWidth = 0 skips the swath (a detection without coverage).
        private static IEnumerator RadarMark(ConfigNode node, TestContext ctx)
        {
            RadarScenario scenario = RadarScenario.Instance ?? throw new InvalidOperationException("no RadarScenario (wrong scene?)");
            string bodyName = node.RequireArg("body");
            CelestialBody body = FlightGlobals.GetBodyByName(bodyName) ?? throw new ArgumentException("unknown body " + bodyName);
            double lat = node.ArgDouble("lat", 0);
            double lon = node.ArgDouble("lon", 0);
            double altitude = node.ArgDouble("altitude", 30000);
            double halfWidth = node.ArgDouble("halfWidth", 2000);
            InstrumentKind kind = ParseInstrument(node);

            string detail = Invariant($"{bodyName} {kind} lat {lat:F3} lon {lon:F3}");
            if (halfWidth > 0)
            {
                scenario.Coverage(bodyName, kind).MarkSwath(lat, lon, halfWidth, body.Radius, altitude);
                detail += Invariant($" altitude {altitude:F0} m halfWidth {halfWidth:F0} m");
            }

            string hint = node.Arg("hint");
            if (!string.IsNullOrEmpty(hint))
            {
                int hash = hint.LastIndexOf('#');
                if (hash <= 0 || !int.TryParse(hint.Substring(hash + 1), out int index) || index < 0)
                {
                    throw new ArgumentException("hint must be <networkId>#<primitiveIndex>: " + hint);
                }

                if (!Enum.TryParse(node.Arg("level", "Hint"), true, out DetectionLevel level) || level == DetectionLevel.None)
                {
                    throw new ArgumentException("level must be Hint or Detected");
                }

                double depth = node.ArgDouble("depth", 50);
                double uncertainty = node.ArgDouble("uncertainty", 1000);
                var target = new SubsurfaceTarget
                {
                    Id = hint,
                    Kind = TargetKind.Void,
                    NetworkId = hint.Substring(0, hash),
                    PrimitiveIndex = index,
                    Lat = lat,
                    Lon = lon,
                    Depth = depth,
                };
                scenario.Detections.Apply(bodyName, target, new Detection(level, uncertainty, depth, depth));
                detail += Invariant($", {level} {hint} depth {depth:F0} m uncertainty {uncertainty:F0} m");
            }

            ctx.Pass(detail);
            yield break;
        }

        // setResourceAmount name = ElectricCharge amount = 0 - sets the amount of that resource in every part of the
        // tracked vessel that holds it (capped at the part's capacity); a fraction = 0.5 fills that share instead.
        // With vessel = Radar D and that vessel unloaded, the amounts in its saved part snapshots are set instead (what
        // it will have when it loads).
        private static IEnumerator SetResourceAmount(ConfigNode node, TestContext ctx)
        {
            string name = node.RequireArg("name");
            string other = node.Arg("vessel");
            Vessel unloaded = other == null ? null : RadarAssertions.FindVessel(other) ?? throw new ArgumentException("no vessel named " + other);
            if (unloaded != null && !unloaded.loaded)
            {
                var saved = unloaded.protoVessel.protoPartSnapshots.SelectMany(p => p.resources).Where(r => r.resourceName == name).ToList();
                if (saved.Count == 0)
                {
                    throw new InvalidOperationException($"no {name} saved on {other}");
                }

                foreach (ProtoPartResourceSnapshot r in saved)
                {
                    r.amount = node.HasValue("fraction") ? r.maxAmount * node.ArgDouble("fraction", 1) : Math.Min(r.maxAmount, node.ArgDouble("amount", 0));
                }

                yield return null;
                ctx.Pass(Invariant($"{name} saved on {other} (unloaded), {saved.Count} part(s): {saved.Sum(r => r.amount):F1}/{saved.Sum(r => r.maxAmount):F1}"));
                yield break;
            }

            Vessel vessel = unloaded ?? ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            var resources = vessel.parts.SelectMany(p => p.Resources.Cast<PartResource>()).Where(r => r.resourceName == name).ToList();
            if (resources.Count == 0)
            {
                throw new InvalidOperationException($"no {name} on {vessel.GetDisplayName()}");
            }

            foreach (PartResource r in resources)
            {
                r.amount = node.HasValue("fraction") ? r.maxAmount * node.ArgDouble("fraction", 1) : Math.Min(r.maxAmount, node.ArgDouble("amount", 0));
            }

            yield return null;
            ctx.Pass(Invariant($"{name} in {resources.Count} part(s): {resources.Sum(r => r.amount):F1}/{resources.Sum(r => r.maxAmount):F1}"));
        }

        internal static InstrumentKind ParseInstrument(ConfigNode node)
        {
            string text = node.Arg("instrument", "OrbitalRadar");
            if (!Enum.TryParse(text, true, out InstrumentKind kind) || !Enum.IsDefined(typeof(InstrumentKind), kind))
            {
                throw new ArgumentException("unknown instrument " + text);
            }

            return kind;
        }
    }
}
