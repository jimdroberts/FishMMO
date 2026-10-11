using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.UnitTests.Harness;

namespace FishMMO.UnitTests.PointsOfInterest
{
	/// <summary>
	/// Pins portal activation (Jim, 2026-10-10): the decision as a truth table over scope ×
	/// conditions × stored state, the store's loaded/dirty bookkeeping, and a use through
	/// <see cref="PortalGate.Use"/> on a real component.
	/// </summary>
	[TestFixture]
	public class PortalActivationTests
	{
		private const long T0 = 1_800_000_000_000L;
		private readonly List<GameObject> hosts = new List<GameObject>();

		/// <summary>A condition with a fixed answer, counting how often it was asked.</summary>
		[Serializable]
		private sealed class FixedCondition : BaseCondition
		{
			public bool Answer;
			public int Calls;
			public override bool Evaluate(ICharacter initiator, EventData eventData = null)
			{
				Calls++;
				return Answer;
			}
		}

		/// <summary>
		/// A NAMED scene for the portals: the store keys activations by scene name and ignores an empty one, and the runner's
		/// own scene is untitled. A preview scene opened from a path takes that scene's name (NewPreviewScene's is empty).
		/// </summary>
		private const string NamedScenePath = "Assets/Scenes/WorldScene/Tutorial Regions.unity";
		private Scene named;

