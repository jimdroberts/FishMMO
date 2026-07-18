#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;
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

		// Global Path Summary (profile-level, always visible)
		private Foldout globalPathFoldout;
		private Label globalPathProfile;
		private Label globalPathRemoteBuild;
		private Label globalPathRemoteLoad;
		private Label globalPathLocalBuild;
		private Label globalPathLocalLoad;
		private Label globalPathCatalog;
		private Label globalPathClientBase;
		private Label globalPathServerBase;
		private Label globalPathServerCatalog;
		private Label globalPathWorldScenesBuild;
		private Label globalPathWorldScenesLoad;
		private Label globalPathTemplatesBuild;
		private Label globalPathTemplatesLoad;
		private Label globalPathEditorStaging;
		private Label globalPathEditorCatalog;

		// Path Simulator (per-asset)
		private Label pathSimBuild;
		private Label pathSimInternalId;
		private Label pathSimClient;
		private Label pathSimServer;
		private Label pathSimClientCatalog;
		private Label pathSimServerCatalog;

		// Dependency Viewer
		private Label depViewerAsset;
		private VisualElement depDirectList;
		private VisualElement depNonAddrList;
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
		/// Asset paths of entries that have non-addressable references or cross-group duplicate deps.
		/// Populated by RunAnalysis, consumed by BindTreeItem for red/pink highlighting.
		/// </summary>
		private readonly HashSet<string> violationEntryPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Group names that contain at least one entry with violations.
		/// Populated by RunAnalysis, consumed by BindTreeItem for group-level highlighting.
		/// </summary>
		private readonly HashSet<string> violationGroupNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

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

			// Global Path Summary — always visible, profile-level
			globalPathFoldout = new Foldout { text = "Global Path Summary", value = false };
			globalPathFoldout.AddToClassList("detail-foldout");
			globalPathFoldout.style.marginTop = 4;
			globalPathFoldout.style.marginBottom = 4;

			globalPathProfile = NewPathRow(globalPathFoldout, "Active Profile:");
			globalPathRemoteBuild = NewPathRow(globalPathFoldout, "Remote Build:");
			globalPathRemoteLoad = NewPathRow(globalPathFoldout, "Remote Load:");
			globalPathLocalBuild = NewPathRow(globalPathFoldout, "Local Build:");
			globalPathLocalLoad = NewPathRow(globalPathFoldout, "Local Load:");
			globalPathWorldScenesBuild = NewPathRow(globalPathFoldout, "WorldScenes Build:");
			globalPathWorldScenesLoad = NewPathRow(globalPathFoldout, "WorldScenes Load:");
			globalPathTemplatesBuild = NewPathRow(globalPathFoldout, "Templates Build:");
			globalPathTemplatesLoad = NewPathRow(globalPathFoldout, "Templates Load:");
			globalPathEditorStaging = NewPathRow(globalPathFoldout, "Editor → Staging:");
			globalPathEditorCatalog = NewPathRow(globalPathFoldout, "Editor → Catalog:");
			globalPathClientBase = NewPathRow(globalPathFoldout, "Client:");
			globalPathCatalog = NewPathRow(globalPathFoldout, "Client → Catalog:");
			globalPathServerBase = NewPathRow(globalPathFoldout, "Server:");
			globalPathServerCatalog = NewPathRow(globalPathFoldout, "Server → Catalog:");

			// Insert after the toolbar
			var toolbar = rootVisualElement.Q<Toolbar>("toolbar");
			if (toolbar != null)
				rootVisualElement.Insert(rootVisualElement.IndexOf(toolbar) + 1, globalPathFoldout);

			// Path Simulator
			pathSimBuild = rootVisualElement.Q<Label>("path-sim-build");
			pathSimInternalId = rootVisualElement.Q<Label>("path-sim-internal-id");
			pathSimClient = rootVisualElement.Q<Label>("path-sim-client");
			pathSimServer = rootVisualElement.Q<Label>("path-sim-server");

			// Add Client Catalog and Server Catalog rows programmatically
			// after the existing UXML rows in the Path Simulator.
			var pathSimulator = rootVisualElement.Q<VisualElement>("path-simulator");
			if (pathSimulator != null)
			{
				pathSimClientCatalog = NewPathRow(pathSimulator, "Client → Catalog:");
				pathSimServerCatalog = NewPathRow(pathSimulator, "Server → Catalog:");
			}

			// Dependency Viewer
			depViewerAsset = rootVisualElement.Q<Label>("dep-viewer-asset");
			depDirectList = rootVisualElement.Q<VisualElement>("dep-direct-list");
			depNonAddrList = rootVisualElement.Q<VisualElement>("dep-nonaddr-list");
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
			UpdateGlobalPathSummary();
		}



		/// <summary>
		/// Creates a label row inside the foldout and returns the value label.
		/// </summary>
		private static Label NewPathRow(VisualElement parent, string caption)
		{
			var row = new VisualElement();
			row.AddToClassList("path-simulator-row");
			var cap = new Label(caption);
			cap.AddToClassList("path-simulator-label");
			var val = new Label("—");
			val.AddToClassList("path-simulator-value");
			row.Add(cap);
			row.Add(val);
			parent.Add(row);
			return val;
		}

		/// <summary>
		/// Populates the Global Path Summary with resolved profile-level paths
		/// and the runtime rewrite bases used by DynamicAddressableLoadPathSystem.
		/// </summary>
		private void UpdateGlobalPathSummary()
		{
			if (globalPathProfile == null) return;

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			globalPathProfile.text = settings.activeProfileId;

			string remoteBuild = ResolveProfilePath(settings, "Remote", "Build");
			string remoteLoad = ResolveProfilePath(settings, "Remote", "Load");

			globalPathRemoteBuild.text = remoteBuild;
			globalPathRemoteLoad.text = remoteLoad;
			globalPathLocalBuild.text = ResolveProfilePath(settings, "Local", "Build");
			globalPathLocalLoad.text = ResolveProfilePath(settings, "Local", "Load");

			// ── Content Source Paths ──
			// Resolved from the Addressables profile when available; fall
			// back to well-known project paths when no profile variable matches.
			globalPathWorldScenesBuild.text = ResolveProfilePath(settings, "WorldScene", "Build");
			globalPathWorldScenesLoad.text = ResolveProfilePath(settings, "WorldScene", "Load");
			globalPathTemplatesBuild.text = ResolveProfilePath(settings, "Template", "Build");
			globalPathTemplatesLoad.text = ResolveProfilePath(settings, "Template", "Load");

			// ── Editor Staging ──
			// Addressables.RuntimePath points to where the Editor stages bundles
			// during Play Mode and Build Addressables.  In the Editor this is
			// Library/com.unity.addressables/aa/{Platform}/; in a player build
			// it resolves to {StreamingAssets}/aa/.
			string runtimePath = Addressables.RuntimePath.Replace('\\', '/').TrimEnd('/');
			string editorStaging = "file://" + runtimePath + "/";
			globalPathEditorStaging.text = editorStaging;
			globalPathEditorCatalog.text = editorStaging + "catalog.hash";

			// ── Runtime Load Paths ──
			// Mirror DynamicAddressableLoadPathSystem.Awake logic.  When the
			// profile's remote load URL is a loopback placeholder with no CDN
			// override, the runtime falls back to local StreamingAssets/aa/.
			// The platform subfolder (StandaloneLinux64, etc.) comes from the
			// InternalId's relative path at transform time — not from the base.
			string effectiveClientBase;
			if (DynamicAddressableLoadPathSystem.IsLoopbackPlaceholder(remoteLoad))
			{
				effectiveClientBase = "file://" + Application.streamingAssetsPath.Replace('\\', '/') + "/aa/";
			}
			else
			{
				effectiveClientBase = remoteLoad;
				if (!string.IsNullOrEmpty(effectiveClientBase) && !effectiveClientBase.EndsWith("/"))
					effectiveClientBase += "/";
			}

			string serverBase = ServerBaseUrlPrefix + Application.streamingAssetsPath.Replace('\\', '/') + ServerBaseUrlSuffix;

			globalPathClientBase.text = effectiveClientBase;
			globalPathCatalog.text = effectiveClientBase + "catalog.hash";
			globalPathServerBase.text = serverBase;
			globalPathServerCatalog.text = serverBase + "catalog.hash";
		}

		/// <summary>
		/// Finds a profile variable by scope (Remote/Local) and role (Build/Load)
		/// and returns its resolved value for the active profile.
		/// </summary>
		private static string ResolveProfilePath(AddressableAssetSettings settings, string scope, string role)
		{
			string resolved = "(not set)";
			foreach (var name in settings.profileSettings.GetVariableNames())
			{
				string lower = name.ToLowerInvariant();
				bool hasRole = !string.IsNullOrEmpty(role);
				if (lower.Contains(scope.ToLowerInvariant()) &&
					(!hasRole || lower.Contains(role.ToLowerInvariant())) &&
					lower.Contains("path"))
				{
					string value = settings.profileSettings.GetValueByName(settings.activeProfileId, name);
					if (!string.IsNullOrEmpty(value))
						resolved = value;
					break;
				}
			}
			return resolved;
		}
	}
}
#endif