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
		public void Decide_TruthTable_InCheckOrder()
		{
			WaypointTravelOptions d = WaypointTravelOptions.Default;
			// canAct, inCombat, sameScene, nearWaypoint, unlocked, conditionsMet
			Assert.AreEqual(WaypointTravelRefusalReason.CannotAct, WaypointTravel.Decide(false, true, false, false, false, false, d));
			Assert.AreEqual(WaypointTravelRefusalReason.InCombat, WaypointTravel.Decide(true, true, false, false, false, false, d));
			Assert.AreEqual(WaypointTravelRefusalReason.NotInScene, WaypointTravel.Decide(true, false, false, false, false, false, d));
			Assert.AreEqual(WaypointTravelRefusalReason.NotNearWaypoint, WaypointTravel.Decide(true, false, true, false, false, false, d));
			Assert.AreEqual(WaypointTravelRefusalReason.Locked, WaypointTravel.Decide(true, false, true, true, false, false, d));
			Assert.AreEqual(WaypointTravelRefusalReason.ConditionsNotMet, WaypointTravel.Decide(true, false, true, true, true, false, d));
			Assert.AreEqual(WaypointTravelRefusalReason.None, WaypointTravel.Decide(true, false, true, true, true, true, d));
		}

		[Test]
		public void Decide_OptionsRelaxPolicyButNeverPhysics()
		{
			WaypointTravelOptions relaxed = new WaypointTravelOptions(requireUnlocked: false, evaluateConditions: false, allowInCombat: true);
			Assert.IsFalse(relaxed.RequireNearbyWaypoint, "A designer-authored move is not bound by the origin rule.");
			Assert.AreEqual(WaypointTravelRefusalReason.None, WaypointTravel.Decide(true, true, true, false, false, false, relaxed));
			Assert.AreEqual(WaypointTravelRefusalReason.CannotAct, WaypointTravel.Decide(false, false, true, true, true, true, relaxed),
				"A dead or unloaded character cannot be moved by any option.");
			Assert.AreEqual(WaypointTravelRefusalReason.NotInScene, WaypointTravel.Decide(true, false, false, true, true, true, relaxed),
				"Same-scene is a fact about where the waypoint is, not a policy.");
		}

		[Test]
		public void PlayerRequestOptions_FollowThePolicy()
		{
			Assert.IsTrue(WaypointTravelOptions.Default.RequireNearbyWaypoint, "Issue #253: the shipped rule is on.");
			Assert.AreEqual(WaypointTravelPolicy.DefaultNearbyRange, WaypointTravelOptions.Default.NearbyRange);

			WaypointTravelOptions off = WaypointTravelOptions.ForPlayerRequest(new WaypointTravelPolicy(false, 25.0f));
			Assert.IsFalse(off.RequireNearbyWaypoint);
			Assert.IsTrue(off.RequireUnlocked && off.EvaluateConditions && !off.AllowInCombat,
				"Turning the origin rule off relaxes nothing else a player request is held to.");
			Assert.AreEqual(WaypointTravelRefusalReason.None, WaypointTravel.Decide(true, false, true, false, true, true, off),
				"With the rule off, being far from every waypoint is not a refusal.");

			WaypointTravelOptions wide = WaypointTravelOptions.ForPlayerRequest(new WaypointTravelPolicy(true, 25.0f));
			Assert.AreEqual(25.0f, wide.NearbyRange);
		}

		// ── WaypointTravelPolicy ─────────────────────────────────────────────────

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
		public void Policy_ClientCheck_NeedsANearbyDiscoveredWaypointInThisScene()
		{
			WaypointController controller = NewHost("Controller").AddComponent<WaypointController>();
			controller.InitializeOnce(new MockCharacter(1));
			controller.Restore("Town", 0, 0b01UL); // 0 discovered, 1 not

			WorldSceneDetails details = new WorldSceneDetails();
			details.Waypoints.Add(0, new SceneWaypointDetails() { Index = 0, Name = "Far", Position = new Vector3(100, 0, 0) });
			details.Waypoints.Add(1, new SceneWaypointDetails() { Index = 1, Name = "Near", Position = Vector3.zero });

			WaypointTravelPolicy on = new WaypointTravelPolicy(true, 10.0f);
			Assert.IsFalse(on.IsSatisfiedBy(details, "Town", controller, Vector3.zero),
				"Standing at a waypoint never activated does not count.");
			Assert.IsTrue(on.IsSatisfiedBy(details, "Town", controller, new Vector3(95, 0, 0)));
			Assert.IsFalse(on.IsSatisfiedBy(details, "Elsewhere", controller, new Vector3(95, 0, 0)), "Discovery is per scene.");
			Assert.IsFalse(on.IsSatisfiedBy(null, "Town", controller, new Vector3(95, 0, 0)), "No cache, no claim.");
			Assert.IsFalse(on.IsSatisfiedBy(details, "Town", null, new Vector3(95, 0, 0)));

			WaypointTravelPolicy off = new WaypointTravelPolicy(false, 10.0f);
			Assert.IsTrue(off.IsSatisfiedBy(null, null, null, Vector3.zero), "The rule off is satisfied everywhere.");
		}

		[Test]
		public void Policy_PayloadRoundTrip()
		{
			foreach (WaypointTravelPolicy sent in new[] { new WaypointTravelPolicy(true, 12.5f), new WaypointTravelPolicy(false, 3.0f), WaypointTravelPolicy.Default })
			{
				Writer writer = new Writer();
				WaypointController.WriteTravelPolicy(writer, sent);
				Reader reader = new Reader(writer.GetArraySegment(), null);
				WaypointTravelPolicy received = WaypointController.ReadTravelPolicy(reader);

				Assert.AreEqual(0, reader.Remaining, "The owner block is framed; the policy must read back exactly what it wrote.");
				Assert.AreEqual(sent.RequireNearbyWaypoint, received.RequireNearbyWaypoint);
				Assert.AreEqual(sent.NearbyRange, received.NearbyRange);
			}
		}

		[Test]
		public void Controller_TravelPolicy_IsTheDefaultUntilTheServerSaysOtherwise()
		{
			WaypointController controller = NewHost("Controller").AddComponent<WaypointController>();
			controller.InitializeOnce(new MockCharacter(1));
			Assert.IsTrue(controller.TravelPolicy.RequireNearbyWaypoint);
			Assert.AreEqual(WaypointTravelPolicy.DefaultNearbyRange, controller.TravelPolicy.NearbyRange);
		}

		// ── WaypointRegistry origin search ───────────────────────────────────────

		[Test]
		public void Registry_TryFindNearest_HonoursRangeDiscoveryAndDistance()
		{
			/* A named preview scene, because edit-mode scenes are untitled and a discovery record
			 * refuses an empty scene name — every discovered-origin assertion would pass or fail
			 * for that reason alone. */
			UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
			try
			{
				scene.name = "WaypointOriginTown";
				Waypoint far = NewHost("Far").AddComponent<Waypoint>();
				Waypoint near = NewHost("Near").AddComponent<Waypoint>();
				Waypoint nearer = NewHost("Nearer").AddComponent<Waypoint>();
				foreach (Waypoint waypoint in new[] { far, near, nearer })
				{
					UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(waypoint.gameObject, scene);
				}
				SetIndex(far, 0);
				SetIndex(near, 1);
				SetIndex(nearer, 2);
				far.transform.position = new Vector3(50, 0, 0);
				near.transform.position = new Vector3(8, 0, 0);
				nearer.transform.position = new Vector3(0, 0, 3);
				Assert.IsTrue(WaypointRegistry.Register(far));
				Assert.IsTrue(WaypointRegistry.Register(near));
				Assert.IsTrue(WaypointRegistry.Register(nearer));

				int handle = scene.handle;
				string sceneName = far.SceneName;
				Assert.IsFalse(string.IsNullOrEmpty(sceneName), "Fixture: the scene needs a name for discovery to mean anything.");

				Assert.IsTrue(WaypointRegistry.TryFindNearest(handle, Vector3.zero, 10.0f, null, out IWaypoint found));
				Assert.AreSame(nearer, found, "The nearest qualifying waypoint is reported.");

				Assert.IsFalse(WaypointRegistry.TryFindNearest(handle, Vector3.zero, 2.0f, null, out found));
				Assert.IsNull(found);

				WaypointController controller = NewHost("Controller").AddComponent<WaypointController>();
				controller.InitializeOnce(new MockCharacter(1));
				controller.Restore(sceneName, 0, 0b011UL); // far and near discovered, nearer not

				Assert.IsTrue(WaypointRegistry.TryFindNearest(handle, Vector3.zero, 10.0f, controller, out found));
				Assert.AreSame(near, found, "An undiscovered waypoint is not an origin, however close.");

				Assert.IsFalse(WaypointRegistry.TryFindNearest(handle, Vector3.zero, 5.0f, controller, out _));
				Assert.IsFalse(WaypointRegistry.TryFindNearest(controller.gameObject.scene.handle, Vector3.zero, 1000.0f, null, out _),
					"Another scene instance's waypoints are never an origin.");
			}
			finally
			{
				WaypointRegistry.Clear();
				UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		// ── WaypointController ───────────────────────────────────────────────────

		[Test]
		public void Controller_UnlockRaisesTheStaticEventOnce_RestoreNever()
		{
			WaypointController controller = NewHost("Controller").AddComponent<WaypointController>();
			controller.InitializeOnce(new MockCharacter(1));

			int raised = 0;
			System.Action<ICharacter, string, int> handler = (c, s, i) => ++raised;
			IWaypointController.OnWaypointUnlocked += handler;
			try
			{
				controller.Restore("Town", 0, 1UL);
				Assert.AreEqual(0, raised, "Restoring persisted bits is not a discovery.");
				Assert.IsTrue(controller.IsUnlocked("Town", 0));

				Assert.IsTrue(controller.Unlock("Town", 1));
				Assert.AreEqual(1, raised);
				Assert.IsFalse(controller.Unlock("Town", 1));
				Assert.AreEqual(1, raised, "Re-unlocking must not re-announce.");

				Assert.IsFalse(controller.Unlock(null, 1));
				Assert.IsFalse(controller.Unlock("Town", -1));
				Assert.AreEqual(1, raised);
			}
			finally
			{
				IWaypointController.OnWaypointUnlocked -= handler;
			}

			List<WaypointPageSnapshot> dirty = new List<WaypointPageSnapshot>();
			controller.CollectDirtyPages(dirty);
			Assert.AreEqual(1, dirty.Count);
			Assert.AreEqual(0b11UL, dirty[0].Mask);

			controller.MarkPersisted("Town", 0, dirty[0].Mask);
			dirty.Clear();
			controller.CollectDirtyPages(dirty);
			Assert.AreEqual(0, dirty.Count);
			Assert.AreEqual(2, controller.CountUnlocked());
		}

		[Test]
		public void Controller_ResetState_DropsTheRecord()
		{
			WaypointController controller = NewHost("Controller").AddComponent<WaypointController>();
			controller.InitializeOnce(new MockCharacter(1));
			controller.Restore("Town", 0, 1UL);
			controller.ResetState(true);
			Assert.IsFalse(controller.IsUnlocked("Town", 0));
			Assert.AreEqual(0, controller.UnlockedScenes.Count);
		}

		[Test]
		public void Controller_NonOwnerPayload_IsAnEmptyFrameThatReadsBackExactly()
		{
			WaypointController sender = NewHost("Sender").AddComponent<WaypointController>();
			sender.InitializeOnce(new MockCharacter(1));
			sender.Restore("Town", 0, 0xFFUL);

			/* An unspawned controller answers "not the owner" for every connection, which is the
			 * filtered direction — so this exercises the observer shape: nothing but the frame
			 * and the shape byte, and the reader must consume exactly that. */
			Writer writer = new Writer();
			sender.WritePayload(null, writer);

			WaypointController receiver = NewHost("Receiver").AddComponent<WaypointController>();
			receiver.InitializeOnce(new MockCharacter(2));
			receiver.Restore("Elsewhere", 0, 1UL);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			receiver.ReadPayload(null, reader);

			Assert.AreEqual(0, reader.Remaining, "The framed block must be consumed exactly; anything else desyncs every behaviour after it.");
			Assert.AreEqual(5, writer.Length, "Four framing bytes plus the shape byte: an observer learns nothing.");
			Assert.IsTrue(receiver.IsUnlocked("Elsewhere", 0), "An empty observer block must not clear what the receiver already holds.");
			Assert.IsFalse(receiver.IsUnlocked("Town", 0));
		}

		// ── MapContent ───────────────────────────────────────────────────────────

		[Test]
		public void AppendWaypoints_DrawsOnlyUnlockedOnes_RegardlessOfFog()
		{
			WaypointController controller = NewHost("Controller").AddComponent<WaypointController>();
			controller.InitializeOnce(new MockCharacter(1));
			controller.Restore("Town", 0, 0b10UL);

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
