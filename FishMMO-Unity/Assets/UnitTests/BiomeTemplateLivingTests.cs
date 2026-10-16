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
	/// authoring prunes the grass entries it wrote for blades a biome no longer grows, and nothing else.
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
		/// Every biome asset's spec habitat is what the client read from it before (AmbientGating.HabitatOfBiome: the keywords
		/// of its asset name, then its display name, and alien ground under another world's air), and never None, which would
		/// send its animals to the climate.
		/// </summary>
		[Test]
		public void SpecHabitat_IsWhatTheNameGave_ForEveryBiomeAsset()
		{
			var wrong = new List<string>();
			int count = 0;
			foreach (BiomeAsset asset in BiomeAssets())
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(asset.Name);
				Assert.That(entry, Is.Not.Null, asset.Name);
				AmbientHabitat old = AmbientGating.HabitatOfBiome(asset.Name, asset.DisplayName, asset.Atmosphere);
				if ((int)entry.Habitat != (int)old)
				{
					wrong.Add($"{asset.Name}: spec {entry.Habitat}, its name gave {old}");
				}
				Assert.That(entry.Habitat, Is.Not.EqualTo(BiomeHabitat.None), asset.Name);
				count++;
			}
			Assert.IsEmpty(wrong, string.Join("; ", wrong));
			Assert.That(count, Is.EqualTo(83));
		}

		// ── Sea life ──────────────────────────────────────────────────

		/// <summary>
		/// Every biome's spec sea life is the kinds that named it among their favourites (SeaCreatureKind.Prefers), in the
		/// kinds' order, both in the code's defaults and on the weather profile the client reads (which holds the same lists).
		/// </summary>
		[Test]
		public void SpecSeaLife_IsTheKindsThatPreferredTheBiome_ForEveryBiomeAsset()
		{
			SeaCreatureKind[] kinds = SeaLifeSettings.Defaults();
			Dictionary<string, List<string>> profile = ProfilePrefers();
			foreach (SeaCreatureKind kind in kinds)
			{
				Assert.That(profile.ContainsKey(kind.Name), Is.True, $"the profile holds {kind.Name}");
				Assert.That(profile[kind.Name], Is.EqualTo(kind.Prefers), $"the profile's {kind.Name} prefers what the defaults do");
			}
			int homes = 0;
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
				List<string> spec = BiomeArtSpec.For(asset.Name).SeaLifeHomes();
				Assert.That(spec, Is.EqualTo(old), asset.Name);
				foreach (SeaCreatureKind kind in kinds)
				{
					Assert.That(SeaLifePlacement.IsHome(kind, spec), Is.EqualTo(SeaLifePlacement.PrefersByName(kind, asset.Name)), $"{asset.Name} {kind.Name}");
				}
				homes += spec.Count;
			}
			Assert.That(homes, Is.EqualTo(8), "Coral Reef 3, Coastal Water 2, Beach 1, Ocean 1, Deep Ocean 1");
		}

		/// <summary>The weight from a template's homes equals the weight from the name, and a kind's homes are matched ignoring case.</summary>
		[Test]
		public void SeaLifeWeight_FromHomes_EqualsTheWeightFromTheName()
		{
			SeaCreatureKind reef = SeaLifeSettings.Defaults().First(k => k.Name == "Reef fish");
			foreach (float t in new[] { -0.8f, 0f, 0.5f, 0.9f })
			{
				Assert.That(SeaLifePlacement.HabitatWeight(reef, SeaLifePlacement.IsHome(reef, new[] { "reef FISH" }), t),
					Is.EqualTo(SeaLifePlacement.HabitatWeight(reef, "Coral Reef", t)));
				Assert.That(SeaLifePlacement.HabitatWeight(reef, SeaLifePlacement.IsHome(reef, new string[0]), t),
					Is.EqualTo(SeaLifePlacement.HabitatWeight(reef, "Ocean", t)));
			}
			Assert.That(SeaLifePlacement.IsHome(reef, null), Is.False);
		}

		// ── Vines ─────────────────────────────────────────────────────

		/// <summary>
		/// Every biome's spec vines are its vine-table row (VineBiomes, by its name), and the form the vine rules read back off
		/// a template holding them is that row, value for value; a biome the table never named grows none (null, as before).
		/// </summary>
		[Test]
		public void SpecVines_AreTheVineTablesRow_ForEveryBiomeAsset()
		{
			int rows = 0;
			foreach (BiomeAsset asset in BiomeAssets())
			{
				VineBiomes.Habitat old = VineBiomes.For(asset.Name);
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
			Assert.That(rows, Is.EqualTo(VineBiomes.All.Length), "every row of the table is a biome asset");
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
			foreach (BiomeAsset asset in BiomeAssets())
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
			foreach (BiomeAsset asset in BiomeAssets())
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
				"SeaLifeSystem.cs: return SeaLifePlacement.PrefersByName(kind, biome.name);",
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
