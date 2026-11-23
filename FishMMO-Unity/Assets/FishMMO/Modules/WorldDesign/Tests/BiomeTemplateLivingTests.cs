using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The ambient creatures' country, the sea life's favourite waters, the vines,
	/// the gathering resources and a herb grove's herbs moved off the biome's NAME onto the biome template. Every spec column
	/// the authoring copies onto the templates is what the name gave before, for every biome asset, so authoring changes
	/// nothing; the code that places them reads the template and names a biome only in its logged fallback. With it: the
	/// authoring prunes the grass entries it wrote for blades a biome no longer grows, and nothing else. And I4b:
	/// the Alien bit is read live from the template's air, the sea life carries a weight per biome, and a point of interest
	/// paints its own biome's prototype of a plant.
	/// </summary>
	public class BiomeTemplateLivingTests
	{
		private const string BiomeFolder = "Assets/FishMMO/Modules/WorldSimulation/Templates/Biomes";
		private const string WorldFolder = "Assets/FishMMO/Modules/WorldDesign/Editor";

		/// <summary>
		/// The live Alien bit is the template's air: ground whose atmosphere leaves out Standard, or that asks a world for a
		/// methane cycle, cryogenic air or frozen nitrogen, is alien ground unless it is lifeless; an Alien bit stored on a
		/// template is never trusted.
		/// </summary>
		[Test]
		public void LiveAlien_IsTheTemplatesAir()
		{
			const BiomeAtmosphereRequirement airless = BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin;
			Assert.That(BiomeHabitats.Live(BiomeHabitat.None, airless, BiomeWorldRequirement.None), Is.EqualTo(BiomeHabitat.Alien));
			Assert.That(BiomeHabitats.Live(BiomeHabitat.Ice, airless, BiomeWorldRequirement.Cryovolcanic), Is.EqualTo(BiomeHabitat.Ice | BiomeHabitat.Alien));
			Assert.That(BiomeHabitats.Live(BiomeHabitat.Barren, airless, BiomeWorldRequirement.None), Is.EqualTo(BiomeHabitat.Barren), "lifeless ground is not alien");
			Assert.That(BiomeHabitats.Live(BiomeHabitat.Forest, BiomeAtmosphereRequirement.Living, BiomeWorldRequirement.None), Is.EqualTo(BiomeHabitat.Forest));
			Assert.That(BiomeHabitats.Live(BiomeHabitat.Mountain, BiomeAtmosphereRequirement.Any, BiomeWorldRequirement.None), Is.EqualTo(BiomeHabitat.Mountain), "any air: the home world's too");
			Assert.That(BiomeHabitats.Live(BiomeHabitat.Mountain, BiomeAtmosphereRequirement.None, BiomeWorldRequirement.None), Is.EqualTo(BiomeHabitat.Mountain), "no requirement asks nothing");
			Assert.That(BiomeHabitats.Live(BiomeHabitat.Wetland, BiomeAtmosphereRequirement.Thick, BiomeWorldRequirement.None), Is.EqualTo(BiomeHabitat.Wetland | BiomeHabitat.Alien));
			const BiomeAtmosphereRequirement breathing = BiomeAtmosphereRequirement.Breathing;
			foreach (BiomeWorldRequirement other in new[] { BiomeWorldRequirement.MethaneCycle, BiomeWorldRequirement.CryogenicAir, BiomeWorldRequirement.FrozenNitrogen })
			{
				Assert.That(BiomeHabitats.Live(BiomeHabitat.None, breathing, other | BiomeWorldRequirement.RockSurface), Is.EqualTo(BiomeHabitat.Alien), other.ToString());
			}
			foreach (BiomeWorldRequirement home in new[] { BiomeWorldRequirement.IceWorld, BiomeWorldRequirement.Volcanic, BiomeWorldRequirement.SurfaceWater, BiomeWorldRequirement.RunawayGreenhouse })
			{
				Assert.That(BiomeHabitats.Live(BiomeHabitat.Ice, BiomeAtmosphereRequirement.Any, home), Is.EqualTo(BiomeHabitat.Ice), home.ToString());
			}
			// A stored bit is masked whatever the air says.
			Assert.That(BiomeHabitats.Live(BiomeHabitat.Forest | BiomeHabitat.Alien, BiomeAtmosphereRequirement.Living, BiomeWorldRequirement.None), Is.EqualTo(BiomeHabitat.Forest));
			Assert.That(BiomeHabitat.Alien & BiomeHabitats.Stored, Is.EqualTo(BiomeHabitat.None));
			Assert.That(BiomeHabitat.Forest & BiomeHabitats.Stored, Is.EqualTo(BiomeHabitat.Forest));
		}

		/// <summary>
		/// The marks the authoring wrote with the Alien bit read without it, so a habitat the tool wrote and nobody touched is
		/// still the tool's (and one edited by hand still a hand edit); no other mark changes.
		/// </summary>
		[Test]
		public void HabitatMarks_WrittenWithTheAlienBit_ReadWithoutIt()
		{
			Assert.That(BiomeArtAuthoring.HabitatMarkWithoutAlien("Ice, Alien"), Is.EqualTo(BiomeArtAuthoring.Mark(BiomeHabitat.Ice)));
			Assert.That(BiomeArtAuthoring.HabitatMarkWithoutAlien(BiomeArtAuthoring.Mark(BiomeHabitat.Alien)), Is.EqualTo(string.Empty), "alien ground alone: None");
			Assert.That(BiomeArtAuthoring.HabitatMarkWithoutAlien("Forest, Woodland"), Is.EqualTo("Forest, Woodland"));
			Assert.That(BiomeArtAuthoring.HabitatMarkWithoutAlien(string.Empty), Is.EqualTo(string.Empty));
			Assert.That(BiomeArtAuthoring.HabitatMarkWithoutAlien(null), Is.Null, "never authored stays never authored");
			Assert.That(BiomeArtAuthoring.HabitatMarkWithoutAlien("0123abcd"), Is.EqualTo("0123abcd"), "not a habitat: untouched");
		}

		/// <summary>No biome's spec file names the Alien bit (it is the template's air, read live); the spec column drops it too.</summary>
		[Test]
		public void SpecFiles_NeverNameTheAlienHabitat()
		{
			string folder = Path.Combine(WorldFolder, "ProceduralArt", "BiomeArtSpec");
			string[] files = Directory.GetFiles(ProjectFiles.Folder(folder), "*.cs");
			Assert.That(files.Length, Is.EqualTo(83));
			foreach (string file in files)
			{
				Assert.That(CodeOnly(ProjectFiles.ReadAllText(file)), Does.Not.Contain("BiomeHabitat.Alien"), Path.GetFileName(file));
			}
		}

		// ── Vines ─────────────────────────────────────────────────────

		/// <summary>
		/// Every biome's spec vines are its vine-table row (VineBiomes, by its name), and the form the vine rules read back off
		/// a template holding them is that row, value for value; a biome the table never named grows none (null, as before).
		/// </summary>
		[Test]
		public void SpecVines_AreTheVineTablesRow_ForEveryBiomeAsset()
		{
			int rows = 0, authored = 0;
			foreach (BiomeAsset asset in BiomeAssets())
			{
				VineBiomes.Habitat old = VineBiomes.For(asset.Name);
				if (BiomeAuthoredRounds.Contains(asset.Name))
				{
					// An authored round gives its own vines (legacy equality only): the table's row is only the unauthored fallback.
					authored += old != null ? 1 : 0;
					continue;
				}
				BiomeVines spec = BiomeArtSpec.For(asset.Name).VineGrowth();
				VineBiomes.Habitat read = BiomeTemplateArt.HabitatOf(asset.Name, spec);
				if (old == null)
				{
					Assert.That(spec.IsEmpty, Is.True, asset.Name);
					Assert.That(read, Is.Null, asset.Name);
					continue;
				}
				rows++;
				Assert.That(read, Is.Not.Null, asset.Name);
				Assert.That(read.Biome, Is.EqualTo(old.Biome));
				Assert.That(new[] { read.Caves, read.Ruins, read.Cliffs, read.Trees }, Is.EqualTo(new[] { old.Caves, old.Ruins, old.Cliffs, old.Trees }), asset.Name);
				Assert.That(read.Species, Is.EqualTo(old.Species), asset.Name);
				for (int i = 0; i <= 20; i++)
				{
					Assert.That(read.Pick(i / 20f), Is.EqualTo(old.Pick(i / 20f)), $"{asset.Name} draw {i}");
				}
			}
			Assert.That(rows + authored, Is.EqualTo(VineBiomes.All.Length), "every row of the table is a biome asset");
		}

		/// <summary>A template's vines with no species read as none, whatever its densities; the spec's own vines replace the row.</summary>
		[Test]
		public void TemplateVines_WithNoSpecies_GrowNone()
		{
			Assert.That(BiomeTemplateArt.HabitatOf("X", new BiomeVines { Caves = 1f, Ruins = 1f }), Is.Null);
			Assert.That(BiomeTemplateArt.HabitatOf("X", null), Is.Null);
			var one = new BiomeVines { Cliffs = 0.5f };
			one.Species.Add(new BiomeVineSpecies("Ivy", 1f));
			Assert.That(BiomeTemplateArt.HabitatOf("X", one).Pick(0.3f), Is.EqualTo("Ivy"));
			Assert.That(one.Clone().Signature(), Is.EqualTo(one.Signature()));
			Assert.That(new BiomeVines().IsEmpty, Is.True);
			Assert.That(one.IsEmpty, Is.False);
		}

		// ── Gathering and a herb grove's herbs ────────────────────────

		/// <summary>
		/// Every biome's spec gathering resources are the catalogue rows that named it (or named none), in the catalogue's order,
		/// and a site drawing from that offer draws exactly what it drew by the name, for every family and many seeds.
		/// </summary>
		[Test]
		public void SpecGathering_IsTheCataloguesRows_AndDrawsAsTheNameDid()
		{
			// An authored round gives its own gathering (legacy equality only; AuthoredFiles_NameOnlyGroundsAndPrefabsThatExist checks its nodes).
			foreach (BiomeAsset asset in BiomeAssets().Where(a => !BiomeAuthoredRounds.Contains(a.Name)))
			{
				var old = new List<string>();
				foreach (GatheringResource resource in GatheringResourceCatalogue.All)
				{
					// GatheringResourceCatalogue.GrowsIn formerly, copied literally.
					if (resource.Biomes.Length == 0 || resource.Biomes.Any(b => string.Equals(b, asset.Name, StringComparison.OrdinalIgnoreCase)))
					{
						old.Add(resource.Node);
					}
				}
				List<string> offered = BiomeArtSpec.For(asset.Name).GatheringResources();
				Assert.That(offered, Is.EqualTo(old), asset.Name);
				foreach (GatheringFamily family in (GatheringFamily[])Enum.GetValues(typeof(GatheringFamily)))
				{
					for (int seed = 1; seed <= 12; seed++)
					{
						GatheringResource byName = GatheringResourceCatalogue.Pick(family, asset.Name, new DeterministicRNG(seed));
						GatheringResource byOffer = GatheringResourceCatalogue.Pick(family, (IReadOnlyCollection<string>)offered, new DeterministicRNG(seed));
						Assert.That(byOffer, Is.SameAs(byName), $"{asset.Name} {family} seed {seed}");
					}
				}
			}
			// No biome: the whole family, as a null name drew it.
			for (int seed = 1; seed <= 12; seed++)
			{
				Assert.That(GatheringResourceCatalogue.Pick(GatheringFamily.Ore, (IReadOnlyCollection<string>)new string[0], new DeterministicRNG(seed)),
					Is.SameAs(GatheringResourceCatalogue.Pick(GatheringFamily.Ore, (string)null, new DeterministicRNG(seed))));
			}
		}

		/// <summary>PointOfInterestHerbGrove.HerbsFor formerly, copied literally: the oracle for the spec's grove herbs.</summary>
		private static List<string> OldHerbsFor(string biome)
		{
			var herbs = new List<string>();
			foreach (GatheringResource resource in GatheringResourceCatalogue.All)
			{
				if ((resource.Family == GatheringFamily.Herb || resource.Family == GatheringFamily.Fungus)
					&& resource.Suits(biome)
					&& !string.IsNullOrEmpty(resource.Visual)
					&& resource.Visual.StartsWith("Detail_", StringComparison.Ordinal)
					&& !herbs.Contains(resource.Visual))
				{
					herbs.Add(resource.Visual);
				}
			}
			if (herbs.Count == 0)
			{
				herbs.AddRange(new[] { "Detail_BroadHerb", "Detail_HerbRuderal" });
			}
			return herbs;
		}

		/// <summary>Every biome's spec herb-grove herbs are what the grove grew there by the biome's name.</summary>
		[Test]
		public void SpecGroveHerbs_AreWhatTheGroveGrewByName_ForEveryBiomeAsset()
		{
			foreach (BiomeAsset asset in BiomeAssets().Where(a => !BiomeAuthoredRounds.Contains(a.Name)))
			{
				Assert.That(BiomeArtSpec.For(asset.Name).GroveHerbs(), Is.EqualTo(OldHerbsFor(asset.Name)), asset.Name);
			}
			Assert.That(BiomeArtSpec.HerbsOf(new string[0]), Is.EqualTo(OldHerbsFor(null)), "no biome: common herbs");
		}

		// ── Grass entries the authoring wrote ────────────────────

		/// <summary>
		/// The authoring prunes an entry only when the biome no longer grows its prefab and the entry still holds the mark the
		/// tool wrote; a hand-made one (no mark), a hand-edited one (mark differs), one with no prefab and a duplicate are kept.
		/// </summary>
		[Test]
		public void GrassPruning_RemovesOnlyTheToolsUntouchedEntriesForBladesNoLongerGrown()
		{
			var marks = new Dictionary<string, string>
			{
				["Detail_GrassLush"] = "m-lush",
				["Detail_GrassDry"] = "m-dry",
				["Detail_GrassTall"] = "m-tall-old",
				["Detail_Reeds"] = "m-reeds",
			};
			var entries = new List<(string Prefab, string Mark)>
			{
				("Detail_GrassLush", "m-lush"),     // 0: still grown: kept
				("Detail_GrassDry", "m-dry"),       // 1: not grown, the tool's, untouched: pruned
				("Detail_GrassTall", "m-tall-new"), // 2: not grown, edited by hand since: kept
				("Detail_FlowersMeadow", "m-x"),    // 3: not grown, never authored (hand-made): kept
				(null, "m-none"),                   // 4: no prefab: kept
				("Detail_Reeds", "m-reeds"),        // 5: not grown, the tool's: pruned
				("Detail_Reeds", "m-reeds"),        // 6: a second entry for the same prefab: kept (only the first is the tool's)
			};
			var grown = new HashSet<string> { "Detail_GrassLush" };
			List<int> prune = BiomeArtAuthoring.PrunableGrassTypes(entries, p => marks.TryGetValue(p, out string m) ? m : null, grown);
			Assert.That(prune, Is.EqualTo(new[] { 1, 5 }));
			Assert.That(BiomeArtAuthoring.PrunableGrassTypes(entries, p => marks.TryGetValue(p, out string m) ? m : null,
				new HashSet<string> { "Detail_GrassLush", "Detail_GrassDry", "Detail_Reeds" }), Is.Empty, "everything still grown");
			Assert.That(BiomeArtAuthoring.PrunableGrassTypes(new List<(string, string)>(), p => null, grown), Is.Empty);
		}

		// ── A point of interest's own prototypes ─────────────────

		/// <summary>
		/// A site reuses a tile's prototype of a plant only when it is recorded for the site's own biome (a rule's or an earlier
		/// site's); another biome's, or an unrecorded one, gets a prototype of its own (the first of the plant is what it copies
		/// and used to paint into); a site with no biome takes the first, as before.
		/// </summary>
		[Test]
		public void SiteLayer_IsTheSitesOwnBiomesPrototype()
		{
			const int meadow = 11, forest = 22, bog = 33;
			string[] prefabs = { "Detail_Fern", "Detail_FlowersMeadow", "Detail_GrassLush", "Detail_FlowersMeadow", null, "Detail_Unrecorded" };
			int[] seeds = { 5, 7, 9, 13, 0, 21 };
			var entries = new List<TerrainDetailBiomes.Entry>
			{
				new TerrainDetailBiomes.Entry { Prefab = "Detail_Fern", NoiseSeed = 5, BiomeID = forest },
				new TerrainDetailBiomes.Entry { Prefab = "Detail_FlowersMeadow", NoiseSeed = 7, BiomeID = meadow },
				new TerrainDetailBiomes.Entry { Prefab = "Detail_GrassLush", NoiseSeed = 9, BiomeID = meadow },
				new TerrainDetailBiomes.Entry { Prefab = "Detail_FlowersMeadow", NoiseSeed = 13, BiomeID = forest, Site = true },
			};
			Assert.That(TerrainDetailBiomes.SiteLayer("Detail_FlowersMeadow", meadow, prefabs, seeds, entries, out int first), Is.EqualTo(1), "the meadow's own rule");
			Assert.That(first, Is.EqualTo(1));
			Assert.That(TerrainDetailBiomes.SiteLayer("Detail_FlowersMeadow", forest, prefabs, seeds, entries, out first), Is.EqualTo(3), "an earlier forest site's, not the meadow's first");
			Assert.That(first, Is.EqualTo(1));
			Assert.That(TerrainDetailBiomes.SiteLayer("Detail_FlowersMeadow", bog, prefabs, seeds, entries, out first), Is.EqualTo(-1), "another biome's only: its own");
			Assert.That(first, Is.EqualTo(1), "copies the first");
			Assert.That(TerrainDetailBiomes.SiteLayer("Detail_Fern", meadow, prefabs, seeds, entries, out first), Is.EqualTo(-1));
			Assert.That(first, Is.EqualTo(0));
			Assert.That(TerrainDetailBiomes.SiteLayer("Detail_Unrecorded", meadow, prefabs, seeds, entries, out first), Is.EqualTo(-1), "unrecorded: not the site's biome");
			Assert.That(first, Is.EqualTo(5));
			Assert.That(TerrainDetailBiomes.SiteLayer("Detail_Moss", meadow, prefabs, seeds, entries, out first), Is.EqualTo(-1), "the tile grows none");
			Assert.That(first, Is.EqualTo(-1));
			Assert.That(TerrainDetailBiomes.SiteLayer("Detail_FlowersMeadow", 0, prefabs, seeds, entries, out first), Is.EqualTo(1), "no biome: the first, as before");
			Assert.That(TerrainDetailBiomes.SiteLayer("Detail_FlowersMeadow", bog, prefabs, seeds, null, out first), Is.EqualTo(-1), "no record: none is the bog's");
			Assert.That(TerrainDetailBiomes.SiteLayer(null, bog, prefabs, seeds, entries, out first), Is.EqualTo(-1));
		}

		/// <summary>
		/// An added prototype keeps the seed it copied unless another prototype of the plant (or the record) has it; then a
		/// seed of its own, the same every run, different per biome, so every prototype of a plant is told apart by its seed.
		/// </summary>
		[Test]
		public void SiteSeed_TellsEveryPrototypeOfAPlantApart()
		{
			string[] prefabs = { "Detail_Fern", "Detail_FlowersMeadow" };
			int[] seeds = { 5, 7 };
			var entries = new List<TerrainDetailBiomes.Entry> { new TerrainDetailBiomes.Entry { Prefab = "Detail_FlowersMeadow", NoiseSeed = 40, BiomeID = 3, Site = true } };
			Assert.That(TerrainDetailBiomes.SiteSeed(5, 9, "Detail_Moss", prefabs, seeds, entries), Is.EqualTo(5), "another plant's seed is no clash: kept, as before");
			int own = TerrainDetailBiomes.SiteSeed(7, 9, "Detail_FlowersMeadow", prefabs, seeds, entries);
			Assert.That(own, Is.Not.EqualTo(7));
			Assert.That(own, Is.GreaterThanOrEqualTo(0));
			Assert.That(TerrainDetailBiomes.SiteSeed(7, 9, "Detail_FlowersMeadow", prefabs, seeds, entries), Is.EqualTo(own), "deterministic");
			Assert.That(TerrainDetailBiomes.SiteSeed(7, 10, "Detail_FlowersMeadow", prefabs, seeds, entries), Is.Not.EqualTo(own), "per biome");
			Assert.That(TerrainDetailBiomes.SiteSeed(40, 9, "Detail_FlowersMeadow", prefabs, seeds, entries), Is.Not.EqualTo(40), "a seed the record holds for the plant");

			// Three sites of three biomes over one tile whose meadow rule grows the flowers: each its own prototype, recorded
			// apart, and the meadow's site reuses the rule's.
			var tilePrefabs = new List<string> { "Detail_FlowersMeadow" };
			var tileSeeds = new List<int> { 7 };
			var record = new List<TerrainDetailBiomes.Entry> { new TerrainDetailBiomes.Entry { Prefab = "Detail_FlowersMeadow", NoiseSeed = 7, BiomeID = 1 } };
			int Paint(int biome)
			{
				int layer = TerrainDetailBiomes.SiteLayer("Detail_FlowersMeadow", biome, tilePrefabs, tileSeeds, record, out int firstOf);
				if (layer >= 0)
				{
					return layer;
				}
				int seed = TerrainDetailBiomes.SiteSeed(tileSeeds[firstOf], biome, "Detail_FlowersMeadow", tilePrefabs, tileSeeds, record);
				tilePrefabs.Add("Detail_FlowersMeadow");
				tileSeeds.Add(seed);
				record.Add(new TerrainDetailBiomes.Entry { Prefab = "Detail_FlowersMeadow", NoiseSeed = seed, BiomeID = biome, Site = true });
				return tilePrefabs.Count - 1;
			}
			Assert.That(Paint(1), Is.EqualTo(0), "the meadow's site: the meadow's rule");
			Assert.That(Paint(2), Is.EqualTo(1));
			Assert.That(Paint(3), Is.EqualTo(2));
			Assert.That(Paint(2), Is.EqualTo(1), "the second forest site: the first forest site's");
			Assert.That(tileSeeds.Distinct().Count(), Is.EqualTo(3), "every prototype of the plant has its own seed");
			for (int i = 0; i < tilePrefabs.Count; i++)
			{
				Assert.That(TerrainDetailBiomes.TryGet(record, tilePrefabs[i], tileSeeds[i], out TerrainDetailBiomes.Entry entry), Is.True);
				Assert.That(entry.BiomeID, Is.EqualTo(i + 1));
			}
		}

		/// <summary>
		/// A site's growth in its own prototype leaves the plant exactly as thick as painting the shared prototype did: only
		/// ever more, measured against the prototype it used to paint into together with its own.
		/// </summary>
		[Test]
		public void SiteCover_LeavesThePlantAsThickAsOnePrototypeDid()
		{
			for (int baseCover = 0; baseCover <= 255; baseCover += 15)
			{
				for (int want = 0; want <= 255; want += 17)
				{
					// The tile's first prototype was another biome's: the old single layer would hold max(base, want).
					int own = TerrainDetailBiomes.SiteCover(baseCover, 0, false, want);
					Assert.That(baseCover + own, Is.EqualTo(Math.Max(baseCover, want)), $"base {baseCover} want {want}");
					// A second site over it: max of everything wanted, as one layer.
					int again = TerrainDetailBiomes.SiteCover(baseCover, own, false, want / 2 + 40);
					Assert.That(baseCover + again, Is.EqualTo(Math.Max(Math.Max(baseCover, want), want / 2 + 40)));
					// Its own is the first (shared): raised as before.
					Assert.That(TerrainDetailBiomes.SiteCover(0, baseCover, true, want), Is.EqualTo(Math.Max(baseCover, want)));
				}
			}
		}

		/// <summary>
		/// Herb groves, cave mouths and fields paint through SiteLayers (their own biome's prototype), never the tile's first
		/// prototype of the plant; and LayerOf/RecordBiome (the first-wins pair) are gone.
		/// </summary>
		[Test]
		public void Sites_PaintTheirOwnBiomesPrototype()
		{
			string features = Path.Combine(WorldFolder, "PointsOfInterest", "Features");
			string grove = CodeOnly(ProjectFiles.ReadAllText(Path.Combine(features, "PointOfInterestHerbGrove.cs")));
			string mouths = CodeOnly(ProjectFiles.ReadAllText(Path.Combine(features, "PointOfInterestCaveMouths.cs")));
			string fields = CodeOnly(ProjectFiles.ReadAllText(Path.Combine(features, "PointOfInterestFields.cs")));
			Assert.That(grove, Does.Contain("layers[s] = LayersOf(terrain, Species[s], Biome);"));
			Assert.That(mouths, Does.Contain("PointOfInterestHerbGrove.LayersOf(terrain, growth.Species[s], growth.Biome)"));
			Assert.That(fields, Does.Contain("PointOfInterestHerbGrove.SiteLayers(terrain, crop, biome, NewWheat)"));
			Assert.That(grove + mouths, Does.Contain("TerrainDetailBiomes.SiteCover("));
			foreach (string source in new[] { grove, mouths, fields })
			{
				Assert.That(Regex.IsMatch(source, @"\bRecordBiome\(|\bLayerOf\("), Is.False);
			}
		}

		// ── The consumers read the template ───────────────────────────

		/// <summary>
		/// The cut's vine, gathering and herb code reads the template: the name tables (VineBiomes.For / .All, a resource's
		/// Suits / GrowsIn, the grove's old HerbsFor) are read only by BiomeTemplateArt's logged fallbacks and the catalogue itself.
		/// </summary>
		[Test]
		public void CutTimeCode_ReadsVinesGatheringAndHerbsFromTheTemplate()
		{
			var byName = new Regex(@"VineBiomes\.(For|All)\b|\.Suits\(|\bGrowsIn\(|\bAnyGrowsIn\(|\bHerbsFor\(|CommonHerbs\b", RegexOptions.CultureInvariant);
			foreach (string old in new[]
			{
				"VineBiomes.Habitat habitat = VineBiomes.For(biome != null ? biome.name : null);",
				"foreach (VineBiomes.Habitat h in VineBiomes.All)",
				"&& resource.Suits(biome)",
				"List<GameObject> herbs = Load(HerbsFor(biome));",
				"herbs.AddRange(CommonHerbs);",
			})
			{
				Assert.That(byName.IsMatch(old), Is.True, "control: " + old);
			}
			var files = new List<string> { Path.Combine(WorldFolder, "SceneGenerator.cs") };
			files.AddRange(Directory.GetFiles(ProjectFiles.Folder(Path.Combine(WorldFolder, "Terrain")), "*.cs", SearchOption.AllDirectories));
			files.AddRange(Directory.GetFiles(ProjectFiles.Folder(Path.Combine(WorldFolder, "PointsOfInterest")), "*.cs", SearchOption.AllDirectories));
			files.AddRange(Directory.GetFiles(ProjectFiles.Folder(Path.Combine(WorldFolder, "Vines")), "*.cs", SearchOption.AllDirectories));
			Assert.That(files.Count, Is.GreaterThan(50));
			var found = new List<string>();
			foreach (string file in files)
			{
				string name = Path.GetFileName(file);
				if (name == "BiomeTemplateArt.cs" || name == "GatheringResourceCatalogue.cs")
				{
					continue;
				}
				foreach (string line in CodeOnly(ProjectFiles.ReadAllText(file)).Split('\n'))
				{
					if (byName.IsMatch(line))
					{
						found.Add($"{name}: {line.Trim()}");
					}
				}
			}
			Assert.IsEmpty(found, "cut-time code reading a biome's vines, resources or herbs by its name: " + string.Join(" | ", found));
		}

		// ── Helpers ───────────────────────────────────────────────────

		internal struct BiomeAsset
		{
			public string Name;
			public string DisplayName;
			public BiomeAtmosphereRequirement Atmosphere;
			public BiomeWorldRequirement Requires;
		}

		/// <summary>Every biome asset's name, display name and atmosphere (the climate profile that shares their folder left out).</summary>
		internal static List<BiomeAsset> BiomeAssets()
		{
			var assets = new List<BiomeAsset>();
			foreach (string path in Directory.GetFiles(ProjectFiles.Folder(BiomeFolder), "*.asset"))
			{
				string text = ProjectFiles.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				assets.Add(new BiomeAsset
				{
					Name = Field(text, "m_Name"),
					DisplayName = Field(text, "DisplayName"),
					Atmosphere = (BiomeAtmosphereRequirement)int.Parse(Field(text, "Atmosphere")),
					Requires = (BiomeWorldRequirement)int.Parse(Field(text, "Requires") ?? "0"),
				});
			}
			assets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
			return assets;
		}

		internal static string Field(string yaml, string key)
		{
			Match m = Regex.Match(yaml, "^  " + key + ": ?(.*)$", RegexOptions.Multiline);
			return m.Success ? m.Groups[1].Value.Trim().Trim('\r') : null;
		}

		/// <summary>The source with comment lines dropped, so a scan sees code and not prose.</summary>
		internal static string CodeOnly(string source)
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
