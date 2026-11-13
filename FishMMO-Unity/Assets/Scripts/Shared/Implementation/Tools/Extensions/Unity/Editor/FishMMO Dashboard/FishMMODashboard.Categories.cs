#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.Shared
{
	/// <summary>
	/// Defines a single template category entry for the dashboard sidebar.
	/// </summary>
	public sealed class TemplateCategory
	{
		/// <summary>Display name shown in the sidebar.</summary>
		public string DisplayName;

		/// <summary>Optional group header (e.g., "Core", "Character", "Interactables").</summary>
		public string Group;

		/// <summary>
		/// The ScriptableObject type to search for in the AssetDatabase.
		/// Null for special categories like Game Settings.
		/// </summary>
		public Type AssetType;

		/// <summary>
		/// Default directory path (relative to Assets/) where new assets of this type are created.
		/// </summary>
		public string DefaultAssetDirectory;

		/// <summary>
		/// If true, this category has no ScriptableObject type and uses custom rendering.
		/// </summary>
		public bool IsSpecial;

		/// <summary>
		/// CreateAssetMenu menuName used by the ScriptableObject, needed for asset creation fallback.
		/// </summary>
		public string CreateAssetMenuName;

		/// <summary>
		/// Optional delegate that returns a group label for an asset, used for sorted sub-headers.
		/// </summary>
		public Func<UnityEngine.Object, string> GetGroupLabel;

		/// <summary>
		/// Optional delegate that returns a sort key for an asset within its group.
		/// Lower values sort first. Defaults to 0 if null.
		/// </summary>
		public Func<UnityEngine.Object, int> GetSortOrder;

		/// <summary>
		/// Concrete ScriptableObject types that can be created for this category.
		/// Used when AssetType is abstract and has multiple concrete subtypes.
		/// Each entry is (displayName, concreteType).
		/// </summary>
		public List<(string Name, Type Type)> ConcreteTypes;

		/// <summary>
		/// For a special category: builds its page into the inspector area. Lets a provider bring its own page
		/// without the dashboard naming it; the dashboard clears the area and sets the header first.
		/// </summary>
		public Action<VisualElement> BuildPage;

		/// <summary>
		/// Optional loader that replaces the <see cref="AssetType"/> search. For categories whose
		/// entries are not a ScriptableObject type — NPC prefabs — where "everything of type T"
		/// is not the right question.
		/// </summary>
		public Func<List<UnityEngine.Object>> LoadAssets;

		/// <summary>
		/// Optional counter for the sidebar badge, paired with <see cref="LoadAssets"/>.
		/// </summary>
		public Func<int> CountAssets;

		/// <summary>
		/// Optional handler for the create button, replacing the empty-asset default. A category
		/// whose entries need several decisions before they are usable opens a form here.
		/// </summary>
		public Action CreateAsset;
	}

	public partial class FishMMODashboard
	{
		/// <summary>
		/// Registers every template category, asking each <see cref="IDashboardCategoryProvider"/> in order.
		/// </summary>
		private void RegisterCategories()
		{
			categories.Clear();
			var list = new DashboardCategoryList(this);
			foreach (IDashboardCategoryProvider provider in DashboardCategoryProviders.All)
			{
				provider.AddCategories(list);
			}
		}

		/// <summary>
		/// Builds (or rebuilds) the category sidebar UI.
		/// </summary>
		private void BuildCategoryList()
		{
			if (categoryList == null) return;

			categoryList.Clear();

			string lastGroup = null;
			bool hasGlobalFilter = !string.IsNullOrWhiteSpace(globalFilter);

			for (int i = 0; i < categories.Count; i++)
			{
				TemplateCategory cat = categories[i];

				if (hasGlobalFilter &&
					cat.DisplayName.IndexOf(globalFilter, StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}

				// Group header
				if (cat.Group != lastGroup)
				{
					lastGroup = cat.Group;
					VisualElement groupHeader = new VisualElement();
					groupHeader.AddToClassList("category-group-header");

					Label groupLabel = new Label(cat.Group);
					groupLabel.AddToClassList("category-group-label");
					groupHeader.Add(groupLabel);

					categoryList.Add(groupHeader);
				}

				int index = i;
				VisualElement item = new VisualElement();
				item.AddToClassList("category-item");
				if (index == selectedCategoryIndex)
				{
					item.AddToClassList("category-item--selected");
				}

				Label nameLabel = new Label(cat.DisplayName);
				nameLabel.AddToClassList("category-label");
				item.Add(nameLabel);

				// Show asset count for non-special categories
				if (!cat.IsSpecial && (cat.CountAssets != null || cat.AssetType != null))
				{
					int count = cat.CountAssets != null ? cat.CountAssets() : CountAssetsOfType(cat.AssetType);
					Label countLabel = new Label($"({count})");
					countLabel.AddToClassList("category-count");
					item.Add(countLabel);
				}

				item.RegisterCallback<ClickEvent>(evt => OnCategorySelected(index));

				categoryList.Add(item);
			}
		}

		/// <summary>
		/// Counts assets of the given type in the project.
		/// Excludes assets under Assets/LOCAL when Enable Local Directory is disabled.
		/// </summary>
		private int CountAssetsOfType(Type type)
		{
			string[] guids = AssetDatabase.FindAssets($"t:{type.Name}");

			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory", false))
				return guids.Length;

			int count = 0;
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (!path.Replace('\\', '/').StartsWith("Assets/LOCAL/", StringComparison.OrdinalIgnoreCase))
				{
					count++;
				}
			}
			return count;
		}

		/// <summary>
		/// Called when a category item is clicked.
		/// </summary>
		private const string SelectedCategoryKey = "FishMMODashboard.SelectedCategory";

		private void OnCategorySelected(int index)
		{
			if (index < 0 || index >= categories.Count) return;

			selectedCategoryIndex = index;
			SessionState.SetInt(SelectedCategoryKey, index);
			selectedAssetIndex = -1;
			entityFilter = string.Empty;

			if (entitySearchField != null)
			{
				entitySearchField.value = string.Empty;
			}

			TemplateCategory cat = categories[index];

			if (entityPanelHeader != null)
			{
				entityPanelHeader.text = cat.DisplayName;
			}

			bool canCreate = !cat.IsSpecial && (cat.AssetType != null || cat.CreateAsset != null);
			if (createButton != null)
			{
				createButton.SetEnabled(canCreate);
			}

			BuildCategoryList(); // Refresh selection highlight

			if (cat.IsSpecial)
			{
				currentAssets.Clear();
				filteredAssets.Clear();
				ClearEntityList();

				if (cat.BuildPage != null)
				{
					ClearInspector();
					if (inspectorHeader != null)
					{
						inspectorHeader.text = cat.DisplayName;
					}
					cat.BuildPage(inspectorContent);
				}
				else if (cat.DisplayName == "Game Settings")
				{
					ShowGameSettingsInspector();
				}
				else if (cat.DisplayName == "Build")
				{
					ShowBuildInspector();
				}
				else if (cat.DisplayName == "Version")
				{
					ShowVersionInspector();
				}
				else if (cat.DisplayName == "Patch Generator")
				{
					ShowPatchGeneratorInspector();
				}
				else if (cat.DisplayName == NAME_GENERATION_CATEGORY)
				{
					ShowNameGenerationPage();
				}
				else if (IsWorldDesignPage(cat.DisplayName))
				{
					ShowWorldDesignPage(cat.DisplayName);
				}
				else if (IsToolPage(cat.DisplayName))
				{
					ShowToolPage(cat.DisplayName);
				}
			}
			else
			{
				LoadAssetsForCategory(cat);
				RefreshEntityList();
				ClearInspector();
				ShowCategoryLanding(cat);
			}

			SetStatus($"Category: {cat.DisplayName}");
		}

		/// <summary>
		/// Loads all ScriptableObject assets for the given category into currentAssets.
		/// Searches the entire project for the type. Applies custom sort order if defined.
		/// </summary>
		private void LoadAssetsForCategory(TemplateCategory cat)
		{
			currentAssets.Clear();

			if (cat.LoadAssets != null)
			{
				currentAssets.AddRange(cat.LoadAssets());
			}
			else
			{
				if (cat.AssetType == null) return;

				string[] guids = AssetDatabase.FindAssets($"t:{cat.AssetType.Name}");
				bool skipLocal = !EditorPrefs.GetBool("FishMMOEnableLocalDirectory", false);

				for (int i = 0; i < guids.Length; i++)
				{
					string path = AssetDatabase.GUIDToAssetPath(guids[i]);
					if (skipLocal && path.Replace('\\', '/').StartsWith("Assets/LOCAL/", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, cat.AssetType);
					if (asset != null)
					{
						currentAssets.Add(asset);
					}
				}
			}

			// Sort by custom group/sort order if defined, then by name
			if (cat.GetSortOrder != null)
			{
				currentAssets.Sort((a, b) =>
				{
					int orderA = cat.GetSortOrder(a);
					int orderB = cat.GetSortOrder(b);
					int cmp = orderA.CompareTo(orderB);
					return cmp != 0 ? cmp : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
				});
			}
			else
			{
				currentAssets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
			}
		}

		/// <summary>
		/// Applies entity filter and rebuild the entity list UI.
		/// </summary>
		private void RefreshEntityList()
		{
			filteredAssets.Clear();

			bool hasFilter = !string.IsNullOrWhiteSpace(entityFilter);

			for (int i = 0; i < currentAssets.Count; i++)
			{
				UnityEngine.Object asset = currentAssets[i];
				if (hasFilter && asset.name.IndexOf(entityFilter, StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				filteredAssets.Add(asset);
			}

			BuildEntityList();

			if (entityCountLabel != null)
			{
				entityCountLabel.text = hasFilter
					? $"{filteredAssets.Count} / {currentAssets.Count} assets"
					: $"{currentAssets.Count} assets";
			}
		}

		/// <summary>
		/// Builds the entity list UI from filteredAssets.
		/// Inserts group sub-headers when the current category defines GetGroupLabel.
		/// </summary>
		private void BuildEntityList()
		{
			if (entityList == null) return;

			entityList.Clear();

			if (filteredAssets.Count == 0)
			{
				VisualElement empty = new VisualElement();
				empty.AddToClassList("empty-state");
				Label emptyLabel = new Label("No assets found.\nUse + to create one.");
				emptyLabel.AddToClassList("empty-state-label");
				empty.Add(emptyLabel);
				entityList.Add(empty);
				return;
			}

			// Determine if we should show group labels
			TemplateCategory cat = (selectedCategoryIndex >= 0 && selectedCategoryIndex < categories.Count)
				? categories[selectedCategoryIndex]
				: null;
			Func<UnityEngine.Object, string> getLabel = cat?.GetGroupLabel;

			string lastGroupLabel = null;

			for (int i = 0; i < filteredAssets.Count; i++)
			{
				UnityEngine.Object asset = filteredAssets[i];
				int index = i;

				// Insert group sub-header if the group label changed
				if (getLabel != null)
				{
					string groupLabel = getLabel(asset);
					if (groupLabel != lastGroupLabel)
					{
						lastGroupLabel = groupLabel;

						VisualElement groupHeader = new VisualElement();
						groupHeader.AddToClassList("entity-group-header");

						Label groupLabelEl = new Label(groupLabel);
						groupLabelEl.AddToClassList("entity-group-label");
						groupHeader.Add(groupLabelEl);

						entityList.Add(groupHeader);
					}
				}

				VisualElement item = new VisualElement();
				item.AddToClassList("entity-item");
				if (index == selectedAssetIndex)
				{
					item.AddToClassList("entity-item--selected");
				}

				// Thumbnail
				Texture2D preview = AssetPreview.GetMiniThumbnail(asset);
				if (preview != null)
				{
					Image icon = new Image();
					icon.image = preview;
					icon.AddToClassList("entity-icon");
					item.Add(icon);
				}

				// Name label
				Label nameLabel = new Label(asset.name);
				nameLabel.AddToClassList("entity-label");
				item.Add(nameLabel);

				// Tag label for group info
				if (getLabel != null)
				{
					string tag = getLabel(asset);
					if (!string.IsNullOrEmpty(tag))
					{
						Label tagLabel = new Label(tag);
						tagLabel.AddToClassList("entity-tag");
						item.Add(tagLabel);
					}
				}

				item.RegisterCallback<ClickEvent>(evt =>
				{
					if (evt.clickCount == 2)
					{
						// Double-click: ping in project
						EditorGUIUtility.PingObject(asset);
					}
					else
					{
						OnAssetSelected(index);
					}
				});

				// Context menu
				item.RegisterCallback<ContextClickEvent>(evt =>
				{
					OnAssetSelected(index);
					ShowEntityContextMenu(asset);
					evt.StopPropagation();
				});

				entityList.Add(item);
			}
		}

		/// <summary>
		/// Clears the entity list UI.
		/// </summary>
		private void ClearEntityList()
		{
			if (entityList != null)
			{
				entityList.Clear();
			}

			if (entityCountLabel != null)
			{
				entityCountLabel.text = string.Empty;
			}
		}

		/// <summary>
		/// Called when an entity item is clicked.
		/// </summary>
		private void OnAssetSelected(int index)
		{
			if (index < 0 || index >= filteredAssets.Count) return;

			selectedAssetIndex = index;
			BuildEntityList(); // Refresh selection highlight

			UnityEngine.Object asset = filteredAssets[index];
			ShowAssetInspector(asset);
			SetStatus($"Selected: {asset.name}");
		}

		/// <summary>
		/// Shows a context menu for the given entity asset.
		/// </summary>
		private void ShowEntityContextMenu(UnityEngine.Object asset)
		{
			GenericMenu menu = new GenericMenu();

			menu.AddItem(new GUIContent("Focus in Project"), false, () =>
			{
				Selection.activeObject = asset;
				EditorGUIUtility.PingObject(asset);
			});

			menu.AddItem(new GUIContent("Duplicate"), false, () =>
			{
				DuplicateAsset(asset);
			});

			menu.AddSeparator("");

			menu.AddItem(new GUIContent("Rename"), false, () =>
			{
				RenameAsset(asset);
			});

			menu.AddSeparator("");

			menu.AddItem(new GUIContent("Delete"), false, () =>
			{
				DeleteAsset(asset);
			});

			menu.ShowAsContext();
		}

		/// <summary>
		/// Creates a new asset for the currently selected category.
		/// If the category has multiple concrete types, shows a selection menu first.
		/// </summary>
		private void OnCreateAssetClicked()
		{
			if (selectedCategoryIndex < 0 || selectedCategoryIndex >= categories.Count) return;

			TemplateCategory cat = categories[selectedCategoryIndex];
			if (cat.IsSpecial) return;

			// A category that needs a form rather than an empty asset.
			if (cat.CreateAsset != null)
			{
				cat.CreateAsset();
				return;
			}

			if (cat.AssetType == null) return;

			// If the category defines concrete subtypes, show a picker menu.
			if (cat.ConcreteTypes != null && cat.ConcreteTypes.Count > 0)
			{
				GenericMenu menu = new GenericMenu();
				for (int i = 0; i < cat.ConcreteTypes.Count; i++)
				{
					var entry = cat.ConcreteTypes[i];
					menu.AddItem(new GUIContent(entry.Name), false, () =>
					{
						CreateAssetOfType(cat, entry.Type, entry.Name);
					});
				}
				menu.ShowAsContext();
				return;
			}

			// Single concrete type — create directly.
			CreateAssetOfType(cat, cat.AssetType, cat.DisplayName);
		}

		/// <summary>
		/// Creates a new ScriptableObject asset of the specified type in the category directory.
		/// </summary>
		private void CreateAssetOfType(TemplateCategory cat, Type assetType, string displayName)
		{

			/* AI assets file themselves by type. The "AI States" category covers attacking,
			 * movement, pet and positioning states, which live in different sub-folders, so a
			 * single DefaultAssetDirectory would drop every new state in the parent folder and
			 * leave it for the organizer to sort out later. */
			string dir = AIAssetOrganizer.ResolveFolder(assetType) ?? cat.DefaultAssetDirectory;
			if (!AssetDatabase.IsValidFolder(dir))
			{
				CreateDirectoryRecursive(dir);
			}

			// Create instance
			ScriptableObject instance = ScriptableObject.CreateInstance(assetType);
			string assetName = $"New {displayName}";

			// Ensure unique name
			string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{assetName}.asset");

			AssetDatabase.CreateAsset(instance, assetPath);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			// Reload and select
			LoadAssetsForCategory(cat);
			RefreshEntityList();

			// Find and select the new asset
			for (int i = 0; i < filteredAssets.Count; i++)
			{
				string path = AssetDatabase.GetAssetPath(filteredAssets[i]);
				if (path == assetPath)
				{
					OnAssetSelected(i);
					break;
				}
			}

			SetStatus($"Created: {assetPath}");
		}

		/// <summary>
		/// Duplicates the given asset in its current directory.
		/// </summary>
		private void DuplicateAsset(UnityEngine.Object asset)
		{
			string sourcePath = AssetDatabase.GetAssetPath(asset);
			string dir = System.IO.Path.GetDirectoryName(sourcePath);
			string name = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
			string ext = System.IO.Path.GetExtension(sourcePath);
			string newPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{name} Copy{ext}");

			if (AssetDatabase.CopyAsset(sourcePath, newPath))
			{
				/* A copied prefab is a new guid with no Addressables entry, and a server cannot
				 * spawn an NPC it cannot load. */
				if (IsNPCPrefab(asset))
				{
					NPCPrefabFactory.RegisterAddressableLike(newPath, sourcePath);
				}

				AssetDatabase.Refresh();
				ReloadCurrentCategory();
				SetStatus($"Duplicated: {newPath}");
			}
		}

		/// <summary>
		/// Renames the given asset via a dialog.
		/// </summary>
		private void RenameAsset(UnityEngine.Object asset)
		{
			string path = AssetDatabase.GetAssetPath(asset);
			string currentName = System.IO.Path.GetFileNameWithoutExtension(path);

			EditorInputDialog.Show("Rename Asset", "New Name:", currentName, result =>
			{
				if (!string.IsNullOrWhiteSpace(result) && result != currentName)
				{
					string error = AssetDatabase.RenameAsset(path, result);
					if (string.IsNullOrEmpty(error))
					{
						// An NPC's address is its name; keep the two together.
						if (IsNPCPrefab(asset))
						{
							NPCPrefabFactory.SyncAddress(AssetDatabase.GetAssetPath(asset));
						}

						AssetDatabase.Refresh();
						ReloadCurrentCategory();
						SetStatus($"Renamed to: {result}");
					}
					else
					{
						Debug.LogError($"[FishMMODashboard] Rename failed: {error}");
					}
				}
			});
		}

		/// <summary>
		/// Deletes the given asset with confirmation.
		/// </summary>
		private void DeleteAsset(UnityEngine.Object asset)
		{
			string path = AssetDatabase.GetAssetPath(asset);

			if (EditorUtility.DisplayDialog("Delete Asset",
				$"Are you sure you want to delete '{asset.name}'?\n\nPath: {path}",
				"Delete", "Cancel"))
			{
				AssetDatabase.DeleteAsset(path);
				AssetDatabase.Refresh();

				selectedAssetIndex = -1;
				ClearInspector();
				ReloadCurrentCategory();
				SetStatus($"Deleted: {path}");
			}
		}

		/// <summary>
		/// Reloads assets for the currently selected category.
		/// </summary>
		private void ReloadCurrentCategory()
		{
			InvalidateNPCPrefabCache();

			if (selectedCategoryIndex < 0 || selectedCategoryIndex >= categories.Count) return;

			TemplateCategory cat = categories[selectedCategoryIndex];
			if (!cat.IsSpecial)
			{
				LoadAssetsForCategory(cat);
				RefreshEntityList();
			}

			BuildCategoryList(); // Refresh counts
		}

		/// <summary>
		/// Creates a directory path recursively, creating each parent as needed.
		/// </summary>
		private static void CreateDirectoryRecursive(string path)
		{
			// Normalize separators
			path = path.Replace("\\", "/");

			string[] parts = path.Split('/');
			string current = parts[0];

			for (int i = 1; i < parts.Length; i++)
			{
				string next = current + "/" + parts[i];
				if (!AssetDatabase.IsValidFolder(next))
				{
					AssetDatabase.CreateFolder(current, parts[i]);
				}
				current = next;
			}
		}
	}

}
#endif