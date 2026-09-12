using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A marker is hidden when it has left the frame, and not while any of it is still on it
	/// (issue #271).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reported as "culling icons/labels at Icon's half width instead of when they actually leave
	/// bounds, this cuts off half of the rendered icon". The cull asked whether the marker's
	/// <i>centre</i> was inside the view, and the marker is drawn centred on its position — so it was
	/// hidden at the exact moment half of it was still on the frame, and what a player saw was a
	/// half-drawn icon vanishing rather than sliding off. A name is worse: it hangs off the icon's
	/// right, so on the left of the frame a marker whose icon is entirely gone can still have its
	/// whole name on screen, and that name went out with the icon.
	/// </para>
	/// <para>
	/// Mounted on the real panel with the real sheets and driven through the view's own layout pass,
	/// because the defect is in the geometry the view decides on — a test that recomputed the
	/// rectangle would be testing a second copy of the rule rather than the one that ships. The
	/// settled geometry is asserted to be non-zero first, since an unlaid-out tree reports every size
	/// as zero and every marker would then look correctly culled.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MapMarkerCullTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Map/UIMap.uxml";

		/// <summary>Side of the map surface the test mounts, in points.</summary>
		private const float ViewSize = 400.0f;

		/// <summary>Half-extent of the view in world metres, so the map covers 200 m across.</summary>
		private const float ViewRange = 100.0f;

		/// <summary>Side of a marker's icon, in points. Half of it is the distance a cull must respect.</summary>
		private const float IconSize = 20.0f;

		/// <summary>A name long enough to reach well back onto the frame from off its left edge.</summary>
		private const string MarkerName = "Blackwood Landing";

		/// <summary>
		/// A position just past the right edge of the view: half a metre out of a 200 m map, so the
		/// icon's centre is over the border with nine of its twenty points still inside.
		/// </summary>
		private const float HalfOverTheEdge = 100.5f;

		/// <summary>Far enough out that no part of the icon reaches the frame at all.</summary>
		private const float WhollyOutside = 120.0f;

		/// <summary>
		/// Far enough off the left edge that the icon is gone, and close enough that the name still
		/// reaches back onto the frame.
		/// </summary>
		private const float OffTheLeftWithTheNameOn = -120.0f;

		private GameObject host;
		private UIDocument document;
		private PanelSettings settings;
		private UITKMapView view;

		[SetUp]
		public void SetUp()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the map UXML must exist at {UxmlPath}");

			settings = Object.Instantiate(asset);

			host = new GameObject("MapMarkerCullTest");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;

			/* Mounted where the panel mounts it, so the surface wears the same sheets the player's
			 * map does — the marker classes are styled by UIMapShared.uss, which only the UXML
			 * loads. */
			VisualElement surfaceHost = document.rootVisualElement.Q<VisualElement>("map-view");
			LogAssert.IsNotNull(surfaceHost, "the map UXML must still have a #map-view to mount the surface into");

			view = new UITKMapView()
			{
				name = "map-surface",
			};
			view.style.flexGrow = 0.0f;
			view.style.width = ViewSize;
			view.style.height = ViewSize;
			view.View = new MapViewTransform(Vector3.zero, ViewRange, 0.0f);

			surfaceHost.Clear();
			surfaceHost.Add(view);
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			if (settings != null)
			{
				Object.DestroyImmediate(settings);
			}

			host = null;
			document = null;
			settings = null;
			view = null;
		}

		/// <summary>
		/// Draws one marker and lets the layout settle the way a running map lets it settle.
		/// </summary>
		/// <param name="marker">The marker to place.</param>
		/// <remarks>
		/// Re-placed every frame, which is what the panels do: collecting markers is expensive enough
		/// to run ten times a second, but the view moves every frame with the player, so the markers
		/// are laid out at the frame rate. It also matters to this fixture — a cull is decided from
		/// the rectangle the element was last laid out over, so a single pass would only ever see a
		/// marker that has never been measured, and every one of them would be drawn.
		/// </remarks>
		private IEnumerator Settle(MapMarkerSnapshot marker)
		{
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			LogAssert.IsTrue(view.contentRect.width > 0.0f && view.contentRect.height > 0.0f,
				$"the map surface must have been laid out for any of this to mean anything; it is " +
				$"{view.contentRect.width}x{view.contentRect.height}");

			view.SetMarkers(new List<MapMarkerSnapshot>() { marker });

			for (int frame = 0; frame < 10; ++frame)
			{
				view.RelayoutMarkers();
				yield return null;
			}
		}

		/// <summary>The one drawn marker of a given type, by the type class the view gives it.</summary>
		/// <remarks>
		/// Found by class rather than by index in the tree: the view pools marker elements, so which
		/// slot a marker lands in is an implementation detail of the pooling.
		/// </remarks>
		private VisualElement MarkerOfType(string typeClass)
		{
			List<VisualElement> markers = view.Query<VisualElement>(
				className: $"{UITKMapView.MarkerClass}--{typeClass}").ToList();
			LogAssert.AreEqual(1, markers.Count,
				$"exactly one {typeClass} marker must have been drawn, {markers.Count} were");
			return markers[0];
		}

		/// <summary>The frame the markers are culled against, in the view's own coordinates.</summary>
		private Rect Frame()
		{
			return new Rect(Vector2.zero, view.contentRect.size);
		}

		/// <summary>A point in the view's own coordinates.</summary>
		private Vector2 Local(Vector2 panelPoint)
		{
			/* Cast to the base type on purpose. UITKMapView declares a WorldToLocal of its own —
			 * world position in, element coordinates out — which would otherwise be in the running
			 * for this call and would silently convert this panel point to a world one. */
			return ((VisualElement)view).WorldToLocal(panelPoint);
		}

		/// <summary>Where the view puts a world position, in the view's own coordinates.</summary>
		private Vector2 Anchor(Vector3 worldPosition)
		{
			return view.WorldToLocal(worldPosition);
		}

		/// <summary>A drawn marker at a world position, wearing a name.</summary>
		private static MapMarkerSnapshot MarkerAt(Vector3 position)
		{
			return new MapMarkerSnapshot()
			{
				Position = position,
				Type = MapMarkerType.Note,
				Relationship = MapRelationship.NonPlayer,
				Tint = Color.white,
				Label = MarkerName,
				Size = IconSize,
				Priority = 10,
				NoteID = 7,
			};
		}

		/// <summary>
		/// The frame's own width is what everything below is measured against, so it must be the one
		/// the view was given.
		/// </summary>
		private void AssertFrameIsTheView()
		{
			Rect frame = Frame();
			LogAssert.IsTrue(frame.width > IconSize && frame.height > IconSize,
				$"the frame must be bigger than an icon for a marker to be half over its edge; it is " +
				$"{frame.width}x{frame.height} and an icon is {IconSize}");
		}

		/// <summary>
		/// A marker whose centre has crossed the edge is still drawn while part of it is on the frame.
		/// </summary>
		[UnityTest]
		public IEnumerator AMarkerHalfOverTheEdgeIsStillDrawn()
		{
			yield return Settle(MarkerAt(new Vector3(HalfOverTheEdge, 0.0f, 0.0f)));
			AssertFrameIsTheView();

			Rect frame = Frame();
			Vector2 anchor = Anchor(new Vector3(HalfOverTheEdge, 0.0f, 0.0f));

			/* The two halves of the case, stated on the position rather than on the element: the
			 * icon's centre is past the frame, and half of the icon is not. Culling by the centre —
			 * which is what the issue reports — gets this wrong in exactly one way. */
			LogAssert.IsTrue(anchor.x > frame.xMax,
				$"the icon's centre must be past the frame's right edge for this to be the case that " +
				$"was reported; the anchor is at {anchor.x:0.#} and the frame ends at {frame.xMax:0.#}");
			LogAssert.IsTrue(anchor.x - (IconSize * 0.5f) < frame.xMax,
				$"and half the icon must still be on the frame; it starts at " +
				$"{anchor.x - (IconSize * 0.5f):0.#} and the frame ends at {frame.xMax:0.#}");

			VisualElement marker = MarkerOfType("note");
			LogAssert.AreEqual(DisplayStyle.Flex, marker.resolvedStyle.display,
				"a marker with half an icon on the frame is not off the map, and hiding it there cuts " +
				"the drawn icon in two (issue #271)");

			/* And the half that is on the frame is really drawn there rather than the element merely
			 * being left visible somewhere else. */
			VisualElement icon = marker.Q(className: UITKMapView.MarkerIconClass);
			LogAssert.IsNotNull(icon, "a marker must still draw an icon");

			float iconLeft = Local(icon.worldBound.min).x;
			float iconRight = Local(icon.worldBound.max).x;
			LogAssert.IsTrue(iconLeft < frame.xMax && iconRight > frame.xMax,
				$"the icon must straddle the frame's right edge: it is drawn from {iconLeft:0.#} to " +
				$"{iconRight:0.#}, and the frame ends at {frame.xMax:0.#}");
		}

		/// <summary>
		/// A marker the frame does not reach is hidden, name and all.
		/// </summary>
		/// <remarks>
		/// The control. A cull that never hid anything would satisfy the test above, and the map
		/// would draw every note in the scene every frame.
		/// </remarks>
		[UnityTest]
		public IEnumerator AMarkerWhollyOffTheFrameIsCulled()
		{
			yield return Settle(MarkerAt(new Vector3(WhollyOutside, 0.0f, 0.0f)));
			AssertFrameIsTheView();

			Rect frame = Frame();
			Vector2 anchor = Anchor(new Vector3(WhollyOutside, 0.0f, 0.0f));

			LogAssert.IsTrue(anchor.x - (IconSize * 0.5f) > frame.xMax,
				$"the whole icon must be past the frame's right edge; it starts at " +
				$"{anchor.x - (IconSize * 0.5f):0.#} and the frame ends at {frame.xMax:0.#}");

			VisualElement marker = MarkerOfType("note");
			LogAssert.AreEqual(DisplayStyle.None, marker.resolvedStyle.display,
				"a marker with nothing left on the frame must be hidden; the map would otherwise draw " +
				"every marker in the scene every frame");
		}

		/// <summary>
		/// A name still reaching onto the frame keeps its marker drawn, long after the icon has gone.
		/// </summary>
		/// <remarks>
		/// The second half of the report. A name hangs off the icon's right — out of flow, see
		/// .map-marker__label — so the last thing of a marker to leave the left edge of the frame is
		/// its own name, and a cull that consults only the icon takes the name away while the player
		/// is still reading it.
		/// </remarks>
		[UnityTest]
		public IEnumerator AMarkerWithItsNameOnTheFrameIsStillDrawn()
		{
			yield return Settle(MarkerAt(new Vector3(OffTheLeftWithTheNameOn, 0.0f, 0.0f)));
			AssertFrameIsTheView();

			Rect frame = Frame();
			Vector2 anchor = Anchor(new Vector3(OffTheLeftWithTheNameOn, 0.0f, 0.0f));

			LogAssert.IsTrue(anchor.x + (IconSize * 0.5f) < frame.xMin,
				$"the icon must be entirely off the left of the frame for the name to be the thing that " +
				$"is being tested; the icon ends at {anchor.x + (IconSize * 0.5f):0.#} and the frame " +
				$"starts at {frame.xMin:0.#}");

			VisualElement marker = MarkerOfType("note");
			LogAssert.AreEqual(DisplayStyle.Flex, marker.resolvedStyle.display,
				"a marker whose name is still on the frame is still a marker on the map; culling it with " +
				"its icon takes the name away while it is being read (issue #271)");

			VisualElement label = marker.Q(className: UITKMapView.MarkerLabelClass);
			LogAssert.IsNotNull(label, "a marker with a name must still draw it");

			/* Asserted first, because a name that measured nothing would satisfy the assertion above
			 * for the wrong reason. */
			LogAssert.IsTrue(label.worldBound.width > 0.0f,
				"the name must have been laid out with a width for this test to mean anything; it " +
				$"resolves to {label.worldBound.width:0.#} points wide");

			float labelLeft = Local(label.worldBound.min).x;
			float labelRight = Local(label.worldBound.max).x;
			LogAssert.IsTrue(labelLeft < frame.xMin && labelRight > frame.xMin,
				$"the name must straddle the frame's left edge: it is drawn from {labelLeft:0.#} to " +
				$"{labelRight:0.#}, and the frame starts at {frame.xMin:0.#}");
		}
	}
}
