using System;
using NUnit.Framework;
using FishMMO.Server.Implementation;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The token-renewal schedule on world and scene servers: when a renewal comes due again, how
	/// retries back off, and how many renewals one sweep may start.
	/// </summary>
	[TestFixture]
	public class TokenRenewalScheduleTests
	{
		private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

		[Test]
		public void Jitter_OnlyEverBringsARenewalForward()
		{
			LogAssert.AreEqual(Interval, TokenServerAuthenticator.NextRenewalDelay(Interval, 0.2, 0.0), "a zero sample keeps the full interval");
			TimeSpan earliest = TokenServerAuthenticator.NextRenewalDelay(Interval, 0.2, 0.999999);
			LogAssert.IsTrue(earliest < Interval && earliest >= TimeSpan.FromMinutes(4), $"the largest sample brings it at most 20% forward (got {earliest})");
			LogAssert.IsTrue(TokenServerAuthenticator.NextRenewalDelay(Interval, 0.2, 5.0) >= TimeSpan.FromMinutes(4), "an out-of-range sample is clamped");
		}

		[Test]
		public void Jitter_SpreadsAGroupThatRenewedTogether()
		{
			var rng = new Random(1234);
			TimeSpan min = TimeSpan.MaxValue;
			TimeSpan max = TimeSpan.Zero;
			for (int i = 0; i < 1000; i++)
			{
				TimeSpan d = TokenServerAuthenticator.NextRenewalDelay(Interval, 0.2, rng.NextDouble());
				if (d < min) min = d;
				if (d > max) max = d;
			}
			LogAssert.IsTrue(max - min > TimeSpan.FromSeconds(50), $"a thousand connections renewed in one sweep come due again across about a minute, not in one sweep (spread {max - min})");
		}

		[Test]
		public void Retries_DoubleFromTheBase_AndNeverPassTheInterval()
		{
			TimeSpan baseDelay = TimeSpan.FromSeconds(15);
			LogAssert.AreEqual(TimeSpan.FromSeconds(15), TokenServerAuthenticator.RenewalRetryDelay(1, baseDelay, Interval), "first retry waits the base delay");
			LogAssert.AreEqual(TimeSpan.FromSeconds(60), TokenServerAuthenticator.RenewalRetryDelay(3, baseDelay, Interval), "third waits four times it");
			LogAssert.AreEqual(Interval, TokenServerAuthenticator.RenewalRetryDelay(8, baseDelay, Interval), "and the backoff stops at the renewal interval");
		}

		[Test]
		public void StartBudget_AtTheDefaults_StartsAWholeServerWithinHalfTheSlack()
		{
			// A 10-minute token renewed every 5 minutes has 300 s of slack; half of it is 150 s.
			// 5,000 connections is ~83 renewals per 5 s sweep in steady state.
			int budget = TokenServerAuthenticator.RenewalStartBudget(5000, 5.0, 300.0, 2.0, 150.0, 32);
			LogAssert.AreEqual(167, budget, "5,000 × 5 s / 150 s, which is also twice the steady-state rate");
		}

		[Test]
		public void StartBudget_NeverFallsBelowTheMinimum()
		{
			LogAssert.AreEqual(32, TokenServerAuthenticator.RenewalStartBudget(10, 5.0, 300.0, 2.0, 150.0, 32), "a handful of connections still gets the minimum");
			LogAssert.AreEqual(32, TokenServerAuthenticator.RenewalStartBudget(0, 5.0, 300.0, 2.0, 150.0, 32), "and so does none");
		}

		[Test]
		public void StartBudget_SpreadsARestartBurst_ButDrainsItInsideTheSlack()
		{
			// The finding's case: 1,000 players rejoin within 30 s of a scene restart, so ~170 come
			// due in each sweep of that window. They used to start in one frame each sweep.
			int budget = TokenServerAuthenticator.RenewalStartBudget(1000, 5.0, 300.0, 2.0, 150.0, 32);
			LogAssert.IsTrue(budget < 170, $"a sweep no longer starts the whole burst (budget {budget})");
			double drainSeconds = Math.Ceiling(1000.0 / budget) * 5.0;
			LogAssert.IsTrue(drainSeconds <= 150.0, $"yet even all 1,000 due at once start within {drainSeconds}s, inside the 300 s the old tokens have left");
		}

		[Test]
		public void StartBudget_FollowsTheSlack_WhenRenewalsComeLate()
		{
			// renewalRefreshFraction 0.9: renewed at 9 minutes, 60 s before expiry, so half the
			// slack is 30 s. The budget must drain the whole set in that, not in half the interval.
			int budget = TokenServerAuthenticator.RenewalStartBudget(5000, 5.0, 540.0, 2.0, 30.0, 32);
			LogAssert.IsTrue(Math.Ceiling(5000.0 / budget) * 5.0 <= 30.0, $"5,000 due at once start within 30 s (budget {budget})");
		}

		[Test]
		public void StartBudget_FollowsTheSteadyRate_WhenRenewalsComeEarly()
		{
			// renewalRefreshFraction 0.1: renewed every minute, so steady state alone is ~417 per
			// sweep for 5,000 connections; the long slack must not shrink the budget below that.
			int budget = TokenServerAuthenticator.RenewalStartBudget(5000, 5.0, 60.0, 2.0, 270.0, 32);
			LogAssert.IsTrue(budget >= 2 * 5000 * 5 / 60, $"twice the steady-state rate at least (budget {budget})");
		}
	}
}
