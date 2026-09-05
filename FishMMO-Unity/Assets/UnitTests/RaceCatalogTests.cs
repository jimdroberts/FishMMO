using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.NameGeneration.Editor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The race catalogue as a whole: every race has a category and lives in that
	/// category's folder, keys and IDs are unique, every category is served by a
	/// title pool, pools merge race-first without duplicates, and — the reason the
	/// catalogue was widened — seeded names and titles rarely collide.
	/// </summary>
	[TestFixture]
	public class RaceCatalogTests
	{
		private static readonly string[] KnownCategories =
		{
			"Humanoid", "Giant", "Fey", "Beastfolk", "Beast", "Draconic", "Aquatic", "Undead", "Construct", "Elemental", "Outsider", "Plant", "Aberration",
		};

		[OneTimeSetUp]
		public void LoadAssets()
		{
			NamingTemplateEditorLoader.Reload();
			Assume.That(RaceRegistry.Count, Is.GreaterThan(0));
		}

		// ── Catalogue shape ──────────────────────────────────────────

		[Test]
		public void Catalogue_IsWide_AndEveryRaceIsCategorised()
		{
			Assert.GreaterOrEqual(RaceRegistry.Count, 250, "The catalogue was widened to 250+ races.");
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				RaceTemplate race = RaceRegistry.Get(key);
				Assert.IsFalse(string.IsNullOrWhiteSpace(race.Category), $"{race.name} has no category.");
				CollectionAssert.Contains(KnownCategories, race.Category.Trim(), $"{race.name}: unknown category '{race.Category}'.");
			}
			CollectionAssert.AreEquivalent(KnownCategories, RaceRegistry.Categories(), "Every category is populated.");
		}

		[Test]
		public void EveryRaceAsset_LivesInItsCategoryFolder()
		{
			foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(RaceTemplate)}"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				RaceTemplate race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(path);
				Assume.That(race, Is.Not.Null);
				StringAssert.Contains($"/Races/{race.Category.Trim()}/", path, $"{race.name} is filed under the wrong folder.");
			}
		}

		[Test]
		public void RaceKeys_AndCachedIDs_AreUnique()
		{
			int assets = AssetDatabase.FindAssets($"t:{nameof(RaceTemplate)}").Length;
			Assert.AreEqual(assets, RaceRegistry.Count, "Two race assets resolving to one naming key would silently shadow each other.");
			var ids = new HashSet<int>();
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				Assert.IsTrue(ids.Add(RaceRegistry.IDOf(RaceRegistry.Get(key))), $"{key}: cached ID collides with another race.");
			}
		}

		[Test]
		public void RacesInCategory_IsCaseInsensitive_AndCoversTheCatalogue()
		{
			int total = 0;
			foreach (string category in RaceRegistry.Categories())
			{
				IReadOnlyList<string> races = RaceRegistry.RacesInCategory(category);
				Assert.Greater(races.Count, 0, category);
				CollectionAssert.AreEqual(races, RaceRegistry.RacesInCategory(category.ToUpperInvariant()));
				total += races.Count;
			}
			Assert.AreEqual(RaceRegistry.Count, total);
			Assert.IsEmpty(RaceRegistry.RacesInCategory(""));
		}

		// ── Title pools ──────────────────────────────────────────────

		[Test]
		public void EveryCategory_HasItsOwnPool_AndTheCommonPoolServesAll()
		{
			Assert.GreaterOrEqual(TitlePoolRegistry.Count, KnownCategories.Length + 1, "One pool per category plus the common pool.");
			foreach (string category in KnownCategories)
			{
				List<TitlePoolTemplate> pools = TitlePoolRegistry.PoolsFor(category);
				Assert.GreaterOrEqual(pools.Count, 2, $"{category}: expected its own pool and the common pool.");
				Assert.IsTrue(pools.Exists(p => p.AppliesToAll), $"{category}: the common pool must apply.");
				Assert.IsTrue(pools.Exists(p => !p.AppliesToAll && p.AppliesTo(category.ToLowerInvariant())), $"{category}: category match is case-insensitive.");
			}
		}

		[Test]
		public void MergedTitles_KeepTheRacesOwnFirst_AndDropDuplicates()
		{
			RaceTemplate wight = RaceRegistry.Get("wight");
			RaceTitles own = wight.Naming.RuntimeTitles;
			RaceTitles merged = TitlePoolRegistry.TitlesFor(wight);
			Assert.Greater(merged.Epithet.Length, own.Epithet.Length);
			for (int i = 0; i < own.Epithet.Length; i++)
			{
				Assert.AreEqual(own.Epithet[i], merged.Epithet[i], "The race's own titles lead the merged list.");
			}
			var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			foreach (string epithet in merged.Epithet)
			{
				Assert.IsTrue(seen.Add(epithet), $"duplicate epithet '{epithet}'");
			}
			Assert.AreSame(merged, TitlePoolRegistry.TitlesFor(wight), "Merges are cached until the registries change.");
			TitlePoolRegistry.Invalidate();
			Assert.AreNotSame(merged, TitlePoolRegistry.TitlesFor(wight));
		}

		[Test]
		public void EveryRace_DrawsFromAtLeastSixtyTitles()
		{
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				Assert.IsTrue(RaceRegistry.TryGetTitles(key, out RaceTitles titles));
				int total = Count(titles.Honorific) + Count(titles.HonorificMasculine) + Count(titles.HonorificFeminine)
					+ Count(titles.Epithet) + Count(titles.Rank) + Count(titles.Legend) + Count(titles.Occupational);
				Assert.GreaterOrEqual(total, 60, $"{key}: only {total} titles after the pool merge.");
			}
		}

		// ── Collisions ───────────────────────────────────────────────

		[Test]
		public void SeededFullNames_RarelyCollide_InAnyRace()
		{
			const int draws = 300;
			var generator = new NameGenerator();
			double worst = 0; string worstRace = "";
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				var names = new HashSet<string>();
				for (int i = 0; i < draws; i++)
				{
					names.Add(generator.Generate(new NameRequest { Race = key, IncludeFamilyName = true, RegionSeed = "collision-test", Index = i }).FullName);
				}
				double rate = 1.0 - names.Count / (double)draws;
				if (rate > worst) { worst = rate; worstRace = key; }
			}
			Assert.LessOrEqual(worst, 0.03, $"{worstRace}: {worst:P1} of {draws} seeded full names were duplicates.");
		}

		[Test]
		public void SeededTitles_MostlyDiffer_InAnyRace()
		{
			const int draws = 300;
			var generator = new NameGenerator();
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				var titles = new HashSet<string>();
				for (int i = 0; i < draws; i++)
				{
					titles.Add(generator.Generate(new NameRequest { Race = key, RegionSeed = "title-test", Index = i }).Title);
				}
				Assert.GreaterOrEqual(titles.Count, draws * 0.65, $"{key}: only {titles.Count} distinct titles in {draws} draws.");
			}
		}

		[Test]
		public void PoolTitles_ReplayIdentically_WhateverTheLoadOrder()
		{
			string first = new NameGenerator(1).Generate(new NameRequest { Race = "wight", Register = TitleRegister.Mythic, RegionSeed = "order", Index = 5 }).Title;
			NamingTemplateEditorLoader.Reload();
			string second = new NameGenerator(2).Generate(new NameRequest { Race = "wight", Register = TitleRegister.Mythic, RegionSeed = "order", Index = 5 }).Title;
			Assert.AreEqual(first, second);
		}

		private static int Count(string[] array) => array?.Length ?? 0;
	}
}
