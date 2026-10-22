using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// Test-only steps and assertions for SCANsat (full profile). SCANsat is never linked: everything goes through
    /// reflection on its public and internal members, which is why a failed lookup is reported with the name that was
    /// not found (a new SCANsat release may rename them).
    /// </summary>
    [StepProvider]
    internal static class ScansatSteps
    {
        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Stat = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void Register()
        {
            StepRegistry.Register("scansatFill", ScansatFill);
            StepRegistry.Register("scansatBigMap", ScansatBigMap);
            StepRegistry.RegisterAssertion("scansatListed", ScansatListed);
            StepRegistry.RegisterAssertion("scansatOverlay", ScansatOverlay);
            StepRegistry.RegisterAssertion("scansatDisplayModule", ScansatDisplayModule);
            StepRegistry.RegisterAssertion("stockChartLacks", StockChartLacks);
        }

        private static Assembly FindScansat()
        {
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SCANsat")
                ?? throw new InvalidOperationException("SCANsat is not loaded (it needs the full profile)");
        }

        private static Type TypeOf(Assembly scansat, string name)
        {
            return scansat.GetType(name) ?? throw new InvalidOperationException("SCANsat has no type " + name);
        }

        private static MethodInfo MethodOf(Type type, string name, BindingFlags flags)
        {
            return type.GetMethod(name, flags) ?? throw new InvalidOperationException($"{type.Name} has no method {name}");
        }

        private static PropertyInfo PropertyOf(Type type, string name)
        {
            return type.GetProperty(name, Inst | BindingFlags.Static) ?? throw new InvalidOperationException($"{type.Name} has no property {name}");
        }

        // The names of the entries of a SCANsat list property (strings, or objects with a Name).
        private static string Join(object value)
        {
            if (!(value is IEnumerable items))
            {
                return value == null ? "null" : "(" + value.GetType().Name + ")";
            }

            return string.Join(",", items.Cast<object>().Select(o => o?.ToString() ?? "null").ToArray());
        }

        // scansatFill body = Mun type = 256 - gives the body full SCANsat coverage of that scan type (128 = low
        // resolution resources, 256 = high resolution) at once, as SCANsat's own map fill does, so the test needs no
        // orbit passes of a scanner.
        private static IEnumerator ScansatFill(ConfigNode node, TestContext ctx)
        {
            Assembly scansat = FindScansat();
            string bodyName = node.RequireArg("body");
            int type = (int)node.ArgDouble("type", 256);
            CelestialBody body = FlightGlobals.GetBodyByName(bodyName) ?? throw new ArgumentException("unknown body " + bodyName);
            Type util = TypeOf(scansat, "SCANsat.SCANUtil");
            object data = util.GetMethod("getData", Stat, null, new[] { typeof(CelestialBody) }, null)?.Invoke(null, new object[] { body });
            if (data == null)
            {
                // SCANsat makes a body's data when it first needs it (a vessel scans it, or a map shows it), as its own
                // big map does.
                Type dataType = TypeOf(scansat, "SCANsat.SCAN_Data.SCANdata");
                ConstructorInfo create = dataType.GetConstructor(Inst, null, new[] { typeof(CelestialBody) }, null)
                    ?? throw new InvalidOperationException("SCANdata has no constructor taking a CelestialBody");
                data = create.Invoke(new object[] { body });
                object controller = PropertyOf(TypeOf(scansat, "SCANsat.SCANcontroller"), "controller").GetValue(null, null)
                    ?? throw new InvalidOperationException("no SCANcontroller (scene without SCANsat?)");
                MethodOf(controller.GetType(), "addToBodyData", Inst).Invoke(controller, new[] { body, data });
            }

            Type scanType = TypeOf(scansat, "SCANsat.SCAN_Data.SCANtype");
            MethodOf(data.GetType(), "fillMap", Inst).Invoke(data, new[] { Enum.ToObject(scanType, type) });
            MethodInfo isCovered = util.GetMethod("isCovered", Stat, null, new[] { typeof(double), typeof(double), typeof(CelestialBody), typeof(int) }, null)
                ?? throw new InvalidOperationException("SCANUtil has no isCovered(double, double, CelestialBody, int)");
            bool covered = (bool)isCovered.Invoke(null, new object[] { 20.0, 5.0, body, type });
            ctx.Pass($"{bodyName} filled with scan type {type}; covered at lat 5 lon 20: {covered}");
            yield break;
        }

        // scansatBigMap body = Mun resource = SubterraCavity - opens SCANsat's big map on that body with that resource
        // drawn (and waits for it to draw); close = true closes it. Follow with a screenshot step.
        private static IEnumerator ScansatBigMap(ConfigNode node, TestContext ctx)
        {
            Assembly scansat = FindScansat();
            Type launcherType = TypeOf(scansat, "SCANsat.SCAN_Toolbar.SCANappLauncher");
            object launcher = PropertyOf(launcherType, "Instance").GetValue(null, null) ?? throw new InvalidOperationException("no SCANappLauncher instance (flight scene?)");
            if (node.Arg("close") == "true")
            {
                PropertyOf(launcherType, "BigMap").SetValue(launcher, false, null);
                ctx.Pass("big map closed");
                yield break;
            }

            string bodyName = node.RequireArg("body");
            string resource = node.RequireArg("resource");
            Type util = TypeOf(scansat, "SCANsat.SCANUtil");
            Type bigMapType = TypeOf(scansat, "SCANsat.SCAN_Unity.SCAN_UI_BigMap");
            Type settingsType = TypeOf(scansat, "SCANsat.SCAN_Settings_Config");

            // The window's position must be set before it opens (x from the left, -y from the top).
            object config = PropertyOf(settingsType, "Instance").GetValue(null, null);
            FieldInfo position = settingsType.GetField("BigMapPosition") ?? throw new InvalidOperationException("SCAN_Settings_Config has no BigMapPosition");
            position.SetValue(config, new UnityEngine.Vector2(10, -10));
            PropertyOf(launcherType, "BigMap").SetValue(launcher, true, null);
            yield return new UnityEngine.WaitForSeconds(3f);

            object bigMap = PropertyOf(bigMapType, "Instance").GetValue(null, null) ?? throw new InvalidOperationException("the SCANsat big map did not open");
            string bodyDisplay = (string)MethodOf(util, "displayNameFromBodyName", Stat).Invoke(null, new object[] { bodyName });
            PropertyOf(bigMapType, "CurrentCelestialBody").SetValue(bigMap, bodyDisplay, null);
            yield return new UnityEngine.WaitForSeconds(3f);

            string resourceDisplay = (string)MethodOf(util, "displayNameFromResource", Stat).Invoke(null, new object[] { resource });
            PropertyOf(bigMapType, "CurrentResource").SetValue(bigMap, resourceDisplay, null);
            PropertyOf(bigMapType, "ResourceToggle").SetValue(bigMap, true, null);
            yield return new UnityEngine.WaitForSeconds(float.Parse(node.Arg("seconds", "8"), System.Globalization.CultureInfo.InvariantCulture));

            ctx.Pass($"big map on '{PropertyOf(bigMapType, "CurrentCelestialBody").GetValue(bigMap, null)}', resource '{PropertyOf(bigMapType, "CurrentResource").GetValue(bigMap, null)}', shown {PropertyOf(bigMapType, "ShowResource").GetValue(bigMap, null)}, resource list [{Join(PropertyOf(bigMapType, "Resources").GetValue(bigMap, null))}]");
        }

        // scansatListed resource = SubterraCavity - the open big map lists the resource in its resource menu (with
        // SCANsat's HideZeroResources at its default, which hides a resource it believes is absent from the body).
        private static bool ScansatListed(ConfigNode node, TestContext ctx)
        {
            Assembly scansat = FindScansat();
            Type bigMapType = TypeOf(scansat, "SCANsat.SCAN_Unity.SCAN_UI_BigMap");
            object bigMap = PropertyOf(bigMapType, "Instance").GetValue(null, null);
            if (bigMap == null)
            {
                ctx.Detail = "the big map is not open";
                return false;
            }

            string resource = node.RequireArg("resource");
            string display = (string)MethodOf(TypeOf(scansat, "SCANsat.SCANUtil"), "displayNameFromResource", Stat).Invoke(null, new object[] { resource });
            var list = ((IEnumerable)PropertyOf(bigMapType, "Resources").GetValue(bigMap, null)).Cast<object>().Select(o => o.ToString()).ToList();
            ctx.Detail = $"'{display}' in [{string.Join(",", list.ToArray())}]";
            return list.Contains(display);
        }

        // scansatOverlay resource = SubterraCavity body = Mun lat = 5 lon = 20 min = 0.3 max = 1 - what SCANsat itself
        // reads for that point (SCANUtil.ResourceOverlay, the call its maps and part readouts make) is within min..max.
        private static bool ScansatOverlay(ConfigNode node, TestContext ctx)
        {
            Assembly scansat = FindScansat();
            string bodyName = node.RequireArg("body");
            CelestialBody body = FlightGlobals.GetBodyByName(bodyName);
            if (body == null)
            {
                ctx.Detail = "unknown body " + bodyName;
                return false;
            }

            string resource = node.RequireArg("resource");
            double lat = node.ArgDouble("lat", 0), lon = node.ArgDouble("lon", 0);
            float value = (float)MethodOf(TypeOf(scansat, "SCANsat.SCANUtil"), "ResourceOverlay", Stat).Invoke(null, new object[] { lat, lon, resource, body, false });
            double min = node.ArgDouble("min", 0), max = node.ArgDouble("max", double.MaxValue);
            ctx.Detail = Invariant($"SCANUtil.ResourceOverlay({resource}, {bodyName}, lat {lat:F2}, lon {lon:F2}) = {value:F4} (wanted {min:F4} to {max:F4})");
            return value >= min - 1e-6 && value <= max + 1e-6;
        }

        // scansatDisplayModule part = OrbitalScanner resource = SubterraCavity - the part (prefab) of the game database
        // carries a SCANresourceDisplay for that resource: the compatibility cfg was applied.
        private static bool ScansatDisplayModule(ConfigNode node, TestContext ctx)
        {
            string partName = node.RequireArg("part");
            string resource = node.RequireArg("resource");
            AvailablePart info = PartLoader.getPartInfoByName(partName);
            if (info == null)
            {
                ctx.Detail = "no part " + partName;
                return false;
            }

            var found = new List<string>();
            foreach (PartModule module in info.partPrefab.Modules)
            {
                if (module.GetType().Name != "SCANresourceDisplay")
                {
                    continue;
                }

                FieldInfo field = module.GetType().GetField("ResourceName", Inst);
                found.Add(field == null ? "?" : (string)field.GetValue(module));
            }

            ctx.Detail = $"{partName}: SCANresourceDisplay for [{string.Join(",", found.ToArray())}]";
            return found.Contains(resource);
        }

        // stockChartLacks body = Mun resource = SubterraCavity - the resource is not in the stock KerbNet resource chart
        // of that body once the planet is scanned (ResourceMap.GetResourceItemList, which the chart is made from), while
        // it is in the game's list of planetary resources. Unlocking the planet is a test-only shortcut for the KerbNet scan.
        private static bool StockChartLacks(ConfigNode node, TestContext ctx)
        {
            string bodyName = node.RequireArg("body");
            string resource = node.RequireArg("resource");
            CelestialBody body = FlightGlobals.GetBodyByName(bodyName);
            if (body == null || ResourceMap.Instance == null)
            {
                ctx.Detail = "unknown body or no resource map";
                return false;
            }

            ResourceMap.Instance.UnlockPlanet(body.flightGlobalsIndex);
            List<PlanetaryResource> chart = ResourceMap.Instance.GetResourceItemList(HarvestTypes.Planetary, body);
            ctx.Detail = $"chart of {bodyName}: [{string.Join(",", chart.Select(r => Invariant($"{r.resourceName}={r.fraction:F3}")).ToArray())}]; names: {string.Join(",", ResourceMap.Instance.FetchAllResourceNames(HarvestTypes.Planetary).ToArray())}";
            // The resource must exist in the game's list, or the check would pass for the wrong reason.
            return ResourceMap.Instance.FetchAllResourceNames(HarvestTypes.Planetary).Contains(resource) && chart.All(r => r.resourceName != resource);
        }
    }
}
