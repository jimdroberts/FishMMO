using FishMMO.Server.Implementation.World.SceneServer;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins how a scene server rations the pending-scene queue against its own load.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Placement is a race: <c>ISceneService.DequeueAsync</c> is <c>FOR UPDATE SKIP LOCKED</c>
	/// take-the-oldest, so before this policy existed the winner was whichever server's pulse timer
	/// fired first — load played no part, and a server that came up first could accumulate scenes
	/// while an idle peer took none. The budget is the only thing that makes an idle peer win.
	/// </para>
	/// <para>
	/// The properties are static and shared, so every test restores the defaults in
	/// <see cref="SetUp"/> rather than trusting the previous one to have left them alone.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class SceneServerPlacementPolicyTests
	{
		private const int MaxPerPulse = 3;

		[SetUp]
		public void SetUp()
		{
			SceneServerPlacementPolicy.SoftCapScenes = 4;
			SceneServerPlacementPolicy.HardCapScenes = 12;
			SceneServerPlacementPolicy.SoftCapCharacters = 200;
			SceneServerPlacementPolicy.HardCapCharacters = 600;
		}

		[TearDown]
		public void TearDown()
		{
			SetUp();
		}

		/// <summary>An idle server must claim the full configured budget.</summary>
		[Test]
		public void IdleServer_TakesTheFullBudget()
		{
			LogAssert.AreEqual(MaxPerPulse,
				SceneServerPlacementPolicy.ResolveDequeueBudget(0, 0, MaxPerPulse),
				"An empty scene server must take as much work as it is allowed to. Throttling here " +
				"would leave scenes queued while the cluster sat idle.");
		}

		/// <summary>A server at its soft cap must still be at full budget.</summary>
		/// <remarks>
		/// The soft cap is where tapering BEGINS, not where it has already taken effect. Off-by-one
		/// here silently costs a third of the cluster's load-in rate.
		/// </remarks>
		[Test]
		public void AtTheSoftCap_IsStillFullBudget()
		{
			LogAssert.AreEqual(MaxPerPulse,
				SceneServerPlacementPolicy.ResolveDequeueBudget(
					SceneServerPlacementPolicy.SoftCapScenes, 0, MaxPerPulse),
				"The soft cap is the point tapering starts from, so it must not itself be tapered.");
		}

		/// <summary>Between the caps the budget must fall, without reaching zero.</summary>
		[Test]
		public void BetweenTheCaps_TapersButNeverToZero()
		{
			int previous = MaxPerPulse;
			for (int scenes = SceneServerPlacementPolicy.SoftCapScenes;
				 scenes < SceneServerPlacementPolicy.HardCapScenes;
				 ++scenes)
			{
				int budget = SceneServerPlacementPolicy.ResolveDequeueBudget(scenes, 0, MaxPerPulse);

				LogAssert.IsTrue(budget >= 1,
					$"At {scenes} scenes the budget was {budget}. Below the hard cap the server still " +
					"has room, and a zero budget there is indistinguishable from being full — it would " +
					"stop taking work with capacity to spare.");
				LogAssert.IsTrue(budget <= previous,
					$"The budget rose from {previous} to {budget} at {scenes} scenes; it must be monotonic " +
					"or a server would claim MORE work as it filled up.");
				previous = budget;
			}
		}

		/// <summary>At the hard cap the server must claim nothing.</summary>
		[Test]
		public void AtTheHardCap_TakesNothing()
		{
			LogAssert.AreEqual(0,
				SceneServerPlacementPolicy.ResolveDequeueBudget(
					SceneServerPlacementPolicy.HardCapScenes, 0, MaxPerPulse),
				"A full server must leave the row queued for a peer with room.");
			LogAssert.AreEqual(0,
				SceneServerPlacementPolicy.ResolveDequeueBudget(
					0, SceneServerPlacementPolicy.HardCapCharacters, MaxPerPulse),
				"The character cap must refuse work on its own, not only alongside the scene cap.");
		}

		/// <summary>
		/// Population must throttle a server that is hosting very few scenes.
		/// </summary>
		/// <remarks>
		/// This is the case scene count alone gets wrong: one heavily populated town is a single
		/// scene, so a scene-count-only policy would keep handing that server more work. Measuring
		/// the MORE loaded of the two is what catches it.
		/// </remarks>
		[Test]
		public void PopulationThrottlesAServerHostingFewScenes()
		{
			int budget = SceneServerPlacementPolicy.ResolveDequeueBudget(
				loadedScenes: 1, characterCount: 550, maxScenesPerPulse: MaxPerPulse);

			LogAssert.IsTrue(budget < MaxPerPulse,
				$"One scene holding 550 characters is a loaded server, but the budget was {budget}. " +
				"Scene count alone is not load — a single busy town outweighs several empty dungeons.");
		}

		/// <summary>An idle server must not be throttled by a peer's shape of load.</summary>
		[Test]
		public void ManyEmptyScenes_StillThrottleOnSceneCount()
		{
			int budget = SceneServerPlacementPolicy.ResolveDequeueBudget(
				loadedScenes: 10, characterCount: 0, maxScenesPerPulse: MaxPerPulse);

			LogAssert.IsTrue(budget < MaxPerPulse,
				$"Ten loaded scenes cost memory and tick time even when empty, but the budget was {budget}.");
		}

		/// <summary>
		/// A hard cap misconfigured at or below the soft cap must throttle, not divide by zero.
		/// </summary>
		[Test]
		public void InvertedCaps_ThrottleRatherThanCrash()
		{
			SceneServerPlacementPolicy.SoftCapScenes = 10;
			SceneServerPlacementPolicy.HardCapScenes = 10;

			LogAssert.AreEqual(0,
				SceneServerPlacementPolicy.ResolveDequeueBudget(10, 0, MaxPerPulse),
				"At the hard cap the answer is zero regardless of how the caps are ordered.");
			LogAssert.AreEqual(MaxPerPulse,
				SceneServerPlacementPolicy.ResolveDequeueBudget(9, 0, MaxPerPulse),
				"Below both caps the server is not loaded and must take a full budget.");
		}

		/// <summary>A non-positive ceiling must yield no work rather than a negative loop bound.</summary>
		[Test]
		public void NonPositiveCeiling_YieldsNoWork()
		{
			LogAssert.AreEqual(0, SceneServerPlacementPolicy.ResolveDequeueBudget(0, 0, 0),
				"A zero ceiling must produce a zero budget.");
			LogAssert.AreEqual(0, SceneServerPlacementPolicy.ResolveDequeueBudget(0, 0, -5),
				"A negative ceiling must produce a zero budget, not a negative one.");
		}

		/// <summary>Configuration keys must apply, and unknown keys must be reported as unknown.</summary>
		[Test]
		public void ApplySetting_AppliesKnownKeysAndRejectsOthers()
		{
			LogAssert.IsTrue(SceneServerPlacementPolicy.ApplySetting("PlacementSoftCapScenes", 7),
				"PlacementSoftCapScenes is a documented key.");
			LogAssert.AreEqual(7, SceneServerPlacementPolicy.SoftCapScenes, "The value must be applied.");

			LogAssert.IsFalse(SceneServerPlacementPolicy.ApplySetting("PlacementNonsense", 1),
				"An unknown key must report false so the caller can warn rather than silently ignoring a typo.");
		}

		/// <summary>A hard cap of zero must be clamped, or the server would never take work.</summary>
		[Test]
		public void ApplySetting_ClampsAHardCapOfZero()
		{
			SceneServerPlacementPolicy.ApplySetting("PlacementHardCapScenes", 0);

			LogAssert.IsTrue(SceneServerPlacementPolicy.HardCapScenes >= 1,
				"A hard cap of zero would refuse every scene forever — the cluster would accept no work " +
				"at all and the queue would grow without bound.");
		}
	}
}
