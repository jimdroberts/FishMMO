using System;
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.WorldServer;
using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Lookup = FishMMO.Server.Implementation.World.WorldServer.WorldSceneRoutingRules.RowLookup;
using Route = FishMMO.Server.Implementation.World.WorldServer.WorldSceneRoutingRules.InstanceRoute;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the world server's routing decisions: which queued connections a pass takes, when a
	/// placement needs its character row rebound, and where an instanced character goes.
	/// </summary>
	[TestFixture]
	public class WorldSceneRoutingRulesTests
	{
		/// <summary>An arbitrary monotonic-clock origin; only differences mean anything.</summary>
		private const double T0 = 1000.0;
		private const double Grace = 180.0;

		private static WorldSceneRoutingRules.QueueCandidate<string> C(string name, int secondsAfterT0, int clientId, bool held = false)
		{
			return new WorldSceneRoutingRules.QueueCandidate<string>(name, T0 + secondsAfterT0, clientId, held);
		}

		// ── Open-world selection ──────────────────────────────────────────────

		[Test]
		public void QueueWait_ExpiresOnlyWhenTheLineHasStalled()
		{
			const double ttl = 45.0;

			// Nobody placed yet: timed from joining.
			LogAssert.IsFalse(WorldSceneRoutingRules.QueueWaitExpired(100.0, null, 144.0, ttl), "44 s after joining, not yet");
			LogAssert.IsTrue(WorldSceneRoutingRules.QueueWaitExpired(100.0, null, 145.0, ttl), "45 s after joining with nobody placed");

			// The line moved 10 s ago: a player who joined long before keeps their place.
			LogAssert.IsFalse(WorldSceneRoutingRules.QueueWaitExpired(100.0, 290.0, 300.0, ttl), "a moving line never purges its tail");
			LogAssert.IsTrue(WorldSceneRoutingRules.QueueWaitExpired(100.0, 290.0, 335.0, ttl), "45 s without a placement purges");

			// A placement from before this player joined does not count as progress for them.
			LogAssert.IsTrue(WorldSceneRoutingRules.QueueWaitExpired(100.0, 50.0, 145.0, ttl), "an older placement is ignored");
		}

		[Test]
		public void Selection_TakesOnlyAsManyAsThereIsRoomFor_OldestFirst()
		{
			var candidates = new List<WorldSceneRoutingRules.QueueCandidate<string>>
			{
				C("late", 30, 1), C("first", 0, 9), C("second", 10, 2), C("third", 20, 3),
			};
			var selected = new List<string>();
			WorldSceneRoutingRules.SelectForRouting(candidates, 2, selected);

			LogAssert.AreEqual(2, selected.Count,
				"A pass cannot place more than the free capacity, so it must not read and re-queue the rest.");
			LogAssert.AreEqual("first", selected[0]);
			LogAssert.AreEqual("second", selected[1],
				"Oldest first, in the order the queue positions are reported, whatever order the set enumerates in.");
		}

		[Test]
		public void Selection_TiesAreBrokenByClientId()
		{
			var candidates = new List<WorldSceneRoutingRules.QueueCandidate<string>> { C("b", 0, 7), C("a", 0, 3) };
			var selected = new List<string>();
			WorldSceneRoutingRules.SelectForRouting(candidates, 1, selected);
			LogAssert.AreEqual("a", selected[0], "The same total order the position sweep ranks by.");
		}

		[Test]
		public void Selection_NoRoom_TakesOnlyHeldConnections()
		{
			var candidates = new List<WorldSceneRoutingRules.QueueCandidate<string>>
			{
				C("waiting", 0, 1), C("held", 50, 2, held: true),
			};
			var selected = new List<string>();
			WorldSceneRoutingRules.SelectForRouting(candidates, 0, selected);

			LogAssert.AreEqual(1, selected.Count);
			LogAssert.AreEqual("held", selected[0],
				"A combat-logout hold waits on one instance, not on a free slot, and must be looked at every pass.");
		}

		[Test]
		public void Selection_HeldConnectionsDoNotSpendCapacity()
		{
			var candidates = new List<WorldSceneRoutingRules.QueueCandidate<string>>
			{
				C("held", 0, 1, held: true), C("first", 5, 2), C("second", 6, 3),
			};
			var selected = new List<string>();
			WorldSceneRoutingRules.SelectForRouting(candidates, 1, selected);

			LogAssert.AreEqual(2, selected.Count);
			LogAssert.IsTrue(selected.Contains("held") && selected.Contains("first"),
				"A held connection at the front of the queue would otherwise occupy the only slot for up to the whole hold.");
		}

		// ── Scene binds ───────────────────────────────────────────────────────

		[Test]
		public void SceneBind_OnlyWhenTheRowWouldBeRefused()
		{
			LogAssert.IsFalse(WorldSceneRoutingRules.NeedsSceneBind(5, 100, 5, 100), "already bound to this world and instance");
			LogAssert.IsTrue(WorldSceneRoutingRules.NeedsSceneBind(4, 100, 5, 100), "stale world server, as after a world restart");
			LogAssert.IsTrue(WorldSceneRoutingRules.NeedsSceneBind(5, 100, 5, 101), "a different instance");
		}

		[Test]
		public void WorldBind_OnlyForAStaleWorldServer()
		{
			LogAssert.IsFalse(WorldSceneRoutingRules.NeedsWorldBind(5, 5));
			LogAssert.IsTrue(WorldSceneRoutingRules.NeedsWorldBind(0, 5), "never bound since character creation");
			LogAssert.IsTrue(WorldSceneRoutingRules.NeedsWorldBind(4, 5));
			LogAssert.IsFalse(WorldSceneRoutingRules.NeedsWorldBind(4, 0), "an unknown world server id binds nothing");
		}

		// ── Instance route ────────────────────────────────────────────────────

		[Test]
		public void InstanceRoute_ReadyOnALiveServer_Connects()
		{
			LogAssert.AreEqual(Route.Connect,
				WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Found, SceneStatus.Ready, 10, Lookup.Found, true, Grace));
		}

		[Test]
		public void InstanceRoute_AFailedReadIsNeverAnAnswer()
		{
			LogAssert.AreEqual(Route.Retry,
				WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Failed, default, 0, Lookup.Absent, false, Grace),
				"A failed scene read must not release a character whose instance may be alive.");
			LogAssert.AreEqual(Route.Retry,
				WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Found, SceneStatus.Ready, 10, Lookup.Failed, false, Grace),
				"A failed scene-server read must not delete the row of a live, populated instance.");
		}

		[Test]
		public void InstanceRoute_AMissingRowReleases()
		{
			LogAssert.AreEqual(Route.Release,
				WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Absent, default, 0, Lookup.Absent, false, Grace));
		}

		[Test]
		public void InstanceRoute_ReadyOnAGoneOrSilentServer_ReleasesAndDeletesTheRow()
		{
			LogAssert.AreEqual(Route.ReleaseDeletingScene,
				WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Found, SceneStatus.Ready, 10, Lookup.Absent, false, Grace),
				"no longer registered");
			LogAssert.AreEqual(Route.ReleaseDeletingScene,
				WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Found, SceneStatus.Ready, 10, Lookup.Found, false, Grace),
				"registered but no longer pulsing");
		}

		[Test]
		public void InstanceRoute_ALoadIsWaitedOnUntilTheRowIsTooOld()
		{
			foreach (SceneStatus status in new[] { SceneStatus.Pending, SceneStatus.Loading })
			{
				LogAssert.AreEqual(Route.WaitForLoad,
					WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Found, status, Grace - 1, Lookup.Absent, false, Grace),
					$"{status} and young: wait for it");
				LogAssert.AreEqual(Route.Release,
					WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Found, status, Grace, Lookup.Absent, false, Grace),
					$"{status} for the whole grace: it is not coming");
			}
		}

		[Test]
		public void InstanceRoute_AFailedRowReleases()
		{
			LogAssert.AreEqual(Route.Release,
				WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Found, SceneStatus.Failed, 1, Lookup.Absent, false, Grace),
				"Nothing makes a failed load ready.");
			LogAssert.AreEqual(Route.Release,
				WorldSceneRoutingRules.DecideInstanceRoute(Lookup.Found, (SceneStatus)99, 1, Lookup.Absent, false, Grace),
				"...or a status this build does not know.");
		}
	}
}
