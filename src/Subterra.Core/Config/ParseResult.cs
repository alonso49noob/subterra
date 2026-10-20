using System.Collections.Generic;

namespace Subterra.Core.Config
{
    /// <summary>A parsed value plus the problems found on the way (each a complete log line).</summary>
    public sealed class ParseResult<T>
    {
        public ParseResult(T value, List<string> warnings)
        {
            Value = value;
            Warnings = warnings ?? new List<string>();
        }

        public T Value { get; }

        public List<string> Warnings { get; }
    }
}
