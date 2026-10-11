using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every biome is dressed (2026-10-10, Jim: "do not skip any of them — generate properly for each biome"):
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
	}
}
