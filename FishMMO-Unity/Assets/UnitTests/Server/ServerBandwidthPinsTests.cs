using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the shape of server bandwidth recording: the SET-not-ADD upserts, the key fixed
	/// before the retried write, the transaction-level rollup lock, roll-before-delete retention,
	/// and the wiring into all three server tiers and the Control Panel.
	/// </summary>
	/// <remarks>
	/// Source scans, like their neighbours. The failures they prevent — an additive upsert that
	/// double-counts a retried write, a clock read inside the retry loop, a session-level advisory
	/// lock left on a pooled connection that is never reset — compile, pass every unit test, and
	/// show up only as wrong totals on a busy shard. The SQL itself was run against a throwaway
	/// PostgreSQL when it was written, including a control with an additive upsert that failed.
	/// </remarks>
	[TestFixture]
	public class ServerBandwidthPinsTests
	{
		private const string WriterPath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Bandwidth/ServerBandwidthService.cs";
		private const string ReportPath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Bandwidth/ServerBandwidthReportService.cs";
		private const string ConfigPath = "../FishMMO-Database/FishMMO-DB/Npgsql/EntityConfigurations/Bandwidth/ServerBandwidthEntityConfigurations.cs";
		private const string RecorderPath = "Assets/Scripts/Server/Implementation/ServerBandwidthRecorder.cs";
		private const string LoginPath = "Assets/Scripts/Server/Implementation/LoginServer/LoginServer/LoginServerSystem.cs";
		private const string WorldPath = "Assets/Scripts/Server/Implementation/World/WorldServer/WorldServer/WorldServerSystem.cs";
		private const string ScenePath = "Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.cs";
		private const string PanelRoot = "../FishMMO-WebServers/FishMMO-ControlPanel/ControlPanel/";

		private static readonly string[] MinuteCounters =
		{
			"interval_ms", "app_sent_bytes", "app_recv_bytes", "udp_sent_bytes", "udp_recv_bytes",
			"udp_sent_datagrams", "udp_recv_datagrams", "sessions_active", "sessions_opened",
			"connections_refused", "handshake_failures",
		};

		private static readonly string[] HourColumns =
		{
			"interval_ms", "app_sent_bytes", "app_recv_bytes", "udp_sent_bytes", "udp_recv_bytes",
			"udp_sent_datagrams", "udp_recv_datagrams", "sessions_active_max", "sessions_active_sum",
			"sessions_opened", "connections_refused", "handshake_failures", "minutes_sampled", "instances",
		};

		/// <summary>Source text with line endings normalised.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source with comment lines removed, so prose about a construct does not trip a scan for it.</summary>
		private static string CodeOnly(string source)
		{
			var code = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}

		/// <summary>The body of the method declared by <paramref name="signature"/>, brace-matched.</summary>
		private static string MethodBody(string source, string signature)
		{
			int at = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(at >= 0, $"the source must still declare {signature}");
			int open = source.IndexOf('{', at);
			LogAssert.IsTrue(open > at, $"the body of {signature} must be locatable");
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			LogAssert.Fail($"the body of {signature} never closes");
			return string.Empty;
		}

		[Test]
		public void TheMinuteWrite_IsASetUpsert_OnTheFourColumnKey_WithNoClockInside()
		{
			string body = CodeOnly(MethodBody(ReadSource(WriterPath), "public async Task<DatabaseResult<int>> RecordAsync("));
			LogAssert.IsTrue(body.Contains("ON CONFLICT (server_kind, server_name, bucket_start, instance_id)"),
				"the conflict target is the table's key, instance included");
			foreach (string column in MinuteCounters)
			{
				LogAssert.IsTrue(body.Contains($"{column} = EXCLUDED.{column}"), $"{column} is SET from the incoming row");
				LogAssert.IsFalse(Regex.IsMatch(body, $@"{column}\s*=\s*t\.{column}\s*\+"), $"{column} is never added to: a retried write would count twice");
			}
			LogAssert.IsFalse(body.Contains("+ EXCLUDED."), "no additive upsert anywhere in the write");
			LogAssert.IsTrue(body.Contains("WHERE EXCLUDED.interval_ms >= t.interval_ms"), "a minute's running total never moves backwards");
			foreach (string clock in new[] { "now()", "clock_timestamp", "CURRENT_TIMESTAMP", "DateTime.UtcNow" })
			{
				LogAssert.IsFalse(body.Contains(clock), $"the write reads no clock ({clock}): the minute is keyed before the retried delegate");
			}
		}

		[Test]
		public void TheRollup_SetsEveryHourColumn_UnderATransactionLevelLock()
		{
			string source = CodeOnly(ReadSource(ReportPath));
			string rollup = MethodBody(source, "private static string RollupSql(");
			LogAssert.IsTrue(rollup.Contains("ON CONFLICT (server_kind, server_name, bucket_start)"), "the hour key is the conflict target");
			foreach (string column in HourColumns)
			{
				LogAssert.IsTrue(rollup.Contains($"{column} = EXCLUDED.{column}"), $"{column} is recomputed and SET");
			}
			LogAssert.IsFalse(rollup.Contains("+ EXCLUDED."), "no hour value is ever added to");

			LogAssert.IsTrue(source.Contains("pg_try_advisory_xact_lock"), "the rollup takes a transaction-level lock");
			LogAssert.IsFalse(source.Contains("pg_advisory_lock(") || source.Contains("pg_try_advisory_lock("),
				"never a session-level lock: pooled connections go back without a reset (NoResetOnClose), so it would outlive the pass");
		}

		[Test]
		public void Retention_FoldsAnHourBeforeDeletingIt_AWholeHourAtATime()
		{
			string prune = CodeOnly(MethodBody(ReadSource(ReportPath), "public async Task<DatabaseResult<ServerBandwidthPruneResult>> PruneAsync("));
			int roll = prune.IndexOf("RollupSql(", StringComparison.Ordinal);
			int delete = prune.IndexOf("DELETE FROM {minute}", StringComparison.Ordinal);
			LogAssert.IsTrue(roll >= 0 && delete > roll, "the hour is rolled before its minutes are deleted, in the same transaction");
			LogAssert.IsTrue(prune.Contains("bucket_start >= {{0}} AND bucket_start < {{0}} + interval '1 hour'"),
				"minutes go a whole hour at a time, so no hour is ever left partial");
		}

		[Test]
		public void TheMinuteTable_HasNoForeignKey_AndKeysTheInstance()
		{
			string config = CodeOnly(ReadSource(ConfigPath));
			LogAssert.IsTrue(config.Contains("HasKey(e => new { e.ServerKind, e.ServerName, e.BucketStart, e.InstanceID })"),
				"the minute key the upsert names");
			LogAssert.IsTrue(config.Contains("HasKey(e => new { e.ServerKind, e.ServerName, e.BucketStart })"), "the hour key the rollup names");
			LogAssert.IsFalse(config.Contains("HasOne(") || config.Contains("WithMany(") || config.Contains("HasForeignKey("),
				"no foreign key: the history outlives the server rows");
		}

		[Test]
		public void TheRecorder_ReadsTheDatabaseClockBeforeCapturing_AndOnlyThere()
		{
			string source = CodeOnly(ReadSource(RecorderPath));
			string sample = MethodBody(source, "private async Task SampleAndWriteAsync(");
			int clock = sample.IndexOf("FetchDatabaseUtcNowAsync", StringComparison.Ordinal);
			int capture = sample.IndexOf("TransportTraffic.Capture", StringComparison.Ordinal);
			int write = sample.IndexOf("RecordAsync(", StringComparison.Ordinal);
			LogAssert.IsTrue(clock >= 0 && capture > clock && write > capture,
				"clock, then capture, then write: the minute is fixed before the write's retry loop runs");
			LogAssert.IsFalse(source.Contains("DateTime.UtcNow"), "the host's clock never keys a row");
			LogAssert.AreEqual(1, Regex.Matches(source, @"instanceId\s*=\s*Guid\.NewGuid\(\)").Count, "one instance id, taken once");
			LogAssert.IsTrue(source.Contains("RegisterPeriodicCallback(SampleIntervalSeconds"), "sampled on the periodic system");
			LogAssert.IsTrue(source.Contains("Interlocked.CompareExchange(ref inFlight, 1, 0)"), "single-flight");

			string stop = MethodBody(source, "public void Stop()");
			LogAssert.IsTrue(stop.Contains("ClampToShutdownBudget") && stop.Contains("UnitySyncOverAsync.TryRun"),
				"the final sample is bounded by the shared shutdown budget");
		}

		[TestCase(LoginPath, "Login")]
		[TestCase(WorldPath, "World")]
		[TestCase(ScenePath, "Scene")]
		public void EveryTier_StartsItsRecorder_AndStopsItOnTeardown(string path, string tier)
		{
			string source = CodeOnly(ReadSource(path));
			LogAssert.IsTrue(source.Contains($"ServerBandwidthRecorder.TryStart(Server, ServerType.{tier})"), $"the {tier} tier records under its own kind");
			string deinit = MethodBody(source, "public override void OnDeinitialize()");
			LogAssert.IsTrue(deinit.Contains("bandwidthRecorder?.Stop();"), $"the {tier} tier takes its final sample on teardown");
		}

		[Test]
		public void TheControlPanel_RollsUp_AndThePagePollsWithoutAuditing()
		{
			string program = CodeOnly(ReadSource(PanelRoot + "Program.cs"));
			LogAssert.IsTrue(program.Contains("AddHostedService<ServerBandwidthRollupService>()"), "the rollup runs in the panel");
			LogAssert.IsTrue(program.Contains("AddScoped<IServerBandwidthReportService, ServerBandwidthReportService>()"), "the report service is registered");

			string controller = CodeOnly(ReadSource(PanelRoot + "Controllers/ServerBandwidthController.cs"));
			LogAssert.IsTrue(controller.Contains("[Authorize(Policy = PanelPolicies.Operator)]"), "at Operator, beside the server board");
			LogAssert.IsFalse(Regex.IsMatch(controller, @"\[Http(Post|Put|Patch|Delete)"), "a read-only surface");

			string view = ReadSource(PanelRoot + "wwwroot/js/views/servers-bandwidth.js");
			LogAssert.IsTrue(view.Contains("setInterval(() => refresh(api.auto), REFRESH_MS)"), "the timer's reads are marked automatic");
		}
	}
}
