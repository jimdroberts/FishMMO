using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Waypoints on the maps: only unlocked ones are drawn, a stray marker component can never reveal one, and the filter
	/// shelves them with the landmarks. Moved from WaypointTests and TravelWaypointTests with the Map module (2026-11-20);
	/// the unlock record is a stub here, so the Map module needs no Travel module to test against.
	/// </summary>
	[TestFixture]
	public class MapWaypointTests
	{
		private readonly List<GameObject> hosts = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < hosts.Count; ++i)
			{
				if (hosts[i] != null)
				{
					Object.DestroyImmediate(hosts[i]);
				}
			}
			hosts.Clear();
		}

		private GameObject NewHost(string name)
		{
			GameObject go = new GameObject(name);
			hosts.Add(go);
			return go;
		}

		/// <summary>An unlock record holding one scene's first page (what <c>WaypointController.Restore</c> would hold).</summary>
		private sealed class StubWaypoints : IWaypointController
		{
			private readonly string scene;
			private readonly ulong mask;

			public StubWaypoints(string scene, ulong mask)
			{
				this.scene = scene;
				this.mask = mask;
			}

			public ICharacter Character => null;
			public bool Initialized => true;
			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
			public WaypointTravelPolicy TravelPolicy => default;
			public void RequestTravel(string sceneName, int waypointIndex) { }
			public IReadOnlyCollection<string> UnlockedScenes => new[] { scene };
			public bool IsUnlocked(string sceneName, int waypointIndex) =>
				sceneName == scene && waypointIndex >= 0 && waypointIndex < 64 && (mask & (1UL << waypointIndex)) != 0;
			public bool Unlock(string sceneName, int waypointIndex) => false;
			public void Restore(string sceneName, int page, ulong pageMask) { }
			public void CollectDirtyPages(List<WaypointPageSnapshot> results) { }
			public void MarkPersisted(string sceneName, int page, ulong writtenMask) { }
		}

		[Test]
		public void AppendWaypoints_DrawsOnlyUnlockedOnes_RegardlessOfFog()
		{
			var controller = new StubWaypoints("Town", 0b10UL);

			WorldSceneDetails details = new WorldSceneDetails();
			details.Waypoints.Add(0, new SceneWaypointDetails() { Index = 0, Name = "Hidden", Position = Vector3.zero });
			details.Waypoints.Add(1, new SceneWaypointDetails() { Index = 1, Name = "Found", Description = "A stone.", Position = new Vector3(10, 0, 10) });

			List<MapMarkerSnapshot> results = new List<MapMarkerSnapshot>();
			MapContent.AppendWaypoints(results, details, "Town", controller, forWorldMap: true);

			Assert.AreEqual(1, results.Count);
			Assert.IsTrue(results[0].IsWaypoint);
			Assert.AreEqual(1, results[0].WaypointIndex);
			Assert.AreEqual(MapMarkerType.Waypoint, results[0].Type);
			Assert.AreEqual("Found", results[0].Label);
			StringAssert.Contains("A stone.", results[0].Tooltip);

			results.Clear();
			MapContent.AppendWaypoints(results, details, "Town", controller, forWorldMap: false);
			Assert.AreEqual(1, results.Count);
			Assert.IsNull(results[0].Label, "The minimap has no room for labels.");

			results.Clear();
			MapContent.AppendWaypoints(results, details, "OtherScene", controller, forWorldMap: true);
			Assert.AreEqual(0, results.Count, "Unlocks are per scene.");

			results.Clear();
			MapContent.AppendWaypoints(results, details, "Town", null, forWorldMap: true);
			Assert.AreEqual(0, results.Count, "No record, nothing drawn.");
		}

		[Test]
		public void Filter_DropsAnyMarkerComponentOfTheWaypointType()
		{
			/* Registered by hand: OnEnable does not run for an edit-mode AddComponent, which is
			 * also how MapSystemTests builds its fixtures. */
			MapMarker marker = NewHost("Leak").AddComponent<MapMarker>();
			marker.Type = MapMarkerType.Waypoint;
			marker.Visibility = MapMarkerVisibility.Always;
			MapMarkerRegistry.Register(marker);

			MapMarker landmark = NewHost("Landmark").AddComponent<MapMarker>();
			landmark.Type = MapMarkerType.Landmark;
			landmark.Visibility = MapMarkerVisibility.Always;
			MapMarkerRegistry.Register(landmark);

			try
			{
				List<MapMarkerSnapshot> results = new List<MapMarkerSnapshot>();
				new MapMarkerFilter().Collect(results, null, true, null);

				bool sawWaypoint = false, sawLandmark = false;
				for (int i = 0; i < results.Count; ++i)
				{
					if (results[i].Source == marker) sawWaypoint = true;
					if (results[i].Source == landmark) sawLandmark = true;
				}
				Assert.IsFalse(sawWaypoint, "A MapMarker of the waypoint type would reveal an undiscovered waypoint to anyone who streamed it.");
				Assert.IsTrue(sawLandmark, "The guard is specific to the waypoint type.");
			}
			finally
			{
				MapMarkerRegistry.Unregister(marker);
				MapMarkerRegistry.Unregister(landmark);
			}
		}

		[Test]
		public void AWaypointIsClassifiedAsALandmark()
		{
			Assert.AreEqual(MapFilterCategory.Landmarks, MapFilters.Categorize(MapMarkerType.Waypoint));
		}
	}
}
