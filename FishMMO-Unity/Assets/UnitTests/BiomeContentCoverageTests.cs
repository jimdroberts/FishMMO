using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using FishMMO.Client;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every biome has what the world builds from it beyond its ground art (audit 2026-10-11): the resources its gathering
	/// sites draw, the way its ground wears, the animals that live on it, and POI templates that name only real biomes.
	/// The biomes are the spec table's, which a guard elsewhere keeps equal to the biome assets.
	/// </summary>
	public class BiomeContentCoverageTests
	{
		private static IEnumerable<string> Biomes() => BiomeArtSpec.Entries.Select(e => e.Biome);

		[Test]
		public void EveryBiomeWithGatheringSites_HasAResourceThatNamesIt()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			var gaps = new List<string>();
			foreach (string biome in Biomes())
			{
				var families = new HashSet<GatheringFamily>();
				foreach (PointOfInterestKindRule rule in rules.Ordered(PointOfInterestPlacement.Placed))
				{
					if (rule.Enabled && rule.BiomeWeight(biome) > 0f)
					{
						families.UnionWith(GatheringResourceCatalogue.FamiliesFor(rule.Kind));
					}
				}
				// The biome's offer as the authoring writes it onto its template (2026-10-16: the template, not the name, decides).
				List<string> offered = BiomeArtSpec.For(biome).GatheringResources();
				if (families.Count > 0 && !GatheringResourceCatalogue.All.Any(r => families.Contains(r.Family) && offered.Contains(r.Node)))
				{
					gaps.Add($"{biome} ({string.Join("/", families)})");
				}
			}
			Assert.IsEmpty(gaps, "gathering sites in these biomes would only draw other biomes' resources: " + string.Join(", ", gaps));
		}

		[Test]
		public void EveryBiome_HasATerrainProcess()
		{
			var tabled = new HashSet<string>(TerrainProcessTable.Assignments.Select(a => a.Biome));
			string[] missing = Biomes().Where(b => !tabled.Contains(b)).ToArray();
			Assert.IsEmpty(missing, "these biomes would wear as temperate country: " + string.Join(", ", missing));
			foreach ((string biome, string profile) in TerrainProcessTable.Assignments)
			{
				Assert.IsTrue(TerrainProcessTable.TryGetProfile(profile, out _), $"{biome} wears '{profile}', which is no profile");
			}
		}

		[Test]
		public void EveryBiome_HasAnAmbientHabitat()
		{
			// The spec's habitat, which the authoring writes onto the template the client reads (2026-10-16), and the name
			// reading a template never authored still falls back on.
			string[] missing = BiomeArtSpec.Entries.Where(e => e.Habitat == FishMMO.Shared.Biomes.BiomeHabitat.None).Select(e => e.Biome).ToArray();
			Assert.IsEmpty(missing, "these biomes name no country, so their animals fall back on the climate: " + string.Join(", ", missing));
			missing = Biomes().Where(b => AmbientGating.HabitatOf(b) == AmbientHabitat.None).ToArray();
			Assert.IsEmpty(missing, "no keyword matches these biomes' names, so their animals fall back on the climate: " + string.Join(", ", missing));
		}

		[Test]
		public void PoiTemplateSpecs_NameOnlyRealBiomes()
		{
			var biomes = new HashSet<string>(Biomes(), System.StringComparer.OrdinalIgnoreCase);
			var unknown = new SortedSet<string>();
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				foreach (PointOfInterestBiomeWeight weight in spec.BiomeWeights)
				{
					if (!biomes.Contains(weight.Biome))
					{
						unknown.Add($"{weight.Biome} ({spec.Name})");
					}
				}
			}
			Assert.IsEmpty(unknown, "a weight for a biome that does not exist matches nothing: " + string.Join(", ", unknown));
		}

		// ── The template as the cut's one source (2026-10-15) ─────────

		/// <summary>
		/// The spec's way and river-bar columns, unset, are exactly what the cut decided in code before the template carried
		/// them, for every biome: the path and road grounds its main ground wears to, the flagstones every biome paved with,
		/// and the desert's and the forest's riverbeds every biome's bars borrowed. So authoring them changes no scene.
		/// </summary>
		[Test]
		public void SpecWaysAndBars_DefaultToWhatTheCutDecidedInCode()
		{
			string desertSand = BiomeArtSpec.For("Desert").Riverbed, forestGravel = BiomeArtSpec.For("Forest").Riverbed;
			Assert.That(desertSand, Is.EqualTo(Ground.Sand), "every biome's bars were the desert riverbed's sand");
			Assert.That(forestGravel, Is.EqualTo(Ground.Gravel), "and the forest riverbed's gravel");
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				// The ground the cut wore paths from: the main layer's, else the first detail's, else grass.
				string main = entry.Main?.Family ?? entry.Details.Select(d => d.Family).FirstOrDefault(f => f != null) ?? Ground.Grass;
				Assert.That(entry.PaintedGround, Is.EqualTo(main), entry.Biome);
				Assert.That(entry.PathGround, Is.EqualTo(SurfaceCatalogue.PathGroundFor(main)), $"{entry.Biome}: path ground");
				Assert.That(entry.RoadGround, Is.EqualTo(SurfaceCatalogue.RoadGroundFor(main)), $"{entry.Biome}: road ground");
				Assert.That(entry.CobbleGround, Is.EqualTo(Ground.Flagstone), $"{entry.Biome}: cobbles");
				Assert.That(entry.BarSand, Is.EqualTo(desertSand), $"{entry.Biome}: bar sand");
				Assert.That(entry.BarGravel, Is.EqualTo(forestGravel), $"{entry.Biome}: bar gravel");
				foreach (string ground in new[] { entry.PathGround, entry.RoadGround, entry.CobbleGround, entry.BarSand, entry.BarGravel })
				{
					Assert.That(SurfaceCatalogue.TryGround(ground, out _), Is.True, $"{entry.Biome}: '{ground}' is a generated ground");
				}
			}
		}

		/// <summary>
		/// A herb grove's flowers and a cave mouth's growth, as the spec now hands them to the template, are what the two
		/// sites computed from the spec at the cut before (their code, kept here as it was), for every biome.
		/// </summary>
		[Test]
		public void SpecGroveFlowersAndCaveMouths_AreWhatTheSitesComputedBefore()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.GroveFlowers(), Is.EqualTo(OldFlowersFor(entry)), $"{entry.Biome}: grove flowers");
				OldGrowthFor(entry, out List<string> bushes, out List<string> ferns, out List<string> boulders, out List<string> logs);
				BiomeArtSpec.CaveMouthGrowth growth = entry.CaveMouth();
				Assert.That(growth.Bushes, Is.EqualTo(bushes), $"{entry.Biome}: cave-mouth bushes");
				Assert.That(growth.Ferns, Is.EqualTo(ferns), $"{entry.Biome}: cave-mouth ferns");
				Assert.That(growth.Boulders, Is.EqualTo(boulders), $"{entry.Biome}: cave-mouth boulders");
				Assert.That(growth.Logs, Is.EqualTo(logs), $"{entry.Biome}: cave-mouth logs");
				Assert.That(growth.Creeper, Is.EqualTo("Detail_Ivy"), $"{entry.Biome}: the creeper every cave mouth grew");
			}
		}

		// PointOfInterestHerbGrove.FlowersFor before 2026-10-15.
		private static List<string> OldFlowersFor(BiomeArtSpec.Entry entry)
		{
			var flowers = new List<string>();
			foreach ((BiomeArtSpec.Layer layer, BiomeArtSpec.Scatter rule) in entry.Rules())
			{
				if (rule.Channel != FishMMO.Shared.Biomes.PrefabSpawnChannel.DetailLayer || rule.Prefabs == null)
				{
					continue;
				}
				foreach (string prefab in rule.Prefabs)
				{
					if (!string.IsNullOrEmpty(prefab) && prefab.StartsWith("Detail_Flowers", StringComparison.Ordinal) && !flowers.Contains(prefab))
					{
						flowers.Add(prefab);
					}
				}
			}
			if (flowers.Count == 0)
			{
				flowers.Add("Detail_FlowersMeadow");
			}
			return flowers;
		}

		// PointOfInterestCaveMouths.GrowthFor before 2026-10-15, by name.
		private static void OldGrowthFor(BiomeArtSpec.Entry entry, out List<string> bushes, out List<string> ferns, out List<string> boulders, out List<string> logs)
		{
			bushes = new List<string>();
			ferns = new List<string>();
			boulders = new List<string>();
			logs = new List<string>();
			foreach ((BiomeArtSpec.Layer layer, BiomeArtSpec.Scatter rule) in entry.Rules())
			{
				if (rule.Prefabs == null)
				{
					continue;
				}
				foreach (string prefab in rule.Prefabs)
				{
					if (string.IsNullOrEmpty(prefab))
					{
						continue;
					}
					List<string> into = prefab.StartsWith("Detail_Bush_", StringComparison.Ordinal) ? bushes
						: prefab.StartsWith("Detail_Fern", StringComparison.Ordinal) ? ferns
						: prefab.StartsWith("Boulder_", StringComparison.Ordinal) ? boulders
						: prefab.StartsWith("Deadwood_", StringComparison.Ordinal) ? logs
						: null;
					if (into != null && !into.Contains(prefab))
					{
						into.Add(prefab);
					}
				}
			}
			if (ferns.Count == 0)
			{
				ferns.AddRange(new[] { "Detail_FernBracken", "Detail_Fern", "Detail_FernHartstongue" });
			}
			if (bushes.Count == 0)
			{
				bushes.AddRange(new[] { "Detail_Bush_Hazel", "Detail_Bush_Bramble" });
			}
		}

		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		/// <summary>
		/// The cut-time code that places ground, rock, ways and growth reads the biome template, never the spec table, and
		/// never picks a biome's ground by another biome's name: the scene generator, the terrain placers and painters
		/// (cliffs, river boulders, fall ledges, scatter, palette) and every point-of-interest builder. The one file allowed
		/// is BiomeTemplateArt, whose logged fallbacks cover a template not yet authored.
		/// </summary>
		[Test]
		public void CutTimeCode_ReadsTheTemplate_NotTheSpecTable()
		{
			var files = new List<string> { Path.Combine(WorldFolder, "SceneGenerator.cs") };
			files.AddRange(Directory.GetFiles(Path.Combine(WorldFolder, "Terrain"), "*.cs", SearchOption.AllDirectories));
			files.AddRange(Directory.GetFiles(Path.Combine(WorldFolder, "PointsOfInterest"), "*.cs", SearchOption.AllDirectories));
			Assert.That(files.Count, Is.GreaterThan(50), "the scan found the cut's sources");
			var readers = new List<string>();
			foreach (string file in files)
			{
				string name = file.Replace('\\', '/');
				if (name.EndsWith("/Terrain/BiomeTemplateArt.cs", StringComparison.Ordinal))
				{
					continue;
				}
				string code = CodeOnly(File.ReadAllText(file));
				foreach (string read in new[] { "BiomeArtSpec", "PathGroundFor(", "RoadGroundFor(", "BiomeRegistry.Get(\"Desert\")", "BiomeRegistry.Get(\"Forest\")" })
				{
					if (code.Contains(read))
					{
						readers.Add($"{Path.GetFileName(file)}: {read}");
					}
				}
			}
			Assert.IsEmpty(readers, "cut-time code deciding a biome's content from something other than its template: " + string.Join(", ", readers));
			string art = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "Terrain/BiomeTemplateArt.cs")));
			Assert.That(art, Does.Contain("Fallback(biome"), "the fallbacks warn");
		}

		/// <summary>The source with comment lines dropped, so a scan sees code and not prose.</summary>
		private static string CodeOnly(string source)
		{
			var code = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}
	}
}
