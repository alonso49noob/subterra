using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>Signed distance field in a network's local frame: negative = void (inside a cave), positive = rock.</summary>
    public interface ISdf
    {
        float Distance(Vec3 p);
    }
}
