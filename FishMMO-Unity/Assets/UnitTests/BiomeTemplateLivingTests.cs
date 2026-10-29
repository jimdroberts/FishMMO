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
	/// Phase I4 of the biome suite (2026-10-16): the ambient creatures' country, the sea life's favourite waters, the vines,
	/// the gathering resources and a herb grove's herbs moved off the biome's NAME onto the biome template. Every spec column
	/// the authoring copies onto the templates is what the name gave before, for every biome asset, so authoring changes
	/// nothing; the code that places them reads the template and names a biome only in its logged fallback. With it, I3b: the
	/// authoring prunes the grass entries it wrote for blades a biome no longer grows, and nothing else. And I4b (2026-10-16):
	/// the Alien bit is read live from the template's air, the sea life carries a weight per biome, and a point of interest
	/// paints its own biome's prototype of a plant.
	/// </summary>
	public class BiomeTemplateLivingTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string ClientWorldFolder = "Assets/Scripts/Client/World";
		private const string ProfilePath = "Assets/Prefabs/Client/Weather/Weather Render Profile.asset";

		// ── Ambient creatures ─────────────────────────────────────────

		/// <summary>The template's habitat is the client's own, member for member and bit for bit (the client casts one to the other).</summary>
		[Test]
		public void BiomeHabitat_MirrorsAmbientHabitat()
		{
			string[] names = Enum.GetNames(typeof(AmbientHabitat));
			Assert.That(Enum.GetNames(typeof(BiomeHabitat)), Is.EqualTo(names));
			foreach (string name in names)
			{
				Assert.That((int)Enum.Parse(typeof(BiomeHabitat), name), Is.EqualTo((int)Enum.Parse(typeof(AmbientHabitat), name)), name);
			}
		}

		/// <summary>
		/// Every biome asset's spec habitat, with the Alien bit the client reads live from the template's air (I4b, 2026-10-16:
		/// <see cref="BiomeHabitats.Live"/> from its Atmosphere and Requires), is what the client read from it before
		/// (AmbientGating.HabitatOfBiome: the keywords of its asset name, then its display name, and alien ground under another
		/// world's air); the spec never names Alien itself (the authoring must not store it), every file names its country, and
		/// the live reading is never None, which would send its animals to the climate.
		/// </summary>
		[Test]
		public void SpecHabitat_WithTheLiveAlienBit_IsWhatTheNameGave_ForEveryBiomeAsset()
		{
			var wrong = new List<string>();
			int count = 0, alien = 0;
			foreach (BiomeAsset asset in BiomeAssets())
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(asset.Name);
				Assert.That(entry, Is.Not.Null, asset.Name);
				Assert.That(entry.WildlifeNamed, Is.True, asset.Name + " names its country");
				Assert.That(entry.Habitat & BiomeHabitat.Alien, Is.EqualTo(BiomeHabitat.None), asset.Name + ": Alien is never stored");
				AmbientHabitat old = AmbientGating.HabitatOfBiome(asset.Name, asset.DisplayName, asset.Atmosphere);
				BiomeHabitat live = BiomeHabitats.Live(entry.Habitat, asset.Atmosphere, asset.Requires);
				// An authored round names its own country (legacy equality only; the invariants above and below still hold for it). The
				// sea halls' phase (I2n-b, 2026-10-26) gave the Subsurface Ocean Vent its sea and its caves on purpose.
				bool changed = BiomeAuthoredRounds.Contains(asset.Name)
					|| asset.Name == "Subsurface Ocean Vent" && live == (BiomeHabitat.Sea | BiomeHabitat.Cave);
				if ((int)live != (int)old && !changed)
				{
					wrong.Add($"{asset.Name}: spec {entry.Habitat} read live as {live}, its name gave {old}");
				}
				Assert.That(live, Is.Not.EqualTo(BiomeHabitat.None), asset.Name);
				alien += (live & BiomeHabitat.Alien) != 0 ? 1 : 0;
				count++;
			}
			Assert.IsEmpty(wrong, string.Join("; ", wrong));
			Assert.That(count, Is.EqualTo(83));
			Assert.That(alien, Is.EqualTo(9), "Cryovolcanic Plain, Dust Sea, Ice Geyser Field, Impact Basin, Methane Lake, Nitrogen Ice Field, Regolith Plain, Rille, Tholin Plain");
		}

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
		/// The marks I4's authoring wrote with the Alien bit read without it, so a habitat the tool wrote and nobody touched is
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
			string[] files = Directory.GetFiles(folder, "*.cs");
			Assert.That(files.Length, Is.EqualTo(83));
			foreach (string file in files)
			{
				Assert.That(CodeOnly(File.ReadAllText(file)), Does.Not.Contain("BiomeHabitat.Alien"), Path.GetFileName(file));
			}
		}

		// ── Sea life ──────────────────────────────────────────────────

		/// <summary>
		/// Every biome's spec sea life is the kinds that named it among their favourites (SeaCreatureKind.Prefers), in the
		/// kinds' order, both in the code's defaults and on the weather profile the client reads (which holds the same lists);
		/// and each at the weight the kind gave its favourites (PreferWeight), so for every biome, every kind and the whole
		/// temperature scale, the weight the template gives is the weight the kind gave by the biome's name.
		/// </summary>
		[Test]
		public void SpecSeaLife_IsTheKindsThatPreferredTheBiome_AtTheirWeight_ForEveryBiomeAsset()
		{
			SeaCreatureKind[] kinds = SeaLifeSettings.Defaults();
			Dictionary<string, List<string>> profile = ProfilePrefers();
			foreach (SeaCreatureKind kind in kinds)
			{
				Assert.That(profile.ContainsKey(kind.Name), Is.True, $"the profile holds {kind.Name}");
				Assert.That(profile[kind.Name], Is.EqualTo(kind.Prefers), $"the profile's {kind.Name} prefers what the defaults do");
			}
			int homes = 0, oldHomes = 0;
			foreach (BiomeAsset asset in BiomeAssets())
			{
				var old = new List<string>();
				foreach (SeaCreatureKind kind in kinds)
				{
					if (SeaLifePlacement.PrefersByName(kind, asset.Name))
					{
						old.Add(kind.Name);
					}
				}
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(asset.Name);
				List<BiomeSeaLife> spec = entry.SeaLife();
				if (BiomeAuthoredRounds.Contains(asset.Name))
				{
					// An authored round names its own sea life at its own weights (BiomeAuthoredRoundTests); every kind it names is one
					// the client has.
					Assert.That(entry.SeaLifeKinds().All(n => kinds.Any(k => k.Name == n)), Is.True, asset.Name);
					oldHomes += old.Count;
					continue;
				}
				Assert.That(entry.SeaLifeKinds(), Is.EqualTo(old), asset.Name);
				foreach (SeaCreatureKind kind in kinds)
				{
					bool home = SeaLifePlacement.TryHomeWeight(kind, spec, out float weight);
					Assert.That(home, Is.EqualTo(SeaLifePlacement.PrefersByName(kind, asset.Name)), $"{asset.Name} {kind.Name}");
					if (home)
					{
						Assert.That(weight, Is.EqualTo(SeaLifePlacement.KindHomeWeight(kind)), $"{asset.Name} {kind.Name}: the kind's own weight");
					}
					for (float t = -1f; t <= 1.0001f; t += 0.05f)
					{
						Assert.That(SeaLifePlacement.HabitatWeight(kind, home, weight, t), Is.EqualTo(SeaLifePlacement.HabitatWeight(kind, asset.Name, t)),
							$"{asset.Name} {kind.Name} at {t}");
					}
				}
				homes += spec.Count;
			}
			Assert.That(homes + oldHomes, Is.EqualTo(8), "Coral Reef 3, Coastal Water 2, Beach 1, Ocean 1, Deep Ocean 1 (an authored biome by the kinds' names)");
		}

		/// <summary>
		/// The spec's copy of each kind's preferred weight (the editor's art tables cannot see the client's settings) is the
		/// kind's, in the code's defaults and on the weather profile, for every kind; never below 1, as the kinds' minimum.
		/// </summary>
		[Test]
		public void SpecSeaKindWeights_AreTheKindsPreferredWeights()
		{
			Dictionary<string, float> profile = ProfilePreferWeights();
			foreach (SeaCreatureKind kind in SeaLifeSettings.Defaults())
			{
				Assert.That(BiomeArtSpec.SeaKindPreferWeight(kind.Name), Is.EqualTo(SeaLifePlacement.KindHomeWeight(kind)), kind.Name);
				Assert.That(profile.ContainsKey(kind.Name), Is.True, $"the profile holds {kind.Name}");
				Assert.That(BiomeArtSpec.SeaKindPreferWeight(kind.Name), Is.EqualTo(Math.Max(1f, profile[kind.Name])), $"the profile's {kind.Name}");
			}
			Assert.That(BiomeArtSpec.SeaKindPreferWeight("A kind not yet made"), Is.EqualTo(new SeaCreatureKind().PreferWeight));
		}

		/// <summary>A template's sea life is matched ignoring case, the first entry for a kind wins, and a weight is never below nothing.</summary>
		[Test]
		public void SeaLifeWeight_IsTheFirstEntryNamingTheKind()
		{
			SeaCreatureKind reef = SeaLifeSettings.Defaults().First(k => k.Name == "Reef fish");
			var life = new List<BiomeSeaLife> { new BiomeSeaLife("Crab", 4f), new BiomeSeaLife("reef FISH", 7f), new BiomeSeaLife("Reef fish", 1f) };
			Assert.That(SeaLifePlacement.TryHomeWeight(reef, life, out float weight), Is.True);
			Assert.That(weight, Is.EqualTo(7f));
			Assert.That(SeaLifePlacement.HabitatWeight(reef, true, weight, -0.9f), Is.EqualTo(7f), "a home whatever the thermometer says");
			Assert.That(SeaLifePlacement.TryHomeWeight(reef, new List<BiomeSeaLife> { new BiomeSeaLife("Reef fish", -2f) }, out weight) && weight == 0f, Is.True);
			Assert.That(SeaLifePlacement.TryHomeWeight(reef, null, out _), Is.False);
			Assert.That(SeaLifePlacement.TryHomeWeight(reef, new List<BiomeSeaLife>(), out _), Is.False);
			Assert.That(SeaLifePlacement.HabitatWeight(reef, false, 7f, 0.8f), Is.EqualTo(SeaLifePlacement.HabitatWeight(reef, "Ocean", 0.8f)), "unlisted: elsewhere");
			Assert.That(new BiomeSeaLife("Crab", 2f).ToString(), Is.EqualTo("Crab×2"));
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
					// GatheringResourceCatalogue.GrowsIn before 2026-10-16, copied literally.
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

		/// <summary>PointOfInterestHerbGrove.HerbsFor before 2026-10-16, copied literally: the oracle for the spec's grove herbs.</summary>
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

		// ── I3b: grass entries the authoring wrote ────────────────────

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

		// ── I4b: a point of interest's own prototypes ─────────────────

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
			string grove = CodeOnly(File.ReadAllText(Path.Combine(features, "PointOfInterestHerbGrove.cs")));
			string mouths = CodeOnly(File.ReadAllText(Path.Combine(features, "PointOfInterestCaveMouths.cs")));
			string fields = CodeOnly(File.ReadAllText(Path.Combine(features, "PointOfInterestFields.cs")));
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
			files.AddRange(Directory.GetFiles(Path.Combine(WorldFolder, "Terrain"), "*.cs", SearchOption.AllDirectories));
			files.AddRange(Directory.GetFiles(Path.Combine(WorldFolder, "PointsOfInterest"), "*.cs", SearchOption.AllDirectories));
			files.AddRange(Directory.GetFiles(Path.Combine(WorldFolder, "Vines"), "*.cs", SearchOption.AllDirectories));
			Assert.That(files.Count, Is.GreaterThan(50));
			var found = new List<string>();
			foreach (string file in files)
			{
				string name = Path.GetFileName(file);
				if (name == "BiomeTemplateArt.cs" || name == "GatheringResourceCatalogue.cs")
				{
					continue;
				}
				foreach (string line in CodeOnly(File.ReadAllText(file)).Split('\n'))
				{
					if (byName.IsMatch(line))
					{
						found.Add($"{name}: {line.Trim()}");
					}
				}
			}
			Assert.IsEmpty(found, "cut-time code reading a biome's vines, resources or herbs by its name: " + string.Join(" | ", found));
		}

		/// <summary>
		/// The client reads a biome's creatures and sea life off its template: the keyword reading of a name is called only by
		/// AmbientGating's own fallback, and the kinds' lists of biome names only by the sea life's fallback (and the
		/// placement's hash of the settings).
		/// </summary>
		[Test]
		public void ClientCode_ReadsHabitatAndSeaLifeFromTheTemplate()
		{
			var habitatByName = new Regex(@"\bHabitatOfBiome\(|\bHabitatOf\(", RegexOptions.CultureInvariant);
			var seaByName = new Regex(@"\bPrefersByName\(|\.Prefers\b|HabitatWeight\([^,]+,\s*(biome|[A-Za-z_.]*name)\b", RegexOptions.CultureInvariant);
			Assert.That(habitatByName.IsMatch("habitat = AmbientGating.HabitatOfBiome(template.name, template.ResolvedDisplayName, template.Atmosphere);"), Is.True, "control");
			Assert.That(seaByName.IsMatch("float weight = HabitatWeight(kind, biome, temperature);"), Is.True, "control");
			Assert.That(seaByName.IsMatch("float weight = HabitatWeight(kind, preferred, temperature);"), Is.False, "control");
			var found = new List<string>();
			foreach (string file in Directory.GetFiles(ClientWorldFolder, "*.cs", SearchOption.AllDirectories))
			{
				string name = Path.GetFileName(file);
				string code = CodeOnly(File.ReadAllText(file));
				foreach (string line in code.Split('\n'))
				{
					bool habitat = habitatByName.IsMatch(line) && name != "AmbientGating.cs";
					bool sea = file.Replace('\\', '/').Contains("/SeaLife/") && seaByName.IsMatch(line) && name != "SeaLifeSettings.cs";
					if (habitat || sea)
					{
						found.Add($"{name}: {line.Trim()}");
					}
				}
			}
			// The allowed ones, each in its fallback or its definition.
			string[] allowed =
			{
				"SeaLifePlacement.cs: public static float HabitatWeight(SeaCreatureKind kind, string biome, float temperature) => HabitatWeight(kind, PrefersByName(kind, biome), temperature);",
				"SeaLifePlacement.cs: public static bool PrefersByName(SeaCreatureKind kind, string biome)",
				"SeaLifePlacement.cs: if (kind == null || biome == null || kind.Prefers == null)",
				"SeaLifePlacement.cs: foreach (string name in kind.Prefers)",
				"SeaLifeSystem.cs: if (kind.Prefers != null)",
				"SeaLifeSystem.cs: foreach (string p in kind.Prefers)",
				"SeaLifeSystem.cs: if (!SeaLifePlacement.PrefersByName(kind, biome.name))",
			};
			Assert.That(found.Except(allowed).ToList(), Is.Empty, "client code reading a biome's creatures by its name: " + string.Join(" | ", found.Except(allowed)));
			// The ambient fallback is the template reader's.
			string gating = CodeOnly(File.ReadAllText(Path.Combine(ClientWorldFolder, "AmbientLife/AmbientGating.cs")));
			Assert.That(Regex.Matches(gating, @"=\s*HabitatOfBiome\(").Count, Is.EqualTo(1), "HabitatOfTemplate's fallback is the one caller");
			string life = CodeOnly(File.ReadAllText(Path.Combine(ClientWorldFolder, "AmbientLife/AmbientLifeSystem.cs")));
			Assert.That(life, Does.Contain("AmbientGating.HabitatOfTemplate(template)"));
		}

		// ── Helpers ───────────────────────────────────────────────────

		private struct BiomeAsset
		{
			public string Name;
			public string DisplayName;
			public BiomeAtmosphereRequirement Atmosphere;
			public BiomeWorldRequirement Requires;
		}

		/// <summary>Every biome asset's name, display name and atmosphere (the climate profile that shares their folder left out).</summary>
		private static List<BiomeAsset> BiomeAssets()
		{
			var assets = new List<BiomeAsset>();
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
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

		private static string Field(string yaml, string key)
		{
			Match m = Regex.Match(yaml, "^  " + key + ": ?(.*)$", RegexOptions.Multiline);
			return m.Success ? m.Groups[1].Value.Trim().Trim('\r') : null;
		}

		/// <summary>Each sea kind's Prefers list on the weather profile, by kind name (only the sea kinds' lists are lists).</summary>
		private static Dictionary<string, List<string>> ProfilePrefers()
		{
			var seaKinds = new HashSet<string>(SeaLifeSettings.Defaults().Select(k => k.Name));
			var prefers = new Dictionary<string, List<string>>();
			string[] lines = File.ReadAllText(ProfilePath).Replace("\r\n", "\n").Split('\n');
			string kind = null;
			for (int i = 0; i < lines.Length; i++)
			{
				Match name = Regex.Match(lines[i], @"^\s*- Name: (.+)$");
				if (name.Success)
				{
					kind = seaKinds.Contains(name.Groups[1].Value.Trim()) ? name.Groups[1].Value.Trim() : null;
					continue;
				}
				if (kind == null)
				{
					continue;
				}
				if (Regex.IsMatch(lines[i], @"^\s*Prefers: \[\]\s*$"))
				{
					prefers[kind] = new List<string>();
				}
				else if (Regex.IsMatch(lines[i], @"^\s*Prefers:\s*$"))
				{
					var list = new List<string>();
					while (i + 1 < lines.Length && Regex.IsMatch(lines[i + 1], @"^\s*- (?!Name:)"))
					{
						list.Add(Regex.Match(lines[++i], @"^\s*- (.+)$").Groups[1].Value.Trim());
					}
					prefers[kind] = list;
				}
			}
			return prefers;
		}

		/// <summary>Each sea kind's PreferWeight on the weather profile, by kind name.</summary>
		private static Dictionary<string, float> ProfilePreferWeights()
		{
			var seaKinds = new HashSet<string>(SeaLifeSettings.Defaults().Select(k => k.Name));
			var weights = new Dictionary<string, float>();
			string kind = null;
			foreach (string line in File.ReadAllText(ProfilePath).Replace("\r\n", "\n").Split('\n'))
			{
				Match name = Regex.Match(line, @"^\s*- Name: (.+)$");
				if (name.Success)
				{
					kind = seaKinds.Contains(name.Groups[1].Value.Trim()) ? name.Groups[1].Value.Trim() : null;
					continue;
				}
				Match weight = Regex.Match(line, @"^\s*PreferWeight: (.+)$");
				if (kind != null && weight.Success)
				{
					weights[kind] = float.Parse(weight.Groups[1].Value.Trim(), System.Globalization.CultureInfo.InvariantCulture);
				}
			}
			return weights;
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
