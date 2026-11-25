using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// <c>Physics.BakeMesh</c> on both games Subterra runs on (Lucas: KSP 1.12.5 and Retooled). KSP 1.12's Unity 2019 takes
    /// the mesh's instance id (int); Retooled's Unity 6 takes an <c>EntityId</c> from <c>Object.GetEntityId()</c>. Found by
    /// reflection once, so one DLL serves both.
    /// </summary>
    internal static class PhysicsBake
    {
        private static readonly MethodInfo BakeMethod;
        private static readonly MethodInfo EntityIdOf;

        static PhysicsBake()
        {
            MethodInfo[] bakes = typeof(Physics).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "BakeMesh" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(bool))
                .ToArray();
            EntityIdOf = typeof(UnityEngine.Object).GetMethod("GetEntityId", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            // Unity 6: the EntityId overload (the int one, if still there, is obsolete).
            BakeMethod = EntityIdOf != null ? bakes.FirstOrDefault(m => m.GetParameters()[0].ParameterType == EntityIdOf.ReturnType) : null;
            if (BakeMethod == null)
            {
                BakeMethod = bakes.FirstOrDefault(m => m.GetParameters()[0].ParameterType == typeof(int));
                EntityIdOf = null;
            }

            Log.Info($"Collider baking: Physics.BakeMesh({BakeMethod?.GetParameters()[0].ParameterType.Name ?? "not found"}, bool)");
        }

        /// <summary>The mesh's id for <see cref="Bake"/> (main thread).</summary>
        public static object IdOf(Mesh mesh) => EntityIdOf != null ? EntityIdOf.Invoke(mesh, null) : mesh.GetInstanceID();

        /// <summary>Bakes the mesh's collision data (any thread), as a non-convex collider.</summary>
        public static void Bake(object id)
        {
            if (BakeMethod == null)
            {
                return;
            }

            BakeMethod.Invoke(null, new[] { id, (object)false });
        }
    }
}
