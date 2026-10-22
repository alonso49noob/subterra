using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// M0 spike (radar Task 2): can SCANsat draw a resource that has an empty GLOBAL_RESOURCE and an abundance
    /// patched in at runtime? Throwaway code. It never links SCANsat: everything goes through reflection.
    /// The abundance patch (a Harmony postfix on ResourceMap.GetAbundance) is applied only while the ScansatSpike
    /// step runs and removed at its end, so it is never active in normal play.
    /// </summary>
    [StepProvider]
    internal static class ScansatSpike
    {
        private const string Cavity = "SubterraCavity";
        private const string CavityB = "SubterraCavityB";
        private const string ModuleType = "SCANsat.SCAN_PartModules.SCANsat";
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Stat = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static CelestialBody patchBody;

        internal static void Register()
        {
            StepRegistry.RegisterSpike("ScansatDump", Dump);
            StepRegistry.RegisterSpike("ScansatScan", Scan);
            StepRegistry.RegisterSpike("ScansatCoverage", Coverage);
            StepRegistry.RegisterSpike("ScansatSpike", Run);
        }

        private static Assembly FindAssembly(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name)
                ?? throw new InvalidOperationException(name + " is not loaded (SCANsat needs the full profile)");
        }

        // spike name = ScansatDump assembly = SCANsat types = A,B file = scansat-api.txt — writes the types, fields and
        // methods of an assembly (all of them, or only the named types) to a file in the run folder.
        private static IEnumerator Dump(ConfigNode node, TestContext ctx)
        {
            Assembly assembly = FindAssembly(node.Arg("assembly", "SCANsat"));
            string[] only = node.Arg("types", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            sb.AppendLine(assembly.GetName().Name + " " + assembly.GetName().Version);
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            foreach (Type type in types.OrderBy(t => t.FullName))
            {
                if (only.Length > 0 && !only.Contains(type.Name))
                {
                    continue;
                }

                sb.AppendLine().Append(type.IsEnum ? "enum " : type.IsInterface ? "interface " : "class ").Append(type.FullName);
                if (type.BaseType != null && type.BaseType != typeof(object))
                {
                    sb.Append(" : ").Append(type.BaseType.Name);
                }

                sb.AppendLine();
                if (type.IsEnum)
                {
                    foreach (string name in Enum.GetNames(type))
                    {
                        sb.AppendLine(Invariant($"    {name} = {System.Convert.ToInt64(Enum.Parse(type, name))}"));
                    }

                    continue;
                }

                foreach (FieldInfo f in type.GetFields(All))
                {
                    bool kspField = f.GetCustomAttributes(false).Any(a => a.GetType().Name == "KSPField");
                    sb.AppendLine($"    field {(f.IsStatic ? "static " : "")}{f.FieldType.Name} {f.Name}{(kspField ? " [KSPField]" : "")}");
                }

                foreach (PropertyInfo p in type.GetProperties(All))
                {
                    sb.AppendLine($"    prop {p.PropertyType.Name} {p.Name}");
                }

                foreach (MethodInfo m in type.GetMethods(All))
                {
                    if (m.IsSpecialName)
                    {
                        continue;
                    }

                    string parameters = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
                    sb.AppendLine($"    method {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({parameters})");
                }
            }

            string file = node.Arg("file", "scansat-api.txt");
            File.WriteAllText(Path.Combine(ctx.ResultsDir, file), sb.ToString());
            ctx.Pass($"{assembly.GetName().Name} {assembly.GetName().Version}: {types.Length} types -> {file}");
            yield break;
        }

        // spike name = ScansatScan mode = SpikeLo|SpikeHi|none — stops every SCANsat module on the active vessel and
        // starts the one whose scanName is mode (none starts nothing). Reflection only: SCANsat is never linked.
        private static IEnumerator Scan(ConfigNode node, TestContext ctx)
        {
            string mode = node.RequireArg("mode");
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            var report = new StringBuilder();
            int found = 0;
            foreach (Part part in vessel.parts)
            {
                foreach (PartModule module in part.Modules)
                {
                    Type type = module.GetType();
                    if (type.FullName != ModuleType && type.FullName != "SCANsat.SCAN_PartModules.ModuleSCANresourceScanner")
                    {
                        continue;
                    }

                    found++;
                    string name = (string)type.GetField("scanName").GetValue(module);
                    type.GetMethod("stopScan", Inst).Invoke(module, null);
                    if (name == mode)
                    {
                        type.GetMethod("startScan", Inst).Invoke(module, null);
                    }

                    yield return null;
                    report.Append($"[{name}: sensorType {type.GetField("sensorType").GetValue(module)}, fov {type.GetField("fov").GetValue(module)}, alt {type.GetField("min_alt").GetValue(module)}/{type.GetField("best_alt").GetValue(module)}/{type.GetField("max_alt").GetValue(module)}, scanning {type.GetProperty("scanningNow", Inst).GetValue(module, null)}, status '{type.GetField("scanStatus").GetValue(module)}'] ");
                }
            }

            if (found == 0)
            {
                ctx.Fail("no SCANsat module on the active vessel: " + vessel.vesselName);
                yield break;
            }

            try
            {
                Assembly scansat = FindAssembly("SCANsat");
                Type controllerType = scansat.GetType("SCANsat.SCANcontroller");
                object controller = controllerType.GetProperty("controller", Stat).GetValue(null, null);
                object mask = controllerType.GetMethod("activeSensorsOnVessel", new[] { typeof(Guid), typeof(bool) }).Invoke(controller, new object[] { vessel.id, true });
                report.Append("| activeSensorsOnVessel = " + mask + " (" + (int)mask + ") sensors registered: ");
                foreach (object known in (IEnumerable)controllerType.GetProperty("Known_Vessels").GetValue(controller, null))
                {
                    Type kt = known.GetType();
                    if ((Guid)kt.GetField("id").GetValue(known) != vessel.id)
                    {
                        continue;
                    }

                    foreach (object sensor in (IEnumerable)kt.GetField("sensors").GetValue(known))
                    {
                        Type st = sensor.GetType();
                        report.Append($"[{st.GetField("sensor").GetValue(sensor)} fov {st.GetField("fov").GetValue(sensor)} inRange {st.GetField("inRange").GetValue(sensor)}] ");
                    }
                }
            }
            catch (Exception e)
            {
                report.Append("| sensor listing failed: " + e.GetType().Name + " " + e.Message);
            }

            Log.Info("ScansatScan " + mode + ": " + report);
            ctx.Pass($"{found} SCANsat modules, '{mode}' on: {report}");
        }

        // The abundance of SubterraCavity: 0.8 on the Mun between latitudes 0 and 10, nothing elsewhere; every other
        // resource keeps its value. Applied only while the ScansatSpike step runs.
        private static void CavityPostfix(AbundanceRequest request, ref float __result)
        {
            if (request.ResourceName == Cavity || request.ResourceName == CavityB)
            {
                __result = patchBody != null && request.BodyId == patchBody.flightGlobalsIndex && request.Latitude >= 0 && request.Latitude <= 10 ? 0.8f : 0f;
            }
        }

        private static string StockReport(CelestialBody mun)
        {
            var sb = new StringBuilder();
            sb.Append("stock FetchAllResourceNames(Planetary): ").Append(string.Join(",", ResourceMap.Instance.FetchAllResourceNames(HarvestTypes.Planetary).ToArray()));
            sb.Append(" | GetResourceItemList(Planetary, Mun): ");
            foreach (PlanetaryResource r in ResourceMap.Instance.GetResourceItemList(HarvestTypes.Planetary, mun))
            {
                sb.Append(Invariant($"{r.resourceName}={r.fraction:F3} "));
            }

            sb.Append(" | resource definition exists: ").Append(PartResourceLibrary.Instance.GetDefinition(Cavity) != null);
            var request = new AbundanceRequest
            {
                ResourceName = Cavity,
                ResourceType = HarvestTypes.Planetary,
                BodyId = mun.flightGlobalsIndex,
                Latitude = 5,
                Longitude = 0,
                Altitude = 0,
                CheckForLock = false,
                ExcludeVariance = true,
            };
            float inside = ResourceMap.Instance.GetAbundance(request);
            request.Latitude = 30;
            sb.Append(Invariant($" | GetAbundance Mun lat 5: {inside}, lat 30: {ResourceMap.Instance.GetAbundance(request)}"));
            return sb.ToString();
        }

        private static string ListNames(object value)
        {
            if (value == null)
            {
                return "null";
            }

            // DictionaryValueList is not necessarily an IEnumerable: fall back to its Values or Keys.
            object source = value;
            if (!(source is IEnumerable))
            {
                source = value.GetType().GetProperty("Values")?.GetValue(value, null) ?? value.GetType().GetProperty("Keys")?.GetValue(value, null);
            }

            if (!(source is IEnumerable items))
            {
                return "(a " + value.GetType().Name + ", not enumerable)";
            }

            var names = new List<string>();
            foreach (object o in items)
            {
                names.Add(o.GetType().GetProperty("Name", Inst)?.GetValue(o, null)?.ToString()
                    ?? o.GetType().GetField("name", Inst)?.GetValue(o)?.ToString() ?? o.ToString());
            }

            return string.Join(",", names.ToArray());
        }

        // Runs one report line; a reflection mistake is reported instead of ending the spike.
        private static void Line(StringBuilder report, string label, Func<string> body)
        {
            try
            {
                report.AppendLine(body());
            }
            catch (Exception e)
            {
                report.AppendLine(label + " FAILED: " + e.GetType().Name + " " + e.Message + " @ " + e.StackTrace.Split((char)10)[0]);
            }
        }

        // The parts of the report that need no waiting: stock lists, SCANsat's lists and settings, what SCANUtil
        // returns and the coverage the scan passes left (128 = ResourceLoRes, 256 = ResourceHiRes).
        private static void Sections(Assembly scansat, CelestialBody mun, StringBuilder report)
        {
            Line(report, "stock", () => StockReport(mun));
            Type controllerType = scansat.GetType("SCANsat.SCANcontroller");
            foreach (string field in new[] { "loadedResources", "masterResourceNodes" })
            {
                string name = field;
                Line(report, name, () => "SCANcontroller." + name + ": " + ListNames(controllerType.GetField(name, Stat).GetValue(null)));
            }

            Type settingsType = scansat.GetType("SCANsat.SCAN_Settings_Config");
            Line(report, "settings", () =>
            {
                object settings = settingsType.GetProperty("Instance", Stat).GetValue(null, null);
                return $"SCANsat settings: HideZeroResources {settingsType.GetField("HideZeroResources").GetValue(settings)}, InstantScan {settingsType.GetField("InstantScan").GetValue(settings)}, RequireNarrowBand {settingsType.GetField("RequireNarrowBand").GetValue(settings)}, BiomeLock {settingsType.GetField("BiomeLock").GetValue(settings)}";
            });
            Line(report, "resource node", () =>
            {
                object resourceNode = controllerType.GetMethod("getResourceNode", Stat).Invoke(null, new object[] { Cavity, false });
                if (resourceNode == null)
                {
                    return "getResourceNode(SubterraCavity) = null";
                }

                Type nodeType = resourceNode.GetType();
                object bodyConfig = nodeType.GetMethod("getBodyConfig", new[] { typeof(string), typeof(bool) }).Invoke(resourceNode, new object[] { "Mun", false });
                string text = $"getResourceNode(SubterraCavity): DisplayName '{nodeType.GetProperty("DisplayName").GetValue(resourceNode, null)}', DefaultZero {nodeType.GetProperty("DefaultZero").GetValue(resourceNode, null)}, min/max {nodeType.GetProperty("DefaultMinValue").GetValue(resourceNode, null)}/{nodeType.GetProperty("DefaultMaxValue").GetValue(resourceNode, null)}, bodyCount {nodeType.GetProperty("getBodyCount").GetValue(resourceNode, null)}";
                if (bodyConfig != null)
                {
                    Type bt = bodyConfig.GetType();
                    text += $"{Environment.NewLine}  Mun config: DefaultZero {bt.GetProperty("DefaultZero").GetValue(bodyConfig, null)}, MinValue {bt.GetProperty("MinValue").GetValue(bodyConfig, null)}, MaxValue {bt.GetProperty("MaxValue").GetValue(bodyConfig, null)}, Fraction {bt.GetProperty("Fraction").GetValue(bodyConfig, null)}";
                }

                return text;
            });

            Type util = scansat.GetType("SCANsat.SCANUtil");
            foreach (string resource in new[] { Cavity, "Ore" })
            {
                string name = resource;
                foreach (double lat in new[] { 5.0, 30.0, -5.0 })
                {
                    double latitude = lat;
                    Line(report, "overlay", () =>
                    {
                        object value = util.GetMethod("ResourceOverlay", Stat).Invoke(null, new object[] { latitude, 0.0, name, mun, false });
                        return Invariant($"SCANUtil.ResourceOverlay(lat {latitude}, lon 0, {name}, Mun) = {value}");
                    });
                }
            }

            Line(report, "coverage", () =>
            {
                MethodInfo isCovered = util.GetMethod("isCovered", Stat, null, new[] { typeof(double), typeof(double), typeof(CelestialBody), typeof(int) }, null);
                var coverage = new StringBuilder("coverage at lat 5 by longitude, -180 to 175 step 5 (L = lo 128, H = hi 256, B = both, . = none): ");
                for (int lon = -180; lon < 180; lon += 5)
                {
                    bool lo = (bool)isCovered.Invoke(null, new object[] { (double)lon, 5.0, mun, 128 });
                    bool hi = (bool)isCovered.Invoke(null, new object[] { (double)lon, 5.0, mun, 256 });
                    coverage.Append(lo && hi ? 'B' : lo ? 'L' : hi ? 'H' : '.');
                }

                return coverage.ToString();
            });
        }

        private static IEnumerable<object> Shot(TestContext ctx, string name)
        {
            var shot = new ConfigNode("STEP");
            shot.AddValue("name", name);
            IEnumerator screenshot = StepRegistry.GetStep("screenshot")(shot, ctx);
            while (screenshot.MoveNext())
            {
                yield return screenshot.Current;
            }
        }

        // spike name = ScansatCoverage label = pass1 — logs the SCANsat coverage of the Mun at latitude 5 by longitude
        // (L = ResourceLoRes only, H = ResourceHiRes only, B = both, . = none).
        private static IEnumerator Coverage(ConfigNode node, TestContext ctx)
        {
            Assembly scansat = FindAssembly("SCANsat");
            CelestialBody mun = FlightGlobals.GetBodyByName("Mun");
            Type util = scansat.GetType("SCANsat.SCANUtil");
            MethodInfo isCovered = util.GetMethod("isCovered", Stat, null, new[] { typeof(double), typeof(double), typeof(CelestialBody), typeof(int) }, null);
            var sb = new StringBuilder();
            foreach (double lat in new[] { 5.0, 45.0 })
            {
                sb.Append(Invariant($"lat {lat}: "));
                for (int lon = -180; lon < 180; lon += 3)
                {
                    bool lo = (bool)isCovered.Invoke(null, new object[] { (double)lon, lat, mun, 128 });
                    bool hi = (bool)isCovered.Invoke(null, new object[] { (double)lon, lat, mun, 256 });
                    sb.Append(lo && hi ? 'B' : lo ? 'L' : hi ? 'H' : '.');
                }

                sb.Append("  ");
            }

            Log.Info("ScansatCoverage " + node.Arg("label", "") + ": " + sb);
            ctx.Pass(node.Arg("label", "") + " " + sb);
            yield break;
        }

        // spike name = ScansatSpike — with the abundance patch on: logs what SCANsat and stock list and return for
        // SubterraCavity, opens the SCANsat big map on the resource and takes screenshots; unpatches at the end.
        private static IEnumerator Run(ConfigNode node, TestContext ctx)
        {
            Assembly scansat = FindAssembly("SCANsat");
            CelestialBody mun = FlightGlobals.GetBodyByName("Mun");
            patchBody = mun;
            var harmony = new Harmony("subterra.devtools.cavityspike");
            var report = new StringBuilder();
            try
            {
                MethodInfo original = AccessTools.Method(typeof(ResourceMap), "GetAbundance", new[] { typeof(AbundanceRequest) });
                harmony.Patch(original, postfix: new HarmonyMethod(typeof(ScansatSpike).GetMethod(nameof(CavityPostfix), BindingFlags.NonPublic | BindingFlags.Static)));

                Sections(scansat, mun, report);

                // 4. The big map.
                Type util = scansat.GetType("SCANsat.SCANUtil");
                Type launcherType = scansat.GetType("SCANsat.SCAN_Toolbar.SCANappLauncher");
                object launcher = launcherType.GetProperty("Instance", Stat).GetValue(null, null);
                report.AppendLine("SCANappLauncher.Instance: " + (launcher == null ? "null" : "found"));
                Type bigMapType = scansat.GetType("SCANsat.SCAN_Unity.SCAN_UI_BigMap");
                Line(report, "position", () =>
                {
                    Type st = scansat.GetType("SCANsat.SCAN_Settings_Config");
                    object config = st.GetProperty("Instance", Stat).GetValue(null, null);
                    st.GetField("BigMapPosition").SetValue(config, new Vector2(10, -10));
                    return "BigMapPosition set to (10, -10) before opening";
                });
                if (launcher != null)
                {
                    launcherType.GetProperty("BigMap").SetValue(launcher, true, null);
                }

                yield return new WaitForSeconds(3f);
                object bigMap = bigMapType.GetProperty("Instance", Stat).GetValue(null, null);
                report.AppendLine("SCAN_UI_BigMap.Instance: " + (bigMap == null ? "null" : "found, visible " + bigMapType.GetProperty("IsVisible").GetValue(bigMap, null)));
                if (bigMap == null)
                {
                    ctx.Fail("the SCANsat big map did not open");
                }
                else
                {
                    Type settingsType = scansat.GetType("SCANsat.SCAN_Settings_Config");
                    string munName = (string)util.GetMethod("displayNameFromBodyName", Stat).Invoke(null, new object[] { "Mun" });
                    Func<string> listState = () => "big map body '" + bigMapType.GetProperty("CurrentCelestialBody").GetValue(bigMap, null) + "', Position " + bigMapType.GetProperty("Position").GetValue(bigMap, null) + ", resource list: [" + ListNames(bigMapType.GetProperty("Resources").GetValue(bigMap, null)) + "]";
                    Line(report, "body", () =>
                    {
                        bigMapType.GetProperty("CurrentCelestialBody").SetValue(bigMap, munName, null);
                        return "set body to Mun ('" + munName + "')";
                    });
                    yield return new WaitForSeconds(3f);
                    Line(report, "list", () => "HideZeroResources=True (the default): " + listState());
                    foreach (string resource in new[] { CavityB, Cavity })
                    {
                        string display = (string)util.GetMethod("displayNameFromResource", Stat).Invoke(null, new object[] { resource });
                        Line(report, "resource", () =>
                        {
                            bigMapType.GetProperty("CurrentResource").SetValue(bigMap, display, null);
                            bigMapType.GetProperty("ResourceToggle").SetValue(bigMap, true, null);
                            return $"set resource '{display}': CurrentResource '{bigMapType.GetProperty("CurrentResource").GetValue(bigMap, null)}', ShowResource {bigMapType.GetProperty("ShowResource").GetValue(bigMap, null)}";
                        });
                        yield return new WaitForSeconds(8f);
                        foreach (var _ in Shot(ctx, "bigmap-" + resource))
                        {
                            yield return _;
                        }
                    }
                }

                // Stock again with the SCANsat window up: the lists must not have changed.
                Line(report, "stock", () => "after the screenshots: " + StockReport(mun));
                Line(report, "display", () =>
                {
                    var sb = new StringBuilder("SCANresourceDisplay modules on the vessel: ");
                    foreach (Part part in FlightGlobals.ActiveVessel.parts)
                    {
                        foreach (PartModule module in part.Modules)
                        {
                            if (module.GetType().Name == "SCANresourceDisplay")
                            {
                                Type mt = module.GetType();
                                sb.Append($"[ResourceName '{mt.GetField("ResourceName").GetValue(module)}', abundance '{mt.GetField("abundance").GetValue(module)}', zeroResource {mt.GetField("zeroResource", Inst).GetValue(module)}, scanResource {(mt.GetField("scanResource", Inst).GetValue(module) == null ? "null" : "set")}] ");
                            }
                        }
                    }

                    return sb.ToString();
                });
                Line(report, "unlock", () =>
                {
                    ResourceMap.Instance.UnlockPlanet(mun.flightGlobalsIndex);
                    return "after ResourceMap.UnlockPlanet(Mun): " + StockReport(mun);
                });
                if (launcher != null)
                {
                    launcherType.GetProperty("BigMap").SetValue(launcher, false, null);
                }
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                patchBody = null;
            }

            Log.Info("ScansatSpike report:\n" + report);
            File.WriteAllText(Path.Combine(ctx.ResultsDir, "scansat-spike.txt"), report.ToString());
            ctx.Pass("report -> scansat-spike.txt, patch removed");
        }
    }
}
