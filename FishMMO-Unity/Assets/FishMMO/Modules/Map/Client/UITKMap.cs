using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// The world map: a large, pannable, zoomable view of the whole scene, drawn over the baked
	/// overhead image, with the player's explored ground, every marker they may see, the scene's
	/// authored landmarks, and the notes they have pinned to it.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this draws a baked image and the minimap does not.</b> The minimap is a live
	/// camera because it is small, centred on the player, and wants to show the world as it is
	/// right now. The world map covers an entire zone: rendering that live would mean an
	/// orthographic camera wide enough to photograph kilometres of terrain, every frame the panel
	/// is open, and it would show nothing but the small part of the zone the client has actually
	/// streamed — a map of a continent with holes in it. A bake taken in the editor has the whole
	/// scene loaded and costs nothing at runtime.</para>
	///
	/// <para><b>What the player can do to it that they cannot do to the minimap.</b> Pan, zoom out
	/// as far as their Cartography allows, filter by category, write notes, and fast travel. The
	/// notes are the reason the panel has a side bar at all; everything else would have fitted in
	/// the corner.</para>
	///
	/// <para><b>Fast travel.</b> A discovered waypoint is a marker like any other until it is
	/// clicked, which selects it into the side bar's waypoint section; the button there sends a
	/// <c>WaypointTravelRequestBroadcast</c>. Nothing moves until the server answers — a
	/// refusal comes back with a reason and the button re-enables, an arrival closes the map. The
	/// click, arrival, refusal and discovery each have a sound slot, and
	/// <see cref="OnFastTravelClicked"/> is the hook for anything else that wants the click. When
	/// the server's <see cref="WaypointTravelPolicy"/> requires it, the button stays disabled until
	/// the character stands near a discovered waypoint — the map can still be opened anywhere to
	/// look at.</para>
	///
	/// <para><b>What it does not show.</b> Exactly what the minimap does not show — the marker
	/// rules are applied by the same <see cref="MapMarkerFilter"/>, so a hostile player who is too
	/// far away for the minimap is equally absent here. Zooming out on the world map reveals more
	/// <i>terrain</i>, which is baked, public and identical for every player; it never reveals
	/// another entity.</para>
	/// </remarks>
	public class UITKMap : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Window;

		/// <summary>Name of the element the map view is mounted into.</summary>
		private const string MAP_VIEW_NAME = "map-view";

		/// <summary>Name of the panel's title label.</summary>
		private const string TITLE_NAME = "header-title";

		/// <summary>The header dropdown choosing which entered scene the map shows.</summary>
		private const string SCENE_SELECT_NAME = "map-scene-select";

		/// <summary>Name of the panel's subtitle label.</summary>
		private const string SUBTITLE_NAME = "header-subtitle";

		/// <summary>Name of the close button.</summary>
		private const string CLOSE_BUTTON_NAME = "close-button";

		/// <summary>Name of the label showing the coordinates under the pointer.</summary>
		private const string CURSOR_LABEL_NAME = "map-cursor-position";

		/// <summary>Name of the label showing how much of the scene has been explored.</summary>
		private const string EXPLORED_LABEL_NAME = "map-explored";

		/// <summary>Name of the label naming the region under the pointer.</summary>
		private const string REGION_LABEL_NAME = "map-region";

		/// <summary>Name of the zoom slider.</summary>
		private const string ZOOM_SLIDER_NAME = "map-zoom";

		/// <summary>Name of the button that recentres the map on the player.</summary>
		private const string RECENTER_BUTTON_NAME = "map-recenter";

		/// <summary>Name of the container the filter toggles are built into.</summary>
		private const string FILTER_LIST_NAME = "map-filters";

		/// <summary>Name of the container the point-of-interest group toggles are built into.</summary>
		private const string PLACE_FILTER_LIST_NAME = "map-place-filters";

		/// <summary>Name of the container the note rows are built into.</summary>
		private const string NOTE_LIST_NAME = "map-note-list";

		/// <summary>Name of the note title field.</summary>
		private const string NOTE_TITLE_FIELD_NAME = "map-note-title";

		/// <summary>Name of the note body field.</summary>
		private const string NOTE_TEXT_FIELD_NAME = "map-note-text";

		/// <summary>Name of the container the note colour swatches are built into.</summary>
		private const string NOTE_COLOR_ROW_NAME = "map-note-colors";

		/// <summary>Name of the button that pins a note.</summary>
		private const string NOTE_ADD_BUTTON_NAME = "map-note-add";

		/// <summary>Name of the label explaining the note editor's current state.</summary>
		private const string NOTE_HINT_NAME = "map-note-hint";

		/// <summary>USS class marking the selected colour swatch.</summary>
		private const string SWATCH_SELECTED_CLASS = "map-swatch--selected";

		/// <summary>Name of the guide section container.</summary>
		private const string GUIDE_SECTION_NAME = "map-guide-section";

		/// <summary>Name of the label naming the guide's target.</summary>
		private const string GUIDE_NAME_LABEL_NAME = "map-guide-name";

		/// <summary>Name of the label giving the distance to the guide's target.</summary>
		private const string GUIDE_STATUS_LABEL_NAME = "map-guide-status";

		/// <summary>Name of the button that stops guiding.</summary>
		private const string GUIDE_CLEAR_BUTTON_NAME = "map-guide-clear";

		/// <summary>Name of the waypoint section container.</summary>
		private const string WAYPOINT_SECTION_NAME = "map-waypoint-section";

		/// <summary>Name of the label naming the selected waypoint.</summary>
		private const string WAYPOINT_NAME_LABEL_NAME = "map-waypoint-name";

		/// <summary>Name of the label describing the selected waypoint.</summary>
		private const string WAYPOINT_DESCRIPTION_LABEL_NAME = "map-waypoint-description";

		/// <summary>Name of the fast travel button.</summary>
		private const string WAYPOINT_TRAVEL_BUTTON_NAME = "map-waypoint-travel";

		/// <summary>Name of the label explaining the waypoint section's state.</summary>
		private const string WAYPOINT_HINT_NAME = "map-waypoint-hint";

		/// <summary>Name of the section listing the discovered Gate Stones while a Gate waits for its destination.</summary>
		private const string GATE_SECTION_NAME = "map-gate-section";

		/// <summary>Name of the label above the Gate Stone list.</summary>
		private const string GATE_HINT_NAME = "map-gate-hint";

		/// <summary>Name of the container the Gate Stone rows are built into.</summary>
		private const string GATE_LIST_NAME = "map-gate-list";

		/// <summary>Class of one Gate Stone row, and of the selected one.</summary>
		private const string GATE_ROW_CLASS = "map-gate__row";
		private const string GATE_ROW_SELECTED_CLASS = "map-gate__row--selected";

		/// <summary>
		/// How long the travel button stays disabled waiting for the server's answer, in seconds.
		/// A lost reply must not leave the button dead for the rest of the session.
		/// </summary>
		private const double TravelResponseTimeout = 5.0;

		/// <summary>How much one notch of the scroll wheel changes the zoom, as a multiplier.</summary>
		private const float ZoomStep = 1.25f;

		/// <summary>How often the markers are rebuilt while the panel is open, in seconds.</summary>
		/// <remarks>
		/// Slower than the minimap's. The world map is a planning tool looked at while standing
		/// still, its markers are small relative to the area it covers, and a player reading it is
		/// not tracking a creature's approach frame by frame.
		/// </remarks>
		private const double MarkerRefreshInterval = 0.25;

		[Header("Fast Travel Sounds")]
		[Tooltip("Played when the fast travel button is clicked. Optional.")]
		public AudioClip FastTravelClickSound;

		[Tooltip("Played when the server confirms the character arrived. Optional.")]
		public AudioClip FastTravelArriveSound;

		[Tooltip("Played when the server refuses the request. Optional.")]
		public AudioClip FastTravelRefusedSound;

		[Tooltip("Played when a waypoint is discovered. Optional.")]
		public AudioClip WaypointDiscoveredSound;

		/// <summary>
		/// Raised when the player clicks the fast travel button, before the request is sent.
		/// Parameters: the scene name and the waypoint index. The hook for anything that wants
		/// the click beyond the sound slot above — an FX, a tutorial, analytics.
		/// </summary>
		public static event Action<string, int> OnFastTravelClicked;

		/// <summary>The map view mounted into the panel.</summary>
		private UITKMapView mapView;

		/// <summary>The dial pointing at the planet's north (<see cref="TrueNorth"/>), over the map's corner.</summary>
		private MapCompass compass;

		/// <summary>The guide section container, shown while there is a target.</summary>
		private VisualElement guideSection;

		/// <summary>Label naming the guide's target.</summary>
		private Label guideNameLabel;

		/// <summary>Label giving the distance to the guide's target.</summary>
		private Label guideStatusLabel;

		/// <summary>The waypoint section container.</summary>
		private VisualElement waypointSection;

		/// <summary>Label naming the selected waypoint.</summary>
		private Label waypointNameLabel;

		/// <summary>Label describing the selected waypoint.</summary>
		private Label waypointDescriptionLabel;

		/// <summary>Button that asks the server to fast travel.</summary>
		private Button waypointTravelButton;

		/// <summary>Label explaining the waypoint section's state.</summary>
		private Label waypointHint;

		/// <summary>Whether a waypoint is selected in the side bar.</summary>
		private bool hasSelectedWaypoint;

		/// <summary>Whether a Gate spell waits for this map to choose its Gate Stone (<see cref="GateStoneChoice"/>).</summary>
		private bool gateMode;

		/// <summary>The Gate Stone section: shown only in gate mode, it lists every discovered stone in any land.</summary>
		private VisualElement gateSection;
		private Label gateHint;
		private VisualElement gateList;
		private readonly List<DiscoveredGateStone> gateStones = new List<DiscoveredGateStone>();

		/// <summary>The selected waypoint's index within the current scene.</summary>
		private int selectedWaypointIndex;

		/// <summary>Whether a travel request is awaiting the server's answer.</summary>
		private bool travelPending;

		/// <summary>Time, on the unscaled clock, at which a pending request is given up on.</summary>
		private double travelPendingUntil;

		/// <summary>
		/// Whether the character satisfied the travel origin rule when the panel was last written.
		/// The refresh tick rewrites the panel only when this flips, so a refusal's hint survives.
		/// </summary>
		private bool travelOriginSatisfied = true;

		/// <summary>Label naming the scene.</summary>
		private Label titleLabel;

		/// <summary>The scene dropdown, and the scene behind each of its choices, in order.</summary>
		private DropdownField sceneSelect;
		private readonly List<string> sceneChoiceScenes = new List<string>();

		/// <summary>The scene the view was last laid out for, to tell a switch from a refresh.</summary>
		private string shownSceneName;

		/// <summary>Label describing the scene.</summary>
		private Label subtitleLabel;

		/// <summary>Label showing the coordinates under the pointer.</summary>
		private Label cursorLabel;

		/// <summary>Label showing how much of the scene has been explored.</summary>
		private Label exploredLabel;

		/// <summary>The explored count and grid size the readout last showed, so it is rebuilt only on change.</summary>
		private int shownExploredChunks = -1;

		/// <summary>See <see cref="shownExploredChunks"/>.</summary>
		private int shownChunkCount = -1;

		/// <summary>Label naming the region under the pointer.</summary>
		private Label regionLabel;

		/// <summary>Slider controlling the zoom.</summary>
		private Slider zoomSlider;

		/// <summary>Container the filter toggles live in.</summary>
		private VisualElement filterList;

		/// <summary>Container the point-of-interest group toggles live in.</summary>
		private VisualElement placeFilterList;

		/// <summary>Container the note rows live in.</summary>
		private VisualElement noteList;

		/// <summary>Field holding a new note's title.</summary>
		private TextField noteTitleField;

		/// <summary>Field holding a new note's body.</summary>
		private TextField noteTextField;

		/// <summary>Container the note colour swatches live in.</summary>
		private VisualElement noteColorRow;

		/// <summary>Button that pins a note.</summary>
		private Button noteAddButton;

		/// <summary>Label explaining what the note editor is waiting for.</summary>
		private Label noteHint;

		/// <summary>Snapshot buffer, reused so a refresh allocates nothing.</summary>
		private readonly List<MapMarkerSnapshot> markerBuffer = new List<MapMarkerSnapshot>();

		/// <summary>The filter toggles, by the category each one controls.</summary>
		private readonly Dictionary<MapFilterCategory, Toggle> filterToggles = new Dictionary<MapFilterCategory, Toggle>();

		/// <summary>Where the map is centred, in world space.</summary>
		private Vector3 center;

		/// <summary>The current zoom, as the view's half-extent in world metres.</summary>
		private float zoom = 200.0f;

		/// <summary>Where the player last clicked on the map, used as a new note's position.</summary>
		private Vector3 pendingNotePosition;

		/// <summary>Whether <see cref="pendingNotePosition"/> holds a place to pin a note.</summary>
		private bool hasPendingNotePosition;

		/// <summary>Which palette colour a new note will use.</summary>
		private int noteColorIndex;

		/// <summary>Time, on the unscaled clock, of the next marker rebuild.</summary>
		private double nextMarkerRefreshTime;

		/// <summary>Whether the pointer is currently panning the map.</summary>
		private bool panning;

		/// <summary>Where the pointer was on the previous pan frame, in the view's coordinates.</summary>
		private Vector2 panPrevious;

		/// <summary>The pointer that started the pan, so a second finger does not hijack it.</summary>
		private int panPointerId;

		/// <summary>
		/// Builds the view and wires every control.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			VisualElement host = root.Q<VisualElement>(MAP_VIEW_NAME);
			if (host == null)
			{
				Log.Error("UITKMap", $"The world map UXML has no element named '{MAP_VIEW_NAME}'; there is nowhere to mount the map.");
				return;
			}

			mapView = new UITKMapView()
			{
				name = "map-surface",
				pickingMode = PickingMode.Position,
			};
			mapView.style.flexGrow = 1.0f;
			mapView.MapTextureIsViewAligned = false;
			mapView.MapTextureNorthDegrees = ClientMapSystem.Viewed.Definition != null ? ClientMapSystem.Viewed.Definition.NorthOffsetDegrees : 0.0f;
			mapView.OnMapScrolled += MapView_OnScrolled;
			mapView.OnMapClicked += MapView_OnClicked;
			mapView.RegisterCallback<PointerDownEvent>(MapView_OnPointerDown);
			mapView.RegisterCallback<PointerMoveEvent>(MapView_OnPointerMove);
			mapView.RegisterCallback<PointerUpEvent>(MapView_OnPointerUp);
			mapView.RegisterCallback<PointerLeaveEvent>(MapView_OnPointerLeave);
			host.Clear();
			host.Add(mapView);
			// After the map, so it draws over it.
			compass = new MapCompass();
			host.Add(compass);

			titleLabel = root.Q<Label>(TITLE_NAME);
			sceneSelect = root.Q<DropdownField>(SCENE_SELECT_NAME);
			if (sceneSelect != null)
			{
				sceneSelect.RegisterValueChangedCallback(OnSceneSelected);
			}
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			cursorLabel = root.Q<Label>(CURSOR_LABEL_NAME);
			exploredLabel = root.Q<Label>(EXPLORED_LABEL_NAME);
			regionLabel = root.Q<Label>(REGION_LABEL_NAME);
			zoomSlider = root.Q<Slider>(ZOOM_SLIDER_NAME);
			filterList = root.Q<VisualElement>(FILTER_LIST_NAME);
			placeFilterList = root.Q<VisualElement>(PLACE_FILTER_LIST_NAME);
			noteList = root.Q<VisualElement>(NOTE_LIST_NAME);
			noteTitleField = root.Q<TextField>(NOTE_TITLE_FIELD_NAME);
			noteTextField = root.Q<TextField>(NOTE_TEXT_FIELD_NAME);
			noteColorRow = root.Q<VisualElement>(NOTE_COLOR_ROW_NAME);
			noteAddButton = root.Q<Button>(NOTE_ADD_BUTTON_NAME);
			noteHint = root.Q<Label>(NOTE_HINT_NAME);

			Button closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}

			Button recenter = root.Q<Button>(RECENTER_BUTTON_NAME);
			if (recenter != null)
			{
				recenter.clicked += CenterOnCharacter;
			}

			if (zoomSlider != null)
			{
				zoomSlider.RegisterValueChangedCallback(OnZoomSliderChanged);
			}

			if (noteTitleField != null)
			{
				noteTitleField.maxLength = MapNote.MaximumTitleLength;
			}
			if (noteTextField != null)
			{
				noteTextField.maxLength = MapNote.MaximumTextLength;
			}

			if (noteAddButton != null)
			{
				noteAddButton.clicked += AddPendingNote;
			}

			guideSection = root.Q<VisualElement>(GUIDE_SECTION_NAME);
			guideNameLabel = root.Q<Label>(GUIDE_NAME_LABEL_NAME);
			guideStatusLabel = root.Q<Label>(GUIDE_STATUS_LABEL_NAME);
			Button guideClear = root.Q<Button>(GUIDE_CLEAR_BUTTON_NAME);
			if (guideClear != null)
			{
				guideClear.clicked += MapGuide.Clear;
			}

			waypointSection = root.Q<VisualElement>(WAYPOINT_SECTION_NAME);
			waypointNameLabel = root.Q<Label>(WAYPOINT_NAME_LABEL_NAME);
			waypointDescriptionLabel = root.Q<Label>(WAYPOINT_DESCRIPTION_LABEL_NAME);
			waypointTravelButton = root.Q<Button>(WAYPOINT_TRAVEL_BUTTON_NAME);
			waypointHint = root.Q<Label>(WAYPOINT_HINT_NAME);
			if (waypointTravelButton != null)
			{
				waypointTravelButton.clicked += OnWaypointButtonClicked;
			}

			gateSection = root.Q<VisualElement>(GATE_SECTION_NAME);
			gateHint = root.Q<Label>(GATE_HINT_NAME);
			gateList = root.Q<VisualElement>(GATE_LIST_NAME);

			BuildFilterToggles();
			BuildColorSwatches();

			/* Removed before adding. OnStarting runs again whenever this panel's visual tree is
			 * replaced (UITKControl.ReinitializeIfTreeReplaced; once every hide/show), and these are
			 * static events, so each opening of the world map used to add another copy of both
			 * handlers. Same defect as UITKMinimap. */
			ClientMapSystem.OnSceneMapChanged -= MapSystem_OnSceneChanged;
			ClientMapSystem.OnSceneMapChanged += MapSystem_OnSceneChanged;
			ClientMapSystem.OnNotesChanged -= MapSystem_OnNotesChanged;
			ClientMapSystem.OnNotesChanged += MapSystem_OnNotesChanged;
			ClientMapSystem.OnViewedSceneChanged -= MapSystem_OnViewedSceneChanged;
			ClientMapSystem.OnViewedSceneChanged += MapSystem_OnViewedSceneChanged;
			ClientMapSystem.OnVisitedScenesChanged -= MapSystem_OnVisitedScenesChanged;
			ClientMapSystem.OnVisitedScenesChanged += MapSystem_OnVisitedScenesChanged;
			MapGuide.OnChanged -= RefreshGuidePanel;
			MapGuide.OnChanged += RefreshGuidePanel;

			/* Static events on the controller interface, so they can be subscribed before any
			 * character exists and survive the character being replaced. Unsubscribed in
			 * OnDestroying; OnStarting re-runs when the tree is rebuilt, and the -= before += keeps
			 * a rebuild from stacking handlers. */
			IWaypointController.OnWaypointUnlocked -= Waypoint_OnUnlocked;
			IWaypointController.OnWaypointUnlocked += Waypoint_OnUnlocked;
			IWaypointController.OnWaypointTravelled -= Waypoint_OnTravelled;
			IWaypointController.OnWaypointTravelled += Waypoint_OnTravelled;
			IWaypointController.OnWaypointTravelRefused -= Waypoint_OnTravelRefused;
			IWaypointController.OnWaypointTravelRefused += Waypoint_OnTravelRefused;
			IWaypointController.OnWaypointMapRequested -= Waypoint_OnMapRequested;
			IWaypointController.OnWaypointMapRequested += Waypoint_OnMapRequested;

			// The Gate spell's choice of a Gate Stone (issue #241): the Travel module begins and ends it through the core seam.
			GateStoneChoice.OnBegan -= GateStoneChoice_OnBegan;
			GateStoneChoice.OnBegan += GateStoneChoice_OnBegan;
			GateStoneChoice.OnEnded -= GateStoneChoice_OnEnded;
			GateStoneChoice.OnEnded += GateStoneChoice_OnEnded;

			ApplySceneToView();
			RefreshWaypointPanel();
			RefreshGuidePanel();
			RefreshGateList();
		}

		/// <summary>
		/// Drops subscriptions.
		/// </summary>
		public override void OnDestroying()
		{
			ClientMapSystem.OnSceneMapChanged -= MapSystem_OnSceneChanged;
			ClientMapSystem.OnNotesChanged -= MapSystem_OnNotesChanged;
			ClientMapSystem.OnViewedSceneChanged -= MapSystem_OnViewedSceneChanged;
			ClientMapSystem.OnVisitedScenesChanged -= MapSystem_OnVisitedScenesChanged;
			MapGuide.OnChanged -= RefreshGuidePanel;
			IWaypointController.OnWaypointUnlocked -= Waypoint_OnUnlocked;
			IWaypointController.OnWaypointTravelled -= Waypoint_OnTravelled;
			IWaypointController.OnWaypointTravelRefused -= Waypoint_OnTravelRefused;
			IWaypointController.OnWaypointMapRequested -= Waypoint_OnMapRequested;
			GateStoneChoice.OnBegan -= GateStoneChoice_OnBegan;
			GateStoneChoice.OnEnded -= GateStoneChoice_OnEnded;
			base.OnDestroying();
		}

		/// <summary>
		/// Recentres on the character and rebuilds everything derived from the scene.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
			ApplySceneToView();
			CenterOnCharacter();
		}

		/// <summary>
		/// Clears the view when the character goes away.
		/// </summary>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			mapView?.ReleaseMarkers();
		}

		/// <summary>
		/// Centres on the character and refreshes when the panel is opened.
		/// </summary>
		public override void Show()
		{
			base.Show();

			/* Recentred on every open rather than remembering where the player left it. A map
			 * reopened somewhere else is disorienting: the first thing anybody does with a world
			 * map is look for themselves on it, and a remembered pan means doing that by hand
			 * every single time. */
			// Every open starts on the scene the character stands in; the dropdown picks another.
			ClientMapSystem.ViewScene(null);
			CenterOnCharacter();
			nextMarkerRefreshTime = 0.0;
			RefreshNotes();
			RefreshWaypointPanel();
			RefreshGateList();

			/* The readouts persist with the tree and would describe wherever the pointer was when the
			 * map was closed until it next moved over the map. */
			if (cursorLabel != null)
			{
				cursorLabel.text = string.Empty;
			}
			if (regionLabel != null)
			{
				regionLabel.text = string.Empty;
			}
		}

		/// <summary>
		/// Hides the map and ends a pan that was in progress.
		/// </summary>
		/// <remarks>
		/// Hiding releases the pointer the map had captured, and a released capture sends no pointer
		/// up, so without this the next open would pan on the first pointer move.
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);
			if (!Visible)
			{
				panning = false;
				// Closing the map while a Gate waits for its stone gives the Gate up (issue #241).
				if (gateMode)
				{
					gateMode = false;
					GateStoneChoice.Cancel();
					RefreshGateList();
				}
			}
		}

		/// <summary>
		/// Keeps the view, markers and readouts current while the panel is open.
		/// </summary>
		protected override void OnTick()
		{
			if (!Visible || mapView == null)
			{
				return;
			}

			double now = Time.unscaledTimeAsDouble;
			if (now < nextMarkerRefreshTime)
			{
				return;
			}
			nextMarkerRefreshTime = now + MarkerRefreshInterval;

			if (gateMode)
			{
				// The countdown in the hint.
				RefreshWaypointPanel();
			}
			else if (travelPending && now >= travelPendingUntil)
			{
				// The server's answer never came. Hand the button back rather than leave it dead.
				travelPending = false;
				RefreshWaypointPanel();
			}
			else if (IsAtTravelOrigin(out _) != travelOriginSatisfied)
			{
				// The character walked up to, or away from, a waypoint with the map open.
				RefreshWaypointPanel();
			}

			/* Only on the refresh tick, not every frame. Panning and zooming call ApplyView
			 * themselves the moment they change something, so the per-frame call this replaced was
			 * marking two full-panel layers dirty sixty times a second to redraw a static baked
			 * image. What genuinely does change on its own is the fog — the character reveals
			 * ground while the map is open — and four times a second is as often as a
			 * quarter-megabyte grid can produce a visible difference anyway. */
			ApplyView();
			RefreshMarkers();
			RefreshExplored();
			RefreshGuidePanel();
		}

		/// <summary>
		/// Reads the scene's map definition into the view and the zoom range.
		/// </summary>
		private void ApplySceneToView()
		{
			if (mapView == null)
			{
				return;
			}

			ClientMapSystem.SceneView view = ClientMapSystem.Viewed;
			shownSceneName = view.SceneName;
			mapView.SceneName = view.SceneName;
			Rect rect = view.MapRect;

			mapView.MapTexture = view.MapImage;
			mapView.MapTextureRect = rect;
			mapView.MapTextureIsViewAligned = false;
			mapView.MapTextureNorthDegrees = view.Definition != null ? view.Definition.NorthOffsetDegrees : 0.0f;
			mapView.Fog = view.Fog;

			WorldMapDefinition definition = view.Definition;
			mapView.MapTint = definition != null ? definition.MapTint : Color.white;
			mapView.MapBackground = definition != null ? definition.MapBackground : new Color(0.02f, 0.04f, 0.06f, 1.0f);

			if (titleLabel != null)
			{
				titleLabel.text = string.IsNullOrEmpty(view.DisplayName)
					? "WORLD MAP"
					: view.DisplayName.ToUpperInvariant();
			}

			if (subtitleLabel != null)
			{
				string description = definition != null ? definition.Description : null;
				subtitleLabel.text = description ?? string.Empty;
				subtitleLabel.style.display = string.IsNullOrEmpty(description) ? DisplayStyle.None : DisplayStyle.Flex;
			}

			if (zoomSlider != null)
			{
				zoomSlider.lowValue = MinimumZoom();
				zoomSlider.highValue = MaximumZoom();
			}

			zoom = Mathf.Clamp(zoom, MinimumZoom(), MaximumZoom());
			ClampCenter();
			ApplyView();
			RefreshNotes();
			RebuildSceneChoices();
		}

		/// <summary>
		/// Writes the current centre and zoom onto the view.
		/// </summary>
		private void ApplyView()
		{
			mapView.View = new MapViewTransform(center, zoom, 0.0f);
			if (compass != null)
			{
				// North where the map is looking: near a pole it turns across a scene. The character's scene is the one shown.
				// Another scene is not loaded here, so it has no settings to read north from: the map's own north is drawn.
				UnityEngine.SceneManagement.Scene scene = ClientMapSystem.Viewed.IsCurrent && Character != null && Character.Transform != null
					? Character.Transform.gameObject.scene
					: default;
				compass.SetNorth(mapView.View.WorldToViewAngle(TrueNorth.BearingAt(scene, center)));
			}
			ClientMapSystem.SceneView shown = ClientMapSystem.Viewed;
			mapView.Fog = shown.Fog;

			if (mapView.MapTexture != shown.MapImage)
			{
				// The baked image loads asynchronously and can arrive after the panel is open.
				mapView.MapTexture = shown.MapImage;
			}

			if (zoomSlider != null && !Mathf.Approximately(zoomSlider.value, zoom))
			{
				zoomSlider.SetValueWithoutNotify(zoom);
			}

			/* The markers move with the view, so panning or zooming has to re-place them in the
			 * same breath. Without this a drag slides the terrain while every icon stays where it
			 * was until the next quarter-second collection catches up. */
			mapView.RelayoutMarkers();
			mapView.RefreshSurface();
		}

		/// <summary>
		/// Rebuilds the marker list, applying the player's category filters.
		/// </summary>
		private void RefreshMarkers()
		{
			ClientMapSystem.SceneView view = ClientMapSystem.Viewed;
			if (gateMode)
			{
				// The Gate's choice: the character and its discovered Gate Stones, nothing else, whatever the filters say.
				CollectGateModeMarkers(view);
				mapView.SetMarkers(markerBuffer);
				return;
			}
			if (view.IsCurrent)
			{
				// Live markers (the character, the party, NPCs) exist only in the scene the character stands in.
				ClientMapSystem.Filter.Collect(markerBuffer, Character, true, view.Fog);
			}
			else
			{
				markerBuffer.Clear();
			}
			if (view.Notes != null)
			{
				MapContent.AppendNotes(markerBuffer, view.Notes, true);
			}
			Rect mapRect = view.MapRect;
			int labelTier = MapContent.LabelTierForZoom(zoom, Mathf.Max(mapRect.width, mapRect.height) * 0.5f);
			MapContent.AppendPointsOfInterest(markerBuffer, view.Details, view.Definition,
				view.Fog, true, labelTier);
			IWaypointController waypointController = null;
			Character?.TryGet(out waypointController);
			MapContent.AppendWaypoints(markerBuffer, view.Details, view.SceneName, waypointController, true);

			for (int i = markerBuffer.Count - 1; i >= 0; --i)
			{
				if (!MapFilters.IsEnabled(markerBuffer[i].Type))
				{
					markerBuffer.RemoveAt(i);
				}
			}

			mapView.SetMarkers(markerBuffer);
		}

		/// <summary>
		/// Gate mode's markers: the local character (in its own scene) and the Gate Stones it has discovered in the viewed scene.
		/// </summary>
		private void CollectGateModeMarkers(ClientMapSystem.SceneView view)
		{
			markerBuffer.Clear();
			if (view.IsCurrent)
			{
				ClientMapSystem.Filter.Collect(markerBuffer, Character, true, view.Fog);
				for (int i = markerBuffer.Count - 1; i >= 0; --i)
				{
					if (markerBuffer[i].Type != MapMarkerType.Self)
					{
						markerBuffer.RemoveAt(i);
					}
				}
			}
			IWaypointController waypointController = null;
			Character?.TryGet(out waypointController);
			int start = markerBuffer.Count;
			MapContent.AppendWaypoints(markerBuffer, view.Details, view.SceneName, waypointController, true);
			for (int i = markerBuffer.Count - 1; i >= start; --i)
			{
				if (view.Details == null || !view.Details.Waypoints.TryGetValue(markerBuffer[i].WaypointIndex, out SceneWaypointDetails details) ||
					details == null || !details.IsGateStone)
				{
					markerBuffer.RemoveAt(i);
				}
			}
		}

		/// <summary>
		/// Updates the explored-percentage readout.
		/// </summary>
		/// <remarks>
		/// Floored rather than rounded, with as many decimals as one chunk is worth. Whole numbers were
		/// the readout's first problem: over a grid of chunks each worth a bit over one percent, a value
		/// that only ever moves in ones is indistinguishable from a counter that increments per chunk.
		/// A fixed tenth was the second: it was chosen for a thousand-metre scene, and on a generated
		/// 4.6 km world (115 by 115 chunks, each 0.0076%) the tenths digit moved once per thirteen
		/// chunks — half a kilometre of fresh ground — so the readout looked stuck. The precision is
		/// now picked from the grid so one chunk moves roughly one unit of the last digit, between
		/// one and three decimals.
		/// <para>
		/// Floored, like the whole-percent version was, because the number is a claim about how much
		/// of the scene the player has seen: rounding 99.96 up to 100.0 would say they have been
		/// everywhere with ground still left.
		/// </para>
		/// <para>
		/// Assembled from an integer rather than formatted from a float. The separator between the
		/// two digits is not the machine's to choose — a culture that writes 3,7 would put a comma in
		/// the middle of the readout, and this is a string a player reads rather than a file anything
		/// parses.
		/// </para>
		/// </remarks>
		private void RefreshExplored()
		{
			if (exploredLabel == null)
			{
				return;
			}

			FogOfWarMap fog = ClientMapSystem.Viewed.Fog;
			if (fog == null)
			{
				shownExploredChunks = -1;
				shownChunkCount = -1;
				exploredLabel.text = string.Empty;
				return;
			}

			if (fog.ExploredChunkCount == shownExploredChunks && fog.ChunkCount == shownChunkCount)
			{
				return;
			}
			shownExploredChunks = fog.ExploredChunkCount;
			shownChunkCount = fog.ChunkCount;

			exploredLabel.text = FormatExplored(shownExploredChunks, shownChunkCount);
		}

		/// <summary>
		/// Formats an explored readout with as many decimals as one chunk of the grid is worth.
		/// </summary>
		/// <param name="explored">Chunks explored.</param>
		/// <param name="total">Chunks in the scene.</param>
		/// <returns>The readout, e.g. "Explored 0.76%".</returns>
		/// <remarks>Integer arithmetic throughout, so it floors exactly and never prints a culture's comma.</remarks>
		public static string FormatExplored(int explored, int total)
		{
			if (total <= 0)
			{
				return string.Empty;
			}

			// One chunk is 100/total percent; round(log10(total/100)) decimals puts that near one unit of the last digit.
			int decimals = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log10(total / 100.0f)), 1, 3);
			long scale = decimals == 1 ? 10L : decimals == 2 ? 100L : 1000L;

			long scaled = ((long)explored * 100L * scale) / total;
			long whole = scaled / scale;
			long fraction = scaled % scale;

			return $"Explored {whole}.{fraction.ToString().PadLeft(decimals, '0')}%";
		}

		/// <summary>
		/// The closest the world map may be zoomed in, as a half-extent in world metres.
		/// </summary>
		/// <returns>The minimum zoom.</returns>
		private static float MinimumZoom()
		{
			return 30.0f;
		}

		/// <summary>
		/// The furthest the world map may be zoomed out, as a half-extent in world metres.
		/// </summary>
		/// <returns>The maximum zoom.</returns>
		/// <remarks>
		/// Scaled by Cartography: a novice sees a window onto the zone, an expert sees the whole
		/// thing at once. Never smaller than the minimum, so a tiny scene at a low tier still has
		/// a usable range rather than an inverted one.
		/// </remarks>
		private static float MaximumZoom()
		{
			Rect rect = ClientMapSystem.Viewed.MapRect;
			float extent = Mathf.Max(rect.width, rect.height) * 0.5f;
			if (extent <= 0.0f)
			{
				return 500.0f;
			}

			return Mathf.Max(MinimumZoom() + 1.0f, extent * Cartography.MaximumWorldMapExtent);
		}

		/// <summary>
		/// Holds the centre so the view stays over the mapped area.
		/// </summary>
		/// <remarks>
		/// When the view is wider than the map the centre is pinned to the map's own centre rather
		/// than clamped, because clamping an over-wide view leaves the map hard against one edge
		/// with dead space on the other for no reason the player can see.
		/// </remarks>
		private void ClampCenter()
		{
			Rect rect = ClientMapSystem.Viewed.MapRect;
			if (rect.width <= 0.0f || rect.height <= 0.0f)
			{
				return;
			}

			float halfWidth = rect.width * 0.5f;
			float halfHeight = rect.height * 0.5f;

			center.x = zoom >= halfWidth
				? rect.center.x
				: Mathf.Clamp(center.x, rect.xMin + zoom, rect.xMax - zoom);

			center.z = zoom >= halfHeight
				? rect.center.y
				: Mathf.Clamp(center.z, rect.yMin + zoom, rect.yMax - zoom);
		}

		/// <summary>
		/// Moves the view to the player's character.
		/// </summary>
		private void CenterOnCharacter()
		{
			ClientMapSystem.SceneView view = ClientMapSystem.Viewed;
			if (view.IsCurrent && Character != null && Character.Transform != null)
			{
				center = Character.Transform.position;
			}
			else
			{
				Rect rect = view.MapRect;
				center = new Vector3(rect.center.x, 0.0f, rect.center.y);
			}

			ClampCenter();
			if (mapView != null)
			{
				ApplyView();
			}
		}

		/// <summary>
		/// Zooms in response to the scroll wheel.
		/// </summary>
		/// <param name="delta">The wheel movement. Positive scrolls down, which zooms out.</param>
		private void MapView_OnScrolled(float delta)
		{
			ApplyZoom(delta > 0.0f ? zoom * ZoomStep : zoom / ZoomStep);
		}

		/// <summary>
		/// Applies the zoom slider.
		/// </summary>
		/// <param name="evt">The slider change.</param>
		private void OnZoomSliderChanged(ChangeEvent<float> evt)
		{
			ApplyZoom(evt.newValue);
		}

		/// <summary>
		/// Sets the zoom and keeps the view on the map.
		/// </summary>
		/// <param name="value">The requested half-extent in world metres.</param>
		private void ApplyZoom(float value)
		{
			zoom = Mathf.Clamp(value, MinimumZoom(), MaximumZoom());
			ClampCenter();
			ApplyView();
		}

		/// <summary>
		/// Remembers where the player clicked, so a note can be pinned there.
		/// </summary>
		/// <param name="world">The world position under the pointer.</param>
		/// <param name="marker">The marker under the pointer, when there was one.</param>
		private void MapView_OnClicked(Vector3 world, MapMarkerSnapshot? marker)
		{
			/* A click on a note selects it for editing; a click on empty ground chooses where the
			 * next note goes. Selecting by proximity rather than by hit-testing the marker
			 * elements is deliberate — see UITKMapView.FindNearestSnapshot. */
			if (marker.HasValue && marker.Value.NoteID != 0)
			{
				SelectNote(marker.Value.NoteID);
				GuideTo(marker.Value);
				return;
			}

			if (marker.HasValue && marker.Value.IsWaypoint)
			{
				SelectWaypoint(marker.Value.WaypointIndex);
				GuideTo(marker.Value);
				return;
			}

			// Any other marker — a place, an NPC, a dungeon entrance, a plot — is chosen for the guide.
			if (marker.HasValue && MapGuide.CanGuideTo(marker.Value))
			{
				if (!GuideTo(marker.Value))
				{
					SetNoteHint("The minimap can guide you only within the scene you are in.");
				}
				return;
			}

			if (!ClientMapSystem.Viewed.IsCurrent)
			{
				SetNoteHint("Notes can be pinned only to the scene you are in.");
				return;
			}

			pendingNotePosition = world;
			hasPendingNotePosition = true;
			RefreshNoteEditor();
		}

		/// <summary>
		/// Chooses a marker for the minimap's guide (<see cref="MapGuide"/>), when it is in the scene the
		/// character stands in: the minimap shows only that scene, so a marker anywhere else has no way to it.
		/// </summary>
		/// <param name="marker">The clicked marker.</param>
		/// <returns>True when the marker was chosen.</returns>
		private bool GuideTo(in MapMarkerSnapshot marker)
		{
			ClientMapSystem.SceneView view = ClientMapSystem.Viewed;
			return view.IsCurrent && MapGuide.Set(in marker, view.SceneName);
		}

		/// <summary>
		/// Shows or hides the guide section, naming the target and the distance to it.
		/// </summary>
		private void RefreshGuidePanel()
		{
			if (guideSection == null)
			{
				return;
			}

			bool show = MapGuide.HasTarget;
			guideSection.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
			if (!show)
			{
				return;
			}

			if (guideNameLabel != null)
			{
				guideNameLabel.text = MapGuide.Name ?? string.Empty;
			}

			if (Character != null && Character.Transform != null &&
				MapGuide.TryGetTarget(Character, ClientMapSystem.Fog, out Vector3 target))
			{
				Vector3 offset = target - Character.Transform.position;
				offset.y = 0.0f;
				int metres = Mathf.RoundToInt(offset.magnitude);
				SetGuideStatus(MapGuide.IsLastKnown
					? $"Last seen {metres} m away. The minimap points the way."
					: $"{metres} m away. The minimap points the way.");
			}
			else if (MapGuide.HasTarget)
			{
				SetGuideStatus("In another scene.");
			}
		}

		/// <summary>Writes the guide section's status line.</summary>
		private void SetGuideStatus(string text)
		{
			if (guideStatusLabel != null && !string.Equals(guideStatusLabel.text, text, StringComparison.Ordinal))
			{
				guideStatusLabel.text = text;
			}
		}

		/// <summary>
		/// Starts a pan.
		/// </summary>
		/// <param name="evt">The pointer press.</param>
		private void MapView_OnPointerDown(PointerDownEvent evt)
		{
			if (evt.button != 0)
			{
				return;
			}

			panning = true;
			panPointerId = evt.pointerId;
			panPrevious = evt.localPosition;

			/* Captured so a drag that leaves the panel keeps panning, and so the release is
			 * delivered here even if it happens over another window. Without the capture a player
			 * who drags off the edge of the map leaves it stuck in a panning state that only ends
			 * when they click somewhere else. */
			mapView.CapturePointer(evt.pointerId);
		}

		/// <summary>
		/// Pans the map, and updates the readouts under the pointer.
		/// </summary>
		/// <param name="evt">The pointer movement.</param>
		private void MapView_OnPointerMove(PointerMoveEvent evt)
		{
			Vector2 local = evt.localPosition;

			if (panning && evt.pointerId == panPointerId)
			{
				Vector3 previousWorld = mapView.LocalToWorld(panPrevious);
				Vector3 currentWorld = mapView.LocalToWorld(local);

				/* Moved by the difference in WORLD space rather than by pixels times a scale
				 * factor. The two are the same thing at this zoom and not the same thing after
				 * one, and deriving the delta through the same transform the map is drawn with
				 * means the ground under the pointer stays under the pointer at every zoom. */
				center -= currentWorld - previousWorld;
				ClampCenter();
				ApplyView();

				panPrevious = local;
			}

			UpdateCursorReadout(local);
		}

		/// <summary>
		/// Ends a pan.
		/// </summary>
		/// <param name="evt">The pointer release.</param>
		private void MapView_OnPointerUp(PointerUpEvent evt)
		{
			if (!panning || evt.pointerId != panPointerId)
			{
				return;
			}

			panning = false;
			mapView.ReleasePointer(evt.pointerId);
		}

		/// <summary>
		/// Clears the cursor readout when the pointer leaves the map.
		/// </summary>
		/// <param name="evt">The pointer leave.</param>
		private void MapView_OnPointerLeave(PointerLeaveEvent evt)
		{
			if (cursorLabel != null)
			{
				cursorLabel.text = string.Empty;
			}
			if (regionLabel != null)
			{
				regionLabel.text = string.Empty;
			}
		}

		/// <summary>
		/// Names the place under the pointer.
		/// </summary>
		/// <param name="local">The pointer position in the view's coordinates.</param>
		private void UpdateCursorReadout(Vector2 local)
		{
			Vector3 world = mapView.LocalToWorld(local);

			if (cursorLabel != null)
			{
				bool show = ClientSettings.MapShowCoordinates && Cartography.ShowsCoordinates;
				cursorLabel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
				if (show)
				{
					cursorLabel.text = $"{Mathf.RoundToInt(world.x)}, {Mathf.RoundToInt(world.z)}";
				}
			}

			if (regionLabel != null)
			{
				regionLabel.text = MapContent.ResolveLocationName(
					ClientMapSystem.Viewed.Definition,
					ClientMapSystem.Viewed.Fog,
					string.Empty,
					world);
			}
		}

		/// <summary>
		/// Builds one toggle per marker category, and one per point-of-interest group under PLACES.
		/// </summary>
		/// <remarks>
		/// Each place row carries a swatch of the group's colour, so the list doubles as the legend. A panel
		/// whose UXML predates the PLACES container gets the place rows in the main list instead, so no
		/// filter is ever unreachable.
		/// </remarks>
		private void BuildFilterToggles()
		{
			if (filterList == null)
			{
				return;
			}

			filterList.Clear();
			placeFilterList?.Clear();
			filterToggles.Clear();

			foreach (MapFilterCategory category in MapFilters.MarkerCategories)
			{
				Toggle toggle = new Toggle(MapFilters.Label(category));
				toggle.AddToClassList("map-filter");
				AddFilterToggle(filterList, category, toggle);
			}

			foreach (MapFilterCategory category in MapFilters.PlaceCategories)
			{
				Toggle toggle = new Toggle() { text = MapFilters.Label(category) };
				toggle.AddToClassList("map-filter");
				toggle.AddToClassList("map-filter--place");

				// The group's colour, between the box and the name: the PLACES rows are the legend too.
				if (MapFilters.TryGroupOf(category, out PointOfInterestGroup group))
				{
					VisualElement swatch = new VisualElement() { pickingMode = PickingMode.Ignore };
					swatch.AddToClassList("map-filter__swatch");
					swatch.AddToClassList("map-filter__swatch--" + MapFilters.GroupClassSuffix(group));
					VisualElement input = toggle.Q(className: Toggle.inputUssClassName);
					(input ?? toggle).Insert(input != null ? Mathf.Min(1, input.childCount) : 0, swatch);
				}

				AddFilterToggle(placeFilterList ?? filterList, category, toggle);
			}
		}

		/// <summary>
		/// Wires one filter toggle to its category and adds it to a list.
		/// </summary>
		private void AddFilterToggle(VisualElement list, MapFilterCategory category, Toggle toggle)
		{
			toggle.value = MapFilters.IsEnabled(category);
			toggle.AddToClassList("fish-toggle");

			MapFilterCategory captured = category;
			toggle.RegisterValueChangedCallback(evt =>
			{
				MapFilters.SetEnabled(captured, evt.newValue);
				nextMarkerRefreshTime = 0.0;
			});

			filterToggles[category] = toggle;
			list.Add(toggle);
		}

		/// <summary>
		/// Builds the note colour swatches.
		/// </summary>
		private void BuildColorSwatches()
		{
			if (noteColorRow == null)
			{
				return;
			}

			noteColorRow.Clear();

			for (int i = 0; i < MapContent.NoteColors.Length; ++i)
			{
				VisualElement swatch = new VisualElement();
				swatch.AddToClassList("map-swatch");
				swatch.style.backgroundColor = MapContent.NoteColors[i];

				int captured = i;
				swatch.RegisterCallback<PointerDownEvent>(evt =>
				{
					noteColorIndex = captured;
					RefreshSwatchSelection();
					evt.StopPropagation();
				});

				noteColorRow.Add(swatch);
			}

			RefreshSwatchSelection();
		}

		/// <summary>
		/// Marks the chosen colour swatch.
		/// </summary>
		private void RefreshSwatchSelection()
		{
			if (noteColorRow == null)
			{
				return;
			}

			for (int i = 0; i < noteColorRow.childCount; ++i)
			{
				noteColorRow[i].EnableInClassList(SWATCH_SELECTED_CLASS, i == noteColorIndex);
			}
		}

		/// <summary>
		/// Pins a note at the last place the player clicked.
		/// </summary>
		private void AddPendingNote()
		{
			if (!hasPendingNotePosition)
			{
				return;
			}

			string title = noteTitleField != null ? noteTitleField.value : string.Empty;
			string text = noteTextField != null ? noteTextField.value : string.Empty;

			if (string.IsNullOrWhiteSpace(title))
			{
				/* Titled, because the title is what is drawn on the map. A note with only body
				 * text would be an unlabelled pin the player has to hover to identify, which is
				 * exactly the thing notes exist to avoid. */
				SetNoteHint("Give the note a title before pinning it.");
				return;
			}

			if (ClientMapSystem.AddNote(pendingNotePosition, title, text, noteColorIndex) == null)
			{
				SetNoteHint($"You can keep {Cartography.NoteCapacity} notes here. Remove one first.");
				return;
			}

			if (noteTitleField != null)
			{
				noteTitleField.SetValueWithoutNotify(string.Empty);
			}
			if (noteTextField != null)
			{
				noteTextField.SetValueWithoutNotify(string.Empty);
			}

			hasPendingNotePosition = false;
			RefreshNoteEditor();
		}

		/// <summary>
		/// Loads a note into the editor so it can be read and removed.
		/// </summary>
		/// <param name="noteID">The note's identifier.</param>
		private void SelectNote(long noteID)
		{
			IReadOnlyList<MapNote> shownNotes = ClientMapSystem.Viewed.Notes;
			for (int i = 0; shownNotes != null && i < shownNotes.Count; ++i)
			{
				MapNote note = shownNotes[i];
				if (note.ID != noteID)
				{
					continue;
				}

				if (noteTitleField != null)
				{
					noteTitleField.SetValueWithoutNotify(note.Title);
				}
				if (noteTextField != null)
				{
					noteTextField.SetValueWithoutNotify(note.Text);
				}
				noteColorIndex = note.ColorIndex;
				RefreshSwatchSelection();
				SetNoteHint($"'{note.Title}' — click the map to pin a copy, or remove it from the list.");
				return;
			}
		}

		/// <summary>
		/// Rebuilds the list of pinned notes.
		/// </summary>
		private void RefreshNotes()
		{
			if (noteList == null)
			{
				return;
			}

			noteList.Clear();

			ClientMapSystem.SceneView view = ClientMapSystem.Viewed;
			IReadOnlyList<MapNote> notes = view.Notes ?? (IReadOnlyList<MapNote>)System.Array.Empty<MapNote>();
			for (int i = 0; i < notes.Count; ++i)
			{
				MapNote note = notes[i];

				VisualElement row = new VisualElement();
				row.AddToClassList("map-note-row");

				VisualElement dot = new VisualElement();
				dot.AddToClassList("map-note-row__dot");
				dot.style.backgroundColor = MapContent.NoteColor(note.ColorIndex);
				row.Add(dot);

				Label label = new Label(note.Title);
				label.AddToClassList("fish-label");
				label.AddToClassList("map-note-row__title");
				row.Add(label);

				Button goTo = new Button(() =>
				{
					center = note.Position;
					ClampCenter();
					ApplyView();
				})
				{
					text = "◎",
					tooltip = "Centre the map here",
				};
				goTo.AddToClassList("fish-button");
				goTo.AddToClassList("fish-button--ghost");
				goTo.AddToClassList("map-note-row__button");
				row.Add(goTo);

				long capturedID = note.ID;
				Button remove = new Button(() => ClientMapSystem.RemoveNote(capturedID))
				{
					text = "✕",
					tooltip = "Remove this note",
				};
				remove.AddToClassList("fish-button");
				remove.AddToClassList("fish-button--danger");
				remove.AddToClassList("map-note-row__button");
				// Another scene's notes are shown, not edited: they are kept for the scene the character stands in.
				remove.style.display = view.IsCurrent ? DisplayStyle.Flex : DisplayStyle.None;
				row.Add(remove);

				noteList.Add(row);
			}

			RefreshNoteEditor();
		}

		/// <summary>
		/// Updates the note editor's button and hint for the current state.
		/// </summary>
		private void RefreshNoteEditor()
		{
			if (noteAddButton != null)
			{
				noteAddButton.SetEnabled(hasPendingNotePosition && ClientMapSystem.Viewed.IsCurrent);
			}

			if (!ClientMapSystem.Viewed.IsCurrent)
			{
				SetNoteHint("Notes can be pinned only to the scene you are in.");
			}
			else if (hasPendingNotePosition)
			{
				SetNoteHint($"Pin at {Mathf.RoundToInt(pendingNotePosition.x)}, {Mathf.RoundToInt(pendingNotePosition.z)} — {ClientMapSystem.Notes.Count}/{Cartography.NoteCapacity} used.");
			}
			else
			{
				SetNoteHint($"Click the map to choose where a note goes — {ClientMapSystem.Notes.Count}/{Cartography.NoteCapacity} used.");
			}
		}

		/// <summary>
		/// Writes the note editor's explanatory line.
		/// </summary>
		/// <param name="text">The text to show.</param>
		private void SetNoteHint(string text)
		{
			if (noteHint != null)
			{
				noteHint.text = text;
			}
		}

		/// <summary>
		/// Rebuilds everything derived from the scene when the character changes zone.
		/// </summary>
		private void MapSystem_OnSceneChanged()
		{
			hasPendingNotePosition = false;
			/* A selection is per scene: index 3 in the new zone is a different place. The
			 * pending flag is dropped too — a request sent from the old zone is answered
			 * NotInScene by the server, and that answer names the old scene, which the refusal
			 * handler ignores. */
			hasSelectedWaypoint = false;
			ShowSelectedWaypoint();
			travelPending = false;
			ApplySceneToView();
			CenterOnCharacter();
			nextMarkerRefreshTime = 0.0;
			RefreshWaypointPanel();
		}

		#region Waypoints

		/// <summary>
		/// Selects a waypoint into the side bar.
		/// </summary>
		/// <param name="waypointIndex">The waypoint's index within the current scene.</param>
		private void SelectWaypoint(int waypointIndex)
		{
			hasSelectedWaypoint = true;
			selectedWaypointIndex = waypointIndex;
			ShowSelectedWaypoint();
			RefreshWaypointPanel();
			RefreshGateList();
		}

		/// <summary>
		/// Marks the selected waypoint's marker on the map (UITKMapView.SelectedWaypointIndex), and has the markers redrawn at
		/// once rather than on the next quarter-second refresh, so the click and the highlight land together.
		/// </summary>
		private void ShowSelectedWaypoint()
		{
			if (mapView == null)
			{
				return;
			}
			mapView.SelectedWaypointIndex = hasSelectedWaypoint ? selectedWaypointIndex : -1;
			nextMarkerRefreshTime = 0.0;
		}

		/// <summary>
		/// The details of a waypoint in the current scene, when the cache knows it.
		/// </summary>
		private static bool TryGetWaypointDetails(int waypointIndex, out SceneWaypointDetails details)
		{
			details = null;
			WorldSceneDetails scene = ClientMapSystem.Viewed.Details;
			return scene != null && scene.Waypoints != null && scene.Waypoints.TryGetValue(waypointIndex, out details) && details != null;
		}

		/// <summary>
		/// Sends the fast-travel request for the selected waypoint.
		/// </summary>
		/// <remarks>
		/// The button disables until the server answers or the timeout passes; nothing here
		/// moves the character or closes the map, because nothing here knows whether the request
		/// will be honoured. Sound and the hook fire on the click, which is what they describe.
		/// </remarks>
		private void RequestFastTravel()
		{
			if (!hasSelectedWaypoint || travelPending || Character == null || !ClientMapSystem.Viewed.IsCurrent)
			{
				return;
			}

			string sceneName = ClientMapSystem.SceneName;
			if (string.IsNullOrEmpty(sceneName))
			{
				return;
			}

			if (!Character.TryGet(out IWaypointController controller) ||
				!controller.IsUnlocked(sceneName, selectedWaypointIndex))
			{
				// The selection outlived the record somehow; say so instead of sending a request the server will refuse.
				SetWaypointHint("You have not discovered this waypoint.");
				return;
			}

			if (!IsAtTravelOrigin(out WaypointTravelPolicy policy))
			{
				SetWaypointHint(DescribeOriginRule(policy));
				return;
			}

			ClientUIAudio.Play(FastTravelClickSound);
			OnFastTravelClicked?.Invoke(sceneName, selectedWaypointIndex);

			travelPending = true;
			travelPendingUntil = Time.unscaledTimeAsDouble + TravelResponseTimeout;
			RefreshWaypointPanel();

			// The Travel module's controller sends the request (WaypointTravelRequestBroadcast).
			controller.RequestTravel(sceneName, selectedWaypointIndex);
		}

		/// <summary>
		/// Writes the waypoint section for the current selection and request state.
		/// </summary>
		private void RefreshWaypointPanel()
		{
			if (waypointSection == null)
			{
				return;
			}

			SceneWaypointDetails details = null;
			bool known = hasSelectedWaypoint && TryGetWaypointDetails(selectedWaypointIndex, out details);
			travelOriginSatisfied = IsAtTravelOrigin(out WaypointTravelPolicy policy);

			if (waypointNameLabel != null)
			{
				waypointNameLabel.text = known ? details.Name : "No waypoint selected";
			}
			if (waypointDescriptionLabel != null)
			{
				string description = known ? details.Description : null;
				waypointDescriptionLabel.text = description ?? string.Empty;
				waypointDescriptionLabel.style.display = string.IsNullOrEmpty(description) ? DisplayStyle.None : DisplayStyle.Flex;
			}
			bool viewingCurrent = ClientMapSystem.Viewed.IsCurrent;
			if (gateMode || (known && details.IsGateStone))
			{
				RefreshGatePanel(known, details);
				return;
			}
			if (waypointTravelButton != null)
			{
				waypointTravelButton.SetEnabled(known && viewingCurrent && !travelPending && travelOriginSatisfied);
				waypointTravelButton.text = travelPending ? "TRAVELLING…" : "FAST TRAVEL";
			}

			if (travelPending)
			{
				SetWaypointHint("Waiting for the server…");
			}
			else if (known && !viewingCurrent)
			{
				SetWaypointHint("Fast travel works only within the scene you are in.");
			}
			else if (known && !travelOriginSatisfied)
			{
				SetWaypointHint(DescribeOriginRule(policy));
			}
			else if (known)
			{
				SetWaypointHint("Fast travel here. You must be able to act and out of combat.");
			}
			else
			{
				SetWaypointHint("Click a discovered waypoint on the map to select it.");
			}
		}

		/// <summary>
		/// The waypoint section for a Gate Stone, and for anything while a Gate waits for its stone (issue #241): in gate mode
		/// the button opens the gate to the selected stone, in any scene the dropdown shows; otherwise a stone is only described,
		/// since it is never a fast-travel point.
		/// </summary>
		private void RefreshGatePanel(bool known, SceneWaypointDetails details)
		{
			bool isStone = known && details.IsGateStone;
			bool discovered = isStone && Character != null && Character.TryGet(out IWaypointController controller) &&
				controller.IsUnlocked(ClientMapSystem.Viewed.SceneName, selectedWaypointIndex);
			if (waypointTravelButton != null)
			{
				waypointTravelButton.SetEnabled(gateMode && discovered);
				waypointTravelButton.text = gateMode ? "OPEN GATE" : "GATE STONE";
			}
			string left = $"{Mathf.CeilToInt(GateStoneChoice.RemainingSeconds)} s left.";
			if (!gateMode)
			{
				SetWaypointHint("A Gate Stone. Cast Gate to open a portal to it; it is not a fast-travel point.");
			}
			else if (!known)
			{
				SetWaypointHint($"Choose a Gate Stone you have discovered, in any land you have visited. {left}");
			}
			else if (!isStone)
			{
				SetWaypointHint($"Gate opens only to a Gate Stone. {left}");
			}
			else if (!discovered)
			{
				SetWaypointHint($"You have not discovered this Gate Stone. {left}");
			}
			else
			{
				SetWaypointHint($"Open a gate to {details.Name} in front of you. {left}");
			}
		}

		/// <summary>The waypoint section's button: fast travel, or in gate mode the Gate's choice.</summary>
		private void OnWaypointButtonClicked()
		{
			if (!gateMode)
			{
				RequestFastTravel();
				return;
			}
			if (!hasSelectedWaypoint || !TryGetWaypointDetails(selectedWaypointIndex, out SceneWaypointDetails details) || !details.IsGateStone)
			{
				return;
			}
			ClientUIAudio.Play(FastTravelClickSound);
			// The server answers by ending gate mode (opened) or with a toast (refused; the wait goes on).
			GateStoneChoice.Choose(ClientMapSystem.Viewed.SceneName, selectedWaypointIndex);
		}

		/// <summary>
		/// A Gate waits for its stone: the map opens on the scene the character stands in, in gate mode, showing only the Gate
		/// Stones the character has discovered, with every one of them (in any land) listed in the side bar.
		/// </summary>
		private void GateStoneChoice_OnBegan()
		{
			gateMode = true;
			hasSelectedWaypoint = false;
			if (!Visible)
			{
				Show();
			}
			else
			{
				ClientMapSystem.ViewScene(null);
			}
			RefreshGateList();
			// A stone in this land is the likeliest choice: selected, so one click opens the gate.
			if (gateStones.Count > 0 && string.Equals(gateStones[0].SceneName, ClientMapSystem.SceneName, StringComparison.Ordinal))
			{
				SelectWaypoint(gateStones[0].WaypointIndex);
			}
			else
			{
				ShowSelectedWaypoint();
				RefreshWaypointPanel();
			}
			nextMarkerRefreshTime = 0.0;
			Log.Debug("UITKMap", $"Gate mode: {gateStones.Count} discovered Gate Stone(s), {GateStoneChoice.RemainingSeconds:0} s to choose.");
		}

		/// <summary>
		/// Rebuilds the Gate Stone list: shown only in gate mode, one row per discovered stone (this land's first), the selected
		/// one marked. A row shows its stone on the map and selects it.
		/// </summary>
		private void RefreshGateList()
		{
			if (gateSection == null)
			{
				return;
			}
			gateSection.style.display = gateMode ? DisplayStyle.Flex : DisplayStyle.None;
			if (!gateMode)
			{
				gateList?.Clear();
				gateStones.Clear();
				return;
			}

			IWaypointController waypoints = null;
			Character?.TryGet(out waypoints);
			GateStoneDirectory.Collect(ClientMapSystem.DetailsCache, waypoints, ClientMapSystem.SceneName, gateStones);

			if (gateHint != null)
			{
				gateHint.text = gateStones.Count == 0
					? "You have discovered no Gate Stone. Find one and use it (E) to open gates to it."
					: "Choose where the gate leads, then OPEN GATE.";
			}
			if (gateList == null)
			{
				return;
			}
			gateList.Clear();
			string viewed = ClientMapSystem.Viewed.SceneName;
			for (int i = 0; i < gateStones.Count; ++i)
			{
				DiscoveredGateStone stone = gateStones[i];
				bool here = string.Equals(stone.SceneName, ClientMapSystem.SceneName, StringComparison.Ordinal);
				string name = string.IsNullOrEmpty(stone.Details.Name) ? "Gate Stone" : stone.Details.Name;
				Button row = new Button(() => ChooseGateStoneRow(stone))
				{
					text = here ? name : $"{name}  ·  {SceneDisplayNameFor(stone.SceneName)}",
					tooltip = here ? $"{name} (this land)" : $"{name}, in {SceneDisplayNameFor(stone.SceneName)}",
				};
				row.AddToClassList("fish-button");
				row.AddToClassList("fish-button--ghost");
				row.AddToClassList(GATE_ROW_CLASS);
				if (hasSelectedWaypoint && selectedWaypointIndex == stone.WaypointIndex &&
					string.Equals(stone.SceneName, viewed, StringComparison.Ordinal))
				{
					row.AddToClassList(GATE_ROW_SELECTED_CLASS);
				}
				gateList.Add(row);
			}
		}

		/// <summary>A Gate Stone row was clicked: show its land, select it and centre the map on it.</summary>
		private void ChooseGateStoneRow(DiscoveredGateStone stone)
		{
			if (!gateMode)
			{
				return;
			}
			bool here = string.Equals(stone.SceneName, ClientMapSystem.SceneName, StringComparison.Ordinal);
			// Switching the viewed scene clears the selection, so it comes first.
			ClientMapSystem.ViewScene(here ? null : stone.SceneName);
			SelectWaypoint(stone.WaypointIndex);
			if (TryGetWaypointDetails(stone.WaypointIndex, out SceneWaypointDetails details))
			{
				center = details.Position;
				ClampCenter();
				ApplyView();
			}
		}

		/// <summary>The wait ended: gate mode ends and the map it opened closes.</summary>
		private void GateStoneChoice_OnEnded()
		{
			if (!gateMode)
			{
				return;
			}
			gateMode = false;
			hasSelectedWaypoint = false;
			ShowSelectedWaypoint();
			RefreshWaypointPanel();
			RefreshGateList();
			nextMarkerRefreshTime = 0.0;
			if (Visible)
			{
				Hide();
			}
		}

		/// <summary>
		/// Whether the character may fast travel from where it stands, by the server's origin rule
		/// as the owner payload reported it. A hint only; the server decides.
		/// </summary>
		/// <param name="policy">The rule that was applied, for the explanation.</param>
		private bool IsAtTravelOrigin(out WaypointTravelPolicy policy)
		{
			policy = WaypointTravelPolicy.Default;
			if (Character == null || Character.Transform == null ||
				!Character.TryGet(out IWaypointController controller))
			{
				// No character means no button to enable; claim nothing about the rule.
				return true;
			}
			policy = controller.TravelPolicy;
			return policy.IsSatisfiedBy(ClientMapSystem.SceneDetails, ClientMapSystem.SceneName, controller, Character.Transform.position);
		}

		/// <summary>
		/// Player-facing statement of the origin rule.
		/// </summary>
		private static string DescribeOriginRule(in WaypointTravelPolicy policy)
		{
			return $"You must be within {policy.NearbyRange:0}m of a discovered waypoint to fast travel.";
		}

		/// <summary>
		/// Writes the waypoint section's explanatory line.
		/// </summary>
		private void SetWaypointHint(string text)
		{
			if (waypointHint != null)
			{
				waypointHint.text = text;
			}
		}

		/// <summary>
		/// Whether an event is about the local character. The static events fire for every
		/// character that raises them on this peer; only the owner's matter here.
		/// </summary>
		private bool IsLocal(ICharacter character)
		{
			return Character != null && character != null && ReferenceEquals(character, Character);
		}

		/// <summary>
		/// A waypoint was discovered: redraw, tell the player, and play the sound.
		/// </summary>
		private void Waypoint_OnUnlocked(ICharacter character, string sceneName, int waypointIndex)
		{
			if (!IsLocal(character))
			{
				return;
			}

			nextMarkerRefreshTime = 0.0;
			ClientUIAudio.Play(WaypointDiscoveredSound);
			RefreshGateList();

			string name = string.Equals(sceneName, ClientMapSystem.SceneName, StringComparison.Ordinal) &&
				TryGetWaypointDetails(waypointIndex, out SceneWaypointDetails details)
				? details.Name
				: null;
			ChatWindow.ShowSystem(string.IsNullOrEmpty(name) ? "Waypoint discovered." : $"Waypoint discovered: {name}.");
		}

		/// <summary>
		/// The server moved the character: close the map, the way arriving somewhere does.
		/// </summary>
		private void Waypoint_OnTravelled(ICharacter character, string sceneName, int waypointIndex)
		{
			if (!IsLocal(character))
			{
				return;
			}

			travelPending = false;
			ClientUIAudio.Play(FastTravelArriveSound);
			RefreshWaypointPanel();
			if (Visible)
			{
				Hide();
			}
		}

		/// <summary>
		/// The server refused: hand the button back and say why.
		/// </summary>
		private void Waypoint_OnTravelRefused(ICharacter character, string sceneName, int waypointIndex, WaypointTravelRefusalReason reason)
		{
			if (!IsLocal(character))
			{
				return;
			}

			travelPending = false;
			ClientUIAudio.Play(FastTravelRefusedSound);
			RefreshWaypointPanel();
			SetWaypointHint(DescribeRefusal(reason));
		}

		/// <summary>
		/// The server asked for the map to open on a waypoint — the character used one they had
		/// already discovered.
		/// </summary>
		private void Waypoint_OnMapRequested(ICharacter character, string sceneName, int waypointIndex)
		{
			if (!IsLocal(character) || !string.Equals(sceneName, ClientMapSystem.SceneName, StringComparison.Ordinal))
			{
				return;
			}

			if (!Visible)
			{
				Show();
			}

			SelectWaypoint(waypointIndex);
			if (TryGetWaypointDetails(waypointIndex, out SceneWaypointDetails details))
			{
				center = details.Position;
				ClampCenter();
				ApplyView();
			}
		}

		/// <summary>
		/// Player-facing text for a refusal.
		/// </summary>
		private static string DescribeRefusal(WaypointTravelRefusalReason reason)
		{
			switch (reason)
			{
				case WaypointTravelRefusalReason.CannotAct: return "You cannot travel right now.";
				case WaypointTravelRefusalReason.InCombat: return "You cannot fast travel while in combat.";
				case WaypointTravelRefusalReason.UnknownWaypoint: return "That waypoint is not here.";
				case WaypointTravelRefusalReason.NotInScene: return "You can only travel to waypoints in the area you are in.";
				case WaypointTravelRefusalReason.Locked: return "You have not discovered this waypoint.";
				case WaypointTravelRefusalReason.ConditionsNotMet: return "You do not meet the requirements to travel here.";
				case WaypointTravelRefusalReason.TooSoon: return "You travelled a moment ago. Try again shortly.";
				case WaypointTravelRefusalReason.NotNearWaypoint: return "You must be at a discovered waypoint to fast travel.";
				case WaypointTravelRefusalReason.NotATravelPoint: return "A Gate Stone is not a fast-travel point. Cast Gate to open a portal to it.";
				default: return "Fast travel was refused.";
			}
		}

		#endregion

		/// <summary>
		/// The viewed scene changed (the dropdown, or the map reset to the character's scene), or something it shows arrived
		/// (its image, the server's fog for it).
		/// </summary>
		private void MapSystem_OnViewedSceneChanged()
		{
			if (mapView == null)
			{
				return;
			}
			if (string.Equals(ClientMapSystem.Viewed.SceneName, shownSceneName, StringComparison.Ordinal))
			{
				// The same scene with more to show: redraw without moving the view.
				ApplyView();
				RefreshExplored();
				nextMarkerRefreshTime = 0.0;
				return;
			}
			hasPendingNotePosition = false;
			hasSelectedWaypoint = false;
			ShowSelectedWaypoint();
			ApplySceneToView();
			CenterOnCharacter();
			RefreshExplored();
			nextMarkerRefreshTime = 0.0;
			RefreshWaypointPanel();
			RefreshGateList();
		}

		private void MapSystem_OnVisitedScenesChanged()
		{
			RebuildSceneChoices();
		}

		/// <summary>The dropdown picked a scene.</summary>
		private void OnSceneSelected(ChangeEvent<string> evt)
		{
			if (sceneSelect == null)
			{
				return;
			}
			int index = sceneSelect.index;
			if (index >= 0 && index < sceneChoiceScenes.Count)
			{
				ClientMapSystem.ViewScene(sceneChoiceScenes[index]);
			}
		}

		/// <summary>
		/// Fills the dropdown with every scene the account has entered — the character's own first — and selects the shown one.
		/// </summary>
		private void RebuildSceneChoices()
		{
			if (sceneSelect == null)
			{
				return;
			}

			sceneChoiceScenes.Clear();
			var labels = new List<string>();
			string current = ClientMapSystem.SceneName;
			if (!string.IsNullOrEmpty(current))
			{
				sceneChoiceScenes.Add(current);
				labels.Add(MapSceneChoices.Label(SceneDisplayNameFor(current), isCurrent: true));
			}

			var others = new List<(string Scene, string Label)>();
			IReadOnlyList<ClientMapSystem.VisitedScene> visited = ClientMapSystem.VisitedScenes;
			for (int i = 0; i < visited.Count; ++i)
			{
				string scene = visited[i].SceneName;
				if (!string.Equals(scene, current, StringComparison.Ordinal) && MapSceneChoices.IsListable(scene, ClientMapSystem.DetailsCache))
				{
					others.Add((scene, SceneDisplayNameFor(scene)));
				}
			}
			others.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
			foreach ((string scene, string label) in others)
			{
				sceneChoiceScenes.Add(scene);
				labels.Add(MapSceneChoices.Label(label, isCurrent: false));
			}
			MapSceneChoices.Disambiguate(labels, sceneChoiceScenes);

			sceneSelect.choices = labels;
			int shown = sceneChoiceScenes.IndexOf(ClientMapSystem.Viewed.SceneName);
			if (shown >= 0)
			{
				sceneSelect.SetValueWithoutNotify(labels[shown]);
			}
			// One entry is nothing to choose between.
			sceneSelect.style.display = labels.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>A scene's player-facing name: its map definition's, else its scene name.</summary>
		private static string SceneDisplayNameFor(string sceneName)
		{
			WorldSceneDetails details = null;
			ClientMapSystem.DetailsCache?.Scenes?.TryGetValue(sceneName, out details);
			WorldMapDefinition definition = details != null ? details.MapDefinition : null;
			return definition != null && !string.IsNullOrEmpty(definition.ResolvedDisplayName) ? definition.ResolvedDisplayName : sceneName;
		}

		/// <summary>
		/// Rebuilds the note list when it changes.
		/// </summary>
		private void MapSystem_OnNotesChanged()
		{
			RefreshNotes();
			nextMarkerRefreshTime = 0.0;
		}
	}
}
