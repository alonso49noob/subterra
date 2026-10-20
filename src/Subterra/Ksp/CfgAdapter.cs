using Subterra.Core.Config;

namespace Subterra
{
    /// <summary>KSP <see cref="ConfigNode"/> → core <see cref="CfgNode"/> (values and children in order).</summary>
    public static class CfgAdapter
    {
        public static CfgNode ToCore(ConfigNode node)
        {
            var result = new CfgNode(node.name);
            foreach (ConfigNode.Value value in node.values)
            {
                result.AddValue(value.name, value.value);
            }

            foreach (ConfigNode child in node.nodes)
            {
                result.AddNode(ToCore(child));
            }

            return result;
        }
    }
}
