using System;
using System.Collections.Generic;

namespace Subterra.Core.Config
{
    /// <summary>
    /// Merges several <c>SUBTERRA_BODY</c> nodes for the same body (the mod's own cfg plus add-ons):
    /// values and GEOTHERM/STRATA come from the first node that sets them; TUBE_SITE, TUBE_FIELD,
    /// CAVE_SCATTER and ENRICHMENT lists are appended in load order (ruling R4).
    /// </summary>
    public static class BodyMerge
    {
        private static readonly HashSet<string> Appended = new HashSet<string> { "TUBE_SITE", "TUBE_FIELD", "CAVE_SCATTER", "ENRICHMENT", "RELIEF" };

        /// <summary>One merged node per body name (first-seen order); nodes without <c>body</c> are returned unmerged.</summary>
        public static List<CfgNode> MergeBodies(IEnumerable<CfgNode> nodes)
        {
            var result = new List<CfgNode>();
            var byBody = new Dictionary<string, CfgNode>(StringComparer.Ordinal);
            foreach (CfgNode node in nodes)
            {
                string body = node.GetValue("body")?.Trim();
                if (string.IsNullOrEmpty(body))
                {
                    result.Add(node);
                    continue;
                }

                if (!byBody.TryGetValue(body, out CfgNode merged))
                {
                    merged = new CfgNode(node.Name);
                    byBody[body] = merged;
                    result.Add(merged);
                }

                foreach (KeyValuePair<string, string> value in node.Values)
                {
                    if (merged.GetValue(value.Key) == null)
                    {
                        merged.AddValue(value.Key, value.Value);
                    }
                }

                foreach (CfgNode child in node.Nodes)
                {
                    if (Appended.Contains(child.Name) || merged.GetNode(child.Name) == null)
                    {
                        merged.AddNode(child);
                    }
                }
            }

            return result;
        }
    }
}
