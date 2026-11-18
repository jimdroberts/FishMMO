using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The ambient creatures' country and the sea life's favourite waters on the biome template (split from BiomeTemplateLivingTests, Phase 4, 2026-11-17).
	/// </summary>
	[TestFixture]
	public class AmbientBiomeTemplateTests
	{
		private const string ClientWorldFolder = "Assets/FishMMO/Modules/AmbientLife/Client";
		private const string ProfilePath = "Assets/FishMMO/Modules/AmbientLife/Settings/Ambient Life Render Profile.asset";

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
		/// Every biome template's habitat, with the Alien bit the client reads live from the template's air (I4b, 2026-10-16:
		/// <see cref="BiomeTemplate.LiveHabitat"/>), is what the client read from its name before (AmbientGating.HabitatOfBiome:
		/// the keywords of its asset name, then its display name, and alien ground under another world's air); Alien is never
		/// stored, and the live reading is never None, which would send its animals to the climate. (Split at the template,
		/// Phase 4, 2026-11-18: the World Design module's tests check its authoring writes the spec's habitat onto the template.)
		/// </summary>
		[Test]
		public void TemplateHabitat_WithTheLiveAlienBit_IsWhatTheNameGave_ForEveryBiomeAsset()
		{
			var wrong = new List<string>();
			int count = 0, alien = 0;
			foreach (BiomeTemplate biome in AmbientBiomeAssets.All())
			{
				Assert.That(biome.Habitat & BiomeHabitat.Alien, Is.EqualTo(BiomeHabitat.None), biome.name + ": Alien is never stored");
				AmbientHabitat old = AmbientGating.HabitatOfBiome(biome.name, biome.DisplayName, biome.Atmosphere);
				BiomeHabitat live = biome.LiveHabitat;
				// An authored round names its own country (legacy equality only; the invariants above and below still hold for it). The
				// sea halls' phase (I2n-b, 2026-10-26) gave the Subsurface Ocean Vent its sea and its caves on purpose.
				bool changed = BiomeAuthoredRounds.Contains(biome.name)
					|| biome.name == "Subsurface Ocean Vent" && live == (BiomeHabitat.Sea | BiomeHabitat.Cave);
				if ((int)live != (int)old && !changed)
				{
					wrong.Add($"{biome.name}: template {biome.Habitat} read live as {live}, its name gave {old}");
				}
				Assert.That(live, Is.Not.EqualTo(BiomeHabitat.None), biome.name);
				alien += (live & BiomeHabitat.Alien) != 0 ? 1 : 0;
				count++;
			}
			Assert.IsEmpty(wrong, string.Join("; ", wrong));
			Assert.That(count, Is.EqualTo(83));
			Assert.That(alien, Is.EqualTo(9), "Cryovolcanic Plain, Dust Sea, Ice Geyser Field, Impact Basin, Methane Lake, Nitrogen Ice Field, Regolith Plain, Rille, Tholin Plain");
		}

		// ── Sea life ──────────────────────────────────────────────────

		/// <summary>
		/// Every biome template's sea life is the kinds that named it among their favourites (SeaCreatureKind.Prefers), in the
		/// kinds' order, both in the code's defaults and on the ambient life profile the client reads (which holds the same
		/// lists); and each at the weight the kind gave its favourites (PreferWeight), so for every biome, every kind and the
		/// whole temperature scale, the weight the template gives is the weight the kind gave by the biome's name.
		/// </summary>
		[Test]
		public void TemplateSeaLife_IsTheKindsThatPreferredTheBiome_AtTheirWeight_ForEveryBiomeAsset()
		{
			SeaCreatureKind[] kinds = SeaLifeSettings.Defaults();
			Dictionary<string, List<string>> profile = ProfilePrefers();
			foreach (SeaCreatureKind kind in kinds)
			{
				Assert.That(profile.ContainsKey(kind.Name), Is.True, $"the profile holds {kind.Name}");
				Assert.That(profile[kind.Name], Is.EqualTo(kind.Prefers), $"the profile's {kind.Name} prefers what the defaults do");
			}
			int homes = 0, oldHomes = 0;
			foreach (BiomeTemplate biome in AmbientBiomeAssets.All())
			{
				var old = new List<string>();
				foreach (SeaCreatureKind kind in kinds)
				{
					if (SeaLifePlacement.PrefersByName(kind, biome.name))
					{
						old.Add(kind.Name);
					}
				}
				List<BiomeSeaLife> life = biome.SeaLife ?? new List<BiomeSeaLife>();
				if (BiomeAuthoredRounds.Contains(biome.name))
				{
					// An authored round names its own sea life at its own weights; every kind it names is one the client has.
					Assert.That(life.All(l => kinds.Any(k => string.Equals(k.Name, l.Kind, StringComparison.OrdinalIgnoreCase))), Is.True, biome.name);
					oldHomes += old.Count;
					continue;
				}
				Assert.That(life.Select(l => l.Kind).ToList(), Is.EqualTo(old), biome.name);
				foreach (SeaCreatureKind kind in kinds)
				{
					bool home = SeaLifePlacement.TryHomeWeight(kind, life, out float weight);
					Assert.That(home, Is.EqualTo(SeaLifePlacement.PrefersByName(kind, biome.name)), $"{biome.name} {kind.Name}");
					if (home)
					{
						Assert.That(weight, Is.EqualTo(SeaLifePlacement.KindHomeWeight(kind)), $"{biome.name} {kind.Name}: the kind's own weight");
					}
					for (float t = -1f; t <= 1.0001f; t += 0.05f)
					{
						Assert.That(SeaLifePlacement.HabitatWeight(kind, home, weight, t), Is.EqualTo(SeaLifePlacement.HabitatWeight(kind, biome.name, t)),
							$"{biome.name} {kind.Name} at {t}");
					}
				}
				homes += life.Count;
			}
			Assert.That(homes + oldHomes, Is.EqualTo(8), "Coral Reef 3, Coastal Water 2, Beach 1, Ocean 1, Deep Ocean 1 (an authored biome by the kinds' names)");
		}

		/// <summary>
		/// Each kind's preferred weight on the ambient life profile is the code's default, never below 1 at home (the kinds'
		/// minimum). The World Design module's spec keeps its own copy (the editor's art tables cannot see the client's
		/// settings); its tests read this profile to check the copy.
		/// </summary>
		[Test]
		public void ProfileSeaKindWeights_AreTheDefaults()
		{
			Dictionary<string, float> profile = ProfilePreferWeights();
			foreach (SeaCreatureKind kind in SeaLifeSettings.Defaults())
			{
				Assert.That(profile.ContainsKey(kind.Name), Is.True, $"the profile holds {kind.Name}");
				Assert.That(SeaLifePlacement.KindHomeWeight(kind), Is.EqualTo(Math.Max(1f, profile[kind.Name])), $"the profile's {kind.Name}");
			}
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
			foreach (string file in Directory.GetFiles(ProjectFiles.Folder(ClientWorldFolder), "*.cs", SearchOption.AllDirectories))
			{
				string name = Path.GetFileName(file);
				string code = SourceScanPins.CodeOnly(ProjectFiles.ReadAllText(file));
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
			string gating = SourceScanPins.CodeOnly(ProjectFiles.ReadAllText(Path.Combine(ClientWorldFolder, "AmbientLife/AmbientGating.cs")));
			Assert.That(Regex.Matches(gating, @"=\s*HabitatOfBiome\(").Count, Is.EqualTo(1), "HabitatOfTemplate's fallback is the one caller");
			string life = SourceScanPins.CodeOnly(ProjectFiles.ReadAllText(Path.Combine(ClientWorldFolder, "AmbientLife/AmbientLifeSystem.cs")));
			Assert.That(life, Does.Contain("AmbientGating.HabitatOfTemplate(template)"));
		}

		/// <summary>Each sea kind's Prefers list on the weather profile, by kind name (only the sea kinds' lists are lists).</summary>
		private static Dictionary<string, List<string>> ProfilePrefers()
		{
			var seaKinds = new HashSet<string>(SeaLifeSettings.Defaults().Select(k => k.Name));
			var prefers = new Dictionary<string, List<string>>();
			string[] lines = ProjectFiles.ReadAllText(ProfilePath).Replace("\r\n", "\n").Split('\n');
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
			foreach (string line in ProjectFiles.ReadAllText(ProfilePath).Replace("\r\n", "\n").Split('\n'))
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
	}
}
