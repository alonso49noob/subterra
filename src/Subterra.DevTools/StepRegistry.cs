using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Subterra.DevTools
{
    /// <summary>
    /// Marks a static class whose static parameterless <c>Register()</c> method adds steps,
    /// assertions or spikes. The runner calls every such method once before the first step,
    /// so new step files only need this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StepProviderAttribute : Attribute
    {
    }

    /// <summary>Name → handler tables for scenario steps (<c>do</c>), assertions (<c>what</c>) and spikes (<c>name</c>).</summary>
    public static class StepRegistry
    {
        private static readonly Dictionary<string, Func<ConfigNode, TestContext, IEnumerator>> Steps =
            new Dictionary<string, Func<ConfigNode, TestContext, IEnumerator>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Func<ConfigNode, TestContext, bool>> Assertions =
            new Dictionary<string, Func<ConfigNode, TestContext, bool>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Func<ConfigNode, TestContext, IEnumerator>> Spikes =
            new Dictionary<string, Func<ConfigNode, TestContext, IEnumerator>>(StringComparer.OrdinalIgnoreCase);

        private static bool initialized;

        /// <summary>Registers the handler run for <c>STEP { do = doName … }</c>. The STEP node is passed as is.</summary>
        public static void Register(string doName, Func<ConfigNode, TestContext, IEnumerator> handler)
        {
            Add(Steps, "step", doName, handler);
        }

        /// <summary>
        /// Registers a check used by <c>assert what = …</c> and <c>waitUntil what = …</c>. It must not
        /// call <see cref="TestContext.Pass"/>/<see cref="TestContext.Fail"/> (waitUntil polls it every
        /// frame); it may explain its result through <see cref="TestContext.Detail"/>.
        /// </summary>
        public static void RegisterAssertion(string what, Func<ConfigNode, TestContext, bool> check)
        {
            Add(Assertions, "assertion", what, check);
        }

        /// <summary>Registers an experiment run by <c>STEP { do = spike  name = … }</c>.</summary>
        public static void RegisterSpike(string name, Func<ConfigNode, TestContext, IEnumerator> handler)
        {
            Add(Spikes, "spike", name, handler);
        }

        public static Func<ConfigNode, TestContext, IEnumerator> GetStep(string doName)
        {
            return Get(Steps, "step", doName);
        }

        public static Func<ConfigNode, TestContext, bool> GetAssertion(string what)
        {
            return Get(Assertions, "assertion", what);
        }

        public static Func<ConfigNode, TestContext, IEnumerator> GetSpike(string name)
        {
            return Get(Spikes, "spike", name);
        }

        /// <summary>Runs every <see cref="StepProviderAttribute"/> class's <c>Register()</c> once.</summary>
        internal static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (Type type in typeof(StepRegistry).Assembly.GetTypes())
            {
                if (!type.IsDefined(typeof(StepProviderAttribute), false))
                {
                    continue;
                }

                MethodInfo register = type.GetMethod("Register", flags, null, Type.EmptyTypes, null);
                if (register == null)
                {
                    throw new InvalidOperationException($"{type.FullName} has [StepProvider] but no static Register() method");
                }

                register.Invoke(null, null);
            }
        }

        private static void Add<T>(Dictionary<string, T> table, string kind, string name, T handler)
            where T : class
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException($"{kind} name is empty");
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (table.ContainsKey(name))
            {
                throw new InvalidOperationException($"{kind} '{name}' is already registered");
            }

            table.Add(name, handler);
        }

        private static T Get<T>(Dictionary<string, T> table, string kind, string name)
        {
            if (name != null && table.TryGetValue(name, out T handler))
            {
                return handler;
            }

            throw new ArgumentException($"unknown {kind} '{name}'");
        }
    }
}
