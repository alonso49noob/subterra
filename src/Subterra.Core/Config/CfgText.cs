using System;
using System.Collections.Generic;

namespace Subterra.Core.Config
{
    /// <summary>
    /// Parses KSP config syntax: <c>key = value</c> (one per line), <c>NAME { … }</c> with the brace
    /// on the same line or the next, <c>//</c> comments. Like KSP, text after a brace starts a new line.
    /// </summary>
    public static class CfgText
    {
        public static List<CfgNode> Parse(string text)
        {
            var roots = new List<CfgNode>();
            var stack = new Stack<CfgNode>();
            string pendingName = null;

            foreach (string line in Tokenize(text))
            {
                if (line == "{")
                {
                    var node = new CfgNode(pendingName ?? "");
                    pendingName = null;
                    if (stack.Count == 0)
                    {
                        roots.Add(node);
                    }
                    else
                    {
                        stack.Peek().AddNode(node);
                    }

                    stack.Push(node);
                    continue;
                }

                pendingName = null;
                if (line == "}")
                {
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals >= 0)
                {
                    if (stack.Count > 0)
                    {
                        stack.Peek().AddValue(line.Substring(0, equals).Trim(), line.Substring(equals + 1).Trim());
                    }
                }
                else
                {
                    pendingName = line;
                }
            }

            return roots;
        }

        private static IEnumerable<string> Tokenize(string text)
        {
            foreach (string rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = rawLine;
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0)
                {
                    line = line.Substring(0, comment);
                }

                int start = 0;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c != '{' && c != '}')
                    {
                        continue;
                    }

                    string before = line.Substring(start, i - start).Trim();
                    if (before.Length > 0)
                    {
                        yield return before;
                    }

                    yield return c.ToString();
                    start = i + 1;
                }

                string rest = line.Substring(start).Trim();
                if (rest.Length > 0)
                {
                    yield return rest;
                }
            }
        }
    }
}
