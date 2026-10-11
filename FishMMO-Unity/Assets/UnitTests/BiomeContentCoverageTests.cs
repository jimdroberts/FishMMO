using System.Collections.Generic;
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
				if (families.Count > 0 && !GatheringResourceCatalogue.All.Any(r => families.Contains(r.Family) && r.Suits(biome)))
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
			string[] missing = Biomes().Where(b => AmbientGating.HabitatOf(b) == AmbientHabitat.None).ToArray();
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
	}
}