		[SetUp]
		public void SetUp()
		{
			named = EditorSceneManager.OpenPreviewScene(NamedScenePath);
		}

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject host in hosts)
			{
				if (host != null)
				{
					UnityEngine.Object.DestroyImmediate(host);
				}
			}
			hosts.Clear();
			if (named.IsValid())
			{
				EditorSceneManager.ClosePreviewScene(named);
			}
			PortalRegistry.Clear();
			PortalClientStates.Clear();
		}

		private static PortalActivationStore Store(bool assumeLoaded, long now = T0)
		{
			return new PortalActivationStore(assumeLoaded) { Clock = () => now };
		}

		private PortalActivation Portal(int index, PortalActivationScope scope, float seconds = 600.0f, params BaseCondition[] conditions)
		{
			var host = new GameObject($"Portal {index}");
			SceneManager.MoveGameObjectToScene(host, named);
			hosts.Add(host);
			PortalActivation portal = host.AddComponent<PortalActivation>();
			portal.Configure(index, scope, seconds);
			portal.Conditions.Clear();
			portal.Conditions.AddRange(conditions);
			return portal;
		}

		#region Rules

		[Test]
		public void Decide_IsTheTruthTable()
		{
			foreach (bool hasRequirements in new[] { false, true })
			{
				foreach (bool condition in new[] { false, true })
				{
					foreach (bool key in new[] { false, true })
					{
						Assert.AreEqual(PortalActivationVerdict.NotReady, PortalActivationRules.Decide(null, hasRequirements, condition, key), "unknown is never closed");
						Assert.AreEqual(PortalActivationVerdict.Open, PortalActivationRules.Decide(true, hasRequirements, condition, key), "open ignores requirements");

						PortalActivationVerdict expected = !hasRequirements || condition
							? PortalActivationVerdict.Activate
							: key ? PortalActivationVerdict.ActivateWithKey : PortalActivationVerdict.Locked;
						Assert.AreEqual(expected, PortalActivationRules.Decide(false, hasRequirements, condition, key),
							$"closed, requirements={hasRequirements}, condition={condition}, key={key}");
					}
				}
			}
		}

		[Test]
		public void IsActive_ReadsTheRecordTheScopeNames()
		{
			var timed = new PortalWorldState(false, T0 + 1000);
			var expired = new PortalWorldState(false, T0 - 1);
			var permanent = new PortalWorldState(true, 0);

			// Per character: only the character's record counts, world openings are ignored.
			Assert.IsNull(PortalActivationRules.IsActive(PortalActivationScope.PerCharacter, null, permanent, T0));
			Assert.AreEqual(false, PortalActivationRules.IsActive(PortalActivationScope.PerCharacter, false, permanent, T0));
			Assert.AreEqual(true, PortalActivationRules.IsActive(PortalActivationScope.PerCharacter, true, null, T0));

			foreach (PortalActivationScope scope in new[] { PortalActivationScope.WorldTimed, PortalActivationScope.WorldPermanent })
			{
				Assert.IsNull(PortalActivationRules.IsActive(scope, true, null, T0), $"{scope}: unloaded scene");
				Assert.AreEqual(false, PortalActivationRules.IsActive(scope, true, default(PortalWorldState), T0), $"{scope}: no row, own record ignored");
				Assert.AreEqual(true, PortalActivationRules.IsActive(scope, null, timed, T0), $"{scope}: timed, running");
				Assert.AreEqual(false, PortalActivationRules.IsActive(scope, null, expired, T0), $"{scope}: timed, run out");
				Assert.AreEqual(true, PortalActivationRules.IsActive(scope, null, permanent, T0), $"{scope}: permanent");
			}
		}

		[Test]
		public void WorldStateFor_WritesPermanentOrAClampedDeadline()
		{
			Assert.AreEqual(new PortalWorldState(true, 0), PortalActivationRules.WorldStateFor(PortalActivationScope.WorldPermanent, 5, T0));
			Assert.AreEqual(new PortalWorldState(false, T0 + 600_000), PortalActivationRules.WorldStateFor(PortalActivationScope.WorldTimed, 600, T0));
			Assert.AreEqual(new PortalWorldState(false, T0 + (long)(PortalActivationRules.MinTimedSeconds * 1000)),
				PortalActivationRules.WorldStateFor(PortalActivationScope.WorldTimed, 0, T0), "too short is clamped up");
			Assert.AreEqual(default(PortalWorldState), PortalActivationRules.WorldStateFor(PortalActivationScope.PerCharacter, 600, T0));
		}

		[Test]
		public void WorldState_MergesCoversAndCountsDown()
		{
			var a = new PortalWorldState(false, T0 + 5000);
			var b = new PortalWorldState(true, T0 + 1000);
			PortalWorldState merged = PortalWorldState.Merge(a, b);
			Assert.IsTrue(merged.Permanent);
			Assert.AreEqual(T0 + 5000, merged.ActiveUntilUnixMs);
			Assert.IsTrue(merged.Covers(a) && merged.Covers(b));
			Assert.IsFalse(a.Covers(merged), "a timed opening does not cover a permanent one");

			Assert.AreEqual(5, a.RemainingSeconds(T0), "rounded up");
			Assert.AreEqual(1, a.RemainingSeconds(T0 + 4001));
			Assert.AreEqual(0, a.RemainingSeconds(T0 + 5000));
			Assert.AreEqual(PortalActivationRules.NoExpiry, b.RemainingSeconds(T0));
		}

		#endregion

		#region Store

		[Test]
		public void Store_UnloadedCharacterIsUnknownUntilMarkedLoaded()
		{
			PortalActivationStore store = Store(assumeLoaded: false);
			Assert.IsNull(store.IsCharacterActive(7, "Scene", 3));

			store.RestoreCharacterPage(7, "Scene", 0, 1UL << 3);
			Assert.IsNull(store.IsCharacterActive(7, "Scene", 3), "restored rows do not count until the read is complete");

			store.MarkCharacterLoaded(7);
			Assert.AreEqual(true, store.IsCharacterActive(7, "Scene", 3));
			Assert.AreEqual(false, store.IsCharacterActive(7, "Scene", 4));

			var pages = new List<WaypointPageSnapshot>();
			store.CollectDirtyPages(7, pages);
			Assert.AreEqual(0, pages.Count, "restored bits are already persisted");
		}

		[Test]
		public void Store_CharacterActivationIsDirtyUntilConfirmed_AndOutlivesRelease()
		{
			PortalActivationStore store = Store(assumeLoaded: false);
			int raised = 0;
			store.CharacterActivated += (id, scene, index) => raised++;
			store.MarkCharacterLoaded(9);

			Assert.IsTrue(store.ActivateForCharacter(9, "Scene", 70));
			Assert.IsFalse(store.ActivateForCharacter(9, "Scene", 70), "already active");
			Assert.AreEqual(1, raised);

			var pages = new List<WaypointPageSnapshot>();
			store.CollectDirtyPages(9, pages);
			Assert.AreEqual(1, pages.Count);
			Assert.AreEqual(1, pages[0].Page, "index 70 lives on page 1");
			Assert.AreEqual(1UL << 6, pages[0].Mask);

			// Leaving with the write unconfirmed keeps the record, unloaded, for the flush.
			store.ReleaseCharacter(9);
			Assert.AreEqual(1, store.CharacterRecordCount);
			Assert.IsNull(store.IsCharacterActive(9, "Scene", 70));
			var dirty = new List<long>();
			store.CollectDirtyCharacters(dirty);
			CollectionAssert.AreEqual(new[] { 9L }, dirty);

			store.MarkCharacterPersisted(9, "Scene", 1, 1UL << 6);
			Assert.AreEqual(0, store.CharacterRecordCount, "a released record goes once it is clean");
		}

		[Test]
		public void Store_ReloadOrsTheDatabaseIntoUnconfirmedBits()
		{
			PortalActivationStore store = Store(assumeLoaded: false);
			store.MarkCharacterLoaded(4);
			store.ActivateForCharacter(4, "Scene", 1);
			store.ReleaseCharacter(4);

			// Back again before the flush landed: the database knows bit 2 (another server), memory still holds bit 1.
			store.RestoreCharacterPage(4, "Scene", 0, 1UL << 2);
			store.MarkCharacterLoaded(4);
			Assert.AreEqual(true, store.IsCharacterActive(4, "Scene", 1));
			Assert.AreEqual(true, store.IsCharacterActive(4, "Scene", 2));

			var pages = new List<WaypointPageSnapshot>();
			store.CollectDirtyPages(4, pages);
			Assert.AreEqual(1, pages.Count, "bit 1 is still owed to the database");
		}

		[Test]
		public void Store_WorldOpeningsMergeAndConfirmOnlyWhenCovered()
		{
			PortalActivationStore store = Store(assumeLoaded: false);
			Assert.IsNull(store.WorldState("Scene", 0), "scene rows not read");

			List<int> grew = store.RestoreWorldScene("Scene", new List<(int, PortalWorldState)> { (0, new PortalWorldState(false, T0 + 1000)) });
			CollectionAssert.AreEqual(new[] { 0 }, grew);
			Assert.AreEqual(default(PortalWorldState), store.WorldState("Scene", 5), "a loaded scene with no row is closed, not unknown");
			Assert.AreEqual(0, store.UnconfirmedWorldCount, "rows read are not owed");

			int raised = 0;
			store.WorldActivated += (scene, index, state) => raised++;
			var first = new PortalWorldState(false, T0 + 60_000);
			Assert.IsTrue(store.ActivateForWorld("Scene", 0, first));
			Assert.IsFalse(store.ActivateForWorld("Scene", 0, new PortalWorldState(false, T0 + 30_000)), "a shorter opening changes nothing");
			Assert.AreEqual(1, raised);

			// Opened for good while the timed write is in flight: the timed confirmation must not clear it.
			Assert.IsTrue(store.ActivateForWorld("Scene", 0, new PortalWorldState(true, 0)));
			store.MarkWorldPersisted("Scene", 0, first);
			Assert.AreEqual(1, store.UnconfirmedWorldCount);

			var pending = new List<(string, int, PortalWorldState)>();
			store.CollectDirtyWorld(pending);
			Assert.AreEqual(1, pending.Count);
			store.MarkWorldPersisted("Scene", 0, pending[0].Item3);
			Assert.AreEqual(0, store.UnconfirmedWorldCount);

			// A refresh that brings nothing new reports nothing.
			grew = store.RestoreWorldScene("Scene", new List<(int, PortalWorldState)> { (0, first) });
			Assert.AreEqual(0, grew.Count);
		}

		[Test]
		public void MemoryOnlyStore_TreatsEverythingAsLoaded()
		{
			PortalActivationStore store = Store(assumeLoaded: true);
			Assert.AreEqual(false, store.IsCharacterActive(1, "Scene", 0));
			Assert.AreEqual(default(PortalWorldState), store.WorldState("Scene", 0));
		}

		#endregion

		#region Gate

		[Test]
		public void Use_AnUnconfiguredPortalOpensOnFirstUse_ThenStaysOpen()
		{
			PortalActivationStore store = Store(assumeLoaded: true);
			PortalActivation portal = Portal(2, PortalActivationScope.PerCharacter);
			var character = new StubCharacter { ID = 11 };

			Assert.IsFalse(portal.HasRequirements);
			Assert.AreEqual(PortalActivationVerdict.Activate, PortalGate.Use(character, portal, null, store, out string message));
			Assert.IsNotEmpty(message);
			Assert.AreEqual(true, store.IsCharacterActive(11, portal.SceneName, 2));
			Assert.AreEqual(PortalActivationVerdict.Open, PortalGate.Use(character, portal, null, store, out message));
			Assert.IsNull(message, "travelling through an open portal says nothing");

			// Per character: someone else still finds it closed (and opens it for themselves).
			Assert.AreEqual(false, store.IsCharacterActive(12, portal.SceneName, 2));
		}

		[Test]
		public void Use_AnyOneConditionOpens_AndConditionsAreNotAskedOnceOpen()
		{
			PortalActivationStore store = Store(assumeLoaded: true);
			var no = new FixedCondition { Answer = false };
			var yes = new FixedCondition { Answer = true };
			PortalActivation locked = Portal(0, PortalActivationScope.PerCharacter, 600, no);
			var character = new StubCharacter { ID = 3 };

			Assert.AreEqual(PortalActivationVerdict.Locked, PortalGate.Use(character, locked, null, store, out string message));
			Assert.AreEqual(locked.LockedMessage, message);
			Assert.AreEqual(false, store.IsCharacterActive(3, locked.SceneName, 0), "a refusal activates nothing");

			PortalActivation anyOf = Portal(1, PortalActivationScope.PerCharacter, 600, no, null, yes);
			Assert.AreEqual(PortalActivationVerdict.Activate, PortalGate.Use(character, anyOf, null, store, out _));
			int asked = yes.Calls;
			Assert.AreEqual(PortalActivationVerdict.Open, PortalGate.Use(character, anyOf, null, store, out _));
			Assert.AreEqual(asked, yes.Calls, "an open portal does not re-run its conditions");
		}

		[Test]
		public void Use_WorldTimedOpensForEveryone_ThenClosesAgain()
		{
			long now = T0;
			var store = new PortalActivationStore(assumeLoaded: true) { Clock = () => now };
			var gate = new FixedCondition { Answer = true };
			PortalActivation portal = Portal(4, PortalActivationScope.WorldTimed, 120, gate);

			Assert.AreEqual(PortalActivationVerdict.Activate, PortalGate.Use(new StubCharacter { ID = 1 }, portal, null, store, out _));
			gate.Answer = false;
			Assert.AreEqual(PortalActivationVerdict.Open, PortalGate.Use(new StubCharacter { ID = 2 }, portal, null, store, out _), "open for a second player");

			now = T0 + 120_000;
			Assert.AreEqual(PortalActivationVerdict.Locked, PortalGate.Use(new StubCharacter { ID = 2 }, portal, null, store, out _), "closed when the time runs out");
		}

		[Test]
		public void Use_NotLoadedRefusesAndAsksForTheRecord()
		{
			PortalActivationStore store = Store(assumeLoaded: false);
			long requestedCharacter = -1;
			string requestedScene = "unset";
			store.LoadRequested += (id, scene) => { requestedCharacter = id; requestedScene = scene; };

			PortalActivation mine = Portal(0, PortalActivationScope.PerCharacter);
			Assert.AreEqual(PortalActivationVerdict.NotReady, PortalGate.Use(new StubCharacter { ID = 8 }, mine, null, store, out string message));
			Assert.AreEqual(PortalGate.NotReadyMessage, message);
			Assert.AreEqual(8L, requestedCharacter);
			Assert.IsNull(requestedScene);

			PortalActivation shared = Portal(1, PortalActivationScope.WorldPermanent);
			Assert.AreEqual(PortalActivationVerdict.NotReady, PortalGate.Use(new StubCharacter { ID = 8 }, shared, null, store, out _));
			Assert.AreEqual(0L, requestedCharacter);
			Assert.AreEqual(shared.SceneName, requestedScene);
		}

		[Test]
		public void Registry_RefusesADuplicateIndexInOneScene()
		{
			// Registration is OnEnable's job at runtime; an EditMode AddComponent never runs it, so it is called here.
			PortalActivation first = Portal(6, PortalActivationScope.PerCharacter);
			PortalActivation second = Portal(6, PortalActivationScope.PerCharacter);
			Assert.IsTrue(PortalRegistry.Register(first));
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				Assert.IsFalse(PortalRegistry.Register(second));
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}
			Assert.IsTrue(PortalRegistry.TryGet(first.gameObject.scene.handle, 6, out PortalActivation found));
			Assert.AreSame(first, found);

			PortalRegistry.Unregister(first);
			Assert.IsFalse(PortalRegistry.TryGet(first.gameObject.scene.handle, 6, out _));
		}

		#endregion

		[Test]
		public void ClientStates_ReplaceAScene_AndApplyOneChange()
		{
			PortalClientStates.ApplyScene("Scene", new ushort[] { 1, 2 }, new[] { PortalActivationRules.NoExpiry, 60 });
			Assert.IsTrue(PortalClientStates.IsActive("Scene", 1));
			Assert.IsTrue(float.IsPositiveInfinity(PortalClientStates.SecondsLeft("Scene", 1)));
			Assert.That(PortalClientStates.SecondsLeft("Scene", 2), Is.InRange(59.0f, 60.0f));

			PortalClientStates.ApplyScene("Scene", new ushort[] { 3 }, new[] { PortalActivationRules.NoExpiry });
			Assert.IsFalse(PortalClientStates.IsActive("Scene", 1), "a full report replaces the scene");

			PortalClientStates.Apply("Scene", 4, 30);
			Assert.IsTrue(PortalClientStates.IsActive("Scene", 4));
			PortalClientStates.Apply("Scene", 4, 0);
			Assert.IsFalse(PortalClientStates.IsActive("Scene", 4));
		}
	}
}
