using System;
using NUnit.Framework;
using FishMMO.Database.Data;
using FishMMO.Server.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A scheduled shutdown counts down on the process's monotonic clock from the time remaining
	/// the database measured, never by comparing the row's instant with the host's wall clock
	/// (optional change O25).
	/// </summary>
	/// <remarks>
	/// Driven with arbitrary monotonic readings: nothing may depend on the clock's origin. The
	/// source pins that keep <c>DateTime.UtcNow</c> out of the servers' countdowns live in
	/// <see cref="WorldRoutingClockTests"/>, beside the routing ones.
	/// </remarks>
	[TestFixture]
	public class ShutdownCountdownTests
	{
		private static readonly DateTime At = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
		private static readonly DateTime Later = At.AddMinutes(10);

		[Test]
		public void NothingScheduled_IsNeverDue()
		{
			var countdown = new ShutdownCountdown();
			LogAssert.IsFalse(countdown.IsScheduled, "fresh");
			LogAssert.IsFalse(countdown.IsDue(1e12), "never due");
			LogAssert.IsFalse(countdown.Adopt(null, null, 5.0), "an empty reading of an empty schedule is not a change");
		}

		[Test]
		public void ASchedule_IsAnchoredWhereTheReplyArrived_PlusWhatTheDatabaseMeasured()
		{
			var countdown = new ShutdownCountdown();
			LogAssert.IsTrue(countdown.Adopt(At, 300.0, 1000.0), "a new schedule is a change");
			LogAssert.IsTrue(Math.Abs(countdown.Deadline - 1300.0) < 1e-9, $"deadline {countdown.Deadline}");
			LogAssert.IsTrue(Math.Abs(countdown.SecondsRemaining(1100.0) - 200.0) < 1e-9, "200 s left 100 s later");
			LogAssert.IsFalse(countdown.IsDue(1299.9), "not yet");
			LogAssert.IsTrue(countdown.IsDue(1300.0), "due at the deadline");
			LogAssert.AreEqual(0.0, countdown.SecondsRemaining(2000.0), "never negative");
		}

		[Test]
		public void LaterReadingsOfTheSameSchedule_OnlyEverTightenTheDeadline()
		{
			var countdown = new ShutdownCountdown();
			countdown.Adopt(At, 300.0, 1000.0);

			LogAssert.IsFalse(countdown.Adopt(At, 294.0, 1010.0), "the same schedule is not a change");
			LogAssert.IsTrue(Math.Abs(countdown.Deadline - 1300.0) < 1e-9,
				"a reply that took longer to arrive anchors later; the earlier anchor stands");

			countdown.Adopt(At, 289.0, 1010.0);
			LogAssert.IsTrue(Math.Abs(countdown.Deadline - 1299.0) < 1e-9,
				"a reading that places it earlier is the better estimate: every reading is late by its own round trip, never early");
		}

		[Test]
		public void ARescheduledShutdown_StartsANewCountdown_EvenALaterOne()
		{
			var countdown = new ShutdownCountdown();
			countdown.Adopt(At, 60.0, 1000.0);

			LogAssert.IsTrue(countdown.Adopt(Later, 660.0, 1001.0), "a different instant is a new schedule");
			LogAssert.AreEqual(Later, countdown.ScheduledAtUtc, "identity follows the row");
			LogAssert.IsTrue(Math.Abs(countdown.Deadline - 1661.0) < 1e-9, "and a moved-back deadline is honoured, not merged");
		}

		[Test]
		public void ACancelledShutdown_ClearsTheCountdown()
		{
			var countdown = new ShutdownCountdown();
			countdown.Adopt(At, 60.0, 1000.0);

			LogAssert.IsTrue(countdown.Adopt(null, null, 1001.0), "cancelling is a change");
			LogAssert.IsFalse(countdown.IsScheduled, "nothing scheduled");
			LogAssert.IsFalse(countdown.IsDue(1e12), "and nothing due");
		}

		[Test]
		public void ADeadlineAlreadyPassed_IsDueAtOnce()
		{
			var countdown = new ShutdownCountdown();
			countdown.Adopt(At, -5.0, 1000.0);
			LogAssert.IsTrue(countdown.IsDue(1000.0), "the database said it passed five seconds ago");
		}

		[Test]
		public void AScheduleWithNoMeasurement_NeverFallsDue()
		{
			var countdown = new ShutdownCountdown();
			LogAssert.IsTrue(countdown.Adopt(At, null, 1000.0), "it is still a schedule");
			LogAssert.IsTrue(countdown.IsScheduled, "scheduled");
			LogAssert.IsFalse(countdown.IsDue(1e12), "but nobody measured it, and stopping a server on a guess is the wrong way to fail");

			countdown.Adopt(At, 30.0, 2000.0);
			LogAssert.IsTrue(countdown.IsDue(2030.0), "the next measured reading anchors it");
		}

		[Test]
		public void TheClockOrigin_DoesNotMatter()
		{
			var early = new ShutdownCountdown();
			var late = new ShutdownCountdown();
			early.Adopt(At, 120.0, 0.0);
			late.Adopt(At, 120.0, 9_000_000.0);
			LogAssert.AreEqual(early.SecondsRemaining(30.0), late.SecondsRemaining(9_000_030.0), "only differences between readings mean anything");
		}

		[Test]
		public void AControlState_CarriesACountdownOnlyWithASchedule()
		{
			var unscheduled = new ServerControlState(false, null, 42.0);
			LogAssert.IsNull(unscheduled.ShutdownInSeconds, "a measurement without a schedule is dropped");
			LogAssert.IsFalse(unscheduled.HasShutdown, "and there is no shutdown");

			var scheduled = new ServerControlState(true, At, 90.0);
			var countdown = new ShutdownCountdown();
			LogAssert.IsTrue(countdown.Adopt(new ServerControlReading(scheduled, 500.0)), "a reading adopts as its parts do");
			LogAssert.IsTrue(Math.Abs(countdown.Deadline - 590.0) < 1e-9, $"deadline {countdown.Deadline}");
			LogAssert.IsFalse(countdown.Adopt(null), "no reading changes nothing");
		}
	}
}
