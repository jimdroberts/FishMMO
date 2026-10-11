#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The World Atlas scene inspector's flat picture of one scene: its baked map or Library preview,
	/// with every point of interest drawn on it in its group's colour.
	/// </summary>
	/// <remarks>
	/// The image is drawn as one quad whose corner UVs come from <see cref="AtlasImage.UV"/>, so a map
	/// baked over a different rectangle or turned by a north offset still sits under the right ground.
	/// The markers are child elements rather than painted dots so that each carries its own tooltip and
	/// click, with nothing to hit-test by hand.
	/// </remarks>
	public sealed class AtlasScenePreview : VisualElement
	{
		private const float MarkerSize = 7f;

		private readonly VisualElement markers;
		private readonly VisualElement ways;
		private readonly List<ScenePath> paths = new List<ScenePath>();
		private AtlasImage image;
		private Rect worldRect;
		private readonly List<AtlasPoint> points = new List<AtlasPoint>();
		private readonly HashSet<PointOfInterestGroup> hidden = new HashSet<PointOfInterestGroup>();
		private static Texture2D blank;

		/// <summary>A marker was clicked.</summary>
		public event Action<AtlasPoint> PointClicked;

		public AtlasScenePreview()
		{
			style.position = Position.Relative;
			style.overflow = Overflow.Hidden;
			style.backgroundColor = new Color(0.08f, 0.1f, 0.12f, 1f);
			style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1f;
			style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = new Color(0f, 0f, 0f, 0.4f);
			generateVisualContent += DrawImage;
			// The ways between the image and the markers, so a marker always sits on its road.
			ways = new VisualElement { pickingMode = PickingMode.Ignore };
			ways.style.position = Position.Absolute;
			ways.style.left = ways.style.top = ways.style.right = ways.style.bottom = 0f;
			ways.generateVisualContent += DrawWays;
			Add(ways);
			markers = new VisualElement { pickingMode = PickingMode.Ignore };
			markers.style.position = Position.Absolute;
			markers.style.left = markers.style.top = markers.style.right = markers.style.bottom = 0f;
			Add(markers);
			RegisterCallback<GeometryChangedEvent>(_ => Layout());
		}

		/// <summary>The groups present, in enum order, for the filter row.</summary>
		public List<PointOfInterestGroup> Groups()
		{
			var present = new SortedSet<PointOfInterestGroup>();
			foreach (AtlasPoint point in points)
			{
				present.Add(point.Group);
			}
			return new List<PointOfInterestGroup>(present);
		}

		/// <summary>Shows a scene: the image, the world rectangle the preview stands for, and its points.</summary>
		public void Set(AtlasImage sceneImage, Rect sceneRect, IReadOnlyList<AtlasPoint> scenePoints)
		{
			image = sceneImage;
			worldRect = sceneRect;
			points.Clear();
			if (scenePoints != null)
			{
				points.AddRange(scenePoints);
			}
			// Keep the scene's own shape, at the inspector's width.
			float aspect = sceneRect.width > 0f && sceneRect.height > 0f ? sceneRect.height / sceneRect.width : 1f;
			style.height = Mathf.Clamp(330f * aspect, 120f, 420f);
			Rebuild();
		}

		/// <summary>Shows a scene's ways (roads, tracks, footpaths, trails, streets) under its markers; null for none.</summary>
		public void SetPaths(IReadOnlyList<ScenePath> scenePaths)
		{
			paths.Clear();
			if (scenePaths != null)
			{
				paths.AddRange(scenePaths);
			}
			ways.MarkDirtyRepaint();
		}

		/// <summary>A way's line on the preview: width by class, a road pale and bold, a lost trail faint.</summary>
		private void DrawWays(MeshGenerationContext context)
		{
			Rect content = contentRect;
			if (paths.Count == 0 || content.width < 2f || worldRect.width <= 0f)
			{
				return;
			}
			Painter2D painter = context.painter2D;
			painter.lineJoin = LineJoin.Round;
			painter.lineCap = LineCap.Round;
			// Lesser ways first, so a road is drawn over the trail that joins it.
			var order = new List<ScenePath>(paths);
			order.Sort((a, b) => a.Class.CompareTo(b.Class));
			foreach (ScenePath path in order)
			{
				if (path == null || path.Count < 2)
				{
					continue;
				}
				float wear = 0f;
				foreach (float w in path.Wear)
				{
					wear += w;
				}
				wear /= Mathf.Max(1, path.Wear.Length);
				switch (path.Class)
				{
					case ScenePathClass.Highway:
						painter.lineWidth = 3f;
						painter.strokeColor = new Color(0.98f, 0.92f, 0.78f, 0.95f);
						break;
					case ScenePathClass.Road:
					case ScenePathClass.Street:
						painter.lineWidth = 2.2f;
						painter.strokeColor = new Color(0.95f, 0.88f, 0.72f, 0.9f);
						break;
					case ScenePathClass.CartTrack:
						painter.lineWidth = 1.6f;
						painter.strokeColor = new Color(0.86f, 0.72f, 0.5f, 0.85f);
						break;
					case ScenePathClass.Footpath:
						painter.lineWidth = 1.2f;
						painter.strokeColor = new Color(0.82f, 0.66f, 0.46f, 0.8f);
						break;
					default:
						painter.lineWidth = 1f;
						painter.strokeColor = new Color(0.78f, 0.6f, 0.42f, Mathf.Lerp(0.35f, 0.7f, wear));
						break;
				}
				painter.BeginPath();
				painter.MoveTo(ToLocal(path.Points[0], content));
				// A bridge's span is drawn too: on the map a road crosses the river it bridges.
				for (int i = 1; i < path.Count; i++)
				{
					painter.LineTo(ToLocal(path.Points[i], content));
				}
				painter.Stroke();
			}
		}

		/// <summary>Shows or hides one group's markers.</summary>
		public void SetGroupVisible(PointOfInterestGroup group, bool visible)
		{
			if (visible) hidden.Remove(group); else hidden.Add(group);
			Layout();
		}

		private void Rebuild()
		{
			markers.Clear();
			// Settlements and other low tiers last, so they sit on top of the small features near them.
			var order = new List<AtlasPoint>(points);
			order.Sort((a, b) => b.Tier.CompareTo(a.Tier));
			foreach (AtlasPoint point in order)
			{
				var marker = new VisualElement { userData = point, tooltip = point.Describe() };
				marker.style.position = Position.Absolute;
				float size = point.Tier <= 1 ? MarkerSize + 3f : MarkerSize;
				marker.style.width = marker.style.height = size;
				marker.style.marginLeft = marker.style.marginTop = -size * 0.5f;
				marker.style.backgroundColor = AtlasPointsOfInterest.GroupColour(point.Group);
				float radius = point.Group == PointOfInterestGroup.Settlement ? 1f : size * 0.5f;
				marker.style.borderTopLeftRadius = marker.style.borderTopRightRadius = marker.style.borderBottomLeftRadius = marker.style.borderBottomRightRadius = radius;
				marker.style.borderTopWidth = marker.style.borderBottomWidth = marker.style.borderLeftWidth = marker.style.borderRightWidth = 1f;
				Color edge = point.Group == PointOfInterestGroup.Settlement ? Color.white : new Color(0f, 0f, 0f, 0.85f);
				marker.style.borderTopColor = marker.style.borderBottomColor = marker.style.borderLeftColor = marker.style.borderRightColor = edge;
				marker.pickingMode = PickingMode.Position;
				AtlasPoint captured = point;
				marker.RegisterCallback<ClickEvent>(evt =>
				{
					PointClicked?.Invoke(captured);
					evt.StopPropagation();
				});
				markers.Add(marker);
			}
			Layout();
			MarkDirtyRepaint();
		}

		/// <summary>Places every marker for the current size.</summary>
		private void Layout()
		{
			Rect content = contentRect;
			foreach (VisualElement marker in markers.Children())
			{
				if (!(marker.userData is AtlasPoint point))
				{
					continue;
				}
				Vector2 at = ToLocal(point.Position, content);
				bool inside = at.x >= 0f && at.y >= 0f && at.x <= content.width && at.y <= content.height;
				marker.style.display = inside && !hidden.Contains(point.Group) ? DisplayStyle.Flex : DisplayStyle.None;
				marker.style.left = at.x;
				marker.style.top = at.y;
			}
		}

		/// <summary>A world position on the preview: +X right, +Z up, the scene rectangle edge to edge.</summary>
		private Vector2 ToLocal(Vector3 world, Rect content)
		{
			if (worldRect.width <= 0f || worldRect.height <= 0f)
			{
				return new Vector2(content.width * 0.5f, content.height * 0.5f);
			}
			float u = (world.x - worldRect.xMin) / worldRect.width;
			float v = (world.z - worldRect.yMin) / worldRect.height;
			return new Vector2(u * content.width, (1f - v) * content.height);
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

		private void DrawImage(MeshGenerationContext context)
		{
			Rect content = contentRect;
			if (content.width < 2f || content.height < 2f || !image.IsValid || worldRect.width <= 0f || worldRect.height <= 0f)
			{
				return;
			}
			// Top-left, top-right, bottom-right, bottom-left of the preview, in world XZ.
			var corners = new[]
			{
				new Vector2(worldRect.xMin, worldRect.yMax),
				new Vector2(worldRect.xMax, worldRect.yMax),
				new Vector2(worldRect.xMax, worldRect.yMin),
				new Vector2(worldRect.xMin, worldRect.yMin),
			};
			var positions = new[]
			{
				new Vector3(content.xMin, content.yMin, Vertex.nearZ),
				new Vector3(content.xMax, content.yMin, Vertex.nearZ),
				new Vector3(content.xMax, content.yMax, Vertex.nearZ),
				new Vector3(content.xMin, content.yMax, Vertex.nearZ),
			};
			MeshWriteData mesh = context.Allocate(4, 6, image.Texture != null ? image.Texture : Blank);
			for (int i = 0; i < 4; i++)
			{
				mesh.SetNextVertex(new Vertex { position = positions[i], tint = Color.white, uv = image.UV(corners[i]) });
			}
			mesh.SetNextIndex(0);
			mesh.SetNextIndex(1);
			mesh.SetNextIndex(2);
			mesh.SetNextIndex(0);
			mesh.SetNextIndex(2);
			mesh.SetNextIndex(3);
		}
	}
}
#endif
