using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The ground's rock and crystal scatter ranges wide and leans small (2026-10-20: "we want massive crystals"): BiomeArtSpec's
	/// pass over every entry, the earlier version kept for the authoring tool, and the plants left as they were.
	/// </summary>
	public class MassiveScatterTests
	{
		private static BiomeArtSpec.Scatter Find(string biome, string name)
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For(biome);
			Assert.That(e, Is.Not.Null, biome);
			foreach (BiomeArtSpec.Layer layer in e.Details)
			{
				foreach (BiomeArtSpec.Scatter s in layer.Scatter)
				{
					if (s.Name == name)
					{
						return s;
					}
				}
			}
			foreach (BiomeArtSpec.Scatter s in e.Main.Scatter)
			{
				if (s.Name == name)
				{
					return s;
				}
			}
			Assert.Fail($"{biome}: no rule '{name}'");
			return null;
		}

		[Test]
		public void CrystalsRangeToGiants_AndMostStayOrdinary()
		{
			BiomeArtSpec.Scatter crystals = Find("Ice Palace", "Formations: Ice crystals");
			Assert.That(crystals.Scale.y, Is.GreaterThanOrEqualTo(6.5f), "the odd crystal several times the old largest");
			Assert.That(crystals.Scale.x, Is.LessThan(1f), "and small ones among them");
			Assert.That(crystals.ScaleSkew, Is.GreaterThan(2f), "leaned small: a few giants, not a field of them");
			Assert.That(crystals.Earlier.Exists(e => e.ScaleSkew == 1f && e.Scale == new Vector2(1.5f, 3f)), Is.True,
				"what an earlier run wrote is kept, so the tool upgrades it rather than taking it for tuning");
		}

		[Test]
		public void EveryRockAndCrystalRuleIsWidened_NothingElse()
		{
			int crystals = 0, rocks = 0;
			foreach (BiomeArtSpec.Entry e in BiomeArtSpec.Entries)
			{
				var layers = new System.Collections.Generic.List<BiomeArtSpec.Layer> { e.Main };
				layers.AddRange(e.Details);
				foreach (BiomeArtSpec.Layer layer in layers)
				{
					if (layer == null)
					{
						continue;
					}
					foreach (BiomeArtSpec.Scatter s in layer.Scatter)
					{
						bool rock = s.Channel == PrefabSpawnChannel.TreeInstance && s.Name != null
							&& (s.Name.StartsWith("Formations: ") || s.Name == "Boulders" || s.Name == "Ice boulders");
						bool crystal = rock && System.Array.Exists(s.Prefabs, p => p != null && p.Contains("Crystal"));
						if (crystal)
						{
							crystals++;
							Assert.That(s.ScaleSkew, Is.GreaterThan(2f), $"{e.Biome} / {s.Name}");
							Assert.That(s.Scale.y / s.Scale.x, Is.GreaterThan(6f), $"{e.Biome} / {s.Name}: a wide range");
						}
						else if (rock)
						{
							rocks++;
							Vector2 was = s.Earlier[s.Earlier.Count - 1].Scale;
							Assert.That(s.ScaleSkew, Is.GreaterThan(1.5f), $"{e.Biome} / {s.Name}");
							Assert.That(s.Scale.y, Is.GreaterThanOrEqualTo(Mathf.Min(2.5f * was.y, 4.2f) - 1e-4f), $"{e.Biome} / {s.Name}: the odd great one, against its own old largest");
							Assert.That(s.Scale.x, Is.LessThan(was.x), $"{e.Biome} / {s.Name}: and smaller ones");
						}
						else if (s.Name != null && (s.Name.StartsWith("Trees") || s.Name.StartsWith("Deadwood")))
						{
							Assert.That(s.ScaleSkew, Is.EqualTo(1f), $"{e.Biome} / {s.Name}: trees and deadwood keep theirs");
						}
					}
				}
			}
			Assert.That(crystals, Is.GreaterThan(5), "crystal rules found");
			Assert.That(rocks, Is.GreaterThan(50), "rock rules found");
		}

		[Test]
		public void TheLeanIsExactlyEvenAtOne_AndSmallAboveIt()
		{
			for (float t = 0f; t <= 1f; t += 0.125f)
			{
				Assert.That(TerrainScatter.Skewed(t, 1f), Is.EqualTo(t), "1 leaves every rule as it was");
				Assert.That(TerrainScatter.Skewed(t, 0f), Is.EqualTo(t), "an asset older than the field reads even");
			}
			Assert.That(TerrainScatter.Skewed(0.5f, 2.4f), Is.LessThan(0.25f), "the middle draw stays small");
			Assert.That(TerrainScatter.Skewed(1f, 2.4f), Is.EqualTo(1f), "the top still reaches the top");
		}
	}
}
