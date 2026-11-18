using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine.SceneManagement;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the waypoint system's invariants: the bit set that is the unlock record, the travel
	/// rules as a truth table, the framed spawn payload, the map's "unlocked or invisible" rule,
	/// and the achievement dirty tracking the waypoint achievements depend on.
	/// </summary>
	[TestFixture]
	public class WaypointTests
	{
		private readonly List<GameObject> hosts = new List<GameObject>();

		/// <summary>Minimal ICharacter for InitializeOnce; the controller never dereferences it here.</summary>
		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id) => ID = id;
			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			/// <inheritdoc/>
			public Nameplate CharacterNameplate { get; set; }
			public Transform MeshRoot => null;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			public void EnableFlags(CharacterFlags flags) => Flags |= 1 << (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(1 << (int)flags);
			public bool IsFlagged(CharacterFlags flags) => (Flags & (1 << (int)flags)) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour b) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour b) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}

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
			WaypointRegistry.Clear();
		}

		private GameObject NewHost(string name)
		{
			GameObject go = new GameObject(name);
			hosts.Add(go);
			return go;
		}

		// ── WaypointUnlockMask ───────────────────────────────────────────────────

		[Test]
		public void Mask_AddThenContains_AndDuplicateIsNotNew()
		{
			WaypointUnlockMask mask = new WaypointUnlockMask();
			Assert.IsTrue(mask.Add(3));
			Assert.IsTrue(mask.Contains(3));
			Assert.IsFalse(mask.Contains(2));
			Assert.IsFalse(mask.Add(3), "A second unlock of the same bit is not new.");
		}

		[Test]
		public void Mask_InvalidIndex_IsRefused()
		{
			WaypointUnlockMask mask = new WaypointUnlockMask();
			Assert.IsFalse(mask.Add(-1));
			Assert.IsFalse(mask.Add(WaypointUnlockMask.MaxIndex + 1));
			Assert.IsFalse(mask.Contains(-1));
			Assert.AreEqual(0, mask.PageCount);
		}

		[Test]
		public void Mask_IndexSixtyFour_LandsOnTheSecondPage()
		{
			WaypointUnlockMask mask = new WaypointUnlockMask();
			Assert.IsTrue(mask.Add(64));
			Assert.AreEqual(2, mask.PageCount);
			Assert.AreEqual(0UL, mask.GetPage(0));
			Assert.AreEqual(1UL, mask.GetPage(1));
			Assert.AreEqual(1, WaypointUnlockMask.PageOf(64));
			Assert.AreEqual(1UL, WaypointUnlockMask.BitOf(64));
			Assert.AreEqual(1UL << 63, WaypointUnlockMask.BitOf(63));
		}

		[Test]
		public void Mask_RestoredBits_AreNotDirty_ButNewBitsAre()
		{
			WaypointUnlockMask mask = new WaypointUnlockMask();
			mask.Restore(0, 0b101UL);
			Assert.IsTrue(mask.Contains(0));
			Assert.IsTrue(mask.Contains(2));
			Assert.IsFalse(mask.IsDirty, "A restored page is what the database already holds.");

			Assert.IsTrue(mask.Add(1));
			Assert.IsTrue(mask.IsDirty);
			Assert.IsTrue(mask.IsPageDirty(0));

			List<WaypointPageSnapshot> dirty = new List<WaypointPageSnapshot>();
			mask.CollectDirtyPages("Scene", dirty);
			Assert.AreEqual(1, dirty.Count);
			Assert.AreEqual("Scene", dirty[0].SceneName);
			Assert.AreEqual(0, dirty[0].Page);
			Assert.AreEqual(0b111UL, dirty[0].Mask, "The snapshot carries the whole page, persisted bits included; the merge is an OR.");
		}

		[Test]
		public void Mask_MarkPersisted_ClearsOnlyTheBitsWritten_SoAnInFlightUnlockStaysDirty()
		{
			WaypointUnlockMask mask = new WaypointUnlockMask();
			mask.Add(0);
			ulong snapshot = mask.GetPage(0);

			// Unlocked while the write was in flight.
			mask.Add(5);

			mask.MarkPersisted(0, snapshot);
			Assert.IsTrue(mask.IsPageDirty(0), "Bit 5 was not in the confirmed write and must remain dirty.");

			List<WaypointPageSnapshot> dirty = new List<WaypointPageSnapshot>();
			mask.CollectDirtyPages("Scene", dirty);
			Assert.AreEqual(1, dirty.Count);

			mask.MarkPersisted(0, dirty[0].Mask);
			Assert.IsFalse(mask.IsDirty);
		}

		[Test]
		public void Mask_MarkPersisted_IgnoresBitsThatAreNotUnlocked()
		{
			WaypointUnlockMask mask = new WaypointUnlockMask();
			mask.Add(1);
			mask.MarkPersisted(0, ~0UL);
			Assert.IsFalse(mask.IsDirty);
			Assert.IsFalse(mask.Contains(2), "Confirming a write never unlocks anything.");

			mask.Add(2);
			Assert.IsTrue(mask.IsDirty, "Bit 2 was not unlocked when the over-broad confirmation arrived, so it must be dirty now.");
		}

		[Test]
		public void Mask_RestoreIsAnOr_NotAnAssignment()
		{
			WaypointUnlockMask mask = new WaypointUnlockMask();
			mask.Add(7);
			mask.Restore(0, 1UL);
			Assert.IsTrue(mask.Contains(7), "A late restore must not drop an unlock made before it arrived.");
			Assert.IsTrue(mask.Contains(0));
			Assert.IsTrue(mask.IsPageDirty(0), "Bit 7 is still unconfirmed.");
		}

		// ── WaypointTravel.Decide ────────────────────────────────────────────────

		[Test]
		public void Policy_RangeIsInclusiveAndClamped()
		{
			WaypointTravelPolicy policy = new WaypointTravelPolicy(true, 10.0f);
			Assert.IsTrue(policy.IsWithinRange(Vector3.zero, new Vector3(10, 0, 0)), "The boundary counts.");
			Assert.IsTrue(policy.IsWithinRange(Vector3.zero, new Vector3(6, 0, 8)));
			Assert.IsFalse(policy.IsWithinRange(Vector3.zero, new Vector3(6, 0, 8.01f)));
			Assert.IsFalse(policy.IsWithinRange(Vector3.zero, new Vector3(0, 10.5f, 0)), "Height counts: a waypoint on the cliff above is not here.");

			Assert.AreEqual(WaypointTravelPolicy.MinimumNearbyRange, new WaypointTravelPolicy(true, 0.0f).NearbyRange);
			Assert.AreEqual(WaypointTravelPolicy.MinimumNearbyRange, new WaypointTravelPolicy(true, -5.0f).NearbyRange);
			Assert.AreEqual(WaypointTravelPolicy.MaximumNearbyRange, new WaypointTravelPolicy(true, 1e9f).NearbyRange);
			Assert.AreEqual(WaypointTravelPolicy.DefaultNearbyRange, new WaypointTravelPolicy(true, float.NaN).NearbyRange);
			Assert.AreEqual(WaypointTravelPolicy.DefaultNearbyRange, new WaypointTravelPolicy(true, float.PositiveInfinity).NearbyRange);
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
		public void MarkerType_WaypointIsAppendedLast_AndClassifiedAsLandmark()
		{
			Assert.AreEqual((byte)MapMarkerType.Note + 1, (byte)MapMarkerType.Waypoint, "The ordinal is draw order; append, never insert.");
			Assert.AreEqual(MapFilterCategory.Landmarks, MapFilters.Categorize(MapMarkerType.Waypoint));
		}

		// ── WaypointRegistry ─────────────────────────────────────────────────────

		[Test]
		public void Registry_RefusesADuplicateIndexInTheSameScene()
		{
			Waypoint first = NewHost("First").AddComponent<Waypoint>();
			Waypoint second = NewHost("Second").AddComponent<Waypoint>();
			SetIndex(first, 4);
			SetIndex(second, 4);

			Assert.IsTrue(WaypointRegistry.Register(first));
			LogAssert.ignoreFailingMessages = true;
			try
			{
				Assert.IsFalse(WaypointRegistry.Register(second), "Two waypoints sharing a bit would let discovering one unlock the other.");
			}
			finally
			{
				LogAssert.ignoreFailingMessages = false;
			}

			Assert.IsTrue(WaypointRegistry.TryGet(first.gameObject.scene.handle, 4, out IWaypoint found));
			Assert.AreSame(first, found);

			WaypointRegistry.Unregister(second);
			Assert.IsTrue(WaypointRegistry.TryGet(first.gameObject.scene.handle, 4, out _), "Unregistering the refused duplicate must not evict the registered one.");

			WaypointRegistry.Unregister(first);
			Assert.IsFalse(WaypointRegistry.TryGet(first.gameObject.scene.handle, 4, out _));
		}

		[Test]
		public void Waypoint_ToDetails_CarriesIdentityAndPosition()
		{
			GameObject host = NewHost("Stone");
			host.transform.position = new Vector3(1, 2, 3);
			Waypoint waypoint = host.AddComponent<Waypoint>();
			SetIndex(waypoint, 9);
			waypoint.WaypointName = "Old Stone";

			SceneWaypointDetails details = waypoint.ToDetails();
			Assert.AreEqual(9, details.Index);
			Assert.AreEqual("Old Stone", details.Name);
			Assert.AreEqual(new Vector3(1, 2, 3), details.Position);
			Assert.AreSame(host.transform, waypoint.ArrivalPoint, "No arrival point authored means the waypoint itself.");
		}

		private static void SetIndex(Waypoint waypoint, int index)
		{
			FieldInfo field = typeof(Waypoint).GetField("waypointIndex", BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.IsNotNull(field);
			field.SetValue(waypoint, index);
		}

		// ── Achievement dirty tracking ───────────────────────────────────────────

		[Test]
		public void Achievement_MarkChanged_AdvancesVersion_AndAStaleConfirmationDoesNotClear()
		{
			Achievement achievement = new Achievement(12345, 0, 0);
			Assert.IsFalse(achievement.PersistenceDirty, "A restored row is clean until something changes it.");

			achievement.MarkChanged();
			Assert.IsTrue(achievement.PersistenceDirty);
			long snapshot = ++achievement.Version; // the save snapshot bumps once more

			achievement.MarkChanged(); // changed while the write was in flight
			achievement.MarkPersisted(snapshot);
			Assert.IsTrue(achievement.PersistenceDirty, "The confirmation is for an older state.");

			achievement.MarkPersisted(achievement.Version);
			Assert.IsFalse(achievement.PersistenceDirty);
		}
	}
}
