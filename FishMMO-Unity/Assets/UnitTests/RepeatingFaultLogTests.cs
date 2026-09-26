using System;
using NUnit.Framework;
using FishMMO.Server.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="RepeatingFaultLog"/>: what the server logs when one unit of repeating work
	/// (a behaviour's update, a periodic callback, an NPC brain) keeps throwing.
	/// </summary>
	/// <remarks>
	/// Drives <see cref="RepeatingFaultLog.Record"/> with a hand-moved clock, so each assertion
	/// is about the rule, not about a logger.
	/// </remarks>
	[TestFixture]
	public class RepeatingFaultLogTests
	{
		private static InvalidOperationException Boom(string message = "boom") => new InvalidOperationException(message);

		[Test]
		public void TheFirstFailure_IsLoggedInFull()
		{
			var log = new RepeatingFaultLog("Test", "Unit", 10.0);

			LogAssert.AreEqual(RepeatingFaultLog.Decision.LogFull, log.Record(Boom(), 0.0, out int repeats));
			LogAssert.AreEqual(0, repeats);
			LogAssert.AreEqual(1, log.ConsecutiveFailures);
		}

		[Test]
		public void IdenticalRepeats_AreSuppressed_ThenSummarisedOncePerInterval()
		{
			var log = new RepeatingFaultLog("Test", "Unit", 10.0);
			log.Record(Boom(), 0.0, out _);

			// Sixty frames a second for just under ten seconds: every repeat is quiet.
			for (int frame = 1; frame < 600; frame++)
			{
				LogAssert.AreEqual(RepeatingFaultLog.Decision.Suppress, log.Record(Boom(), frame / 60.0, out _));
			}

			// The interval elapses: one summary covering every repeat since the full trace.
			LogAssert.AreEqual(RepeatingFaultLog.Decision.LogSummary, log.Record(Boom(), 10.0, out int repeats));
			LogAssert.AreEqual(600, repeats);

			// And the next one is quiet again.
			LogAssert.AreEqual(RepeatingFaultLog.Decision.Suppress, log.Record(Boom(), 10.01, out _));
			LogAssert.AreEqual(602, log.ConsecutiveFailures);
		}

		[Test]
		public void ADifferentException_IsLoggedInFull_EvenMidRun()
		{
			var log = new RepeatingFaultLog("Test", "Unit", 10.0);
			log.Record(Boom("first"), 0.0, out _);
			log.Record(Boom("first"), 1.0, out _);

			LogAssert.AreEqual(RepeatingFaultLog.Decision.LogFull, log.Record(Boom("second"), 2.0, out _));
			LogAssert.AreEqual(RepeatingFaultLog.Decision.LogFull, log.Record(new ArgumentException("second"), 3.0, out _));
		}

		[Test]
		public void ASuccess_EndsTheRun_AndTheNextFailureIsFullAgain()
		{
			var log = new RepeatingFaultLog("Test", "Unit", 10.0);
			log.Record(Boom(), 0.0, out _);
			log.Record(Boom(), 1.0, out _);

			LogAssert.AreEqual(2, log.RecordSuccess());
			LogAssert.AreEqual(0, log.ConsecutiveFailures);
			LogAssert.AreEqual(0, log.RecordSuccess());

			LogAssert.AreEqual(RepeatingFaultLog.Decision.LogFull, log.Record(Boom(), 2.0, out _));
		}
	}
}
