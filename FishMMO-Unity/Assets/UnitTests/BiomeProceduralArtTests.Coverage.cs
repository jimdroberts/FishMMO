using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every biome is dressed (2026-10-10: "do not skip any of them — generate properly for each biome"):
	/// each spec entry carries at least three things of its own, the lifeless and alien ones included, and the five
	/// biomes that once had nothing at all now have their ground.
	/// </summary>
	public partial class BiomeProceduralArtTests
	{
		/// <summary>The fewest scatter rules (main, details and bed together) a biome may carry.</summary>
		private const int FewestRulesABiomeCarries = 3;

		[Test]
		public void SpecTable_EveryBiomeCarriesAtLeastThreeThingsOfItsOwn()
		{
			var thin = new List<string>();
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				int rules = entry.Rules().Count();
				if (rules < FewestRulesABiomeCarries)
				{
					thin.Add($"{entry.Biome} ({rules})");
				}
			}
			Assert.IsEmpty(thin, "a biome with fewer than three rules reads as bare ground: " + string.Join(", ", thin));
		}

		[Test]
		public void SpecTable_TheBiomesThatHadNothing_NowHaveTheirGround()
		{
			foreach (string name in new[] { "Methane Lake", "Dust Sea", "Salt Flat", "Nitrogen Ice Field", "Ice Palace", "Ice Sheet" })
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				Assert.IsNotNull(entry, $"{name} has a spec entry");
				Assert.GreaterOrEqual(entry.Rules().Count(), FewestRulesABiomeCarries, $"{name} is dressed");
			}
		}

		[Test]
		public void SpecTable_NoBiomeIsListedTwice()
		{
			List<string> names = BiomeArtSpec.Entries.Select(e => e.Biome).ToList();
			Assert.AreEqual(names.Count, names.Distinct().Count(), "one entry a biome: " +
				string.Join(", ", names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));
		}

		/// <summary>Where each biome's entry is written: one file a biome (2026-10-15).</summary>
		private const string SpecBiomeFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt/BiomeArtSpec";

		/// <summary>
		/// Each biome's entry lives in its own file, BiomeArtSpec.&lt;Biome&gt;.cs (the asset's name without its spaces),
		/// which writes that biome's entry and no other, and every such file's biome is in the table: a file whose method
		/// the table forgot to call would drop its biome silently.
		/// </summary>
		[Test]
		public void SpecTable_EachBiomeIsWrittenInItsOwnFile()
		{
			Assert.That(Directory.Exists(SpecBiomeFolder), Is.True, "run from the project root");
			var entryCall = new Regex("\\bE\\(\"([^\"]+)\"");
			var files = Directory.GetFiles(SpecBiomeFolder, "*.cs", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();
			Assert.That(files.Count, Is.EqualTo(BiomeArtSpec.Entries.Length), "one file a biome");
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				string name = Regex.Replace(entry.Biome, "[^A-Za-z0-9]+", string.Empty);
				string path = Path.Combine(SpecBiomeFolder, "BiomeArtSpec." + name + ".cs");
				Assert.That(File.Exists(path), Is.True, $"{entry.Biome}'s entry belongs in {path}");
				string source = File.ReadAllText(path);
				string[] biomes = entryCall.Matches(source).Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
				Assert.That(biomes, Is.EqualTo(new[] { entry.Biome }), $"{path} writes {entry.Biome}'s entry and no other");
				Assert.That(source, Does.Contain("private static Entry " + name + "()"), $"{path}: the biome's method is named for it");
			}
		}

		/// <summary>
		/// A biome's file declares no static field. <see cref="BiomeArtSpec.Entries"/> is built by a static initialiser,
		/// and the initialisers in a partial class's other files run in an order the language does not promise: a field
		/// an entry read could still be unset (null, or zero) when the table is built. Shared values are properties or methods.
		/// </summary>
		[Test]
		public void SpecTable_BiomeFilesDeclareNoStaticField()
		{
			Assert.That(Directory.Exists(SpecBiomeFolder), Is.True, "run from the project root");
			// A static member whose name is followed by '=' (not '=>') or ';': a field. A method's name is followed by '(' and a property's by '=>'.
			var field = new Regex(@"\bstatic\s+(readonly\s+)?[\w.<>\[\],?\s]*?\s(\w+)\s*(=(?!>)|;)");
			foreach (string path in Directory.GetFiles(SpecBiomeFolder, "*.cs", SearchOption.AllDirectories))
			{
				foreach (string line in File.ReadAllLines(path))
				{
					string code = line.Trim();
					if (code.StartsWith("//", System.StringComparison.Ordinal))
					{
						continue;
					}
					Match m = field.Match(code);
					Assert.That(m.Success, Is.False, $"{Path.GetFileName(path)} declares a static field ({code}): write it as a property or method");
				}
			}
		}
	}
}
