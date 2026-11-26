using System;
using System.Collections.Generic;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Water
{
    /// <summary>An ice tower hanging from an ocean's ceiling.</summary>
    public readonly struct OceanTower
    {
        public OceanTower(Vec3d direction, double baseRadius, double length)
        {
            Direction = direction;
            BaseRadius = baseRadius;
            Length = length;
        }

        /// <summary>Unit direction of its axis from the body's centre.</summary>
        public Vec3d Direction { get; }

        /// <summary>Radius (m) where it leaves the ceiling.</summary>
        public double BaseRadius { get; }

        /// <summary>How far (m) it hangs below the ceiling.</summary>
        public double Length { get; }
    }

    /// <summary>A hydrothermal vent on an ocean's floor: a chimney of <see cref="Radius"/> at its base, <see cref="Height"/> tall.</summary>
    public readonly struct OceanVent
    {
        public OceanVent(Vec3d direction, double radius, double height, ulong seed)
        {
            Direction = direction;
            Radius = radius;
            Height = height;
            Seed = seed;
        }

        public Vec3d Direction { get; }

        public double Radius { get; }

        public double Height { get; }

        public ulong Seed { get; }
    }

    /// <summary>A pillar of ice from an ocean's ceiling down to its floor (Pol), on the line from the body's centre.</summary>
    public readonly struct OceanPillar
    {
        public OceanPillar(Vec3d direction, double radius, ulong seed)
        {
            Direction = direction;
            Radius = radius;
            Seed = seed;
        }

        /// <summary>Unit direction of its axis from the body's centre.</summary>
        public Vec3d Direction { get; }

        /// <summary>Radius (m) at its middle.</summary>
        public double Radius { get; }

        public ulong Seed { get; }

        /// <summary>
        /// Radius at <paramref name="t"/> (0 at the ceiling, 1 at the floor) and angle <paramref name="angle"/> (radians):
        /// flared where it meets the ceiling and the floor, lumpy along its length.
        /// </summary>
        public double RadiusAt(double t, double angle, double length)
        {
            // Flares over about two radii at each end.
            double flareLength = Math.Max(1e-3, 2 * Radius / Math.Max(1, length));
            double flare = Math.Exp(-t / flareLength) + Math.Exp(-(1 - t) / flareLength);
            float lump = Noise.Fbm(new Vec3((float)Math.Cos(angle) * 1.5f, (float)Math.Sin(angle) * 1.5f, (float)(t * length / 150)), Seed, 3);
            return Radius * (1 + 1.2 * flare) * (1 + 0.5 * lump);
        }
    }

    /// <summary>
    /// What hangs and stands in an ocean under the ice (Lucas): ice towers hanging from the ceiling (giant icicles, like
    /// the brinicles under Earth's sea ice; of rock under Tylo's rock ceiling) and, in Pol's shallow ocean, pillars joining
    /// the ceiling to the floor. Placed by hashing cells of a grid on the body (one candidate per cell, kept with the
    /// body's chance): the same in every save, nothing stored.
    /// </summary>
    public static class OceanFeatures
    {
        /// <summary>Size (m) of a tower's cell, and of a pillar's.</summary>
        public const double TowerSpacing = 120, PillarSpacing = 1500;

        /// <summary>
        /// How far (m) the ceiling hangs lower at <paramref name="at"/> (body frame, on or near the ceiling) because of the
        /// towers there; 0 away from them.
        /// </summary>
        public static double TowerHang(OceanShell ocean, Vec3d at, ulong seed)
        {
            OceanSpec spec = ocean.Spec;
            if (spec.TowerChance <= 0)
            {
                return 0;
            }

            double hang = 0;
            Vec3d dir = at.Normalized;
            foreach (OceanTower tower in TowersNear(ocean, dir, seed))
            {
                double d = (dir - tower.Direction).Length * ocean.BodyRadius;
                if (d < tower.BaseRadius)
                {
                    // A pointed, slightly bulging cone.
                    hang = Math.Max(hang, tower.Length * Math.Pow(1 - d / tower.BaseRadius, 1.8));
                }
            }

            return hang;
        }

        /// <summary>The towers in the cells around <paramref name="dir"/> (out to <paramref name="rings"/> cells).</summary>
        public static IEnumerable<OceanTower> TowersNear(OceanShell ocean, Vec3d dir, ulong seed, int rings = 1)
        {
            OceanSpec spec = ocean.Spec;
            if (spec.TowerChance <= 0)
            {
                yield break;
            }

            foreach ((Vec3d towerDir, ulong h) in Candidates(ocean, dir.Normalized, TowerSpacing, seed + 101, spec.TowerChance, rings))
            {
                // Base 15-60 m in radius, 30-100% of the longest length.
                yield return new OceanTower(towerDir, 15 + 45 * Unit(Hash.Mix(h + 3)), spec.TowerLength * (0.3 + 0.7 * Unit(Hash.Mix(h + 4))));
            }
        }

        /// <summary>Metres between the cells of the floor that may hold a vent.</summary>
        public const double VentSpacing = 350;

        /// <summary>The vents within <paramref name="reach"/> metres of <paramref name="dir"/> (chimneys 3–9 m wide at the base, 8–35 m tall).</summary>
        public static List<OceanVent> VentsNear(OceanShell ocean, Vec3d dir, double reach, ulong seed)
        {
            var result = new List<OceanVent>();
            if (ocean.Spec.VentChance <= 0)
            {
                return result;
            }

            dir = dir.Normalized;
            int rings = (int)Math.Ceiling(reach / VentSpacing) + 1;
            foreach ((Vec3d ventDir, ulong h) in Candidates(ocean, dir, VentSpacing, seed + 303, ocean.Spec.VentChance, rings))
            {
                if ((ventDir - dir).Length * ocean.BodyRadius <= reach)
                {
                    result.Add(new OceanVent(ventDir, 3 + 6 * Unit(Hash.Mix(h + 8)), 8 + 27 * Unit(Hash.Mix(h + 9)), Hash.Mix(h + 10)));
                }
            }

            return result;
        }

        /// <summary>The pillars whose axis passes within <paramref name="reach"/> metres of <paramref name="dir"/>.</summary>
        public static List<OceanPillar> PillarsNear(OceanShell ocean, Vec3d dir, double reach, ulong seed)
        {
            var result = new List<OceanPillar>();
            OceanSpec spec = ocean.Spec;
            if (spec.PillarChance <= 0)
            {
                return result;
            }

            dir = dir.Normalized;
            int rings = (int)Math.Ceiling(reach / PillarSpacing) + 1;
            foreach ((Vec3d pillarDir, ulong h) in Candidates(ocean, dir, PillarSpacing, seed + 202, spec.PillarChance, rings))
            {
                if ((pillarDir - dir).Length * ocean.BodyRadius <= reach)
                {
                    result.Add(new OceanPillar(pillarDir, spec.PillarRadius * (0.4 + 0.6 * Unit(Hash.Mix(h + 5))), Hash.Mix(h + 6)));
                }
            }

            return result;
        }

        // One candidate point per cell of a 3D grid of `spacing` around the datum sphere, in the cells around `dir`: the
        // cells whose jittered point lies within half a cell of the sphere (one layer of them), kept with `chance`.
        private static IEnumerable<(Vec3d Dir, ulong Hash)> Candidates(OceanShell ocean, Vec3d dir, double spacing, ulong seed, double chance, int rings = 1)
        {
            double radius = ocean.BodyRadius;
            Vec3d p = dir * radius;
            long cx = (long)Math.Floor(p.X / spacing), cy = (long)Math.Floor(p.Y / spacing), cz = (long)Math.Floor(p.Z / spacing);
            for (long x = cx - rings; x <= cx + rings; x++)
            {
                for (long y = cy - rings; y <= cy + rings; y++)
                {
                    for (long z = cz - rings; z <= cz + rings; z++)
                    {
                        ulong h = Hash.Combine(seed, Hash.Combine((ulong)x, Hash.Combine((ulong)y, (ulong)z)));
                        var point = new Vec3d((x + Unit(Hash.Mix(h))) * spacing, (y + Unit(Hash.Mix(h + 1))) * spacing, (z + Unit(Hash.Mix(h + 2))) * spacing);
                        if (Math.Abs(point.Length - radius) > spacing * 0.5 || Unit(Hash.Mix(h + 7)) >= chance)
                        {
                            continue;
                        }

                        yield return (point.Normalized, h);
                    }
                }
            }
        }

        private static double Unit(ulong h) => (h >> 11) / (double)(1UL << 53);
    }
}
