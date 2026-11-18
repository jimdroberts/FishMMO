using System;
using NUnit.Framework;
using FishMMO.Server.Core.LoginServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Verdict = FishMMO.Server.Core.LoginServer.IpAbuseTracker.AttemptVerdict;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="IpAbuseTracker"/>: account creation's per-IP rate limit and failure block.
	/// </summary>
	[TestFixture]
	public class IpAbuseTrackerTests
	{
		private static readonly TimeSpan RateLimit = TimeSpan.FromSeconds(5);
		private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);
		private const int MaxFailures = 5;
		private const string Ip = "198.51.100.7";
		/// <summary>A monotonic reading in seconds; only differences matter.</summary>
		private const double T0 = 5000.0;
		private const double Minute = 60.0;
		private static readonly double RetentionSeconds = Retention.TotalSeconds;

		private IpAbuseTracker tracker;

		[SetUp]
		public void SetUp()
		{
			tracker = new IpAbuseTracker();
		}

		private Verdict Attempt(double now) => tracker.TryBeginAttempt(Ip, now, RateLimit, MaxFailures, Retention);

		private void Fail(int times, double now)
		{
			for (int i = 0; i < times; i++)
			{
				LogAssert.IsTrue(tracker.TryRecordFailure(Ip, now, Retention, 50_000), "the failure is recorded");
			}
		}

		[Test]
		public void Attempts_AreRateLimitedPerIp()
		{
			LogAssert.AreEqual(Verdict.Accepted, Attempt(T0), "the first attempt proceeds");
			LogAssert.AreEqual(Verdict.RateLimited, Attempt(T0 + 4), "one inside the rate limit is refused");
			LogAssert.AreEqual(Verdict.Accepted, Attempt(T0 + 5), "one at the limit proceeds");
		}

		[Test]
		public void ARefusedAttempt_DoesNotMoveTheRateLimit()
		{
			Attempt(T0);
			Attempt(T0 + 4);
			LogAssert.AreEqual(Verdict.Accepted, Attempt(T0 + 5), "the limit still counts from the last accepted attempt");
		}

		[Test]
		public void EnoughFailures_BlockTheIp_UntilTheBlockDurationPassesSinceTheLastFailure()
		{
			Attempt(T0);
			Fail(MaxFailures, T0 + 1);

			LogAssert.AreEqual(Verdict.Blocked, Attempt(T0 + Minute), "blocked at the threshold");
			LogAssert.IsTrue(tracker.IsBlocked(Ip, T0 + Minute, MaxFailures, Retention), "and reported blocked");

			// Hammering a block does not keep it alive: blocked attempts are not recorded.
			for (int i = 2; i < 5; i++)
			{
				Attempt(T0 + i * Minute);
			}

			double lapse = T0 + 1 + RetentionSeconds;
			LogAssert.IsTrue(tracker.IsBlocked(Ip, lapse - 0.001, MaxFailures, Retention), "still blocked a millisecond before it lapses");
			LogAssert.IsFalse(tracker.IsBlocked(Ip, lapse, MaxFailures, Retention), "the block lapses the block duration after the last failure");
			LogAssert.AreEqual(Verdict.Accepted, Attempt(lapse), "and the IP may try again");
		}

		[Test]
		public void AVerificationFailureWithNoAttempt_LastsTheBlockDuration()
		{
			// The verification path records failures without a creation attempt. Those used to be
			// dropped by whichever minute's sweep reached them first.
			Fail(MaxFailures, T0);
			tracker.SweepExpired(T0 + Minute, Retention, int.MaxValue);
			LogAssert.IsTrue(tracker.IsBlocked(Ip, T0 + Minute, MaxFailures, Retention), "a sweep a minute later leaves the block in place");
			LogAssert.IsFalse(tracker.IsBlocked(Ip, T0 + RetentionSeconds, MaxFailures, Retention), "it lapses with the block duration");
		}

		[Test]
		public void ASuccess_ClearsFailures_ButKeepsTheRateLimit()
		{
			Attempt(T0);
			Fail(MaxFailures - 1, T0);
			tracker.ClearFailures(Ip);

			Fail(MaxFailures - 1, T0 + 1);
			LogAssert.IsFalse(tracker.IsBlocked(Ip, T0 + 1, MaxFailures, Retention), "the count restarted from zero");
			LogAssert.AreEqual(Verdict.RateLimited, Attempt(T0 + 2), "the attempt record survives the success");
		}

		[Test]
		public void TheFailureCap_CountsOnlyFailingIps_AndAnIpAlreadyFailingIsAlwaysCounted()
		{
			tracker.TryBeginAttempt("a", T0, RateLimit, MaxFailures, Retention);
			LogAssert.IsTrue(tracker.TryRecordFailure("b", T0, Retention, 1), "one failing IP fits a cap of one; an attempt-only record does not count");
			LogAssert.IsFalse(tracker.TryRecordFailure("c", T0, Retention, 1), "a second failing IP is refused, and the caller fails closed");
			LogAssert.IsTrue(tracker.TryRecordFailure("b", T0, Retention, 1), "an IP already failing keeps counting at the cap");
			LogAssert.AreEqual(1, tracker.FailingCount, "one IP is failing");
		}

		[Test]
		public void Sweep_ReachesEveryLapsedRecord_AndStopsAtTheFirstLiveOne()
		{
			for (int i = 0; i < 300; i++)
			{
				tracker.TryBeginAttempt($"10.0.{i / 256}.{i % 256}", T0, RateLimit, MaxFailures, Retention);
			}
			for (int i = 0; i < 50; i++)
			{
				tracker.TryBeginAttempt($"10.9.0.{i}", T0 + 4 * Minute, RateLimit, MaxFailures, Retention);
			}

			double now = T0 + RetentionSeconds;
			int removed = 0;
			for (int pass = 0; pass < 5; pass++)
			{
				removed += tracker.SweepExpired(now, Retention, 128);
			}

			LogAssert.AreEqual(300, removed, "every lapsed record was reached, however many live ones exist");
			LogAssert.AreEqual(50, tracker.Count, "and the live ones were left");
		}
	}
}
