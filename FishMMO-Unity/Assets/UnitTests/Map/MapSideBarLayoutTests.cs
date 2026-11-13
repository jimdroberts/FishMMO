using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEditor;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Everything in the world map's side bar can actually be seen and used.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reported as "clicking a waypoint on the map does nothing" and "the Places list is cut off",
	/// which were one fault. The side bar's sections would not shrink and its container clipped, so
	/// the filters filled it and everything after them was laid out past the bottom of the panel.
	/// The click worked; it selected the waypoint into a section nobody could see, and the only
	/// button that travels was in that section.
	/// </para>
	/// <para>
	/// The real panel is mounted and built by its own <c>OnStarting</c>, because the rows that do
	/// the overflowing are made in C# and the fault is in how they add up against the real sheets.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MapSideBarLayoutTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Map/UIMap.uxml";

		/// <summary>Frames given to layout and text generation after anything changes.</summary>
		private const int SettleFrames = 6;

		/// <summary>How far past an edge counts as rounding rather than being cut off, in points.</summary>
		private const float EdgeTolerance = 0.5f;

		private GameObject host;
		private PanelSettings settings;
		private RenderTexture target;
		private UITKMap map;
		private VisualElement root;
		private VisualElement side;

		[SetUp]
		public void SetUp()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(ProjectFiles.Path(PanelSettingsPath));
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ProjectFiles.Path(UxmlPath));
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the map UXML must exist at {UxmlPath}");

			settings = Object.Instantiate(asset);

			// The reference resolution, so one panel unit is one pixel and no length is snapped.
			target = new RenderTexture(1200, 800, 24, RenderTextureFormat.ARGB32);
			target.Create();
			settings.targetTexture = target;

			host = new GameObject("MapSideBarLayoutTest");
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;

			map = host.AddComponent<UITKMap>();
			map.Document = document;
			map.OnStarting();

			root = document.rootVisualElement;
			side = root.Q<VisualElement>("map-side");
			LogAssert.IsNotNull(side, "the map UXML must still have a #map-side");
		}

		[TearDown]
		public void TearDown()
		{
			if (map != null)
			{
				map.OnDestroying();
			}
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			if (settings != null)
			{
				Object.DestroyImmediate(settings);
			}
			if (target != null)
			{
				target.Release();
				Object.DestroyImmediate(target);
			}

			host = null;
			map = null;
			root = null;
			side = null;
			settings = null;
			target = null;
		}

		private static IEnumerator Frames()
		{
			for (int frame = 0; frame < SettleFrames; ++frame)
			{
				yield return null;
			}
		}

		/// <summary>Waits for layout, and refuses to judge a tree that has none.</summary>
		private IEnumerator Settle()
		{
			yield return Frames();

			LogAssert.IsTrue(side.worldBound.width > 0.0f && side.worldBound.height > 0.0f,
				$"the side bar must have been laid out for any of this to mean anything; it is " +
				$"{side.worldBound.width}x{side.worldBound.height}");
		}

		private List<Toggle> Filters()
		{
			List<Toggle> toggles = side.Query<Toggle>(className: "map-filter").ToList();
			LogAssert.IsTrue(toggles.Count > 0, "the panel must have built its filter toggles");
			return toggles;
		}

		private static string Describe(VisualElement element)
		{
			return $"{element.name} y {element.worldBound.yMin:0.#} to {element.worldBound.yMax:0.#}";
		}

		/// <summary>
		/// Fast travel needs no scrolling: it is what a click on a waypoint is for, and the click
		/// has to visibly do something wherever the side bar happens to be scrolled to.
		/// </summary>
		[UnityTest]
		public IEnumerator TheFastTravelButtonIsAlwaysVisible()
		{
			yield return Settle();

			Button travel = root.Q<Button>("map-waypoint-travel");
			Label hint = root.Q<Label>("map-waypoint-hint");
			LogAssert.IsNotNull(travel, "the map UXML must still have the fast travel button");
			LogAssert.IsNotNull(hint, "the map UXML must still have the waypoint hint");
			LogAssert.IsNull(travel.GetFirstAncestorOfType<ScrollView>(),
				"the fast travel button must not scroll away with the lists");

			foreach (VisualElement element in new VisualElement[] { travel, hint })
			{
				LogAssert.IsTrue(element.worldBound.yMax <= side.worldBound.yMax + EdgeTolerance,
					$"{Describe(element)} must be inside the side bar, which ends at " +
					$"{side.worldBound.yMax:0.#}");
			}
		}

		/// <summary>
		/// The side bar holds more than fits, so everything in it must be reachable by scrolling.
		/// </summary>
		[UnityTest]
		public IEnumerator EveryControlCanBeBroughtIntoView()
		{
			yield return Settle();

			List<VisualElement> controls = Filters().Cast<VisualElement>().ToList();
			controls.Add(root.Q<TextField>("map-note-title"));
			controls.Add(root.Q<Button>("map-note-add"));

			foreach (VisualElement control in controls)
			{
				LogAssert.IsNotNull(control, "the map UXML must still have every note control");

				ScrollView scroll = control.GetFirstAncestorOfType<ScrollView>();
				if (scroll != null)
				{
					scroll.ScrollTo(control);
					yield return Frames();
				}

				Rect visible = scroll != null ? scroll.worldBound : side.worldBound;
				LogAssert.IsTrue(control.worldBound.yMin >= visible.yMin - EdgeTolerance &&
					control.worldBound.yMax <= visible.yMax + EdgeTolerance,
					$"{Describe(control)} ('{(control as Toggle)?.label ?? (control as Toggle)?.text}') " +
					$"must be visible, inside y {visible.yMin:0.#} to {visible.yMax:0.#}");
			}
		}

		/// <summary>A filter is identified by its name, so the whole name has to be drawn.</summary>
		[UnityTest]
		public IEnumerator NoFilterNameIsCutOff()
		{
			yield return Settle();

			var failures = new List<string>();
			foreach (Toggle toggle in Filters())
			{
				foreach (Label label in toggle.Query<Label>().ToList())
				{
					if (string.IsNullOrEmpty(label.text))
					{
						continue;
					}

					float needed = label.MeasureTextSize(label.text, 0.0f, VisualElement.MeasureMode.Undefined,
						0.0f, VisualElement.MeasureMode.Undefined).x;
					float available = label.contentRect.width;
					float overhang = label.worldBound.xMax - toggle.worldBound.xMax;

					Debug.Log($"[MapSideBar] '{label.text}' needs {needed:0.##}, has {available:0.##}, " +
						$"elided {label.isElided}, letter spacing {label.resolvedStyle.letterSpacing}, " +
						$"row {toggle.worldBound.width:0.#}x{toggle.worldBound.height:0.#}, overhang {overhang:0.##}");

					if (label.isElided || needed > available + EdgeTolerance || overhang > EdgeTolerance)
					{
						failures.Add($"'{label.text}' needs {needed:0.##} and has {available:0.##}" +
							$" (elided {label.isElided}, past its row by {overhang:0.##})");
					}
				}
			}

			LogAssert.IsTrue(failures.Count == 0,
				"every filter's name must be drawn whole:\n" + string.Join("\n", failures));
		}
	}
}
