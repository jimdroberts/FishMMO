#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>A scene as the globe draws it.</summary>
	public sealed class GlobeScene
	{
		public WorldAtlasScene Entry;
		public AtlasFootprint Footprint;
		public Texture2D Preview;
		/// <summary>Where the preview sits in the world, so a map covering a different rectangle still lines up.</summary>
		public AtlasImage Image;
		/// <summary>The world XZ rectangle (metres) the footprint stands for; empty maps the preview edge to edge.</summary>
		public Rect WorldRect;
		public Color Tint = new Color(0.55f, 0.75f, 0.95f, 1f);
		/// <summary>Drawn as a faint outline only (another layer).</summary>
		public bool Ghost;
		/// <summary>Red outline: overlaps, too big, or broken.</summary>
		public bool Problem;
		public int Warnings;
		public bool HasExternalLinks;
		public string Caption;
	}

	/// <summary>A connection line on the globe.</summary>
	public sealed class GlobeRoute
	{
		/// <summary>Unit vectors along the line.</summary>
		public readonly List<Vector3d> Points = new List<Vector3d>();
		public bool TwoWay;
		public bool Clean = true;
		public bool Highlight;
		/// <summary>Links scenes in different layers: drawn as a dotted arc lifted off the surface.</summary>
		public bool CrossLayer;
		/// <summary>Faint: neither end is in the layer being viewed.</summary>
		public bool Ghost;
		/// <summary>Where the teleporter is (a dot is drawn there), if known.</summary>
		public bool MarkStart;
		public Color Colour = new Color(0.95f, 0.85f, 0.45f, 1f);
	}

	/// <summary>
	/// A real 3D globe for the world atlas, drawn with UI Toolkit meshes: the body with its
	/// day/night side and time zones, each scene as a patch of its preview at its true size,
	/// connection lines, and labels. It never renders unless something changed.
	/// </summary>
	/// <remarks>
	/// Frame: +Y north, longitude 0 toward +Z (see <see cref="AtlasGeometry"/>). The view is
	/// orthographic; a point faces the viewer when its rotated Z is positive. Picking is an
	/// analytic ray–sphere test, so it does not depend on physics scenes.
	/// </remarks>
	public class GlobeView : VisualElement
	{
		private const int SphereRows = 36;
		private const int SphereColumns = 72;
		private const int PatchSteps = 10;

		public readonly List<GlobeScene> Scenes = new List<GlobeScene>();
		public readonly List<GlobeRoute> Routes = new List<GlobeRoute>();

		public double RadiusKm = 30.0;
		public Color BodyColour = new Color(0.3f, 0.45f, 0.6f, 1f);

		/// <summary>
		/// The body's baked surface, drawn on the globe when there is one.
		/// </summary>
		/// <remarks>
		/// Null is the normal state and not a fault: the bake is build output, so a freshly cloned
		/// project has none and the globe shows a plain ball in <see cref="BodyColour"/>. Baking
		/// puts the real coastlines under the scene rectangles, which is what makes picking a
		/// location for a new scene a decision rather than a guess.
		/// </remarks>
		public Texture2D Surface;

		/// <summary>
		/// Draw the lines a developer places scenes against: the equator, the prime meridian, the
		/// tropics and the polar circles.
		/// </summary>
		public bool ShowReference = true;

		/// <summary>
		/// The body's axial tilt, which is what puts the tropics and the polar circles where they are.
		/// </summary>
		/// <remarks>
		/// Not decoration. The tilt decides the sun's noon altitude at every latitude, which is the
		/// only thing making a pole cold in this model — so the tropic is literally the furthest the
		/// sun ever gets overhead, and the polar circle is where it stops rising at midwinter. A
		/// scene placed between them is temperate for a reason that is visible on the globe.
		/// </remarks>
		public float AxialTiltDegrees = 23.4f;
		/// <summary>Unit vector toward the sun in the body's frame, or null for no daylight shading.</summary>
		public Vector3d? SunDirection;
		public bool ShowGrid = true;
		public bool ShowTimeZones = true;
		public bool ShowDaylight = true;
		public bool LockScenes;
		public bool Snap = true;
		public double SnapKm = 0.25;
		public WorldAtlasScene Selected;
		public Func<WorldAtlasScene, string> TimeLabel;

		public Quaternion Orientation = Quaternion.identity;
		public float Zoom = 1f;

		private readonly List<Label> labels = new List<Label>();
		private readonly List<Label> zoneLabels = new List<Label>();
		private static Texture2D blank;

		// Interaction state.
		private bool rotating;
		private GlobeScene dragging;
		private Vector2 grabKm;
		private Vector2 lastPointer;
		private bool moved;
		private Quaternion animateFrom, animateTo;
		private double animateStart = -1.0;
		private IVisualElementScheduledItem animation;

		public event Action<WorldAtlasScene> SceneClicked;
		public event Action EmptyClicked;

		/// <summary>
		/// Drag a rectangle instead of turning the globe: the tool for cutting a new scene out of
		/// a world.
		/// </summary>
		/// <remarks>
		/// A mode rather than a modifier because drawing a rectangle and turning the ball are both
		/// left-drags on empty sky, and guessing which one somebody meant from how far they moved
		/// would get it wrong exactly when the rectangle is small.
		/// </remarks>
		public bool CutMode;

		/// <summary>A rectangle was drawn: its centre in degrees and its size in kilometres.</summary>
		public event Action<double, double, Vector2> RectangleDrawn;

		private bool cutting;
		private Vector3d cutFrom;
		private Vector3d cutTo;
		public event Action<WorldAtlasScene> SceneMoveStarted;
		public event Action<WorldAtlasScene, double, double> SceneMoving;
		public event Action<WorldAtlasScene> SceneMoveEnded;
		/// <summary>Right-click: the scene under the pointer (or null) and the pointer position.</summary>
		public event Action<WorldAtlasScene, Vector2> ContextRequested;
		/// <summary>Raised after any change of view.</summary>
		public event Action ViewChanged;

		public GlobeView()
		{
			style.flexGrow = 1f;
			style.overflow = Overflow.Hidden;
			style.backgroundColor = new Color(0.035f, 0.04f, 0.06f, 1f);
			focusable = true;
			generateVisualContent += Draw;
			RegisterCallback<GeometryChangedEvent>(_ => Refresh());
			RegisterCallback<PointerDownEvent>(OnPointerDown);
			RegisterCallback<PointerMoveEvent>(OnPointerMove);
			RegisterCallback<PointerUpEvent>(OnPointerUp);
			RegisterCallback<WheelEvent>(OnWheel);
			RegisterCallback<KeyDownEvent>(OnKeyDown);
		}

		private static Texture2D Blank
		{
			get
			{
				if (blank == null)
				{
					blank = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
					blank.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
					blank.Apply();
				}
				return blank;
			}
		}

		private float Scale => Mathf.Min(contentRect.width, contentRect.height) * 0.42f * Zoom;

		private Vector2 Centre => contentRect.center;

		// ── Projection ──

		private Vector3 View(Vector3d p) => Orientation * new Vector3((float)p.X, (float)p.Y, (float)p.Z);

		private Vector2 Screen(Vector3 view) => Centre + new Vector2(view.x, -view.y) * Scale;

		/// <summary>The screen point of a unit vector, and whether it faces the viewer.</summary>
		public bool Project(Vector3d p, out Vector2 screen)
		{
			Vector3 v = View(p);
			screen = Screen(v);
			return v.z > 0f;
		}

		/// <summary>The globe point under a screen position, if any.</summary>
		public bool Pick(Vector2 screen, out Vector3d point)
		{
			Vector2 d = (screen - Centre) / Scale;
			d.y = -d.y;
			float r2 = d.sqrMagnitude;
			if (r2 > 1f)
			{
				point = default;
				return false;
			}
			var view = new Vector3(d.x, d.y, Mathf.Sqrt(1f - r2));
			Vector3 p = Quaternion.Inverse(Orientation) * view;
			point = new Vector3d(p.x, p.y, p.z).Normalized;
			return true;
		}

		/// <summary>The topmost scene (not a ghost) under a screen position.</summary>
		public GlobeScene SceneAt(Vector2 screen)
		{
			if (!Pick(screen, out Vector3d point))
			{
				return null;
			}
			GlobeScene hit = null;
			foreach (GlobeScene scene in Scenes)
			{
				if (scene.Ghost || scene.Entry == null)
				{
					continue;
				}
				if (LocalKm(scene.Footprint, point, out Vector2 km) &&
					Mathf.Abs(km.x) <= scene.Footprint.SizeKm.x * 0.5f && Mathf.Abs(km.y) <= scene.Footprint.SizeKm.y * 0.5f)
				{
					if (hit == null || scene.Entry == Selected)
					{
						hit = scene;
					}
				}
			}
			return hit;
		}

		/// <summary>A globe point in a scene's own km frame (X right, Z forward).</summary>
		private bool LocalKm(in AtlasFootprint footprint, Vector3d point, out Vector2 km)
		{
			Vector2 plane = AtlasGeometry.Project(footprint.Latitude, footprint.Longitude, point, RadiusKm);
			double h = footprint.HeadingDegrees * AtlasGeometry.Deg2Rad;
			double cos = Math.Cos(h), sin = Math.Sin(h);
			// east = x cos + z sin, north = −x sin + z cos → invert.
			km = new Vector2((float)(plane.x * cos - plane.y * sin), (float)(plane.x * sin + plane.y * cos));
			return plane.magnitude < RadiusKm * Math.PI * 0.5;
		}

		// ── View control ──

		/// <summary>Orientation that puts a place in the middle with north up.</summary>
		public static Quaternion Facing(double latitude, double longitude)
		{
			return Quaternion.AngleAxis((float)latitude, Vector3.right) * Quaternion.AngleAxis((float)-longitude, Vector3.up);
		}

		public void TurnTo(double latitude, double longitude, bool animate = true)
		{
			Quaternion target = Facing(latitude, longitude);
			if (!animate)
			{
				Orientation = target;
				Refresh();
				return;
			}
			animateFrom = Orientation;
			animateTo = target;
			animateStart = UnityEditor.EditorApplication.timeSinceStartup;
			animation?.Pause();
			animation = schedule.Execute(StepAnimation).Every(16);
		}

		private void StepAnimation()
		{
			double t = (UnityEditor.EditorApplication.timeSinceStartup - animateStart) / 0.35;
			if (t >= 1.0)
			{
				Orientation = animateTo;
				animation?.Pause();
				animation = null;
			}
			else
			{
				float s = (float)(t * t * (3.0 - 2.0 * t));
				Orientation = Quaternion.Slerp(animateFrom, animateTo, s);
			}
			Refresh();
		}

		public void ResetView()
		{
			Zoom = 1f;
			TurnTo(20.0, 0.0);
		}

		/// <summary>Turns and zooms so every placed scene is in view.</summary>
		public void Frame()
		{
			var sum = new Vector3d(0, 0, 0);
			int count = 0;
			foreach (GlobeScene scene in Scenes)
			{
				if (!scene.Ghost)
				{
					sum = sum + AtlasGeometry.ToUnit(scene.Footprint.Latitude, scene.Footprint.Longitude);
					count++;
				}
			}
			if (count == 0)
			{
				ResetView();
				return;
			}
			Vector3d centre = sum.Magnitude > 1e-9 ? sum.Normalized : new Vector3d(0, 0, 1);
			AtlasGeometry.FromUnit(centre, out double lat, out double lon);
			double spread = 0.0;
			foreach (GlobeScene scene in Scenes)
			{
				if (!scene.Ghost)
				{
					double d = AtlasGeometry.AngularDistance(lat, lon, scene.Footprint.Latitude, scene.Footprint.Longitude) * AtlasGeometry.Deg2Rad
						+ scene.Footprint.RadiusKm / Math.Max(1e-6, RadiusKm);
					spread = Math.Max(spread, d);
				}
			}
			Zoom = Mathf.Clamp(spread < 1e-6 ? 4f : 0.9f / (float)Math.Sin(Math.Min(spread, Math.PI * 0.5)), 1f, 60f);
			TurnTo(lat, lon);
		}

		/// <summary>Recomputes labels and repaints.</summary>
		public void Refresh()
		{
			LayoutLabels();
			MarkDirtyRepaint();
			ViewChanged?.Invoke();
		}

		// ── Labels ──

		private void LayoutLabels()
		{
			int index = 0;

			/* The poles, first and therefore under the scene labels if they ever collide.
			 *
			 * Real labels rather than painted text, because these are UI elements and so are drawn
			 * over the globe mesh whatever the camera is doing — a pole marker stroked into the
			 * mesh disappears under the ice cap it is meant to be naming, which is the one place it
			 * has to be readable. */
			if (ShowReference)
			{
				index = PoleLabel(index, 90.0, "N");
				index = PoleLabel(index, -90.0, "S");
			}

			foreach (GlobeScene scene in Scenes)
			{
				if (scene.Ghost || scene.Entry == null)
				{
					continue;
				}
				Vector3d top = AtlasGeometry.SceneToUnit(scene.Footprint, 0.0, scene.Footprint.SizeKm.y * 0.5, RadiusKm);
				if (!Project(top, out Vector2 at) || !contentRect.Contains(at))
				{
					continue;
				}
				Label label = LabelAt(labels, index++);
				string time = TimeLabel != null ? TimeLabel(scene.Entry) : string.Empty;
				string badges = (scene.Warnings > 0 ? $"  ⚠{scene.Warnings}" : string.Empty) + (scene.HasExternalLinks ? "  ◉" : string.Empty);
				label.text = $"{scene.Caption}{badges}\n{time}";
				label.style.left = at.x - 60f;
				label.style.top = at.y - 30f;
				label.style.color = scene.Problem ? new Color(1f, 0.55f, 0.5f, 1f) : scene.Entry == Selected ? new Color(1f, 0.9f, 0.5f, 1f) : new Color(0.95f, 0.96f, 1f, 1f);
				label.style.display = DisplayStyle.Flex;
			}
			for (int i = index; i < labels.Count; i++)
			{
				labels[i].style.display = DisplayStyle.None;
			}

			int zones = 0;
			if (ShowTimeZones)
			{
				// Along the parallel through the middle of the view, so the labels are never clipped.
				Pick(Centre, out Vector3d middle);
				AtlasGeometry.FromUnit(middle, out double lat, out _);
				lat = Math.Max(-70.0, Math.Min(70.0, lat));
				for (int zone = -12; zone <= 12; zone++)
				{
					Vector3d p = AtlasGeometry.ToUnit(lat, zone * 15.0);
					if (!Project(p, out Vector2 at) || View(p).z < 0.25f || !contentRect.Contains(at))
					{
						continue;
					}
					Label label = LabelAt(zoneLabels, zones++);
					label.text = zone == 0 ? "±0 h" : $"{zone:+0;-0} h";
					label.style.left = at.x - 14f;
					label.style.top = at.y + 4f;
					label.style.color = new Color(0.8f, 0.9f, 1f, 0.55f);
					label.style.display = DisplayStyle.Flex;
				}
			}
			for (int i = zones; i < zoneLabels.Count; i++)
			{
				zoneLabels[i].style.display = DisplayStyle.None;
			}
		}

		private Label LabelAt(List<Label> pool, int index)
		{
			while (pool.Count <= index)
			{
				var label = new Label { pickingMode = PickingMode.Ignore };
				label.style.position = Position.Absolute;
				label.style.width = 120f;
				label.style.fontSize = 10;
				label.style.unityTextAlign = TextAnchor.LowerCenter;
				label.style.whiteSpace = WhiteSpace.NoWrap;
				label.style.paddingLeft = 0;
				label.style.paddingRight = 0;
				label.style.paddingTop = 0;
				label.style.paddingBottom = 0;
				label.style.marginLeft = 0;
				label.style.marginTop = 0;
				label.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 1f, color = new Color(0f, 0f, 0f, 0.9f) };
				pool.Add(label);
				Add(label);
			}
			return pool[index];
		}

		/// <summary>Places one pole's letter, if that pole is facing us.</summary>
		private int PoleLabel(int index, double latitude, string text)
		{
			Vector3d pole = AtlasGeometry.ToUnit(latitude, 0.0);
			if (!Project(pole, out Vector2 at) || !contentRect.Contains(at))
			{
				return index;
			}
			Label label = LabelAt(labels, index++);
			label.text = text;
			label.style.left = at.x - 60f;
			label.style.top = at.y - 30f;
			label.style.color = new Color(1f, 0.95f, 0.8f, 0.95f);
			label.style.display = DisplayStyle.Flex;
			return index;
		}

		// ── Drawing ──

		private void Draw(MeshGenerationContext context)
		{
			if (contentRect.width < 10f || contentRect.height < 10f)
			{
				return;
			}
			DrawSphere(context);
			Painter2D painter = context.painter2D;
			if (ShowGrid || ShowTimeZones)
			{
				DrawGrid(painter);
			}

			foreach (GlobeScene scene in Scenes)
			{
				if (!scene.Ghost && scene.Entry != Selected)
				{
					DrawPatch(context, scene);
				}
			}
			foreach (GlobeScene scene in Scenes)
			{
				if (!scene.Ghost && scene.Entry == Selected)
				{
					DrawPatch(context, scene);
				}
			}
			foreach (GlobeScene scene in Scenes)
			{
				DrawOutline(painter, scene);
			}
			// After the scenes, so the rectangle being cut is visible over whatever it overlaps —
			// which is exactly when somebody needs to see it.
			DrawPendingCut(painter);
			foreach (GlobeRoute route in Routes)
			{
				DrawRoute(painter, route);
			}

			// Limb.
			painter.strokeColor = new Color(0.6f, 0.75f, 0.9f, 0.35f);
			painter.lineWidth = 1.5f;
			painter.BeginPath();
			painter.Arc(Centre, Scale, 0f, 360f);
			painter.Stroke();
		}

		private void DrawSphere(MeshGenerationContext context)
		{
			var vertices = new Vertex[(SphereRows + 1) * (SphereColumns + 1)];
			var views = new Vector3[vertices.Length];
			/* With a surface the mesh is white and the texture supplies the colour; the daylight
			 * and limb terms then multiply it, so night and the edge of the disc still read the
			 * same way. Without one, the tint IS the colour, exactly as before. */
			bool textured = Surface != null;
			Color day = textured ? Color.white : BodyColour;
			Color night = new Color(day.r * 0.28f, day.g * 0.3f, day.b * 0.38f, 1f);
			for (int r = 0; r <= SphereRows; r++)
			{
				double lat = 90.0 - 180.0 * r / SphereRows;
				for (int c = 0; c <= SphereColumns; c++)
				{
					double lon = -180.0 + 360.0 * c / SphereColumns;
					Vector3d p = AtlasGeometry.ToUnit(lat, lon);
					Vector3 v = View(p);
					int i = r * (SphereColumns + 1) + c;
					views[i] = v;
					Color colour = day;
					if (ShowDaylight && SunDirection.HasValue)
					{
						float light = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.12f, (float)Vector3d.Dot(p, SunDirection.Value)));
						colour = Color.Lerp(night, day, light);
					}
					// A little shading toward the limb so the ball reads as a ball.
					colour *= 0.72f + 0.28f * Mathf.Clamp01(v.z);
					colour.a = 1f;
					vertices[i] = new Vertex
					{
						position = new Vector3(Screen(v).x, Screen(v).y, Vertex.nearZ),
						tint = colour,
						// Equirectangular, the same way round the baker writes it: longitude
						// across, latitude down from the north pole.
						uv = textured
							? new Vector2((float)((lon + 180.0) / 360.0), (float)((90.0 - lat) / 180.0))
							: Vector2.zero,
					};
				}
			}
			var indices = new List<ushort>(SphereRows * SphereColumns * 6);
			for (int r = 0; r < SphereRows; r++)
			{
				for (int c = 0; c < SphereColumns; c++)
				{
					int a = r * (SphereColumns + 1) + c, b = a + 1, d = a + SphereColumns + 1, e = d + 1;
					if (views[a].z + views[b].z + views[d].z + views[e].z <= 0f)
					{
						continue;
					}
					AddTriangle(indices, vertices, a, d, b);
					AddTriangle(indices, vertices, b, d, e);
				}
			}
			if (indices.Count == 0)
			{
				return;
			}
			MeshWriteData mesh = context.Allocate(vertices.Length, indices.Count, textured ? Surface : Blank);
			mesh.SetAllVertices(vertices);
			mesh.SetAllIndices(indices.ToArray());
		}

		/// <summary>Adds a triangle wound clockwise on screen, which UI Toolkit expects.</summary>
		private static void AddTriangle(List<ushort> indices, Vertex[] vertices, int a, int b, int c)
		{
			Vector3 pa = vertices[a].position, pb = vertices[b].position, pc = vertices[c].position;
			float cross = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
			if (Mathf.Abs(cross) < 1e-6f)
			{
				return;
			}
			indices.Add((ushort)a);
			if (cross > 0f)
			{
				indices.Add((ushort)b);
				indices.Add((ushort)c);
			}
			else
			{
				indices.Add((ushort)c);
				indices.Add((ushort)b);
			}
		}

		private void DrawGrid(Painter2D painter)
		{
			painter.lineWidth = 1f;
			if (ShowGrid)
			{
				painter.strokeColor = new Color(1f, 1f, 1f, 0.08f);
				for (int lat = -75; lat <= 75; lat += 15)
				{
					Polyline(painter, i => AtlasGeometry.ToUnit(lat, -180.0 + 360.0 * i / 120), 121, false);
				}
				for (int lon = -180; lon < 180; lon += 15)
				{
					Polyline(painter, i => AtlasGeometry.ToUnit(-90.0 + 180.0 * i / 60, lon), 61, false);
				}
			}
			if (ShowReference)
			{
				DrawReference(painter);
			}
			if (ShowTimeZones)
			{
				// Zone boundaries sit half an hour either side of each whole hour.
				painter.strokeColor = new Color(0.65f, 0.85f, 1f, 0.22f);
				for (int zone = -12; zone < 12; zone++)
				{
					double lon = zone * 15.0 + 7.5;
					Polyline(painter, i => AtlasGeometry.ToUnit(-90.0 + 180.0 * i / 60, lon), 61, false);
				}
			}
		}

		/// <summary>
		/// The rectangle currently being dragged, as a centre and a size in kilometres.
		/// </summary>
		/// <remarks>
		/// Measured in the centre's own tangent plane rather than in degrees of latitude and
		/// longitude. A degree of longitude is 111 km at the equator and 29 km at 75°, so a
		/// rectangle sized in degrees would be a different scene depending where it was drawn.
		/// </remarks>
		private bool TryPendingRectangle(out double latitude, out double longitude, out Vector2 sizeKm)
		{
			latitude = 0.0;
			longitude = 0.0;
			sizeKm = Vector2.zero;

			AtlasGeometry.FromUnit(cutFrom, out double fromLat, out double fromLon);
			AtlasGeometry.FromUnit(cutTo, out double toLat, out double toLon);

			// Midpoint of the two corners, through the sphere, so it is the centre of the ground
			// rather than the average of two angles that may straddle the antimeridian.
			var middle = new Vector3d(
				(cutFrom.X + cutTo.X) * 0.5,
				(cutFrom.Y + cutTo.Y) * 0.5,
				(cutFrom.Z + cutTo.Z) * 0.5);
			if (middle.Magnitude < 1e-9)
			{
				return false;
			}
			AtlasGeometry.FromUnit(middle, out latitude, out longitude);
			latitude = Math.Max(-85.0, Math.Min(85.0, latitude));
			longitude = AtlasGeometry.WrapLongitude(longitude);

			Vector2 a = AtlasGeometry.Project(latitude, longitude, cutFrom, RadiusKm);
			Vector2 b = AtlasGeometry.Project(latitude, longitude, cutTo, RadiusKm);
			sizeKm = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));

			if (Snap && SnapKm > 0.0)
			{
				sizeKm = new Vector2(
					Mathf.Round(sizeKm.x / (float)SnapKm) * (float)SnapKm,
					Mathf.Round(sizeKm.y / (float)SnapKm) * (float)SnapKm);
			}
			// Below this it is a stray click, not a scene.
			return sizeKm.x >= 0.05f && sizeKm.y >= 0.05f;
		}

		/// <summary>Whether the rectangle being dragged lands on a scene it is not allowed to.</summary>
		private bool PendingOverlaps(in AtlasFootprint footprint)
		{
			foreach (GlobeScene scene in Scenes)
			{
				if (scene.Ghost)
				{
					continue;
				}
				if (AtlasGeometry.Overlaps(footprint, scene.Footprint, RadiusKm))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Outlines the rectangle being dragged, so its size and whether it is allowed are both
		/// visible while it is still being chosen.
		/// </summary>
		private void DrawPendingCut(Painter2D painter)
		{
			if (!cutting || !TryPendingRectangle(out double lat, out double lon, out Vector2 sizeKm))
			{
				return;
			}
			var footprint = new AtlasFootprint { Latitude = lat, Longitude = lon, SizeKm = sizeKm, HeadingDegrees = 0f };
			Vector3d[] corners = AtlasGeometry.Corners(footprint, RadiusKm);
			if (corners == null || corners.Length < 4)
			{
				return;
			}
			// Red while it is over something, so the refusal is seen before the mouse is released
			// rather than read out of a dialog afterwards.
			painter.strokeColor = PendingOverlaps(footprint)
				? new Color(1f, 0.32f, 0.28f, 0.95f)
				: new Color(1f, 0.85f, 0.3f, 0.95f);
			painter.lineWidth = 2f;
			Polyline(painter, i => corners[i % corners.Length], corners.Length + 1, DashStyle.Dashed);
		}

		/// <summary>
		/// The lines a scene is placed against: equator, prime meridian, tropics, polar circles.
		/// </summary>
		/// <remarks>
		/// The tropics and the polar circles are derived from the body's own axial tilt rather than
		/// drawn at Earth's, so a world tipped on its side shows tropics that nearly reach its poles
		/// — which is exactly the case where a designer's intuition about where it is warm is
		/// wrong, and the one where seeing it matters most.
		/// </remarks>
		private void DrawReference(Painter2D painter)
		{
			float tilt = Mathf.Clamp(Mathf.Abs(AxialTiltDegrees), 0f, 90f);

			// Tropics: the furthest from the equator the sun is ever directly overhead.
			if (tilt > 0.5f)
			{
				painter.strokeColor = new Color(1f, 0.82f, 0.45f, 0.3f);
				painter.lineWidth = 1f;
				Parallel(painter, tilt);
				Parallel(painter, -tilt);

				// Polar circles: where the sun fails to rise at all at midwinter.
				float polar = 90f - tilt;
				if (polar < 89.5f)
				{
					painter.strokeColor = new Color(0.7f, 0.88f, 1f, 0.3f);
					Parallel(painter, polar);
					Parallel(painter, -polar);
				}
			}

			// The equator and the prime meridian, brighter because everything else is measured
			// from them.
			painter.strokeColor = new Color(1f, 1f, 1f, 0.32f);
			painter.lineWidth = 1.4f;
			Parallel(painter, 0f);
			Polyline(painter, i => AtlasGeometry.ToUnit(-90.0 + 180.0 * i / 60, 0.0), 61, DashStyle.Solid);

			// The poles themselves, as short spurs along the axis: a dot at the pole is hidden by
			// whatever is drawn over it, and on a turned globe an axis stub reads as an axis.
			painter.strokeColor = new Color(1f, 0.95f, 0.8f, 0.55f);
			painter.lineWidth = 2f;
			Polyline(painter, i => AtlasGeometry.ToUnit(90.0 - i * 2.5, 0.0), 3, DashStyle.Solid);
			Polyline(painter, i => AtlasGeometry.ToUnit(-90.0 + i * 2.5, 0.0), 3, DashStyle.Solid);

			// Where the star is overhead right now, if the sun is known: the hottest point on the
			// world at this instant, and the centre of the lit hemisphere.
			if (SunDirection.HasValue)
			{
				Vector3d sub = SunDirection.Value;
				AtlasGeometry.FromUnit(sub, out double subLat, out double subLon);
				painter.strokeColor = new Color(1f, 0.85f, 0.3f, 0.7f);
				painter.lineWidth = 1.5f;
				const double Ring = 3.0;
				Polyline(painter, i =>
				{
					double angle = 2.0 * Math.PI * i / 24.0;
					return AtlasGeometry.Offset(subLat, subLon,
						Math.Cos(angle) * Ring / 180.0 * Math.PI * RadiusKm,
						Math.Sin(angle) * Ring / 180.0 * Math.PI * RadiusKm,
						RadiusKm);
				}, 25, DashStyle.Solid);
			}
		}

		/// <summary>A line of latitude all the way round.</summary>
		private void Parallel(Painter2D painter, double latitude)
		{
			Polyline(painter, i => AtlasGeometry.ToUnit(latitude, -180.0 + 360.0 * i / 120), 121, DashStyle.Solid);
		}

		private delegate Vector3d PointAt(int index);

		private enum DashStyle { Solid, Dashed, Dotted }

		/// <summary>Strokes the visible parts of a polyline on the globe.</summary>
		private void Polyline(Painter2D painter, PointAt point, int count, bool dashed)
		{
			Polyline(painter, point, count, dashed ? DashStyle.Dashed : DashStyle.Solid);
		}

		private void Polyline(Painter2D painter, PointAt point, int count, DashStyle style)
		{
			float period = style == DashStyle.Dotted ? 7f : 12f;
			float on = style == DashStyle.Dotted ? 2.5f : 7f;
			bool open = false;
			bool drawing = false;
			float run = 0f;
			Vector2 previous = default;
			painter.BeginPath();
			for (int i = 0; i < count; i++)
			{
				bool visible = Project(point(i), out Vector2 s);
				if (!visible)
				{
					open = false;
					continue;
				}
				if (!open)
				{
					painter.MoveTo(s);
					open = true;
					drawing = true;
					run = 0f;
					previous = s;
					continue;
				}
				if (style == DashStyle.Solid)
				{
					painter.LineTo(s);
					previous = s;
					continue;
				}
				// Walk the segment, switching the pen at each dash boundary, so dashes keep their length however the points are spaced.
				float length = Vector2.Distance(previous, s);
				float done = 0f;
				while (done < length)
				{
					float phase = run % period;
					float boundary = drawing ? on - phase : period - phase;
					if (boundary <= 0f)
					{
						boundary = drawing ? period - phase : on;
					}
					float stepLength = Mathf.Min(boundary, length - done);
					done += stepLength;
					run += stepLength;
					Vector2 at = Vector2.Lerp(previous, s, done / length);
					if (drawing)
					{
						painter.LineTo(at);
					}
					else
					{
						painter.MoveTo(at);
					}
					float next = run % period;
					bool shouldDraw = next < on;
					if (shouldDraw != drawing)
					{
						drawing = shouldDraw;
					}
				}
				previous = s;
			}
			painter.Stroke();
		}

		private void DrawPatch(MeshGenerationContext context, GlobeScene scene)
		{
			int n = PatchSteps;
			var vertices = new Vertex[(n + 1) * (n + 1)];
			var facing = new bool[vertices.Length];
			AtlasFootprint f = scene.Footprint;
			bool textured = scene.Preview != null;
			Color tint = textured ? Color.white : scene.Tint;
			/* Overlapping scenes are filled red, not merely outlined in it.
			 *
			 * Scenes in one layer may not overlap — two of them claim the same ground, and a
			 * character standing there is in both and in neither. The outline already went red,
			 * but an outline is a line: on a globe full of rectangles it reads as a selection, and
			 * on a textured world it disappears into the coastline underneath. A flooded patch is
			 * unmistakable at a glance, which is the point of showing it at all. */
			if (scene.Problem)
			{
				tint = Color.Lerp(tint, new Color(1f, 0.25f, 0.2f, 1f), 0.55f);
			}
			if (scene.Entry == Selected)
			{
				tint = Color.Lerp(tint, new Color(1f, 0.95f, 0.7f, 1f), 0.15f);
			}
			for (int y = 0; y <= n; y++)
			{
				for (int x = 0; x <= n; x++)
				{
					double u = x / (double)n, v = y / (double)n;
					Vector2 uv = new Vector2((float)u, (float)v);
					if (textured && scene.Image.IsValid && scene.WorldRect.width > 0f && scene.WorldRect.height > 0f)
					{
						uv = scene.Image.UV(new Vector2(
							scene.WorldRect.xMin + (float)u * scene.WorldRect.width,
							scene.WorldRect.yMin + (float)v * scene.WorldRect.height));
					}
					Vector3d p = AtlasGeometry.SceneToUnit(f, (u - 0.5) * f.SizeKm.x, (v - 0.5) * f.SizeKm.y, RadiusKm);
					Vector3 view = View(p);
					int i = y * (n + 1) + x;
					facing[i] = view.z > 0f;
					Vector2 s = Screen(view);
					vertices[i] = new Vertex
					{
						position = new Vector3(s.x, s.y, Vertex.nearZ),
						tint = tint,
						uv = uv,
					};
				}
			}
			var indices = new List<ushort>(n * n * 6);
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					int a = y * (n + 1) + x, b = a + 1, d = a + n + 1, e = d + 1;
					if (!(facing[a] && facing[b] && facing[d] && facing[e]))
					{
						continue;
					}
					AddTriangle(indices, vertices, a, b, e);
					AddTriangle(indices, vertices, a, e, d);
				}
			}
			if (indices.Count == 0)
			{
				return;
			}
			MeshWriteData mesh = context.Allocate(vertices.Length, indices.Count, textured ? scene.Preview : Blank);
			mesh.SetAllVertices(vertices);
			mesh.SetAllIndices(indices.ToArray());
		}

		private void DrawOutline(Painter2D painter, GlobeScene scene)
		{
			AtlasFootprint f = scene.Footprint;
			double hx = f.SizeKm.x * 0.5, hz = f.SizeKm.y * 0.5;
			const int perSide = 12;
			Vector3d Edge(int i)
			{
				int side = Math.Min(3, i / perSide);
				double t = (i - side * perSide) / (double)perSide;
				switch (side)
				{
					case 0: return AtlasGeometry.SceneToUnit(f, -hx + 2 * hx * t, -hz, RadiusKm);
					case 1: return AtlasGeometry.SceneToUnit(f, hx, -hz + 2 * hz * t, RadiusKm);
					case 2: return AtlasGeometry.SceneToUnit(f, hx - 2 * hx * t, hz, RadiusKm);
					default: return AtlasGeometry.SceneToUnit(f, -hx, hz - 2 * hz * Math.Min(1.0, t), RadiusKm);
				}
			}
			bool selected = scene.Entry == Selected && !scene.Ghost;
			painter.strokeColor = scene.Problem ? new Color(1f, 0.35f, 0.3f, 1f)
				: selected ? new Color(1f, 0.85f, 0.35f, 1f)
				: scene.Ghost ? new Color(scene.Tint.r, scene.Tint.g, scene.Tint.b, 0.35f)
				: new Color(scene.Tint.r, scene.Tint.g, scene.Tint.b, 0.95f);
			painter.lineWidth = selected ? 2.5f : scene.Problem ? 2f : 1.2f;
			Polyline(painter, Edge, perSide * 4 + 1, scene.Ghost);

			if (!scene.Ghost)
			{
				// North marker: a small triangle on the scene's +Z edge.
				Vector3d tip = AtlasGeometry.SceneToUnit(f, 0.0, hz * 0.92, RadiusKm);
				Vector3d left = AtlasGeometry.SceneToUnit(f, -Math.Min(hx, hz) * 0.12, hz * 0.72, RadiusKm);
				Vector3d right = AtlasGeometry.SceneToUnit(f, Math.Min(hx, hz) * 0.12, hz * 0.72, RadiusKm);
				if (Project(tip, out Vector2 a) & Project(left, out Vector2 b) & Project(right, out Vector2 c))
				{
					painter.fillColor = new Color(1f, 1f, 1f, 0.85f);
					painter.BeginPath();
					painter.MoveTo(a);
					painter.LineTo(b);
					painter.LineTo(c);
					painter.ClosePath();
					painter.Fill();
				}
			}
		}

		private void DrawRoute(Painter2D painter, GlobeRoute route)
		{
			if (route.Points.Count < 2)
			{
				return;
			}
			Color colour = route.Clean ? route.Colour : new Color(1f, 0.3f, 0.3f, 1f);
			if (route.Highlight)
			{
				colour = Color.Lerp(colour, Color.white, 0.4f);
			}
			if (route.Ghost)
			{
				colour.a *= 0.35f;
			}
			painter.strokeColor = colour;
			painter.lineWidth = route.Highlight ? 3f : 2f;
			painter.lineJoin = LineJoin.Round;
			painter.lineCap = LineCap.Round;
			Polyline(painter, i => route.Points[i], route.Points.Count, route.CrossLayer ? DashStyle.Dotted : DashStyle.Solid);

			if (route.MarkStart && Project(route.Points[0], out Vector2 start))
			{
				// The teleporter.
				painter.fillColor = colour;
				painter.BeginPath();
				painter.Arc(start, route.Highlight ? 4f : 3f, 0f, 360f);
				painter.Fill();
			}

			// Arrowheads where the line arrives: at the end, and at the start too when it goes both ways.
			Arrowhead(painter, route, colour, true);
			if (route.TwoWay)
			{
				Arrowhead(painter, route, colour, false);
			}
		}

		private void Arrowhead(Painter2D painter, GlobeRoute route, Color colour, bool atEnd)
		{
			int count = route.Points.Count;
			int tip = atEnd ? count - 1 : 0;
			int step = atEnd ? -1 : 1;
			if (!Project(route.Points[tip], out Vector2 e))
			{
				return;
			}
			int back = tip + step;
			Vector2 b = e;
			while (back >= 0 && back < count && Project(route.Points[back], out b) && (e - b).sqrMagnitude < 36f)
			{
				back += step;
			}
			if (back < 0 || back >= count || (e - b).sqrMagnitude <= 1f)
			{
				return;
			}
			Vector2 dir = (e - b).normalized;
			var side = new Vector2(-dir.y, dir.x);
			float size = route.Highlight ? 11f : 9f;
			painter.fillColor = colour;
			painter.BeginPath();
			painter.MoveTo(e);
			painter.LineTo(e - dir * size + side * size * 0.5f);
			painter.LineTo(e - dir * size - side * size * 0.5f);
			painter.ClosePath();
			painter.Fill();
		}

		// ── Input ──

		private void OnPointerDown(PointerDownEvent evt)
		{
			Focus();
			lastPointer = evt.localPosition;
			moved = false;
			if (evt.button == 1)
			{
				GlobeScene hit = SceneAt(evt.localPosition);
				ContextRequested?.Invoke(hit?.Entry, evt.localPosition);
				evt.StopPropagation();
				return;
			}
			if (evt.clickCount == 2 && evt.button == 0 && Pick(evt.localPosition, out Vector3d point))
			{
				AtlasGeometry.FromUnit(point, out double lat, out double lon);
				TurnTo(lat, lon);
				evt.StopPropagation();
				return;
			}
			/* A cut never takes a click that belongs to an existing scene. Scenes cannot overlap,
			 * so a rectangle started inside one could not have been generated anyway — and a mode
			 * that silently stops you selecting what is under the cursor is a mode people leave on
			 * by accident and then fight. Press on empty ground to cut; press on a scene to pick
			 * it up, exactly as when the mode is off. */
			if (CutMode && evt.button == 0 && !evt.altKey && !LockScenes
				&& SceneAt(evt.localPosition) == null
				&& Pick(evt.localPosition, out Vector3d start))
			{
				cutting = true;
				cutFrom = start;
				cutTo = start;
				this.CapturePointer(evt.pointerId);
				evt.StopPropagation();
				return;
			}

			bool turnOnly = evt.button == 2 || evt.altKey || LockScenes;
			GlobeScene scene = turnOnly ? null : SceneAt(evt.localPosition);
			if (scene != null && evt.button == 0)
			{
				SceneClicked?.Invoke(scene.Entry);
				if (Pick(evt.localPosition, out Vector3d p) && LocalKm(scene.Footprint, p, out Vector2 km))
				{
					// Remember where on the scene it was grabbed, in east/north km.
					double h = scene.Footprint.HeadingDegrees * AtlasGeometry.Deg2Rad;
					grabKm = new Vector2((float)(km.x * Math.Cos(h) + km.y * Math.Sin(h)), (float)(-km.x * Math.Sin(h) + km.y * Math.Cos(h)));
					dragging = scene;
				}
			}
			else
			{
				rotating = true;
			}
			this.CapturePointer(evt.pointerId);
			evt.StopPropagation();
		}

		private void OnPointerMove(PointerMoveEvent evt)
		{
			if (!this.HasPointerCapture(evt.pointerId))
			{
				return;
			}
			Vector2 delta = (Vector2)evt.localPosition - lastPointer;
			if (delta.sqrMagnitude < 0.01f)
			{
				return;
			}
			if (!moved && delta.magnitude < 2f)
			{
				return;
			}
			if (!moved && dragging != null)
			{
				SceneMoveStarted?.Invoke(dragging.Entry);
			}
			moved = true;
			lastPointer = evt.localPosition;
			if (cutting)
			{
				if (Pick(evt.localPosition, out Vector3d corner))
				{
					cutTo = corner;
					Refresh();
				}
			}
			else if (rotating)
			{
				float degreesPerPixel = Mathf.Rad2Deg / Mathf.Max(1f, Scale);
				Orientation = Quaternion.AngleAxis(delta.x * degreesPerPixel, Vector3.up) * Quaternion.AngleAxis(delta.y * degreesPerPixel, Vector3.right) * Orientation;
				Refresh();
			}
			else if (dragging != null && Pick(evt.localPosition, out Vector3d point))
			{
				AtlasGeometry.FromUnit(point, out double lat, out double lon);
				Vector3d centre = AtlasGeometry.Offset(lat, lon, -grabKm.x, -grabKm.y, RadiusKm);
				AtlasGeometry.FromUnit(centre, out lat, out lon);
				if (Snap && SnapKm > 0.0)
				{
					double step = SnapKm / Math.Max(1e-6, RadiusKm) * AtlasGeometry.Rad2Deg;
					lat = Math.Round(lat / step) * step;
					lon = Math.Round(lon / step) * step;
				}
				lat = Math.Max(-85.0, Math.Min(85.0, lat));
				SceneMoving?.Invoke(dragging.Entry, lat, AtlasGeometry.WrapLongitude(lon));
			}
			evt.StopPropagation();
		}

		private void OnPointerUp(PointerUpEvent evt)
		{
			if (!this.HasPointerCapture(evt.pointerId))
			{
				return;
			}
			this.ReleasePointer(evt.pointerId);
			if (cutting)
			{
				cutting = false;
				if (moved && TryPendingRectangle(out double lat, out double lon, out Vector2 sizeKm))
				{
					RectangleDrawn?.Invoke(lat, lon, sizeKm);
				}
				Refresh();
				evt.StopPropagation();
				return;
			}
			if (dragging != null && moved)
			{
				SceneMoveEnded?.Invoke(dragging.Entry);
			}
			else if (!moved && rotating && evt.button == 0)
			{
				EmptyClicked?.Invoke();
			}
			dragging = null;
			rotating = false;
			evt.StopPropagation();
		}

		private void OnWheel(WheelEvent evt)
		{
			Zoom = Mathf.Clamp(Zoom * (evt.delta.y > 0 ? 1f / 1.12f : 1.12f), 0.5f, 400f);
			Refresh();
			evt.StopPropagation();
		}

		private void OnKeyDown(KeyDownEvent evt)
		{
			const float step = 5f;
			switch (evt.keyCode)
			{
				case KeyCode.LeftArrow:
					Orientation = Quaternion.AngleAxis(-step, Vector3.up) * Orientation;
					break;
				case KeyCode.RightArrow:
					Orientation = Quaternion.AngleAxis(step, Vector3.up) * Orientation;
					break;
				case KeyCode.UpArrow:
					Orientation = Quaternion.AngleAxis(-step, Vector3.right) * Orientation;
					break;
				case KeyCode.DownArrow:
					Orientation = Quaternion.AngleAxis(step, Vector3.right) * Orientation;
					break;
				case KeyCode.Home:
					ResetView();
					break;
				case KeyCode.Equals:
				case KeyCode.KeypadPlus:
					Zoom = Mathf.Min(400f, Zoom * 1.2f);
					break;
				case KeyCode.Minus:
				case KeyCode.KeypadMinus:
					Zoom = Mathf.Max(0.5f, Zoom / 1.2f);
					break;
				default:
					return;
			}
			Refresh();
			evt.StopPropagation();
		}
	}
}
#endif
