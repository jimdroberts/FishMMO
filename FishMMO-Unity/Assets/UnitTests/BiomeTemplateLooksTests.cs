using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Flora looks and blade grass moved onto the biome template (2026-10-15): every value the template now carries is
	/// what the code decided before (a biome's look class from its name, each ground's makeup from its layer's name, each
	/// blade prefab's look from the render profile's one list and its one ground tint), so authoring changes no scene; the
	/// scene's blade type table keeps one entry per distinct look within the GPU's 32; and nothing outside the fallbacks
	/// reads a biome's or a ground's name any more.
	/// </summary>
	public class BiomeTemplateLooksTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";

		// ── Look class ────────────────────────────────────────────────

		/// <summary>Every biome asset's spec look class is what <see cref="BiomeLooks.ClassOf(string, float, float)"/> read from it.</summary>
		[Test]
		public void SpecLookClass_IsWhatTheNameGave_ForEveryBiomeAsset()
		{
			int checkedBiomes = 0;
			var wrong = new List<string>();
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				string name = Field(text, "m_Name");
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				if (entry == null || Field(text, "MinTemperature") == null)
				{
					continue;
				}
				string display = Field(text, "DisplayName");
				string resolved = string.IsNullOrWhiteSpace(display) ? name : display;
				float temperature = 0.5f * (Number(text, "MinTemperature") + Number(text, "MaxTemperature"));
				float humidity = 0.5f * (Number(text, "MinHumidity") + Number(text, "MaxHumidity"));
				BiomeLookClass old = BiomeLooks.ClassOf(resolved, temperature, humidity);
				if (entry.LookClass != old)
				{
					wrong.Add($"{name}: spec {entry.LookClass}, its name gave {old}");
				}
				checkedBiomes++;
			}
			Assert.IsEmpty(wrong, string.Join("; ", wrong));
			Assert.That(checkedBiomes, Is.EqualTo(BiomeArtSpec.Entries.Length), "every spec biome has its asset");
		}

		[Test]
		public void EverySpecBiome_NamesItsLookClass()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.LookClass, Is.Not.EqualTo(BiomeLookClass.Unset), entry.Biome);
				Assert.That((int)entry.LookClass, Is.LessThanOrEqualTo((int)BiomeLookClass.Aquatic), entry.Biome);
			}
		}

		private static string Field(string yaml, string key)
		{
			Match m = Regex.Match(yaml, "^  " + key + ": ?(.*)$", RegexOptions.Multiline);
			return m.Success ? m.Groups[1].Value.Trim().Trim('\r') : null;
		}

		private static float Number(string yaml, string key) => float.Parse(Field(yaml, key), CultureInfo.InvariantCulture);

		// ── Rare looks ────────────────────────────────────────────────

		[Test]
		public void EveryRareLook_NamesAFantasticLook_InOrder()
		{
			string[] names = Enum.GetNames(typeof(BiomeRareLook));
			Assert.That(names.Length, Is.EqualTo(BiomeLooks.FantasticCount + 1), "one per fantastic look, and Auto");
			for (int i = 0; i < BiomeLooks.FantasticCount; i++)
			{
				Assert.That(((BiomeRareLook)(i + 1)).ToString(), Is.EqualTo(BiomeLooks.FantasticName(i)));
			}
		}

		[Test]
		public void RareLooks_AutoChangesNothing_AndAChoiceIsHonoured()
		{
			BiomeLooks.WorldTraits earth = BiomeLooks.WorldTraits.Earthlike;
			uint hash = BiomeLooks.Hash("enchantedwood");
			BiomeLooks.Look plain = BiomeLooks.For(BiomeLookClass.Forest, hash, earth);
			BiomeLooks.Look auto = BiomeLooks.For(BiomeLookClass.Forest, hash, earth, BiomeRareLook.Auto, BiomeRareLook.Auto);
			for (int i = 0; i < BiomeLooks.Variants; i++)
			{
				Assert.That(auto.Variants[i].Name, Is.EqualTo(plain.Variants[i].Name));
				Assert.That(auto.Variants[i].Foliage, Is.EqualTo(plain.Variants[i].Foliage));
				Assert.That(auto.Variants[i].Shift, Is.EqualTo(plain.Variants[i].Shift));
			}
			BiomeLooks.Look chosen = BiomeLooks.For(BiomeLookClass.Forest, hash, earth, BiomeRareLook.Faerie, BiomeRareLook.Moonsilver);
			Assert.That(chosen.Variants[BiomeLooks.FirstFantastic].Name, Is.EqualTo("Faerie"));
			Assert.That(chosen.Variants[BiomeLooks.FirstFantastic + 1].Name, Is.EqualTo("Moonsilver"));
			Assert.That(chosen.Variants[0].Foliage, Is.EqualTo(plain.Variants[0].Foliage), "the common look is the biome's whatever its rare ones");
			BiomeLooks.Look same = BiomeLooks.For(BiomeLookClass.Forest, hash, earth, BiomeRareLook.Arcane, BiomeRareLook.Arcane);
			Assert.That(same.Variants[BiomeLooks.FirstFantastic + 1].Name, Is.Not.EqualTo("Arcane"), "the second never repeats the first");
		}

		// ── Ground makeup ─────────────────────────────────────────────

		/// <summary>Every generated ground's makeup is what its layer's name (Ground_&lt;Name&gt;) gave before.</summary>
		[Test]
		public void GroundMakeup_IsWhatTheLayersNameGave_ForEveryGround()
		{
			int grounds = 0;
			foreach (string ground in SurfaceCatalogue.GroundsWithMakeup)
			{
				Assert.That(SurfaceCatalogue.TryMakeupOf(ground, out Vector3 makeup), ground);
				BiomeLooks.GroundShare old = BiomeLooks.GroundShareOf("Ground_" + ground);
				Assert.That(makeup.x, Is.EqualTo(old.Foliage), ground + " living");
				Assert.That(makeup.y, Is.EqualTo(old.Accent), ground + " lichen");
				Assert.That(makeup.z, Is.EqualTo(old.Mineral), ground + " mineral");
				grounds++;
			}
			foreach (System.Reflection.FieldInfo field in typeof(Ground).GetFields())
			{
				if (field.IsLiteral && field.FieldType == typeof(string))
				{
					Assert.That(SurfaceCatalogue.TryMakeupOf((string)field.GetRawConstantValue(), out _), $"Ground.{field.Name} has no makeup");
				}
			}
			Assert.That(grounds, Is.GreaterThanOrEqualTo(54));
		}

		// ── Blade looks ───────────────────────────────────────────────

		/// <summary>
		/// The old per-type tunings, as the Weather Render Profile asset and GrassTypeDefaults held them before 2026-10-15
		/// (prefix, density, stiffness, width, clump pull, bend, sprinkle, head, head size, head share, blunt, wade, tall,
		/// root, tip, head colours), every one at the profile's one Terrain Texture Tint of 0.85.
		/// </summary>
		private static readonly (string Prefix, float Density, float Stiffness, float Width, float Clump, float Bend, float Sprinkle, int Head,
			float HeadSize, float HeadShare, bool Blunt, float Wade, bool Tall, string Root, string Tip, string[] Heads)[] OldTunings =
		{
			("Detail_Grass", 1f, 1f, 1f, 0.12f, 0.4f, 0f, 0, 0.035f, 1f, false, 0f, false, null, null, null),
			("Detail_GrassLush", 1f, 0.9f, 1f, 0.1f, 0.45f, 0f, 0, 0.035f, 1f, false, 0f, false, null, null, null),
			("Detail_GrassDry", 0.8f, 1.3f, 0.85f, 0.15f, 0.25f, 0f, 0, 0.035f, 1f, false, 0f, false, null, null, null),
			("Detail_GrassTall", 0.75f, 0.7f, 1.15f, 0.12f, 0.55f, 0f, 0, 0.035f, 1f, false, 0f, false, null, null, null),
			("Detail_GrassTuft", 0.85f, 1.6f, 0.9f, 0.3f, 0.3f, 0f, 0, 0.035f, 1f, false, 0f, false, null, null, null),
			("Detail_Reeds", 1f, 2.6f, 2f, 0.35f, 0.15f, 110f, 1, 0.16f, 1f, true, 0f, false, null, null, null),
			("Detail_Flowers", 1f, 1.3f, 0.8f, 0.1f, 0.12f, 14f, 2, 0.032f, 1f, false, 0f, false, null, null, null),
			("Detail_GrassSedge", 0.85f, 1.3f, 1.1f, 0.45f, 0.6f, 0f, 0, 0.035f, 1f, false, 0.1f, false, "#5e7a4a", "#8a9a62", null),
			("Detail_GrassCotton", 1f, 1.2f, 0.7f, 0.1f, 0.1f, 30f, 2, 0.045f, 1f, false, 0.05f, false, "#5a6a3a", "#8a9450", new[] { "#f6f6f0", "#ecebe0" }),
			("Detail_GrassDune", 0.7f, 1.8f, 0.7f, 0.55f, 0.3f, 0f, 0, 0.035f, 1f, false, 0f, false, "#8a9a6e", "#a8b088", null),
			("Detail_GrassFeather", 0.8f, 0.8f, 0.6f, 0.4f, 0.55f, 0f, 3, 0.22f, 0.25f, false, 0f, false, "#8a8a5a", "#c8c49a", new[] { "#dcd8c0" }),
			("Detail_GrassSavanna", 0.8f, 0.8f, 1.3f, 0.15f, 0.5f, 0f, 0, 0.035f, 1f, false, 0f, true, "#b8963c", "#d8bc6a", null),
			("Detail_ReedsPlume", 1f, 2.4f, 2f, 0.35f, 0.2f, 110f, 3, 0.3f, 1f, true, 0.4f, true, "#4e6430", "#8a9050", new[] { "#6a4a5a" }),
			("Detail_GrassRush", 0.9f, 2.2f, 0.55f, 0.5f, 0.08f, 0f, 1, 0.04f, 0.3f, false, 0.25f, false, "#2e4a1e", "#3e5a2a", new[] { "#6a4a2a" }),
			("Detail_GrassWheat", 0.9f, 1.2f, 1f, 0.05f, 0.35f, 0f, 1, 0.08f, 0.6f, false, 0f, false, "#9a8a40", "#d8c070", new[] { "#c8a050" }),
			("Detail_FlowersAlpine", 1f, 1.5f, 0.7f, 0.1f, 0.08f, 20f, 2, 0.018f, 1f, false, 0f, false, "#4a6a2a", "#66843a", new[] { "#3050c0", "#f4f4f0", "#c0408a", "#f0c830" }),
			("Detail_FlowersWet", 1f, 1.1f, 0.9f, 0.1f, 0.15f, 10f, 2, 0.04f, 1f, false, 0.1f, false, "#3e6a2a", "#5e8a3a", new[] { "#b0408a", "#f0d030", "#f0ead0", "#f2b81e" }),
			("Detail_FlowersWoodland", 1f, 1.3f, 0.8f, 0.1f, 0.2f, 12f, 2, 0.025f, 1f, false, 0f, false, "#3a6224", "#58803a", new[] { "#5a5ac8", "#f6f6f2", "#eef0e6", "#f0e890" }),
			("Detail_FlowersTall", 1f, 1f, 0.9f, 0.1f, 0.2f, 8f, 2, 0.05f, 1f, false, 0f, false, "#4a7a2c", "#6a9040", new[] { "#c84890", "#e0b020", "#7a5ac8", "#f2f0e6" }),
			("Detail_FlowersDesert", 1f, 1.4f, 0.8f, 0.1f, 0.1f, 18f, 2, 0.03f, 1f, false, 0f, false, "#6a7a3a", "#8a9450", new[] { "#f08020", "#4a60c8", "#f0c020", "#d0609a" }),
		};

		/// <summary>The old table's tuning for a prefab name, by longest prefix, as GrassBladeSettings.TuningFor resolved it.</summary>
		private static int OldTuning(string name)
		{
			int best = -1;
			for (int i = 0; i < OldTunings.Length; i++)
			{
				if (name.StartsWith(OldTunings[i].Prefix, StringComparison.Ordinal) && (best < 0 || OldTunings[i].Prefix.Length > OldTunings[best].Prefix.Length))
				{
					best = i;
				}
			}
			return best;
		}

		/// <summary>Every blade prefab's look (and every prefix's) is exactly the old tuning, at the old ground tint.</summary>
		[Test]
		public void BladeDefaults_AreTheOldTunings_AtTheOldGroundTint()
		{
			var names = new List<string>(BiomeGrassDefaults.Prefixes);
			foreach (string prefab in ProceduralArtCatalogue.AllPrefabNames())
			{
				if (BiomeGrassDefaults.IsBlade(prefab))
				{
					names.Add(prefab);
				}
			}
			Assert.That(names.Count, Is.GreaterThan(OldTunings.Length), "the catalogue has blade details");
			foreach (string name in names)
			{
				int i = OldTuning(name);
				Assert.That(i, Is.GreaterThanOrEqualTo(0), name);
				var old = OldTunings[i];
				BiomeGrassType look = BiomeGrassDefaults.For(name);
				Assert.That(look, Is.Not.Null, name);
				Assert.That(look.Density, Is.EqualTo(old.Density), name);
				Assert.That(look.Stiffness, Is.EqualTo(old.Stiffness), name);
				Assert.That(look.Width, Is.EqualTo(old.Width), name);
				Assert.That(look.ClumpPull, Is.EqualTo(old.Clump), name);
				Assert.That(look.Bend, Is.EqualTo(old.Bend), name);
				Assert.That(look.HeightScale, Is.EqualTo(1f), name);
				Assert.That(look.Sprinkle, Is.EqualTo(old.Sprinkle), name);
				Assert.That((int)look.Head, Is.EqualTo(old.Head), name);
				Assert.That(look.HeadSize, Is.EqualTo(old.HeadSize), name);
				Assert.That(look.HeadShare, Is.EqualTo(old.HeadShare), name);
				Assert.That(look.BluntTip, Is.EqualTo(old.Blunt), name);
				Assert.That(look.Wade, Is.EqualTo(old.Wade), name);
				Assert.That(look.Tall, Is.EqualTo(old.Tall), name);
				Assert.That(look.ColoursOverMesh, Is.False, name + ": the mesh's colours win, as they did");
				Assert.That(look.HeadColoursOverMesh, Is.False, name);
				Assert.That(look.RootColour, Is.EqualTo(old.Root != null ? BiomeGrassType.Hex(old.Root) : Color.clear), name);
				Assert.That(look.TipColour, Is.EqualTo(old.Tip != null ? BiomeGrassType.Hex(old.Tip) : Color.clear), name);
				Color[] heads = old.Heads != null ? Array.ConvertAll(old.Heads, BiomeGrassType.Hex) : new[] { new Color(0.36f, 0.25f, 0.14f) };
				Assert.That(look.HeadColours, Is.EqualTo(heads), name + " head colours (the palette, else the one head colour)");
				Assert.That(look.GroundTint, Is.EqualTo(0.85f), name + " ground tint (the profile's Terrain Texture Tint)");
			}
			Assert.That(BiomeGrassDefaults.For("Detail_Fern"), Is.Null, "a fern is no blade");
		}

		/// <summary>Until a biome's file tunes them, the authoring writes every blade prefab's old look onto every biome.</summary>
		[Test]
		public void SpecBladeLooks_AreTheOldLooks_ForEveryBiome()
		{
			int looks = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((string prefab, BiomeGrassType look) in entry.GrassTypes())
				{
					Assert.That(BiomeGrassDefaults.IsBlade(prefab), prefab);
					Assert.That(look.Signature(), Is.EqualTo(BiomeGrassDefaults.For(prefab).Signature()), $"{entry.Biome} / {prefab}");
					Assert.That(look.GroundTint, Is.EqualTo(BiomeGrassType.LegacyGroundTint));
					looks++;
				}
			}
			Assert.That(looks, Is.GreaterThan(50), "the biomes grow blade grass");
		}

		/// <summary>
		/// A blade's healthy and dry tints are now its rule's (carried by its detail prototype) instead of its material's:
		/// for every blade rule the spec writes they are the same colours (both are the catalogue's), so the switch changes
		/// nothing until a biome tunes its rule.
		/// </summary>
		[Test]
		public void SpecBladeRules_TintAsTheirMaterialsDo()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter rule) in entry.Rules())
				{
					if (rule.Prefabs == null)
					{
						continue;
					}
					foreach (string prefab in rule.Prefabs)
					{
						if (!BiomeGrassDefaults.IsBlade(prefab))
						{
							continue;
						}
						Assert.That(ProceduralArtCatalogue.TryDetail(prefab.Substring("Detail_".Length), out DetailSpec spec), prefab);
						Assert.That(rule.Healthy, Is.EqualTo(spec.Healthy), $"{entry.Biome} / {rule.Name}: healthy tint");
						Assert.That(rule.Dry, Is.EqualTo(spec.Dry), $"{entry.Biome} / {rule.Name}: dry tint");
					}
				}
			}
		}

		[Test]
		public void AHandMadeBladeLook_StartsAtTheNewGroundTint()
		{
			Assert.That(new BiomeGrassType().GroundTint, Is.EqualTo(0.4f));
			Assert.That(BiomeGrassType.DefaultGroundTint, Is.EqualTo(0.4f));
		}

		[Test]
		public void ASignature_IsEqualForEqualLooks_AndMovesWithEveryValue()
		{
			BiomeGrassType a = BiomeGrassDefaults.For("Detail_FlowersWet"), b = BiomeGrassDefaults.For("Detail_FlowersWet");
			Assert.That(a.Signature(), Is.EqualTo(b.Signature()));
			b.GroundTint = 0.4f;
			Assert.That(a.Signature(), Is.Not.EqualTo(b.Signature()), "ground tint");
			b = a.Clone();
			b.HeadColours[0] = Color.red;
			Assert.That(a.Signature(), Is.Not.EqualTo(b.Signature()), "a head colour");
			Assert.That(a.HeadColours[0], Is.Not.EqualTo(Color.red), "a clone's colours are its own");
			b = a.Clone();
			b.ColoursOverMesh = true;
			Assert.That(a.Signature(), Is.Not.EqualTo(b.Signature()), "colours over the mesh");
		}

		// ── The scene's blade type table ──────────────────────────────

		[Test]
		public void TheTypeTable_SharesEqualLooks_AndGivesEachDistinctLookItsOwn()
		{
			var keys = new List<(string Prefab, string Look)>();
			int lush = GrassTypeTable.Place(keys, "Lush", "old", 32, out GrassTypeTable.Placement how);
			Assert.That(how, Is.EqualTo(GrassTypeTable.Placement.Added));
			Assert.That(GrassTypeTable.Place(keys, "Lush", "old", 32, out how), Is.EqualTo(lush), "a second biome with the same look shares the entry");
			Assert.That(how, Is.EqualTo(GrassTypeTable.Placement.Existing));
			int straw = GrassTypeTable.Place(keys, "Lush", "straw", 32, out how);
			Assert.That(how, Is.EqualTo(GrassTypeTable.Placement.Added));
			Assert.That(straw, Is.Not.EqualTo(lush), "one prefab, two looks: two entries");
			int dry = GrassTypeTable.Place(keys, "Dry", "old", 32, out how);
			Assert.That(dry, Is.EqualTo(2));
			Assert.That(keys.Count, Is.EqualTo(3));
		}

		[Test]
		public void TheTypeTable_IsCappedAtTheGpus32_MergingLooksOfAPrefabItHas()
		{
			Assert.That(GrassBladeRenderer.MaxTypes, Is.EqualTo(32), "five bits of the blade record");
			var keys = new List<(string Prefab, string Look)>();
			for (int i = 0; i < GrassBladeRenderer.MaxTypes; i++)
			{
				GrassTypeTable.Place(keys, i < 16 ? "Lush" : "P" + i, "look" + i, GrassBladeRenderer.MaxTypes, out GrassTypeTable.Placement added);
				Assert.That(added, Is.EqualTo(GrassTypeTable.Placement.Added));
			}
			int merged = GrassTypeTable.Place(keys, "Lush", "another biome's", GrassBladeRenderer.MaxTypes, out GrassTypeTable.Placement how);
			Assert.That(how, Is.EqualTo(GrassTypeTable.Placement.Merged));
			Assert.That(merged, Is.EqualTo(0), "a full table draws the look with its prefab's first entry");
			Assert.That(GrassTypeTable.Place(keys, "Unseen", "x", GrassBladeRenderer.MaxTypes, out how), Is.EqualTo(-1));
			Assert.That(how, Is.EqualTo(GrassTypeTable.Placement.Refused), "a prefab with no entry past the cap stays meshes");
			Assert.That(keys.Count, Is.EqualTo(GrassBladeRenderer.MaxTypes));
			Assert.That(GrassTypeTable.Place(keys, "Lush", "look3", GrassBladeRenderer.MaxTypes, out how), Is.EqualTo(3));
			Assert.That(how, Is.EqualTo(GrassTypeTable.Placement.Existing), "a look it has is still found when full");
		}

		// ── Nothing reads names any more ──────────────────────────────

		/// <summary>
		/// Runtime and cut-time code reads a biome's look class from its template (<see cref="BiomeLooks.ClassOf(BiomeTemplate)"/>)
		/// and a ground's makeup from its slot: the keyword readers are called only by their fallbacks.
		/// </summary>
		[Test]
		public void OnlyTheFallbacks_ReadABiomesOrAGroundsName()
		{
			var offenders = new List<string>();
			foreach (string path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
			{
				string file = path.Replace('\\', '/');
				string code = StripComments(File.ReadAllText(path));
				foreach (Match m in Regex.Matches(code, @"\bClassOf\(([^()]*)\)"))
				{
					bool byName = m.Groups[1].Value.Contains(",");
					bool fallback = file.EndsWith("/BiomeLooks.cs") && (m.Groups[1].Value.StartsWith("string name") || m.Groups[1].Value.StartsWith("biome.ResolvedDisplayName"));
					if (byName && !fallback)
					{
						offenders.Add($"{file}: ClassOf({m.Groups[1].Value})");
					}
				}
				if (file.EndsWith("/BiomeLooks.cs"))
				{
					int call = code.IndexOf("ClassOf(biome.ResolvedDisplayName", StringComparison.Ordinal);
					int from = code.IndexOf("BiomeLookClass ClassFromName(", StringComparison.Ordinal);
					int to = code.IndexOf("public static BiomeLookClass ClassOf(string name", StringComparison.Ordinal);
					Assert.That(call > from && from >= 0 && call < to, "the name reading is called only from ClassFromName");
				}
				bool groundFallback = file.EndsWith("/BiomeLooks.cs") || file.EndsWith("/TerrainArraySet.cs") || file.EndsWith("/GroundColourMap.cs");
				if (!groundFallback && code.Contains("GroundShareOf("))
				{
					offenders.Add($"{file}: GroundShareOf");
				}
				if (code.Contains("TuningFor(") || code.Contains("GrassTypeTuning") || code.Contains("TerrainTextureTint"))
				{
					offenders.Add($"{file}: the profile's old per-type looks");
				}
			}
			Assert.IsEmpty(offenders, string.Join("\n", offenders));
		}

		private static string StripComments(string code)
		{
			var lines = code.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				int at = lines[i].IndexOf("//", StringComparison.Ordinal);
				if (at >= 0)
				{
					lines[i] = lines[i].Substring(0, at);
				}
			}
			return Regex.Replace(string.Join("\n", lines), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
		}
	}
}
