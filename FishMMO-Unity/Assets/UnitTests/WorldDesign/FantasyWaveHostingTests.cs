using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Where the fantasy wave stands (2026-10-27): every wave kind is hosted by some biome's .Sites list; keeps and towers weigh at
	/// least 1 in every earth land biome; the wild lands name the hostile and ruined keeps and towers, the woods the elves', the high
	/// places the dwarves'; and the alien worlds host only the wave's relics. Pure: the spec files' entries and their source.
	/// </summary>
	[TestFixture]
	public class FantasyWaveHostingTests
	{
		private static readonly string[] NotEarthLand =
		{
			"Molten Surface", "Runaway Greenhouse Plain", "Sulphuric Cloud Deck", "Radiation Plain", "Regolith Plain", "Tholin Plain",
			"Nitrogen Ice Field", "Methane Lake", "Cryovolcanic Plain", "Ice Geyser Field", "Tidal Fracture", "Subsurface Ocean Vent",
			"Impact Basin", "Dust Sea", "Rille",
			"Ocean", "Deep Ocean", "Coastal Water", "Abyss", "Abyssal Plain", "Seamount", "Underwater Canyon", "Coral Reef",
			"Lake", "River", "Cave", "Ice Cave", "Lava Tube", "Swamp Cave", "Volcanic Cave",
		};

		private static readonly string[] Wild =
		{
			"Swamp", "Peat Bog", "Mangrove", "Swamp Ruins", "Swamp Temple", "Wetlands", "Tundra", "Snow", "Glacier", "Ice Sheet", "Ice Shelf",
			"Permanent Ice", "Ice Ruin", "Ice Palace", "Volcanic", "Geyser Basin", "Sulphur Flats", "Volcanic Temple", "Wasteland", "Desert",
			"Badlands", "Salt Flat", "Scree", "Forest Ruins", "Jungle", "Jungle Ruins", "Jungle Temple", "Crater",
		};

		private static IEnumerable<BiomeArtSpec.Entry> EarthLand() => BiomeArtSpec.Entries.Where(e => !NotEarthLand.Contains(e.Biome));

		private static float WeightOf(BiomeArtSpec.Entry entry, POIType kind)
		{
			BiomeSiteKind[] found = entry.SiteKinds().Where(k => k.Kind == kind).ToArray();
			return found.Length > 0 ? found[0].Weight : 0f;
		}

		[Test]
		public void EveryWaveKind_IsHostedBySomeBiome()
		{
			var hosted = new HashSet<POIType>(BiomeArtSpec.Entries.SelectMany(e => e.SiteKinds()).Where(k => k.Weight > 0f).Select(k => k.Kind));
			string[] unhosted = FantasyWaveKindsTests.Kinds().Where(k => !hosted.Contains(k)).Select(k => k.ToString()).ToArray();
			Assert.That(unhosted, Is.Empty, "wave kinds no biome hosts");
		}

		[Test]
		public void KeepsAndTowers_WeighAtLeastOne_InEveryEarthLandBiome()
		{
			int biomes = 0;
			foreach (BiomeArtSpec.Entry entry in EarthLand())
			{
				Assert.That(WeightOf(entry, POIType.Keep), Is.GreaterThanOrEqualTo(1f), $"{entry.Biome}: keeps");
				Assert.That(WeightOf(entry, POIType.Tower), Is.GreaterThanOrEqualTo(1f), $"{entry.Biome}: towers");
				biomes++;
			}
			Assert.That(biomes, Is.EqualTo(53), "every earth land biome was seen");
		}

		/// <summary>The wild lands hold hostile and ruined keeps and towers; the woods hold the elves', the high places the dwarves'.</summary>
		[Test]
		public void TheLandsNameWhoHoldsTheirKeepsAndTowers()
		{
			string folder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt/BiomeArtSpec";
			var sources = Directory.GetFiles(folder, "BiomeArtSpec.*.cs").Select(File.ReadAllText).ToArray();
			string SourceOf(string biome) => sources.Single(s => s.Contains("return E(\"" + biome + "\""));
			bool Prefers(string biome, string kind, string variant) => SourceOf(biome).Contains($".Prefers(POIType.{kind}, \"{variant}\"");
			foreach (string biome in Wild)
			{
				Assert.That(Prefers(biome, "Keep", "Orc Hold") && Prefers(biome, "Keep", "Ruined Keep") && Prefers(biome, "Tower", "Dark Tower"), Is.True,
					$"{biome}: the wild's keeps are held by its enemies or lie ruined");
			}
			foreach (string biome in new[] { "Forest", "Woodland", "Taiga" })
			{
				Assert.That(Prefers(biome, "Keep", "Elven Hall") || Prefers(biome, "Tower", "Elven Tower"), Is.True, $"{biome}: the elves'");
			}
			foreach (string biome in new[] { "Alpine", "Mountain Slope", "Rocky Terrain", "Karst" })
			{
				Assert.That(Prefers(biome, "Keep", "Dwarven Hold") || Prefers(biome, "Tower", "Dwarven Tower"), Is.True, $"{biome}: the dwarves'");
			}
			foreach (string biome in new[] { "Plains", "Farmland", "Grassland" })
			{
				Assert.That(Prefers(biome, "Keep", "Orc Hold"), Is.False, $"{biome}: settled land keeps its garrisons");
			}
		}

		/// <summary>On the alien worlds the wave hosts only its relics, never a lived-in kind.</summary>
		[Test]
		public void AlienWorlds_HostOnlyTheWavesRelics()
		{
			var wave = new HashSet<POIType>(FantasyWaveKindsTests.Kinds());
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries.Where(e => NotEarthLand.Take(15).Contains(e.Biome)))
			{
				foreach (BiomeSiteKind site in entry.SiteKinds().Where(k => wave.Contains(k.Kind)))
				{
					Assert.That(site.Kind == POIType.ElderMonolith || site.Kind == POIType.WaygateRing, Is.True, $"{entry.Biome} hosts {site.Kind}");
					Assert.That(BiomeSiteAir.NeedsAirByDefault(site.Kind), Is.False, $"{site.Kind} stands where no one breathes");
				}
			}
		}
	}
}
