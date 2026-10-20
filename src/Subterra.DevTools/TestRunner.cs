using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Subterra.DevTools
{
    /// <summary>
    /// Runs the scenario named in GameData/Subterra.DevTools/PluginData/run.txt: each STEP of the
    /// matching SUBTERRA_TEST node in order, writing "PASS|FAIL &lt;n&gt; &lt;do&gt; &lt;msg&gt;" per step and
    /// "DONE" at the end to PluginData/results/&lt;scenario&gt;.txt, then quits KSP. Does nothing
    /// without run.txt.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public sealed class TestRunner : MonoBehaviour
    {
        private const string LogPrefix = "[Subterra.DevTools] ";

        // KSP creates this addon on every main menu load; only the first one may run.
        private static bool started;

        private TestContext context;
        private string resultsFile;
        private Exception stepError;

        internal static string PluginDataDir =>
            Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/Subterra.DevTools/PluginData"));

        internal static string RunFile => Path.Combine(PluginDataDir, "run.txt");

        private void Start()
        {
            if (started || !File.Exists(RunFile))
            {
                Destroy(gameObject);
                return;
            }

            started = true;
            DontDestroyOnLoad(gameObject);
            string scenario = File.ReadAllText(RunFile).Trim();

            // A later manual launch of the test copy must not run the scenario again.
            File.Delete(RunFile);
            StartCoroutine(Run(scenario));
        }

        private void Update()
        {
            if (context == null)
            {
                return;
            }

            try
            {
                context.MaintainHold();
            }
            catch (Exception e)
            {
                // MaintainHold drops the hold when it throws, so this is logged once.
                Debug.LogException(e);
            }
        }

        private void OnDestroy()
        {
            context?.Dispose();
        }

        private IEnumerator Run(string scenario)
        {
            string resultsDir = Path.Combine(PluginDataDir, "results");
            Directory.CreateDirectory(resultsDir);
            resultsFile = Path.Combine(resultsDir, scenario + ".txt");
            File.Delete(resultsFile);
            context = new TestContext(scenario, resultsDir, this);
            Debug.Log(LogPrefix + "running scenario '" + scenario + "'");

            // MainMenu.Start resets HighLogic.CurrentGame: let it run before the first step.
            yield return new WaitForSecondsRealtime(1f);

            ConfigNode[] steps = null;
            try
            {
                StepRegistry.EnsureInitialized();
                steps = FindScenario(scenario).GetNodes("STEP");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                WriteLine("FAIL 0 load " + Describe(e));
            }

            if (steps != null)
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    yield return StartCoroutine(RunStep(i + 1, steps[i]));
                    if (stepError != null)
                    {
                        // After an exception KSP is in an unknown state: go straight to the quit step.
                        int quit = Array.FindIndex(steps, i + 1, IsQuitStep);
                        if (quit >= 0)
                        {
                            yield return StartCoroutine(RunStep(quit + 1, steps[quit]));
                        }

                        break;
                    }

                    if (context.QuitRequested)
                    {
                        break;
                    }
                }
            }

            WriteLine("DONE");
            Application.Quit();
        }

        private IEnumerator RunStep(int number, ConfigNode step)
        {
            string doName = step.GetValue("do") ?? "";
            context.BeginStep();
            stepError = null;
            IEnumerator body = null;
            try
            {
                body = StepRegistry.GetStep(doName)(step, context);
            }
            catch (Exception e)
            {
                stepError = e;
            }

            if (body != null)
            {
                yield return StartCoroutine(Pump(body));
            }

            if (stepError != null)
            {
                Debug.LogException(stepError);
                context.Fail("exception " + Describe(stepError));
            }

            string verdict = context.StepFailed ? "FAIL" : "PASS";
            WriteLine($"{verdict} {number} {doName} {context.StepMessage}".TrimEnd());
        }

        // Drives a step's enumerator, running nested IEnumerators inline, and records an exception
        // in stepError instead of letting Unity log it and silently stop the coroutine.
        private IEnumerator Pump(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                bool moved;
                try
                {
                    moved = top.MoveNext();
                }
                catch (Exception e)
                {
                    stepError = e;
                    yield break;
                }

                if (!moved)
                {
                    stack.Pop();
                }
                else if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                }
                else
                {
                    yield return top.Current;
                }
            }
        }

        private static ConfigNode FindScenario(string name)
        {
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("SUBTERRA_TEST"))
            {
                if (string.Equals(node.GetValue("name"), name, StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }
            }

            throw new ArgumentException($"no SUBTERRA_TEST named '{name}' in GameDatabase");
        }

        private static bool IsQuitStep(ConfigNode step)
        {
            return string.Equals(step.GetValue("do"), "quit", StringComparison.OrdinalIgnoreCase);
        }

        private static string Describe(Exception e)
        {
            if (e is TargetInvocationException && e.InnerException != null)
            {
                e = e.InnerException;
            }

            return e.GetType().Name + ": " + e.Message;
        }

        private void WriteLine(string line)
        {
            line = line.Replace('\r', ' ').Replace('\n', ' ');
            Debug.Log(LogPrefix + line);

            // The test script polls this file; retry briefly if a read is in progress.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.AppendAllText(resultsFile, line + "\n");
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }
    }
}
