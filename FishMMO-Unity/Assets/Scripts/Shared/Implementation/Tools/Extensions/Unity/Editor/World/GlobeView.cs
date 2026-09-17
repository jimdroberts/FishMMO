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
			Color day = BodyColour;
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
						uv = Vector2.zero,
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
			MeshWriteData mesh = context.Allocate(vertices.Length, indices.Count, Blank);
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
			if (rotating)
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
