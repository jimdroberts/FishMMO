using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
	/// A marker is drawn where it is, and is clickable over everything it draws (issue #270).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reported as "clicking a Waypoint icon on the map should let you select it, not just the text
	/// label for the waypoint". The reports are right, and the cause is one line of layout: the
	/// <c>-50%/-50%</c> translate that is meant to put a marker's centre on its position was applied
	/// to the marker's whole row rather than to its icon. A marker with a label is therefore slid
	/// left by half the label's width, so the icon lands well outside the radius the click test
	/// looks in — while the label, sitting over the position instead, is what the click finds.
	/// Clicking the icon did nothing at all.
	/// </para>
	/// <para>
	/// Mounted on the real panel settings with the real sheets, and driven through the view's own
	/// click resolution rather than a copy of its arithmetic, because the whole defect is a
	/// disagreement between where a marker is drawn and where the click test looks for it — a test
	/// that recomputes either half could not see it. The resolved geometry is asserted to be
	/// non-zero first: an unlaid-out tree reports every size as zero, and a distance check against
	/// zeroes would pass for the wrong reason.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MapMarkerClickTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Map/UIMap.uxml";

		/// <summary>Side of the map surface the test mounts, in points.</summary>
		private const float ViewSize = 400.0f;

		/// <summary>Half-extent of the view in world metres, so the map covers 200 m across.</summary>
		private const float ViewRange = 100.0f;

		/// <summary>The waypoint's authored index, so the click can be proved to find this one.</summary>
		private const int WaypointIndex = 3;

		/// <summary>A label of the length a real waypoint name has, which is what pushes the icon away.</summary>
		private const string WaypointName = "Blackwood Landing";

		/// <summary>How far from the icon the waypoint's label is expected to start.</summary>
		private const float IconRadius = 10.0f;

		/// <summary>A click may land this far from the icon's centre and still be on the icon.</summary>
		private const float CentreTolerance = 2.0f;

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

			host = new GameObject("MapMarkerClickTest");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;

			/* Mounted where the panel mounts it, so the surface wears the same sheets the player's
			 * map does — the marker classes are styled by UIMapShared.uss, which only the UXML
			 * loads. The size is set here rather than left to the panel so that the geometry the
			 * test reads does not depend on how tall the editor's panel happens to be. */
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
		/// Draws the waypoint and a note far away from it, and waits for the layout to settle.
		/// </summary>
		/// <remarks>
		/// The note is there to be the wrong answer. With only one marker on the map a click test
		/// that ignored its argument and returned the first snapshot would pass every assertion
		/// below.
		/// </remarks>
		private IEnumerator Settle()
		{
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			LogAssert.IsTrue(view.contentRect.width > 0.0f && view.contentRect.height > 0.0f,
				$"the map surface must have been laid out for any of this to mean anything; it is " +
				$"{view.contentRect.width}x{view.contentRect.height}");

			view.SetMarkers(new List<MapMarkerSnapshot>()
			{
				Waypoint(),
				Note(),
			});

			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}
		}

		private static MapMarkerSnapshot Waypoint()
		{
			return new MapMarkerSnapshot()
			{
				Position = Vector3.zero,
				Type = MapMarkerType.Waypoint,
				Relationship = MapRelationship.NonPlayer,
				Tint = Color.white,
				Label = WaypointName,
				Size = 20.0f,
				Priority = 8,
				IsWaypoint = true,
				WaypointIndex = WaypointIndex,
			};
		}

		private static MapMarkerSnapshot Note()
		{
			return new MapMarkerSnapshot()
			{
				Position = new Vector3(60.0f, 0.0f, 0.0f),
				Type = MapMarkerType.Note,
				Relationship = MapRelationship.NonPlayer,
				Tint = Color.white,
				Label = "Camp",
				Size = 14.0f,
				Priority = 10,
				NoteID = 7,
			};
		}

		/// <summary>
		/// The one drawn marker of a given type, found by the type class the view gives it.
		/// </summary>
		/// <remarks>
		/// Found by class rather than by the marker's index in the tree: the view pools marker
		/// elements, so which slot a type lands in is an implementation detail of the pooling and not
		/// something a test about geometry should depend on. The class name is the enum member
		/// lowercased behind the shared prefix, which is the same rule the stylesheet is written to.
		/// </remarks>
		private VisualElement MarkerOfType(string typeClass)
		{
			List<VisualElement> markers = view.Query<VisualElement>(
				className: $"{UITKMapView.MarkerClass}--{typeClass}").ToList();
			LogAssert.AreEqual(1, markers.Count,
				$"exactly one {typeClass} marker must have been drawn, {markers.Count} were");
			return markers[0];
		}

		/// <summary>The icon of the one drawn marker of a given type.</summary>
		private VisualElement IconOfType(string typeClass)
		{
			VisualElement icon = MarkerOfType(typeClass).Q(className: UITKMapView.MarkerIconClass);
			LogAssert.IsNotNull(icon, "a marker must still draw an icon");
			return icon;
		}

		/// <summary>A point in the view's own coordinates, which is the space the click test works in.</summary>
		private Vector2 Local(Vector2 panelPoint)
		{
			/* Cast to the base type on purpose. UITKMapView declares a WorldToLocal of its own —
			 * world position in, element coordinates out — which would otherwise be in the running
			 * for this call and would silently convert this panel point to a world one. */
			return ((VisualElement)view).WorldToLocal(panelPoint);
		}

		/// <summary>Where the click test looks for the waypoint, in the view's coordinates.</summary>
		private Vector2 Anchor()
		{
			return view.WorldToLocal(Vector3.zero);
		}

		/// <summary>Runs the view's own click resolution, so the test cannot disagree with it.</summary>
		private MapMarkerSnapshot? Resolve(Vector2 localPosition)
		{
			MethodInfo info = typeof(UITKMapView).GetMethod(
				"FindNearestSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, "UITKMapView must still declare FindNearestSnapshot");

			return (MapMarkerSnapshot?)info.Invoke(view, new object[] { localPosition });
		}

		/// <summary>
		/// A waypoint keeps the diamond that tells it apart from everything else on the map.
		/// </summary>
		/// <remarks>
		/// The shape is set by a stylesheet rule on the icon, and the view writes the icon's rotation
		/// itself for markers that face somewhere. Writing it unconditionally meant writing zero for
		/// every discovered waypoint, and an inline style beats a stylesheet — so the diamond was
		/// destroyed every frame by the code that had no opinion about it. A waypoint is the one
		/// marker the player is meant to click rather than read, and the shape is how they find it.
		/// </remarks>
		[UnityTest]
		public IEnumerator AWaypointKeepsItsDiamond()
		{
			yield return Settle();

			VisualElement icon = IconOfType("waypoint");
			float angle = Mathf.Abs(icon.resolvedStyle.rotate.angle.ToDegrees());
			LogAssert.IsTrue(Mathf.Abs(angle - 45.0f) < 0.5f,
				$"a waypoint's icon must keep the 45° rotation that makes it a diamond — the shape is " +
				$"the one thing nothing else on the map uses. It resolves to {angle:0.#}°, so the " +
				$"stylesheet rule is being overwritten by the view's own rotation write.");

			/* And nothing else wears one: the same write must not have leaked a rotation onto a
			 * marker that has no facing, or every note on the map would be a diamond too. */
			VisualElement noteIcon = IconOfType("note");

			float noteAngle = Mathf.Abs(noteIcon.resolvedStyle.rotate.angle.ToDegrees());
			LogAssert.IsTrue(noteAngle < 0.5f,
				$"a marker with no facing and no shape rule must not be rotated; the note's icon " +
				$"resolves to {noteAngle:0.#}°");
		}

		[UnityTest]
		public IEnumerator TheIconIsDrawnOnTheWaypointsPosition()
		{
			yield return Settle();

			VisualElement icon = IconOfType("waypoint");

			Vector2 anchor = Anchor();
			Vector2 iconCentre = Local(icon.worldBound.center);
			float offset = Vector2.Distance(anchor, iconCentre);

			LogAssert.IsTrue(offset < CentreTolerance,
				$"the icon must be drawn on the waypoint's position, so that the thing the player " +
				$"sees is the thing that is there — and so that a click on it lands where the click " +
				$"test looks. It is drawn {offset:0.#}px from its position: the icon's centre is at " +
				$"{iconCentre}, the position is at {anchor} (issue #270).");
		}

		[UnityTest]
		public IEnumerator AClickOnTheWaypointIconSelectsTheWaypoint()
		{
			yield return Settle();

			VisualElement icon = IconOfType("waypoint");

			/* Everywhere on the icon, not just its exact centre: the icon is the target, and a
			 * player clicking its edge is clicking it. */
			Vector2 centre = Local(icon.worldBound.center);
			Vector2[] clicks =
			{
				centre,
				centre + new Vector2(IconRadius * 0.75f, 0.0f),
				centre + new Vector2(0.0f, -IconRadius * 0.75f),
			};

			foreach (Vector2 click in clicks)
			{
				MapMarkerSnapshot? hit = Resolve(click);

				LogAssert.IsTrue(hit.HasValue && hit.Value.IsWaypoint,
					$"a click at {click} is on the waypoint's icon and must select it; it resolved to " +
					$"{(hit.HasValue ? hit.Value.Type.ToString() : "nothing")} (issue #270)");
				LogAssert.AreEqual(WaypointIndex, hit.Value.WaypointIndex,
					"and it must select the waypoint that was clicked");
			}
		}

		[UnityTest]
		public IEnumerator AClickOnTheWaypointLabelStillSelectsTheWaypoint()
		{
			yield return Settle();

			VisualElement marker = MarkerOfType("waypoint");
			VisualElement label = marker.Q(className: UITKMapView.MarkerLabelClass);
			LogAssert.IsNotNull(label, "a waypoint's name must still be drawn beside its icon");

			/* Beside the icon, and level with it. The name is hung out of flow, so nothing about the
			 * direction it hangs in keeps it centred on the dot it belongs to — that is its own rule,
			 * and a name sitting four pixels low reads as a marker assembled carelessly.
			 *
			 * Measured against the marker rather than the icon: the marker's centre is the position
			 * the icon is drawn on, and unlike a waypoint's icon the marker is never rotated — a
			 * rotated element's worldBound is the box round the rotated shape, which for a diamond
			 * is a third wider than the square it came from. */
			float lift = Mathf.Abs(label.worldBound.center.y - marker.worldBound.center.y);
			LogAssert.IsTrue(lift < 0.5f,
				$"a marker's name must be centred on its icon; it is {lift:0.#}px out of line " +
				$"(label centre y {label.worldBound.center.y:0.#}, position y " +
				$"{marker.worldBound.center.y:0.#})");

			/* The far end of the name as well as its middle. Clicking the name is how a player
			 * chooses a waypoint today, and centring the icon on the position must not take that
			 * away — a marker has to be clickable over everything it draws, not only over the part
			 * that happens to sit near its position. */
			Vector2[] clicks =
			{
				Local(label.worldBound.center),
				Local(new Vector2(label.worldBound.xMax - 2.0f, label.worldBound.center.y)),
			};

			foreach (Vector2 click in clicks)
			{
				MapMarkerSnapshot? hit = Resolve(click);

				LogAssert.IsTrue(hit.HasValue && hit.Value.IsWaypoint,
					$"a click at {click} is on the waypoint's name and must still select it; it " +
					$"resolved to {(hit.HasValue ? hit.Value.Type.ToString() : "nothing")}");
				LogAssert.AreEqual(WaypointIndex, hit.Value.WaypointIndex,
					"and it must select the waypoint whose name was clicked");
			}
		}

		[UnityTest]
		public IEnumerator AClickOnEmptyGroundSelectsNothing()
		{
			yield return Settle();

			/* The control. Without it, a click test that returned a snapshot for every point would
			 * satisfy both tests above, and would drop a note on top of a waypoint instead of
			 * selecting it. North of the waypoint is empty ground at this zoom — and it has to be
			 * empty by a wide margin, because a marker's name is drawn out to the right of it and
			 * counts as part of the marker. */
			MapMarkerSnapshot? hit = Resolve(view.WorldToLocal(new Vector3(0.0f, 0.0f, 30.0f)));

			LogAssert.IsFalse(hit.HasValue,
				$"empty ground must resolve to no marker — a click there chooses where a note goes; " +
				$"it resolved to {(hit.HasValue ? hit.Value.Type.ToString() : "nothing")}");
		}
	}
}
