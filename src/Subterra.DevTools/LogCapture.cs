using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Subterra.DevTools
{
    /// <summary>
    /// Keeps every Unity log message from the moment the DevTools assembly loads, so assertions can
    /// look at messages written while GameData loads (before the main menu and the runner exist).
    /// Only active during a scenario run (run.txt present).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class LogCapture : MonoBehaviour
    {
        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly object Gate = new object();
        private static bool capturing;

        public struct Entry
        {
            public LogType Type;
            public string Message;
            public string StackTrace;
        }

        /// <summary>The captured messages matching <paramref name="match"/>, oldest first.</summary>
        public static List<Entry> FindAll(Func<Entry, bool> match)
        {
            var found = new List<Entry>();
            lock (Gate)
            {
                foreach (Entry entry in Entries)
                {
                    if (match(entry))
                    {
                        found.Add(entry);
                    }
                }
            }

            return found;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            // Stack traces only for errors: loading GameData logs tens of thousands of plain lines.
            bool keepStack = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            var entry = new Entry { Type = type, Message = message ?? "", StackTrace = keepStack ? stackTrace ?? "" : "" };
            lock (Gate)
            {
                Entries.Add(entry);
            }
        }

        private void Awake()
        {
            if (!capturing && File.Exists(TestRunner.RunFile))
            {
                capturing = true;
                Application.logMessageReceivedThreaded += OnLog;
            }

            Destroy(gameObject);
        }
    }
}
