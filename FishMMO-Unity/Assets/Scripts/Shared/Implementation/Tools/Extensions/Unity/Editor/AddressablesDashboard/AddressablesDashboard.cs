#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// Professional-grade Addressables Dashboard editor window.
	/// Displays groups, entries, labels, paths, sizes, and project-wide statistics.
	/// Supports drag-and-drop between groups, context menu operations, and deep dependency analysis.
	/// Split into partial classes by responsibility: Core, TreeView, Analysis, Categorization,
	/// SmartGroup, Interactions, Viewers, and Build.
	/// </summary>
	public partial class AddressablesDashboard : EditorWindow
	{
		private const string WindowTitle = "Addressables Dashboard";
		private const string UxmlPath = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/AddressablesDashboard/AddressablesDashboard.uxml";
		private const int ProgressReportInterval = 50;
		private const string DefaultClientBaseUrl = "http://127.0.0.1:8000/";
		private const string ServerBaseUrlPrefix = "file://";
		private const string ServerBaseUrlSuffix = "/ServerData/";

		private TreeView treeView;
		private ToolbarSearchField searchField;
		private Label statusBar;

		// Stat labels — basic
		private Label statGroups;
		private Label statAssets;
		private Label statSize;
		private Label statLabels;
		private Label statEmptyGroups;
		private Label statLargestAsset;

		// Stat labels — analysis (populated by Analyze)
		private Label statDuplicates;
		private Label statNonAddressableRefs;
		private Label statTotalDeps;
		private Label statUnusedLabels;
		private Label statAddressCollisions;
		private Label statStaleEntries;

		// Detail panel
		private Foldout detailFoldout;
		private Label detailContent;

		// Path Simulator
		private Label pathSimBuild;
		private Label pathSimInternalId;
		private Label pathSimClient;
		private Label pathSimServer;

		// Dependency Viewer
		private Label depViewerAsset;
		private VisualElement depDirectList;
		private VisualElement depDupesList;

		/// <summary>
		/// Unique ID counter for TreeView items.
		/// </summary>
		private int nextId;

		/// <summary>
		/// Maps TreeView item IDs to their backing Addressable group.
		/// </summary>
		private readonly Dictionary<int, AddressableAssetGroup> idToGroup = new Dictionary<int, AddressableAssetGroup>();

		/// <summary>
		/// Maps TreeView item IDs to their backing Addressable asset entry.
		/// </summary>
		private readonly Dictionary<int, AddressableAssetEntry> idToEntry = new Dictionary<int, AddressableAssetEntry>();

		/// <summary>
		/// Current search filter applied to the tree.
		/// </summary>
		private string currentFilter = string.Empty;

		/// <summary>
		/// Cached tree data for rebuilding after filter changes.
		/// </summary>
		private List<TreeViewItemData<string>> fullTreeData;

		/// <summary>
		/// Cached analysis results updated on Analyze.
		/// </summary>
		private int cachedDuplicateCount;
		private int cachedNonAddressableRefCount;
		private int cachedTotalDepCount;
		private int cachedUnusedLabelCount;
		private int cachedAddressCollisionCount;
		private int cachedStaleEntryCount;

		/// <summary>
		/// Cached detailed analysis report text for the detail panel.
		/// </summary>
		private string cachedDetailReport = "";

		/// <summary>
		/// Opens the Addressables Dashboard window from the menu.
		/// </summary>
		[MenuItem("FishMMO/Addressables Dashboard")]
		public static void ShowWindow()
		{
			var window = GetWindow<AddressablesDashboard>();
			window.titleContent = new GUIContent(WindowTitle);
			window.minSize = new Vector2(600, 400);
		}

		/// <summary>
		/// Called when the window is created. Builds the UI from UXML and wires up events.
		/// </summary>
		public void CreateGUI()
		{
			var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			if (visualTree == null)
			{
				Debug.LogError($"[AddressablesDashboard] Could not load UXML at: {UxmlPath}");
				return;
			}

			visualTree.CloneTree(rootVisualElement);

			treeView = rootVisualElement.Q<TreeView>("addressables-tree");
			searchField = rootVisualElement.Q<ToolbarSearchField>("search-field");
			statusBar = rootVisualElement.Q<Label>("status-bar");

			statGroups = rootVisualElement.Q<Label>("stat-groups");
			statAssets = rootVisualElement.Q<Label>("stat-assets");
			statSize = rootVisualElement.Q<Label>("stat-size");
			statLabels = rootVisualElement.Q<Label>("stat-labels");
			statEmptyGroups = rootVisualElement.Q<Label>("stat-empty-groups");
			statLargestAsset = rootVisualElement.Q<Label>("stat-largest-asset");
			statDuplicates = rootVisualElement.Q<Label>("stat-duplicates");
			statNonAddressableRefs = rootVisualElement.Q<Label>("stat-non-addressable-refs");
			statTotalDeps = rootVisualElement.Q<Label>("stat-total-deps");
			statUnusedLabels = rootVisualElement.Q<Label>("stat-unused-labels");
			statAddressCollisions = rootVisualElement.Q<Label>("stat-address-collisions");
			statStaleEntries = rootVisualElement.Q<Label>("stat-stale-entries");

			detailFoldout = rootVisualElement.Q<Foldout>("detail-foldout");
			detailContent = rootVisualElement.Q<Label>("detail-content");

			// Path Simulator
			pathSimBuild = rootVisualElement.Q<Label>("path-sim-build");
			pathSimInternalId = rootVisualElement.Q<Label>("path-sim-internal-id");
			pathSimClient = rootVisualElement.Q<Label>("path-sim-client");
			pathSimServer = rootVisualElement.Q<Label>("path-sim-server");

			// Dependency Viewer
			depViewerAsset = rootVisualElement.Q<Label>("dep-viewer-asset");
			depDirectList = rootVisualElement.Q<VisualElement>("dep-direct-list");
			depDupesList = rootVisualElement.Q<VisualElement>("dep-dupes-list");

			if (treeView == null || searchField == null || statusBar == null)
			{
				Debug.LogError("[AddressablesDashboard] Required UI elements not found in UXML.");
				return;
			}

			treeView.makeItem = MakeTreeItem;
			treeView.bindItem = BindTreeItem;
			treeView.selectionType = SelectionType.Single;
			treeView.selectedIndicesChanged += OnTreeSelectionChanged;

			// Search
			searchField.RegisterValueChangedCallback(OnSearchChanged);

			// Refresh button
			var refreshButton = rootVisualElement.Q<ToolbarButton>("refresh-button");
			if (refreshButton != null)
			{
				refreshButton.clicked += RebuildTree;
			}

			// Analyze button
			var analyzeButton = rootVisualElement.Q<ToolbarButton>("analyze-button");
			if (analyzeButton != null)
			{
				analyzeButton.clicked += RunAnalysis;
			}

			// Export button
			var exportButton = rootVisualElement.Q<ToolbarButton>("export-button");
			if (exportButton != null)
			{
				exportButton.clicked += ExportAnalysis;
			}

			// Fix All button
			var fixAllButton = rootVisualElement.Q<ToolbarButton>("fix-all-button");
			if (fixAllButton != null)
			{
				fixAllButton.clicked += FixAll;
			}

			// Smart Group button
			var smartGroupButton = rootVisualElement.Q<ToolbarButton>("smart-group-button");
			if (smartGroupButton != null)
			{
				smartGroupButton.clicked += SmartGroupAll;
			}

			// Add Group button
			var addGroupButton = rootVisualElement.Q<ToolbarButton>("add-group-button");
			if (addGroupButton != null)
			{
				addGroupButton.clicked += AddNewGroup;
			}

			// Build Addressables button
			var buildButton = rootVisualElement.Q<ToolbarButton>("build-button");
			if (buildButton != null)
			{
				buildButton.clicked += BuildAddressables;
			}

			// Double-click to select in Project
			treeView.RegisterCallback<PointerDownEvent>(OnTreeDoubleClick);

			// Drag and drop
			treeView.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
			treeView.RegisterCallback<DragPerformEvent>(OnDragPerform);
			treeView.RegisterCallback<PointerDownEvent>(OnPointerDownForDrag);

			RebuildTree();
		}

	}
}
#endif