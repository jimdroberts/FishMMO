using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The map itself: terrain image, fog of war, markers, and the chrome that sits over them.
	/// Used by both the minimap and the world map.
	/// </summary>
	/// <remarks>
	/// <para><b>One element, two panels.</b> The minimap and the world map differ in size, in
	/// where their image comes from and in what the player may do to them, and in nothing else.
	/// Everything below — the world-to-view mapping, the fog overlay, marker placement, edge
	/// clamping — is identical, and a second copy of it is how a note pinned on the world map ends
	/// up a few metres from where it shows on the minimap.</para>
	///
	/// <para><b>Four stacked layers, not one drawing.</b> Background, terrain, fog and markers are
	/// separate child elements rather than one <see cref="VisualElement.generateVisualContent"/>
	/// pass on this element, because generated content on a parent draws <i>behind</i> all of its
	/// children — so the terrain would have had to be a child anyway, and then the fog could not
	/// have been drawn over it. Layers also let each one choose how it draws: see below.</para>
	///
	/// <para><b>Why the terrain layer has two ways to draw and the fog only one.</b> The live
	/// minimap render texture is drawn as a plain <c>background-image</c>, because the camera has
	/// already rotated and framed exactly what should be on screen — there is no texture-coordinate
	/// work to do, and going through UI Toolkit's own path avoids the render-texture orientation
	/// difference between graphics APIs (a render target is stored bottom-up under OpenGL and
	/// top-down under D3D, Vulkan and Metal, so hand-written texture coordinates are upside down on
	/// half the platforms the client ships to). The baked world map and the fog are windows into a
	/// much larger image and need coordinates computed per corner, which a background image cannot
	/// express, so those are generated meshes — and both are ordinary <c>Texture2D</c>s, where the
	/// orientation question does not arise.</para>
	///
	/// <para><b>Markers are real elements, not drawn into the mesh.</b> They need labels,
	/// tooltips, hover states and clicks — everything UI Toolkit already does — and there are tens
	/// of them, not thousands. They are pooled rather than rebuilt, because a marker element
	/// recreated every refresh loses its hover state twice a second.</para>
	/// </remarks>
	public class UITKMapView : VisualElement
	{
		/// <summary>USS class on the root of the view.</summary>
		public const string ViewClass = "map-view-surface";

		/// <summary>USS class on the layer that markers live in.</summary>
		public const string MarkerLayerClass = "map-view__markers";

		/// <summary>USS class on each marker.</summary>
		public const string MarkerClass = "map-marker";

		/// <summary>USS class on a marker's label.</summary>
		public const string MarkerLabelClass = "map-marker__label";

		/// <summary>USS class on a marker's icon.</summary>
		public const string MarkerIconClass = "map-marker__icon";

		/// <summary>USS class added to a marker that has been pinned to the frame edge.</summary>
		public const string MarkerClampedClass = "map-marker--clamped";

		/// <summary>USS class prefix for the marker type modifier, completed with the type name.</summary>
		public const string MarkerTypeClassPrefix = "map-marker--";

		/// <summary>
		/// How far inside the frame a clamped marker is drawn, as a fraction of the view.
		/// </summary>
		/// <remarks>
		/// Not zero. A marker pinned exactly to the border is half outside it and gets cut in two
		/// by the frame's overflow clip, which reads as a rendering fault rather than as a
		/// direction indicator.
		/// </remarks>
		private const float ClampInset = 0.045f;

		/// <summary>
		/// How far the pointer may move between press and release and still count as a click.
		/// </summary>
		/// <remarks>
		/// A press on the world map starts a pan, and a click chooses where a note goes. Without a
		/// threshold every pan would also move the pending note, because the press that started
		/// the drag is the same press that ends it.
		/// </remarks>
		private const float ClickMovementTolerance = 4.0f;

		/// <summary>The layer the terrain image is drawn on.</summary>
		private readonly VisualElement imageLayer;

		/// <summary>The layer the fog is drawn on.</summary>
		private readonly VisualElement fogLayer;

		/// <summary>The layer markers are added to.</summary>
		private readonly VisualElement markerLayer;

		/// <summary>Marker elements currently in use, one per drawn snapshot.</summary>
		private readonly List<VisualElement> activeMarkers = new List<VisualElement>();

		/// <summary>Marker elements available for reuse.</summary>
		private readonly Stack<VisualElement> markerPool = new Stack<VisualElement>();

		/// <summary>The snapshots drawn on the last refresh.</summary>
		private readonly List<MapMarkerSnapshot> snapshots = new List<MapMarkerSnapshot>();

		/// <summary>Vertex position scratch, reused so a per-frame draw allocates nothing.</summary>
		private readonly Vector3[] cornerScratch = new Vector3[4];

		/// <summary>Texture coordinate scratch, reused alongside <see cref="cornerScratch"/>.</summary>
		private readonly Vector2[] uvScratch = new Vector2[4];

		/// <summary>Where the pointer went down, used to tell a click from a pan.</summary>
		private Vector2 pressPosition;

		/// <summary>The pointer that is currently pressed, or -1 when none is.</summary>
		private int pressPointerId = -1;

		/// <summary>
		/// The terrain image drawn under everything. A live render texture, or a baked map.
		/// </summary>
		public Texture MapTexture { get; set; }

		/// <summary>
		/// The world rectangle <see cref="MapTexture"/> covers.
		/// </summary>
		/// <remarks>
		/// Ignored when <see cref="MapTextureIsViewAligned"/> is set, because a live overhead
		/// render always covers exactly the view that produced it.
		/// </remarks>
		public Rect MapTextureRect { get; set; }

		/// <summary>
		/// Whether <see cref="MapTexture"/> is a live render of exactly this view.
		/// </summary>
		public bool MapTextureIsViewAligned { get; set; }

		/// <summary>Colour drawn behind the map image.</summary>
		public Color MapBackground { get; set; } = new Color(0.02f, 0.04f, 0.06f, 1.0f);

		/// <summary>Tint multiplied into the map image.</summary>
		public Color MapTint { get; set; } = Color.white;

		/// <summary>The explored map drawn over the terrain, or null for no fog.</summary>
		public FogOfWarMap Fog { get; set; }

		/// <summary>Colour of unexplored ground.</summary>
		public Color FogColor { get; set; } = new Color(0.01f, 0.02f, 0.03f, 0.94f);

		/// <summary>The window this view is showing.</summary>
		public MapViewTransform View { get; set; } = new MapViewTransform(Vector3.zero, 25.0f, 0.0f);

		/// <summary>
		/// Raised when the player clicks the map without dragging it, with the world position they
		/// clicked and the marker nearest to it, when one was close enough to count.
		/// </summary>
		public event Action<Vector3, MapMarkerSnapshot?> OnMapClicked;

		/// <summary>
		/// Raised when the player scrolls over the map, with the scroll delta.
		/// </summary>
		public event Action<float> OnMapScrolled;

		/// <summary>
		/// Builds an empty view.
		/// </summary>
		public UITKMapView()
		{
			AddToClassList(ViewClass);

			/* Clipped. Without it a marker near the edge draws outside the frame and over whatever
			 * panel is next to it, and the fog quad — sized to the element — spills the same way. */
			style.overflow = Overflow.Hidden;

			imageLayer = CreateLayer("map-image");
			imageLayer.generateVisualContent += OnGenerateImageContent;
			Add(imageLayer);

			fogLayer = CreateLayer("map-fog");
			fogLayer.generateVisualContent += OnGenerateFogContent;
			Add(fogLayer);

			markerLayer = CreateLayer("map-markers");
			markerLayer.AddToClassList(MarkerLayerClass);
			Add(markerLayer);

			RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
			RegisterCallback<PointerDownEvent>(OnPointerDown);
			RegisterCallback<PointerUpEvent>(OnPointerUp);
			RegisterCallback<WheelEvent>(OnWheel);
		}

		/// <summary>
		/// Builds one full-size, non-interactive layer.
		/// </summary>
		/// <param name="name">The element's name, for debugging in the UI Toolkit inspector.</param>
		/// <returns>The layer.</returns>
		/// <remarks>
		/// Every layer ignores the pointer so that presses reach this element, which is what
		/// interprets them as a click, a drag or a scroll. A layer that accepted the pointer would
		/// silently swallow all three.
		/// </remarks>
		private static VisualElement CreateLayer(string name)
		{
			VisualElement layer = new VisualElement()
			{
				name = name,
				pickingMode = PickingMode.Ignore,
			};
			layer.style.position = Position.Absolute;
			layer.style.left = 0;
			layer.style.top = 0;
			layer.style.right = 0;
			layer.style.bottom = 0;
			return layer;
		}

		/// <summary>
		/// Replaces what the view draws and lays it out again.
		/// </summary>
		/// <param name="markers">The markers to draw. Copied, not retained.</param>
		/// <remarks>
		/// Copied because the caller reuses its list every refresh; holding the caller's list would
		/// mean the view's contents changed underneath it between a refresh and the layout pass
		/// that acts on it.
		/// </remarks>
		public void SetMarkers(List<MapMarkerSnapshot> markers)
		{
			snapshots.Clear();
			if (markers != null)
			{
				snapshots.AddRange(markers);

				/* Sorted so that overlapping markers resolve the same way every frame. Without a
				 * total order the registry's enumeration order decides which of two markers in the
				 * same spot is on top, and that order changes whenever anything spawns — so a
				 * marker flickers between over and under its neighbour for no visible reason. */
				snapshots.Sort(CompareSnapshots);
			}

			LayoutMarkers();
		}

		/// <summary>
		/// Re-places the markers already collected, for the current <see cref="View"/>.
		/// </summary>
		/// <remarks>
		/// Split from <see cref="SetMarkers"/> because the two costs are wildly different and want
		/// wildly different rates. Collecting the markers walks the whole registry, resolves a
		/// relationship per marker and applies the visibility rules; placing them writes two style
		/// values per element. Collecting ten times a second is plenty — a creature crosses about
		/// two pixels of a minimap in that time — but the view itself moves every single frame with
		/// the player, so placing at the collection rate leaves every marker pinned to where the map
		/// used to be for a tenth of a second, which reads as the terrain sliding out from under the
		/// icons.
		/// </remarks>
		public void RelayoutMarkers()
		{
			LayoutMarkers();
		}

		/// <summary>
		/// Redraws the terrain and fog layers without touching the markers.
		/// </summary>
		/// <remarks>
		/// Called after every overhead render. A render texture assigned as a background image does
		/// not by itself mark anything dirty, so a minimap between marker refreshes would show the
		/// last frame UI Toolkit happened to repaint rather than the one just rendered.
		/// </remarks>
		public void RefreshSurface()
		{
			ApplyImageLayer();
			imageLayer.MarkDirtyRepaint();
			fogLayer.MarkDirtyRepaint();
		}

		/// <summary>
		/// Points the terrain layer at the current texture, in whichever way suits it.
		/// </summary>
		private void ApplyImageLayer()
		{
			imageLayer.style.backgroundColor = MapBackground;

			/* Tested against the concrete type, not just for null. The view-aligned path is only
			 * meaningful for a render texture, and Background.FromRenderTexture given anything
			 * else produces an empty background — a silently blank map rather than an error. */
			RenderTexture liveTexture = MapTextureIsViewAligned ? MapTexture as RenderTexture : null;

			if (liveTexture != null)
			{
				imageLayer.style.backgroundImage = Background.FromRenderTexture(liveTexture);
				imageLayer.style.unityBackgroundImageTintColor = MapTint;
			}
			else
			{
				imageLayer.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
				imageLayer.style.unityBackgroundImageTintColor = new StyleColor(StyleKeyword.Null);
			}
		}

		/// <summary>
		/// Orders markers so overlapping ones stack predictably.
		/// </summary>
		/// <param name="a">First snapshot.</param>
		/// <param name="b">Second snapshot.</param>
		/// <returns>Standard comparison result, lower drawing first.</returns>
		private static int CompareSnapshots(MapMarkerSnapshot a, MapMarkerSnapshot b)
		{
			if (a.Priority != b.Priority)
			{
				return a.Priority.CompareTo(b.Priority);
			}

			/* Reversed: the type enum runs from most important (Self) to least, and the most
			 * important marker must be drawn last so it ends up on top. */
			if (a.Type != b.Type)
			{
				return ((byte)b.Type).CompareTo((byte)a.Type);
			}

			return a.NoteID.CompareTo(b.NoteID);
		}

		/// <summary>
		/// Where a marker should actually be drawn this frame.
		/// </summary>
		/// <param name="snapshot">The collected snapshot.</param>
		/// <returns>The live transform position for a tracking marker, the collected one otherwise.</returns>
		/// <remarks>
		/// <para>The single place a marker's position is decided, so the mesh, the layout and the
		/// click test cannot disagree about where a marker is — a click test reading a different
		/// position from the one drawn means markers that cannot be clicked where they appear.</para>
		/// <para>The null test is a Unity object comparison and costs an engine call, which is why
		/// the flag is checked first: notes and landmarks have no source at all and skip it, and a
		/// destroyed marker falls back to the last position the filter published rather than
		/// throwing. It disappears on the next collection a tenth of a second later.</para>
		/// </remarks>
		private static Vector3 ResolvePosition(in MapMarkerSnapshot snapshot)
		{
			if (snapshot.TracksSource && snapshot.Source != null)
			{
				return snapshot.Source.Position;
			}
			return snapshot.Position;
		}

		/// <summary>
		/// Which way a marker's icon should point this frame.
		/// </summary>
		/// <param name="snapshot">The collected snapshot.</param>
		/// <returns>The live heading for a tracking marker, the collected one otherwise.</returns>
		private static float ResolveFacing(in MapMarkerSnapshot snapshot)
		{
			if (snapshot.TracksSource && snapshot.Source != null)
			{
				return snapshot.Source.FacingDegrees;
			}
			return snapshot.FacingDegrees;
		}

		/// <summary>
		/// Converts a world position into a point inside this element.
		/// </summary>
		/// <param name="worldPosition">The world position.</param>
		/// <returns>The point in the element's own coordinate space.</returns>
		public Vector2 WorldToLocal(Vector3 worldPosition)
		{
			Rect content = contentRect;
			Vector2 view = View.WorldToView(worldPosition);

			// Y is flipped: view coordinates run up, UI Toolkit lays out down.
			return new Vector2(view.x * content.width, (1.0f - view.y) * content.height);
		}

		/// <summary>
		/// Converts a point inside this element into a world position.
		/// </summary>
		/// <param name="localPosition">The point in the element's own coordinate space.</param>
		/// <returns>The world position on the XZ plane.</returns>
		public Vector3 LocalToWorld(Vector2 localPosition)
		{
			Rect content = contentRect;
			if (content.width <= 0.0f || content.height <= 0.0f)
			{
				return View.Center;
			}

			Vector2 view = new Vector2(localPosition.x / content.width,
									   1.0f - (localPosition.y / content.height));
			return View.ViewToWorld(view);
		}

		/// <summary>
		/// Places every marker element for the current view.
		/// </summary>
		private void LayoutMarkers()
		{
			Rect content = contentRect;
			if (content.width <= 0.0f || content.height <= 0.0f)
			{
				/* No layout yet. Bailing here rather than dividing by zero: the geometry callback
				 * runs this again the moment the element has a size, so nothing is lost. */
				return;
			}

			EnsureMarkerCount(snapshots.Count);

			for (int i = 0; i < snapshots.Count; ++i)
			{
				MapMarkerSnapshot snapshot = snapshots[i];
				VisualElement element = activeMarkers[i];
				MarkerElements parts = (MarkerElements)element.userData;

				/* Measured before anything below writes to the element, so that the geometry this
				 * reads is the previous frame's styles settled, and not half of two frames. */
				MeasureDrawnBounds(element, parts);

				Vector3 position = ResolvePosition(in snapshot);
				Vector2 view = View.WorldToView(position);
				bool outside = view.x < 0.0f || view.x > 1.0f || view.y < 0.0f || view.y > 1.0f;

				/* Culled by the rectangle the marker draws, not by where its centre stands. Half a
				 * marker is still on the frame when its centre crosses the border, and hiding it
				 * there cuts a half-drawn icon out of the picture (issue #271); what actually leaves
				 * the frame is the whole rectangle — icon and the name hanging off its right — and
				 * that is the thing to ask.
				 *
				 * An element that has not been laid out yet has no rectangle to be judged by, so it
				 * is given this frame to produce one. That is once per element rather than once per
				 * frame: after it has been drawn once, the measurement is kept. */
				if (parts.DrawnBoundsMeasured && !snapshot.ClampToEdge &&
					!OverlapsFrame(view, in parts.DrawnBounds, content))
				{
					element.style.display = DisplayStyle.None;
					continue;
				}

				/* Pinned to the frame edge only when the marker is one that asked for it and its
				 * object is off the view. A marker that is still being drawn at the edge is drawn
				 * where it is: pinning it inwards would move it away from the thing it marks, to a
				 * place it is already sitting on top of. The pin is for a marker with nothing left on
				 * the frame, which is why it is decided here rather than by `outside` alone. */
				bool clamped = outside && snapshot.ClampToEdge;
				if (clamped)
				{
					view = ClampToFrame(view);
				}

				element.style.display = DisplayStyle.Flex;
				element.EnableInClassList(MarkerClampedClass, clamped);
				element.style.left = view.x * content.width;
				element.style.top = (1.0f - view.y) * content.height;

				ApplyMarkerVisuals(element, snapshot, position, clamped);
			}

			// Anything left over from a busier frame is hidden rather than destroyed.
			for (int i = snapshots.Count; i < activeMarkers.Count; ++i)
			{
				activeMarkers[i].style.display = DisplayStyle.None;
			}
		}

		/// <summary>
		/// Records the rectangle a marker is drawn over, as offsets from the point its position maps
		/// to.
		/// </summary>
		/// <param name="element">The marker element.</param>
		/// <param name="parts">Its cached pieces, which the measurement is written to.</param>
		/// <remarks>
		/// <para>Kept rather than taken fresh each frame, because a culled marker is hidden with
		/// <c>display: none</c> and a hidden element has no geometry left to read — so a marker that
		/// leaves the frame would lose the only description of itself it had, and a marker whose name
		/// reached back onto the frame would then stay hidden for good.</para>
		/// <para>Offsets relative to the position rather than a rectangle in pixels, because the
		/// measurement is taken where the marker was and used where it is: the map moves under its
		/// markers every frame, and the rectangle moves with them.</para>
		/// </remarks>
		private void MeasureDrawnBounds(VisualElement element, MarkerElements parts)
		{
			/* A clamped marker is drawn shrunk, turned to point out of the frame, and without its
			 * name — none of which is the shape the marker has when it stands where it belongs, and
			 * all of which would be measured here. Skipped rather than invalidated: the rectangle is
			 * kept as offsets from the marker's position, so the last measurement taken of the marker
			 * itself is still the measurement of this marker wherever it has since moved to. */
			if (element.ClassListContains(MarkerClampedClass))
			{
				return;
			}

			if (!TryGetDrawnBounds(element, out Rect bounds))
			{
				return;
			}

			/* The marker's box is centred on its position by the -50%/-50% translate, so its icon's
			 * centre is that position — including for a clamped marker, which is scaled and rotated
			 * about the same point. */
			VisualElement self = this;
			Vector2 anchor = self.WorldToLocal(parts.Icon.worldBound.center);

			parts.DrawnBounds = new Rect(bounds.position - anchor, bounds.size);
			parts.DrawnBoundsMeasured = true;
		}

		/// <summary>
		/// The rectangle a marker is drawn over, in this element's coordinates.
		/// </summary>
		/// <param name="marker">The drawn element for one snapshot.</param>
		/// <param name="rect">The rectangle the marker and everything it draws cover.</param>
		/// <returns>False when there is no geometry: the marker is hidden, or not laid out yet.</returns>
		/// <remarks>
		/// Read from <c>worldBound</c> rather than <c>layout</c>, because that is the rectangle the
		/// player sees: the icon and the label are both moved off their layout boxes on purpose, and
		/// the layout boxes answer for rectangles that are not on screen.
		/// </remarks>
		private bool TryGetDrawnBounds(VisualElement marker, out Rect rect)
		{
			rect = default;

			if (marker == null || marker.resolvedStyle.display == DisplayStyle.None)
			{
				return false;
			}

			Rect bound = marker.worldBound;
			float xMin = bound.xMin;
			float yMin = bound.yMin;
			float xMax = bound.xMax;
			float yMax = bound.yMax;

			/* The marker's own rectangle, plus whatever it draws. A marker's label is laid out of
			 * flow — see .map-marker__label — so it lies outside the marker's own rectangle and has
			 * to be taken in separately, or a click on a waypoint's name would fall through to the
			 * ground beneath it and drop a note pin, and a marker would be culled while its own name
			 * was still on the frame. A label that is turned off is not there to be clicked, and is
			 * skipped rather than counted at wherever it was last drawn. */
			for (int i = 0; i < marker.childCount; ++i)
			{
				VisualElement child = marker[i];
				if (child.resolvedStyle.display == DisplayStyle.None)
				{
					continue;
				}

				Rect childBound = child.worldBound;
				xMin = Mathf.Min(xMin, childBound.xMin);
				yMin = Mathf.Min(yMin, childBound.yMin);
				xMax = Mathf.Max(xMax, childBound.xMax);
				yMax = Mathf.Max(yMax, childBound.yMax);
			}

			/* Asked of the base class on purpose. This element declares a WorldToLocal of its own
			 * that takes a world position and answers in map coordinates, and the inherited one —
			 * which is what converts a panel point into a point here — has to be named by type to be
			 * reached. */
			VisualElement element = this;

			Vector2 min = element.WorldToLocal(new Vector2(xMin, yMin));
			Vector2 max = element.WorldToLocal(new Vector2(xMax, yMax));

			rect = new Rect(min, max - min);

			/* Both zero means nothing has been laid out: an element that has just been created, or
			 * one whose styles arrived a frame ago and whose box the panel has not sized yet. Such a
			 * rectangle is not a measurement of anything, and returning it as one would have every
			 * freshly pooled marker culled by the geometry of an empty box. */
			return rect.width > 0.0f || rect.height > 0.0f;
		}

		/// <summary>
		/// Whether any part of the rectangle a marker draws is inside the frame.
		/// </summary>
		/// <param name="view">The marker's position, in view coordinates.</param>
		/// <param name="drawn">The rectangle it draws, as offsets from that position.</param>
		/// <param name="content">The frame, in points.</param>
		/// <returns>True while any of the marker is still on the frame.</returns>
		/// <remarks>
		/// The rectangle is measured outside this element's clip, so a marker is not culled at the
		/// edge of its own drawing but at the edge of the frame: an icon half over the border is half
		/// an icon the player can see, and it stays until the last of it is gone.
		/// </remarks>
		private static bool OverlapsFrame(Vector2 view, in Rect drawn, Rect content)
		{
			float anchorX = view.x * content.width;
			float anchorY = (1.0f - view.y) * content.height;

			return anchorX + drawn.xMax > 0.0f
				&& anchorX + drawn.xMin < content.width
				&& anchorY + drawn.yMax > 0.0f
				&& anchorY + drawn.yMin < content.height;
		}

		/// <summary>
		/// Moves an off-view point onto the frame's border.
		/// </summary>
		/// <param name="view">The out-of-range view coordinates.</param>
		/// <returns>View coordinates on the border, in the same direction from the centre.</returns>
		/// <remarks>
		/// Projected along the line from the centre rather than clamped per axis. Clamping each
		/// axis independently puts everything beyond a corner into that corner, so three markers
		/// in quite different directions pile up in the same place and the indicator stops
		/// indicating anything.
		/// </remarks>
		private static Vector2 ClampToFrame(Vector2 view)
		{
			Vector2 fromCenter = view - new Vector2(0.5f, 0.5f);
			float extent = 0.5f - ClampInset;

			float longest = Mathf.Max(Mathf.Abs(fromCenter.x), Mathf.Abs(fromCenter.y));
			if (longest <= 0.0001f)
			{
				return view;
			}

			fromCenter *= extent / longest;
			return fromCenter + new Vector2(0.5f, 0.5f);
		}

		/// <summary>
		/// Writes a snapshot's appearance onto its element.
		/// </summary>
		/// <param name="element">The marker element.</param>
		/// <param name="snapshot">What to show.</param>
		/// <param name="position">The world position the marker was placed at this frame.</param>
		/// <param name="clamped">Whether the marker was pinned to the frame edge.</param>
		private void ApplyMarkerVisuals(VisualElement element, MapMarkerSnapshot snapshot, Vector3 position, bool clamped)
		{
			MarkerElements parts = (MarkerElements)element.userData;

			string typeClass = MarkerTypeClassPrefix + snapshot.Type.ToString().ToLowerInvariant();
			if (!string.Equals(parts.TypeClass, typeClass, StringComparison.Ordinal))
			{
				if (parts.TypeClass != null)
				{
					element.RemoveFromClassList(parts.TypeClass);
				}
				element.AddToClassList(typeClass);
				parts.TypeClass = typeClass;
			}

			parts.Icon.style.width = snapshot.Size;
			parts.Icon.style.height = snapshot.Size;
			parts.Icon.style.backgroundImage = snapshot.Icon != null
				? new StyleBackground(snapshot.Icon)
				: new StyleBackground(StyleKeyword.Null);
			parts.Icon.style.unityBackgroundImageTintColor = snapshot.Tint;

			/* A clamped marker points at where its object actually is; an unclamped one shows the
			 * object's heading. Rotating a clamped marker by the object's heading instead would
			 * make the edge indicator point wherever that creature happened to be facing. */
			if (clamped)
			{
				Vector2 direction = View.WorldToView(position) - new Vector2(0.5f, 0.5f);
				parts.Icon.style.rotate = new StyleRotate(
					new Rotate(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg));
			}
			else if (snapshot.HasFacing)
			{
				parts.Icon.style.rotate = new StyleRotate(
					new Rotate(View.WorldToViewAngle(ResolveFacing(in snapshot))));
			}
			else
			{
				/* Cleared rather than written as zero degrees, so that a rotation the icon wears from
				 * its own rule survives. A discovered waypoint is a diamond because of one — the only
				 * shape no other marker uses, and the reason it is recognisable as the one marker
				 * that is pressed rather than read. An inline 0deg over the top of it drew every
				 * waypoint as a square, indistinguishable in shape from the notes and landmarks
				 * beside it. */
				parts.Icon.style.rotate = StyleKeyword.Null;
			}

			bool hasLabel = !clamped && !string.IsNullOrEmpty(snapshot.Label);
			parts.Label.style.display = hasLabel ? DisplayStyle.Flex : DisplayStyle.None;
			if (hasLabel)
			{
				parts.Label.text = snapshot.Label;
			}

			element.tooltip = snapshot.Tooltip ?? string.Empty;
		}

		/// <summary>
		/// Grows the pool of marker elements to at least a given size.
		/// </summary>
		/// <param name="count">How many markers are needed.</param>
		private void EnsureMarkerCount(int count)
		{
			while (activeMarkers.Count < count)
			{
				VisualElement element = markerPool.Count > 0 ? markerPool.Pop() : CreateMarkerElement();

				/* A pooled element still wears the last marker it drew, name and all, and its
				 * measurement describes that one. Thrown away rather than trusted: the element then
				 * spends a frame drawn to be measured again, which is what a new element spends
				 * anyway, and no marker is ever culled by another marker's name. */
				((MarkerElements)element.userData).DrawnBoundsMeasured = false;

				activeMarkers.Add(element);
				markerLayer.Add(element);
			}
		}

		/// <summary>The pieces of one marker element, cached so they are not queried per frame.</summary>
		private sealed class MarkerElements
		{
			/// <summary>The icon, which carries the sprite and the rotation.</summary>
			public VisualElement Icon;

			/// <summary>The label drawn beside the icon.</summary>
			public Label Label;

			/// <summary>The type modifier class currently applied, so it can be swapped cleanly.</summary>
			public string TypeClass;

			/// <summary>
			/// The rectangle the marker draws — its icon and the name beside it — as offsets from the
			/// point its position maps to. See <see cref="MeasureDrawnBounds"/>.
			/// </summary>
			public Rect DrawnBounds;

			/// <summary>
			/// Whether <see cref="DrawnBounds"/> describes anything yet. False until the element has
			/// been laid out once, and again after it is taken back out of the pool.
			/// </summary>
			public bool DrawnBoundsMeasured;
		}

		/// <summary>
		/// Builds one marker element.
		/// </summary>
		/// <returns>The new element, with its parts recorded in <c>userData</c>.</returns>
		private static VisualElement CreateMarkerElement()
		{
			VisualElement root = new VisualElement()
			{
				pickingMode = PickingMode.Ignore,
			};
			root.AddToClassList(MarkerClass);
			root.style.position = Position.Absolute;

			/* Translated by minus half its own size so that left/top address the marker's centre.
			 * Without it every marker sits down and to the right of the thing it marks by half an
			 * icon, which at sixteen points is enough to put a gathering node in the wrong bush.
			 *
			 * "Its own size" is the icon's, and the label — which is laid out of flow, see
			 * .map-marker__label — is what keeps it that way. A label in the row would make this
			 * marker's size the icon's plus the label's, and the same -50% would then slide a
			 * labelled marker off its position by half the width of its own name: the icon drawn
			 * tens of points from the place it stands for, and a click on it selecting nothing
			 * (issue #270). */
			root.style.translate = new StyleTranslate(new Translate(Length.Percent(-50.0f), Length.Percent(-50.0f)));

			VisualElement icon = new VisualElement()
			{
				pickingMode = PickingMode.Ignore,
			};
			icon.AddToClassList(MarkerIconClass);
			root.Add(icon);

			Label label = new Label()
			{
				pickingMode = PickingMode.Ignore,
			};
			label.AddToClassList(MarkerLabelClass);
			root.Add(label);

			root.userData = new MarkerElements()
			{
				Icon = icon,
				Label = label,
			};

			return root;
		}

		/// <summary>
		/// Re-places the markers and redraws whenever the element is resized.
		/// </summary>
		/// <param name="evt">The geometry change.</param>
		private void OnGeometryChanged(GeometryChangedEvent evt)
		{
			LayoutMarkers();
			RefreshSurface();
		}

		/// <summary>
		/// Records where a press landed, so the release can be told from a drag.
		/// </summary>
		/// <param name="evt">The pointer press.</param>
		private void OnPointerDown(PointerDownEvent evt)
		{
			/* Primary button only, matching the pan. A right-press that armed the click would be
			 * completed by whichever button happened to be released next, so opening a context menu
			 * over the map would also drop a note pin. */
			if (evt.button != 0)
			{
				return;
			}

			pressPointerId = evt.pointerId;
			pressPosition = evt.localPosition;
		}

		/// <summary>
		/// Reports a click, if the pointer did not travel far enough to have been a drag.
		/// </summary>
		/// <param name="evt">The pointer release.</param>
		private void OnPointerUp(PointerUpEvent evt)
		{
			if (evt.button != 0 || evt.pointerId != pressPointerId)
			{
				return;
			}
			pressPointerId = -1;

			if (OnMapClicked == null)
			{
				return;
			}

			Vector2 local = evt.localPosition;
			if (Vector2.Distance(local, pressPosition) > ClickMovementTolerance)
			{
				// That was a pan, not a click.
				return;
			}

			OnMapClicked.Invoke(LocalToWorld(local), FindNearestSnapshot(local));
		}

		/// <summary>
		/// Reports a scroll over the map.
		/// </summary>
		/// <param name="evt">The wheel movement.</param>
		private void OnWheel(WheelEvent evt)
		{
			if (OnMapScrolled == null)
			{
				return;
			}

			OnMapScrolled.Invoke(evt.delta.y);
			evt.StopPropagation();
		}

		/// <summary>
		/// The drawn marker nearest a point, when one is close enough to count as clicked.
		/// </summary>
		/// <param name="localPosition">The point in the element's own coordinate space.</param>
		/// <returns>The nearest marker, or null when none is within its own icon of the point.</returns>
		/// <remarks>
		/// <para>Answered by distance rather than by hit-testing the elements, because the marker
		/// elements are all <c>PickingMode.Ignore</c> — they have to be, or a marker under the
		/// pointer would swallow the press the map itself needs in order to pan or place a note.</para>
		/// <para>Two things count as being on a marker: a circle around its position, wide enough to
		/// forgive a small icon and an unsteady hand, and the rectangle it is actually drawn over.
		/// The second is what makes a marker's name part of the marker — a player aiming at a
		/// waypoint's label is aiming at the waypoint, and the label hangs off the icon's right, well
		/// outside any radius that would not also swallow everything near it.</para>
		/// </remarks>
		private MapMarkerSnapshot? FindNearestSnapshot(Vector2 localPosition)
		{
			float bestDistance = float.MaxValue;
			int bestIndex = -1;

			for (int i = 0; i < snapshots.Count; ++i)
			{
				/* Copied out of the list rather than passed by reference: a List indexer returns a
				 * value, not a ref, so it cannot bind to an `in` parameter. */
				MapMarkerSnapshot snapshot = snapshots[i];

				Vector2 point = WorldToLocal(ResolvePosition(in snapshot));
				float distance = Vector2.Distance(point, localPosition);
				float radius = Mathf.Max(8.0f, snapshot.Size);

				/* Guarded rather than assumed: the markers are placed by a layout pass, and a click
				 * can arrive between a marker joining the list and being given an element. */
				if (i < activeMarkers.Count)
				{
					distance = Mathf.Min(distance, DistanceToDrawnMarker(activeMarkers[i], localPosition));
				}

				/* `<=` so that overlapping markers resolve to the one drawn on top. The list is
				 * sorted lowest-priority-first, and a tie is the common case for two rectangles that
				 * both contain the point. */
				if (distance < radius && distance <= bestDistance)
				{
					bestDistance = distance;
					bestIndex = i;
				}
			}

			return bestIndex >= 0 ? snapshots[bestIndex] : (MapMarkerSnapshot?)null;
		}

		/// <summary>
		/// How far a point is outside the rectangle a marker is drawn over.
		/// </summary>
		/// <param name="marker">The drawn element for one snapshot.</param>
		/// <param name="localPosition">A point in this element's coordinates.</param>
		/// <returns>Zero anywhere inside the marker, the distance to its nearest edge outside it.</returns>
		private float DistanceToDrawnMarker(VisualElement marker, Vector2 localPosition)
		{
			/* The same rectangle the cull is decided by, so that what is clickable and what is culled
			 * cannot come apart: a marker the frame no longer draws answers as far away as possible,
			 * and one it draws — name included — answers for every point of it. */
			if (!TryGetDrawnBounds(marker, out Rect rect))
			{
				return float.MaxValue;
			}

			float dx = Mathf.Max(Mathf.Max(rect.xMin - localPosition.x, 0.0f), localPosition.x - rect.xMax);
			float dy = Mathf.Max(Mathf.Max(rect.yMin - localPosition.y, 0.0f), localPosition.y - rect.yMax);
			return Mathf.Sqrt(dx * dx + dy * dy);
		}

		/// <summary>
		/// Draws the baked terrain image, for the world map.
		/// </summary>
		/// <param name="context">The mesh generation context.</param>
		/// <remarks>
		/// Does nothing for the live minimap: that texture is drawn as this layer's background
		/// image instead, and drawing it twice would put a hand-oriented copy over the correct one.
		/// </remarks>
		private void OnGenerateImageContent(MeshGenerationContext context)
		{
			if (MapTextureIsViewAligned || MapTexture == null)
			{
				return;
			}

			DrawWindowedQuad(context, imageLayer.contentRect, MapTexture, MapTint, MapTextureRect);
		}

		/// <summary>
		/// Draws the fog of war.
		/// </summary>
		/// <param name="context">The mesh generation context.</param>
		private void OnGenerateFogContent(MeshGenerationContext context)
		{
			if (Fog == null || FogColor.a <= 0.0f)
			{
				return;
			}

			/* GridRect, not WorldRect. The chunk grid rounds up to whole chunks and so covers more
			 * ground than the scene's rectangle; sampling it as though it covered exactly the
			 * rectangle stretches the fog by the overhang and walks it out of alignment with the
			 * terrain underneath. See FogOfWarMap.GridRect. */
			DrawWindowedQuad(context, fogLayer.contentRect, Fog.GetTexture(), FogColor, Fog.GridRect);
		}

		/// <summary>
		/// Emits one quad filling a layer, sampling a texture that covers a world rectangle.
		/// </summary>
		/// <param name="context">The mesh generation context.</param>
		/// <param name="content">The layer's content rectangle.</param>
		/// <param name="texture">The texture to sample.</param>
		/// <param name="tint">Colour multiplied into the result.</param>
		/// <param name="worldRect">The world rectangle the texture covers.</param>
		private void DrawWindowedQuad(MeshGenerationContext context, Rect content, Texture texture,
			Color tint, Rect worldRect)
		{
			if (content.width <= 0.0f || content.height <= 0.0f || texture == null)
			{
				return;
			}

			MeshWriteData mesh = context.Allocate(4, 6, texture);

			cornerScratch[0] = new Vector3(content.xMin, content.yMin, Vertex.nearZ);
			cornerScratch[1] = new Vector3(content.xMax, content.yMin, Vertex.nearZ);
			cornerScratch[2] = new Vector3(content.xMax, content.yMax, Vertex.nearZ);
			cornerScratch[3] = new Vector3(content.xMin, content.yMax, Vertex.nearZ);

			/* Each corner is converted through the view and then normalised into the texture's own
			 * world rectangle. Doing it per corner rather than computing one rectangle is what
			 * makes a rotated view work: the four points describe a rotated square in texture
			 * space, and UI Toolkit interpolates between them affinely, which is exactly right for
			 * a rotation. Note the Y flip — UI Toolkit's top-left corner is the view's (0, 1). */
			uvScratch[0] = WorldToTextureUV(worldRect, View.ViewToWorld(new Vector2(0.0f, 1.0f)));
			uvScratch[1] = WorldToTextureUV(worldRect, View.ViewToWorld(new Vector2(1.0f, 1.0f)));
			uvScratch[2] = WorldToTextureUV(worldRect, View.ViewToWorld(new Vector2(1.0f, 0.0f)));
			uvScratch[3] = WorldToTextureUV(worldRect, View.ViewToWorld(new Vector2(0.0f, 0.0f)));

			Color32 color = tint;
			for (int i = 0; i < 4; ++i)
			{
				Vector2 uv = uvScratch[i];

				/* Remapped through the write data's UV region. UI Toolkit may have placed the
				 * texture inside a dynamic atlas, in which case 0..1 addresses the whole atlas
				 * rather than this texture — sampling without the remap draws some other panel's
				 * artwork, and only for the textures that happened to get atlased. */
				uv = new Vector2(mesh.uvRegion.xMin + (uv.x * mesh.uvRegion.width),
								 mesh.uvRegion.yMin + (uv.y * mesh.uvRegion.height));

				mesh.SetNextVertex(new Vertex()
				{
					position = cornerScratch[i],
					tint = color,
					uv = uv,
				});
			}

			mesh.SetNextIndex(0);
			mesh.SetNextIndex(1);
			mesh.SetNextIndex(2);
			mesh.SetNextIndex(0);
			mesh.SetNextIndex(2);
			mesh.SetNextIndex(3);
		}

		/// <summary>
		/// Normalises a world position into a texture's coordinate space.
		/// </summary>
		/// <param name="worldRect">The world rectangle the texture covers.</param>
		/// <param name="worldPosition">The world position.</param>
		/// <returns>Texture coordinates, not clamped.</returns>
		private static Vector2 WorldToTextureUV(Rect worldRect, Vector3 worldPosition)
		{
			if (worldRect.width <= 0.0f || worldRect.height <= 0.0f)
			{
				return new Vector2(0.5f, 0.5f);
			}

			return new Vector2((worldPosition.x - worldRect.xMin) / worldRect.width,
							   (worldPosition.z - worldRect.yMin) / worldRect.height);
		}

		/// <summary>
		/// Releases the marker elements back to the pool.
		/// </summary>
		/// <remarks>
		/// Called when a panel's visual tree is rebuilt. UI Toolkit hands a document a fresh root
		/// on every enable, so a view holding elements from the previous tree would keep adding
		/// them to a parent nobody draws.
		/// </remarks>
		public void ReleaseMarkers()
		{
			for (int i = 0; i < activeMarkers.Count; ++i)
			{
				VisualElement element = activeMarkers[i];
				element.RemoveFromHierarchy();
				markerPool.Push(element);
			}
			activeMarkers.Clear();
			snapshots.Clear();
		}
	}
}
