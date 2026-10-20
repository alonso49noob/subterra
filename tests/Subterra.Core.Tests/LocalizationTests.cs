using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Subterra.Core.Config;
using Xunit;

namespace Subterra.Core.Tests
{
    /// <summary>Task 36: both languages have the same keys, and every #Subterra_ key used in cfg or code exists.</summary>
    public class LocalizationTests
    {
        private static readonly Regex KeyPattern = new Regex(@"#Subterra_[A-Za-z0-9_]+");

        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Subterra.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir ?? throw new DirectoryNotFoundException("Subterra.sln not found above the test folder");
        }

        private static HashSet<string> Keys(string language)
        {
            string path = Path.Combine(RepoRoot(), "GameData", "Subterra", "Localization", language + ".cfg");
            CfgNode root = CfgText.Parse(File.ReadAllText(path)).Single(n => n.Name == "Localization");
            CfgNode lang = root.GetNode(language);
            Assert.NotNull(lang);
            return new HashSet<string>(lang.Values.Select(v => v.Key));
        }

        [Fact]
        public void SameKeysInBothLanguages()
        {
            HashSet<string> en = Keys("en-us"), es = Keys("es-es");
            Assert.Empty(en.Except(es));
            Assert.Empty(es.Except(en));
        }

        [Fact]
        public void EveryUsedKeyExists()
        {
            string root = RepoRoot();
            HashSet<string> en = Keys("en-us");
            var files = Directory.EnumerateFiles(Path.Combine(root, "GameData"), "*.cfg", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Localization" + Path.DirectorySeparatorChar))
                .Concat(Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)));
            var missing = new List<string>();
            foreach (string file in files)
            {
                foreach (Match m in KeyPattern.Matches(File.ReadAllText(file)))
                {
                    // Keys built at run time from a prefix ("#Subterra_Biome_" + id) are checked by the biome tests.
                    if (m.Value.EndsWith("_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!en.Contains(m.Value))
                    {
                        missing.Add($"{m.Value} in {Path.GetFileName(file)}");
                    }
                }
            }

            Assert.Empty(missing.Distinct());
        }

        [Fact]
        public void EveryZoneHasABiomeName()
        {
            HashSet<string> en = Keys("en-us");
            foreach (Subterra.Core.Model.ZoneKind zone in Enum.GetValues(typeof(Subterra.Core.Model.ZoneKind)))
            {
                if (zone != Subterra.Core.Model.ZoneKind.None)
                {
                    Assert.Contains("#Subterra_Biome_" + zone, en);
                }
            }
        }
    }
}
