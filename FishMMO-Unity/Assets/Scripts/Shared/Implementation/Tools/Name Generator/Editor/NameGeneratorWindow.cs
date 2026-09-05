using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.NameGeneration.Editor
{
	/// <summary>
	/// Test window for the name generator. Generates characters, cities,
	/// dungeons, points of interest and legendary item names straight from the
	/// registries so the output can be checked without entering play mode.
	/// Layout and palette follow <c>FishMMODashboard</c>.
	/// </summary>
	public class NameGeneratorWindow : EditorWindow
	{
		private const string UxmlPath =
			"Assets/Scripts/Shared/Implementation/Tools/Name Generator/Editor/NameGeneratorWindow.uxml";

		/// <summary>The generator categories, in the order the left panel lists them.</summary>
		public enum Category
		{
			Characters = 0,
			Cities = 1,
			Dungeons = 2,
			PointsOfInterest = 3,
			Items = 4,
		}

		private static readonly (Category Category, string Label)[] Categories =
		{
			(Category.Characters, "Characters"),
			(Category.Cities, "Cities"),
			(Category.Dungeons, "Dungeons"),
			(Category.PointsOfInterest, "Points of Interest"),
			(Category.Items, "Items"),
		};

		// ── State ─────────────────────────────────────────────────────
		private NameGenerator generator = new();
		private Category activeCategory = Category.Characters;

		private readonly List<CharacterEntry> characterResults = new();
		private readonly List<CityNameEntry> cityResults = new();
		private readonly List<DungeonNameEntry> dungeonResults = new();
		private readonly List<POINameEntry> poiResults = new();
		private readonly List<ItemNameEntry> itemResults = new();

		// Registry keys, parallel to the display lists shown in the pickers.
		private List<string> raceKeys;
		private List<string> raceDisplayNames;
		/// <summary>Category per race, parallel to <see cref="raceKeys"/>; groups the race dropdowns.</summary>
		private List<string> raceGroups;
		private List<string> biomeKeys;
		private List<string> biomeDisplayNames;
		private List<string> cultureKeys = new();

		// ── UXML references ───────────────────────────────────────────
		private VisualElement categoryListElement;
		private VisualElement settingsContent;
		private VisualElement resultsList;
		private Label resultsHeader;
		private Label resultsCountLabel;
		private Label statusBar;
		private Button generateFullButton;
		private VisualElement categoryPanel;
		private VisualElement settingsPanel;
		private VisualElement uiRoot;

		// ── Settings controls ─────────────────────────────────────────
		private SearchableDropdownField raceField;
		private SearchableDropdownField secondRaceField;
		private SearchableDropdownField biomeField;
		private SearchableDropdownField variantField;
		/// <summary>Variant behind each entry of <see cref="variantField"/>; null for "(none)".</summary>
		private List<BiomeClimateVariant> variantObjects = new List<BiomeClimateVariant>();
		/// <summary>Climate assets whose default variants the variant dropdown offers.</summary>
		private List<ClimateSettings> climateAssets = new List<ClimateSettings>();
		/// <summary>Label of the city-only biome choice that lets the race pick its own home.</summary>
		private const string RaceHomeBiomeChoice = "(race's home biome)";
		private DropdownField cultureField;
		private DropdownField genderField;
		private DropdownField titleTypeField;
		private DropdownField registerField;
		private TextField professionField;
		private IntegerField maxTitleField;
		private DropdownField cityTypeField;
		private DropdownField poiTypeField;
		private DropdownField itemTypeField;
		private Toggle hybridToggle;
		private Slider dominanceSlider;
		private IntegerField countField;
		private TextField seedField;
		private TextField regionSeedField;
		private Toggle uniqueToggle;

		private VisualElement raceRow;
		private VisualElement cultureRow;
		private VisualElement hybridRow;
		private VisualElement characterGroup;
		private VisualElement cityGroup;
		private VisualElement biomeRow;
		private VisualElement variantRow;
		private VisualElement poiGroup;
		private VisualElement itemGroup;

		// ── Menu ──────────────────────────────────────────────────────

		[MenuItem("FishMMO/Name Generator/Name Generator Test Window")]
		public static void ShowWindow()
		{
			NameGeneratorWindow window = GetWindow<NameGeneratorWindow>("Name Generator");
			window.minSize = new Vector2(720, 420);
		}

		// ── Construction ──────────────────────────────────────────────

		private void CreateGUI()
		{
			BuildUI(rootVisualElement);
		}

		/// <summary>
		/// Clones the UXML into <paramref name="root"/> and wires everything up.
		/// Separate from <c>CreateGUI</c> so an edit-mode test can build and
		/// drive the whole window without opening one.
		/// </summary>
		public void BuildUI(VisualElement root)
		{
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			if (uxml == null)
			{
				Debug.LogError($"[NameGeneratorWindow] Cannot find UXML at: {UxmlPath}");
				return;
			}

			uiRoot = root;
			uxml.CloneTree(uiRoot);

			// Outside play mode nothing has loaded the naming assets into the registries.
			NamingTemplateEditorLoader.EnsureLoaded();

			BindUIReferences();
			CacheRegistryLists();
			BuildCategoryList();
			BuildSettings();
			SetupResizers();

			SelectCategory(activeCategory);
			SetStatus("Ready");
		}

		private void BindUIReferences()
		{
			categoryListElement = uiRoot.Q<VisualElement>("category-list");
			settingsContent = uiRoot.Q<VisualElement>("settings-content");
			resultsList = uiRoot.Q<VisualElement>("results-list");
			resultsHeader = uiRoot.Q<Label>("results-header");
			resultsCountLabel = uiRoot.Q<Label>("results-count-label");
			statusBar = uiRoot.Q<Label>("status-bar");
			categoryPanel = uiRoot.Q<VisualElement>("category-panel");
			settingsPanel = uiRoot.Q<VisualElement>("settings-panel");

			uiRoot.Q<Button>("generate-button").clicked += () => GenerateResults(fullCharacters: false);
			generateFullButton = uiRoot.Q<Button>("generate-full-button");
			generateFullButton.clicked += () => GenerateResults(fullCharacters: true);
			uiRoot.Q<Button>("copy-button").clicked += CopyResults;
			uiRoot.Q<Button>("export-button").clicked += ExportResultsCsv;
			uiRoot.Q<Button>("clear-button").clicked += ClearResults;
		}

		private void CacheRegistryLists()
		{
			raceKeys = NameGenerator.SupportedRaces.ToList();
			raceDisplayNames = raceKeys.Select(RaceRegistry.GetDisplayName).ToList();
			raceGroups = raceKeys
				.Select(key => RaceRegistry.TryGet(key, out RaceTemplate race) && !string.IsNullOrWhiteSpace(race.Category) ? race.Category.Trim() : "Other")
				.ToList();
			biomeKeys = NameGenerator.SupportedBiomes.ToList();
			biomeDisplayNames = biomeKeys.Select(BiomeRegistry.GetDisplayName).ToList();
			climateAssets = AssetDatabase.FindAssets($"t:{nameof(ClimateSettings)}")
				.Select(guid => AssetDatabase.LoadAssetAtPath<ClimateSettings>(AssetDatabase.GUIDToAssetPath(guid)))
				.Where(asset => asset != null)
				.OrderBy(asset => asset.name, StringComparer.Ordinal)
				.ToList();
		}

		// ── Category list (left panel) ────────────────────────────────

		private void BuildCategoryList()
		{
			categoryListElement.Clear();

			for (int i = 0; i < Categories.Length; i++)
			{
				(Category category, string label) = Categories[i];

				var item = new VisualElement();
				item.AddToClassList("category-item");
				if (category == activeCategory)
				{
					item.AddToClassList("category-item--selected");
				}

				var nameLabel = new Label(label);
				nameLabel.AddToClassList("category-label");
				item.Add(nameLabel);

				var countLabel = new Label(ResultCount(category).ToString());
				countLabel.AddToClassList("category-count");
				item.Add(countLabel);

				Category captured = category;
				item.RegisterCallback<ClickEvent>(_ => SelectCategory(captured));
				categoryListElement.Add(item);
			}
		}

		/// <summary>Selects a category, as clicking its row in the left panel does.</summary>
		public void SelectCategory(Category category)
		{
			activeCategory = category;
			BuildCategoryList();

			bool isCharacters = category == Category.Characters;
			bool usesRace = category == Category.Characters
				|| category == Category.Cities
				|| category == Category.Items;
			bool usesBiome = category == Category.Dungeons
				|| category == Category.PointsOfInterest
				|| category == Category.Cities;
			// Cities may leave the biome to the race; the other biome categories must name one.
			bool wasCities = biomeField.Choices.Count == biomeDisplayNames.Count + 1;
			bool isCities = category == Category.Cities;
			if (usesBiome && wasCities != isCities)
			{
				biomeField.SetChoices(isCities
					? new[] { RaceHomeBiomeChoice }.Concat(biomeDisplayNames)
					: biomeDisplayNames);
				RefreshVariantChoices();
			}

			raceRow.style.display = usesRace ? DisplayStyle.Flex : DisplayStyle.None;
			cultureRow.style.display = usesRace && cultureKeys.Count > 0
				? DisplayStyle.Flex
				: DisplayStyle.None;
			biomeRow.style.display = usesBiome ? DisplayStyle.Flex : DisplayStyle.None;
			variantRow.style.display = usesBiome ? DisplayStyle.Flex : DisplayStyle.None;

			characterGroup.style.display = isCharacters ? DisplayStyle.Flex : DisplayStyle.None;
			cityGroup.style.display = category == Category.Cities ? DisplayStyle.Flex : DisplayStyle.None;
			poiGroup.style.display = category == Category.PointsOfInterest
				? DisplayStyle.Flex
				: DisplayStyle.None;
			itemGroup.style.display = category == Category.Items ? DisplayStyle.Flex : DisplayStyle.None;

			// "Generate Full" only adds titles, which only characters have.
			generateFullButton.style.display = isCharacters ? DisplayStyle.Flex : DisplayStyle.None;

			RefreshResults();
		}

		// ── Settings panel (middle) ───────────────────────────────────

		private void BuildSettings()
		{
			// Race (shared by Characters / Cities / Items)
			raceRow = new VisualElement();
			raceRow.Add(FieldLabel("Race"));
			int humanIndex = Mathf.Max(0, raceKeys.IndexOf("human"));
			raceField = new SearchableDropdownField("Race", raceDisplayNames, humanIndex, raceGroups) { name = "race-field" };
			raceField.OnValueChanged += _ => RefreshCultureChoices();
			raceRow.Add(raceField);
			settingsContent.Add(raceRow);

			// Culture (populated from the selected race)
			cultureRow = new VisualElement();
			cultureRow.Add(FieldLabel("Culture (optional)"));
			cultureField = new DropdownField(new List<string> { "(none)" }, 0) { name = "culture-field" };
			cultureField.labelElement.style.display = DisplayStyle.None;
			cultureRow.Add(cultureField);
			settingsContent.Add(cultureRow);

			// Biome (shared by Dungeons / POI)
			biomeRow = new VisualElement();
			biomeRow.Add(FieldLabel("Biome"));
			biomeField = new SearchableDropdownField("Biome", biomeDisplayNames) { name = "biome-field" };
			biomeField.OnValueChanged += _ => RefreshVariantChoices();
			biomeRow.Add(biomeField);
			settingsContent.Add(biomeRow);

			// Climate variant: the biome's own, then every ClimateSettings asset's defaults.
			variantRow = new VisualElement();
			variantRow.Add(FieldLabel("Climate Variant"));
			variantField = new SearchableDropdownField("Climate Variant", new[] { "(none)" }) { name = "variant-field" };
			variantRow.Add(variantField);
			settingsContent.Add(variantRow);
			RefreshVariantChoices();

			BuildCharacterGroup();
			BuildCityGroup();
			BuildPoiGroup();
			BuildItemGroup();
			BuildSharedGroup();

			RefreshCultureChoices();
		}

		private void BuildCharacterGroup()
		{
			characterGroup = new VisualElement();

			var row = new VisualElement();
			row.AddToClassList("field-row");

			var genderCol = new VisualElement();
			genderCol.AddToClassList("field-col");
			genderCol.Add(FieldLabel("Gender"));
			genderField = new DropdownField(Enum.GetNames(typeof(CharacterGender)).ToList(), 0) { name = "gender-field" };
			genderField.labelElement.style.display = DisplayStyle.None;
			genderCol.Add(genderField);
			row.Add(genderCol);

			var titleCol = new VisualElement();
			titleCol.AddToClassList("field-col");
			titleCol.Add(FieldLabel("Title Type"));
			titleTypeField = new DropdownField(Enum.GetNames(typeof(TitleType)).ToList(), 0) { name = "title-type-field" };
			titleTypeField.labelElement.style.display = DisplayStyle.None;
			titleCol.Add(titleTypeField);
			row.Add(titleCol);

			characterGroup.Add(row);

			var titleRow = new VisualElement();
			titleRow.AddToClassList("field-row");

			var registerCol = new VisualElement();
			registerCol.AddToClassList("field-col");
			registerCol.Add(FieldLabel("Register"));
			registerField = new DropdownField(Enum.GetNames(typeof(TitleRegister)).ToList(), 0) { name = "register-field" };
			registerField.labelElement.style.display = DisplayStyle.None;
			registerField.tooltip = "Civil: honorifics and trades. Martial: ranks and deeds. Mythic: legends.";
			registerCol.Add(registerField);
			titleRow.Add(registerCol);

			var maxCol = new VisualElement();
			maxCol.AddToClassList("field-col");
			maxCol.Add(FieldLabel("Max title length"));
			maxTitleField = new IntegerField { value = 0, name = "max-title-field" };
			maxTitleField.labelElement.style.display = DisplayStyle.None;
			maxTitleField.tooltip = "0 = unlimited. Nameplates use 32.";
			maxCol.Add(maxTitleField);
			titleRow.Add(maxCol);

			characterGroup.Add(titleRow);

			characterGroup.Add(FieldLabel("Profession (optional)"));
			professionField = new TextField { value = "", name = "profession-field" };
			professionField.labelElement.style.display = DisplayStyle.None;
			professionField.tooltip = "Fills the {profession} slot of Civil titles, e.g. 'Banker'.";
			characterGroup.Add(professionField);

			hybridToggle = new Toggle("Hybrid Mode") { value = false, name = "hybrid-toggle" };
			hybridToggle.tooltip = "Blend the phonology of two races into one name.";
			hybridToggle.RegisterValueChangedCallback(e =>
				hybridRow.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None);
			characterGroup.Add(hybridToggle);

			hybridRow = new VisualElement();
			hybridRow.AddToClassList("settings-group");
			hybridRow.style.display = DisplayStyle.None;
			hybridRow.Add(FieldLabel("Second Race"));
			int elfIndex = Mathf.Max(0, raceKeys.IndexOf("elf"));
			secondRaceField = new SearchableDropdownField("Second Race", raceDisplayNames, elfIndex, raceGroups) { name = "second-race-field" };
			hybridRow.Add(secondRaceField);

			hybridRow.Add(FieldLabel("Dominance (1 = first race)"));
			dominanceSlider = new Slider(0f, 1f) { value = 0.5f, showInputField = true, name = "dominance-slider" };
			hybridRow.Add(dominanceSlider);
			characterGroup.Add(hybridRow);

			settingsContent.Add(characterGroup);
		}

		private void BuildCityGroup()
		{
			cityGroup = new VisualElement();
			cityGroup.Add(FieldLabel("City Type"));
			cityTypeField = new DropdownField(Enum.GetNames(typeof(CityType)).ToList(), 0) { name = "city-type-field" };
			cityTypeField.labelElement.style.display = DisplayStyle.None;
			cityGroup.Add(cityTypeField);
			settingsContent.Add(cityGroup);
		}

		private void BuildPoiGroup()
		{
			poiGroup = new VisualElement();
			poiGroup.Add(FieldLabel("POI Type"));
			poiTypeField = new DropdownField(Enum.GetNames(typeof(POIType)).ToList(), 0) { name = "poi-type-field" };
			poiTypeField.labelElement.style.display = DisplayStyle.None;
			poiGroup.Add(poiTypeField);
			settingsContent.Add(poiGroup);
		}

		private void BuildItemGroup()
		{
			itemGroup = new VisualElement();
			itemGroup.Add(FieldLabel("Item Type"));
			itemTypeField = new DropdownField(Enum.GetNames(typeof(ItemType)).ToList(), 0) { name = "item-type-field" };
			itemTypeField.labelElement.style.display = DisplayStyle.None;
			itemGroup.Add(itemTypeField);

			var note = new Label("Item names are built from the selected race's phonology.");
			note.AddToClassList("field-note");
			itemGroup.Add(note);
			settingsContent.Add(itemGroup);
		}

		private void BuildSharedGroup()
		{
			settingsContent.Add(FieldLabel("Count"));
			countField = new IntegerField { value = 10, name = "count-field" };
			countField.labelElement.style.display = DisplayStyle.None;
			countField.RegisterValueChangedCallback(e =>
			{
				int clamped = Mathf.Clamp(e.newValue, 1, 100);
				if (clamped != e.newValue)
				{
					countField.SetValueWithoutNotify(clamped);
				}
			});
			settingsContent.Add(countField);

			settingsContent.Add(FieldLabel("Seed (optional)"));
			seedField = new TextField { value = "", name = "seed-field" };
			seedField.labelElement.style.display = DisplayStyle.None;
			seedField.tooltip = "Whole number. Seeds this window's own RNG so a run can be repeated.";
			settingsContent.Add(seedField);

			settingsContent.Add(FieldLabel("Region Seed (optional)"));
			regionSeedField = new TextField { value = "", name = "region-seed-field" };
			regionSeedField.labelElement.style.display = DisplayStyle.None;
			regionSeedField.tooltip =
				"Any text. Names derived from a region seed are reproducible across " +
				"machines and sessions — the same seed always yields the same list.";
			settingsContent.Add(regionSeedField);

			uniqueToggle = new Toggle("Unique names (no duplicates)") { value = true, name = "unique-toggle" };
			uniqueToggle.style.marginTop = 6;
			settingsContent.Add(uniqueToggle);
		}

		private static Label FieldLabel(string text)
		{
			var label = new Label(text);
			label.AddToClassList("field-label");
			return label;
		}

		// ── Resizers ──────────────────────────────────────────────────

		private void SetupResizers()
		{
			MakeResizer(uiRoot.Q<VisualElement>("left-resizer"), categoryPanel, 120f, 320f);
			MakeResizer(uiRoot.Q<VisualElement>("right-resizer"), settingsPanel, 200f, 480f);
		}

		private static void MakeResizer(VisualElement handle, VisualElement target, float min, float max)
		{
			if (handle == null || target == null)
			{
				return;
			}

			bool dragging = false;
			float startX = 0f;
			float startWidth = 0f;

			handle.RegisterCallback<PointerDownEvent>(e =>
			{
				dragging = true;
				startX = e.position.x;
				startWidth = target.resolvedStyle.width;
				handle.CapturePointer(e.pointerId);
				e.StopPropagation();
			});

			handle.RegisterCallback<PointerMoveEvent>(e =>
			{
				if (!dragging)
				{
					return;
				}
				float width = Mathf.Clamp(startWidth + (e.position.x - startX), min, max);
				target.style.width = width;
				e.StopPropagation();
			});

			handle.RegisterCallback<PointerUpEvent>(e =>
			{
				if (!dragging)
				{
					return;
				}
				dragging = false;
				handle.ReleasePointer(e.pointerId);
				e.StopPropagation();
			});
		}

		// ── Key resolution ────────────────────────────────────────────

		private string SelectedRaceKey()
		{
			int i = raceField.Index;
			return i >= 0 && i < raceKeys.Count ? raceKeys[i] : "human";
		}

		private string SelectedSecondRaceKey()
		{
			int i = secondRaceField.Index;
			return i >= 0 && i < raceKeys.Count ? raceKeys[i] : "elf";
		}

		/// <summary>The chosen biome key, or null when a city is left to its race's home biome.</summary>
		private string SelectedBiomeKey()
		{
			int i = biomeField.Index;
			if (biomeField.Choices.Count == biomeDisplayNames.Count + 1)
			{
				// Cities: entry 0 is the race's home.
				i--;
				if (i < 0) return null;
			}
			return i >= 0 && i < biomeKeys.Count ? biomeKeys[i] : biomeKeys.FirstOrDefault();
		}

		private BiomeClimateVariant SelectedVariant()
		{
			int i = variantField.Index;
			return i >= 0 && i < variantObjects.Count ? variantObjects[i] : null;
		}

		private void RefreshVariantChoices()
		{
			if (variantField == null) return;
			var labels = new List<string> { "(none)" };
			variantObjects = new List<BiomeClimateVariant> { null };
			string key = SelectedBiomeKey();
			if (key != null && BiomeRegistry.TryGet(key, out BiomeTemplate biome))
			{
				foreach (BiomeClimateVariant variant in biome.ClimateVariants)
				{
					if (variant == null || string.IsNullOrWhiteSpace(variant.Name)) continue;
					labels.Add(variant.Name);
					variantObjects.Add(variant);
				}
			}
			foreach (ClimateSettings climate in climateAssets)
			{
				foreach (BiomeClimateVariant variant in climate.DefaultVariants)
				{
					if (variant == null || string.IsNullOrWhiteSpace(variant.Name)) continue;
					labels.Add($"{variant.Name} ({climate.name})");
					variantObjects.Add(variant);
				}
			}
			variantField.SetChoices(labels);
		}

		private static string VariantSuffix(BiomeClimateVariant variant)
		{
			return variant == null ? "" : $", {variant.Name}";
		}

		private string SelectedCulture()
		{
			int i = cultureField.index - 1; // slot 0 is "(none)"
			return i >= 0 && i < cultureKeys.Count ? cultureKeys[i] : null;
		}

		private string RegionSeed()
		{
			return string.IsNullOrWhiteSpace(regionSeedField.value) ? null : regionSeedField.value.Trim();
		}

		private void ResetGenerator()
		{
			string seedText = seedField.value?.Trim();
			generator = !string.IsNullOrEmpty(seedText) && int.TryParse(seedText, out int seed)
				? new NameGenerator(seed)
				: new NameGenerator();
		}

		private void RefreshCultureChoices()
		{
			cultureKeys = NameGenerator.GetCultures(SelectedRaceKey()).ToList();

			var choices = new List<string> { "(none)" };
			choices.AddRange(cultureKeys.Select(c => char.ToUpper(c[0]) + c.Substring(1)));
			cultureField.choices = choices;
			cultureField.index = 0;

			bool usesRace = activeCategory == Category.Characters
				|| activeCategory == Category.Cities
				|| activeCategory == Category.Items;
			cultureRow.style.display = usesRace && cultureKeys.Count > 0
				? DisplayStyle.Flex
				: DisplayStyle.None;
		}

		// ── Generation ────────────────────────────────────────────────

		/// <summary>Runs the "Generate" / "Generate Full" toolbar action.</summary>
		public void GenerateResults(bool fullCharacters)
		{
			ResetGenerator();

			switch (activeCategory)
			{
				case Category.Characters:
					GenerateCharacters(fullCharacters);
					break;
				case Category.Cities:
					GenerateCities();
					break;
				case Category.Dungeons:
					GenerateDungeons();
					break;
				case Category.PointsOfInterest:
					GeneratePOIs();
					break;
				case Category.Items:
					GenerateItems();
					break;
			}

			BuildCategoryList();
			RefreshResults();
		}

		private void GenerateCharacters(bool fullCharacters)
		{
			string raceKey = SelectedRaceKey();
			var gender = (CharacterGender)Enum.Parse(typeof(CharacterGender), genderField.value);
			var titleType = (TitleType)Enum.Parse(typeof(TitleType), titleTypeField.value);
			var register = (TitleRegister)Enum.Parse(typeof(TitleRegister), registerField.value);
			string profession = string.IsNullOrWhiteSpace(professionField.value) ? null : professionField.value.Trim();
			int maxTitle = Mathf.Max(0, maxTitleField.value);
			int count = Mathf.Clamp(countField.value, 1, 100);
			bool unique = uniqueToggle.value;
			string culture = SelectedCulture();
			string regionSeed = RegionSeed();

			characterResults.Clear();

			if (hybridToggle.value)
			{
				GenerateHybrids(raceKey, gender, titleType, register, profession, maxTitle, count, unique, fullCharacters, regionSeed);
			}
			else
			{
				var request = new NameRequest
				{
					Race = raceKey,
					Gender = gender,
					TitleType = titleType,
					Register = register,
					Profession = profession,
					MaxTitleLength = maxTitle,
					Culture = culture,
					RegionSeed = regionSeed,
					NameOnly = !fullCharacters,
				};
				characterResults.AddRange(unique
					? generator.GenerateUnique(request, count).Items
					: generator.GenerateBatch(request, count));
			}

			string mode = hybridToggle.value
				? "hybrid name(s)"
				: (fullCharacters ? "character(s)" : "name(s)");
			SetStatus($"Generated {characterResults.Count} {mode}{UniqueSuffix(unique, characterResults.Count, count)} — " +
				$"{raceField.Value}{CultureSuffix(culture)}" +
				$"{(hybridToggle.value ? "/" + secondRaceField.Value : "")}{RegionSuffix(regionSeed)}");
		}

		/// <summary>
		/// Hybrids go through the request API rather than the positional
		/// helper so a region seed reaches the generator, and so each batch
		/// entry gets its own index — without one, a seeded batch derives the
		/// same RNG every iteration and returns the same name N times.
		/// </summary>
		private void GenerateHybrids(string raceKey, CharacterGender gender, TitleType titleType,
			TitleRegister register, string profession, int maxTitle,
			int count, bool unique, bool fullCharacters, string regionSeed)
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int maxAttempts = unique ? count * 20 : count;

			for (int attempt = 0; attempt < maxAttempts && characterResults.Count < count; attempt++)
			{
				CharacterEntry entry = generator.Generate(new HybridRequest
				{
					RaceA = raceKey,
					RaceB = SelectedSecondRaceKey(),
					Gender = gender,
					TitleType = titleType,
					Register = register,
					Profession = profession,
					MaxTitleLength = maxTitle,
					Dominance = dominanceSlider.value,
					RegionSeed = regionSeed,
					Index = attempt,
				});

				if (unique && !seen.Add(entry.Name))
				{
					continue;
				}
				if (!fullCharacters)
				{
					entry.Title = "";
					entry.TitleCategory = "";
				}
				characterResults.Add(entry);
			}
		}

		private void GenerateCities()
		{
			string raceKey = SelectedRaceKey();
			var cityType = (CityType)Enum.Parse(typeof(CityType), cityTypeField.value);
			int count = Mathf.Clamp(countField.value, 1, 100);
			bool unique = uniqueToggle.value;
			string culture = SelectedCulture();
			string regionSeed = RegionSeed();

			string biomeKey = SelectedBiomeKey();
			BiomeClimateVariant variant = SelectedVariant();
			var request = new CityRequest
			{
				Race = raceKey, CityType = cityType, Culture = culture, RegionSeed = regionSeed,
				Biome = biomeKey, Variant = variant,
			};

			cityResults.Clear();
			cityResults.AddRange(unique
				? generator.GenerateUnique(request, count).Items
				: generator.GenerateBatch(request, count));

			string where = biomeKey == null ? "" : $", {biomeField.Value}";
			SetStatus($"Generated {cityResults.Count} city name(s)" +
				$"{UniqueSuffix(unique, cityResults.Count, count)} — " +
				$"{raceField.Value}{CultureSuffix(culture)}{where}{VariantSuffix(variant)}{RegionSuffix(regionSeed)}");
		}

		private void GenerateDungeons()
		{
			string biomeKey = SelectedBiomeKey();
			int count = Mathf.Clamp(countField.value, 1, 100);
			bool unique = uniqueToggle.value;
			string regionSeed = RegionSeed();

			BiomeClimateVariant variant = SelectedVariant();
			var request = new DungeonRequest { Biome = biomeKey, RegionSeed = regionSeed, Variant = variant };

			dungeonResults.Clear();
			dungeonResults.AddRange(unique
				? generator.GenerateUnique(request, count).Items
				: generator.GenerateBatch(request, count));

			SetStatus($"Generated {dungeonResults.Count} dungeon name(s)" +
				$"{UniqueSuffix(unique, dungeonResults.Count, count)} — " +
				$"{biomeField.Value}{VariantSuffix(variant)}{RegionSuffix(regionSeed)}");
		}

		private void GeneratePOIs()
		{
			string biomeKey = SelectedBiomeKey();
			var poiType = (POIType)Enum.Parse(typeof(POIType), poiTypeField.value);
			int count = Mathf.Clamp(countField.value, 1, 100);
			bool unique = uniqueToggle.value;
			string regionSeed = RegionSeed();

			BiomeClimateVariant variant = SelectedVariant();
			var request = new POIRequest { Biome = biomeKey, POIType = poiType, RegionSeed = regionSeed, Variant = variant };

			poiResults.Clear();
			poiResults.AddRange(unique
				? generator.GenerateUnique(request, count).Items
				: generator.GenerateBatch(request, count));

			SetStatus($"Generated {poiResults.Count} POI name(s)" +
				$"{UniqueSuffix(unique, poiResults.Count, count)} — " +
				$"{biomeField.Value}{VariantSuffix(variant)}{RegionSuffix(regionSeed)}");
		}

		private void GenerateItems()
		{
			string raceKey = SelectedRaceKey();
			var itemType = (ItemType)Enum.Parse(typeof(ItemType), itemTypeField.value);
			int count = Mathf.Clamp(countField.value, 1, 100);
			bool unique = uniqueToggle.value;
			string culture = SelectedCulture();
			string regionSeed = RegionSeed();

			var request = new ItemRequest
			{
				Race = raceKey,
				Culture = culture,
				ItemType = itemType,
				RegionSeed = regionSeed,
			};

			itemResults.Clear();
			itemResults.AddRange(unique
				? generator.GenerateUnique(request, count).Items
				: generator.GenerateBatch(request, count));

			SetStatus($"Generated {itemResults.Count} item name(s)" +
				$"{UniqueSuffix(unique, itemResults.Count, count)} — " +
				$"{raceField.Value}{CultureSuffix(culture)}{RegionSuffix(regionSeed)}");
		}

		private static string UniqueSuffix(bool unique, int produced, int requested)
		{
			if (!unique)
			{
				return "";
			}
			return produced < requested ? " (unique — pool exhausted)" : " (unique)";
		}

		private static string CultureSuffix(string culture)
		{
			return string.IsNullOrEmpty(culture) ? "" : $" [{culture}]";
		}

		private static string RegionSuffix(string regionSeed)
		{
			return string.IsNullOrEmpty(regionSeed) ? "" : $" region:{regionSeed}";
		}

		// ── Results (right panel) ─────────────────────────────────────

		private int ResultCount(Category category)
		{
			switch (category)
			{
				case Category.Characters: return characterResults.Count;
				case Category.Cities: return cityResults.Count;
				case Category.Dungeons: return dungeonResults.Count;
				case Category.PointsOfInterest: return poiResults.Count;
				case Category.Items: return itemResults.Count;
				default: return 0;
			}
		}

		private void RefreshResults()
		{
			resultsList.Clear();
			resultsHeader.text = $"Results — {Categories[(int)activeCategory].Label}";

			switch (activeCategory)
			{
				case Category.Characters:
					for (int i = 0; i < characterResults.Count; i++)
					{
						CharacterEntry entry = characterResults[i];
						VisualElement row = MakeRow(i, entry.Name, entry.Meaning, entry.FragmentBreakdown,
							entry.FullTitle);
						if (!string.IsNullOrEmpty(entry.Title))
						{
							row.Insert(2, Tagged("result-title", $", {entry.Title}"));
						}
						if (!string.IsNullOrEmpty(entry.TitleCategory))
						{
							AddTag(row, entry.TitleCategory);
						}
						resultsList.Add(row);
					}
					break;

				case Category.Cities:
					for (int i = 0; i < cityResults.Count; i++)
					{
						CityNameEntry entry = cityResults[i];
						VisualElement row = MakeRow(i, entry.Name, entry.Meaning, entry.FragmentBreakdown,
							entry.Name);
						AddTag(row, entry.CityType);
						resultsList.Add(row);
					}
					break;

				case Category.Dungeons:
					for (int i = 0; i < dungeonResults.Count; i++)
					{
						DungeonNameEntry entry = dungeonResults[i];
						VisualElement row = MakeRow(i, entry.Name, entry.Meaning, entry.FragmentBreakdown,
							entry.Name);
						AddTag(row, entry.Biome);
						resultsList.Add(row);
					}
					break;

				case Category.PointsOfInterest:
					for (int i = 0; i < poiResults.Count; i++)
					{
						POINameEntry entry = poiResults[i];
						VisualElement row = MakeRow(i, entry.Name, entry.Meaning, entry.FragmentBreakdown,
							entry.Name);
						AddTag(row, entry.POIType);
						resultsList.Add(row);
					}
					break;

				case Category.Items:
					for (int i = 0; i < itemResults.Count; i++)
					{
						ItemNameEntry entry = itemResults[i];
						VisualElement row = MakeRow(i, entry.Name, entry.Meaning, entry.FragmentBreakdown,
							entry.Name);
						AddTag(row, entry.ItemCategory);
						resultsList.Add(row);
					}
					break;
			}

			int count = ResultCount(activeCategory);
			resultsCountLabel.text = count == 0
				? "No results — press Generate."
				: $"{count} result(s)";

			if (count == 0)
			{
				var empty = new Label("Nothing generated yet.\nPick a category, set the options, then press Generate.");
				empty.AddToClassList("empty-state-label");
				var wrapper = new VisualElement();
				wrapper.AddToClassList("empty-state");
				wrapper.Add(empty);
				resultsList.Add(wrapper);
			}
		}

		private VisualElement MakeRow(int index, string name, string meaning, string tooltip,
			string clipboardText)
		{
			var row = new VisualElement();
			row.AddToClassList("result-row");
			if (index % 2 == 1)
			{
				row.AddToClassList("result-row--alt");
			}
			if (!string.IsNullOrEmpty(tooltip))
			{
				row.tooltip = tooltip;
			}

			row.Add(Tagged("result-index", $"{index + 1}."));
			row.Add(Tagged("result-name", name));

			var spacer = new VisualElement();
			spacer.AddToClassList("result-spacer");
			row.Add(spacer);

			if (!string.IsNullOrEmpty(meaning))
			{
				row.Add(Tagged("result-meaning", meaning));
			}

			var copyButton = new Button(() =>
			{
				EditorGUIUtility.systemCopyBuffer = clipboardText;
				SetStatus($"Copied: {clipboardText}");
			})
			{
				text = "⎘",
				tooltip = "Copy this entry to the clipboard",
			};
			copyButton.AddToClassList("result-copy-button");
			row.Add(copyButton);

			return row;
		}

		private static Label Tagged(string ussClass, string text)
		{
			var label = new Label(text);
			label.AddToClassList(ussClass);
			return label;
		}

		private static void AddTag(VisualElement row, string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			// Sit the tag just before the copy button.
			row.Insert(row.childCount - 1, Tagged("result-tag", text.ToUpperInvariant()));
		}

		// ── Toolbar actions ───────────────────────────────────────────

		/// <summary>Runs the "Copy All" action.</summary>
		public void CopyResults()
		{
			if (ResultCount(activeCategory) == 0)
			{
				SetStatus("Nothing to copy — generate some results first.");
				return;
			}

			EditorGUIUtility.systemCopyBuffer = BuildClipboardTextForActiveCategory();
			SetStatus($"Copied {ResultCount(activeCategory)} result(s) to the clipboard.");
		}

		/// <summary>The text "Copy All" places on the clipboard: one result per line.</summary>
		public string BuildClipboardTextForActiveCategory()
		{
			IEnumerable<string> lines;
			switch (activeCategory)
			{
				case Category.Characters:
					lines = characterResults.Select(r => Join(r.FullTitle, r.Meaning, r.TitleCategory));
					break;
				case Category.Cities:
					lines = cityResults.Select(r => Join(r.Name, r.Meaning, r.CityType));
					break;
				case Category.Dungeons:
					lines = dungeonResults.Select(r => Join(r.Name, r.Meaning, r.Biome));
					break;
				case Category.PointsOfInterest:
					lines = poiResults.Select(r => Join(r.Name, r.Meaning, r.POIType));
					break;
				default:
					lines = itemResults.Select(r => Join(r.Name, r.Meaning, r.ItemCategory));
					break;
			}
			return string.Join("\n", lines);
		}

		private static string Join(string name, string meaning, string tag)
		{
			var parts = new List<string> { name };
			if (!string.IsNullOrEmpty(meaning))
			{
				parts.Add($"({meaning})");
			}
			if (!string.IsNullOrEmpty(tag))
			{
				parts.Add($"[{tag}]");
			}
			return string.Join("  ", parts);
		}

		/// <summary>
		/// Writes the current category's results to a .csv file the user picks:
		/// a header row naming the columns, then one row per result (name, type,
		/// race or biome, and the generator's derived meaning). Opens in any
		/// spreadsheet, and is the shape a bulk content-import script wants.
		/// </summary>
		private void ExportResultsCsv()
		{
			if (ResultCount(activeCategory) == 0)
			{
				SetStatus("Nothing to export — generate some results first.");
				return;
			}

			string label = Categories[(int)activeCategory].Label;
			string path = EditorUtility.SaveFilePanel($"Export {label} to CSV",
				Application.dataPath, $"NameGen_{label.Replace(" ", "")}.csv", "csv");
			if (string.IsNullOrEmpty(path))
			{
				SetStatus("Export cancelled.");
				return;
			}

			try
			{
				System.IO.File.WriteAllText(path, BuildCsvForActiveCategory(), System.Text.Encoding.UTF8);
				SetStatus($"Exported {ResultCount(activeCategory)} row(s) to {path}");
			}
			catch (Exception ex)
			{
				SetStatus($"Export failed: {ex.Message}");
				Debug.LogException(ex);
			}
		}

		/// <summary>The CSV text "Export CSV" writes for the active category.</summary>
		public string BuildCsvForActiveCategory()
		{
			switch (activeCategory)
			{
				case Category.Characters: return NameGeneratorCsv.FromCharacters(characterResults);
				case Category.Cities: return NameGeneratorCsv.FromCities(cityResults);
				case Category.Dungeons: return NameGeneratorCsv.FromDungeons(dungeonResults);
				case Category.PointsOfInterest: return NameGeneratorCsv.FromPOIs(poiResults);
				default: return NameGeneratorCsv.FromItems(itemResults);
			}
		}

		// ── Read-only state, for tests and for callers driving the window ──

		/// <summary>Number of results held for the active category.</summary>
		public int ActiveResultCount => ResultCount(activeCategory);

		/// <summary>Current status-bar text.</summary>
		public string StatusText => statusBar == null ? "" : statusBar.text;

		/// <summary>Category currently selected in the left panel.</summary>
		public Category ActiveCategory => activeCategory;

		/// <summary>Runs the "Clear" action.</summary>
		public void ClearResults()
		{
			characterResults.Clear();
			cityResults.Clear();
			dungeonResults.Clear();
			poiResults.Clear();
			itemResults.Clear();
			BuildCategoryList();
			RefreshResults();
			SetStatus("Cleared.");
		}

		private void SetStatus(string message)
		{
			if (statusBar != null)
			{
				statusBar.text = message;
			}
		}
	}
}
