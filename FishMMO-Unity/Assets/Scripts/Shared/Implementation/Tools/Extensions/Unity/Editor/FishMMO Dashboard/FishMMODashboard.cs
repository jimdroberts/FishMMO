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
	/// FishMMO Dashboard — a unified editor window for browsing and configuring
	/// all game templates (Abilities, Items, Quests, etc.) and core game settings.
	/// Split into partial classes: Core (this file), Categories, and Inspector.
	/// </summary>
	public partial class FishMMODashboard : EditorWindow
	{
		private const string WindowTitle = "FishMMO Dashboard";
		private const string UxmlPath = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/FishMMO Dashboard/FishMMODashboard.uxml";

		/// <summary>Registered template categories keyed by display name.</summary>
		private readonly List<TemplateCategory> categories = new List<TemplateCategory>();

		/// <summary>Currently selected category index, or -1 if none.</summary>
		private int selectedCategoryIndex = -1;

		/// <summary>Assets discovered for the current category.</summary>
		private readonly List<UnityEngine.Object> currentAssets = new List<UnityEngine.Object>();

		/// <summary>Filtered view of currentAssets based on entity search.</summary>
		private readonly List<UnityEngine.Object> filteredAssets = new List<UnityEngine.Object>();

		/// <summary>Currently selected asset index within filteredAssets, or -1.</summary>
		private int selectedAssetIndex = -1;

		/// <summary>Global search filter from the toolbar.</summary>
		private string globalFilter = string.Empty;

		/// <summary>Entity-level search filter.</summary>
		private string entityFilter = string.Empty;

		// UI references
		private ToolbarSearchField searchField;
		private ToolbarSearchField entitySearchField;
		private VisualElement categoryList;
		private VisualElement entityList;
		private VisualElement inspectorContent;
		private Label entityPanelHeader;
		private Label entityCountLabel;
		private Label statusBar;
		private Label inspectorHeader;
		private Button createButton;

		// Resizer state
		private VisualElement categoryPanel;
		private VisualElement entityPanel;
		private bool isDraggingLeft;
		private bool isDraggingRight;

		/// <summary>
		/// Opens the FishMMO Dashboard window from the menu.
		/// </summary>
		[MenuItem("FishMMO/FishMMO Dashboard %#d")]
		public static void ShowWindow()
		{
			FishMMODashboard wnd = GetWindow<FishMMODashboard>();
			wnd.titleContent = new GUIContent(WindowTitle, EditorGUIUtility.IconContent("d_ScriptableObject Icon").image);
			wnd.minSize = new Vector2(800, 500);
		}

		/// <summary>
		/// Called when the window is enabled. Builds UI and registers categories.
		/// </summary>
		private void CreateGUI()
		{
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			if (uxml == null)
			{
				Debug.LogError($"[FishMMODashboard] Cannot find UXML at: {UxmlPath}");
				return;
			}

			uxml.CloneTree(rootVisualElement);

			BindUIReferences();
			RegisterCategories();
			BuildCategoryList();
			SetupResizers();
			SetupSearch();
			SetupCreateButton();

			// Restore the last-selected category across domain reloads
			// (e.g., after script recompilation).
			int savedIndex = SessionState.GetInt(SelectedCategoryKey, -1);
			if (savedIndex >= 0 && savedIndex < categories.Count)
			{
				OnCategorySelected(savedIndex);
			}

			SetStatus("Ready");
		}

		/// <summary>
		/// Binds all named UI elements from the UXML tree.
		/// </summary>
		private void BindUIReferences()
		{
			searchField = rootVisualElement.Q<ToolbarSearchField>("search-field");
			entitySearchField = rootVisualElement.Q<ToolbarSearchField>("entity-search-field");
			categoryList = rootVisualElement.Q<VisualElement>("category-list");
			entityList = rootVisualElement.Q<VisualElement>("entity-list");
			inspectorContent = rootVisualElement.Q<VisualElement>("inspector-content");
			entityPanelHeader = rootVisualElement.Q<Label>("entity-panel-header");
			entityCountLabel = rootVisualElement.Q<Label>("entity-count-label");
			statusBar = rootVisualElement.Q<Label>("status-bar");
			inspectorHeader = rootVisualElement.Q<Label>("inspector-header");
			createButton = rootVisualElement.Q<Button>("create-button");
			categoryPanel = rootVisualElement.Q<VisualElement>("category-panel");
			entityPanel = rootVisualElement.Q<VisualElement>("entity-panel");
		}

		/// <summary>
		/// Wires up the global and entity search fields.
		/// </summary>
		private void SetupSearch()
		{
			if (searchField != null)
			{
				searchField.RegisterValueChangedCallback(evt =>
				{
					globalFilter = evt.newValue ?? string.Empty;
					BuildCategoryList();
					RefreshEntityList();
				});
			}

			if (entitySearchField != null)
			{
				entitySearchField.RegisterValueChangedCallback(evt =>
				{
					entityFilter = evt.newValue ?? string.Empty;
					RefreshEntityList();
				});
			}
		}

		/// <summary>
		/// Sets up the + button to create new assets for the current category.
		/// </summary>
		private void SetupCreateButton()
		{
			if (createButton != null)
			{
				createButton.clicked += OnCreateAssetClicked;
				createButton.SetEnabled(false);
			}
		}

		/// <summary>
		/// Sets up drag-to-resize on the panel dividers.
		/// </summary>
		private void SetupResizers()
		{
			VisualElement leftResizer = rootVisualElement.Q<VisualElement>("left-resizer");
			VisualElement rightResizer = rootVisualElement.Q<VisualElement>("right-resizer");

			if (leftResizer != null)
			{
				leftResizer.RegisterCallback<PointerDownEvent>(evt =>
				{
					isDraggingLeft = true;
					leftResizer.CapturePointer(evt.pointerId);
					evt.StopPropagation();
				});

				leftResizer.RegisterCallback<PointerMoveEvent>(evt =>
				{
					if (!isDraggingLeft) return;
					float newWidth = categoryPanel.resolvedStyle.width + evt.deltaPosition.x;
					if (newWidth >= 140 && newWidth <= 400)
					{
						categoryPanel.style.width = newWidth;
					}
					evt.StopPropagation();
				});

				leftResizer.RegisterCallback<PointerUpEvent>(evt =>
				{
					isDraggingLeft = false;
					leftResizer.ReleasePointer(evt.pointerId);
					evt.StopPropagation();
				});
			}

			if (rightResizer != null)
			{
				rightResizer.RegisterCallback<PointerDownEvent>(evt =>
				{
					isDraggingRight = true;
					rightResizer.CapturePointer(evt.pointerId);
					evt.StopPropagation();
				});

				rightResizer.RegisterCallback<PointerMoveEvent>(evt =>
				{
					if (!isDraggingRight) return;
					float newWidth = entityPanel.resolvedStyle.width + evt.deltaPosition.x;
					if (newWidth >= 180 && newWidth <= 500)
					{
						entityPanel.style.width = newWidth;
					}
					evt.StopPropagation();
				});

				rightResizer.RegisterCallback<PointerUpEvent>(evt =>
				{
					isDraggingRight = false;
					rightResizer.ReleasePointer(evt.pointerId);
					evt.StopPropagation();
				});
			}
		}

		/// <summary>
		/// Updates the status bar text.
		/// </summary>
		private void SetStatus(string message)
		{
			if (statusBar != null)
			{
				statusBar.text = message;
			}
		}
	}
}
#endif