using System;
using NUnit.Framework;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Which rock a biome's cliffs, river boulders and fall ledges are made of: the planet geology's where the biome
	/// accepts it, else the biome's own. Geology alone walled Flo Monolith's bog and taiga in sandstone and
	/// Baoakraal's karst and grassland in conglomerate (Jim, 2026-10-08).
	/// </summary>
	[TestFixture]
	public class BiomeRockTests
	{
		/// <summary>Green, wet or cold biomes: never walled in sandstone, whatever the geology under them.</summary>
		private static readonly string[] Green =
		{
			"Grassland", "Plains", "Farmland", "Forest", "Forest Ruins", "Woodland", "Hills", "Taiga", "Tundra", "Peat Bog",
			"Wetlands", "Swamp", "Lake", "River", "Estuary", "Karst", "Jungle", "Bamboo Forest", "Alpine Meadow", "Valley",
		};

		[Test]
		public void EveryRockABiomeAcceptsIsARockWithCliffArt()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				string own = CliffRocks.OwnRock(entry);
				Assert.That(CliffRocks.MaterialName(own), Is.Not.Null, $"{entry.Biome}: its own rock {own} has cliff art");
				foreach (string rock in entry.Rocks)
				{
					Assert.That(RockTypes.TryGet(rock, out _), Is.True, $"{entry.Biome}: {rock} is a rock type");
					Assert.That(CliffRocks.MaterialName(rock), Is.Not.Null, $"{entry.Biome}: {rock} has cliff art");
				}
			}
		}

		[Test]
		public void EveryBiomeTakesOnlyARockItAccepts_WhateverTheGeology()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(CliffRocks.RockFor(entry, null), Is.EqualTo(CliffRocks.OwnRock(entry)), $"{entry.Biome}: no geology, its own rock");
				foreach (Lithology lithology in PlanetGeology.Lithologies)
				{
					string rock = CliffRocks.RockFor(entry, lithology.RockTypeName);
					Assert.That(rock == CliffRocks.Ice || CliffRocks.Accepts(entry, rock), Is.True,
						$"{entry.Biome} on {lithology.Name} takes {rock}, which it does not accept");
				}
			}
		}

		[Test]
		public void AGreenBiomeIsNeverWalledInSandstone()
		{
			foreach (string name in Green)
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				Assert.That(entry, Is.Not.Null, name);
				Assert.That(CliffRocks.RockFor(entry, "Sandstone"), Is.Not.EqualTo("Sandstone"), $"{name} on a sandstone province");
				if (CliffRocks.OwnRock(entry) != "Conglomerate")
				{
					Assert.That(CliffRocks.RockFor(entry, "Conglomerate"), Is.Not.EqualTo("Conglomerate"), $"{name} on a conglomerate province");
				}
			}
		}

		[Test]
		public void AcceptedGeologyStillVariesTheRock()
		{
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Grassland"), "Gneiss"), Is.EqualTo("Gneiss"), "a grassland on gneiss stands in gneiss");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Grassland"), "Sandstone"), Is.EqualTo("Granite"), "and on sandstone in its own granite");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Desert"), "Limestone"), Is.EqualTo("Limestone"), "a desert on limestone stands in limestone");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Desert"), "Gneiss"), Is.EqualTo("Sandstone"), "and on gneiss in its own sandstone");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Karst"), "Granite"), Is.EqualTo("Limestone"), "karst is limestone country");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Glacier"), "Granite"), Is.EqualTo(CliffRocks.Ice), "ice stays ice");
		}

		[Test]
		public void EveryLandBiomeOfTheArthisScenesNamesItsRocks()
		{
			// A misspelt key in the table would leave a biome on its own rock alone, silently.
			foreach (string name in new[] { "Grassland", "Forest", "Taiga", "Peat Bog", "Karst", "Hills", "Mountain Slope", "Woodland", "Savanna", "Rocky Terrain", "Scree", "Valley", "Desert" })
			{
				Assert.That(BiomeArtSpec.For(name).Rocks, Is.Not.Empty, name);
			}
		}

		[Test]
		public void SandstoneIsNoLongerTheLikeliestProvinceOnAnEarthLikeWorld()
		{
			float[] shares = PlanetGeology.Shares(BiomeWorldConditions.Earthlike);
			int sandstone = Array.FindIndex(PlanetGeology.Lithologies, l => l.Name == "Sandstone");
			int shale = Array.FindIndex(PlanetGeology.Lithologies, l => l.Name == "Shale");
			int conglomerate = Array.FindIndex(PlanetGeology.Lithologies, l => l.Name == "Conglomerate");
			Assert.That(shares[sandstone], Is.LessThan(0.13f));
			Assert.That(shares[shale], Is.GreaterThan(shares[sandstone]), "shale is the commonest sediment");
			Assert.That(shares[sandstone] + shares[conglomerate], Is.LessThan(0.2f));
		}

		[Test]
		public void BouldersAndLedgesAskTheSameRuleAsTheCliffs()
		{
			string generator = System.IO.File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs");
			Assert.That(generator, Does.Contain("Func<float, float, float, string> rockAt = BiomeRockTypes(field, cliffOptions.RockTypeAt);"));
			Assert.That(generator, Does.Not.Contain("cliffOptions.RockTypeAt, options.Seed"), "no boulder or ledge reads the geology alone");
			string placer = System.IO.File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain/CliffPlacer.cs");
			Assert.That(placer, Does.Contain("CliffRocks.Accepts(spec[b], bedrock)"), "a cliff takes the geology's rock only where its biome accepts it");
		}
	}
}
