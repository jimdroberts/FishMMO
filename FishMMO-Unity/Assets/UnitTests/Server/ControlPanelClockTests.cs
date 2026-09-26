using System;
using NUnit.Framework;
using FishMMO.Database.Npgsql.Services;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Maintenance windows and the daemon board judge time by the database clock, never by the
	/// Control Panel's or a daemon host's.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What was wrong.</b> A maintenance window planned its deadline as the panel's
	/// <c>DateTime.UtcNow</c> plus the drain and derived every status from the panel's "now", while
	/// the servers count that deadline down against the database clock and the database stamps their
	/// pulses; the panel then aged each pulse and the countdown on its own clock too. The daemon
	/// heartbeat was stamped with the daemon host's clock, inside <c>DaemonService</c>, and aged
	/// against the panel's. In both, the skew between two machines decided what an operator was told:
	/// a panel a minute fast called every healthy server silent, and one running slow gave players
	/// less drain than was asked for. The server shutdowns were already fixed this way
	/// (<c>ServerControlSql</c>, <c>ServerBoardService</c>); these follow them.
	/// </para>
	/// <para>
	/// The pure rules are behavioural; the routes are source scans with their own control runs,
	/// because no test can step a host's clock. The SQL itself was run against a throwaway
	/// PostgreSQL (start, list, fetch, cancel, a zero-second drain, heartbeats, ages, eight
	/// overlapping beats for one host).
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ControlPanelClockTests
	{
		private const string MaintenanceServicePath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Maintenance/MaintenanceService.cs";
		private const string DaemonServicePath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Daemon/DaemonService.cs";
		private const string MaintenanceControllerPath = "../FishMMO-WebServers/FishMMO-ControlPanel/ControlPanel/Controllers/MaintenanceController.cs";
		private const string DaemonControllerPath = "../FishMMO-WebServers/FishMMO-ControlPanel/ControlPanel/Controllers/DaemonController.cs";
		private const string MaintenanceViewPath = "../FishMMO-WebServers/FishMMO-ControlPanel/ControlPanel/wwwroot/js/views/servers-maintenance.js";
		private const string DaemonViewPath = "../FishMMO-WebServers/FishMMO-ControlPanel/ControlPanel/wwwroot/js/views/daemon-hosts.js";

		// ── The pure rules ──────────────────────────────────────────────────────────────────────

		[Test]
		public void APulseAge_IsTheGapBetweenTwoDatabaseInstants_NeverNegative()
		{
			DateTime now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
			LogAssert.AreEqual(10.0, MaintenanceService.AgeSeconds(now, now.AddSeconds(-10)), "ten seconds old");
			LogAssert.AreEqual(0.0, MaintenanceService.AgeSeconds(now, now), "stamped this instant");
			LogAssert.AreEqual(0.0, MaintenanceService.AgeSeconds(now, now.AddMilliseconds(5)),
				"a pulse that committed after the clock was read is not in the future: it is fresh");
		}

		[Test]
		public void ASilentServer_IsOneStrictlyPastTheThreshold()
		{
			LogAssert.AreEqual(60, MaintenanceService.StaleAfterSeconds, "the server board's threshold, so the two surfaces agree");
			LogAssert.IsFalse(MaintenanceService.IsSilent(0.0), "a fresh pulse");
			LogAssert.IsFalse(MaintenanceService.IsSilent(MaintenanceService.StaleAfterSeconds), "a pulse exactly at the threshold is still pulsing");
			LogAssert.IsTrue(MaintenanceService.IsSilent(MaintenanceService.StaleAfterSeconds + 0.001), "past it, silent");
		}

		// ── The routes ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheMaintenanceService_ReadsNoClockButTheDatabases()
		{
			string code = SourceScanPins.ReadCode(MaintenanceServicePath);
			SourceScanPins.HoldsAndFires("MaintenanceService", code,
				source => source.Contains("DateTime.UtcNow")
					? "a maintenance decision or stamp reads this process's clock"
					: source.Contains("DateTime deadline = now.AddSeconds(drainSeconds);") &&
						SourceScanPins.InOrder(source, "DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken)", "DateTime deadline = now.AddSeconds(drainSeconds);") == null
						? null
						: "the window's deadline is not planned on the database clock",
				SourceScanPins.Replace("DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken).ConfigureAwait(false);\n\t\t\t\tDateTime deadline",
					"DateTime now = DateTime.UtcNow;\n\t\t\t\tDateTime deadline"),
				"the plan's deadline built on the panel's clock again");

			SourceScanPins.HoldsAndFires("ToData", code,
				source =>
				{
					string body = SourceScanPins.Body(source, "private static MaintenanceOperationData ToData(");
					return body != null &&
						body.Contains("SecondsUntilDeadline = (operation.DeadlineUtc - databaseNowUtc).TotalSeconds") &&
						body.Contains("AgeSeconds(databaseNowUtc, t.ObservedLastPulseUtc.Value)")
						? null
						: "the reply's countdown or pulse age is not measured at the database's read instant";
				},
				SourceScanPins.Replace("AgeSeconds(databaseNowUtc, t.ObservedLastPulseUtc.Value)", "AgeSeconds(DateTime.UtcNow, t.ObservedLastPulseUtc.Value)"),
				"a pulse age taken on the panel's clock");
		}

		[Test]
		public void TheMaintenanceController_TakesItsAgesFromTheService()
		{
			string code = SourceScanPins.ReadCode(MaintenanceControllerPath);
			SourceScanPins.HoldsAndFires("MaintenanceController", code,
				source =>
				{
					if (source.Contains("DateTime.UtcNow"))
					{
						return "the controller reads the panel's clock";
					}
					string body = SourceScanPins.Body(source, "private static object Project(MaintenanceOperationData operation)");
					return body != null &&
						body.Contains("Math.Round(operation.SecondsUntilDeadline, 1)") &&
						body.Contains("MaintenanceService.IsSilent(t.ObservedPulseAgeSeconds.Value)")
						? null
						: "the countdown or the not-pulsing flag is not the service's database-measured age";
				},
				SourceScanPins.Replace("Math.Round(operation.SecondsUntilDeadline, 1)", "Math.Round((operation.DeadlineUtc - DateTime.UtcNow).TotalSeconds, 1)"),
				"the countdown measured on the panel's clock");
		}

		[Test]
		public void TheDaemonHeartbeat_IsStampedAndAgedByTheDatabase()
		{
			string service = SourceScanPins.ReadCode(DaemonServicePath);
			SourceScanPins.HoldsAndFires("HeartbeatAsync", service,
				source =>
				{
					string body = SourceScanPins.Body(source, "public async Task<DatabaseResult> HeartbeatAsync(");
					if (body == null)
					{
						return "HeartbeatAsync was not found";
					}
					if (body.Contains("DateTime.UtcNow"))
					{
						return "the heartbeat is stamped with the daemon host's clock";
					}

					/* Every stamp after the host lock comes from a clock read taken under it, so two
					 * overlapping beats stamp the history in the order they compared. */
					int locked = body.IndexOf("FOR UPDATE", StringComparison.Ordinal);
					int read = locked < 0 ? -1 : body.IndexOf("\n\t\t\t\tnow = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken)", locked, StringComparison.Ordinal);
					if (read < 0)
					{
						return "the database clock is not read once the host row is locked";
					}
					return SourceScanPins.InOrder(body.Substring(read),
						"hostRow.LastHeartbeatUtc = now;",
						"row.LastReportedUtc = now;",
						"ObservedUtc = now,");
				},
				SourceScanPins.InsertBefore("var existing = await dbContext.DaemonApps", "now = DateTime.UtcNow;\n\t\t\t\t"),
				"the beat restamped with the daemon host's clock after the lock");

			SourceScanPins.HoldsAndFires("FetchHostsAsync", service,
				source =>
				{
					string body = SourceScanPins.Body(source, "public async Task<DatabaseResult<IReadOnlyList<DaemonHostData>>> FetchHostsAsync(");
					return body != null &&
						body.Contains("AgeSecondsSql(\"h.last_heartbeat_utc\")") &&
						body.Contains("AgeSecondsSql(\"a.last_reported_utc\")") &&
						body.Contains("IsolationLevel.RepeatableRead")
						? null
						: "the host and app ages are not taken in SQL, in one snapshot";
				},
				SourceScanPins.Replace("AgeSecondsSql(\"h.last_heartbeat_utc\")", "0.0"),
				"the heartbeat age left for the reader to work out");

			string controller = SourceScanPins.ReadCode(DaemonControllerPath);
			SourceScanPins.HoldsAndFires("DaemonController.Hosts", controller,
				source =>
				{
					string body = SourceScanPins.Body(source, "public async Task<IActionResult> Hosts()");
					if (body == null)
					{
						return "Hosts() was not found";
					}
					if (body.Contains("DateTime.UtcNow"))
					{
						return "the board ages a heartbeat on the panel's clock";
					}
					return body.Contains("Math.Round(h.HeartbeatAgeSeconds, 1)") && body.Contains("IsHeartbeatStale(h.HeartbeatAgeSeconds)")
						? null
						: "the board does not use the database-measured heartbeat age";
				},
				SourceScanPins.Replace("IsHeartbeatStale(h.HeartbeatAgeSeconds)", "(DateTime.UtcNow - h.LastHeartbeatUtc).TotalSeconds > StaleAfterSeconds"),
				"the stale flag judged on the panel's clock");
		}

		[Test]
		public void TheViews_CountDownAndAgeFromTheDatabasesMeasurements()
		{
			string maintenance = SourceScanPins.ReadCode(MaintenanceViewPath);
			SourceScanPins.HoldsAndFires("countdownSpan", maintenance,
				source =>
				{
					string anchor = SourceScanPins.Body(source, "function anchorDeadline(operation, readAt)");
					string span = SourceScanPins.Body(source, "function countdownSpan(operation)");
					if (anchor == null || span == null)
					{
						return "anchorDeadline or countdownSpan was not found";
					}
					if (!anchor.Contains("readAt + seconds * 1000"))
					{
						return "the deadline is not anchored on the database's seconds at the read";
					}
					return span.Contains("Date.parse") ? "the countdown reads the browser's clock against the deadline stamp" : null;
				},
				SourceScanPins.Replace("const at = Number(operation?.deadlineAt);", "const at = Date.parse(operation?.deadlineUtc);"),
				"the countdown back on the browser's clock");

			string daemon = SourceScanPins.ReadCode(DaemonViewPath);
			SourceScanPins.HoldsAndFires("daemon-hosts heartbeat", daemon,
				source => source.Contains("ui.ago(h.lastHeartbeatUtc)") || source.Contains("ui.ago(a.lastReportedUtc)")
					? "a heartbeat is aged on the browser's clock"
					: null,
				SourceScanPins.Replace("agoByDatabase(ui, h.heartbeatAgeSeconds, h.lastHeartbeatUtc)", "ui.ago(h.lastHeartbeatUtc)"),
				"the heartbeat shown by the browser's clock");
		}
	}
}
