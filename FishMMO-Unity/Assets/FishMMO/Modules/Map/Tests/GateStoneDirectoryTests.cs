using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Gate spell's list of destinations (<see cref="GateStoneDirectory"/>): only discovered Gate Stones, in every scene, the
	/// character's own scene first.
	/// </summary>
	public class GateStoneDirectoryTests
	{
		/// <summary>An unlock record over any number of scenes (first page only).</summary>
		private sealed class StubWaypoints : IWaypointController
		{
			private readonly Dictionary<string, ulong> masks = new Dictionary<string, ulong>();

			public StubWaypoints With(string scene, params int[] indices)
			{
				masks.TryGetValue(scene, out ulong mask);
				foreach (int index in indices)
				{
					mask |= 1UL << index;
				}
				masks[scene] = mask;
				return this;
			}

			public ICharacter Character => null;
			public bool Initialized => true;
			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
			public WaypointTravelPolicy TravelPolicy => default;
			public void RequestTravel(string sceneName, int waypointIndex) { }
			public IReadOnlyCollection<string> UnlockedScenes => masks.Keys;
			public bool IsUnlocked(string sceneName, int waypointIndex) =>
				sceneName != null && masks.TryGetValue(sceneName, out ulong mask) && waypointIndex >= 0 && waypointIndex < 64 &&
				(mask & (1UL << waypointIndex)) != 0;
			public bool Unlock(string sceneName, int waypointIndex) => false;
			public void Restore(string sceneName, int page, ulong pageMask) { }
			public void CollectDirtyPages(List<WaypointPageSnapshot> results) { }
			public void MarkPersisted(string sceneName, int page, ulong writtenMask) { }
		}

		private static WorldSceneDetails Scene(params (int Index, WaypointKind Kind)[] waypoints)
		{
			var details = new WorldSceneDetails();
			foreach ((int index, WaypointKind kind) in waypoints)
			{
				details.Waypoints[index] = new SceneWaypointDetails() { Index = index, Kind = kind, Name = $"Point {index}" };
			}
			return details;
		}

		private static Dictionary<string, WorldSceneDetails> World()
		{
			return new Dictionary<string, WorldSceneDetails>()
			{
				["Alpha"] = Scene((0, WaypointKind.Waypoint), (3, WaypointKind.GateStone), (1, WaypointKind.GateStone)),
				["Beta"] = Scene((2, WaypointKind.GateStone), (4, WaypointKind.Waypoint)),
				["Gamma"] = Scene((5, WaypointKind.GateStone)),
			};
		}

		[Test]
		public void Collect_ListsOnlyDiscoveredGateStones_CurrentSceneFirst()
		{
			// Discovered: Alpha's waypoint 0 (not a stone), Alpha's stones 1 and 3, Beta's stone 2 and waypoint 4; Gamma's stone is not.
			var waypoints = new StubWaypoints().With("Alpha", 0, 1, 3).With("Beta", 2, 4);
			var results = new List<DiscoveredGateStone>();

			GateStoneDirectory.Collect(World(), waypoints, "Beta", results);

			Assert.AreEqual(3, results.Count);
			Assert.AreEqual(("Beta", 2), (results[0].SceneName, results[0].WaypointIndex), "the character's own scene comes first");
			Assert.AreEqual(("Alpha", 1), (results[1].SceneName, results[1].WaypointIndex));
			Assert.AreEqual(("Alpha", 3), (results[2].SceneName, results[2].WaypointIndex));
			foreach (DiscoveredGateStone stone in results)
			{
				Assert.IsTrue(stone.Details.IsGateStone);
			}
		}

		[Test]
		public void AnyDiscovered_IsFalse_WhenOnlyPlainWaypointsOrNothingIsDiscovered()
		{
			Assert.IsFalse(GateStoneDirectory.AnyDiscovered(World(), new StubWaypoints()));
			Assert.IsFalse(GateStoneDirectory.AnyDiscovered(World(), new StubWaypoints().With("Alpha", 0).With("Beta", 4)));
			Assert.IsTrue(GateStoneDirectory.AnyDiscovered(World(), new StubWaypoints().With("Gamma", 5)));
			Assert.IsFalse(GateStoneDirectory.AnyDiscovered((IDictionary<string, WorldSceneDetails>)null, new StubWaypoints().With("Gamma", 5)));
		}

		[Test]
		public void Collect_ClearsTheList_WithoutARecord()
		{
			var results = new List<DiscoveredGateStone>() { default };
			GateStoneDirectory.Collect(World(), null, "Alpha", results);
			Assert.AreEqual(0, results.Count);
		}
	}
}
