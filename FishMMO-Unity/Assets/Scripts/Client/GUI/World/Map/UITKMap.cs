using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
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
	/// as far as their Cartography allows, filter by category, and write notes. The notes are the
	/// reason the panel has a side bar at all; everything else would have fitted in the corner.
	/// </para>
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

		/// <summary>How much one notch of the scroll wheel changes the zoom, as a multiplier.</summary>
		private const float ZoomStep = 1.25f;

		/// <summary>How often the markers are rebuilt while the panel is open, in seconds.</summary>
		/// <remarks>
		/// Slower than the minimap's. The world map is a planning tool looked at while standing
		/// still, its markers are small relative to the area it covers, and a player reading it is
		/// not tracking a creature's approach frame by frame.
		/// </remarks>
		private const double MarkerRefreshInterval = 0.25;

		/// <summary>The map view mounted into the panel.</summary>
		private UITKMapView mapView;

		/// <summary>Label naming the scene.</summary>
		private Label titleLabel;

		/// <summary>Label describing the scene.</summary>
		private Label subtitleLabel;

		/// <summary>Label showing the coordinates under the pointer.</summary>
		private Label cursorLabel;

		/// <summary>Label showing how much of the scene has been explored.</summary>
		private Label exploredLabel;

		/// <summary>Label naming the region under the pointer.</summary>
		private Label regionLabel;

		/// <summary>Slider controlling the zoom.</summary>
		private Slider zoomSlider;

		/// <summary>Container the filter toggles live in.</summary>
		private VisualElement filterList;

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
			mapView.OnMapScrolled += MapView_OnScrolled;
			mapView.OnMapClicked += MapView_OnClicked;
			mapView.RegisterCallback<PointerDownEvent>(MapView_OnPointerDown);
			mapView.RegisterCallback<PointerMoveEvent>(MapView_OnPointerMove);
			mapView.RegisterCallback<PointerUpEvent>(MapView_OnPointerUp);
			mapView.RegisterCallback<PointerLeaveEvent>(MapView_OnPointerLeave);
			host.Clear();
			host.Add(mapView);

			titleLabel = root.Q<Label>(TITLE_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			cursorLabel = root.Q<Label>(CURSOR_LABEL_NAME);
			exploredLabel = root.Q<Label>(EXPLORED_LABEL_NAME);
			regionLabel = root.Q<Label>(REGION_LABEL_NAME);
			zoomSlider = root.Q<Slider>(ZOOM_SLIDER_NAME);
			filterList = root.Q<VisualElement>(FILTER_LIST_NAME);
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

			BuildFilterToggles();
			BuildColorSwatches();

			ClientMapSystem.OnSceneMapChanged += MapSystem_OnSceneChanged;
			ClientMapSystem.OnNotesChanged += MapSystem_OnNotesChanged;

			ApplySceneToView();
		}

		/// <summary>
		/// Drops subscriptions.
		/// </summary>
		public override void OnDestroying()
		{
			ClientMapSystem.OnSceneMapChanged -= MapSystem_OnSceneChanged;
			ClientMapSystem.OnNotesChanged -= MapSystem_OnNotesChanged;
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
			CenterOnCharacter();
			nextMarkerRefreshTime = 0.0;
			RefreshNotes();
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

			/* Only on the refresh tick, not every frame. Panning and zooming call ApplyView
			 * themselves the moment they change something, so the per-frame call this replaced was
			 * marking two full-panel layers dirty sixty times a second to redraw a static baked
			 * image. What genuinely does change on its own is the fog — the character reveals
			 * ground while the map is open — and four times a second is as often as a
			 * quarter-megabyte grid can produce a visible difference anyway. */
			ApplyView();
			RefreshMarkers();
			RefreshExplored();
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

			Rect rect = ClientMapSystem.MapRect;

			mapView.MapTexture = ClientMapSystem.MapImage;
			mapView.MapTextureRect = rect;
			mapView.MapTextureIsViewAligned = false;
			mapView.Fog = ClientMapSystem.Fog;

			WorldMapDefinition definition = ClientMapSystem.Definition;
			mapView.MapTint = definition != null ? definition.MapTint : Color.white;
			mapView.MapBackground = definition != null ? definition.MapBackground : new Color(0.02f, 0.04f, 0.06f, 1.0f);

			if (titleLabel != null)
			{
				titleLabel.text = string.IsNullOrEmpty(ClientMapSystem.SceneDisplayName)
					? "WORLD MAP"
					: ClientMapSystem.SceneDisplayName.ToUpperInvariant();
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
		}

		/// <summary>
		/// Writes the current centre and zoom onto the view.
		/// </summary>
		private void ApplyView()
		{
			mapView.View = new MapViewTransform(center, zoom, 0.0f);
			mapView.Fog = ClientMapSystem.Fog;

			if (mapView.MapTexture != ClientMapSystem.MapImage)
			{
				// The baked image loads asynchronously and can arrive after the panel is open.
				mapView.MapTexture = ClientMapSystem.MapImage;
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
			ClientMapSystem.Filter.Collect(markerBuffer, Character, true, ClientMapSystem.Fog);
			MapContent.AppendNotes(markerBuffer, ClientMapSystem.Notes, true);
			MapContent.AppendPointsOfInterest(markerBuffer, ClientMapSystem.Definition, ClientMapSystem.Fog, true);

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
		/// Updates the explored-percentage readout.
		/// </summary>
		private void RefreshExplored()
		{
			if (exploredLabel == null)
			{
				return;
			}

			if (ClientMapSystem.Fog == null)
			{
				exploredLabel.text = string.Empty;
				return;
			}

			float fraction = ClientMapSystem.Fog.ExploredFraction();
			exploredLabel.text = $"Explored {Mathf.FloorToInt(fraction * 100.0f)}%";
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
			Rect rect = ClientMapSystem.MapRect;
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
			Rect rect = ClientMapSystem.MapRect;
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
			if (Character != null && Character.Transform != null)
			{
				center = Character.Transform.position;
			}
			else
			{
				Rect rect = ClientMapSystem.MapRect;
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
				return;
			}

			pendingNotePosition = world;
			hasPendingNotePosition = true;
			RefreshNoteEditor();
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
					ClientMapSystem.Definition,
					ClientMapSystem.Fog,
					string.Empty,
					world);
			}
		}

		/// <summary>
		/// Builds one toggle per marker category.
		/// </summary>
		private void BuildFilterToggles()
		{
			if (filterList == null)
			{
				return;
			}

			filterList.Clear();
			filterToggles.Clear();

			foreach (MapFilterCategory category in MapFilters.Categories)
			{
				Toggle toggle = new Toggle(MapFilters.Label(category))
				{
					value = MapFilters.IsEnabled(category),
				};
				toggle.AddToClassList("fish-toggle");
				toggle.AddToClassList("map-filter");

				MapFilterCategory captured = category;
				toggle.RegisterValueChangedCallback(evt =>
				{
					MapFilters.SetEnabled(captured, evt.newValue);
					nextMarkerRefreshTime = 0.0;
				});

				filterToggles[category] = toggle;
				filterList.Add(toggle);
			}
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
			for (int i = 0; i < ClientMapSystem.Notes.Count; ++i)
			{
				MapNote note = ClientMapSystem.Notes[i];
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

			IReadOnlyList<MapNote> notes = ClientMapSystem.Notes;
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
				noteAddButton.SetEnabled(hasPendingNotePosition);
			}

			if (hasPendingNotePosition)
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
			ApplySceneToView();
			CenterOnCharacter();
			nextMarkerRefreshTime = 0.0;
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
