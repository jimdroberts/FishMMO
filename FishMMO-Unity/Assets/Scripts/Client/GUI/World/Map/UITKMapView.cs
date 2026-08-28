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

				Vector2 view = View.WorldToView(snapshot.Position);
				bool outside = view.x < 0.0f || view.x > 1.0f || view.y < 0.0f || view.y > 1.0f;

				if (outside && !snapshot.ClampToEdge)
				{
					element.style.display = DisplayStyle.None;
					continue;
				}

				bool clamped = false;
				if (outside)
				{
					view = ClampToFrame(view);
					clamped = true;
				}

				element.style.display = DisplayStyle.Flex;
				element.EnableInClassList(MarkerClampedClass, clamped);
				element.style.left = view.x * content.width;
				element.style.top = (1.0f - view.y) * content.height;

				ApplyMarkerVisuals(element, snapshot, clamped);
			}

			// Anything left over from a busier frame is hidden rather than destroyed.
			for (int i = snapshots.Count; i < activeMarkers.Count; ++i)
			{
				activeMarkers[i].style.display = DisplayStyle.None;
			}
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
		/// <param name="clamped">Whether the marker was pinned to the frame edge.</param>
		private void ApplyMarkerVisuals(VisualElement element, MapMarkerSnapshot snapshot, bool clamped)
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
			float rotation;
			if (clamped)
			{
				Vector2 direction = View.WorldToView(snapshot.Position) - new Vector2(0.5f, 0.5f);
				rotation = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
			}
			else
			{
				rotation = snapshot.HasFacing ? View.WorldToViewAngle(snapshot.FacingDegrees) : 0.0f;
			}
			parts.Icon.style.rotate = new StyleRotate(new Rotate(rotation));

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
			 * icon, which at sixteen points is enough to put a gathering node in the wrong bush. */
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
		/// Answered by distance rather than by hit-testing the elements, because the marker
		/// elements are all <c>PickingMode.Ignore</c> — they have to be, or a marker under the
		/// pointer would swallow the press the map itself needs in order to pan or place a note.
		/// </remarks>
		private MapMarkerSnapshot? FindNearestSnapshot(Vector2 localPosition)
		{
			float bestDistance = float.MaxValue;
			int bestIndex = -1;

			for (int i = 0; i < snapshots.Count; ++i)
			{
				Vector2 point = WorldToLocal(snapshots[i].Position);
				float distance = Vector2.Distance(point, localPosition);
				float radius = Mathf.Max(8.0f, snapshots[i].Size);

				if (distance < radius && distance < bestDistance)
				{
					bestDistance = distance;
					bestIndex = i;
				}
			}

			return bestIndex >= 0 ? snapshots[bestIndex] : (MapMarkerSnapshot?)null;
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

			DrawWindowedQuad(context, fogLayer.contentRect, Fog.GetTexture(), FogColor, Fog.WorldRect);
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
