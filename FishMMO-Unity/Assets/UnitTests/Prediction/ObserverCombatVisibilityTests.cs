using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// What a third party sees of somebody else's fight, and when.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Observers never resolve hits for themselves — a peer holds every other character
	/// interpolated against its own latency, so its answer would not be the one that counts. It is
	/// TOLD instead, and each of these tests covers a way it used to be told nothing at all.
	/// </para>
	/// <para>
	/// Source-level where the behaviour needs a spawned NetworkObject and a live physics scene to
	/// exercise, which an EditMode test cannot build; direct where the seam is pure arithmetic.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ObserverCombatVisibilityTests
	{
		private const string ObjectPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs";

		private const string ActivationPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs";

		private const string NetworkingPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Networking.cs";

		private const string HitscanPath =
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/Ability/AbilityApplyHitscanAction.cs";

		private const string AreaPath =
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/Ability/AbilityApplyAreaAction.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		[Test]
		public void AHitscanShotAndAnAreaBlastBothPublishTheirImpacts()
		{
			/* The gap this closes. Both actions run their own lag-compensated query and execute the
			 * OnHit chain directly, so neither ever reaches AbilityObject.ApplyHit — the only thing
			 * that published a hit. A third party watching a gunfight saw the beam object appear
			 * and nothing whatever happen to the people it went through: no impact effect, no
			 * decal, no sound. A hitscan is the worst case of the two, having no projectile to
			 * watch in flight either. */
			string hitscan = ReadSource(HitscanPath);
			string area = ReadSource(AreaPath);

			LogAssert.IsTrue(hitscan.Contains("abilityObject.PublishActionHit("),
				"a hitscan shot must tell observers what it hit");
			LogAssert.IsTrue(area.Contains("abilityObject.PublishActionHit("),
				"and so must a blast");

			/* Ahead of the triggers, for the reason ApplyHit gives: an authored action is free to
			 * destroy the object while handling the impact, and the impact still has to be
			 * reported. */
			int publish = hitscan.IndexOf("abilityObject.PublishActionHit(", StringComparison.Ordinal);
			int dispatch = hitscan.IndexOf("trigger?.Execute(collisionEvent)", publish, StringComparison.Ordinal);
			LogAssert.IsTrue(dispatch > publish,
				"the impact is published before the events that may end the object");
		}

		[Test]
		public void AnActionImpactIsNeitherDedupedNorSentToTheOwner()
		{
			/* Two properties that have to travel together, and the reason the message needs a flag
			 * rather than reusing the swept path.
			 *
			 * The per-object hit set is what makes a swept hit free for an owner that predicted it.
			 * An action impact has no such entry on any peer, so the owner — which resolved the
			 * shot itself — would play every impact twice if it were included. And an area effect
			 * wired to OnTick pulses the same victims repeatedly (a lingering trap is authored
			 * exactly that way), so deduping on the receiver would silence every pulse after the
			 * first. */
			string source = ReadSource(ObjectPath);

			int publish = source.IndexOf("internal void PublishActionHit", StringComparison.Ordinal);
			LogAssert.IsTrue(publish >= 0, "the action-hit publisher must exist");
			string body = source.Substring(publish, Math.Min(1600, source.Length - publish));

			LogAssert.IsTrue(body.Contains("BroadcastToObserversExceptOwner"),
				"an action impact goes to observers only — the owner resolved it and has already run the chain");
			LogAssert.IsTrue(body.Contains("DirectImpact = true"),
				"and it is flagged, so the receiver knows not to put it through the hit set");

			int apply = source.IndexOf("internal void ApplyObservedActionHit", StringComparison.Ordinal);
			LogAssert.IsTrue(apply >= 0, "the receiving half must exist");
			string applyBody = source.Substring(apply, Math.Min(700, source.Length - apply));
			LogAssert.IsTrue(applyBody.Contains("RunHitEvents("),
				"it runs the OnHit chain");
			LogAssert.IsFalse(applyBody.Contains("hitTargets"),
				"and touches neither the hit set");
			LogAssert.IsFalse(applyBody.Contains("HitCount"),
				"nor the hit count, both of which belong to the peer that resolved the impact");
		}

		[Test]
		public void ATargetedCastIsStillDrawnByAnObserverThatCannotSeeTheVictim()
		{
			/* The drop this removes. A character outside a client's streaming budget is not in
			 * Objects.Spawned, so the activation handler resolved a null target — and Spawn refused
			 * the whole spawn for any RequiresTarget ability. The bolt was never drawn on that
			 * client, and the destroy message that ended it named an object which had never
			 * existed there.
			 *
			 * The target transform's only use in Spawn is BUILDING the pose, and a reproduction is
			 * handed the pose the server already built from it. */
			string source = ReadSource(ObjectPath);

			LogAssert.IsTrue(
				source.Contains("if (template.RequiresTarget && targetInfo.Target == null && pose == null)"),
				"a supplied pose waives the target requirement, because the pose is what the target was for");

			/* The same waiver is what lets the in-flight block describe a targeted ability, which it
			 * used to skip for exactly the reason that no longer holds. */
			string networking = ReadSource(NetworkingPath);
			int collector = networking.IndexOf("private void CollectInFlightAbilityObjects", StringComparison.Ordinal);
			LogAssert.IsTrue(collector >= 0, "the in-flight collector must still exist");
			string body = networking.Substring(collector, Math.Min(2600, networking.Length - collector));
			LogAssert.IsFalse(body.Contains("RequiresTarget"),
				"a targeted ability in flight must be described to a late observer, not skipped");
		}

		[Test]
		public void AForkTellsObserversWhenItTurnedAndNotJustWhere()
		{
			/* Redirect resets the trajectory leg to zero elapsed ticks and the closed-form pose
			 * reads that counter — so applying the message without advancing it restarted the new
			 * leg from the corner at the moment of arrival, leaving the copy a transit delay behind
			 * for the rest of the object's life. */
			string broadcasts = ReadSource(
				"Assets/Scripts/Shared/Implementation/Network/Character/Prediction/PredictionObserverBroadcasts.cs");
			int redirect = broadcasts.IndexOf("struct AbilityObjectRedirectBroadcast", StringComparison.Ordinal);
			LogAssert.IsTrue(redirect >= 0, "the redirect message must exist");
			string body = broadcasts.Substring(redirect, Math.Min(1800, broadcasts.Length - redirect));
			LogAssert.IsTrue(body.Contains("public uint ServerTick"),
				"the turn's tick must travel, or the new leg cannot be corrected");

			string activation = ReadSource(ActivationPath);
			LogAssert.IsTrue(activation.Contains("ComputeObserverCatchUpTicks(nm.TimeManager, msg.ServerTick)"),
				"and the receiver must advance the new leg by the transit the message spent");

			string source = ReadSource(ObjectPath);
			int apply = source.IndexOf("internal void ApplyObservedRedirect", StringComparison.Ordinal);
			string applyBody = source.Substring(apply, Math.Min(1600, source.Length - apply));
			int noop = applyBody.IndexOf("> 0.99999f", StringComparison.Ordinal);
			int forward = applyBody.IndexOf("FastForward(catchUpTicks)", StringComparison.Ordinal);
			LogAssert.IsTrue(noop >= 0 && forward > noop,
				"a peer already on this heading ran the fork itself and returns before the advance; " +
				"advancing it too would push its copy AHEAD of the server's by the transit");
		}

		[Test]
		public void AReorderedResourceUpdateIsDroppedRatherThanApplied()
		{
			/* Unreliable means unordered. Two pushes sent a few ticks apart can arrive the wrong
			 * way round, and the receiver applied whichever landed last — most likely exactly when
			 * pushes are most frequent, which is mid-fight. The symptom was a health bar that
			 * jumped back up after a hit. */
			LogAssert.IsTrue(CharacterAttributeController.IsNewerObservedSequence(8, 7),
				"the next update is newer");
			LogAssert.IsFalse(CharacterAttributeController.IsNewerObservedSequence(7, 8),
				"one that overtook it on the way is not, and must not be applied");
			LogAssert.IsFalse(CharacterAttributeController.IsNewerObservedSequence(8, 8),
				"and a duplicate is not newer either");

			/* Wrapping, so the counter rolls over without a special case. */
			LogAssert.IsTrue(CharacterAttributeController.IsNewerObservedSequence(0, ushort.MaxValue),
				"the wrap boundary is a difference like any other");
			LogAssert.IsFalse(CharacterAttributeController.IsNewerObservedSequence(ushort.MaxValue, 0),
				"and reads the same way in the other direction");
		}

		[Test]
		public void TheUpdateThatENDSABurstIsTheOneSentReliably()
		{
			/* The stream is self-correcting while a value keeps moving: the next push supersedes
			 * whatever was lost. The settling repeat is the one send with nothing behind it — it
			 * exists because the value has STOPPED changing — so losing it strands every observer
			 * on the last packet that did arrive, and the scheduler has already cleared the pending
			 * confirmation. At the end of a fight that left a corpse standing at half health, for
			 * good. */
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterAttributeController.cs");

			LogAssert.IsTrue(
				source.Contains("decision == ObservedResourcePushScheduler.Decision.Confirm"),
				"the confirmation must be distinguished from an ordinary push at the call site");
			LogAssert.IsTrue(source.Contains("reliable ? Channel.Reliable : Channel.Unreliable"),
				"and it is the confirmation that rides the reliable channel");
		}

		[Test]
		public void AnObserverIsNeverHandedTheOwnersReplicateClock()
		{
			/* The spawn tick is replicate-domain, and that domain belongs to the owning client. On
			 * a third party reproducing the spawn from a broadcast it is a foreign, unsynchronised
			 * counter — and stamping it replicate-domain told ApplyBuffAction it had a
			 * same-character prediction tick, which takes the direct-Apply path. An OnSpawn
			 * self-buff was therefore installed locally, at somebody else's tick, on the one peer
			 * that must never predict. */
			string source = ReadSource(ObjectPath);
			int dispatch = source.IndexOf("private static void DispatchSpawnEvents", StringComparison.Ordinal);
			LogAssert.IsTrue(dispatch >= 0, "the spawn dispatch must still exist");
			string body = source.Substring(dispatch, Math.Min(3000, source.Length - dispatch));

			LogAssert.IsTrue(body.Contains("ResolvesHitsOnThisPeer("),
				"the domain test is the same one the hit path uses: server, or the caster's owner");
			LogAssert.IsTrue(body.Contains("TimeManager.LocalTick"),
				"and an observer is handed its own authoritative tick instead, which routes through " +
				"the target controller's domain mapper like every other cross-peer effect");
		}

		[Test]
		public void ASpawnDispatchIsNotMistakenForAReconcileReplay()
		{
			/* IsReplayTick used to read IsReplicateTick, which says which CLOCK a tick is counted
			 * on and nothing about how many times it has run. Every ability spawn and every
			 * self-target dispatch carries a replicate-domain tick — on the server as much as on
			 * the owner — and each is skipped outright on a replayed tick, so the guard answered
			 * true for precisely the dispatches that are never replays. PlayFXAction refused every
			 * self-buff and self-heal impact effect in the game, on every peer, permanently. */
			TickEventData spawnDispatch = new TickEventData(null, new PredictionTick(1234u));

			LogAssert.IsTrue(spawnDispatch.IsReplicateTick,
				"a spawn dispatch is still replicate-domain — prediction-aware actions depend on that");
			LogAssert.IsFalse(spawnDispatch.IsReplay,
				"but it is not a replay, and a one-shot visual must not be suppressed as though it were");

			TickEventData replay = new TickEventData(null, new PredictionTick(1234u), isReplay: true);
			LogAssert.IsTrue(replay.IsReplay,
				"a dispatch that genuinely re-runs during a reconcile can still say so");
		}
	}
}
