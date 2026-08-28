using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// The heads-up minimap: a live overhead view centred on the player, with markers, fog of war,
	/// a location readout and a way into the world map.
	/// </summary>
	/// <remarks>
	/// <para><b>What this panel owns and what it borrows.</b> It owns its zoom, its rotation mode
	/// and the decision of when to render; everything else — the overhead camera, the explored
	/// map, the notes, the marker rules — lives in <see cref="ClientMapSystem"/> and is shared
	/// with the world map. The panel is also the map subsystem's heartbeat: it drives
	/// <see cref="ClientMapSystem.Tick"/> from its own update whether or not it is visible,
	/// because exploration happens when the character walks, not when the player looks at a
	/// map.</para>
	///
	/// <para><b>The bugs this replaced.</b> The previous implementation rendered nothing at all,
	/// and had four independent reasons for it: it overwrote the camera's culling mask with an
	/// inspector field that was left at zero, so only an empty layer was drawn; the only component
	/// that put anything on that layer was used by no prefab in the project; the camera sat at a
	/// height of 1000 against a far clip plane of 1000, placing the ground exactly on the far
	/// plane; and the camera's own transform started below the world with the panel closed, so
	/// nothing positioned it until the player pressed the minimap key. Each of those alone would
	/// have produced the same black square, which is why reading the code never explained it. The
	/// camera is now configured entirely in code and re-asserted on every render — see
	/// <see cref="MinimapCameraRenderer"/>.</para>
	/// </remarks>
	public class UITKMinimap : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>Name of the element the map view is mounted into.</summary>
		private const string MINIMAP_VIEW_NAME = "minimap-view";

		/// <summary>Name of the label showing the current region.</summary>
		private const string LOCATION_LABEL_NAME = "minimap-location";

		/// <summary>Name of the label showing the player's coordinates.</summary>
		private const string COORDINATE_LABEL_NAME = "minimap-coordinates";

		/// <summary>Name of the button that opens the world map.</summary>
		private const string MAP_BUTTON_NAME = "minimap-map-button";

		/// <summary>Name of the zoom-in button.</summary>
		private const string ZOOM_IN_BUTTON_NAME = "minimap-zoom-in";

		/// <summary>Name of the zoom-out button.</summary>
		private const string ZOOM_OUT_BUTTON_NAME = "minimap-zoom-out";

		/// <summary>Name of the north indicator.</summary>
		private const string COMPASS_NAME = "minimap-compass";

		/// <summary>Name of the world map panel this one opens.</summary>
		private const string WORLD_MAP_PANEL = "UIMap";

		/// <summary>How often the marker list is rebuilt, in seconds.</summary>
		/// <remarks>
		/// Ten times a second, independently of the camera's frame rate. Markers are UI elements
		/// whose positions are recomputed from scratch each time, and a creature crossing a
		/// two-hundred-pixel map at running speed moves about two pixels in a tenth of a second —
		/// so a faster refresh costs real work to move things by less than they are wide.
		/// </remarks>
		private const double MarkerRefreshInterval = 0.1;

		/// <summary>How much one notch of the scroll wheel changes the zoom, as a multiplier.</summary>
		private const float ZoomStep = 1.2f;

		/// <summary>
		/// The scene details cache, used to find the current scene's map definition.
		/// </summary>
		/// <remarks>
		/// Assigned in the inspector, and handed to <see cref="ClientMapSystem"/> rather than kept.
		/// The world map needs the same asset and is opened from here, so routing it through the
		/// shared system means only one panel has to carry the reference.
		/// </remarks>
		[Tooltip("World scene details cache. Supplies each scene's map definition and boundaries.")]
		public WorldSceneDetailsCache WorldSceneDetails;

		/// <summary>
		/// A camera in the scene to use for the overhead render instead of creating one.
		/// </summary>
		/// <remarks>
		/// Optional, and entirely reconfigured when it is used: position, rotation, projection,
		/// clipping planes, clear flags and culling mask are all written by the renderer on every
		/// render. Leaving this empty is the recommended setup — the renderer then makes its own
		/// camera and there is nothing in a scene to drift out of step.
		/// </remarks>
		[Tooltip("Optional scene camera for the overhead render. Leave empty to have one created.")]
		public Camera MinimapCamera;

		/// <summary>
		/// Extra layers the overhead camera photographs beyond Default, Ground and Water.
		/// </summary>
		[Tooltip("Extra layers the minimap camera photographs, beyond Default, Ground and Water.")]
		public LayerMask AdditionalLayers;

		/// <summary>The map view mounted into the panel.</summary>
		private UITKMapView mapView;

		/// <summary>Label showing the current region name.</summary>
		private Label locationLabel;

		/// <summary>Label showing the player's coordinates.</summary>
		private Label coordinateLabel;

		/// <summary>The north indicator, rotated to match the view.</summary>
		private VisualElement compass;

		/// <summary>Snapshot buffer, reused so a refresh allocates nothing.</summary>
		private readonly List<MapMarkerSnapshot> markerBuffer = new List<MapMarkerSnapshot>();

		/// <summary>Time, on the unscaled clock, of the next marker rebuild.</summary>
		private double nextMarkerRefreshTime;

		/// <summary>The current zoom, as the view's half-extent in world metres.</summary>
		private float zoom = 25.0f;

		/// <summary>Whether the renderer has been configured for the current settings.</summary>
		private bool rendererConfigured;

		/// <summary>The Cartography tier the renderer was configured for.</summary>
		private int configuredDetailTier = -1;

		/// <summary>
		/// Builds the view, mounts it, and wires the panel's controls.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			ClientMapSystem.DetailsCache = WorldSceneDetails;
			if (WorldSceneDetails == null)
			{
				Log.Warning("UITKMinimap", $"No WorldSceneDetailsCache is assigned to '{Name}'. The minimap will still render, but there is nothing to look a scene's map bounds up in, so fog of war and the world map are unavailable. Assign the cache on the GameObject.");
			}

			VisualElement host = root.Q<VisualElement>(MINIMAP_VIEW_NAME);
			if (host == null)
			{
				Log.Error("UITKMinimap", $"The minimap UXML has no element named '{MINIMAP_VIEW_NAME}'; there is nowhere to mount the map.");
				return;
			}

			mapView = new UITKMapView()
			{
				name = "minimap-surface",
				/* Position, not Ignore. The minimap accepts a scroll to zoom and a click to ping,
				 * and an element that ignores picking receives neither. The wrapper above it in
				 * the UXML stays Ignore so the rest of the screen is unaffected. */
				pickingMode = PickingMode.Position,
			};
			mapView.style.flexGrow = 1.0f;
			mapView.MapTextureIsViewAligned = true;
			mapView.OnMapScrolled += MapView_OnScrolled;
			host.Clear();
			host.Add(mapView);

			locationLabel = root.Q<Label>(LOCATION_LABEL_NAME);
			coordinateLabel = root.Q<Label>(COORDINATE_LABEL_NAME);
			compass = root.Q<VisualElement>(COMPASS_NAME);

			Button mapButton = root.Q<Button>(MAP_BUTTON_NAME);
			if (mapButton != null)
			{
				mapButton.clicked += () => UIManager.ToggleVisibility(WORLD_MAP_PANEL);
			}

			Button zoomIn = root.Q<Button>(ZOOM_IN_BUTTON_NAME);
			if (zoomIn != null)
			{
				zoomIn.clicked += () => ApplyZoom(zoom / ZoomStep);
			}

			Button zoomOut = root.Q<Button>(ZOOM_OUT_BUTTON_NAME);
			if (zoomOut != null)
			{
				zoomOut.clicked += () => ApplyZoom(zoom * ZoomStep);
			}

			zoom = ClientSettings.GetFloat(ClientSettings.MinimapZoomKey, 25.0f, 5.0f, 200.0f);

			ClientMapSystem.OnSceneMapChanged += MapSystem_OnSceneChanged;
			ClientMapSystem.OnNotesChanged += MapSystem_OnNotesChanged;
		}

		/// <summary>
		/// Drops subscriptions and shuts the map subsystem down.
		/// </summary>
		public override void OnDestroying()
		{
			ClientMapSystem.OnSceneMapChanged -= MapSystem_OnSceneChanged;
			ClientMapSystem.OnNotesChanged -= MapSystem_OnNotesChanged;

			/* The panel outlives nothing here — the map subsystem is static and this is the panel
			 * that drives it, so tearing it down with the panel is what stops a camera and a
			 * quarter-megabyte fog grid surviving a scene change with nothing to update them. */
			ClientMapSystem.Shutdown();

			base.OnDestroying();
		}

		/// <summary>
		/// Points the map subsystem at the new character.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			ClientMapSystem.DetailsCache = WorldSceneDetails;
			ClientMapSystem.SetCharacter(Character);
			RefreshChrome();
		}

		/// <summary>
		/// Writes anything outstanding and clears the map.
		/// </summary>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			ClientMapSystem.SetCharacter(null);
			mapView?.ReleaseMarkers();
		}

		/// <summary>
		/// Flushes the explored map before the client returns to the login screen.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClientMapSystem.Shutdown();
			base.OnQuitToLogin();
		}

		/// <summary>
		/// Advances the map subsystem, whether or not the panel is on screen.
		/// </summary>
		/// <remarks>
		/// <see cref="UITKControl"/> drives this from its own <c>Update</c> for every control, so
		/// there is no second <c>Update</c> here to shadow the base one — a mistake this codebase
		/// has made before and documents at length on <c>UITKControl.Update</c>.
		/// </remarks>
		protected override void OnTick()
		{
			ClientMapSystem.Tick();
		}

		/// <summary>
		/// Follows the character, renders the overhead view, and refreshes the markers.
		/// </summary>
		/// <remarks>
		/// <c>LateUpdate</c> so the view is centred on the position the character actually finished
		/// the frame at, after movement and the camera rig have run. Following in <c>Update</c>
		/// lags by a frame, which shows up as the map sliding a little behind the player.
		/// </remarks>
		private void LateUpdate()
		{
			if (!Visible || mapView == null || Character == null || Character.Transform == null)
			{
				return;
			}

			EnsureRenderer();

			Vector3 center = Character.Transform.position;
			float rotation = ClientSettings.MinimapRotates && Character.MeshRoot != null
				? Character.MeshRoot.eulerAngles.y
				: 0.0f;

			MapViewTransform view = new MapViewTransform(center, ClampZoom(zoom), rotation);
			mapView.View = view;

			if (ClientMapSystem.Renderer.Render(view))
			{
				mapView.MapTexture = ClientMapSystem.Renderer.Texture;
				mapView.RefreshSurface();
			}

			mapView.Fog = ClientMapSystem.Fog;

			double now = Time.unscaledTimeAsDouble;
			if (now >= nextMarkerRefreshTime)
			{
				nextMarkerRefreshTime = now + MarkerRefreshInterval;
				RefreshMarkers();
				RefreshChrome();
			}
			else
			{
				/* The markers themselves are only collected ten times a second, but the view moves
				 * with the player every frame — so their screen positions have to be recomputed
				 * every frame or the icons sit still while the terrain slides underneath them. */
				mapView.RelayoutMarkers();
			}

			if (compass != null)
			{
				compass.style.rotate = new StyleRotate(new Rotate(-rotation));
			}
		}

		/// <summary>
		/// Configures the overhead renderer, and reconfigures it when the settings change.
		/// </summary>
		private void EnsureRenderer()
		{
			int detailTier = Cartography.DetailTier;

			if (!rendererConfigured || configuredDetailTier != detailTier)
			{
				ClientMapSystem.Renderer.Configure(MinimapCamera, AdditionalLayers, Cartography.MinimapResolution);
				rendererConfigured = true;
				configuredDetailTier = detailTier;
				mapView.MapTexture = ClientMapSystem.Renderer.Texture;
			}

			/* Read every frame rather than cached. It is one dictionary lookup against a store that
			 * is already in memory, and caching it means a player who changes the setting sees no
			 * effect until something else happens to reconfigure the renderer. */
			ClientMapSystem.Renderer.FramesPerSecond = ClientSettings.MinimapFrameRate;
		}

		/// <summary>
		/// Rebuilds the marker list from the registry and the character's notes.
		/// </summary>
		private void RefreshMarkers()
		{
			ClientMapSystem.Filter.Collect(markerBuffer, Character, false, ClientMapSystem.Fog);
			MapContent.AppendNotes(markerBuffer, ClientMapSystem.Notes, false);
			MapContent.AppendPointsOfInterest(markerBuffer, ClientMapSystem.Definition, ClientMapSystem.Fog, false);
			mapView.SetMarkers(markerBuffer);
		}

		/// <summary>
		/// Updates the region name and coordinate readouts.
		/// </summary>
		private void RefreshChrome()
		{
			if (Character == null || Character.Transform == null)
			{
				return;
			}

			Vector3 position = Character.Transform.position;

			if (locationLabel != null)
			{
				locationLabel.text = MapContent.ResolveLocationName(
					ClientMapSystem.Definition,
					ClientMapSystem.Fog,
					ClientMapSystem.SceneDisplayName,
					position);
			}

			if (coordinateLabel != null)
			{
				bool show = ClientSettings.MapShowCoordinates && Cartography.ShowsCoordinates;
				coordinateLabel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
				if (show)
				{
					coordinateLabel.text = $"{Mathf.RoundToInt(position.x)}, {Mathf.RoundToInt(position.z)}";
				}
			}
		}

		/// <summary>
		/// Zooms the minimap in response to the scroll wheel.
		/// </summary>
		/// <param name="delta">The wheel movement. Positive scrolls down, which zooms out.</param>
		private void MapView_OnScrolled(float delta)
		{
			ApplyZoom(delta > 0.0f ? zoom * ZoomStep : zoom / ZoomStep);
		}

		/// <summary>
		/// Applies and persists a new zoom.
		/// </summary>
		/// <param name="value">The requested half-extent in world metres.</param>
		private void ApplyZoom(float value)
		{
			float clamped = ClampZoom(value);
			if (Mathf.Approximately(clamped, zoom))
			{
				return;
			}

			zoom = clamped;
			ClientSettings.Set(ClientSettings.MinimapZoomKey, zoom);
		}

		/// <summary>
		/// Holds the zoom inside the range the current scene allows.
		/// </summary>
		/// <param name="value">The requested half-extent in world metres.</param>
		/// <returns>The clamped value.</returns>
		/// <remarks>
		/// The bounds come from the scene's map definition, so a cramped interior can forbid the
		/// wide view that suits an open zone. Applied here <i>and</i> in the renderer: this one is
		/// what the player experiences, the renderer's is what holds when something writes to the
		/// camera behind the panel's back.
		/// </remarks>
		private static float ClampZoom(float value)
		{
			WorldMapDefinition definition = ClientMapSystem.Definition;
			float minimum = definition != null ? definition.MinimapMinimumRange : 12.0f;
			float maximum = definition != null ? definition.MinimapMaximumRange : 60.0f;
			return Mathf.Clamp(value, minimum, maximum);
		}

		/// <summary>
		/// Re-reads everything derived from the scene when the character changes zone.
		/// </summary>
		private void MapSystem_OnSceneChanged()
		{
			zoom = ClampZoom(zoom);
			nextMarkerRefreshTime = 0.0;

			if (mapView != null)
			{
				mapView.Fog = ClientMapSystem.Fog;
				mapView.RefreshSurface();
			}

			RefreshChrome();
		}

		/// <summary>
		/// Redraws the markers when the player's notes change.
		/// </summary>
		private void MapSystem_OnNotesChanged()
		{
			nextMarkerRefreshTime = 0.0;
		}
	}
}
