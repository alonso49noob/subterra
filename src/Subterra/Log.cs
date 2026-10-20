using UnityEngine;

namespace Subterra
{
    /// <summary>KSP.log lines with the <c>[Subterra] </c> prefix.</summary>
    public static class Log
    {
        public const string Prefix = "[Subterra] ";

        public static void Info(string message) => Debug.Log(Prefix + message);

        public static void Warn(string message) => Debug.LogWarning(Prefix + message);

        public static void Error(string message) => Debug.LogError(Prefix + message);
    }
}
