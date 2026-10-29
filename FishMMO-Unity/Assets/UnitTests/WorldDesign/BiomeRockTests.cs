using System;
using System.Linq;
using NUnit.Framework;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Which rock a biome's cliffs, river boulders and fall ledges are made of: the planet geology's where the biome
	/// accepts it, else the biome's own. Geology alone walled Flo Monolith's bog and taiga in sandstone and
	/// Baoakraal's karst and grassland in conglomerate (2026-10-08).
	/// </summary>
	[TestFixture]
	public class BiomeRockTests
	{
		/// <summary>Green, wet or cold biomes: never walled in sandstone, whatever the geology under them.</summary>
		private static readonly string[] Green =
		{
			// Forest left the list in its round (approved 2026-10-16): its own rock is a mossy grey-buff sandstone. Plains left it in its
			// round (approved 2026-10-18): it accepts the Dakota sandstone rimrock over its red beds.
			"Grassland", "Farmland", "Forest Ruins", "Woodland", "Hills", "Taiga", "Tundra", "Peat Bog",
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
				// Estuary accepts the red Heligoland sandstone and Mangrove the Magarini coast sands (their rounds, approved 2026-10-18/25).
				if (name == "Estuary" || name == "Mangrove")
				{
					Assert.That(CliffRocks.RockFor(entry, "Sandstone"), Is.EqualTo("Sandstone"), $"{name} accepts its sandstone");
				}
				else
				{
					Assert.That(CliffRocks.RockFor(entry, "Sandstone"), Is.Not.EqualTo("Sandstone"), $"{name} on a sandstone province");
				}
				// Woodland accepts the Hertfordshire puddingstone (its round, approved 2026-10-16), Alpine Meadow the Bucegi
				// conglomerate of its crones and stone sheep (its round, approved 2026-10-26).
				if (CliffRocks.OwnRock(entry) != "Conglomerate" && name != "Woodland" && name != "Alpine Meadow")
				{
					Assert.That(CliffRocks.RockFor(entry, "Conglomerate"), Is.Not.EqualTo("Conglomerate"), $"{name} on a conglomerate province");
				}
			}
		}

		[Test]
		public void AcceptedGeologyStillVariesTheRock()
		{
			// The Grassland round (2026-10-26): loess country, accepting the Sarmatian limestone.
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Grassland"), "Limestone"), Is.EqualTo("Limestone"), "a grassland on limestone stands in limestone");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Grassland"), "Sandstone"), Is.EqualTo("Loess"), "and on sandstone in its own loess");
			// The Desert round (2026-10-26): Nubian sandstone country, accepting the basement granite and dropping the limestone.
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Desert"), "Granite"), Is.EqualTo("Granite"), "a desert on granite stands in granite");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Desert"), "Limestone"), Is.EqualTo("Sandstone"), "and on limestone in its own sandstone");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Desert"), "Gneiss"), Is.EqualTo("Sandstone"), "and on gneiss in its own sandstone");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Karst"), "Granite"), Is.EqualTo("Limestone"), "karst is limestone country");
			// The Glacier's round gave it greenstone cliffs (2026-10-26); Permanent Ice is still ice-walled.
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Permanent Ice"), "Granite"), Is.EqualTo(CliffRocks.Ice), "ice stays ice");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Glacier"), "Granite"), Is.EqualTo("Granite"), "a glacier on granite stands in the granite it accepts");
			Assert.That(CliffRocks.RockFor(BiomeArtSpec.For("Glacier"), "Sandstone"), Is.EqualTo("Greenstone"), "and elsewhere in its own greenstone");
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
		public void ABiomeIsNeverWalledInAFormationOnlyRock()
		{
			// A biome whose formation rules name halite polygons or sinter cones most still takes its cliffs, river
			// boulders and ledges from the crust's rocks (or ice): BedrockOf counts only cliff rocks.
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				string own = CliffRocks.OwnRock(entry);
				Assert.That(own == CliffRocks.Ice || CliffRocks.IsCliffRock(own), Is.True, $"{entry.Biome}: its own rock {own}");
				string bedrock = CliffRocks.BedrockOf(entry);
				Assert.That(bedrock == null || CliffRocks.IsCliffRock(bedrock), Is.True, $"{entry.Biome}: bedrock {bedrock}");
			}
		}

		/// <summary>The vegetation design's §2h list: every mineral, ice and alien formation the lifeless and frozen biomes take.</summary>
		private static readonly (string Type, string Shape)[] Formations2h =
		{
			("Halite", "Polygons"), ("Halite", "Pinnacles"), ("Halite", "Terrace"),
			("Gypsum", "Crystal"),
			("Sulphur", "Chimney"), ("Sulphur", "Crystals"), ("Sulphur", "Cone"),
			("Sinter", "Cone"), ("Sinter", "Terrace"),
			("Scoria", "Cone"), ("Scoria", "Bomb"), ("Scoria", "Lump"),
			("Breccia", "Block"), ("Breccia", "Boulder"), ("Breccia", "CraterRim"),
			("Anorthosite", "Boulder"),
			("Sulphide", "Smoker"),
			("Chalk", "Pinnacle"),
			("Pyrite", "Cubes"),
			("DarkLag", "FrostKnob"),
			("Termitaria", "Cathedral"),
			("Limestone", "Stalagmite"), ("Limestone", "Flowstone"), ("Limestone", "Gours"),
			("Basalt", "Ledges"), ("Basalt", "Scree"), ("Basalt", "Slabs"), ("Basalt", "Fin"), ("Basalt", "Dribble"), ("Basalt", "Ventifact"),
			("Sandstone", "Concretion"),
			("Firn", "Sastrugi"), ("Firn", "Penitentes"), ("Firn", "PitRim"),
			("WaterIce", "GlacierTable"), ("WaterIce", "DirtCone"), ("WaterIce", "Stalagmite"), ("WaterIce", "Crystal"), ("WaterIce", "FrostFlowers"),
			("WaterIce", "Cone"), ("WaterIce", "Lobe"), ("WaterIce", "Ridge"),
		};

		[Test]
		public void EveryMineralIceAndAlienFormationIsMade_AndWearsAMaterialThatIsWritten()
		{
			var made = new System.Collections.Generic.HashSet<string>(RockArtNames.AllFormations().Select(f => RockArtNames.FormationPrefab(f.Type.Name, f.Shape.Name, f.Variant)));
			var materials = new System.Collections.Generic.HashSet<string>(ProceduralArtCatalogue.RockMaterialNames());
			foreach (RockMaterialSpec legacy in ProceduralArtCatalogue.RockMaterials)
			{
				materials.Add(ProceduralArtCatalogue.RockMaterial(legacy.Name));
			}
			foreach ((string typeName, string shapeName) in Formations2h)
			{
				Assert.That(RockTypes.TryGet(typeName, out RockType type), Is.True, typeName);
				Assert.That(RockTypes.TryShape(in type, shapeName, out FormationShape shape), Is.True, $"{typeName}/{shapeName}");
				foreach (string prefab in RockArtNames.Formations(in type, shapeName))
				{
					Assert.That(made, Does.Contain(prefab));
				}
				foreach (string material in RockArtNames.FormationMaterials(in type, in shape))
				{
					Assert.That(material, Is.Not.Null, $"{typeName}/{shapeName}");
					Assert.That(materials, Does.Contain(material), $"{typeName}/{shapeName}");
				}
			}
			// Two bodies where the formation is two materials; colliderless ground cover; ice cobbles of water ice.
			Assert.That(RockArtNames.FormationMaterials(RockTypes.WaterIce, Shape(RockTypes.WaterIce, "GlacierTable")), Has.Length.EqualTo(2));
			Assert.That(RockArtNames.FormationMaterials(RockTypes.DarkLag, Shape(RockTypes.DarkLag, "FrostKnob")), Has.Length.EqualTo(2));
			Assert.That(Shape(RockTypes.Firn, "Sastrugi").Colliderless && Shape(RockTypes.Halite, "Polygons").Colliderless && Shape(RockTypes.WaterIce, "FrostFlowers").Colliderless, Is.True);
			Assert.That(RockArtNames.RockSurfaceTypeForLegacy("Ice"), Is.EqualTo("WaterIce"));
			foreach (RockType t in RockTypes.FormationRocks)
			{
				Assert.That(new[] { "Ice", "Nodule", "Coral" }, Does.Not.Contain(t.Name), "Rock_Ice, Rock_Nodule and Rock_Coral belong to small-rock materials");
			}
		}

		private static FormationShape Shape(RockType type, string name)
		{
			Assert.That(RockTypes.TryShape(in type, name, out FormationShape shape), Is.True, name);
			return shape;
		}

		[Test]
		public void BouldersAndLedgesAskTheSameRuleAsTheCliffs()
		{
			string generator = System.IO.File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs");
			Assert.That(generator, Does.Contain("Func<float, float, float, string> rockAt = BiomeRockTypes(field, cliffOptions.RockTypeAt);"));
			Assert.That(generator, Does.Not.Contain("cliffOptions.RockTypeAt, options.Seed"), "no boulder or ledge reads the geology alone");
			string placer = System.IO.File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain/CliffPlacer.cs");
			Assert.That(placer, Does.Contain("CliffRocks.Accepts(ownRock[b], accepted[b], bedrock)"), "a cliff takes the geology's rock only where its biome accepts it");
			Assert.That(placer, Does.Contain("ownRock[b] = BiomeTemplateArt.OwnRock(entry.Biome);"), "the biome's own rock is its template's");
			Assert.That(placer, Does.Contain("accepted[b] = BiomeTemplateArt.AcceptedRocks(entry.Biome);"), "and so are the rocks it accepts");
			Assert.That(placer, Does.Contain("BiomeTemplateArt.CliffRock(entry, out string family)"), "each cliff layer's rock is its template layer's");
			Assert.That(generator, Does.Contain("rules[b] = BiomeTemplateArt.RockRule(field.Biomes[b]);"), "boulders and ledges ask the template's rule");
		}

		/// <summary>
		/// The rule a template's values feed (its own rock and the rocks it accepts, as the authoring writes them from the
		/// spec) gives the same rock as the spec entry's rule did, for every biome on every planet lithology and on none:
		/// moving the decision onto the template changed no cliff, boulder, ledge or site stone.
		/// </summary>
		[Test]
		public void TheTemplatesRockRule_GivesWhatTheSpecsDid()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				string own = CliffRocks.OwnRock(entry);
				var accepted = new System.Collections.Generic.List<string>(entry.Rocks);
				Assert.That(CliffRocks.RockFor(own, accepted, null), Is.EqualTo(CliffRocks.RockFor(entry, null)), entry.Biome);
				foreach (Lithology lithology in PlanetGeology.Lithologies)
				{
					string geology = lithology.RockTypeName;
					Assert.That(CliffRocks.RockFor(own, accepted, geology), Is.EqualTo(CliffRocks.RockFor(entry, geology)), $"{entry.Biome} on {lithology.Name}");
					Assert.That(CliffRocks.Accepts(own, accepted, geology), Is.EqualTo(CliffRocks.Accepts(entry, geology)), $"{entry.Biome} accepts {geology}");
				}
			}
			// No biome at all: granite, whatever the geology but granite itself.
			Assert.That(CliffRocks.RockFor(CliffRocks.OwnRock((BiomeArtSpec.Entry)null), System.Array.Empty<string>(), "Basalt"), Is.EqualTo("Granite"));
		}

		/// <summary>
		/// A cliff layer's authored rock type is what the scene cut reads, and None keeps the face plain terrain; the rock
		/// it is authored with is the one the placer computed from the spec's family before (CliffRocks.RockTypeFor).
		/// </summary>
		[Test]
		public void ACliffLayersRockType_IsWhatTheCutStandsOnIt()
		{
			var basalt = new SceneTerrainPalette.Entry { Slot = SceneTerrainPalette.SlotCliff + "0", Source = new CliffTextureLayer { rockType = "Basalt" } };
			Assert.That(BiomeTemplateArt.CliffRock(basalt, out _), Is.EqualTo("Basalt"));
			var soil = new SceneTerrainPalette.Entry { Slot = SceneTerrainPalette.SlotCliff + "0", Source = new CliffTextureLayer { rockType = CliffTextureLayer.NoRock } };
			Assert.That(BiomeTemplateArt.CliffRock(soil, out _), Is.Null, "None stays terrain");
			int rockFaces = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach (BiomeArtSpec.Cliff cliff in entry.Cliffs)
				{
					string rock = CliffRocks.RockTypeFor(cliff.Family, entry);
					Assert.That(rock == null || rock == CliffRocks.Ice || CliffRocks.IsCliffRock(rock), Is.True, $"{entry.Biome} {cliff.Family}: {rock}");
					rockFaces += rock != null ? 1 : 0;
				}
			}
			Assert.That(rockFaces, Is.GreaterThan(40), "most biomes' cliffs stand in rock");
		}
	}
}
