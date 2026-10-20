using Xunit;

namespace Subterra.Core.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void CoreAssemblyHasNoUnityReference()
        {
            // Vec3 is only used to locate the Subterra.Core assembly.
            var referenced = typeof(Subterra.Core.Numerics.Vec3).Assembly.GetReferencedAssemblies();

            // The core must reference neither Unity nor KSP's own assemblies.
            Assert.DoesNotContain(referenced, a => a.Name.StartsWith("UnityEngine") || a.Name.StartsWith("Assembly-CSharp"));
        }
    }
}
