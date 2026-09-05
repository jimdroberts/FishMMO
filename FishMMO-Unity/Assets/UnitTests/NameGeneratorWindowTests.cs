using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.NameGeneration.Editor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Drives the Name Generator window the way a user does — pick a category,
	/// set the options, press the toolbar buttons — and asserts on the visual
	/// tree and status bar it produces. <c>BuildUI</c> mounts the real UXML into
	/// a detached root, so none of this needs a window to be shown.
	/// </summary>
	[TestFixture]
	public class NameGeneratorWindowTests
	{
		private NameGeneratorWindow window;
		private VisualElement root;

		[SetUp]
		public void SetUp()
		{
			NamingTemplateEditorLoader.EnsureLoaded();
			window = ScriptableObject.CreateInstance<NameGeneratorWindow>();
			root = new VisualElement();
			window.BuildUI(root);
		}

		[TearDown]
		public void TearDown()
		{
			if (window != null)
			{
				UnityEngine.Object.DestroyImmediate(window);
			}
			window = null;
			root = null;
		}

		// ── Helpers ───────────────────────────────────────────────────

		private Button Btn(string name) => root.Q<Button>(name);

		private VisualElement ResultsList => root.Q<VisualElement>("results-list");

		/// <summary>Result rows only — the empty-state placeholder is not one.</summary>
		private List<VisualElement> Rows() =>
			ResultsList.Children().Where(c => c.ClassListContains("result-row")).ToList();

		private void SetCount(int n) => root.Q<IntegerField>("count-field").value = n;

		private void SetUnique(bool on) => root.Q<Toggle>("unique-toggle").value = on;

		private void SetRegionSeed(string seed) => root.Q<TextField>("region-seed-field").value = seed;

		private SearchableDropdownField RaceField => root.Q<SearchableDropdownField>("race-field");

		private SearchableDropdownField BiomeField => root.Q<SearchableDropdownField>("biome-field");

		private static readonly NameGeneratorWindow.Category[] AllCategories =
		{
			NameGeneratorWindow.Category.Characters,
			NameGeneratorWindow.Category.Cities,
			NameGeneratorWindow.Category.Dungeons,
			NameGeneratorWindow.Category.PointsOfInterest,
			NameGeneratorWindow.Category.Items,
		};

		// ── Construction ──────────────────────────────────────────────

		[Test]
		public void BuildUI_WiresEveryToolbarAndResultsButton()
		{
			foreach (string name in new[]
			{
				"generate-button", "generate-full-button", "copy-button", "export-button", "clear-button",
			})
			{
				Assert.IsNotNull(Btn(name), $"Button '{name}' is missing from the UXML.");
			}

			Assert.IsNotNull(root.Q<Label>("status-bar"), "Status bar is missing.");
			Assert.IsNotNull(ResultsList, "Results list is missing.");
			Assert.AreEqual("Ready", window.StatusText);
		}

		[Test]
		public void BuildUI_ListsEveryCategoryInTheLeftPanel()
		{
			List<VisualElement> items = root.Q<VisualElement>("category-list").Children().ToList();
			Assert.AreEqual(AllCategories.Length, items.Count);
			Assert.IsTrue(items[0].ClassListContains("category-item--selected"),
				"Characters should be selected on open.");
		}

		[Test]
		public void PickersAreLoadedFromTheRegistries()
		{
			Assert.AreEqual(NameGenerator.SupportedRaces.Count, RaceField.Choices.Count);
			Assert.AreEqual(NameGenerator.SupportedBiomes.Count, BiomeField.Choices.Count);
			Assert.AreEqual("Human", RaceField.Value, "Race picker should open on Human.");
		}

		/// <summary>
		/// The picker shows display names but the generator needs registry keys,
		/// so the index has to line up with SupportedRaces.
		/// </summary>
		[Test]
		public void RacePickerIndexMapsToTheRegistryKey()
		{
			int woodElf = NameGenerator.SupportedRaces.ToList().IndexOf("woodelf");
			Assume.That(woodElf, Is.GreaterThanOrEqualTo(0));

			RaceField.SetIndex(woodElf);
			Assert.AreEqual("Wood Elf", RaceField.Value);

			SetUnique(false);
			SetCount(4);
			window.GenerateResults(fullCharacters: false);
			Assert.AreEqual(4, Rows().Count);
			StringAssert.Contains("Wood Elf", window.StatusText);
		}

		/// <summary>
		/// Regression: the picker used to carry its choice index in
		/// <c>AdvancedDropdownItem.id</c>. <c>AddChild</c> overwrites the id of
		/// every child with a hash of (parent id, name), so the index was gone
		/// by the time the item was in the tree — and clamping that hash into
		/// range meant every pick landed on the first or last entry. The index
		/// now travels on the item's own type.
		/// </summary>
		[Test]
		public void DropdownItemIndex_SurvivesUnityOverwritingTheItemId()
		{
			var item = new SearchableDropdownField.IndexedItem("Wood Elf", 42);
			Assert.AreEqual(42, SearchableDropdownField.ResolveIndex(item));

			var root = new AdvancedDropdownItem("Race");
			root.AddChild(item);

			Assert.AreNotEqual(42, item.id,
				"AddChild is expected to overwrite id — if it no longer does, this trap is gone.");
			Assert.AreEqual(42, SearchableDropdownField.ResolveIndex(item),
				"The choice index must survive AddChild.");
		}

		[Test]
		public void ResolveIndex_RejectsItemsFromElsewhere()
		{
			Assert.AreEqual(-1, SearchableDropdownField.ResolveIndex(new AdvancedDropdownItem("Foreign")));
			Assert.AreEqual(-1, SearchableDropdownField.ResolveIndex(null));
		}

		/// <summary>Every entry in the race picker must resolve to its own name.</summary>
		[Test]
		public void PickingAnyRace_SelectsThatExactRace()
		{
			for (int i = 0; i < RaceField.Choices.Count; i++)
			{
				var item = new SearchableDropdownField.IndexedItem(RaceField.Choices[i], i);
				new AdvancedDropdownItem("Race").AddChild(item);

				RaceField.SetIndex(SearchableDropdownField.ResolveIndex(item));

				Assert.AreEqual(RaceField.Choices[i], RaceField.Value,
					$"Picking entry {i} selected the wrong race.");
			}
		}

		[Test]
		public void PickingARace_FiresTheChangeCallbackWithTheNewValue()
		{
			string observed = null;
			RaceField.OnValueChanged += v => observed = v;

			int target = RaceField.Choices.Count - 1;
			RaceField.SetIndex(target);

			Assert.AreEqual(RaceField.Choices[target], observed);
			Assert.AreEqual(RaceField.Choices[target], RaceField.Value);
		}

		/// <summary>The picked race must reach the generator, not just the label.</summary>
		[Test]
		public void PickedRace_IsTheRaceActuallyGenerated()
		{
			var races = NameGenerator.SupportedRaces.ToList();
			foreach (string key in new[] { "dwarf", "orc", "woodelf" })
			{
				int i = races.IndexOf(key);
				Assume.That(i, Is.GreaterThanOrEqualTo(0));

				RaceField.SetIndex(i);
				SetUnique(false);
				SetCount(2);
				window.GenerateResults(fullCharacters: false);

				StringAssert.Contains(RaceRegistry.GetDisplayName(key), window.StatusText,
					$"Generating after picking '{key}' did not use that race.");
			}
		}

		[Test]
		public void PickingABiome_IsTheBiomeActuallyGenerated()
		{
			window.SelectCategory(NameGeneratorWindow.Category.Dungeons);
			var biomes = NameGenerator.SupportedBiomes.ToList();

			for (int i = 0; i < biomes.Count; i += Mathf.Max(1, biomes.Count / 6))
			{
				BiomeField.SetIndex(i);
				SetUnique(false);
				SetCount(2);
				window.GenerateResults(fullCharacters: false);

				StringAssert.Contains(BiomeRegistry.GetDisplayName(biomes[i]), window.StatusText,
					$"Generating after picking biome '{biomes[i]}' did not use it.");
			}
		}

		// ── Generate ──────────────────────────────────────────────────

		[Test]
		public void Generate_ProducesRequestedCount_ForEveryCategory()
		{
			foreach (NameGeneratorWindow.Category category in AllCategories)
			{
				window.ClearResults();
				window.SelectCategory(category);
				SetUnique(false);
				SetCount(7);

				window.GenerateResults(fullCharacters: false);

				Assert.AreEqual(7, window.ActiveResultCount, $"{category}: wrong result count.");
				Assert.AreEqual(7, Rows().Count, $"{category}: wrong number of rendered rows.");
				StringAssert.StartsWith("Generated 7", window.StatusText, $"{category}: status not updated.");
			}
		}

		[Test]
		public void Generate_RendersNameAndCopyButtonOnEveryRow()
		{
			SetCount(3);
			window.GenerateResults(fullCharacters: false);

			foreach (VisualElement row in Rows())
			{
				Label name = row.Q<Label>(className: "result-name");
				Assert.IsNotNull(name, "Row is missing its name label.");
				Assert.IsNotEmpty(name.text, "Row rendered an empty name.");
				Assert.IsNotNull(row.Q<Button>(className: "result-copy-button"),
					"Row is missing its copy button.");
			}
		}

		[Test]
		public void GenerateFull_AddsTitlesThatPlainGenerateDoesNot()
		{
			SetCount(8);

			window.GenerateResults(fullCharacters: false);
			Assert.IsFalse(Rows().Any(r => r.Q<Label>(className: "result-title") != null),
				"Plain Generate should not produce titles.");

			window.GenerateResults(fullCharacters: true);
			Assert.IsTrue(Rows().Any(r => r.Q<Label>(className: "result-title") != null),
				"Generate Full should produce at least one title.");
		}

		/// <summary>Only characters have titles, so the button is meaningless elsewhere.</summary>
		[Test]
		public void GenerateFull_IsHiddenOutsideCharacters()
		{
			window.SelectCategory(NameGeneratorWindow.Category.Characters);
			Assert.AreEqual(DisplayStyle.Flex, Btn("generate-full-button").style.display.value);

			foreach (NameGeneratorWindow.Category category in AllCategories.Skip(1))
			{
				window.SelectCategory(category);
				Assert.AreEqual(DisplayStyle.None, Btn("generate-full-button").style.display.value,
					$"Generate Full should be hidden for {category}.");
			}
		}

		[Test]
		public void Generate_RespectsTheUniqueToggle()
		{
			window.SelectCategory(NameGeneratorWindow.Category.Cities);
			SetCount(25);
			SetUnique(true);
			window.GenerateResults(fullCharacters: false);

			List<string> names = Rows()
				.Select(r => r.Q<Label>(className: "result-name").text)
				.ToList();
			Assert.AreEqual(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
				"Unique mode returned duplicates.");
		}

		[Test]
		public void Generate_WithRegionSeed_IsReproducible()
		{
			SetCount(10);
			SetRegionSeed("azure-vale");

			window.GenerateResults(fullCharacters: false);
			List<string> first = Rows().Select(r => r.Q<Label>(className: "result-name").text).ToList();

			window.GenerateResults(fullCharacters: false);
			List<string> second = Rows().Select(r => r.Q<Label>(className: "result-name").text).ToList();

			CollectionAssert.AreEqual(first, second, "The same region seed produced a different list.");
			StringAssert.Contains("region:azure-vale", window.StatusText);
		}

		/// <summary>
		/// Regression: hybrid generation used the positional helper, which drops
		/// the region seed and passes no batch index — every entry in a seeded
		/// batch derived the same RNG and came back identical.
		/// </summary>
		[Test]
		public void Generate_Hybrid_WithRegionSeed_DoesNotRepeatOneName()
		{
			window.SelectCategory(NameGeneratorWindow.Category.Characters);
			root.Q<Toggle>("hybrid-toggle").value = true;
			SetCount(10);
			SetRegionSeed("hybrid-region");
			SetUnique(false);

			window.GenerateResults(fullCharacters: false);

			List<string> names = Rows().Select(r => r.Q<Label>(className: "result-name").text).ToList();
			Assert.AreEqual(10, names.Count);
			Assert.Greater(names.Distinct().Count(), 1,
				"A seeded hybrid batch collapsed to a single repeated name.");
		}

		[Test]
		public void Generate_CountIsClampedToOneHundred()
		{
			SetCount(5000);
			Assert.AreEqual(100, root.Q<IntegerField>("count-field").value);

			SetCount(0);
			Assert.AreEqual(1, root.Q<IntegerField>("count-field").value);
		}

		// ── Category switching ────────────────────────────────────────

		[Test]
		public void SelectCategory_ShowsRaceOrBiomeAsAppropriate()
		{
			var expectRace = new[]
			{
				NameGeneratorWindow.Category.Characters,
				NameGeneratorWindow.Category.Cities,
				NameGeneratorWindow.Category.Items,
			};

			foreach (NameGeneratorWindow.Category category in AllCategories)
			{
				window.SelectCategory(category);
				bool raceVisible = RaceField.parent.style.display.value == DisplayStyle.Flex;
				bool biomeVisible = BiomeField.parent.style.display.value == DisplayStyle.Flex;

				Assert.AreEqual(expectRace.Contains(category), raceVisible,
					$"{category}: race row visibility is wrong.");
				Assert.AreEqual(!expectRace.Contains(category), biomeVisible,
					$"{category}: biome row visibility is wrong.");
			}
		}

		[Test]
		public void SelectCategory_KeepsEachCategorysResultsSeparate()
		{
			window.SelectCategory(NameGeneratorWindow.Category.Characters);
			SetUnique(false);
			SetCount(3);
			window.GenerateResults(fullCharacters: false);

			window.SelectCategory(NameGeneratorWindow.Category.Dungeons);
			SetCount(6);
			window.GenerateResults(fullCharacters: false);
			Assert.AreEqual(6, Rows().Count);

			window.SelectCategory(NameGeneratorWindow.Category.Characters);
			Assert.AreEqual(3, Rows().Count, "Character results were lost when switching categories.");
		}

		// ── Copy All ──────────────────────────────────────────────────

		[Test]
		public void CopyAll_PutsOneLinePerResultOnTheClipboard()
		{
			SetUnique(false);
			SetCount(5);
			window.GenerateResults(fullCharacters: true);
			string[] lines = window.BuildClipboardTextForActiveCategory()
				.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			Assert.AreEqual(5, lines.Length);
			foreach (string line in lines)
			{
				Assert.IsNotEmpty(line.Trim());
			}

			// The clipboard write itself needs a display, so only the status is
			// asserted here; the text above is exactly what CopyResults writes.
			window.CopyResults();
			StringAssert.Contains("Copied 5", window.StatusText);
		}

		[Test]
		public void CopyAll_OnEmptyResults_SaysSoInsteadOfDoingNothing()
		{
			window.ClearResults();
			window.CopyResults();
			StringAssert.Contains("Nothing to copy", window.StatusText);
		}

		// ── Export CSV ────────────────────────────────────────────────

		[Test]
		public void ExportCsv_HasAHeaderAndOneRowPerResult()
		{
			foreach (NameGeneratorWindow.Category category in AllCategories)
			{
				window.ClearResults();
				window.SelectCategory(category);
				SetUnique(false);
				SetCount(4);
				window.GenerateResults(fullCharacters: true);

				string[] lines = window.BuildCsvForActiveCategory()
					.Split('\n', StringSplitOptions.RemoveEmptyEntries);

				Assert.AreEqual(5, lines.Length, $"{category}: expected a header plus 4 rows.");
				StringAssert.StartsWith("Name,", lines[0], $"{category}: header should lead with Name.");

				int columns = lines[0].Split(',').Length;
				for (int i = 1; i < lines.Length; i++)
				{
					Assert.AreEqual(columns, CountCsvFields(lines[i]),
						$"{category}: row {i} has the wrong column count.");
				}
			}
		}

		[Test]
		public void ExportCsv_OnEmptyResults_ReportsRatherThanWritingAnEmptyFile()
		{
			window.ClearResults();
			// The button opens a save panel first; the guard must fire before that.
			Assert.AreEqual(0, window.ActiveResultCount);

			string csv = window.BuildCsvForActiveCategory();
			Assert.AreEqual(1, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length,
				"An empty category should produce a header and nothing else.");
		}

		[Test]
		public void ExportCsv_QuotesFieldsThatNeedIt()
		{
			Assert.AreEqual("plain", NameGeneratorCsv.Field("plain"));
			Assert.AreEqual("\"a,b\"", NameGeneratorCsv.Field("a,b"));
			Assert.AreEqual("\"say \"\"hi\"\"\"", NameGeneratorCsv.Field("say \"hi\""));
			Assert.AreEqual("\"two\nlines\"", NameGeneratorCsv.Field("two\nlines"));
			Assert.AreEqual("", NameGeneratorCsv.Field(null));
		}

		/// <summary>Counts RFC 4180 fields, honouring quoted commas.</summary>
		private static int CountCsvFields(string line)
		{
			int fields = 1;
			bool quoted = false;
			for (int i = 0; i < line.Length; i++)
			{
				char c = line[i];
				if (c == '"')
				{
					quoted = !quoted;
				}
				else if (c == ',' && !quoted)
				{
					fields++;
				}
			}
			return fields;
		}

		// ── Clear ─────────────────────────────────────────────────────

		[Test]
		public void Clear_EmptiesEveryCategoryAndItsCount()
		{
			foreach (NameGeneratorWindow.Category category in AllCategories)
			{
				window.SelectCategory(category);
				SetCount(3);
				window.GenerateResults(fullCharacters: false);
			}

			window.ClearResults();

			foreach (NameGeneratorWindow.Category category in AllCategories)
			{
				window.SelectCategory(category);
				Assert.AreEqual(0, window.ActiveResultCount, $"{category} still holds results.");
				Assert.AreEqual(0, Rows().Count, $"{category} still renders rows.");
			}

			List<VisualElement> items = root.Q<VisualElement>("category-list").Children().ToList();
			foreach (VisualElement item in items)
			{
				Assert.AreEqual("0", item.Q<Label>(className: "category-count").text,
					"Category counts were not reset.");
			}
		}

		[Test]
		public void Clear_ShowsTheEmptyState()
		{
			SetCount(3);
			window.GenerateResults(fullCharacters: false);
			window.ClearResults();

			Assert.IsNotNull(ResultsList.Q<Label>(className: "empty-state-label"),
				"Cleared results should show the empty-state message.");
			StringAssert.Contains("Cleared", window.StatusText);
		}
	}
}
