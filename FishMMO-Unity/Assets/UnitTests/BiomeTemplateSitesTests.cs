using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I5 of the biome suite (2026-10-16): the points of interest a biome hosts, the template variants its sites are
	/// built as, how the weather finishes its buildings and what its fields grow moved off the biome's NAME onto the biome
	/// template, and the template gained its structure palette. Every spec column the authoring copies onto the templates is
	/// what the name (or the kit) gave before, for every biome, so authoring changes nothing; the cut reads the template and
	/// names a biome only in BiomeTemplateArt's logged fallbacks and the naming.
	/// </summary>
	public class BiomeTemplateSitesTests
	{
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private static IEnumerable<BiomeArtSpec.Entry> Biomes => BiomeArtSpec.Entries;

		// ── Kinds ─────────────────────────────────────────────────────

		/// <summary>
		/// Every biome's site kinds, as the authoring writes them and the planner reads them (PointOfInterestBiome.WeightOf), give
		/// every kind exactly the weight the catalogue's rows gave the biome by its name (PointOfInterestKindRule.BiomeWeight, the
		/// planner's old reading, never below 0): the swamp kinds only in the swamps, settlements by how habitable the ground
		/// is, nothing built on the sea floor or a lake, detected landforms anywhere.
		/// </summary>
		[Test]
		public void SpecSiteKinds_GiveEveryKindTheWeightTheRowsGaveByName()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			var kinds = (POIType[])Enum.GetValues(typeof(POIType));
			int compared = 0, hosted = 0;
			foreach (BiomeArtSpec.Entry entry in Biomes)
			{
				List<BiomeSiteKind> list = entry.SiteKinds(rules);
				Assert.That(list.Select(k => k.Kind).Distinct().Count(), Is.EqualTo(list.Count), $"{entry.Biome}: one entry a kind");
				Assert.That(list.All(k => k.Weight > 0f), Is.True, $"{entry.Biome}: only kinds it hosts");
				var biome = new PointOfInterestBiome(entry.Biome, 1, null, list.ToDictionary(k => k.Kind, k => k.Weight));
				foreach (POIType kind in kinds)
				{
					PointOfInterestKindRule rule = rules.RuleFor(kind);
					if (rule == null)
					{
						continue;
					}
					Assert.That(biome.WeightOf(rule), Is.EqualTo(Mathf.Max(0f, rule.BiomeWeight(entry.Biome))), $"{entry.Biome}: {kind}");
					compared++;
				}
				hosted += list.Count;
			}
			Assert.That(compared, Is.GreaterThan(83 * 90));
			Assert.That(hosted, Is.GreaterThan(83 * 20));

			// Spot checks against the old table, literally.
			Weight("Swamp", POIType.WitchHut, 1f);
			Weight("Plains", POIType.WitchHut, 0f);
			Weight("Farmland", POIType.Village, 1.2f);
			Weight("Farmland", POIType.StiltVillage, 0f);
			Weight("Ocean", POIType.Village, 0f);
			Weight("Lake", POIType.Ruins, 0f);
			Weight("Ocean", POIType.SunkenShip, 1f);
			Weight("Regolith Plain", POIType.Camp, 0f);
			Weight("Glacier", POIType.Camp, 0.2f);
			Weight("Scrubland", POIType.Oasis, 0f);
			Weight("Desert", POIType.NaturalArch, 1.5f);
			Weight("Forest", POIType.HerbGrove, 1.5f);
			Weight("Geyser Basin", POIType.HotSpring, 1f);
			// The POI biome audit (2026-10-25): land-found kinds leave the sea floor, and kinds that stood on every biome keep to their families.
			Weight("Ocean", POIType.Waterfall, 0f);
			Weight("Abyss", POIType.Peak, 0f);
			Weight("Plains", POIType.Peak, 1f);
			Weight("Forest", POIType.WitchHut, 0.5f);
			Weight("Forest", POIType.HuntingLodge, 1.5f);
			Weight("Desert", POIType.HuntingLodge, 0f);
			Weight("Plains", POIType.Windmill, 1f);
			Weight("Desert", POIType.Windmill, 0f);
			Weight("Jungle", POIType.StoneCircle, 0f);
			Weight("Hills", POIType.StoneCircle, 1f);
			Weight("Swamp", POIType.Battlefield, 0f);
			Weight("Plains", POIType.Overhang, 0f);
			Weight("Badlands", POIType.NaturalArch, 1.5f);
			Weight("Mangrove", POIType.NaturalArch, 0f);
			Weight("Farmland", POIType.AbandonedFarm, 1.2f);
			Weight("Marble Palace", POIType.AbandonedFarm, 0f);
		}

		private static void Weight(string biome, POIType kind, float expected)
		{
			BiomeSiteKind[] found = BiomeArtSpec.For(biome).SiteKinds().Where(k => k.Kind == kind).ToArray();
			Assert.That(found.Length > 0 ? found[0].Weight : 0f, Is.EqualTo(expected), $"{biome}: {kind}");
		}

		/// <summary>A biome's file may give a kind its own weight (or none), over the rows'.</summary>
		[Test]
		public void SpecHosts_OverridesOneKind()
		{
			var entry = new BiomeArtSpec.Entry { Biome = "Plains" };
			List<BiomeSiteKind> before = entry.SiteKinds();
			entry.Hosts(POIType.WitchHut, 0.5f).Hosts(POIType.Village, 0f);
			List<BiomeSiteKind> after = entry.SiteKinds();
			Assert.That(after.Single(k => k.Kind == POIType.WitchHut).Weight, Is.EqualTo(0.5f));
			Assert.That(after.Any(k => k.Kind == POIType.Village), Is.False, "0 is not hosted");
			Assert.That(after.Count, Is.EqualTo(before.Count), "one added, one taken away");
			Assert.That(after.Select(k => (int)k.Kind), Is.Ordered, "kind order");
		}

		/// <summary>A biome with no kinds table takes each row's default weight, as on ground with no biome; an empty table hosts nothing.</summary>
		[Test]
		public void PointOfInterestBiome_WithoutKinds_TakesTheRowsDefault()
		{
			PointOfInterestKindRule village = PointOfInterestRules.Default().RuleFor(POIType.Village);
			PointOfInterestKindRule camp = PointOfInterestRules.Default().RuleFor(POIType.Camp);
			Assert.That(new PointOfInterestBiome("x", 1).WeightOf(village), Is.EqualTo(0f));
			Assert.That(new PointOfInterestBiome("x", 1).WeightOf(camp), Is.EqualTo(1f));
			Assert.That(new PointOfInterestBiome("x", 1, null, new Dictionary<POIType, float>()).WeightOf(camp), Is.EqualTo(0f));
			Assert.That(new PointOfInterestBiome("x", 1, null, new Dictionary<POIType, float> { [POIType.Camp] = -2f }).WeightOf(camp), Is.EqualTo(0f));
		}

		// ── Template variants ─────────────────────────────────────────

		/// <summary>
		/// The deterministic pick: for every biome, every kind with templates, every size class, a spread of site races and 24
		/// seeds, the template drawn with the biome template's variant weights (the spec column the authoring writes, read as
		/// the cut reads it: BiomeTemplateArt.SiteVariantWeight by the asset name's variant) is the template drawn by the
		/// templates' own biome lists by the biome's name (PointOfInterestTemplate.WeightFor before 2026-10-16, copied here).
		/// The templates are the assets the authoring writes, split into size runs.
		/// </summary>
		[Test]
		public void SpecSiteVariants_PickWhatTheTemplatesPickedByName()
		{
			// Every size its own asset, so every split name is read back (the authoring's real split is a subset of these).
			List<PointOfInterestTemplateAuthoring.Planned> planned = PointOfInterestTemplateAuthoring.Plan(PointOfInterestTemplateSpecs.All(), (spec, size) => size.ToString());
			Dictionary<POIType, List<PointOfInterestTemplateAuthoring.Planned>> byKind = planned.GroupBy(p => p.Spec.Kind).ToDictionary(g => g.Key, g => g.ToList());
			string[] categories = { string.Empty, "Humanoid", "Beastfolk", "Giant", "Fey", "Beast", "Undead", "Elemental", "Aquatic" };
			int picks = 0, built = 0, differing = 0;
			foreach (BiomeArtSpec.Entry entry in Biomes)
			{
				List<BiomeSiteVariant> variants = entry.SiteVariants();
				List<BiomeSiteStyle> styles = entry.SiteStyles();
				Assert.That(styles, Is.Empty, $"{entry.Biome}: every style at 1 by default");
				differing += variants.Count;
				foreach (KeyValuePair<POIType, List<PointOfInterestTemplateAuthoring.Planned>> kind in byKind)
				{
					List<PointOfInterestTemplateAuthoring.Planned> list = kind.Value;
					for (int size = 0; size < 3; size++)
					{
						foreach (string category in categories)
						{
							var before = new float[list.Count];
							var after = new float[list.Count];
							float totalBefore = 0f, totalAfter = 0f;
							for (int i = 0; i < list.Count; i++)
							{
								PointOfInterestTemplateAuthoring.Planned t = list[i];
								before[i] = OldWeightFor(t, entry.Biome, size, category);
								float biome = BiomeTemplateArt.SiteVariantWeight(variants, styles, t.Spec.Kind, PointOfInterestTemplate.VariantOf(t.Name, t.Spec.Kind),
									t.Spec.Style, t.Spec.DefaultBiomeWeight);
								after[i] = PointOfInterestTemplate.Fit(t.Spec.Weight, biome, t.Small, t.Medium, t.Large, Array.Empty<string>(), t.Spec.RaceCategories,
									size, string.Empty, category);
								Assert.That(after[i], Is.EqualTo(before[i]), $"{entry.Biome}: {t.Name} at size {size}, race category '{category}'");
								totalBefore += before[i];
								totalAfter += after[i];
							}
							for (int seed = 0; seed < 24; seed++)
							{
								int siteSeed = PointOfInterestPlanner.SiteSeed(1234, seed * 7919 + (int)kind.Key);
								PointOfInterestTemplateAuthoring.Planned? old = PickOf(list, before, totalBefore, siteSeed);
								PointOfInterestTemplateAuthoring.Planned? now = PickOf(list, after, totalAfter, siteSeed);
								Assert.That(now?.Name, Is.EqualTo(old?.Name), $"{entry.Biome}: {kind.Key} seed {siteSeed}");
								picks++;
								built += now != null ? 1 : 0;
							}
						}
					}
				}
			}
			Assert.That(picks, Is.GreaterThan(1000000));
			Assert.That(built, Is.GreaterThan(picks / 4), "most draws build something");
			Assert.That(differing, Is.GreaterThan(83), "the biomes weigh variants of their own");
			// Spot checks: what the spec writes, from the templates' biome lists.
			Assert.That(BiomeArtSpec.For("Desert").SiteVariants(), Does.Contain(new BiomeSiteVariant(POIType.Village, "Stone", 2f)));
			Assert.That(BiomeArtSpec.For("Desert").SiteVariants(), Does.Contain(new BiomeSiteVariant(POIType.Village, "Timber", 0.1f)));
			Assert.That(BiomeArtSpec.For("Plains").SiteVariants().Any(v => v.Kind == POIType.Village), Is.False, "the plains weigh every village as anywhere");
		}

		// PointOfInterestTemplate.WeightFor before 2026-10-16, on a planned asset (no race keys: the authoring writes none).
		private static float OldWeightFor(PointOfInterestTemplateAuthoring.Planned t, string biomeName, int sizeClass, string raceCategory)
		{
			bool size = sizeClass == 0 ? t.Small : sizeClass == 1 ? t.Medium : t.Large;
			if (!size)
			{
				return 0f;
			}
			float biome = t.Spec.DefaultBiomeWeight;
			if (!string.IsNullOrEmpty(biomeName))
			{
				foreach (PointOfInterestBiomeWeight entry in t.Spec.BiomeWeights)
				{
					if (string.Equals(entry.Biome, biomeName, StringComparison.OrdinalIgnoreCase))
					{
						biome = entry.Weight;
					}
				}
			}
			float fit = 1f;
			if (t.Spec.RaceCategories.Count > 0 && !(raceCategory.Length > 0 && t.Spec.RaceCategories.Any(c => string.Equals(c, raceCategory, StringComparison.OrdinalIgnoreCase))))
			{
				fit = 0f;
			}
			return Mathf.Max(0f, t.Spec.Weight) * Mathf.Max(0f, biome) * fit;
		}

		private static PointOfInterestTemplateAuthoring.Planned? PickOf(List<PointOfInterestTemplateAuthoring.Planned> list, float[] weights, float total, int seed)
		{
			var boxed = list.Select(p => (object)p).ToList();
			object picked = PointOfInterestTemplateLibrary.Pick(boxed, weights, total, seed);
			return picked != null ? (PointOfInterestTemplateAuthoring.Planned?)picked : null;
		}

		/// <summary>
		/// A template's variant is read back from every name the authoring gives it ("Camp - Hide Tents (Medium-Large)" → Hide
		/// Tents), every spec's variant and kind pair is unique, and a name of another form is its own variant.
		/// </summary>
		[Test]
		public void TemplateVariant_IsReadBackFromItsAssetName()
		{
			List<PointOfInterestTemplateSpec> specs = PointOfInterestTemplateSpecs.All();
			Assert.That(specs.Select(s => (s.Kind, s.Variant.ToLowerInvariant())).Distinct().Count(), Is.EqualTo(specs.Count), "kind and variant name a template");
			foreach (Func<PointOfInterestTemplateSpec, int, string> split in new Func<PointOfInterestTemplateSpec, int, string>[]
			{
				null, (spec, size) => size.ToString(), (spec, size) => size == 0 ? "a" : "b", (spec, size) => size == 2 ? "a" : "b",
			})
			{
				foreach (PointOfInterestTemplateAuthoring.Planned p in PointOfInterestTemplateAuthoring.Plan(specs, split))
				{
					Assert.That(PointOfInterestTemplate.VariantOf(p.Name, p.Spec.Kind), Is.EqualTo(p.Spec.Variant), p.Name);
				}
			}
			Assert.That(PointOfInterestTemplate.VariantOf("Camp - Hide Tents (Medium-Large)", POIType.Camp), Is.EqualTo("Hide Tents"));
			Assert.That(PointOfInterestTemplate.VariantOf("My Hall (Old)", POIType.Village), Is.EqualTo("My Hall (Old)"));
			Assert.That(PointOfInterestTemplate.VariantOf("Town - Stone", POIType.Village), Is.EqualTo("Town - Stone"), "another kind's prefix is kept");
		}

		/// <summary>A biome's variant entry replaces the template's weight elsewhere; its style list multiplies by style; unlisted stay as they were.</summary>
		[Test]
		public void SiteVariantWeight_ReadsTheVariantThenTheStyle()
		{
			var variants = new List<BiomeSiteVariant> { new BiomeSiteVariant(POIType.Village, "stone", 3f), new BiomeSiteVariant(POIType.Village, "Stone", 9f) };
			var styles = new List<BiomeSiteStyle> { new BiomeSiteStyle(BiomeStructureStyle.Stone, 0.5f) };
			Assert.That(BiomeTemplateArt.SiteVariantWeight(variants, null, POIType.Village, "Stone", "stone", 0.3f), Is.EqualTo(3f), "first entry, case ignored");
			Assert.That(BiomeTemplateArt.SiteVariantWeight(variants, styles, POIType.Village, "Stone", "stone", 0.3f), Is.EqualTo(1.5f));
			Assert.That(BiomeTemplateArt.SiteVariantWeight(variants, styles, POIType.Town, "Stone", "stone", 0.3f), Is.EqualTo(0.15f), "another kind's variant");
			Assert.That(BiomeTemplateArt.SiteVariantWeight(variants, styles, POIType.Village, "Timber", "timber", 1f), Is.EqualTo(1f));
			Assert.That(BiomeTemplateArt.SiteVariantWeight(variants, styles, POIType.Village, "Timber", "orcish", 1f), Is.EqualTo(1f), "a race's style is no style");
			Assert.That(BiomeSiteStyle.Parse("Hide"), Is.EqualTo(BiomeStructureStyle.Hide));
		}

		// ── Weathering ────────────────────────────────────────────────

		/// <summary>
		/// The finish of every piece in every biome, for every kind, decay and roll, with the biome's weathering as the spec writes
		/// it, is what PropsFeature.FinishFor gave it by the biome's name before 2026-10-16 (its code copied here); and with no biome.
		/// </summary>
		[Test]
		public void SpecWeathering_FinishesEveryPieceAsTheNameDid()
		{
			var kinds = (POIType[])Enum.GetValues(typeof(POIType));
			var modes = (PointOfInterestFinish[])Enum.GetValues(typeof(PointOfInterestFinish));
			var names = Biomes.Select(e => e.Biome).Concat(new string[] { null, string.Empty }).ToList();
			int compared = 0;
			var finishes = new HashSet<StructureFinish>();
			foreach (string name in names)
			{
				BiomeWeathering weathering = name != null && BiomeArtSpec.For(name) != null ? BiomeArtSpec.For(name).Weathering() : BiomeArtSpec.WeatheringByName(name);
				foreach (POIType kind in kinds)
				{
					foreach (PointOfInterestFinish mode in modes)
					{
						if (mode != PointOfInterestFinish.Auto && kind != POIType.Ruins)
						{
							continue; // a forced finish reads neither kind nor biome: one kind is enough
						}
						for (int d = 0; d <= 10; d++)
						{
							float decay = d / 10f;
							for (int r = 0; r <= 50; r++)
							{
								float roll = r / 50f;
								StructureFinish old = OldFinishFor(mode, 0.6f, kind, name, decay, roll);
								StructureFinish now = PropsFeature.FinishFor(mode, 0.6f, kind, weathering, decay, roll);
								// The old inline expression and the extracted float-returning ChanceAt
								// round differently at an exact threshold on Mono. Away from that
								// single-precision boundary the legacy decisions must match exactly.
								if (now != old && Mathf.Abs(roll - weathering.ChanceAt(decay)) > 1e-7f)
								{
									Assert.Fail($"{name ?? "(no biome)"}: {kind} {mode} decay {decay} roll {roll}: {now}, was {old}");
								}
								finishes.Add(now);
								compared++;
							}
						}
					}
				}
			}
			Assert.That(compared, Is.GreaterThan(5000000));
			Assert.That(finishes, Is.EquivalentTo(Enum.GetValues(typeof(StructureFinish))), "every finish comes up");
			Assert.That(BiomeArtSpec.For("Swamp").Weathering(), Is.EqualTo(new BiomeWeathering(BiomeStructureFinish.Algae, 0.3f, 0.6f)));
			Assert.That(BiomeArtSpec.For("Desert").Weathering().Finish, Is.EqualTo(BiomeStructureFinish.None));
			Assert.That(BiomeArtSpec.For("Forest").Weathering(), Is.EqualTo(new BiomeWeathering(BiomeStructureFinish.Mossy, 0f, 1f)));
			Assert.That(BiomeArtSpec.For("Plains").Weathering(), Is.EqualTo(new BiomeWeathering(BiomeStructureFinish.Mossy, 0f, 0.5f)));
		}

		private static readonly string[] OldWet = { "Swamp", "Mangrove", "Bog", "Wetland", "Marsh", "Estuary" };
		private static readonly string[] OldDamp = { "Forest", "Woodland", "Jungle", "Rainforest", "Taiga", "Bamboo", "Valley", "Karst", "Temperate", "Lake", "River" };
		private static readonly string[] OldDry = { "Desert", "Badlands", "Salt", "Arid", "Dust", "Wasteland", "Savanna", "Scrub", "Volcanic", "Molten", "Sulphur", "Oasis" };

		// PropsFeature.FinishFor before 2026-10-16, word for word.
		private static StructureFinish OldFinishFor(PointOfInterestFinish mode, float chance, POIType kind, string biome, float decay, float roll)
		{
			switch (mode)
			{
				case PointOfInterestFinish.None:
					return StructureFinish.None;
				case PointOfInterestFinish.Mossy:
					return roll < chance ? StructureFinish.Mossy : StructureFinish.None;
				case PointOfInterestFinish.Charred:
					return roll < chance ? StructureFinish.Charred : StructureFinish.None;
				case PointOfInterestFinish.Algae:
					return roll < chance ? StructureFinish.Algae : StructureFinish.None;
			}
			if (PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Underwater))
			{
				return StructureFinish.Algae;
			}
			if (kind == POIType.Battlefield && roll < 0.5f)
			{
				return StructureFinish.Charred;
			}
			biome ??= string.Empty;
			if (Mentions(biome, OldWet))
			{
				return roll < 0.3f + 0.6f * decay ? StructureFinish.Algae : StructureFinish.None;
			}
			if (Mentions(biome, OldDry))
			{
				return StructureFinish.None;
			}
			float moss = decay * (Mentions(biome, OldDamp) ? 1f : 0.5f);
			PointOfInterestGroup group = PointOfInterestKinds.Info(kind).Group;
			if (group == PointOfInterestGroup.Ruins || group == PointOfInterestGroup.Dead)
			{
				moss = Mathf.Max(moss, 0.55f + 0.4f * decay);
			}
			return roll < moss ? StructureFinish.Mossy : StructureFinish.None;
		}

		private static bool Mentions(string biome, string[] words)
		{
			foreach (string word in words)
			{
				if (biome.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>A charred biome chars by its chance and gives ruins no moss floor; the finish enum mirrors the kit's.</summary>
		[Test]
		public void Weathering_CharredAndTheMirror()
		{
			var charred = new BiomeWeathering(BiomeStructureFinish.Charred, 0.2f, 0.5f);
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Ruins, charred, 0.4f, 0.39f), Is.EqualTo(StructureFinish.Charred));
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Ruins, charred, 0.4f, 0.41f), Is.EqualTo(StructureFinish.None));
			Assert.That(Enum.GetNames(typeof(BiomeStructureFinish)), Is.EqualTo(Enum.GetNames(typeof(StructureFinish))));
			foreach (string name in Enum.GetNames(typeof(StructureFinish)))
			{
				Assert.That(Convert.ToInt32(Enum.Parse(typeof(BiomeStructureFinish), name)), Is.EqualTo((int)Enum.Parse(typeof(StructureFinish), name)), name);
			}
		}

		// ── Crops ─────────────────────────────────────────────────────

		/// <summary>Every biome's fields grow wheat by default (the prefab every field grew), and one crop is grown without a draw.</summary>
		[Test]
		public void SpecFieldCrops_AreWheat_AndTheDrawLeavesOneCropAlone()
		{
			Assert.That(BiomeArtSpec.WheatCrop, Is.EqualTo(ProceduralArtCatalogue.DetailPrefab("GrassWheat")));
			foreach (BiomeArtSpec.Entry entry in Biomes)
			{
				Assert.That(entry.FieldCrops(), Is.EqualTo(new List<(string, float)> { ("Detail_GrassWheat", 1f) }), entry.Biome);
			}
			for (int seed = 0; seed < 200; seed++)
			{
				Assert.That(PointOfInterestFields.CropIndex(new[] { 1f }, seed, seed % 7), Is.EqualTo(0));
				Assert.That(PointOfInterestFields.CropIndex(new[] { 0f }, seed, 0), Is.EqualTo(-1));
			}
			Assert.That(PointOfInterestFields.CropIndex(Array.Empty<float>(), 1, 0), Is.EqualTo(-1));
			var counts = new int[3];
			for (int seed = 0; seed < 3000; seed++)
			{
				int pick = PointOfInterestFields.CropIndex(new[] { 1f, 0f, 3f }, seed, seed % 5);
				Assert.That(pick, Is.EqualTo(PointOfInterestFields.CropIndex(new[] { 1f, 0f, 3f }, seed, seed % 5)), "seeded");
				counts[pick]++;
			}
			Assert.That(counts[1], Is.EqualTo(0), "a crop of no weight is never grown");
			Assert.That(counts[2], Is.InRange(2000, 2500), "by weight");
		}

		// ── Structure palette ─────────────────────────────────────────

		/// <summary>The palette's style and material enums are the kit's, member for member.</summary>
		[Test]
		public void PaletteEnums_MirrorTheKits()
		{
			Assert.That(Enum.GetNames(typeof(BiomeStructureStyle)), Is.EqualTo(Enum.GetNames(typeof(StructureStyle))));
			Assert.That(Enum.GetNames(typeof(BiomeStructureMaterial)), Is.EqualTo(Enum.GetNames(typeof(StructureMaterial))));
			foreach (string name in Enum.GetNames(typeof(StructureMaterial)))
			{
				Assert.That(Convert.ToInt32(Enum.Parse(typeof(BiomeStructureMaterial), name)), Is.EqualTo((int)Enum.Parse(typeof(StructureMaterial), name)), name);
			}
			foreach (string name in Enum.GetNames(typeof(StructureStyle)))
			{
				Assert.That(Convert.ToInt32(Enum.Parse(typeof(BiomeStructureStyle), name)), Is.EqualTo((int)Enum.Parse(typeof(StructureStyle), name)), name);
			}
		}

		/// <summary>
		/// The kit palette is every style and role once, tinted as the kit tints each material (StructureSurfaces.TintOf); every
		/// biome's spec palette is the kit's; and a palette builds a style as the kit only while that style's entries are the kit's.
		/// </summary>
		[Test]
		public void SpecPalette_IsTheKitsOwnMaterials_ForEveryBiome()
		{
			BiomeStructurePalette kit = BiomeStructurePalette.Kit();
			Assert.That(kit.Surfaces.Count, Is.EqualTo(BiomeStructurePalette.Styles.Length * BiomeStructurePalette.Roles.Length));
			Assert.That(kit.Surfaces.Select(s => (s.Style, s.Role)).Distinct().Count(), Is.EqualTo(kit.Surfaces.Count));
			foreach (BiomeStructureMaterial material in Enum.GetValues(typeof(BiomeStructureMaterial)))
			{
				Assert.That(BiomeStructurePalette.KitTint(material), Is.EqualTo(StructureSurfaces.TintOf((StructureMaterial)(int)material)), material.ToString());
			}
			foreach (BiomeStructureSurface surface in kit.Surfaces)
			{
				Assert.That(surface.Tint, Is.EqualTo(StructureSurfaces.TintOf((StructureMaterial)(int)surface.Material)), surface.Signature());
				Assert.That(surface.Surface, Is.Empty);
			}
			foreach (BiomeStructureStyle style in BiomeStructurePalette.Styles)
			{
				Assert.That(kit.BuildsAsKit(style), Is.True, style.ToString());
				Assert.That(new BiomeStructurePalette().BuildsAsKit(style), Is.True, "an empty palette is the kit's");
			}
			foreach (BiomeArtSpec.Entry entry in Biomes)
			{
				Assert.That(entry.StructurePalette().Signature(), Is.EqualTo(kit.Signature()), entry.Biome);
			}
			var adobe = new BiomeArtSpec.Entry { Biome = "Desert" }.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth).StructurePalette();
			Assert.That(adobe.BuildsAsKit(BiomeStructureStyle.Timber), Is.False);
			Assert.That(adobe.BuildsAsKit(BiomeStructureStyle.Stone), Is.True);
			Assert.That(adobe.For(BiomeStructureStyle.Timber, BiomeStructureRole.Wall).Material, Is.EqualTo(BiomeStructureMaterial.Earth));
			Assert.That(adobe.Surfaces.Count, Is.EqualTo(kit.Surfaces.Count), "replaced in place");
			BiomeStructurePalette surfaced = BiomeStructurePalette.Kit();
			surfaced.Surfaces[0] = new BiomeStructureSurface(surfaced.Surfaces[0].Style, surfaced.Surfaces[0].Role, surfaced.Surfaces[0].Material, surfaced.Surfaces[0].Tint, "desert-1");
			Assert.That(surfaced.BuildsAsKit(surfaced.Surfaces[0].Style), Is.False, "a surface variant is not the kit's");
		}

		/// <summary>
		/// The kit palette's materials are the builders' (literal oracles from StructurePieces): the cottage's plaster walls,
		/// thatch or shingle by variant and fieldstone plinth; the stone house's ashlar under thatch or shingle; the plastered
		/// storey over a stone ground floor; the tent's canvas over earth with a dark flap; a house's plank door by default.
		/// </summary>
		[Test]
		public void KitPalette_IsWhatTheBuildersBuild()
		{
			string art = Path.Combine(WorldFolder, "ProceduralArt");
			string buildings = File.ReadAllText(Path.Combine(art, "StructurePieces.Buildings.cs"));
			string camp = File.ReadAllText(Path.Combine(art, "StructurePieces.Camp.cs"));
			string common = File.ReadAllText(Path.Combine(art, "StructurePieces.Common.cs"));
			Assert.That(buildings, Does.Contain("WallMaterial = StructureMaterial.Plaster, RoofMaterial = variant == 0 ? StructureMaterial.Thatch : StructureMaterial.Timber,"));
			Assert.That(buildings, Does.Contain("PlinthMaterial = StructureMaterial.Fieldstone, Frame = true"));
			Assert.That(buildings, Does.Contain("WallMaterial = StructureMaterial.Stone, RoofMaterial = d.Variant == 0 ? StructureMaterial.Thatch : StructureMaterial.Timber,"));
			Assert.That(buildings, Does.Contain("UpperWallMaterial = stone ? StructureMaterial.Plaster"));
			Assert.That(camp, Does.Contain("StructureMaterial cloth = dyed ? StructureMaterial.Dyed : StructureMaterial.Canvas;"));
			Assert.That(camp, Does.Contain("WallMaterial = cloth, RoofMaterial = cloth, PlinthMaterial = StructureMaterial.Earth,"));
			Assert.That(camp, Does.Contain("DoorMaterial = StructureMaterial.Ash"));
			Assert.That(common, Does.Contain("public StructureMaterial DoorMaterial;"));
			Assert.That((StructureMaterial)0, Is.EqualTo(StructureMaterial.Timber), "a house's door left unset is timber planks");

			void Kit(BiomeStructureStyle style, BiomeStructureRole role, BiomeStructureMaterial material)
				=> Assert.That(BiomeStructurePalette.KitMaterial(style, role), Is.EqualTo(material), $"{style} {role}");
			Kit(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster);
			Kit(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch);
			Kit(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Timber);
			Kit(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone);
			Kit(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber);
			Kit(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber);
			Kit(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Stone);
			Kit(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster);
			Kit(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch);
			Kit(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Timber);
			Kit(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone);
			Kit(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Canvas);
			Kit(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas);
			Kit(BiomeStructureStyle.Hide, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth);
			Kit(BiomeStructureStyle.Hide, BiomeStructureRole.Door, BiomeStructureMaterial.Ash);
		}

		// ── The cut reads the template ────────────────────────────────

		/// <summary>
		/// The cut's points of interest read the biome template, never its name: the planner weighs kinds by the template's
		/// list (PointOfInterestBiome.Kinds), the pick by its variants, the props by its weathering and palette, the fields by its
		/// crops; the old by-name readers (a row's BiomeWeight, the rows' KindWeightsFor, a template's BiomeWeightByName) are
		/// called only in BiomeTemplateArt's logged fallbacks and the tables' own definitions; and no file under PointsOfInterest
		/// reads a biome's name (biome.name, the planner's BiomeName, the props' keyword lists) or writes a biome's name as a
		/// literal, apart from the naming and the name tables the authoring reads.
		/// </summary>
		[Test]
		public void PointsOfInterest_ReadTheTemplate_NotTheBiomesName()
		{
			string poi = Path.Combine(WorldFolder, "PointsOfInterest");
			string Code(string relative) => CodeOnly(File.ReadAllText(Path.Combine(poi, relative)));
			Assert.That(Code("PointOfInterestStage.cs"), Does.Contain("BiomeTemplateArt.SiteKinds(biome, rules, breathable, liquidWater)"));
			Assert.That(Regex.Matches(Code("PointOfInterestStage.cs"), Regex.Escape("library.Pick(record, BiomeTemplateArt.SiteVariantWeights(biome),")).Count, Is.EqualTo(1));
			// 2026-10-25 (I2n): the planned sites' pick weighs a linked cave's two templates merged, every other site its biome's.
			Assert.That(Code("PointOfInterestStage.cs"), Does.Contain("Func<PointOfInterestTemplate, float> weights = BiomeTemplateArt.SiteVariantWeights(biome);"));
			// 2026-10-25 (NEW-POI-KINDS, decision 96): a template needing liquid water drops out on a world with none.
			Assert.That(Code("PointOfInterestStage.cs"), Does.Contain("library.Pick(record, PointOfInterestTemplate.WaterFit(weights, liquidWater), RaceCategoryOf(record.Race))"));
			Assert.That(Code("PropsFeature.cs"), Does.Contain("BiomeTemplateArt.Weathering(siteBiome)"));
			Assert.That(Code("PropsFeature.cs"), Does.Contain("{ Palette = palette }"));
			Assert.That(Code(Path.Combine("Templates", "StructureKitPieceSource.cs")), Does.Contain("PrefabFor(piece, variant, request.Finish, request.Palette, request.AllowLocal)"));
			Assert.That(Code(Path.Combine("Features", "PointOfInterestFields.cs")), Does.Contain("BiomeTemplateArt.FieldCrops(biome)"));
			Assert.That(Code("PointOfInterestPlanner.cs"), Does.Contain("biome.WeightOf(rule)"));

			var byNameCalls = new Regex(@"\.BiomeWeight\(\s*(?!rule\b|hotRule\b)|\bKindWeightsFor\(|\bBiomeWeightByName\(", RegexOptions.CultureInvariant);
			var nameReads = new Regex(@"[bB]iome\w*\s*\??\.\s*name\b|BiomeAt\b[^;]*\.\s*name\b|\bBiomeName\(|Biomes\[[^\]]*\]\??\.Name\b|\bMentions\(\s*biome", RegexOptions.CultureInvariant);
			// The props' old keyword lists of wet, dry and damp country (a name table: allowed only among the tables).
			var keywordLists = new Regex(@"\b(Wet|Dry|Damp)\s*=\s*\{", RegexOptions.CultureInvariant);
			foreach (string control in new[]
			{
				"return rule.BiomeWeight(biomeName);", "bool isHot = hotRule.BiomeWeight(biome) > 0f;", "foreach (var k in rules.KindWeightsFor(name))",
			})
			{
				Assert.That(byNameCalls.IsMatch(control), Is.True, "control: " + control);
			}
			Assert.That(byNameCalls.IsMatch("public float BiomeWeight(PointOfInterestKindRule rule, int cell)"), Is.False, "the planner's own cell weight");
			Assert.That(byNameCalls.IsMatch("if (BiomeWeight(rule, i) <= 0f)"), Is.False);
			foreach (string control in new[]
			{
				"string biome = context.BiomeAt?.Invoke(record.Position.x, record.Position.z)?.name;",
				"library.Pick(record, biome != null ? biome.name : null, string.Empty);",
				"string biome = BiomeName(G.CellAt(x, z));",
			})
			{
				Assert.That(nameReads.IsMatch(control), Is.True, "control: " + control);
			}
			Assert.That(keywordLists.IsMatch("private static readonly string[] Wet = { \"Swamp\" };"), Is.True, "control");
			var definitions = new HashSet<string> { "PointOfInterestKindRule.cs", "PointOfInterestRules.cs", "PointOfInterestTemplate.cs", "PointOfInterestTemplateSpecs.cs" };
			// The naming reads a biome's name to word a site's name and description; it decides no content.
			var naming = new HashSet<string> { "PointOfInterestNaming.cs" };
			// The tables of biome names the authoring reads (the kinds' and variants' default biomes, the gathering catalogue's).
			var tables = new HashSet<string> { "PointOfInterestRules.cs", "PointOfInterestTemplateSpecs.cs", "GatheringResourceCatalogue.cs" };
			var biomeNames = new HashSet<string>(Biomes.Select(e => e.Biome));
			// A rock's broken shape and the cave shaper's own name share a word with a biome's.
			var notBiomes = new HashSet<string> { "Scree", "Cave" };
			string[] files = Directory.GetFiles(poi, "*.cs", SearchOption.AllDirectories);
			Assert.That(files.Length, Is.GreaterThan(40));
			var found = new List<string>();
			foreach (string file in files)
			{
				string name = Path.GetFileName(file);
				foreach (string line in CodeOnly(File.ReadAllText(file)).Split('\n'))
				{
					if (!definitions.Contains(name) && byNameCalls.IsMatch(line))
					{
						found.Add($"{name}: by-name weight: {line.Trim()}");
					}
					// A scene biome's name rides on the planner's PointOfInterestBiome as a label for notes; it decides nothing.
					if (!naming.Contains(name) && nameReads.IsMatch(line) && !line.Contains("new PointOfInterestBiome("))
					{
						found.Add($"{name}: reads a biome's name: {line.Trim()}");
					}
					if (!naming.Contains(name) && !tables.Contains(name) && keywordLists.IsMatch(line))
					{
						found.Add($"{name}: keeps a keyword list of biome names: {line.Trim()}");
					}
					if (!naming.Contains(name) && !tables.Contains(name))
					{
						foreach (Match literal in Regex.Matches(line, "\"([^\"\\n]*)\""))
						{
							string value = literal.Groups[1].Value;
							if (biomeNames.Contains(value) && !notBiomes.Contains(value))
							{
								found.Add($"{name}: names the biome '{value}': {line.Trim()}");
							}
						}
					}
				}
			}
			Assert.IsEmpty(found, "points-of-interest code deciding by a biome's name: " + string.Join(" | ", found));

			// The by-name readers outside PointsOfInterest: only BiomeTemplateArt's fallbacks and the spec.
			string terrain = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "Terrain", "BiomeTemplateArt.cs")));
			Assert.That(terrain, Does.Contain(".KindWeightsFor(biome.name)"));
			Assert.That(terrain, Does.Contain("template.BiomeWeightByName(name)"));
		}

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
