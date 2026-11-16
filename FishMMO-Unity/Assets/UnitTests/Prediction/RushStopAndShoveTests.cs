using NUnit.Framework;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the two "it does not feel predicted" fixes: a charge stopped by a predicted hit stays stopped through the owner's
	/// replays (<see cref="PredictedRushStops"/>), and the attacker draws a knockback's slide at once and hands it over to the
	/// server's (<see cref="CharacterHitReaction.ShoveLead"/>).
	/// </summary>
	[TestFixture]
	public class RushStopAndShoveTests
	{
		// ── The rush stop survives a replay ──────────────────────────────────────────────────

		[Test]
		public void AReplayPassingTheHitStopsThereAgain()
		{
			PredictedRushStops stops = new PredictedRushStops();
			stops.Record(100); // the owner's hit stopped the rush after tick 100
			// A correction for tick 95 (before the hit), the server still rushing: nothing to do yet, the replay will pass 100.
			Assert.That(stops.OnReconciled(95, serverRushing: true), Is.False);
			Assert.That(stops.StopsAfter(100), Is.True, "the replay of tick 100 stops the rush again");
			Assert.That(stops.StopsAfter(99) || stops.StopsAfter(101), Is.False);
		}

		[Test]
		public void ACorrectionPastTheHit_WithTheServerStillRushing_StopsNow()
		{
			PredictedRushStops stops = new PredictedRushStops();
			stops.Record(100);
			// The server's hit is a tick or two behind the owner's: its state for 101 still rushes. The owner must not run on.
			Assert.That(stops.OnReconciled(101, serverRushing: true), Is.True);
			Assert.That(stops.Count, Is.EqualTo(1), "kept until the server confirms");
		}

		[Test]
		public void TheServerConfirming_ForgetsTheStop()
		{
			PredictedRushStops stops = new PredictedRushStops();
			stops.Record(100);
			Assert.That(stops.OnReconciled(102, serverRushing: false), Is.False);
			Assert.That(stops.Count, Is.Zero);
		}

		[Test]
		public void AStopTheServerNeverMade_Expires_AndTheServerWins()
		{
			PredictedRushStops stops = new PredictedRushStops();
			stops.Record(100);
			Assert.That(stops.OnReconciled(100 + PredictedRushStops.ExpiryTicks + 1, serverRushing: true), Is.False);
			Assert.That(stops.Count, Is.Zero);
		}

		[Test]
		public void StopsAreBounded_AndNotDuplicated()
		{
			PredictedRushStops stops = new PredictedRushStops();
			stops.Record(5);
			stops.Record(5);
			Assert.That(stops.Count, Is.EqualTo(1));
			for (uint t = 10; t < 30; ++t)
			{
				stops.Record(t);
			}
			Assert.That(stops.Count, Is.EqualTo(PredictedRushStops.Capacity));
			Assert.That(stops.StopsAfter(5), Is.False, "the oldest went first");
		}

		[Test]
		public void TheCancelKey_StopsARush_InsideTheReplicate()
		{
			string source = System.IO.File.ReadAllText(ProjectFiles.Path(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs"));
			int interrupt = source.IndexOf("private bool ProcessInterrupt(");
			int next = source.IndexOf("private bool TryStartAbility(", interrupt);
			Assert.That(interrupt, Is.GreaterThan(0));
			string body = source.Substring(interrupt, next - interrupt);
			Assert.That(body.Contains("CharacterController.EndMotion()"), Is.True,
				"an interrupt ends a rush in progress, in the replicate, so replays (whose input carries the flag) stop it too");
		}

		// ── The predicted shove ──────────────────────────────────────────────────────────────

		[Test]
		public void TheShove_LeadsBySlideTheRootHasNotMade_AndHandsOver()
		{
			const float speed = 8f, deceleration = 24f, catchUp = 0.6f;
			float stop = speed / deceleration;              // a third of a second
			float slide = speed * speed / (2f * deceleration); // 1.33 m

			Assert.That(CharacterHitReaction.ShoveLead(speed, deceleration, 0f, 0f, catchUp), Is.Zero, "starts at the root");
			float half = stop * 0.5f;
			float predictedHalf = speed * half - 0.5f * deceleration * half * half;
			Assert.That(CharacterHitReaction.ShoveLead(speed, deceleration, half, 0f, catchUp), Is.EqualTo(predictedHalf).Within(1e-4f),
				"the server has not moved it yet: the model carries the whole slide so far");
			Assert.That(CharacterHitReaction.ShoveLead(speed, deceleration, stop, 0.5f, catchUp), Is.EqualTo(slide - 0.5f).Within(1e-4f),
				"half a metre of it has arrived: the model leads by the rest");
			Assert.That(CharacterHitReaction.ShoveLead(speed, deceleration, stop + 0.1f, slide, catchUp), Is.Zero,
				"all of it has arrived: no lead, no doubling");
			Assert.That(CharacterHitReaction.ShoveLead(speed, deceleration, stop + 0.1f, slide + 0.4f, catchUp), Is.Zero,
				"the server moved it further: never a lead backwards");
			Assert.That(CharacterHitReaction.ShoveLead(speed, deceleration, stop, -2f, catchUp), Is.EqualTo(slide).Within(1e-4f),
				"walking against it: the lead is never more than the slide");
			Assert.That(CharacterHitReaction.ShoveLead(speed, deceleration, stop + catchUp, 0f, catchUp), Is.Zero,
				"the server never moved it (refused): the lead has faded out");
			Assert.That(CharacterHitReaction.ShoveLead(speed, deceleration, stop + catchUp * 0.5f, 0f, catchUp), Is.EqualTo(slide * 0.5f).Within(1e-4f),
				"and fades, rather than snapping");
		}
	}
}
