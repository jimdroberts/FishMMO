#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// The NPCs category: lists every NPC prefab, designs new ones from a recipe, and edits the
	/// designer-facing components of an existing one without leaving the dashboard.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every other category is a ScriptableObject type, and the dashboard's create button makes
	/// an empty asset of that type. An NPC is a prefab, and an empty prefab is not an NPC — it
	/// needs a race, an archetype and an attribute database before it can even stand up. So this
	/// category loads prefabs instead of assets, and its create button opens a form that builds
	/// a complete prefab through <see cref="NPCPrefabFactory"/> from a recipe pre-filled from
	/// whichever NPC is selected.
	/// </para>
	/// </remarks>
	public partial class FishMMODashboard
	{
		/// <summary>Sidebar name of the category.</summary>
		private const string NPC_CATEGORY = "NPCs";

		/// <summary>
		/// NPC prefabs found by the last scan. Scanning loads every prefab in the project, so the
		/// result is kept until something changes the asset list.
		/// </summary>
		private List<UnityEngine.Object> npcPrefabCache;

		/// <summary>The recipe the create form is editing.</summary>
		private NPCRecipe npcRecipe;

		/// <summary>Validation readout under the create form.</summary>
		private Label npcProblemsLabel;

		/// <summary>The create form's submit button; disabled while the recipe has problems.</summary>
		private Button npcCreateButton;

		/// <summary>Container the ability rows are rebuilt into.</summary>
		private VisualElement npcAbilityRows;

		/// <summary>Merchant template field, shown only for merchants.</summary>
		private VisualElement npcMerchantRow;

		/// <summary>
		/// Adds the NPCs category. Called first in the NPCs group, because a prefab is what a
		/// designer is ultimately trying to make; every other NPC category authors a part of one.
		/// </summary>
		private void RegisterNPCCategory()
		{
			categories.Add(new TemplateCategory
			{
				DisplayName = NPC_CATEGORY,
				Group = "NPCs",
				DefaultAssetDirectory = NPCPrefabFactory.DEFAULT_FOLDER,
				// Opening the category always rescans; the cache only serves the sidebar count.
				LoadAssets = () =>
				{
					InvalidateNPCPrefabCache();
					return new List<UnityEngine.Object>(LoadNPCPrefabs());
				},
				CountAssets = () => LoadNPCPrefabs().Count,
				CreateAsset = () => ShowNPCCreateInspector(SelectedNPCPrefab()),
				GetGroupLabel = asset => NPCPrefabFactory.Classify(asset as GameObject),
				GetSortOrder = asset => NPCPrefabFactory.SortOrder(asset as GameObject),
			});
		}

		/// <summary>
		/// The cached NPC prefab list, scanning the project when nothing is cached.
		/// </summary>
		/// <returns>Every NPC prefab, sorted by name.</returns>
		private List<UnityEngine.Object> LoadNPCPrefabs()
		{
			if (npcPrefabCache == null)
			{
				bool includeLocal = EditorPrefs.GetBool("FishMMOEnableLocalDirectory", false);
				npcPrefabCache = new List<UnityEngine.Object>();
				foreach (GameObject prefab in NPCPrefabFactory.FindNPCPrefabs(includeLocal))
				{
					npcPrefabCache.Add(prefab);
				}
			}
			return npcPrefabCache;
		}

		/// <summary>
		/// Forgets the cached prefab list so the next load rescans.
		/// </summary>
		private void InvalidateNPCPrefabCache()
		{
			npcPrefabCache = null;
		}

		/// <summary>
		/// True when the NPCs category is the one on screen.
		/// </summary>
		private bool IsNPCCategorySelected()
		{
			return selectedCategoryIndex >= 0 &&
				selectedCategoryIndex < categories.Count &&
				categories[selectedCategoryIndex].DisplayName == NPC_CATEGORY;
		}

		/// <summary>
		/// Unity's notice that assets were created, deleted, moved or imported. NPC prefabs are
		/// made in many places — the Project window, a duplicate, a generator, a git pull — and
		/// the category should show them without anyone pressing anything. Unity refreshes the
		/// asset database when the editor regains focus, so external changes arrive here too.
		/// </summary>
		private void OnProjectChange()
		{
			RescanNPCPrefabs();
		}

		/// <summary>
		/// Drops the cache and, if the NPCs category is showing, reloads its list in place while
		/// keeping the current selection where the selected prefab still exists.
		/// </summary>
		private void RescanNPCPrefabs()
		{
			// CreateGUI has not run yet, or the window is being torn down.
			if (categories.Count == 0 || entityList == null)
			{
				return;
			}

			InvalidateNPCPrefabCache();

			if (!IsNPCCategorySelected())
			{
				BuildCategoryList();
				return;
			}

			UnityEngine.Object selected = selectedAssetIndex >= 0 && selectedAssetIndex < filteredAssets.Count
				? filteredAssets[selectedAssetIndex]
				: null;

			selectedAssetIndex = -1;
			LoadAssetsForCategory(categories[selectedCategoryIndex]);
			RefreshEntityList();
			BuildCategoryList();

			if (selected == null)
			{
				return;
			}

			// The selection indexes the filtered list, which RefreshEntityList has just rebuilt.
			int index = filteredAssets.IndexOf(selected);
			if (index >= 0)
			{
				selectedAssetIndex = index;
				BuildEntityList();
			}
			else
			{
				ClearInspector();
			}
		}

		/// <summary>
		/// The NPC prefab currently selected in the entity list, or null.
		/// </summary>
		/// <returns>The prefab, or null when nothing NPC-shaped is selected.</returns>
		private GameObject SelectedNPCPrefab()
		{
			if (selectedAssetIndex < 0 || selectedAssetIndex >= filteredAssets.Count)
			{
				return null;
			}
			GameObject prefab = filteredAssets[selectedAssetIndex] as GameObject;
			return prefab != null && prefab.GetComponent<NPC>() != null ? prefab : null;
		}

		/// <summary>
		/// True when an asset should be shown through the NPC inspector rather than the generic one.
		/// </summary>
		/// <param name="asset">Any asset.</param>
		/// <returns>True for a prefab whose root carries an <see cref="NPC"/>.</returns>
		private static bool IsNPCPrefab(UnityEngine.Object asset)
		{
			GameObject prefab = asset as GameObject;
			return prefab != null && prefab.GetComponent<NPC>() != null;
		}

		// ── Inspector: existing NPC ─────────────────────────────────────────────────────

		/// <summary>
		/// Shows an NPC prefab as the handful of components a designer edits, each with its full
		/// inspector, plus the summary a spawner author needs at a glance.
		/// </summary>
		/// <param name="prefab">The NPC prefab.</param>
		private void ShowNPCInspector(GameObject prefab)
		{
			ClearInspector();

			if (prefab == null)
			{
				return;
			}

			if (inspectorHeader != null)
			{
				inspectorHeader.text = prefab.name;
			}

			BuildNPCSummarySection(prefab);

			/* The generic path creates one Editor for the asset, which for a prefab is the
			 * GameObject header and nothing else. What a designer edits lives on components. */
			AddNPCComponentSection(prefab.GetComponent<NPC>(), "NPC — loot, corpse, abilities");
			AddNPCComponentSection(prefab.GetComponent<AIController>(), "AI Controller — archetype, boss script");
			AddNPCComponentSection(prefab.GetComponent<FactionController>(), "Faction — race, aggression");
			AddNPCComponentSection(prefab.GetComponent<CharacterAttributeController>(), "Attributes");
			AddNPCComponentSection(prefab.GetComponent<Interactable>(), "Interaction");
			AddNPCComponentSection(prefab.GetComponent<SceneObjectNamer>(), "Naming");
			AddNPCComponentSection(prefab.GetComponent<MapMarker>(), "Map Marker");

			Button saveButton = new Button(() =>
			{
				EditorUtility.SetDirty(prefab);
				AssetDatabase.SaveAssets();
				SetStatus($"Saved: {prefab.name}");
			});
			saveButton.text = "Save Prefab";
			saveButton.style.marginTop = 8;
			saveButton.style.height = 28;
			saveButton.style.backgroundColor = new Color(0.24f, 0.43f, 0.24f, 1f);
			saveButton.style.color = new Color(0.75f, 0.95f, 0.75f, 1f);
			inspectorContent.Add(saveButton);

			Button likeButton = new Button(() => ShowNPCCreateInspector(prefab));
			likeButton.text = "New NPC Like This";
			likeButton.style.marginTop = 4;
			likeButton.style.height = 26;
			likeButton.style.backgroundColor = new Color(0.22f, 0.30f, 0.45f, 1f);
			likeButton.style.color = new Color(0.80f, 0.88f, 1f, 1f);
			inspectorContent.Add(likeButton);

			Button openButton = new Button(() => AssetDatabase.OpenAsset(prefab));
			openButton.text = "Open Prefab";
			openButton.style.marginTop = 4;
			openButton.style.height = 24;
			inspectorContent.Add(openButton);

			Button selectButton = new Button(() =>
			{
				Selection.activeObject = prefab;
				EditorGUIUtility.PingObject(prefab);
			});
			selectButton.text = "Select in Project";
			selectButton.style.marginTop = 4;
			selectButton.style.height = 24;
			inspectorContent.Add(selectButton);
		}

		/// <summary>
		/// The at-a-glance rows: what kind of NPC this is and which assets define it.
		/// </summary>
		/// <param name="prefab">The NPC prefab.</param>
		private void BuildNPCSummarySection(GameObject prefab)
		{
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(prefab);
			VisualElement section = CreateConstantsSection("Summary");

			AddConstantRow(section, "Kind", NPCPrefabFactory.Classify(prefab));
			AddConstantRow(section, "Race", recipe.Race != null ? recipe.Race.name : "— none —");
			AddConstantRow(section, "Archetype", recipe.Archetype != null ? recipe.Archetype.name : "— none —");
			AddConstantRow(section, "Attributes", recipe.AttributeDatabase != null ? recipe.AttributeDatabase.name : "— none —");
			AddConstantRow(section, "Attribute Bonuses", recipe.AttributeBonuses != null ? recipe.AttributeBonuses.name : "— none —");
			AddConstantRow(section, "Loot Table", recipe.LootTable != null ? recipe.LootTable.name : "— none —");
			AddConstantRow(section, "Abilities", recipe.Abilities.Count.ToString());
			AddConstantRow(section, "Interaction", recipe.Interaction.ToString());
			AddConstantRow(section, "Path", AssetDatabase.GetAssetPath(prefab));

			List<string> problems = DescribeNPCProblems(prefab, recipe);
			if (problems.Count > 0)
			{
				Label warning = new Label(string.Join("\n", problems));
				warning.style.color = new Color(1f, 0.75f, 0.4f, 1f);
				warning.style.whiteSpace = WhiteSpace.Normal;
				warning.style.marginTop = 4;
				section.Add(warning);
			}

			inspectorContent.Add(section);
		}

		/// <summary>
		/// The things an existing prefab can have wrong that make it spawn and do nothing.
		/// </summary>
		/// <param name="prefab">The NPC prefab.</param>
		/// <param name="recipe">Its recipe.</param>
		/// <returns>One line per problem.</returns>
		private static List<string> DescribeNPCProblems(GameObject prefab, NPCRecipe recipe)
		{
			List<string> problems = new List<string>();
			if (recipe.Archetype == null)
			{
				problems.Add("No AI archetype: the controller has no states and the NPC never ticks.");
			}
			if (recipe.Race == null)
			{
				problems.Add("No race: no model is loaded and the NPC belongs to no faction.");
			}
			if (recipe.AttributeDatabase == null)
			{
				problems.Add("No attribute database: the NPC has no health.");
			}
			if (recipe.Archetype != null && recipe.Archetype.AttackingState != null && recipe.Abilities.Count == 0 && prefab.GetComponent<Pet>() == null)
			{
				problems.Add("Combat archetype but no abilities: it will chase its target and never strike.");
			}
			if (recipe.Interaction == NPCInteraction.Merchant && recipe.MerchantTemplate == null)
			{
				problems.Add("Merchant with no merchant template: nothing to sell.");
			}
			return problems;
		}

		/// <summary>
		/// Adds a collapsible full inspector for one component of the prefab.
		/// </summary>
		/// <param name="component">The component, or null to add nothing.</param>
		/// <param name="title">Foldout title.</param>
		private void AddNPCComponentSection(Component component, string title)
		{
			if (component == null)
			{
				return;
			}

			Editor editor = Editor.CreateEditor(component);
			if (editor == null)
			{
				return;
			}
			activeEditors.Add(editor);

			Foldout foldout = new Foldout();
			foldout.text = title;
			foldout.value = true;
			foldout.AddToClassList("constants-section");
			foldout.Add(new InspectorElement(editor));
			inspectorContent.Add(foldout);
		}

		// ── Inspector: create form ──────────────────────────────────────────────────────

		/// <summary>
		/// Shows the create form, pre-filled from a prefab.
		/// </summary>
		/// <param name="like">The prefab to start from; null picks the first monster in the project.</param>
		private void ShowNPCCreateInspector(GameObject like)
		{
			ClearInspector();

			if (inspectorHeader != null)
			{
				inspectorHeader.text = "New NPC";
			}

			if (like == null)
			{
				like = DefaultNPCBasePrefab();
			}

			npcRecipe = NPCPrefabFactory.RecipeFrom(like);
			if (npcRecipe == null)
			{
				Label empty = new Label("No NPC prefab exists to use as a base.\nCreate one NPC prefab by hand, then every further NPC can be designed here.");
				empty.AddToClassList("empty-state-label");
				empty.style.whiteSpace = WhiteSpace.Normal;
				inspectorContent.Add(empty);
				return;
			}

			npcRecipe.Name = "New NPC";
			BuildNPCRecipeForm();
			SetStatus($"New NPC based on '{like.name}'");
		}

		/// <summary>
		/// The prefab the create form starts from when nothing is selected.
		/// </summary>
		/// <returns>The first monster, else the first NPC of any kind, else null.</returns>
		private GameObject DefaultNPCBasePrefab()
		{
			List<UnityEngine.Object> prefabs = LoadNPCPrefabs();
			for (int i = 0; i < prefabs.Count; i++)
			{
				GameObject prefab = prefabs[i] as GameObject;
				if (NPCPrefabFactory.Classify(prefab) == NPCPrefabFactory.KIND_MONSTER)
				{
					return prefab;
				}
			}
			return prefabs.Count > 0 ? prefabs[0] as GameObject : null;
		}

		/// <summary>
		/// Builds the recipe form into the inspector panel.
		/// </summary>
		private void BuildNPCRecipeForm()
		{
			Label info = new Label(
				"An NPC is a race (model and faction), an AI archetype (brain), an attribute set, and a kit of " +
				"abilities and loot. Everything else on the prefab is cloned from the base prefab.");
			info.style.fontSize = 10;
			info.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			info.style.whiteSpace = WhiteSpace.Normal;
			info.style.marginBottom = 8;
			inspectorContent.Add(info);

			// ── Identity ──
			VisualElement identity = CreateConstantsSection("Identity");

			TextField nameField = new TextField("Name");
			nameField.value = npcRecipe.Name;
			nameField.RegisterValueChangedCallback(evt =>
			{
				npcRecipe.Name = evt.newValue;
				RefreshNPCValidation();
			});
			identity.Add(nameField);

			TextField folderField = new TextField("Folder");
			folderField.value = npcRecipe.Folder;
			folderField.RegisterValueChangedCallback(evt =>
			{
				npcRecipe.Folder = evt.newValue;
				RefreshNPCValidation();
			});
			identity.Add(folderField);

			ObjectField baseField = NPCObjectField<GameObject>("Base Prefab", npcRecipe.BasePrefab, value =>
			{
				/* Changing the base re-reads every slot from it, keeping only the name and
				 * folder the designer typed — the base is "make me one of these". */
				GameObject prefab = value as GameObject;
				if (prefab == null || prefab.GetComponent<NPC>() == null)
				{
					npcRecipe.BasePrefab = prefab;
					RefreshNPCValidation();
					return;
				}

				string name = npcRecipe.Name;
				string folder = npcRecipe.Folder;
				npcRecipe = NPCPrefabFactory.RecipeFrom(prefab);
				npcRecipe.Name = name;
				npcRecipe.Folder = folder;
				ShowNPCCreateFormAgain();
			});
			identity.Add(baseField);

			inspectorContent.Add(identity);

			// ── Definition ──
			VisualElement definition = CreateConstantsSection("Definition");

			definition.Add(NPCObjectField<RaceTemplate>("Race", npcRecipe.Race, value =>
			{
				npcRecipe.Race = value as RaceTemplate;
				RefreshNPCValidation();
			}));

			definition.Add(NPCObjectField<AIArchetypeTemplate>("AI Archetype", npcRecipe.Archetype, value =>
			{
				npcRecipe.Archetype = value as AIArchetypeTemplate;
				RefreshNPCValidation();
			}));

			definition.Add(NPCObjectField<CharacterAttributeTemplateDatabase>("Attribute Database", npcRecipe.AttributeDatabase, value =>
			{
				npcRecipe.AttributeDatabase = value as CharacterAttributeTemplateDatabase;
				RefreshNPCValidation();
			}));

			definition.Add(NPCObjectField<NPCAttributeDatabase>("Attribute Bonuses", npcRecipe.AttributeBonuses, value =>
			{
				npcRecipe.AttributeBonuses = value as NPCAttributeDatabase;
				RefreshNPCValidation();
			}));

			definition.Add(NPCObjectField<LootTableTemplate>("Loot Table", npcRecipe.LootTable, value =>
			{
				npcRecipe.LootTable = value as LootTableTemplate;
				RefreshNPCValidation();
			}));

			Toggle aggressiveToggle = new Toggle("Aggressive");
			aggressiveToggle.tooltip = "Treats every other faction as hostile.";
			aggressiveToggle.value = npcRecipe.IsAggressive;
			aggressiveToggle.RegisterValueChangedCallback(evt => npcRecipe.IsAggressive = evt.newValue);
			definition.Add(aggressiveToggle);

			Toggle charmableToggle = new Toggle("Charmable");
			charmableToggle.value = npcRecipe.IsCharmable;
			charmableToggle.RegisterValueChangedCallback(evt => npcRecipe.IsCharmable = evt.newValue);
			definition.Add(charmableToggle);

			inspectorContent.Add(definition);

			// ── Interaction ──
			VisualElement interaction = CreateConstantsSection("Interaction");

			EnumField interactionField = new EnumField("Role", npcRecipe.Interaction);
			interactionField.RegisterValueChangedCallback(evt =>
			{
				npcRecipe.Interaction = (NPCInteraction)evt.newValue;
				npcMerchantRow.style.display = npcRecipe.Interaction == NPCInteraction.Merchant ? DisplayStyle.Flex : DisplayStyle.None;
				RefreshNPCValidation();
			});
			interaction.Add(interactionField);

			npcMerchantRow = NPCObjectField<MerchantTemplate>("Merchant Template", npcRecipe.MerchantTemplate, value =>
			{
				npcRecipe.MerchantTemplate = value as MerchantTemplate;
				RefreshNPCValidation();
			});
			npcMerchantRow.style.display = npcRecipe.Interaction == NPCInteraction.Merchant ? DisplayStyle.Flex : DisplayStyle.None;
			interaction.Add(npcMerchantRow);

			inspectorContent.Add(interaction);

			// ── Abilities ──
			VisualElement abilities = CreateConstantsSection("Abilities");

			npcAbilityRows = new VisualElement();
			abilities.Add(npcAbilityRows);
			RebuildNPCAbilityRows();

			Button addAbility = new Button(() =>
			{
				npcRecipe.Abilities.Add(null);
				RebuildNPCAbilityRows();
				RefreshNPCValidation();
			});
			addAbility.text = "+ Add Ability";
			addAbility.style.marginTop = 4;
			abilities.Add(addAbility);

			inspectorContent.Add(abilities);

			// ── Output ──
			VisualElement output = CreateConstantsSection("Output");

			Toggle addressableToggle = new Toggle("Register with Addressables");
			addressableToggle.tooltip = "Adds the prefab to the base prefab's Addressables group so the servers can load it. Leave on.";
			addressableToggle.value = npcRecipe.RegisterAddressable;
			addressableToggle.RegisterValueChangedCallback(evt => npcRecipe.RegisterAddressable = evt.newValue);
			output.Add(addressableToggle);

			npcProblemsLabel = new Label();
			npcProblemsLabel.style.whiteSpace = WhiteSpace.Normal;
			npcProblemsLabel.style.marginTop = 4;
			output.Add(npcProblemsLabel);

			npcCreateButton = new Button(CreateNPCFromForm);
			npcCreateButton.text = "Create NPC";
			npcCreateButton.style.marginTop = 8;
			npcCreateButton.style.height = 28;
			npcCreateButton.style.backgroundColor = new Color(0.24f, 0.43f, 0.24f, 1f);
			npcCreateButton.style.color = new Color(0.75f, 0.95f, 0.75f, 1f);
			output.Add(npcCreateButton);

			inspectorContent.Add(output);

			RefreshNPCValidation();
		}

		/// <summary>
		/// Rebuilds the form around the current recipe, after the base prefab changed.
		/// </summary>
		private void ShowNPCCreateFormAgain()
		{
			NPCRecipe keep = npcRecipe;
			ClearInspector();
			if (inspectorHeader != null)
			{
				inspectorHeader.text = "New NPC";
			}
			npcRecipe = keep;
			BuildNPCRecipeForm();
		}

		/// <summary>
		/// Re-renders the ability rows from the recipe.
		/// </summary>
		private void RebuildNPCAbilityRows()
		{
			npcAbilityRows.Clear();

			if (npcRecipe.Abilities.Count == 0)
			{
				Label none = new Label("No abilities. A combat NPC with none will chase its target and never strike.");
				none.style.fontSize = 10;
				none.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
				none.style.whiteSpace = WhiteSpace.Normal;
				npcAbilityRows.Add(none);
				return;
			}

			for (int i = 0; i < npcRecipe.Abilities.Count; i++)
			{
				int index = i;
				VisualElement row = new VisualElement();
				row.style.flexDirection = FlexDirection.Row;
				row.style.alignItems = Align.Center;

				ObjectField field = NPCObjectField<AbilityTemplate>(string.Empty, npcRecipe.Abilities[index], value =>
				{
					npcRecipe.Abilities[index] = value as AbilityTemplate;
					RefreshNPCValidation();
				});
				field.style.flexGrow = 1;
				row.Add(field);

				Button remove = new Button(() =>
				{
					npcRecipe.Abilities.RemoveAt(index);
					RebuildNPCAbilityRows();
					RefreshNPCValidation();
				});
				remove.text = "−";
				remove.style.width = 24;
				row.Add(remove);

				npcAbilityRows.Add(row);
			}
		}

		/// <summary>
		/// Runs the recipe through <see cref="NPCPrefabFactory.Validate"/> and shows the result.
		/// </summary>
		private void RefreshNPCValidation()
		{
			if (npcProblemsLabel == null || npcCreateButton == null)
			{
				return;
			}

			List<string> problems = new List<string>();
			bool valid = NPCPrefabFactory.Validate(npcRecipe, problems);

			npcProblemsLabel.text = valid
				? $"Will create: {NPCPrefabFactory.TargetPath(npcRecipe)}"
				: string.Join("\n", problems);
			npcProblemsLabel.style.color = valid
				? new Color(0.6f, 0.8f, 0.6f, 1f)
				: new Color(1f, 0.55f, 0.45f, 1f);
			npcCreateButton.SetEnabled(valid);
		}

		/// <summary>
		/// Builds the prefab and selects it in the list.
		/// </summary>
		private void CreateNPCFromForm()
		{
			GameObject created;
			try
			{
				created = NPCPrefabFactory.Create(npcRecipe);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[FishMMODashboard] Could not create NPC: {ex.Message}");
				SetStatus("NPC creation failed — see console");
				return;
			}

			string createdPath = AssetDatabase.GetAssetPath(created);
			InvalidateNPCPrefabCache();
			ReloadCurrentCategory();

			for (int i = 0; i < filteredAssets.Count; i++)
			{
				if (AssetDatabase.GetAssetPath(filteredAssets[i]) == createdPath)
				{
					OnAssetSelected(i);
					break;
				}
			}

			SetStatus($"Created: {createdPath}");
		}

		/// <summary>
		/// An asset picker restricted to one type, wired to a setter.
		/// </summary>
		/// <typeparam name="T">The asset type.</typeparam>
		/// <param name="label">Field label.</param>
		/// <param name="value">Initial value.</param>
		/// <param name="onChange">Receives the new value.</param>
		/// <returns>The field.</returns>
		private static ObjectField NPCObjectField<T>(string label, UnityEngine.Object value, Action<UnityEngine.Object> onChange) where T : UnityEngine.Object
		{
			ObjectField field = new ObjectField(label);
			field.objectType = typeof(T);
			field.allowSceneObjects = false;
			field.value = value;
			field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
			return field;
		}
	}
}
#endif
