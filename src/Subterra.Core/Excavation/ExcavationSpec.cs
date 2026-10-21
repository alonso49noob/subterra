using System;
using System.Collections.Generic;
using System.Globalization;
using Subterra.Core.Config;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;

namespace Subterra.Core.Excavation
{
    /// <summary>
    /// A tunnel dug by the player: straight pieces of one radius, in a local frame (body axes moved to
    /// <see cref="Anchor"/>, like a network's). Points are the tunnel's floor centre. Saved per game.
    /// </summary>
    public sealed class ExcavationSpec
    {
        /// <summary>A piece shorter than this keeps growing instead of starting a new one, if it stays straight.</summary>
        public const float MaxPieceLength = 20f;

        /// <summary>Largest turn (degrees) that still extends the last piece.</summary>
        public const float StraightAngle = 5f;

        public ExcavationSpec(string id, string body, Vec3d anchor, float radius)
        {
            Id = id;
            Body = body;
            Anchor = anchor;
            Radius = radius;
        }

        /// <summary><c>XP-&lt;body&gt;-&lt;n&gt;</c>.</summary>
        public string Id { get; }

        public string Body { get; }

        public Vec3d Anchor { get; }

        public float Radius { get; }

        /// <summary>Floor-centre points; piece i goes from Points[i] to Points[i + 1].</summary>
        public List<Vec3> Points { get; } = new List<Vec3>();

        /// <summary>The network this tunnel was joined into (null while it stands alone).</summary>
        public string JoinedTo { get; set; }

        public int PieceCount => Math.Max(0, Points.Count - 1);

        public Vec3 ToLocal(Vec3d bodyPos) => (bodyPos - Anchor).ToVec3();

        public Vec3d ToBody(Vec3 local) => Anchor + Vec3d.FromVec3(local);

        /// <summary>
        /// Digs to <paramref name="to"/>: the last piece grows if the tunnel keeps straight and the piece is still
        /// short, otherwise a new piece starts. Returns the new or grown piece's index.
        /// </summary>
        public int Append(Vec3 to)
        {
            if (Points.Count == 0)
            {
                Points.Add(to);
                return -1;
            }

            if (Points.Count >= 2)
            {
                Vec3 a = Points[Points.Count - 2], b = Points[Points.Count - 1];
                Vec3 last = b - a, next = to - b;
                if (last.Length > 0 && next.Length > 0 && (to - a).Length <= MaxPieceLength
                    && Vec3.Dot(last.Normalized, next.Normalized) >= Math.Cos(StraightAngle * Math.PI / 180))
                {
                    Points[Points.Count - 1] = to;
                    return Points.Count - 2;
                }
            }

            Points.Add(to);
            return Points.Count - 2;
        }

        /// <summary>The piece as a tube primitive (round section of <see cref="Radius"/>, smooth walls), in <paramref name="frame"/>'s coordinates.</summary>
        public TubeSegment Piece(int index, Vec3d frame)
        {
            Vec3 offset = (Anchor - frame).ToVec3();
            Vec3 a = Points[index] + offset, b = Points[index + 1] + offset;
            Vec3 upA = ToBody(Points[index]).Normalized.ToVec3(), upB = ToBody(Points[index + 1]).Normalized.ToVec3();
            float d = 2 * Radius;
            return new TubeSegment(a, b, upA, upB, d, d, d, d, 0f, ZoneKind.Excavation)
            {
                Roughness = 0.15f,
                NoiseScale = 3f,
                Source = index,
            };
        }

        /// <summary>Every piece as a primitive in <paramref name="frame"/>'s coordinates.</summary>
        public List<Primitive> Pieces(Vec3d frame)
        {
            var list = new List<Primitive>(PieceCount);
            for (int i = 0; i < PieceCount; i++)
            {
                list.Add(Piece(i, frame));
            }

            return list;
        }

        /// <summary>Box (local) around piece <paramref name="index"/>, walls and noise included.</summary>
        public Aabb PieceBounds(int index) => Piece(index, Anchor).Bounds;

        public CfgNode ToCfg()
        {
            var node = new CfgNode("EXCAVATION");
            node.AddValue("id", Id);
            node.AddValue("body", Body);
            node.AddValue("anchor", Format(Anchor.X, Anchor.Y, Anchor.Z));
            node.AddValue("radius", Radius.ToString("R", CultureInfo.InvariantCulture));
            if (JoinedTo != null)
            {
                node.AddValue("joinedTo", JoinedTo);
            }

            foreach (Vec3 p in Points)
            {
                node.AddValue("point", Format(p.X, p.Y, p.Z));
            }

            return node;
        }

        /// <summary>Reads a node written by <see cref="ToCfg"/>; null (and a warning) if it is broken.</summary>
        public static ExcavationSpec FromCfg(CfgNode node, List<string> warnings)
        {
            string id = node.GetValue("id"), body = node.GetValue("body");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(body) || !TryParse(node.GetValue("anchor"), out double[] anchor)
                || !float.TryParse(node.GetValue("radius"), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) || radius <= 0)
            {
                warnings?.Add($"EXCAVATION '{id}': missing id, body, anchor or radius; skipped");
                return null;
            }

            var spec = new ExcavationSpec(id, body, new Vec3d(anchor[0], anchor[1], anchor[2]), radius) { JoinedTo = node.GetValue("joinedTo") };
            foreach (string value in node.GetValues("point"))
            {
                if (TryParse(value, out double[] p))
                {
                    spec.Points.Add(new Vec3((float)p[0], (float)p[1], (float)p[2]));
                }
                else
                {
                    warnings?.Add($"EXCAVATION '{id}': bad point '{value}' skipped");
                }
            }

            return spec;
        }

        private static string Format(double x, double y, double z) =>
            string.Join(",", x.ToString("R", CultureInfo.InvariantCulture), y.ToString("R", CultureInfo.InvariantCulture), z.ToString("R", CultureInfo.InvariantCulture));

        private static bool TryParse(string value, out double[] xyz)
        {
            xyz = null;
            if (value == null)
            {
                return false;
            }

            string[] parts = value.Split(',');
            if (parts.Length != 3)
            {
                return false;
            }

            var result = new double[3];
            for (int i = 0; i < 3; i++)
            {
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                {
                    return false;
                }
            }

            xyz = result;
            return true;
        }
    }
}
