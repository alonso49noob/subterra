using System.Collections.Generic;

namespace Subterra
{
    /// <summary>The bodies Subterra is active on, by body name.</summary>
    public sealed class BodyRegistry
    {
        private readonly Dictionary<string, BodyRuntime> bodies = new Dictionary<string, BodyRuntime>();

        public static BodyRegistry Instance { get; } = new BodyRegistry();

        public IReadOnlyDictionary<string, BodyRuntime> Bodies => bodies;

        public bool TryGet(CelestialBody body, out BodyRuntime runtime)
        {
            if (body == null)
            {
                runtime = null;
                return false;
            }

            return bodies.TryGetValue(body.bodyName, out runtime);
        }

        internal void Add(BodyRuntime runtime) => bodies[runtime.Body.bodyName] = runtime;

        internal void Clear() => bodies.Clear();
    }
}
