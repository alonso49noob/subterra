using System;
using System.Collections.Generic;

namespace Subterra.Core.Config
{
    /// <summary>A KSP-style config node without Unity: ordered values and child nodes.</summary>
    public sealed class CfgNode
    {
        public CfgNode(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public List<KeyValuePair<string, string>> Values { get; } = new List<KeyValuePair<string, string>>();

        public List<CfgNode> Nodes { get; } = new List<CfgNode>();

        public CfgNode AddValue(string key, string value)
        {
            Values.Add(new KeyValuePair<string, string>(key, value));
            return this;
        }

        public CfgNode AddNode(CfgNode node)
        {
            Nodes.Add(node);
            return node;
        }

        /// <summary>The first value of <paramref name="key"/> (case-sensitive, like KSP), or null.</summary>
        public string GetValue(string key)
        {
            foreach (KeyValuePair<string, string> pair in Values)
            {
                if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                {
                    return pair.Value;
                }
            }

            return null;
        }

        public IEnumerable<string> GetValues(string key)
        {
            foreach (KeyValuePair<string, string> pair in Values)
            {
                if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                {
                    yield return pair.Value;
                }
            }
        }

        public IEnumerable<CfgNode> GetNodes(string name)
        {
            foreach (CfgNode node in Nodes)
            {
                if (string.Equals(node.Name, name, StringComparison.Ordinal))
                {
                    yield return node;
                }
            }
        }

        public CfgNode GetNode(string name)
        {
            foreach (CfgNode node in GetNodes(name))
            {
                return node;
            }

            return null;
        }
    }
}
