using System;
using NUnit.Framework;
using FishMMO.Database.Data;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A daemon command is stamped, claimed, judged expired and completed by the database clock, and
	/// the daemon re-checks expiry on its monotonic clock, never its wall clock.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What was wrong.</b> The Control Panel stamped <c>requested_utc</c> and <c>expires_utc</c>
	/// with its own <c>DateTime.UtcNow</c>. The daemon's claim then kept only rows with
	/// <c>expires_utc &gt; </c> its own stamp, and <c>DaemonControlPlane.ExecuteCommandAsync</c>
	/// re-checked <c>ExpiresUtc &lt;= DateTime.UtcNow</c> on the daemon host. A command lives five
	/// minutes, so a daemon five minutes ahead of the panel saw every command as already expired, and
	/// one running behind executed commands after the operator had stopped waiting. The claim and
	/// completion stamps were the daemon's clock too, so the panel's "waited" and "ran" were the skew
	/// between two machines.
	/// </para>
	/// <para>
	/// Now the enqueue, the claim stamp and the completion read the database clock; the claim judges
	/// expiry against <c>clock_timestamp()</c> and returns the seconds left, which the daemon counts
	/// down from a <c>Stopwatch</c> anchor taken before it sent the claim. The pure rule is
	/// behavioural; the routes are source scans with their own control runs, because no test can step
	/// a host's clock. The SQL was run against a throwaway PostgreSQL, with the previous claim
	/// statement given a daemon stamp five minutes ahead as the control (it claimed nothing), a claim
	/// and a completion each retried after a reply lost past the commit, and a claim whose command
	/// expired between the first attempt and the retry (it comes back with no time left).
	/// </para>
	/// </remarks>
	[TestFixture]
	public class DaemonCommandExpiryTests
	{
		private const string DaemonServicePath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Daemon/DaemonService.cs";
		private const string ControlPlanePath = "../FishMMO-AppHealthMonitor/AppHealthMonitor/DaemonControlPlane.cs";

		// ── The pure rule ───────────────────────────────────────────────────────────────────────

		[Test]
		public void SecondsLeft_IsTheDatabasesMeasurement_LessTheMonotonicTimeSinceTheClaimWasSent()
		{
			LogAssert.AreEqual(290.0, DaemonCommandExpiry.SecondsLeft(300.0, TimeSpan.FromSeconds(10)), "ten seconds into a five-minute command");
			LogAssert.AreEqual(300.0, DaemonCommandExpiry.SecondsLeft(300.0, TimeSpan.Zero), "at the anchor");
			LogAssert.AreEqual(-5.0, DaemonCommandExpiry.SecondsLeft(300.0, TimeSpan.FromSeconds(305)), "past it, negative");
		}

		[Test]
		public void ExactlyZeroLeft_IsExpired_TheClaimsOwnBoundary()
		{
			/* The claim takes a row only while expires_utc > now, so zero left is expired on both
			 * sides of the wire. */
			LogAssert.IsFalse(DaemonCommandExpiry.IsExpired(300.0, TimeSpan.FromSeconds(299.999)), "a millisecond left still runs");
			LogAssert.IsTrue(DaemonCommandExpiry.IsExpired(300.0, TimeSpan.FromSeconds(300)), "none left is expired");
			LogAssert.IsTrue(DaemonCommandExpiry.IsExpired(0.0, TimeSpan.Zero), "a claim that measured zero");
			LogAssert.IsTrue(DaemonCommandExpiry.IsExpired(-0.25, TimeSpan.Zero), "a claim that measured a hair past");
		}

		[Test]
		public void ACommandWithNoMeasuredDeadline_IsNeverRun()
		{
			/* Fail closed: running a restart late is worse than not running it, so a row that did
			 * not come with the database's measurement is refused rather than trusted. */
			LogAssert.IsTrue(DaemonCommandExpiry.IsExpired(null, TimeSpan.Zero), "nothing measured");
			LogAssert.IsTrue(DaemonCommandExpiry.IsExpired(double.NaN, TimeSpan.Zero), "a meaningless measurement");
			LogAssert.IsTrue(double.IsNegativeInfinity(DaemonCommandExpiry.SecondsLeft(null, TimeSpan.Zero)), "no time left to report");
		}

		[Test]
		public void ElapsedTime_NeverExtendsADeadline()
		{
			LogAssert.AreEqual(10.0, DaemonCommandExpiry.SecondsLeft(10.0, TimeSpan.FromSeconds(-5)), "a negative elapsed counts as zero");
		}

		[Test]
		public void ASkewedDaemonClock_IsNotAnInput()
		{
			/* The case that broke: the command was stamped with five minutes to live, and the daemon's
			 * wall clock reads five minutes and a second past the stamp. The old re-check compared the
			 * two and refused; the rule now has no wall-clock parameter at all, so the only inputs are
			 * what the database measured and what the daemon's monotonic clock counted since. */
			DateTime databaseNow = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
			DateTime expiresUtc = databaseNow.AddMinutes(5);
			DateTime daemonWallClock = databaseNow.AddMinutes(5).AddSeconds(1);
			LogAssert.IsTrue(expiresUtc <= daemonWallClock, "the comparison the daemon used to make would refuse this command");

			double measuredAtClaim = (expiresUtc - databaseNow).TotalSeconds;
			LogAssert.IsFalse(DaemonCommandExpiry.IsExpired(measuredAtClaim, TimeSpan.FromMilliseconds(40)),
				"the database's seconds left, counted down on the monotonic clock, run it");
		}

		// ── The routes ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheClaim_JudgesExpiryByTheDatabaseClock_AndReturnsTheSecondsLeft()
		{
			string code = SourceScanPins.ReadCode(DaemonServicePath);
			Func<string, string> check = source =>
			{
				string body = SourceScanPins.Body(source, "public async Task<DatabaseResult<IReadOnlyList<DaemonCommandData>>> ClaimCommandsAsync(");
				if (body == null)
				{
					return "ClaimCommandsAsync was not found";
				}
				if (body.Contains("DateTime.UtcNow"))
				{
					return "the claim reads the daemon host's clock";
				}
				if (!body.Contains("AND ((claimed_utc IS NULL AND expires_utc > {DatabaseUtcClockSql})"))
				{
					return "an unclaimed command's expiry is not judged against the database clock";
				}

				/* A retry's own claims come back whatever the clock says now, so one that expired
				 * between the attempts is refused and reported rather than left claimed with no
				 * outcome. */
				if (!body.Contains("OR (claimed_utc = {{2}} AND claimed_by = {{1}}))\n"))
				{
					return "a retry's own claims are filtered by expiry, so one can be left claimed with no outcome";
				}
				if (!body.Contains("EXTRACT(EPOCH FROM (expires_utc - {DatabaseUtcClockSql}))::double precision"))
				{
					return "the claim does not return the seconds left, measured by the database";
				}

				/* The stamp is the retry identity: read once, before the retried write, and never
				 * re-taken inside it, or a retry after a lost reply could not find its own claims. */
				string order = SourceScanPins.InOrder(body,
					"ReadDatabaseUtcNowAsync(dbContext, cancellationToken)",
					"DateTime claimStampUtc = stamp.Data;",
					"return await ExecuteWriteAsync(");
				if (order != null)
				{
					return order;
				}
				string retried = body.Substring(body.IndexOf("return await ExecuteWriteAsync(", StringComparison.Ordinal));
				return retried.Contains("claimStampUtc =")
					? "the claim stamp is taken again inside the retried delegate"
					: null;
			};

			SourceScanPins.HoldsAndFires("ClaimCommandsAsync expiry", code, check,
				SourceScanPins.Replace("expires_utc > {DatabaseUtcClockSql}", "expires_utc > {{2}}"),
				"expiry judged against the daemon's claim stamp again");
			SourceScanPins.HoldsAndFires("ClaimCommandsAsync own claims", code, check,
				SourceScanPins.RegexReplaceFirst(
					@"AND \(\(claimed_utc IS NULL AND expires_utc > \{DatabaseUtcClockSql\}\)\s*OR \(claimed_utc = \{\{2\}\} AND claimed_by = \{\{1\}\}\)\)",
					"AND (claimed_utc IS NULL OR (claimed_utc = {{2}} AND claimed_by = {{1}}))\n\t\t\t\t\t\t  AND expires_utc > {DatabaseUtcClockSql}"),
				"the expiry filter applied to a retry's own claims as well");
			SourceScanPins.HoldsAndFires("ClaimCommandsAsync stamp clock", code, check,
				SourceScanPins.Replace("DateTime claimStampUtc = stamp.Data;", "DateTime claimStampUtc = DateTime.UtcNow;"),
				"the claim stamped with the daemon host's clock");
			SourceScanPins.HoldsAndFires("ClaimCommandsAsync stamp once", code, check,
				SourceScanPins.InsertBefore("string table = dbContext.GetTableName<DaemonCommandEntity>();",
					"claimStampUtc = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken);\n\t\t\t\t"),
				"the stamp re-read on every attempt, so a retry claims a second set");
			SourceScanPins.HoldsAndFires("ClaimCommandsAsync seconds left", code, check,
				SourceScanPins.Replace("EXTRACT(EPOCH FROM (expires_utc - {DatabaseUtcClockSql}))::double precision", "NULL::double precision"),
				"the seconds left no longer returned");
		}

		[Test]
		public void TheEnqueueAndTheCompletion_AreStampedByTheDatabaseClock()
		{
			string code = SourceScanPins.ReadCode(DaemonServicePath);
			SourceScanPins.HoldsAndFires("EnqueueCommandAsync", code,
				source =>
				{
					string body = SourceScanPins.Body(source, "public async Task<DatabaseResult<long>> EnqueueCommandAsync(");
					if (body == null)
					{
						return "EnqueueCommandAsync was not found";
					}
					if (body.Contains("DateTime.UtcNow"))
					{
						return "the request or its expiry is stamped with the panel's clock";
					}
					return SourceScanPins.InOrder(body,
						".Where(c => c.RequestKey == requestKey)",
						"DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken)",
						"RequestedUtc = now,",
						"ExpiresUtc = now + lifetime,");
				},
				SourceScanPins.Replace("ExpiresUtc = now + lifetime,", "ExpiresUtc = DateTime.UtcNow + lifetime,"),
				"the expiry built on the panel's clock");

			SourceScanPins.HoldsAndFires("CompleteCommandAsync", code,
				source =>
				{
					string body = SourceScanPins.Body(source, "public async Task<DatabaseResult> CompleteCommandAsync(");
					if (body == null)
					{
						return "CompleteCommandAsync was not found";
					}
					if (body.Contains("DateTime.UtcNow"))
					{
						return "the completion is stamped with the daemon host's clock";
					}
					return SourceScanPins.InOrder(body,
						"if (command.CompletedUtc == null)",
						"command.CompletedUtc = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken)");
				},
				SourceScanPins.Replace("command.CompletedUtc = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken).ConfigureAwait(false);",
					"command.CompletedUtc = DateTime.UtcNow;"),
				"the completion stamped with the daemon host's clock");
		}

		[Test]
		public void TheDaemon_ReChecksExpiryOnItsMonotonicClock_AnchoredBeforeTheClaim()
		{
			string code = SourceScanPins.ReadCode(ControlPlanePath);
			Func<string, string> check = source =>
			{
				string execute = SourceScanPins.Body(source, "private async Task<(bool Succeeded, string Outcome)> ExecuteCommandAsync(");
				string poll = SourceScanPins.Body(source, "private async Task<bool> PollCommandsAsync(");
				if (execute == null || poll == null)
				{
					return "ExecuteCommandAsync or PollCommandsAsync was not found";
				}
				if (execute.Contains("DateTime.UtcNow") || execute.Contains("DateTime.Now"))
				{
					return "the re-check reads the daemon host's wall clock";
				}
				if (!execute.Contains("DaemonCommandExpiry.IsExpired(command.ExpiresInSeconds, Stopwatch.GetElapsedTime(claimSentAt))"))
				{
					return "the re-check does not count the database's seconds left down on the monotonic clock";
				}

				/* Anchored before the claim is sent: the database measured somewhere between the send
				 * and the reply, so this can only overstate the time elapsed. */
				return SourceScanPins.InOrder(poll,
					"long claimSentAt = Stopwatch.GetTimestamp();",
					"daemonService.ClaimCommandsAsync(",
					"ExecuteCommandAsync(command, claimSentAt, token)");
			};

			SourceScanPins.HoldsAndFires("ExecuteCommandAsync expiry", code, check,
				SourceScanPins.Replace("DaemonCommandExpiry.IsExpired(command.ExpiresInSeconds, Stopwatch.GetElapsedTime(claimSentAt))",
					"command.ExpiresUtc <= DateTime.UtcNow"),
				"the deadline compared with the daemon's wall clock again");
			SourceScanPins.HoldsAndFires("PollCommandsAsync anchor", code, check,
				s => SourceScanPins.InsertBefore("var commands = claim.Data;", "long claimSentAt = Stopwatch.GetTimestamp();\n\t\t\t")(
					s.Replace("long claimSentAt = Stopwatch.GetTimestamp();", string.Empty)),
				"the anchor taken after the reply, which understates the time elapsed");
		}
	}
}
